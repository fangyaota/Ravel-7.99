namespace Ravel.Runtime;

public partial class Interpreter
{
    private const int MaxTrace = 12;

    private Frame _top = null!;
    private RuntimeValue _result = VoidVal.Instance;
    private Scope _rootScope = null!;

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

    private Scope AmbientScope()
    {
        var f = _top;
        while (f is not BlockExecFrame && f.Parent != null) f = f.Parent;
        return f.Scope;
    }

    private void SetAmbientScope(Scope scope)
    {
        var f = _top;
        while (f is not BlockExecFrame && f.Parent != null) f = f.Parent;
        f.Scope = scope;
        if (f.Parent == null) _rootScope = scope;
    }

    internal RuntimeValue? ActiveInstance(Scope? start, int implId)
    {
        for (var s = start; s != null; s = s.Parent)
            if (ActiveAt(s, implId) is { } lexical) return lexical;

        for (var f = _top; f != null; f = f.Parent)
            for (var s = f.Scope; s != null; s = s.Parent)
                if (ActiveAt(s, implId) is { } dynamic) return dynamic;

        return null;
    }

    private static RuntimeValue? ActiveAt(Scope s, int implId)
        => s.LookupField(BuiltinClasses.InstanceActiveMember) is { } vr
           && s.LookupField(BuiltinClasses.ImplActiveMember)?.Value is IntVal id && id.Value == implId
            ? vr.Value      // 接收者可以是**任何值** —— 标量(字符串 / 数 / 字符)也能实现接口
            : null;

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
        catch (SyntaxException sx)
        {
            // `eval`/`using` 会在求值中途碰到语法错误。对 Ravel 层来说那只是一次
            // 「操作失败」,该和运行时错误一样能被 Ex.try 接住——否则 eval 一段用户输入
            // 就能把程序打死(Ravel 的 try 只认 RuntimeException)。
            // 位置用解析器给的那个(比当前帧准),调用栈照常补上。
            var ex = new RuntimeException(sx.Message)
            {
                File = sx.Spot.File, Line = sx.Spot.Line, Column = sx.Spot.Column,
                Trace = StackTraceOf(_top),
            };
            if (HandToRavelHandler(ex)) return;
            throw;                                // 没人接 → 原样冒泡,CLI 按语法错误渲染
        }
    }

    private void RestoreLoadingIfNeeded(RuntimeValue? snap)
    {
        if (snap is null) return;
        if (ReferenceEquals(snap, EmptyLoading) && _loading.Count == 0) return;
        RestoreLoading(snap);
    }

    internal RuntimeValue SnapshotLoading()
    {
        if (_loading.Count == 0) return EmptyLoading;
        return new ListVal([.. _loading.Select(m => (RuntimeValue)new StringVal(m))]);
    }

    private static readonly ListVal EmptyLoading = new([]);

    internal RuntimeValue RestoreLoading(RuntimeValue snap)
    {
        if (snap is not ListVal loading) return VoidVal.Instance;
        if (loading.Elements.Count == 0 && _loading.Count == 0) return VoidVal.Instance;   // 两边都空:没什么可还原

        var keep = new List<string>();
        foreach (var e in loading.Elements)
            if (e is StringVal m) keep.Add(m.Value);

        foreach (var m in _loading)
            if (!keep.Contains(m)) _loaded.Remove(m);

        _loading.Clear();
        for (var i = keep.Count - 1; i >= 0; i--) _loading.Push(keep[i]);

        return VoidVal.Instance;
    }

    internal FunctionVal? ErrorHook
    {
        get => _errorHook;
        set => _errorHook = value;
    }

    private FunctionVal? _errorHook;

    private bool _reporting;

    private RuntimeException? _handed;

    private bool HandToRavelHandler(RuntimeException ex)
    {
        // **重入保护**:库正在处理一个错误时,处理代码自己又出错了 —— 不再回库,
        // 让这次错误一路冒泡到 CLI 渲染。否则处理代码一读成员就再进来一次,成环。
        if (_reporting) return false;
        if (_errorHook is not { } hook) return false;

        _handed = ex;
        _reporting = true;
        try
        {
            // 钩子体内一般会 escape 回 try 的 callcc,所以 sink 取冒泡点即可
            CallInto(_top.Parent ?? _top, hook, BuiltinClasses.NewException(ex.Message, ex.Kind));
        }
        finally
        {
            _reporting = false;
        }
        return true;
    }

    internal RuntimeException? Handed => _handed;

    internal RuntimeValue Unhandled(RuntimeValue e)
    {
        var ex = _handed;
        _handed = null;
        throw ex ?? new RuntimeException(BuiltinClasses.ExceptionMessage(e) ?? Show(e));
    }

    private static (int Line, int Column) ErrorSpot(Frame top)
    {
        for (var f = top; f != null; f = f.Parent)
        {
            // 控制帧自己没位置,但它记着发起它的那个节点 —— 用它更准
            if (f is ControlFrame { CallSite: { } site } && site.Line > 0)
                return (site.Line, site.Column);

            var (line, col) = FrameSpot(f);
            if (line > 0) return (line, col);
        }

        return (0, 0);
    }

    private static RuntimeException Locate(RuntimeException ex, Frame top)
    {
        (ex.Line, ex.Column) = ErrorSpot(top);
        ex.File = NearestSource(top);
        ex.Trace = StackTraceOf(top);
        return ex;
    }

    private static List<string> StackTraceOf(Frame top)
    {
        var trace = new List<string>();
        for (var f = top; f != null; f = f.Parent)
        {
            if (f is not BlockExecFrame bf) continue;
            if (trace.Count >= MaxTrace) continue;
            var (line, col) = FrameSpot(bf);
            if (line == 0) continue;          // 没有位置的块(合成的)不入栈
            trace.Add(bf.Block.Source is { } src ? $"{ErrorReport.ShortPath(src)}:{line}:{col}" : $"{line}:{col}");
        }

        return trace;
    }

    private static string? NearestSource(Frame top)
    {
        for (var f = top; f != null; f = f.Parent)
            if (f is BlockExecFrame { Block.Source: { } src }) return src;
        return null;
    }

    private static (int Line, int Column) FrameSpot(Frame f) => f switch
    {
        NodeFrame nf => (nf.Node.Line, nf.Node.Column),
        BlockExecFrame bf => (bf.Block.Line, bf.Block.Column),
        _ => (0, 0),
    };

    private void Return(Frame f, RuntimeValue v)
    {
        _top = f.Parent == null ? null! : f.Parent.WithResult(v);
        _result = f.Parent == null ? v : _result;
        // 节点帧一 `Return` 就**没人要了**:只有 `_top` 指着它(链上每个帧只有一个孩子),
        // 没被续延拎走过就原样收回 —— 每一步都要一个,而它占步数的 ~79%。
        if (f is NodeFrame nf) Recycle(nf);
    }

    private readonly Stack<NodeFrame> _nodePool = [];

    private void Recycle(NodeFrame nf)
    {
        if (nf.Captured) return;
        nf.Parent = null;
        _nodePool.Push(nf);
    }

    private NodeFrame RentNode(AstNode node, Frame parent)
        => _nodePool.Count > 0 ? _nodePool.Pop().Rent(node, parent) : new NodeFrame(node) { Parent = parent, Scope = parent.Scope };

    private void PushChild(Frame parent, AstNode child) => _top = RentNode(child, parent);

    // ======================== 块执行 ========================

    private void StepBlockExec(BlockExecFrame bf)
    {
        if (bf.Index < bf.Block.Statements.Count)
        {
            // 走到这儿 = **上一条语句刚跑完**(Results[Index-1]),而且后面还有别的语句 ——
            // 也就是说它那一份值是被**丢掉**的。丢的是不是个函数,在这儿看一眼。
            if (bf.Index > 0) WarnIfForgotCall(bf);

            // `ExpressionStatement` 只是个**壳** —— `StepNode` 那一臂做的就是"推给里层、
            // 再**原样**交回来",一次 push 加一次 return 拷贝,什么也没干。**别为它推那一帧**:
            // 一个循环体一万次,那就是一万次白白的分配。
            //
            // 位置没丢:解析器建这个壳时写的就是里层那个表达式的行列(`Parser.Atoms.cs` 三处、
            // `Parser.Holes.cs` 一处),报错指的仍是同一处。
            var stmt = bf.Block.Statements[bf.Index];
            // **游标在这一步推进**(而不是"结果表又长了一格"):推出去的父帧已经是"下一条"了,
            // 这句跑完 `Return` 把值追加到它身上,回到这儿时 `Index` 就是下一条。
            // 没被续延拎走过就**原地推进**(见 `BlockExecFrame.Advance`);帧本身也走池子。
            _top = RentNode(stmt is ExpressionStatement es ? es.Expr : stmt, bf.Advance());
            return;
        }

        var v = bf.Index == 0 ? VoidVal.Instance : bf.Last;
        // 柯里化函数的体:交出去的那个就是**半成品**(还等着下一批实参)
        if (bf.Curried && v is FunctionVal f) f.IsPartial = true;
        Return(bf, v);
    }

    private readonly HashSet<(string?, int, int)> _warnedForgotCall = [];

    private void WarnIfForgotCall(BlockExecFrame bf)
    {
        if (!WarnForgotCall) return;

        var stmt = bf.Block.Statements[bf.Index - 1];
        if (stmt is not ExpressionStatement es) return;
        // 赋值那一类跳过:`a.b = v` / `a.b := v` 在语法上也是 ExpressionStatement(BinaryExpr),
        // 但值是"赋进去的那个"这个副产物 —— 事已经做了(见 OperatorSymbols.AssignOps)
        if (es.Expr is BinaryExpr bin && OperatorSymbols.AssignOps.Contains(bin.Op)) return;
        if (bf.Last is not FunctionVal fn || fn is BoolVal or ContinuationVal) return;
        // 半成品(少给了实参),或"光写了个名字"—— 就这两种
        if (!fn.IsPartial && es.Expr is not (IdentifierExpr or MemberAccess)) return;

        var spot = new SourceSpot(bf.Block.Source, stmt.Line, stmt.Column);
        if (!_warnedForgotCall.Add((spot.File, spot.Line, spot.Column))) return;

        Console.Error.WriteLine(ErrorReport.Warning(
            Messages.T("runtime.call.function_not_invoked", Brief(fn)), spot));
    }

    private static string Brief(FunctionVal fn)
    {
        var sb = new System.Text.StringBuilder(80);
        var gap = false;
        foreach (var c in fn.ToString())
        {
            if (char.IsWhiteSpace(c)) { gap = sb.Length > 0; continue; }
            if (sb.Length >= 80) { sb.Append('…'); break; }
            if (gap) { sb.Append(' '); gap = false; }
            sb.Append(c);
        }

        return sb.ToString();
    }
}
