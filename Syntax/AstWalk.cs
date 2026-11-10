namespace Ravel;

using System.Collections;
using System.Reflection;

internal static class AstWalk
{
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

    private static bool IsChild(object? value) => value is AstNode or Parameter or DictPatEntry;

    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

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
