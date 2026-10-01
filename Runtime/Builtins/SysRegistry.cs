namespace Ravel.Runtime;

using System.Reflection;

/// <summary>`[Sys]` 那一族的**扫描与绑定**。
///
/// 扫的是**整个程序集**(不列名单 —— 列了就又回到"加一个得回去改一行"那条老路):
/// 谁身上有 `[Sys]` 方法谁就是内置那一族,写在哪个文件、哪个类里都行。
/// 扫描**静态缓存**(反射不便宜,而解释器是每跑一次就新建一个的),绑委托每实例做一次 ——
/// 调用在热路上(`print` 也过它),不能留 `MethodInfo.Invoke`。</summary>
internal static class SysRegistry
{
    private static readonly List<(string Name, MethodInfo Method)> Found = Scan();

    /// <summary>把扫出来的全登记进去。`def` 就是解释器那边的 `DefFn`(登记到 `System` 模块表里)。</summary>
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
                throw new InvalidOperationException($"内置 '{attr.Name}'（{type.Name}.{m.Name}）{why}");
            if (found.Any(f => f.Item1 == attr.Name))
                throw new InvalidOperationException($"内置 '{attr.Name}' 登记了两次（{type.Name}.{m.Name}）");

            found.Add((attr.Name, m));
        }

        // **按名字排**:反射给的先后没有保证(取决定元数据顺序),而成员表是**按登记先后**的
        // —— `System.Fields ()` 打出来就是那个顺序。不排的话同一份源码在不同运行时上
        // 列出来的可能不一样,那是个没人会想到去查的坑。
        found.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return found;
    }

    /// <summary>签名对不对。**建表时就报**,别等调到才发现。</summary>
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

    private static FunctionVal Bind(Interpreter self, MethodInfo m)
    {
        var ps = m.GetParameters();
        var takesSelf = ps.Length > 0 && ps[0].ParameterType == typeof(Interpreter);
        var arity = ps.Length - (takesSelf ? 1 : 0);

        // `Direct` 而不是 `From`:**少包一层** —— `CreateDelegate` 出来的委托没法像
        // 编译器生成的闭包那样被 JIT 去虚化,再叠一层 lambda 就是白多一跳
        // (实测每次调用差 80ns —— 见 `FunctionVal.Direct` 上那段)。
        if (takesSelf)
        {
            return arity switch
            {
                1 => Call(m.CreateDelegate<Func<Interpreter, RuntimeValue, RuntimeValue>>()),
                2 => Call(m.CreateDelegate<Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue>>()),
                _ => Call(m.CreateDelegate<Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue>>()),
            };

            FunctionVal Call(Delegate f)
            {
                return arity switch
                {
                    1 => FunctionVal.Direct(a => ((Func<Interpreter, RuntimeValue, RuntimeValue>)f)(self, a)),
                    2 => FunctionVal.Direct((a, b) => ((Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue>)f)(self, a, b)),
                    _ => FunctionVal.Direct((a, b, c) =>
                        ((Func<Interpreter, RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue>)f)(self, a, b, c)),
                };
            }
        }

        return arity switch
        {
            1 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue>>()),
            2 => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue>>()),
            _ => FunctionVal.Direct(m.CreateDelegate<Func<RuntimeValue, RuntimeValue, RuntimeValue, RuntimeValue>>()),
        };
    }
}
