using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>网络 —— **服务端**那一半(Http 客户端在 `NetNative.cs`)。对外是 `Native.HttpListen`
/// 那几条,`lib/httpd.rav` 是它的策略半边。
///
/// **自己拿 `TcpListener` 说 HTTP/1.1,不用 `HttpListener`** —— 和用例里那台回环服务器
/// (`Cli/Testing/LoopbackServer.cs`)同一个理由,那儿写着:后者在 Windows 上走 http.sys、
/// 要管理员配 URL 前缀,而且响应字节不完全可控。自己写就没有权限问题,发出的每一颗字节
/// 也都是我们说了算 —— 那对一份要被 golden 用例钉住的实现是必须的。
///
/// **句柄不进 Ravel 堆**:监听器和连接都是要 Dispose 的本机东西,而 Ravel 值没有析构那一套。
/// 照 `SqliteNative` 的规矩办:这一格存着(两张 `号 → 东西` 的表),交给 Ravel 的是个 **int 号**,
/// `HttpClose` 时把号还回来。所以**不用加新的值类型**。
///
/// 每条原语**收一个 dict、交回一个 dict**(和 `HttpReq` 一个形状):加字段不用改签名。
/// 唯独 `HttpRead` 收两个参数 —— 第二个是 keep-alive 的空闲超时,它和"读哪个连接"一样是
/// 每次调用都要现给的,塞进 dict 反而绕(对照 `DecodeText` 也是两个参数)。
///
/// **会等的那几条交回 `Waitable`**(`HttpAccept` / `HttpRead` / `HttpAnswer`):
/// `WaitableVal` 包的就是个 .NET `Task`,Ravel 那边 `group.Await` 它 —— 服务端因此是
/// **一个任务**,慢 handler 挡不住别的连接(见 CONTEXT「并发」那节)。</summary>
[RavelModule("Native")]
internal static class ServerNative
{
    /// <summary>一台在听的服务器。`Closed` 一置上,正挂在 `AcceptTcpClientAsync` 上那一下
    /// 会因为 `Tcp.Stop()` 抛出来,那条路把 `()` 交回去 —— **正常收摊,不是报错**。</summary>
    private sealed class Listener
    {
        public TcpListener Tcp = null!;
        public X509Certificate2? Cert;
        public bool Closed;
    }

    /// <summary>一条连上的连接。`Stream` 可能就是 `Client.GetStream ()`(纯 HTTP),
    /// 也可能是包了一层 `SslStream`(HTTPS)—— 上面那些读写只认 `Stream`,不关心是哪一种。</summary>
    private sealed class Conn
    {
        public TcpClient Client = null!;
        public Stream Stream = null!;
        public bool Closed;
    }

    private static readonly Dictionary<int, Listener> Listeners = [];
    private static readonly Dictionary<int, Conn> Conns = [];
    private static int _next;                 // 两种号共用一个计数器:号落在哪张表上一查便知

    /// <summary>「没有下一步了」交回的那一枚 —— **空 dict,不是 `()`**。
    ///
    /// Ravel 那边要判它,而 `Void` 跟非 `Void` 是**不能比**的(`() == ()` 倒可以)——
    /// 判一句"交回来的是不是空"得绕成 `(typeof a) == System.Void`。空 dict 直接 `IsEmpty ()`,
    /// 而且**真请求 / 真连接永远不会是空的**(至少带着 `method` / `conn`),认不错。
    ///
    /// 用 `()` 还有一层更坏的:库里就只能靠一个"我喊过停"的布尔标志去收摊,而那个标志
    /// **反映不了"监听器因为别的原因没了"** —— 那种情况下 accept 会一圈圈空转。</summary>
    private static DictVal Gone => new([]);

    // ── 起一台 ──

