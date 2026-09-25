namespace Ravel.Runtime;

/// <summary>显式持久帧栈求值器:Step() 循环逐帧推进,C# 栈恒平,递归深度=帧链长度。
/// 本文件只放推进循环本身(取帧/推帧/返回);节点求值见 Interpreter.Nodes.cs,
/// 调用分派见 Interpreter.Call.cs,控制帧见 Interpreter.Control.cs,模块加载见 Interpreter.Modules.cs。</summary>
public partial class Interpreter
{
    /// <summary>调用栈最多列几层——深递归时帧链可能有几十万层</summary>
    private const int MaxTrace = 12;

    private Frame _top = null!;
    private RuntimeValue _result = VoidVal.Instance;
    private Scope _rootScope = null!;

    /// <summary>顶层入口:整个程序一个块根帧(帧链包含剩余语句,callcc 续延才能完整恢复)。scope 用 _rootScope(ravel 模块切换会改它)</summary>
    private RuntimeValue RunStack(Program p)
    {
        _rootScope = _global;
        // 根块的 Source/位置要带上,否则报错时调用栈最后一层是"0:0"
        _top = new BlockExecFrame(new BlockExpr(p.Statements) { Line = 1, Column = 1, Source = p.Source })
        {
            Scope = _rootScope
        };
        _result = VoidVal.Instance;
        while (_top != null) StepOnce();
        return _result;
    }

    /// <summary>切换作用域:顶层改 _rootScope,块内改最近的 BlockExecFrame 的 scope(ravel 模块用)</summary>
    private void SetAmbientScope(Scope scope)
    {
        var f = _top;
        while (f is not BlockExecFrame && f.Parent != null) f = f.Parent;
        f.Scope = scope;
        if (f.Parent == null) _rootScope = scope;
    }

    private void StepOnce()
    {
        CurrentScope = _top.Scope;
        try
        {
            switch (_top)
            {
                case NodeFrame nf: StepNode(nf); break;
                case BlockExecFrame bf: StepBlockExec(bf); break;
                case ControlFrame cf: StepControl(cf); break;
            }
        }
        catch (RuntimeException ex)
        {
            // 只在最内层处理一次:补过位置之后 Located 为真,外层不再覆盖成更外侧的位置
            if (ex.Located) throw;

            Locate(ex, _top);
            if (HandToRavelHandler(ex)) return;   // 交给 Ravel 的 handler,异常到此为止
            throw;                                // 没人接 → 冒泡给 CLI 打报告
        }
    }

    /// <summary>把运行时错误交给 Ravel 层的 handler(`try.rav` 的 handlerStack 栈顶)。
    /// 有 handler 就调它、异常不再冒泡成 C# 异常——这样 `Ex.try { 1 + true } {...}` 也能接住,
    /// 而不是只有显式 `Ex.throw` 才接得住。没有就抛,由 CLI 打带位置和调用栈的报告。</summary>
    private bool HandToRavelHandler(RuntimeException ex)
    {
        // try.rav 第一行就 `ravel "Ex"`,所以 handlerStack 在 Ex 模块里;也接受放全局的写法
        var stack = (_global.TryLookup("Ex")?.Value as ModuleVal)?.ModuleScope.TryLookup("handlerStack")?.Value
                        as ListVal
                    ?? _global.TryLookup("handlerStack")?.Value as ListVal;
        if (stack == null) return false;
        if (stack.Elements.Count == 0) return false;
        if (stack.Elements[0] is not FunctionVal handler) return false;

        // 先把 handler 弹出栈再调它(和 try.rav 的 `handlerStack.Remove 0 e` 一致)。
        // 不弹的话,handler 自己出错时会又被交给同一个 handler,无限递归。
        stack.Elements.RemoveAt(0);

        // handler 体内一般会 escape 回 try 的 callcc,所以 sink 取冒泡点即可
        CallInto(_top.Parent ?? _top, handler, new ExceptionVal(ex.Message));
        return true;
    }

    /// <summary>出错位置:当前正在求值的节点;它没有位置(控制帧代表「一次内建调用」而不是
    /// 源码上的某个点,合成的块也一样)就沿帧链往上找最近一个有位置的。
    /// 不加这个回退的话,`using` 找不到文件、类没有 init 这类错误全都报不出位置——
    /// `Locate` 只补一次,而 StepOnce 不是递归的,没有第二层来兜底。</summary>
    private static (int Line, int Column) ErrorSpot(Frame top)
    {
        for (var f = top; f != null; f = f.Parent)
        {
            var (line, col) = FrameSpot(f);
            if (line > 0) return (line, col);
        }

        return (0, 0);
    }

    /// <summary>给运行时错误补上位置和 Ravel 层调用栈。帧链本身就是调用栈,沿它收集即可。</summary>
    private static RuntimeException Locate(RuntimeException ex, Frame top)
    {
        (ex.Line, ex.Column) = ErrorSpot(top);

        // 文件与调用栈:块帧才代表一次「调用」,节点帧只是栈帧内部的步骤
        var trace = new List<string>();
        string? file = null;
        for (var f = top; f != null; f = f.Parent)
        {
            if (f is not BlockExecFrame bf) continue;
            file ??= bf.Block.Source;
            if (trace.Count >= MaxTrace) continue;
            var (line, col) = FrameSpot(bf);
            if (line == 0) continue;          // eval 之类没有位置的块不入栈
            trace.Add(bf.Block.Source is { } src ? $"{ErrorReport.ShortPath(src)}:{line}:{col}" : $"{line}:{col}");
        }

        ex.File = file;
        ex.Trace = trace;
        return ex;
    }

    /// <summary>帧在源码里的位置。块帧用块本身的位置(≈ 函数定义处)</summary>
    private static (int Line, int Column) FrameSpot(Frame f) => f switch
    {
        NodeFrame nf => (nf.Node.Line, nf.Node.Column),
        BlockExecFrame bf => (bf.Block.Line, bf.Block.Column),
        _ => (0, 0),
    };

    /// <summary>完成帧:把结果交给父帧(父空=顶层完成)</summary>
    private void Return(Frame f, RuntimeValue v)
    {
        _top = f.Parent == null ? null! : f.Parent.WithResult(v);
        _result = f.Parent == null ? v : _result;
    }

    private void PushChild(Frame parent, AstNode child) => _top = new NodeFrame(child) { Parent = parent, Scope = parent.Scope };

    // ======================== 块执行 ========================

    private void StepBlockExec(BlockExecFrame bf)
    {
        if (bf.Count < bf.Block.Statements.Count)
        {
            _top = new NodeFrame(bf.Block.Statements[bf.Count]) { Parent = bf, Scope = bf.Scope };
            return;
        }

        Return(bf, bf.Count == 0 ? VoidVal.Instance : bf.Last);
    }
}
