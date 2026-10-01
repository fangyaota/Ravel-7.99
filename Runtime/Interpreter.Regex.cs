namespace Ravel.Runtime;

using System.Text.RegularExpressions;

/// <summary>`System` 模块里的**正则原语** —— 把 .NET 的 `System.Text.RegularExpressions` 包一层。
///
/// **策略在库**(`lib/regex.rav`):这几条只做"给模式 + 文本 + 选项,交出数据"(匹配交回普通
/// dict,由库拼成人话的对象),和文件那批 syscall 一个规矩。
///
/// 两条**必须在这层兜住**的:
/// <list type="bullet">
/// <item>**超时** —— .NET 的 `Regex` 默认没有超时,写歪一个模式(`(a+)+$` 配一长串 a)
/// 能把进程**卡死**。卡死不是异常,Ravel 的 `try` 接不住,只能在这一层给个时限
/// (见 <see cref="RegexTimeout"/>)—— 超了报成普通的 Ravel 错误。</item>
/// <item>**C# 异常** —— 模式写错抛的是 `ArgumentException`,漏出去会绕过 Ravel 的 `try`
/// 把程序打掉(和 `Fs` 那条规矩一样);这里接住,换成说人话的 Ravel 错误。</item>
/// </list>
///
/// 用的都是**静态**那组重载(`Regex.IsMatch (input, pattern, options, timeout)`):
/// 它们走 .NET 内部那张模式缓存,同一个模式不必反复编译。</summary>
public partial class Interpreter
{
    /// <summary>一条正则最多跑多久。够日常用,又不至于让一个坏模式把程序挂死。</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // 一个匹配 → dict:`{value, index, length, groups, named}`。
    // `groups` 是**一列**(0 号是整体;没参与匹配的组给 `()` —— 和"空串"分得开);
    // `named` 只放**有名字**的组(`(?<名字>…)`),让库那边能按名字取。
    private static DictVal Shape(Match m)
    {
        var groups = new List<RuntimeValue>();
        foreach (Group g in m.Groups)
            groups.Add(g.Success ? new StringVal(g.Value) : VoidVal.Instance);

        var named = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var name in m.Groups.Keys)
        {
            if (int.TryParse(name, out _)) continue;      // 数字组已经在 groups 里了
            var g = m.Groups[name];
            named[new StringVal(name)] = g.Success ? new StringVal(g.Value) : VoidVal.Instance;
        }

        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("value")] = new StringVal(m.Value),
            [new StringVal("index")] = new IntVal(m.Index),
            [new StringVal("length")] = new IntVal(m.Length),
            [new StringVal("groups")] = new ListVal(groups),
            [new StringVal("named")] = new DictVal(named),
        });
    }

    // ── 选项串 ──
    // `i` 不分大小写、`m` 多行(`^` / `$` 认每一行)、`s` 让 `.` 也吃换行、
    // `x` 忽略模式里的空白与 `#` 注释。**不认识的字符当场报错** —— 悄悄忽略等于
    // 让写的人以为生效了(和"修饰符不认识就报"一条规矩)。
    private static RegexOptions Flags(string flags)
    {
        var opts = RegexOptions.None;
        foreach (var c in flags)
            opts |= c switch
            {
                'i' => RegexOptions.IgnoreCase,
                'm' => RegexOptions.Multiline,
                's' => RegexOptions.Singleline,
                'x' => RegexOptions.IgnorePatternWhitespace,
                _ => throw new RuntimeException($"不认识的选项 '{c}'（有 i / m / s / x）", ErrorKind.Regex),
            };
        return opts;
    }

    // 把"两条兜底"收在一处:超时 + 模式写错。参数类型不对由 `As` 那层报。
    private static RuntimeValue Guarded(string pattern, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuntimeException(
                $"匹配超时（{RegexTimeout.TotalSeconds:0} 秒）—— 这个模式可能有灾难性回溯：{pattern}", ErrorKind.Regex);
        }
        catch (ArgumentException ex)
        {
            throw new RuntimeException($"正则式写错了 —— {ex.Message}", ErrorKind.Regex);
        }
    }

    private static string Str(RuntimeValue v, string what) => As<StringVal>(v, what).Value;

    // ── 六条 ──(统一形状:**模式、文本、选项**,`Replace` 再多一个替换串)

    [Sys("RegexEscape")]
    private static RuntimeValue RegexEscape(RuntimeValue a)
        => new StringVal(Regex.Escape(Str(a, "RegexEscape 的文本")));

    [Sys("RegexIsMatch")]
    private static RuntimeValue RegexIsMatch(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexIsMatch 的模式");
        return Guarded(pattern, () =>
            new BoolVal(Regex.IsMatch(Str(t, "RegexIsMatch 的文本"), pattern,
                Flags(Str(f, "RegexIsMatch 的选项")), RegexTimeout)));
    }

    [Sys("RegexMatch")]
    private static RuntimeValue RegexMatch(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexMatch 的模式");
        return Guarded(pattern, () =>
        {
            var m = Regex.Match(Str(t, "RegexMatch 的文本"), pattern,
                Flags(Str(f, "RegexMatch 的选项")), RegexTimeout);
            return m.Success ? Shape(m) : VoidVal.Instance;      // 没有就是 `()`,由库决定报错还是 None
        });
    }

    [Sys("RegexFindAll")]
    private static RuntimeValue RegexFindAll(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexFindAll 的模式");
        return Guarded(pattern, () =>
        {
            var found = new List<RuntimeValue>();
            foreach (Match m in Regex.Matches(Str(t, "RegexFindAll 的文本"), pattern,
                         Flags(Str(f, "RegexFindAll 的选项")), RegexTimeout))
                found.Add(Shape(m));
            return new ListVal(found);
        });
    }

    /// <summary>四个参数:前三个进这一层,交回的是"还等着替换串"的那枚函数
    /// (柯里化,和 `FunctionVal.From` 那套一个走法)。</summary>
    [Sys("RegexReplace")]
    private static RuntimeValue RegexReplace(RuntimeValue p, RuntimeValue t, RuntimeValue f)
        => FunctionVal.From(r =>
        {
            var pattern = Str(p, "RegexReplace 的模式");
            return Guarded(pattern, () =>
                new StringVal(Regex.Replace(Str(t, "RegexReplace 的文本"), pattern,
                    Str(r, "RegexReplace 的替换串"),
                    Flags(Str(f, "RegexReplace 的选项")), RegexTimeout)));
        });

    [Sys("RegexSplit")]
    private static RuntimeValue RegexSplit(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexSplit 的模式");
        return Guarded(pattern, () =>
            new ListVal([.. Regex.Split(Str(t, "RegexSplit 的文本"), pattern,
                Flags(Str(f, "RegexSplit 的选项")), RegexTimeout)
                .Select(part => (RuntimeValue)new StringVal(part))]));
    }
}
