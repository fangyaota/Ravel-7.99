using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>网络 —— 只做到 HTTP 这一层(裸 TCP 以后再说)。
///
/// 和文件系统那批一个规矩:**只做 syscall,不做策略** —— URL 怎么拼、响应怎么解、
/// 重试几次、重定向跟不跟,全是 `lib/http.rav` 的事(和正则、随机数一个路子)。
/// 四条原语都**收一个 dict、交回一个 dict**:加字段不用改签名,而且以后加并发时,
/// "发请求"整个变成一次**挂起点**,库里那些 `Http.Get` 一个字都不用改。
///
/// 请求 dict 认这些键(缺的都走默认):
/// `url` / `method`(默认 GET)/ `headers`(名字→值,值也能给 list)/ `body`(字节表)/
/// `bodyFile`(从文件流式发)/ `follow`(默认 true)/ `timeout`(毫秒,默认 30 秒)/
/// `max`(响应正文封顶,默认 16 MB)。
/// 交回:`status` / `reason` / `headers`(小写名→值,重复的用 `, ` 连起来)/ `body`(字节表)/
/// `url`(跟完重定向之后那个)。下载、上传那两条把 `body` 换成 `bytes`(写出去多少字节)。</summary>
internal static class SysNet
{
    [Sys("HttpReq")]
    public static RuntimeValue HttpReq(RuntimeValue a)
        => Http("发请求", () => Send(As<DictVal>(a, "HttpReq 的请求"), null));

    [Sys("HttpDownload")]
    public static RuntimeValue HttpDownload(RuntimeValue a) => Http("下载", () =>
    {
        var req = As<DictVal>(a, "HttpDownload 的请求");
        var path = OptText(req, "path", "");
        NeedParentDir(path, "下载");
        // 边收边落盘:正文**不进 Ravel 堆** —— 下载一个 500 MB 的东西,堆里不该多一个字节表
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        return Send(req, file);
    });

    [Sys("HttpUpload")]
    public static RuntimeValue HttpUpload(RuntimeValue a) => Http("上传", () =>
    {
        var req = As<DictVal>(a, "HttpUpload 的请求");
        var path = OptText(req, "path", "");
        NeedFile(path, "上传的文件");
        return Send(req, null, path, OptText(req, "field", "file"));
    });

    /// <summary>字节 → 文本。**认不出来的字符集不报错**、按 UTF-8 解:网上写着 `charset=utf8`、
    /// `charset=UTF8`、甚至拼错的大把,为一个只影响显示的字段把整次请求打断不值当。</summary>
    [Sys("DecodeText")]
    public static RuntimeValue DecodeText(RuntimeValue a, RuntimeValue b) => Fs("解码文本", () =>
        new StringVal(Decode(BytesOf(a, "DecodeText 的字节"), As<StringVal>(b, "DecodeText 的字符集").Value)));

    /// <summary>进程级共享的那一台 —— **别每次请求 new 一个**:连接池挂在它身上,
    /// 一次请求一台的话连接永远复用不上,请求一多就把本机端口耗光(.NET 上最经典的那个坑)。
    ///
    /// 超时**不设在这台身上**(设了就是全进程一把尺子),每个请求自己用 CancellationToken 管。
    /// 两台只差一件事:跟不跟重定向 —— 那是**处理器**(handler)级的开关,没法按请求改,
    /// 所以宁可养两台也不给用户一个"说是不跟、其实跟了"的 `follow`。</summary>
    public static readonly HttpClient Web = MakeWeb(true);
    public static readonly HttpClient WebNoRedirect = MakeWeb(false);