    /// <summary>`Native.HttpListen {port host backlog cert key password}` →
    /// `{handle port url}`。**`port` 给 0 就是"随便挑一个"**,交回来的 `port` 是**真正绑上的那个**
    /// —— 用例靠它拿端口(而且绝不许打印出来,每次都变,打印了期望就钉不住)。
    ///
    /// `host` 默认 `127.0.0.1`:**默认只听本机**。要对外就显式写 `0.0.0.0`,
    /// 这种"要不要暴露到网上"的事不该由一个默认值替你决定。
    ///
    /// 给了 `cert` 就是 HTTPS:`key` 也给了按 **PEM**(证书 + 私钥两个文件)读,
    /// 否则按 **PFX**(`password` 是它的口令)。</summary>
    [RavelFn("HttpListen")]
    public static RuntimeValue HttpListen(RuntimeValue a) => Guarded("监听", () =>
    {
        var cfg = Dict(a, "HttpListen 的配置");
        var port = OptInt(cfg, "port", 8080);
        var host = OptText(cfg, "host", "127.0.0.1");
        var backlog = OptInt(cfg, "backlog", 128);
        if (port is < 0 or > 65535) throw Fail($"端口要在 0..65535 之间，得到 {port}", ErrorKind.Value);

        var cert = OptText(cfg, "cert", "");
        var key = OptText(cfg, "key", "");
        var password = OptText(cfg, "password", "");

        var listener = new Listener
        {
            Tcp = new TcpListener(Address(host), port),
            Cert = cert.Length == 0 ? null : LoadCert(cert, key, password),
        };
        listener.Tcp.Start(backlog);
        var id = ++_next;
        Listeners[id] = listener;
        var bound = ((IPEndPoint)listener.Tcp.LocalEndpoint).Port;
        var scheme = listener.Cert is null ? "http" : "https";
        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("handle")] = IntVal.Of(id),
            [new StringVal("port")] = IntVal.Of(bound),
            // 绑 0 的时候只有绑完才知道是哪个端口,所以 url 在这儿拼 —— 不能由 Ravel 那边按
            // "我给的那个 port" 拼(它给的是 0)
            [new StringVal("url")] = new StringVal($"{scheme}://{Show2Host(host)}:{bound}"),
        });
    });

    /// <summary>`Native.HttpAccept handle` → 交回句柄。**一次只收一条**(收过来的交回句柄里
    /// 带着连接号),不是"一次收一批":要几条并着收,就多挂几个 accept 在调度器里 ——
    /// 那是策略,归 `lib/httpd.rav`。
    ///
    /// **监听器关了交回空 dict,不报错** —— 那是 `Stop ()` 的收摊路,拿报错表示"我主动关了"
    /// 会让每一处 accept 都得裹一层 `try`。库里判一句 `a.IsEmpty ()` 就能收摊,
    /// **不管监听器是被谁、为什么关掉的**。
    ///
    /// HTTPS 的**握手就在这儿做完**(`AuthenticateAsServerAsync`):交回来的连接拿起来就能读,
    /// 用的人不用知道底下还有一层 TLS。**握手失败只丢这一条连接**,循环接着等下一条 ——
    /// 扫描器、写错协议的客户端都会撞到这条路,为它们把整台服务器带走是不对的。</summary>
    [RavelFn("HttpAccept")]
    public static RuntimeValue HttpAccept(RuntimeValue handle)
        => new WaitableVal(Accept(Int(handle, "HttpAccept 的句柄")));

    private static async Task<RuntimeValue> Accept(int id)
    {
        while (true)
        {
            if (!Listeners.TryGetValue(id, out var l) || l.Closed) return Gone;

            TcpClient client;
            try
            {
                client = await l.Tcp.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return Gone;                       // Stop() 了,或者监听器没了
            }

            Stream stream = client.GetStream();
            var tls = false;
            if (l.Cert is { } c)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                try
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = c,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    });
                }
                catch (Exception)
                {
                    ssl.Dispose();                 // 这一条扔了,接着等下一条
                    client.Dispose();
                    continue;
                }

                stream = ssl;
                tls = true;
            }

            var connId = ++_next;
            Conns[connId] = new Conn { Client = client, Stream = stream };
            var peer = client.Client.RemoteEndPoint?.ToString() ?? "";
            return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("conn")] = IntVal.Of(connId),
                [new StringVal("peer")] = new StringVal(peer),
                [new StringVal("tls")] = new BoolVal(tls),
            });
        }
    }

    // ── 收一个请求 ──

    /// <summary>`Native.HttpRead conn idle` → 交回句柄,结果是
    /// `{method path query version headers body keepAlive}`;对面关了(或者空转超时)交回**空 dict**。
    ///
    /// **`idle` 是 keep-alive 的空闲超时**(毫秒,0 = 不超时)。少了它就是一条不说话的连接
    /// 永远占着一个任务 —— 而任务是有数的。第一条请求一般给 0(客户端正在发),
    /// 后面几条才给超时。
    ///
    /// **正文只认 `Content-Length`**;碰上 `Transfer-Encoding: chunked` 明确报错,**不猜** ——
    /// 猜错就是悄悄地把正文读歪,那比报错难查一百倍。
    ///
    /// 头名字**一律小写**(和 `NetNative.HeaderDict` 一个口径:HTTP 头不区分大小写,
    /// 给两套写法只会让查的人猜)。同名的多个值用 `, ` 连起来。</summary>
    [RavelFn("HttpRead")]
    public static RuntimeValue HttpRead(RuntimeValue conn, RuntimeValue idle)
        => new WaitableVal(Read(Int(conn, "HttpRead 的句柄"), Int(idle, "HttpRead 的空闲超时")));

    private static async Task<RuntimeValue> Read(int id, int idleMs)
    {
        if (!Conns.TryGetValue(id, out var c) || c.Closed) return Gone;

        try
        {
            using var cts = new CancellationTokenSource(
                idleMs > 0 ? TimeSpan.FromMilliseconds(idleMs) : Timeout.InfiniteTimeSpan);

            var head = await ReadHead(c.Stream, cts.Token);
            if (head is null) return Gone;         // 对面关了 / 空转超时

            var (headText, extra) = head.Value;
            return await Parse(c, headText, extra, cts.Token);
        }
        catch (Exception)
        {
            // 对面把连接掐了、或者读到一半断了 —— **这是网络上的家常便饭,不是这台服务器的错**。
            // 当"这一条结束了"交回空 dict,让库里那条 while 正常收摊(和 LoopbackServer.Serve 一个态度)。
            return Gone;
        }
    }

    /// <summary>读到 `\r\n\r\n` 为止。交回**头的文本**和**已经读进来的正文那几个字节**
    /// —— 一次读一大块的时候,正文的头几个字节常常跟着头一起到了,丢掉它们正文就少一截。</summary>
    private static async Task<(string Head, byte[] Extra)?> ReadHead(Stream s, CancellationToken ct)
    {
        var buf = new byte[8192];
        var acc = new MemoryStream();
        while (acc.Length < MaxHead)
        {
            var n = await s.ReadAsync(buf, ct);
            if (n == 0) return null;               // 对面关了
            acc.Write(buf, 0, n);
            var at = EndOfHead(acc.GetBuffer(), (int)acc.Length);
            if (at >= 0)
            {
                var all = acc.GetBuffer();
                var head = Encoding.ASCII.GetString(all, 0, at);
                var extra = new byte[acc.Length - at];
                Array.Copy(all, at, extra, 0, extra.Length);
                return (head, extra);
            }
        }

        throw Fail($"请求头太大（超过 {MaxHead} 字节）", ErrorKind.Io);
    }

    /// <summary>找 `\r\n\r\n` 之后那个位置(也就是正文的起点);没有就 -1</summary>
    private static int EndOfHead(byte[] b, int len)
    {
        for (var i = 3; i < len; i++)
            if (b[i - 3] == '\r' && b[i - 2] == '\n' && b[i - 1] == '\r' && b[i] == '\n') return i + 1;
        return -1;
    }

    private static async Task<RuntimeValue> Parse(Conn c, string head, byte[] extra, CancellationToken ct)
    {
        var lines = head.Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length < 2) throw Fail($"请求行读不动：{lines[0]}", ErrorKind.Io);

        var method = parts[0].ToUpperInvariant();
        var target = parts[1];
        var version = parts.Length > 2 ? parts[2] : "HTTP/1.1";

        var q = target.IndexOf('?');
        var path = q < 0 ? target : target[..q];
        var query = q < 0 ? "" : target[(q + 1)..];

        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Trim().ToLowerInvariant();
            if (!headers.TryGetValue(name, out var list)) headers[name] = list = [];
            list.Add(line[(colon + 1)..].Trim());
        }

        // 头一律小写、重复的用 ", " 连起来 —— 和 NetNative.HeaderDict 一个口径
        var headDict = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var (k, v) in headers) headDict[new StringVal(k)] = new StringVal(string.Join(", ", v));

        if (headers.ContainsKey("transfer-encoding"))
            throw Fail("请求正文用了 Transfer-Encoding（chunked）—— 这儿只认 Content-Length，不猜",
                       ErrorKind.Io);

        var want = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl[0], out var size)
            ? size : 0;

        var body = new byte[want];
        var got = Math.Min(want, extra.Length);
        Array.Copy(extra, body, got);
        while (got < want)
        {
            var n = await c.Stream.ReadAsync(body.AsMemory(got, want - got), ct);
            if (n == 0) break;
            got += n;
        }

        // 默认:HTTP/1.1 一直连着,HTTP/1.0 收完就断。`Connection` 头能盖掉它。
        var keep = version.Contains("1.1");
        if (headers.TryGetValue("connection", out var connHdr))
        {
            var v = string.Join(",", connHdr).ToLowerInvariant();
            if (v.Contains("close")) keep = false;
            else if (v.Contains("keep-alive")) keep = true;
        }

        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("method")] = new StringVal(method),
            [new StringVal("path")] = new StringVal(Uri.UnescapeDataString(path)),
            [new StringVal("query")] = new StringVal(query),
            [new StringVal("version")] = new StringVal(version),
            [new StringVal("headers")] = new DictVal(headDict),
            [new StringVal("body")] = BytesList(body),
            [new StringVal("keepAlive")] = new BoolVal(keep),
        });
    }

    // ── 回一个响应 ──

    /// <summary>`Native.HttpAnswer {conn status headers body close}` → 交回句柄,结果是
    /// **写出去多少字节**。
    ///
    /// `close` 决定回完断不断(默认跟着请求那边来 —— `lib/httpd.rav` 负责算这件事)。
    /// 头里已经有 `content-length` 时用它、没有才按 `body` 长度补 —— 让库那边有机会
    /// 自己说了算(比如 `304` 那种不该有正文的)。</summary>
    [RavelFn("HttpAnswer")]
    public static RuntimeValue HttpAnswer(RuntimeValue a)
        => new WaitableVal(Answer(Dict(a, "HttpAnswer 的响应")));

    private static async Task<RuntimeValue> Answer(DictVal resp)
    {
        if (Opt(resp, "conn") is not { } connArg)
            throw Fail("HttpAnswer 要一个 conn 号", ErrorKind.Argument);
        var id = Int(connArg, "HttpAnswer 的 conn");
        if (!Conns.TryGetValue(id, out var c) || c.Closed)
            throw Fail("这条连接已经没了", ErrorKind.Io);

        var status = OptInt(resp, "status", 200);
        var body = Opt(resp, "body") is { } b ? Bytes(b, "HttpAnswer 的正文") : [];
        var close = OptBool(resp, "close", true);

        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");

        var hasLength = false;
        var hasType = false;
        if (Opt(resp, "headers") is DictVal hs)
        {
            foreach (var (k, v) in hs.Entries)
            {
                var name = k as StringVal ?? throw Fail($"响应头的名字要是字符串，得到 {k.Type}", ErrorKind.Type);
                var low = name.Value.ToLowerInvariant();
                if (low is "content-length") hasLength = true;
                if (low is "content-type") hasType = true;
                if (low is "connection") close = Show(v).Contains("close", StringComparison.OrdinalIgnoreCase);
                if (low is "date" or "server") continue;   // 会变的头不该由我们发(用例钉不住)
                sb.Append(name.Value).Append(": ").Append(Show(v)).Append("\r\n");
            }
        }

        // 没有正文的状态码不该报长度 —— 204 / 304 带 Content-Length 是协议错,有些客户端会掐连接
        var bodyless = status is 204 or 304 || status < 200;
        if (!hasLength && !bodyless) sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
        if (!hasType && !bodyless) sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("Connection: ").Append(close ? "close" : "keep-alive").Append("\r\n\r\n");

        var head = Encoding.ASCII.GetBytes(sb.ToString());
        try
        {
            await c.Stream.WriteAsync(head);
            if (!bodyless && body.Length > 0) await c.Stream.WriteAsync(body);
            await c.Stream.FlushAsync();
        }
        catch (Exception)
        {
            return Void;                           // 对面走了,写不动就算了(和读那头一个态度)
        }

        if (close) Close(id);
        return Narrow(head.Length + (long)body.Length, "写出去的字节数");
    }

    /// <summary>状态码 → 原因短语。**不查 .NET 那张表**:它的文本跟着版本走,而响应的头
    /// 是要被用例钉住的(`HttpStatusCode` 的枚举名也不是能直接发出去的东西)。
    /// 库里没写的统一给 `OK`,库那边可以用 `reason` 字段自己盖。</summary>
    private static string Reason(int status) => status switch
    {
        200 => "OK", 201 => "Created", 202 => "Accepted", 204 => "No Content",
        206 => "Partial Content", 301 => "Moved Permanently", 302 => "Found",
        303 => "See Other", 304 => "Not Modified", 307 => "Temporary Redirect",
        308 => "Permanent Redirect", 400 => "Bad Request", 401 => "Unauthorized",
        403 => "Forbidden", 404 => "Not Found", 405 => "Method Not Allowed",
        408 => "Request Timeout", 409 => "Conflict", 411 => "Length Required",
        413 => "Payload Too Large", 414 => "URI Too Long", 415 => "Unsupported Media Type",
        418 => "I'm a teapot", 429 => "Too Many Requests",
        500 => "Internal Server Error", 501 => "Not Implemented", 502 => "Bad Gateway",
        503 => "Service Unavailable", 504 => "Gateway Timeout",
        _ => "OK",
    };

    // ── 收摊 ──

    /// <summary>`Native.HttpClose 号` —— 关一条连接或者一台监听器,**幂等**(已经关了当"关过了",
    /// 和 `SqliteClose` 一个规矩)。号是哪一种按两张表查,调用方不用分。</summary>
    [RavelFn("HttpClose")]
    public static RuntimeValue HttpClose(RuntimeValue handle)
    {
        var id = Int(handle, "HttpClose 的句柄");
        Close(id);
        return Void;
    }

    private static void Close(int id)
    {
        if (Conns.Remove(id, out var c)) Dispose(c);
        else if (Listeners.Remove(id, out var l)) DisposeListen(l);
    }

    private static void Dispose(Conn c)
    {
        c.Closed = true;
        try { c.Stream.Dispose(); } catch (Exception) { /* 已经断了的连接,关它再抛一次没意义 */ }
        try { c.Client.Dispose(); } catch (Exception) { /* 同上 */ }
    }

    private static void DisposeListen(Listener l)
    {
        l.Closed = true;
        try { l.Tcp.Stop(); } catch (Exception) { /* 同上 */ }
        l.Cert?.Dispose();
    }

    // ── 自签证书(只为本地起 HTTPS) ──

    /// <summary>`Native.HttpMakeCert {path password names days}` —— 自己签一张证书落成 **PFX**。
    ///
    /// 它存在只为一件事:**本地起个 HTTPS 试一把**。真上线用的证书该由 CA 签、由运维摆进来,
    /// 不该由应用自己现造 —— 这个函数不是那个用途。
    ///
    /// `names` 是证书认的名字(域名或 IP),不写就按 `localhost` + `127.0.0.1`。
    /// 自签的证书**没有任何一家客户端会信**,所以要连它得让客户端别校验证书
    /// (`HttpReq` 的 `insecure` 那个开关,同样只为本地)。</summary>
    [RavelFn("HttpMakeCert")]
    public static RuntimeValue HttpMakeCert(RuntimeValue a) => Guarded("签证书", () =>
    {
        var cfg = Dict(a, "HttpMakeCert 的参数");
        var path = PathOf(Opt(cfg, "path") ?? throw Fail("HttpMakeCert 要 path", ErrorKind.Argument),
                          "HttpMakeCert 的 path");
        NeedParentDir(path, "证书");
        var password = OptText(cfg, "password", "");
        var days = OptInt(cfg, "days", 365);
        var names = Opt(cfg, "names") is ListVal ls && ls.Elements.Count > 0
            ? ls.Elements.Select(x => Show(x)).ToArray()
            : ["localhost", "127.0.0.1"];

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=" + names[0], rsa, HashAlgorithmName.SHA256,
                                         RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var n in names)
        {
            if (IPAddress.TryParse(n, out var ip)) san.AddIpAddress(ip);
            else san.AddDnsName(n);
        }

        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(days));
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return new StringVal(path);
    });

    // ── 零碎 ──

    /// <summary>`cert` 那两条路:**两个文件是 PEM、一个文件是 PFX**。PEM 那条要多走一步
    /// (`CreateFromPemFile` 交回的证书,私钥在 Windows 上不能直接喂给 `SslStream` ——
    /// 得先导出成 PFX 再读回来,才是一份"带着能用私钥"的证书)。</summary>
    private static X509Certificate2 LoadCert(string cert, string key, string password)
    {
        if (!File.Exists(cert)) throw Fail($"找不到证书文件：{cert}", ErrorKind.Io);
        if (key.Length > 0)
        {
            if (!File.Exists(key)) throw Fail($"找不到私钥文件：{key}", ErrorKind.Io);
            using var pem = X509Certificate2.CreateFromPemFile(cert, key);
            return X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null);
        }

        return X509CertificateLoader.LoadPkcs12FromFile(cert, password);
    }

    private static IPAddress Address(string host) => host switch
    {
        "" or "localhost" or "127.0.0.1" => IPAddress.Loopback,
        "0.0.0.0" or "*" => IPAddress.Any,
        "::1" => IPAddress.IPv6Loopback,
        _ => IPAddress.TryParse(host, out var ip)
            ? ip
            : throw Fail($"host 得是个 IP 地址（或者 localhost / 0.0.0.0），得到：{host}", ErrorKind.Value),
    };

    /// <summary>拼进 `url` 的 host:`0.0.0.0` 对外其实不是一个能连的地址,给 `127.0.0.1`
    /// 才是"这台机器自己"的意思</summary>
    private static string Show2Host(string host) => host is "0.0.0.0" or "*" ? "127.0.0.1" : host;

    private const int MaxHead = 64 * 1024;
}
