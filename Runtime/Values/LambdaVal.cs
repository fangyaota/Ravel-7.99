namespace Ravel.Runtime;

public sealed record LambdaVal(string ParamName, Expression ParamTypeExpr, ObjectVal ParamType, BlockExpr Block)
    : FunctionVal(null!, (_, _) => FunctionVal.PlaceholderBody("LambdaVal"))
{
    public Pattern? ParamPattern { get; init; }

    public List<string>? ParamAttrs { get; init; }

    public override string ToString()
        => "<function " + (Name is { Length: > 0 } n ? n + " " : "") + Describe() + ">";

    internal string Describe()
    {
        var text = AstPrinter.Signature(this);
        var applied = Applied();
        if (applied.Count > 0) text += " applied " + string.Join(" ", applied);
        return text;
    }

    private List<string> Applied()
    {
        var outer = new List<string>();
        for (var scope = CaptureScope; scope != null;)
        {
            if (scope.LookupField("self")?.Value is not FunctionVal caller) break;
            var name = caller switch
            {
                LambdaVal l => l.ParamName,
                NativeClosure nc => nc.ParamName,
                _ => null,
            };
            if (name == null || scope.LookupField(name) is not { } arg) break;
            // 值可能又是个容器/对象,展开会很长也可能自引用 —— 交给深度护栏收住
            outer.Insert(0, name + "=" + ShowDepth.Guard(() => arg.Value.ToString() ?? "()"));
            scope = caller.CaptureScope;
        }

        return outer;
    }
}
