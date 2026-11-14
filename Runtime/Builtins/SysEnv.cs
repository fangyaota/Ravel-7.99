namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

internal static class SysEnv
{
    [Sys("Args")]
    public static RuntimeValue ScriptArgList(Interpreter self, RuntimeValue _)
        => new ListVal([.. self.ScriptArgs.Select(a => (RuntimeValue)new StringVal(a))]);

    [Sys("Env")]
    public static RuntimeValue GetEnv(RuntimeValue a) => Fs("读环境变量", () =>
    {
        var name = EnvName(a, "Env");
        var v = Environment.GetEnvironmentVariable(name);
        if (v == null) throw new RuntimeException(Messages.T("runtime.io.env_var_missing", name), ErrorKind.Key);
        return new StringVal(v);
    });

    [Sys("EnvOr")]
    public static RuntimeValue GetEnvOr(RuntimeValue a, RuntimeValue b) => Fs("读环境变量", () =>
    {
        var name = EnvName(a, "EnvOr");
        var dflt = As<StringVal>(b, "EnvOr 的默认值").Value;
        return new StringVal(Environment.GetEnvironmentVariable(name) ?? dflt);
    });

    [Sys("SetEnv")]
    public static RuntimeValue SetEnv(RuntimeValue a, RuntimeValue b) => Fs("设环境变量", () =>
    {
        var name = EnvName(a, "SetEnv");
        var value = As<StringVal>(b, "SetEnv 的值").Value;
        // **空串 = 删掉**(和 `UnsetEnv` 同一件事,只是写法顺手)—— 见上面那段:
        // 这是我们的约定,不靠运行时"收空串会删"那点脾气(net8 是,net10 不是)
        if (value.Length == 0)
        {
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
            return VoidVal.Instance;
        }
        // 只动**本进程**那份(.NET 还能写用户级/机器级的 —— 那是装环境,不该由一句赋值顺手做掉)。
        // 新起的子进程(`cmd` / `Io` 里那些)照常看得见。
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
        return VoidVal.Instance;
    });

    [Sys("UnsetEnv")]
    public static RuntimeValue UnsetEnv(RuntimeValue a) => Fs("删环境变量", () =>
    {
        Environment.SetEnvironmentVariable(EnvName(a, "UnsetEnv"), null, EnvironmentVariableTarget.Process);
        return VoidVal.Instance;
    });

    [Sys("EnvAll")]
    public static RuntimeValue EnvAll(RuntimeValue _)
    {
        var entries = new Dictionary<RuntimeValue, RuntimeValue>();
        foreach (var n in Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(n => n, StringComparer.Ordinal))
            entries[new StringVal(n)] = new StringVal(Environment.GetEnvironmentVariable(n) ?? "");
        return new DictVal(entries);
    }
}
