namespace Ravel.Runtime;

public record ObjectVal(RuntimeType ClassType, Scope Scope) : RuntimeValue
{
    /// <summary>最多列几个字段,超出用 ... 收尾</summary>
    private const int MaxFields = 8;

    public override RuntimeType Type => ClassType;

    /// <summary>`C { x = 1, s = "hi" }`。类名取 DisplayName(`:=` 建的类没有名字,显示成 class)。</summary>
    public override string ToString()
    {
        var name = ClassType.DisplayName;
        var fields = new List<string>();
        foreach (var kv in Scope.Variables)
        {
            // 只列数据字段:方法(含 init)是噪音,this/base 是机制内部的
            if (kv.Value.Value.IsClosure) continue;
            if (kv.Key is "this" or "parent" or "base" or "thistype" or "block") continue;
            if (fields.Count == MaxFields) { fields.Add("..."); break; }
            fields.Add(kv.Key + " = " + Brief(kv.Value.Value));
        }

        return fields.Count == 0 ? name + " {}" : name + " { " + string.Join(", ", fields) + " }";
    }

    /// <summary>字段值的短形式:嵌套的对象/集合不再展开。列表本身就展开元素,
    /// 这里跟着展开的话,`a.Add a` 这种自引用会直接把栈打爆。</summary>
    private static string Brief(RuntimeValue v) => v switch
    {
        ObjectVal o => o.ClassType.DisplayName + " {...}",
        ListVal l => "[" + l.Elements.Count + " 项]",
        SetVal s => "{" + s.Elements.Count + " 项}",
        DictVal d => "{" + d.Entries.Count + " 项}",
        _ => v.ToString() ?? "()",
    };
}
