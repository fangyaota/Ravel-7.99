namespace Ravel.Runtime;

/// <summary>用户 lambda:调用时推 body 帧(不再用闭包 rawBody)。CaptureScope=捕获的闭包作用域;Block=lambda 体。
///
/// `ParamType` 是**解析后**的类对象(参数检查用它),`ParamTypeName` 是**源码里写的**那个名字
/// (`int` 而不是 `Integer`)—— 打印函数时要把代码还原成作者写的样子。</summary>
public sealed record LambdaVal(string ParamName, string ParamTypeName, ObjectVal ParamType, BlockExpr Block)
    : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>打印成**它的代码** + **柯里化已经收下的实参**:
    ///
    ///     <function (a: int b: int) => { a + b; }>
    ///     <function (b: int) => { a + b; } applied a=1>      ← 只喂了 a
    ///     <function add (x: int) => { x; }>               ← `::=` 命名的带上名字
    ///
    /// 签名里是**还等着**的参数,已经收下的放在 `已收` 后面 —— 两者合起来正好是完整的形状。
    /// 没写 `(a: int b: int) => …` 那种完整签名,是因为链外面那几层的参数类型只能从**运行时的值**
    /// 反推(`Integer`),而这层能拿到源码里写的名字(`int`);混在一张表里会出现两套叫法。
    ///
    /// `applied` 从哪来:每次调用 lambda 都会在调用点作用域里落下 `self`(被调的那个函数)
    /// 和 `参数名`(这次收到的实参),而内层 lambda 捕获的正是那个作用域 ——
    /// 所以顺着 `CaptureScope` 往外走就能把喂过的实参一个个捡回来。
    ///
    /// 打印出来的**全是 ASCII** —— 值的形式是数据,不该混进中文;中文只留给报错文案。
/// record 自动生成的 ToString 会把 Body/CaptureScope/Block 这些实现细节全 dump 出来,
    /// 盖掉基类的 `<function>`;显式盖回来。</summary>
    public override string ToString()
        => "<function " + (Name is { Length: > 0 } n ? n + " " : "") + Describe() + ">";

    /// <summary>「签名 + 已收的实参」那一段(`<function …>` 里面那个)。半成品构造器也用它拼。</summary>
    internal string Describe()
    {
        var text = AstPrinter.Signature(this);
        var applied = Applied();
        if (applied.Count > 0) text += " applied " + string.Join(" ", applied);
        return text;
    }

    /// <summary>已经喂进来的实参,由外到内。见类文档里那条 `self`/参数名 的线索。</summary>
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
