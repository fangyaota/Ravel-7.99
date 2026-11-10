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

[RavelModule("Native")]
internal static class ServerNative
{
    private sealed class Listener
    {
        public TcpListener Tcp = null!;
        public X509Certificate2? Cert;
        public bool Closed;
        public int MaxBody = DefaultMaxBody;
    }

    private sealed class Conn
    {
        public TcpClient Client = null!;
        public Stream Stream = null!;
        public bool Closed;
        public int MaxBody;                    // 从它那台监听器上抄下来的一次请求正文上限
        public int PendingBody;                // 因为超限**没去读**的那些字节(见 Parse / Answer)
    }

    private static readonly Dictionary<int, Listener> Listeners = [];
    private static readonly Dictionary<int, Conn> Conns = [];
    private static int _next;                 // 两种号共用一个计数器:号落在哪张表上一查便知

    private static DictVal Gone => new([]);

    // ── 起一台 ──

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
            MaxBody = OptInt(cfg, "max", 0) is var m and > 0 ? m : DefaultMaxBody,
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
            Conns[connId] = new Conn { Client = client, Stream = stream, MaxBody = l.MaxBody };
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

        // **超过上限就一个字都不读**:读了再丢等于"对面说多大我们就读多大",那个顶就白设了。
        // 交回一个带 `oversize` 的请求(正文空的),**连接明确标成不再复用** —— 库里那头据此
        // 回 413 然后断。之所以不是在这儿直接抛:抛了连接任务是没了,对面看到的只是断线,
        // 连句人话都收不到。
        if (want > c.MaxBody)
        {
            // 记下"有多少正文还在路上" —— 回完 413 要**把它收干净再关**(见 `Answer` 里的
            // 那一小段)。不收的话关这个连接会发 RST,对面看不到刚写出去的 413。
            c.PendingBody = want;
            return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("method")] = new StringVal(method),
                [new StringVal("path")] = new StringVal(Uri.UnescapeDataString(path)),
                [new StringVal("query")] = new StringVal(query),
                [new StringVal("version")] = new StringVal(version),
                [new StringVal("headers")] = new DictVal(headDict),
                [new StringVal("body")] = BytesList([]),
                [new StringVal("keepAlive")] = new BoolVal(false),
                [new StringVal("oversize")] = new BoolVal(true),
                // 文案在这儿拼成一整句 —— 和 `NetNative.Why` 一个道理:**报错只有一份**
                [new StringVal("why")] = new StringVal(
                    $"请求正文太大（超过 {NetNative.Human(c.MaxBody)}，它说 {NetNative.Human(want)}）"),
            });
        }

        // 正文正常读完了(或者本来就没有),把"还欠着"那笔账清掉
        c.PendingBody = 0;
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

        if (close)
        {
            // **关之前先把对面还在路上的正文收干净。** 关一个"收下过东西、却没读它"的连接,
            // 内核发的是 **RST** 而不是 FIN —— 对面于是看不到我们刚写出去的那句 413,
            // 只看到"连接被重置"(HttpClient 那边直接抛异常)。实测约每 12 次撞 1 次。
            //
            // **只收这一小段**:真去把 2 GB 收完,那个"上限"就白设了(见 `Parse` 里那段)。
            // 所以是尽力而为 —— 常见那点超限(几十 KB)够用;恶意的大肚子照样吃 RST,
            // 那正是我们想要的。
            if (c.PendingBody > 0) await Drain(c.Stream, c.PendingBody);
            Close(id);
        }
        return Narrow(head.Length + (long)body.Length);
    }

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

    [RavelFn("HttpClose")]
    public static RuntimeValue HttpClose(RuntimeValue handle)
    {
        var id = Int(handle, "HttpClose 的句柄");
        Close(id);
        return Void;
    }

    private static async Task Drain(Stream s, int cap)
    {
        var buf = new byte[8192];
        var left = Math.Min(cap, DrainCap);
        using var cts = new CancellationTokenSource(DrainTimeout);
        try
        {
            while (left > 0)
            {
                var n = await s.ReadAsync(buf.AsMemory(0, Math.Min(buf.Length, left)), cts.Token);
                if (n <= 0) break;
                left -= n;
            }
        }
        catch (Exception)
        {
            // 超时 / 对面断了 / 读不动了 —— 都无所谓,这一步本来就是尽力而为
        }
    }

    private const int DrainCap = 1024 * 1024;

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromMilliseconds(250);

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

    private static string Show2Host(string host) => host is "0.0.0.0" or "*" ? "127.0.0.1" : host;

    private const int MaxHead = 64 * 1024;

    private const int DefaultMaxBody = 16 * 1024 * 1024;
}
