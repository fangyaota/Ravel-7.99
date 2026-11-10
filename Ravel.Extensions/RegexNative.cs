using System.Text.RegularExpressions;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

[RavelModule("Native")]
internal static class RegexNative
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // 一个匹配 → dict:`{value, index, length, groups, named}`。
    // `groups` 是**一列**(0 号是整体;没参与匹配的组给 `()` —— 和"空串"分得开);
    // `named` 只放**有名字**的组(`(?<名字>…)`),让库那边能按名字取。
    private static DictVal Shape(Match m)
    {
        var groups = new List<RuntimeValue>();
        foreach (Group g in m.Groups)
            groups.Add(g.Success ? new StringVal(g.Value) : Void);

        var named = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var name in m.Groups.Keys)
        {
            if (int.TryParse(name, out _)) continue;      // 数字组已经在 groups 里了
            var g = m.Groups[name];
            named[new StringVal(name)] = g.Success ? new StringVal(g.Value) : Void;
        }

        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("value")] = new StringVal(m.Value),
            [new StringVal("index")] = IntVal.Of(m.Index),
            [new StringVal("length")] = IntVal.Of(m.Length),
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
                _ => throw Fail($"不认识的选项 '{c}'（有 i / m / s / x）", ErrorKind.Regex),
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
            throw Fail(
                $"匹配超时（{RegexTimeout.TotalSeconds:0} 秒）—— 这个模式可能有灾难性回溯：{pattern}", ErrorKind.Regex);
        }
        catch (ArgumentException ex)
        {
            throw Fail($"正则式写错了 —— {ex.Message}", ErrorKind.Regex);
        }
    }

    private static string Str(RuntimeValue v, string what) => Text(v, what).Value;

    // ── 六条 ──(统一形状:**模式、文本、选项**,`Replace` 再多一个替换串)

    [RavelFn("RegexEscape")]
    public static RuntimeValue RegexEscape(RuntimeValue a)
        => new StringVal(Regex.Escape(Str(a, "RegexEscape 的文本")));

    [RavelFn("RegexIsMatch")]
    public static RuntimeValue RegexIsMatch(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexIsMatch 的模式");
        return Guarded(pattern, () =>
            new BoolVal(Regex.IsMatch(Str(t, "RegexIsMatch 的文本"), pattern,
                Flags(Str(f, "RegexIsMatch 的选项")), RegexTimeout)));
    }

    [RavelFn("RegexMatch")]
    public static RuntimeValue RegexMatch(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexMatch 的模式");
        return Guarded(pattern, () =>
        {
            var m = Regex.Match(Str(t, "RegexMatch 的文本"), pattern,
                Flags(Str(f, "RegexMatch 的选项")), RegexTimeout);
            return m.Success ? Shape(m) : Void;      // 没有就是 `()`,由库决定报错还是 None
        });
    }

    [RavelFn("RegexFindAll")]
    public static RuntimeValue RegexFindAll(RuntimeValue p, RuntimeValue t, RuntimeValue f)
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

    [RavelFn("RegexReplace")]
    public static RuntimeValue RegexReplace(RuntimeValue p, RuntimeValue t, RuntimeValue f)
        => FunctionVal.From(r =>
        {
            var pattern = Str(p, "RegexReplace 的模式");
            return Guarded(pattern, () =>
                new StringVal(Regex.Replace(Str(t, "RegexReplace 的文本"), pattern,
                    Str(r, "RegexReplace 的替换串"),
                    Flags(Str(f, "RegexReplace 的选项")), RegexTimeout)));
        });

    [RavelFn("RegexSplit")]
    public static RuntimeValue RegexSplit(RuntimeValue p, RuntimeValue t, RuntimeValue f)
    {
        var pattern = Str(p, "RegexSplit 的模式");
        return Guarded(pattern, () =>
            new ListVal([.. Regex.Split(Str(t, "RegexSplit 的文本"), pattern,
                Flags(Str(f, "RegexSplit 的选项")), RegexTimeout)
                .Select(part => (RuntimeValue)new StringVal(part))]));
    }
}
