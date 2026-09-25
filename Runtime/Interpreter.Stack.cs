namespace Ravel.Runtime;

/// <summary>显式持久帧栈求值器:Step() 循环逐帧推进,C# 栈恒平,递归深度=帧链长度。
/// 本文件只放推进循环本身(取帧/推帧/返回);节点求值见 Interpreter.Nodes.cs,
/// 调用分派见 Interpreter.Call.cs,控制帧见 Interpreter.Control.cs,模块加载见 Interpreter.Modules.cs。</summary>
public partial class Interpreter
{
    private Frame _top = null!;
    private RuntimeValue _result = VoidVal.Instance;
    private Scope _rootScope = null!;

    // 续延恢复状态:多发时重跑局部 onDone 到 resumeRoot 完成,然后返回 arg 给调用者
    private AstNode? _resumeStopNode;
    private Frame? _resumeCaller;
    private RuntimeValue _resumeValue = VoidVal.Instance;

    /// <summary>顶层入口:整个程序一个块根帧(帧链包含剩余语句,callcc 续延才能完整恢复)。scope 用 _rootScope(ravel 模块切换会改它)</summary>
    private RuntimeValue RunStack(Program p)
    {
        _rootScope = _global;
        _top = new BlockExecFrame(new BlockExpr(p.Statements)) { Scope = _rootScope };
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
        switch (_top)
        {
            case NodeFrame nf: StepNode(nf); break;
            case BlockExecFrame bf: StepBlockExec(bf); break;
            case ControlFrame cf: StepControl(cf); break;
        }
    }

    /// <summary>完成帧:把结果交给父帧(父空=顶层完成)</summary>
    private void Return(Frame f, RuntimeValue v)
    {
        // 续延恢复:resumeRoot(值消费者帧)完成 → 返回 arg 给调用者,不继续程序
        if (_resumeStopNode != null && f is NodeFrame rnf && rnf.Node == _resumeStopNode)
        {
            _resumeStopNode = null;
            _top = _resumeCaller!.WithResult(_resumeValue);
            return;
        }

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
