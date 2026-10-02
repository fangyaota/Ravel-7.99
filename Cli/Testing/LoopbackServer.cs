namespace Ravel.Testing;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>给 golden 用例用的**回环 HTTP 服务器** —— 网络那几条用例不碰真网络:
/// 真网络会飘(断网、限流、返回变了),那对一份要精确比对的用例是致命的。
///
/// 自己拿 `TcpListener` 说 HTTP/1.1,不用 `HttpListener`:后者在 Windows 上走 http.sys、
/// 要管理员配 URL 前缀,而且响应字节不完全可控。自己写就能造出**想要的每一颗字节** ——
/// GBK 的网页、故意慢的响应、500、302、几百 KB 的正文,都在这儿。
///
/// 端口让它自己挑(`TcpListener` 绑 0),基址通过环境变量 `RAVEL_TEST_HTTP` 交给用例。
/// **用例里不许打印 URL** —— 端口每次都变,打印出来就钉不住了。</summary>
internal sealed class LoopbackServer : IDisposable
{
    /// <summary>用例读的那个环境变量的名字(引擎那边就是 `System.Env`)</summary>
    public const string EnvName = "RAVEL_TEST_HTTP";

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private int _flaky;          // `/flaky` 被敲过几次(前两次故意 500)

    /// <summary>基址,形如 `http://127.0.0.1:51234`(没有结尾的 `/`)</summary>
    public string BaseUrl { get; }

    public LoopbackServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        Environment.SetEnvironmentVariable(EnvName, BaseUrl, EnvironmentVariableTarget.Process);
        new Thread(Accept) { IsBackground = true }.Start();
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();                        // AcceptTcpClient 会抛 SocketException,线程自己收摊
        Environment.SetEnvironmentVariable(EnvName, null, EnvironmentVariableTarget.Process);
    }

    private void Accept()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException)
            {
                return;                           // 停了
            }

            // 一个连接一条线程:用例里就那几下,不必搞连接池
            new Thread(() => Serve(client)) { IsBackground = true }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        using var c = client;
        try
        {
            using var s = c.GetStream();
            Handle(s);
        }
        catch (Exception)
        {
            // 客户端提前跑掉(超时那条用例就是)**是正常的** —— 这条连接扔了就行
        }
    }

    private void Handle(Stream s)
    {
        var (method, path, body, headers) = ReadRequest(s);

        switch (path)
        {
            case "/hello":
                Text(s, 200, "OK", "hello 你好", "text/plain; charset=utf-8");
                break;
            case "/json":
                Text(s, 200, "OK", """{"a":1,"b":[1,2],"s":"x"}""", "application/json");
                break;
            case "/gbk":
                // 老编码那条:字节是 GBK 的,头里明说了 charset —— `Text ()` 该按它解
                Bytes(s, 200, "OK", Encoding.GetEncoding("gbk").GetBytes("中文网页"),
                      "text/html; charset=gbk");
                break;
            case "/redirect":
                s.Write(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 302 Found\r\nLocation: /hello\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                break;
            case "/notfound":
                Text(s, 404, "Not Found", "nope", "text/plain; charset=utf-8");
                break;
            case "/slow":
                Thread.Sleep(1500);               // 比用例里给的 timeout 长
                Text(s, 200, "OK", "总算回来了", "text/plain; charset=utf-8");
                break;
            case "/big":
                // 几百 KB:下载那条要它,`max` 封顶那条也要它
                Bytes(s, 200, "OK", [.. Enumerable.Repeat((byte)'x', 300_000)], "application/octet-stream");
                break;
            case "/flaky":
                // 头两次 500、第三次才成 —— 重试那条用例看的就是这个
                if (Interlocked.Increment(ref _flaky) <= 2) Text(s, 500, "Internal Server Error", "又坏了", "text/plain");
                else Text(s, 200, "OK", "好了", "text/plain; charset=utf-8");
                break;
            case "/echo":
                // 把收到的东西原样吐回来(方法 / 正文 / 自定义头),用来验"发出去的是什么"
                var echo = $"{method}|{Encoding.UTF8.GetString(body)}|{headers.GetValueOrDefault("x-test", "-")}";
                Text(s, 200, "OK", echo, "text/plain; charset=utf-8");
                break;
            case "/upload":
                // POST 上来的是 multipart:把里面的文件名和总字节数报回去(不解析内容)
                var raw = Encoding.UTF8.GetString(body);
                Text(s, 200, "OK", $"name={FileOf(raw)} bytes={body.Length}", "text/plain; charset=utf-8");
                break;
            default:
                Text(s, 404, "Not Found", $"没有这条路由：{path}", "text/plain; charset=utf-8");
                break;
        }
    }

    /// <summary>从 multipart 正文里抠出文件名(够用就行,不上真正的解析器)。
    /// 引号可有可无 —— .NET 那台写出来是 `filename=up.txt`(不带引号),
    /// 别的客户端可能写 `filename="up.txt"`;`filename*=`(带 `*` 那个)不会被误抓。</summary>
    private static string FileOf(string raw)
    {
        const string key = "filename=";
        var i = raw.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return "-";
        var start = i + key.Length;
        var end = raw.IndexOfAny([';', '\r', '\n'], start);
        return (end < 0 ? raw[start..] : raw[start..end]).Trim().Trim('"');
    }

    /// <summary>读一个请求:请求行、头、正文(按 Content-Length)。自己按字节读 ——
    /// 混用 StreamReader 会在它自己那层缓冲里多吞掉正文的头几个字节。</summary>
    private static (string Method, string Path, byte[] Body, Dictionary<string, string> Headers) ReadRequest(Stream s)
    {
        var head = new MemoryStream();
        var one = new byte[1];
        while (head.Length < 65536)
        {
            if (s.Read(one, 0, 1) == 0) break;
            head.WriteByte(one[0]);
            var n = (int)head.Length;
            if (n >= 4 && head.GetBuffer()[n - 4] == '\r' && head.GetBuffer()[n - 3] == '\n'
                       && head.GetBuffer()[n - 2] == '\r' && head.GetBuffer()[n - 1] == '\n')
                break;
        }

        var lines = Encoding.ASCII.GetString(head.ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ');
        var method = parts.Length > 0 ? parts[0] : "GET";
        var path = parts.Length > 1 ? parts[1] : "/";
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];             // 查询串不参与路由

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var c = line.IndexOf(':');
            if (c > 0) headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }

        var len = headers.TryGetValue("Content-Length", out var l) && int.TryParse(l, out var size) ? size : 0;
        var body = new byte[len];
        for (var read = 0; read < len;)
        {
            var got = s.Read(body, read, len - read);
            if (got <= 0) break;
            read += got;
        }

        return (method, path, body, headers);
    }

    private static void Text(Stream s, int status, string reason, string text, string type)
        => Bytes(s, status, reason, Encoding.UTF8.GetBytes(text), type);

    private static void Bytes(Stream s, int status, string reason, byte[] body, string type)
    {
        var head = $"HTTP/1.1 {status} {reason}\r\n"
                 + $"Content-Type: {type}\r\n"
                 + $"Content-Length: {body.Length}\r\n"
                 + "Connection: close\r\n\r\n";
        s.Write(Encoding.ASCII.GetBytes(head));
        s.Write(body);
        s.Flush();
    }
}
