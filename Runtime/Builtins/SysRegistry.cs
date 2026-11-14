namespace Ravel.Runtime;

using System.Reflection;

internal static class SysRegistry
{
    private static readonly List<(string Name, MethodInfo Method)> Found = Scan();

    public static void Register(Interpreter self, Action<string, FunctionVal> def)
    {
        foreach (var (name, method) in Found)
            def(name, Bind(self, method));
    }

    private static List<(string, MethodInfo)> Scan()
    {
        var found = new List<(string, MethodInfo)>();
        foreach (var type in typeof(Interpreter).Assembly.GetTypes())
        foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (m.GetCustomAttribute<SysAttribute>() is not { } attr) continue;

            if (!Check(m, out var why))
                throw new InvalidOperationException(Messages.T("runtime.interop.builtin_bad_signature", attr.Name, type.Name, m.Name, why));
            if (found.Any(f => f.Item1 == attr.Name))
                throw new InvalidOperationException(Messages.T("runtime.interop.builtin_registered_twice", attr.Name, type.Name, m.Name));

            found.Add((attr.Name, m));
        }

        // **按名字排**:反射给的先后没有保证(取决定元数据顺序),而成员表是**按登记先后**的
        // —— `System.Fields ()` 打出来就是那个顺序。不排的话同一份源码在不同运行时上
        // 列出来的可能不一样,那是个没人会想到去查的坑。
        found.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return found;
    }

    private static bool Check(MethodInfo m, out string why)
    {
        var ps = m.GetParameters();
        var first = ps.Length > 0 && ps[0].ParameterType == typeof(Interpreter);

        if (m.ReturnType != typeof(RuntimeValue)) { why = " 要返回 RuntimeValue"; return false; }
        if (ps.Skip(first ? 1 : 0).Any(p => p.ParameterType != typeof(RuntimeValue)))
        {
            why = " 的参数要么是开头的 Interpreter，要么是 RuntimeValue";
            return false;
        }

        var arity = ps.Length - (first ? 1 : 0);
        if (arity is < 1 or > 3) { why = $" 要收 1~3 个实参（现在 {arity} 个）"; return false; }
        why = "";
        return true;
    }

    private static FunctionVal Bind(Interpreter self, MethodInfo m) => ClassRegistry.Fn(self, m);
}
