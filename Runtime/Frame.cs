namespace Ravel.Runtime;

/// <summary>显式调用栈帧——Parent/Results 不可变(持久化,供 callcc 捕获还原),Scope 可变(ravel 模块切换)</summary>
public abstract record Frame
{
    public Frame? Parent { get; init; }

    /// <summary>发起这个帧的那个 AST 节点。控制帧自己没有位置(它代表"一次内建调用"),
    /// 报错时靠它指回源码里的调用点 —— 否则 `fraction 1 0` 这类错只能报到最外层的块。</summary>
    public AstNode? CallSite { get; init; }
    public Scope Scope { get; set; } = null!;
    public RList<RuntimeValue> Results { get; internal set; } = RList<RuntimeValue>.Empty;
    public int Count => Results.Count;
    public RuntimeValue Result(int i) => Results.At(i);
    public RuntimeValue Last => Results.Last;

    /// <summary>这一帧**被续延拎走过**吗 —— 拎走过就**冻结**:往后一律拷新的,不再原地改。
    ///
    /// 为什么要有这一格:帧从前是**纯持久**的 —— 每次改都 `with` 拷一份,于是"某一帧被
    /// 续延拍进模板之后不会被后来的执行弄脏"白拿。可拷一份就是一次分配,而
    /// `Return` / 块推进**每一步**都在拷(量过:一步 ≈ 一个帧对象 + 一个 `RList` 节点,
    /// 约 115 字节,是整个解释器分配的大头)。
    ///
    /// 改成"没被拎走过就原地改"之后,**唯一**会把帧留到它那一趟之外的就只剩
    /// `ContinuationVal.Captured`(全引擎就这一处 —— 见 `StepCallCC`)。
    /// 标记要**沿链传染到根**:恢复续延是从捕获点**往上**走的,会读到那些祖先。
    /// (从前那条"帧不可变"的注释见 CONTEXT「求值器架构」;`Scope` 那一格本来就可变,
    /// 是"ravel 模块切换"要的。)</summary>
    internal bool Captured;

    /// <summary>往这一帧挂一个结果。**没被拎走过就原地改**(省掉一次帧拷贝),
    /// 被拎走过的照旧拷一份新的 —— 那一份是续延要用的模板,不能脏。</summary>
    public Frame WithResult(RuntimeValue v)
    {
        if (Captured) return this with { Results = Results.Add(v) };
        Results = Results.Add(v);
        return this;
    }
}

/// <summary>求值一个 AST 节点(语句/表达式)</summary>
public record NodeFrame(AstNode Node) : Frame;

/// <summary>执行一个代码块:逐语句求值,**`Index` = 下一条要跑的那句**(0 基);`Results` 只存
/// 跑完的那些值(最后一条就是块的值)。
///
/// **位置是存在帧里的,不是从 `Results.Count` 推出来的** —— 从前是后者,而那样一来
/// "往这个帧上追加一个结果"就等于"块往前走一句",两件事被绑死在一起;**续延恢复**干的
/// 正是前者(`_top = k.Captured.WithResult(arg)`,见 `Interpreter.Call.cs`),
/// 于是"恢复"和"推语句"共用同一个动作。分开之后谁也不会顺手把游标带跑。
/// (别的语言里位置本来就是显式的:C# `async` 编译出来的状态机、Go/Lua 的协程栈都是。)
///
/// **这一条本身不改可观察行为** —— 2026-10-02 拿 11 个用例对过(含当时那版调度器的并发
/// 场景),改前改后**逐字相同**;别把这条当成某个 bug 的修复。
///
/// 那一天那版调度器(`Task.Yield ()` 那种"任务主动让出")确实挂着一处「值 0 不是函数」,
/// 原因在别处(任务与调度器共用同一条帧链),这个游标救不了它。现在的 `lib/tasks.rav`
/// **没有"主动让出"这个挂起点** —— 任务只在 `group.Await` 上停,而那一条路实测没出过事。
/// 这不是这个游标的功劳,是挂起点的形状变了;哪天要加回"主动让出",先回来看这一条。</summary>
public record BlockExecFrame(BlockExpr Block) : Frame
{
    /// <summary>下一条要跑的语句在 `Block.Statements` 里的下标。**只由 `StepBlockExec` 推进** ——
    /// 推语句的时候顺手把游标往前走一格(见 <see cref="Advance"/>),别的路一概不动它。</summary>
    public int Index { get; internal set; }

    /// <summary>游标往前走一格,返回"往后那句该挂在哪个父帧下"。
    /// **没被续延拎走过就原地改**(省掉一次拷贝),拎走过就拷一份 —— 和
    /// <see cref="Frame.WithResult"/> 一个道理。这条落在最热的一格上:块里每推一句都走一次。</summary>
    internal BlockExecFrame Advance()
    {
        if (Captured) return this with { Index = Index + 1 };
        Index++;
        return this;
    }

    /// <summary>这个块是**柯里化函数**的体(见 `BlockExpr.Curried`)—— 它交出去的那个函数
    /// 是个半成品,收尾时顺手打上标(见 `FunctionVal.IsPartial`)。调用方只有 `CallInto`。</summary>
    public bool Curried { get; init; }
}

/// <summary>控制帧:while/if/with/foreach/callcc/using/eval 的状态机。Args=收集的块/参数,State=循环累积值</summary>
public record ControlFrame(ControlKind Kind, RList<RuntimeValue> Args, RuntimeValue State) : Frame
{
    /// <summary>取第 i 个已收集的参数并收成 T,`owner` 是这一帧属于谁(报错文案用)。
    ///
    /// 控制帧的参数是按位置存的裸 RList,每个 Kind 各参数的含义只写在对应的 Step* 里。
    /// 从前一律写成 `(BlockVal)cf.Args.At(1)` —— 调用方给错类型(`with 5 6`)就抛
    /// C# 的 InvalidCastException,它不是 RuntimeException,Ravel 层的 `Ex.try` 接不住,
    /// 一路漏到顶层把程序打掉。走这里就成了正常的 Ravel 错误。</summary>
    public T Arg<T>(int i, string owner) where T : RuntimeValue
    {
        var v = Args.At(i);
        return v as T ?? throw new RuntimeException(
            $"{owner} 的第 {i + 1} 个参数需要{ArgNames.Of(typeof(T))}，得到 {v.Type}", ErrorKind.Type);
    }
}

/// <summary>C# 值类型 → 报错文案里该说的 Ravel 说法。**全库只此一份** ——
/// `Frame.Arg&lt;T&gt;`(「第 N 个参数需要 X」)和 `Interpreter.As&lt;T&gt;`(「X 需要 Y」)共用。
///
/// 拉丁名带一个**前导空格**、中文名不带:两处的模板都是 `需要{名字}`,这样拼出来
/// 「需要 string」和「需要对象」都自然。从前这是两张平行的表(另一张在 Interpreter.cs,
/// 叫 `RavelName`),措辞各写一套,还会把 `ClassVal` 这种 C# 名字漏进消息里。</summary>
internal static class ArgNames
{
    public static string Of(Type t) => t switch
    {
        _ when t == typeof(BlockVal) => "代码块",
        _ when t == typeof(StringVal) => " string",
        _ when t == typeof(IntVal) => " int",
        _ when t == typeof(ListVal) => " list",
        _ when t == typeof(DictVal) => " dict",
        _ when t == typeof(ObjectVal) => "对象",
        _ when t == typeof(BoolVal) => " bool",
        _ when t == typeof(ClassVal) => "类",        // 比 FunctionVal 更具体,要排在它前面
        _ when t == typeof(FunctionVal) => "函数",
        _ => "值",
    };
}
