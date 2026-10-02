namespace Ravel.Runtime;

/// <summary>字节码 VM —— **旁挂在现有求值器上**,不是替换它。
///
/// 脊梁只有一条:**VM 帧就是"会吃结果的帧"**。引擎推动一步的方式本来就是
/// `_top = f.Parent.WithResult(v)`(把值交给父帧、然后接着跑父帧),而 `VmFrame.WithResult`
/// 的定义就是"收下这个值、接着跑自己" —— 于是 VM 里的每一次调用/块执行,
/// 照抄树遍历那套 `_top = …` 推帧然后返回,被调方收尾时把值交回来,它就活了。
/// **谁编译了、谁没编译,都不影响对方。**
///
/// 第一版只编**块**(`{ … }`,树遍历那边包成 `BlockVal` 的那一批)—— 循环体、
/// `if`/`while` 的条件和体全是块,热的那几格正好都在里头,而且块没有参数,
/// 少掉一整块"绑参数"的麻烦。
///
/// 编译是**整个块编、或者整个不编**:**任何一个节点不认得就整块作废**
/// (见 <see cref="VmBuilder"/>)。半编译状态引出的坑比它省的时间值钱。</summary>
public partial class Interpreter
{
    /// <summary>这块能不能编出来 —— **编过就记住结论**(包括"编不了",挂在 AST 节点上,
    /// 和 `StringLiteral.Packed` 一个路子)。块会被反复执行,不能每次重编。</summary>
    private VmCode? TryCompileBlock(BlockExpr blk)
    {
        if (blk.CompileTried) return blk.Compiled;

        // 「是不是忘了调用」那套警告住在树遍历那一半(`WarnIfForgotCall` 要一个块帧)。
        // 开着 `--warn` 的时候干脆不编 —— 免得同一个程序开不开它行为不一样。
        if (WarnForgotCall) { blk.CompileTried = true; return null; }

        var b = new VmBuilder();
        if (!b.Block(blk)) { blk.CompileTried = true; return null; }

        blk.Compiled = new VmCode
        {
            Ops = [.. b.Ops],
            A = [.. b.A],
            Consts = [.. b.Consts],
            Names = [.. b.Names],
        };
        blk.CompileTried = true;
        return blk.Compiled;
    }

    /// <summary>把一块摊成指令。**任何一个节点不认得,整块作废**(交回 false)。
    /// 白名单很窄,是故意的:先让能编的那部分跑对、跑出数,再谈覆盖率。</summary>
    private sealed class VmBuilder
    {
        public readonly List<Op> Ops = [];
        public readonly List<int> A = [];
        public readonly List<RuntimeValue> Consts = [];
        public readonly List<string> Names = [];

        private int Const(RuntimeValue v) { Consts.Add(v); return Consts.Count - 1; }
        private int Name(string s) { Names.Add(s); return Names.Count - 1; }
        private void Emit(Op op, int a = 0) { Ops.Add(op); A.Add(a); }

        /// <summary>一块:逐条求值,不是最后一条就把值丢掉。**最后一条的值留在栈上**
        /// (块的值 = 最后一条语句的值,和树遍历一个规矩)。</summary>
        public bool Block(BlockExpr blk)
        {
            if (blk.Statements.Count == 0)
            {
                Emit(Op.Const, Const(VoidVal.Instance));    // 空块的值就是 ()
                Emit(Op.Ret);
                return true;
            }

            for (var i = 0; i < blk.Statements.Count; i++)
            {
                if (!Statement(blk.Statements[i])) return false;
                if (i < blk.Statements.Count - 1) Emit(Op.Drop);
            }
            Emit(Op.Ret);                     // 栈上那个就是块的值
            return true;
        }

        private bool Statement(Statement s) => s switch
        {
            ExpressionStatement es => Expr(es.Expr),
            // `x = v` 在**语句位置**是一个 `Assignment` 节点(表达式位置才是 BinaryExpr "=")
            Assignment a => Expr(a.Value) && Store(a.Name),
            // `:=` 那一套(注解、属性、预设类体、`default` 的转换)语义很细,第一版不碰;
            // `x = v` 在语法上是 ExpressionStatement(BinaryExpr "="),不在这儿
            _ => false,
        };

