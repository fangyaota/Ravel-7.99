namespace Ravel;

using System.Collections;
using System.Reflection;

/// <summary>把一棵 AST 摊平:**反射**走属性,不手写每个节点的孩子。
///
/// 手写的话每加一个节点都得回来补一笔,漏了就是**静默少查** —— 而"少数一个节点"
/// 恰恰是这条语言最恨的那种失败。转储那边(`Cli/AstDump`)早就是反射了,同一个道理。
///
/// 走**属性**而不是字段:AST 全是 record,孩子都在属性上。`Parameter` / `DictPatEntry`
/// 不是 <see cref="AstNode"/>(它们不继承),但照样有孩子(`Pattern` / `Expression`),
/// 一并走。
///
/// 只在**编译期那一趟诊断**上用(见 <see cref="CompileWarnings"/>),一个程序跑一次 ——
/// 反射那点开销在这儿可以忽略。</summary>
internal static class AstWalk
{
    /// <summary>这棵树里的**所有**节点(含 <paramref name="root"/> 自己),**先左后右**。
    ///
    /// 位置(`Line` / `Column`)是数字,本来就不是孩子,不用特意排。
    /// 遍历次序按属性的声明次序(record 的位置参数就是声明次序)。</summary>
    public static IEnumerable<AstNode> Nodes(AstNode root)
    {
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is AstNode a) yield return a;

            var props = Props(node.GetType());
            // **倒着压**:栈是后进先出,倒着压出来的次序才和声明次序一致
            for (var i = props.Length - 1; i >= 0; i--)
            {
                var value = props[i].GetValue(node);
                if (value is IEnumerable list and not string)
                {
                    var items = list.Cast<object>().ToList();
                    for (var j = items.Count - 1; j >= 0; j--)
                        if (IsChild(items[j])) stack.Push(items[j]);
                }
                else if (IsChild(value)) stack.Push(value!);
            }
        }
    }

    /// <summary>值是不是"树里的一个孩子"。`null` 和别的东西(字符串、bool、枚举、
    /// `RuntimeValue`)都不是 —— `<see cref="LiteralExpr"/>` 那个值就不是节点。</summary>
    private static bool IsChild(object? value) => value is AstNode or Parameter or DictPatEntry;

    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

    /// <summary>公开实例属性,按声明次序。`Line` / `Column` 排掉 —— 它们是**位置**不是孩子,
    /// 每个节点都有、走一遍纯属白费。</summary>
    private static PropertyInfo[] Props(Type type)
    {
        if (Cache.TryGetValue(type, out var hit)) return hit;
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not ("Line" or "Column"))
            .OrderBy(p => p.MetadataToken)
            .ToArray();
        Cache[type] = props;
        return props;
    }
}