    public static HttpClient MakeWeb(bool follow) => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = follow,
        MaxAutomaticRedirections = 10,
        AutomaticDecompression = DecompressionMethods.All,   // gzip / deflate / br 全让 .NET 顺手解开
    })
    { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>发出去、等回来。`bodyOut` 不是 null 时把正文**流式写进它**(下载那条路),
    /// `uploadPath` 不是 null 时把那个文件**流式发出去**(multipart,上传那条路)。</summary>
    public static RuntimeValue Send(DictVal req, Stream? bodyOut, string? uploadPath = null, string field = "file")
    {
        var url = OptText(req, "url", "");
        if (url.Length == 0) throw new RuntimeException("请求 dict 里没有 url", ErrorKind.Argument);
        // 网址**先自己验一遍**:`HttpRequestMessage` 对"这是个相对 URI"是构造得出来的,
        // 那要等到发的时候才报,而那时的话是 .NET 说的(还跟着系统语言变)。
        // 这里挡下来,报的是我们自己那句,用例也钉得住。
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps))
            throw new RuntimeException($"不是个能发的网址（要 http:// 或 https:// 开头）：{url}", ErrorKind.Value);

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
            msg.Content = new ByteArrayContent(BytesOf(body, "请求正文"));
        }

        using var cts = new CancellationTokenSource(timeout <= 0 ? Timeout.Infinite : timeout);
        var client = OptBool(req, "follow", true) ? Web : WebNoRedirect;
        try
        {
            // ResponseHeadersRead:正文**边到边收**,不先在内存里攒成一大坨
            using var resp = client.Send(msg, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var entries = new Dictionary<RuntimeValue, RuntimeValue>
            {
                [new StringVal("status")] = new IntVal((int)resp.StatusCode),
                [new StringVal("reason")] = new StringVal(resp.ReasonPhrase ?? ""),
                [new StringVal("headers")] = HeaderDict(resp),
                // 跟完重定向之后真正停在哪儿 —— 短链、跳登录页这些一眼看得出来
                [new StringVal("url")] = new StringVal(resp.RequestMessage?.RequestUri?.ToString() ?? url),
            };
            if (bodyOut is null)
            {
                using var stream = resp.Content.ReadAsStream(cts.Token);
                entries[new StringVal("body")] = BytesList(ReadCapped(stream, max, url));
            }
            else
            {
                using var stream = resp.Content.ReadAsStream(cts.Token);
                entries[new StringVal("bytes")] = BuiltinClasses.Narrow(CopyTo(stream, bodyOut, cts.Token), "下载的字节数");
            }

            return new DictVal(entries);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new RuntimeException($"{what} {url} 超时（{timeout} 毫秒没回来）", ErrorKind.Io);
        }
        catch (HttpRequestException ex)
        {
            // 连不上、域名解析不了、证书不对…… 都落这儿。消息里带上 URL:批量抓的时候才知道是哪一个
            throw new RuntimeException($"{what} {url} 失败: {Why(ex)}", ErrorKind.Io);
        }
    }

    /// <summary>网络错误的那句中文。**不能直接用 `ex.Message`**:那是 .NET 跟着系统语言给的
    /// (中文机器上是一种说法、英文机器上又一种),而报错消息是要被 golden 用例**钉住**的。
    /// 认不出来的照样把原文带上 —— 总比一句"失败了"强。</summary>
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

    /// <summary>请求头。名字撞上"内容头"(`content-type` 这类)时要加在 Content 那半边 ——
    /// 加错地方 .NET 会抛「不能把 content header 加到 request headers 上」,而那是个
    /// 用起来很意外的错误。</summary>
    public static void ApplyHeaders(HttpRequestMessage msg, DictVal headers)
    {
        foreach (var (k, v) in headers.Entries)
        {
            var name = k as StringVal ?? throw new RuntimeException($"请求头的名字要是字符串，得到 {k.Type}", ErrorKind.Type);
            var values = (v is ListVal l ? l.Elements : [v]).Select(x => Show(x)).ToArray();
            if (!msg.Headers.TryAddWithoutValidation(name.Value, values))
            {
                msg.Content ??= new ByteArrayContent([]);
                msg.Content.Headers.TryAddWithoutValidation(name.Value, values);
            }
        }
    }

    /// <summary>响应头:响应那一层 + 内容那一层合成一张。名字**一律小写**(HTTP 头不区分大小写,
    /// 给两套写法只会让查的人猜);同名的多个值用 `, ` 连起来(和 HTTP 自己的规矩一致)。</summary>
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

    /// <summary>读正文,超过 `cap` 就停下报错。不封顶的话一个大文件会先在堆里攒成字节表,
    /// 而堆是有上限的(测试里 256 MB)—— 那种时候该走 `Http.Download`。</summary>
    public static byte[] ReadCapped(Stream s, long cap, string url)
    {
        var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (ms.Length + n > cap)
                throw new RuntimeException(
                    $"响应太大（超过 {Human(cap)}）：{url} —— 大文件用 `Http.Download` 直接落盘", ErrorKind.Io);
            ms.Write(buffer, 0, n);
        }

        return ms.ToArray();
    }

    /// <summary>一个字节数怎么说人话(报错里用)。默认那 16 MB 不能印成 16777216。
    /// (和 `Format.Bytes` 那个不一样:这儿是**上限**、不是给人看的文件大小,整数就够)</summary>
    public static string Human(long n)
        => n >= 1024 * 1024 ? $"{n / (1024 * 1024)} MB"
         : n >= 1024 ? $"{n / 1024} KB"
         : $"{n} 字节";

    /// <summary>正文直接抄进别的流(下载),交回抄了多少字节 —— 中途不经过内存里那张字节表</summary>
    public static long CopyTo(Stream from, Stream to, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            to.Write(buffer, 0, n);
            total += n;
        }

        return total;
    }

    /// <summary>按字符集解字节。GBK 那一批要 `CodePagesEncodingProvider` 才认得
    /// (见静态构造函数);认不出来的名字退回 UTF-8,不报错。</summary>
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

    /// <summary>宽松 UTF-8:坏字节变成 U+FFFD,不抛异常、也不静默丢掉 ——
    /// 网页上出现个把乱码不值得把整次请求打断。</summary>
    public static readonly UTF8Encoding LenientUtf8 = new(false, throwOnInvalidBytes: false);

    /// <summary>第一次用到这个类时把老编码注册上 —— 就挂在要用它的这一格,
    /// 不必再挂到解释器身上(那是「整台引擎」的事,而这是「解码文本」的事)。</summary>
    static SysNet()
    {
        // 老编码(GBK / GB2312 / Big5…)在 .NET Core 上要显式注册才认 —— 中文网页常见,
        // 不注册的话 `charset=gbk` 会静默退回 UTF-8,整页乱码还找不到原因
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const int DefaultTimeoutMs = 30_000;
    private const int DefaultMaxBytes = 16 * 1024 * 1024;

    /// <summary>网络那一批的兜底:兜住的比 <see cref="Fs"/> 宽 —— 网络能出的岔子
    /// (DNS、连接被拒、TLS、坏 URL)本来就不是 `IOException` 那一族,漏出去会把程序打掉。</summary>
    public static RuntimeValue Http(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or InvalidOperationException
                                   or System.Security.SecurityException or System.Net.Sockets.SocketException)
        {
            throw new RuntimeException($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }
}