        private bool Expr(Expression e)
        {
            switch (e)
            {
                case NumberLiteral nn: Emit(Op.Const, Const(MakeNumber(nn))); return true;
                case StringLiteral ss: Emit(Op.Const, Const(ss.Packed ??= new StringVal(ss.Value))); return true;
                case CharLiteral cc: Emit(Op.Const, Const(new CharVal(cc.Value))); return true;
                case VoidLiteral: Emit(Op.Const, Const(VoidVal.Instance)); return true;
                case LiteralExpr le: Emit(Op.Const, Const(le.Value)); return true;
                case IdentifierExpr id: Emit(Op.Load, Name(id.Name)); return true;

                // `x = v`(变量赋值在表达式位置也成立,见 Interpreter.Binary)
                case BinaryExpr { Op: "=" } bin when bin.Left is IdentifierExpr t:
                    return Expr(bin.Right) && Store(t.Name);

                // 赋值那一族其余形状(成员赋值、复合赋值、`:=`)一概不收 ——
                // `:=` 在表达式位置本来是**语法错**(见 OperatorSymbols.AssignOps 的用处),
                // 收进来就会被当成一个普通运算符去绑定,报出「Integer 不支持运算符 ':='」这种
                // 指错方向的错。不收,那条错就照旧由原来那一层报。
                case BinaryExpr { Left: MemberAccess }: return false;
                case BinaryExpr bin when OperatorSymbols.AssignOps.Contains(bin.Op): return false;

                // `&&` / `||` **不是普通运算符** —— 树遍历那边单独走 StepShortCircuit
                // (短路、而且两边是延迟的),照普通二元算会报「Bool 不支持运算符 '&&'」。
                // 第一版不收,留给树遍历。
                case BinaryExpr { Op: "&&" or "||" }: return false;

                case BinaryExpr bin:
                    return Expr(bin.Left) && Expr(bin.Right) && Bin(bin.Op);

                case CallExpr call:
                    if (!Expr(call.Function)) return false;
                    if (!Expr(call.Argument)) return false;
                    Emit(Op.Call);
                    return true;

                default: return false;
            }
        }

        private bool Bin(string op) { Emit(Op.Bin, Name(op)); return true; }
        private bool Store(string name) { Emit(Op.Store, Name(name)); return true; }
    }

    /// <summary>把一条帧链拍成**只读快照** —— 续延那条路上两个地方要用(捕获、恢复)。
    ///
    /// 别的帧是持久化 record,`with` 一出就是新的一份,所以捕获整条链天然安全;
    /// **VM 帧可变**,可变的东西不能直接进捕获链:捕获进去的那一份要能反复跳(多发射),
    /// 而活的那一份还要接着改。所以链上**逐个 VM 帧克隆一份**,别的帧原样带走。
    ///
    /// 恢复时也要拍一次 —— 捕获链是只读模板,每次跳都该从同一份状态出发。
    ///
    /// 链上没有 VM 帧时**一个对象都不分配**(原样交回,递归只是走一遍) ——
    /// 绝大多数 `callcc` 都是这种。</summary>
    private static Frame Snap(Frame f)
    {
        var parent = f.Parent is null ? null : Snap(f.Parent);
        if (f is not VmFrame && ReferenceEquals(parent, f.Parent)) return f;
        return f is VmFrame vf ? vf.Snapshot(parent) : f with { Parent = parent };
    }

    // ==================== 跑 ====================

    private static void Push(VmFrame vf, RuntimeValue v)
    {
        if (vf.Sp == vf.Stack.Length) Array.Resize(ref vf.Stack, vf.Sp == 0 ? 4 : vf.Sp * 2);
        vf.Stack[vf.Sp++] = v;
    }

    private static RuntimeValue Pop(VmFrame vf) => vf.Stack[--vf.Sp];

