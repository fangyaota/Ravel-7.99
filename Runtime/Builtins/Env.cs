namespace Ravel.Runtime;

/// <summary>`System` 的命令行参数与环境变量 —— **只做 syscall,不做策略**:
/// 外面给进来什么就是什么,"要不要先问一句"交给 `EnvOr`(不报错的那条)。
/// 变量名空着那种没用的情况当场拦下,免得 `System.SetEnv "" "x"` 静默变成一次什么都没做的调用。</summary>
public partial class Interpreter
{
    /// <summary>`Args ()` 给的是**脚本名之后**那些参数,由 CLI 填进来(见 Program.cs 的 RunFile)。
    /// 解释器自己不读命令行 —— 不这么切的话,`dotnet out/ravel.dll a.rav` 里第一个参数
    /// 就变成了解释器自己的路径,脚本作者还得自己去认那几个。</summary>
    [Sys("Args")]
    private RuntimeValue ScriptArgList(RuntimeValue _)
        => new ListVal([.. ScriptArgs.Select(a => (RuntimeValue)new StringVal(a))]);

    /// <summary>没有这个变量就报错(要默认值用 `EnvOr`)。**空串不算"有"** —— .NET 那边
    /// `SetEnvironmentVariable` 收空串就是把这变量删掉,所以 `SetEnv "X" ""` 之后
    /// `Env "X"` 照样报错(要"空着的值"这个状态,环境变量这套里本来也存不下)。</summary>
    [Sys("Env")]
    private static RuntimeValue GetEnv(RuntimeValue a) => Fs("读环境变量", () =>
    {
        var name = EnvName(a, "Env");
        var v = Environment.GetEnvironmentVariable(name);
        if (v == null) throw new RuntimeException($"环境变量 '{name}' 没有（想给个默认值用 System.EnvOr）", ErrorKind.Key);
        return new StringVal(v);
    });

    [Sys("EnvOr")]
    private static RuntimeValue GetEnvOr(RuntimeValue a, RuntimeValue b) => Fs("读环境变量", () =>
    {
        var name = EnvName(a, "EnvOr");
        var dflt = As<StringVal>(b, "EnvOr 的默认值").Value;
        return new StringVal(Environment.GetEnvironmentVariable(name) ?? dflt);
    });

    [Sys("SetEnv")]
    private static RuntimeValue SetEnv(RuntimeValue a, RuntimeValue b) => Fs("设环境变量", () =>
    {
        var name = EnvName(a, "SetEnv");
        var value = As<StringVal>(b, "SetEnv 的值").Value;
        // 只动**本进程**那份(.NET 还能写用户级/机器级的 —— 那是装环境,不该由一句赋值顺手做掉)。
        // 新起的子进程(`cmd` / `Io` 里那些)照常看得见。
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        return VoidVal.Instance;
    });

    [Sys("UnsetEnv")]
    private static RuntimeValue UnsetEnv(RuntimeValue a) => Fs("删环境变量", () =>
    {
        Environment.SetEnvironmentVariable(EnvName(a, "UnsetEnv"), null, EnvironmentVariableTarget.Process);
        return VoidVal.Instance;
    });

    /// <summary>全部环境变量:名字 → 值。**按名字排序** —— 进程那本字典的先后随机器而定,
    /// 排一下打印出来才可期待(和 `Io.Dir.List ()` 那边同一个理由)。</summary>
    [Sys("EnvAll")]
    private static RuntimeValue EnvAll(RuntimeValue _)
    {
        var entries = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var n in Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(n => n, StringComparer.Ordinal))
            entries[new StringVal(n)] = new StringVal(Environment.GetEnvironmentVariable(n) ?? "");
        return new DictVal(entries);
    }
}
