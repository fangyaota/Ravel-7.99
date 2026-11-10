using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class NetNative
{
    // ── 每条两副面孔 ──
    //
    // **同步那条**给"我就是要等它回来"的脚本用(顶层、`tests/268` 那种)。
    // **`…Task` 那条交回一个 `Waitable` 句柄** —— 给任务用:`group.Await` 它,别的任务接着跑。
    //
    // 两条压的是**同一个 `Send`**(底下全是 await),所以超时、封顶、报错文案只有一份。
    // 别拿 .NET 那个同步 `client.Send` 去实现同步那条 —— 那就成了两套码,迟早就分叉。

    [RavelFn("HttpReq")]
    public static RuntimeValue HttpReq(RuntimeValue a)
        => Http("发请求", () => Send(Dict(a, "HttpReq 的请求"), null).GetAwaiter().GetResult());

    [RavelFn("HttpReqTask")]
    public static RuntimeValue HttpReqTask(RuntimeValue a)
        => new WaitableVal(HttpAsync("发请求", () => Send(Dict(a, "HttpReq 的请求"), null)));

    [RavelFn("HttpDownload")]
    public static RuntimeValue HttpDownload(RuntimeValue a)
        => Http("下载", () => Download(a).GetAwaiter().GetResult());

    [RavelFn("HttpDownloadTask")]
    public static RuntimeValue HttpDownloadTask(RuntimeValue a)
        => new WaitableVal(HttpAsync("下载", () => Download(a)));

    [RavelFn("HttpUpload")]
    public static RuntimeValue HttpUpload(RuntimeValue a)
        => Http("上传", () => Upload(a).GetAwaiter().GetResult());

    [RavelFn("HttpUploadTask")]
    public static RuntimeValue HttpUploadTask(RuntimeValue a)
        => new WaitableVal(HttpAsync("上传", () => Upload(a)));

    private static async Task<RuntimeValue> Download(RuntimeValue a)
    {
        var req = Dict(a, "HttpDownload 的请求");
        var path = OptText(req, "path", "");
        NeedParentDir(path, "下载");
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write,
                                              FileShare.Read, bufferSize: 81920, useAsync: true);
        return await Send(req, file);
    }

    private static Task<RuntimeValue> Upload(RuntimeValue a)
    {
        var req = Dict(a, "HttpUpload 的请求");
        var path = OptText(req, "path", "");
        NeedFile(path, "上传的文件");
        return Send(req, null, path, OptText(req, "field", "file"));
    }

    [RavelFn("DecodeText")]
    public static RuntimeValue DecodeText(RuntimeValue a, RuntimeValue b) => Guarded("解码文本", () =>
        new StringVal(Decode(Bytes(a, "DecodeText 的字节"), Text(b, "DecodeText 的字符集").Value)));

    public static readonly HttpClient Web = MakeWeb(true, false);
    public static readonly HttpClient WebNoRedirect = MakeWeb(false, false);
    public static readonly HttpClient WebLax = MakeWeb(true, true);
    public static readonly HttpClient WebLaxNoRedirect = MakeWeb(false, true);

    public static HttpClient MakeWeb(bool follow, bool insecure)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = follow,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,   // gzip / deflate / br 全让 .NET 顺手解开
        };

        // **就地改那份现成的** `SslOptions`,别整个换一个 —— 换掉会把 .NET 填好的那些默认
        // (协议版本之类)一起清掉,而症状是"某些服务器突然握手不上",很难查。
        if (insecure) handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static async Task<RuntimeValue> Send(DictVal req, Stream? bodyOut, string? uploadPath = null, string field = "file")
    {
        var url = OptText(req, "url", "");
        if (url.Length == 0) throw Fail("请求 dict 里没有 url", ErrorKind.Argument);
        // 网址**先自己验一遍**:`HttpRequestMessage` 对"这是个相对 URI"是构造得出来的,
        // 那要等到发的时候才报,而那时的话是 .NET 说的(还跟着系统语言变)。
        // 这里挡下来,报的是我们自己那句,用例也钉得住。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            throw Fail($"不是个能发的网址（要 http:// 或 https:// 开头）：{url}", ErrorKind.Value);

        var what = OptText(req, "method", uploadPath is null ? "GET" : "POST");
        var timeout = OptInt(req, "timeout", DefaultTimeoutMs);
        var max = OptInt(req, "max", 0) is var m && m > 0 ? m : DefaultMaxBytes;

        using var msg = new HttpRequestMessage(new HttpMethod(what), target);
        if (Opt(req, "headers") is DictVal hs) ApplyHeaders(msg, hs);
        if (uploadPath is { } up)
        {
            var form = new MultipartFormDataContent();
            form.Add(new StreamContent(File.OpenRead(up)), field, Path.GetFileName(up));
            msg.Content = form;
        }
        else if (Opt(req, "bodyFile") is StringVal bf)
        {
            NeedFile(bf.Value, "上传的文件");
            msg.Content = new StreamContent(File.OpenRead(bf.Value));
        }
        else if (Opt(req, "body") is { } body)
        {
            msg.Content = new ByteArrayContent(Bytes(body, "请求正文"));
        }

        using var cts = new CancellationTokenSource(timeout <= 0 ? Timeout.Infinite : timeout);
        var follow = OptBool(req, "follow", true);
        var lax = OptBool(req, "insecure", false);
        var client = (follow, lax) switch
        {
            (true, false) => Web,
            (false, false) => WebNoRedirect,
            (true, true) => WebLax,
            _ => WebLaxNoRedirect,
        };
        try
        {
            // ResponseHeadersRead:正文**边到边收**,不先在内存里攒成一大坨
            using var resp = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var entries = new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("status")] = IntVal.Of((int)resp.StatusCode),
                [new StringVal("reason")] = new StringVal(resp.ReasonPhrase ?? ""),
                [new StringVal("headers")] = HeaderDict(resp),
                // 跟完重定向之后真正停在哪儿 —— 短链、跳登录页这些一眼看得出来
                [new StringVal("url")] = new StringVal(resp.RequestMessage?.RequestUri?.ToString() ?? url),
            };
            if (bodyOut is null)
            {
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                entries[new StringVal("body")] = BytesList(await ReadCapped(stream, max, url, cts.Token));
            }
            else
            {
                using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
                entries[new StringVal("bytes")] = Narrow(await CopyTo(stream, bodyOut, cts.Token));
            }

            return new DictVal(entries);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw Fail($"{what} {url} 超时（{timeout} 毫秒没回来）", ErrorKind.Io);
        }
        catch (HttpRequestException ex)
        {
            // 连不上、域名解析不了、证书不对…… 都落这儿。消息里带上 URL:批量抓的时候才知道是哪一个
            throw Fail($"{what} {url} 失败: {Why(ex)}", ErrorKind.Io);
        }
    }

    public static string Why(HttpRequestException ex) => ex.HttpRequestError switch
    {
        HttpRequestError.NameResolutionError => "域名解析不了",
        HttpRequestError.ConnectionError when ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }
            => "连接被拒绝（对面那个端口没人听）",
        HttpRequestError.ConnectionError => "连不上（网络不通，或者地址/端口不对）",
        HttpRequestError.SecureConnectionError => "TLS 握手没过（证书不对？）",
        HttpRequestError.InvalidResponse => "对面回的响应读不动",
        _ => ex.Message,
    };

    public static void ApplyHeaders(HttpRequestMessage msg, DictVal headers)
    {
        foreach (var (k, v) in headers.Entries)
        {
            var name = k as StringVal ?? throw Fail($"请求头的名字要是字符串，得到 {k.Type}", ErrorKind.Type);
            var values = (v is ListVal l ? l.Elements : [v]).Select(x => Show(x)).ToArray();
            if (!msg.Headers.TryAddWithoutValidation(name.Value, values))
            {
                msg.Content ??= new ByteArrayContent([]);
                msg.Content.Headers.TryAddWithoutValidation(name.Value, values);
            }
        }
    }

    public static DictVal HeaderDict(HttpResponseMessage resp)
    {
        var merged = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Take(IEnumerable<KeyValuePair<string, IEnumerable<string>>> src)
        {
            foreach (var (name, values) in src)
            {
                var key = name.ToLowerInvariant();
                if (!merged.TryGetValue(key, out var list)) merged[key] = list = [];
                list.AddRange(values);
            }
        }

        Take(resp.Headers);
        Take(resp.Content.Headers);

        return new DictVal(merged.ToDictionary(
            kv => (RuntimeValue)new StringVal(kv.Key),
            kv => (RuntimeValue)new StringVal(string.Join(", ", kv.Value))));
    }

    public static async Task<byte[]> ReadCapped(Stream s, long cap, string url, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            if (ms.Length + n > cap)
                throw Fail(
                    $"响应太大（超过 {Human(cap)}）：{url} —— 大文件用 `Http.Download` 直接落盘", ErrorKind.Io);
            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }

    public static string Human(long n)
        => n >= 1024 * 1024 ? $"{n / (1024 * 1024)} MB"
         : n >= 1024 ? $"{n / 1024} KB"
         : $"{n} 字节";

    public static async Task<long> CopyTo(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await from.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
        {
            await to.WriteAsync(buffer, 0, n, ct);
            total += n;
        }

        return total;
    }

    public static string Decode(byte[] bytes, string charset)
    {
        if (charset.Length == 0) return LenientUtf8.GetString(bytes);
        try
        {
            return Encoding.GetEncoding(charset.Trim().Trim('"', '\'')).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return LenientUtf8.GetString(bytes);
        }
    }

    public static readonly UTF8Encoding LenientUtf8 = new(false, throwOnInvalidBytes: false);

    private const int DefaultTimeoutMs = 30_000;
    private const int DefaultMaxBytes = 16 * 1024 * 1024;

    public static RuntimeValue Http(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (IsNetFault(ex))
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    public static async Task<RuntimeValue> HttpAsync(string what, Func<Task<RuntimeValue>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex) when (IsNetFault(ex))
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }

    private static bool IsNetFault(Exception ex)
        => ex is IOException or UnauthorizedAccessException or ArgumentException
           or NotSupportedException or InvalidOperationException
           or System.Security.SecurityException or System.Net.Sockets.SocketException;
}