    /// <summary>推进一步 —— 一路跑到底,只在"得推别人的帧"时才交回帧循环。
    ///
    /// 每条指令都**先把 `Ip` 推到下一条**,再决定要不要交回控制;这样被调方返回时
    /// (`vf.WithResult` 把值压栈并交回本帧)从正确的下一条接着跑 —— 和树遍历里
    /// `nf.Count` 那个"阶段"是同一回事。</summary>
    private void StepVm(VmFrame vf)
    {
        while (true)
        {
            var code = vf.Code;
            var ip = vf.Ip;

            if (ip >= code.Length)
                throw new RuntimeException(
                    $"VM 帧跑过了最后一条指令(ip={ip}, 长度={code.Length}, 栈深={vf.Sp}) —— 少了一条 Ret");

            switch (code.Ops[ip])
            {
                case Op.Const:
                    vf.Ip = ip + 1;
                    Push(vf, code.Consts[code.A[ip]]);
                    continue;

                case Op.Drop:
                    vf.Ip = ip + 1;
                    vf.Sp--;
                    continue;

                case Op.Load:
                {
                    var name = code.Names[code.A[ip]];
                    var v = vf.Scope.LookupVar(name)
                        ?? throw new RuntimeException($"未定义的变量 '{name}'", ErrorKind.Name);
                    BoxedValue.GateRead(v, name, this);          // 和成员访问共用一套门禁
                    vf.Ip = ip + 1;
                    if (v.HasAttr(Attr.By))
                    {
                        // `by` 属性:读要走 getter。推它,值回来时本帧接着跑
                        CallInto(vf, PropertyGetter(v.Value, name), VoidVal.Instance);
                        return;
                    }
                    Push(vf, v.Value);
                    continue;
                }

                case Op.Store:
                    StepVmStore(vf, code.Names[code.A[ip]], ip);
                    if (!ReferenceEquals(_top, vf)) return;
                    continue;

                case Op.Bin:
                {
                    var right = Pop(vf);
                    var left = Pop(vf);
                    var op = code.Names[code.A[ip]];
                    vf.Ip = ip + 1;

                    var (fn, builtin) = BindOperator(left, op);

                    // 判定类运算符的接口兜底(和 StepBinaryOp 里那一段同一个判据)
                    if (builtin && right is ObjectVal rt)
                    {
                        if (op is "is" or "isnot")
                        {
                            Push(vf, new BoolVal(op == "is"
                                ? left.Type.IsAssignableTo(rt) || BuiltinClasses.HasTrait(this, left.Type, rt)
                                : !(left.Type.IsAssignableTo(rt) || BuiltinClasses.HasTrait(this, left.Type, rt))));
                            continue;
                        }
                        if (op is "<:" or ":>" && left is ObjectVal { IsClass: true } lt && rt.IsClass)
                        {
                            var (x, y) = op == "<:" ? (lt, rt) : (rt, lt);
                            Push(vf, new BoolVal(x.IsAssignableTo(y) || BuiltinClasses.HasTrait(this, x, y)));
                            continue;
                        }
                    }

                    if (fn is BuiltinMethodVal bm) { Push(vf, bm.Impl(left, right)); continue; }
                    CallInto(vf, BindSelf(fn, left), right);
                    if (!ReferenceEquals(_top, vf)) return;
                    continue;
                }

                case Op.Call:
                {
                    var arg = Pop(vf);
                    var fn = Pop(vf);
                    vf.Ip = ip + 1;
                    CallInto(vf, fn, arg);
                    // 同步算完的(`FunctionVal` 那一臂)会把值压回本帧,那就接着跑;
                    // 推了别的帧 / 跳了续延的话,交给帧循环
                    if (!ReferenceEquals(_top, vf)) return;
                    continue;
                }

                case Op.Ret:
                {
                    var v = Pop(vf);
                    vf.Ip = ip + 1;
                    Return(vf, v);
                    return;
                }

                default:
                    throw new RuntimeException($"VM 里没接上的指令: {code.Ops[ip]}");
            }
        }
    }

    /// <summary>写一个变量 —— 和 `WriteVariable` 同一套规矩(`by` 属性要走 setter,
    /// 其余直接落),只是收尾把值压回 VM 帧而不是 `Return` 给父帧。</summary>
    private void StepVmStore(VmFrame vf, string name, int ip)
    {
        var val = Pop(vf);
        vf.Ip = ip + 1;

        var field = vf.Scope.LookupVar(name);
        if (field is null)
        {
            vf.Scope.Assign(name, val, ViaTrait(val));           // 未定义 → 它负责报错
            Push(vf, val);
            return;
        }

        if (field.HasAttr(Attr.Core) && !IsUnsafe)
            throw new RuntimeException($"字段 '{name}' 是核心字段，需要 unsafe", ErrorKind.Access);

        if (field.HasAttr(Attr.By))
        {
            field.CheckWritable();
            field.CheckAssignable(val, ViaTrait(val));
            PushCallReturn(vf, PropertySetter(field.Value, field.Name), val, val);
            return;
        }

        field.Assign(val, ViaTrait(val));
        Push(vf, val);
    }
}
