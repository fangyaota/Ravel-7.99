namespace Ravel.Runtime;

public class Variable(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
{
    public string Name { get; } = name;
    /// <summary>落下这个变量的**那条定义语句**(AST 节点)。语言层的 `:=` 会填它,
    /// 于是"同一个 scope 里同名再定义"能分辨两种情形:
    /// 同一个节点**重跑**(callcc 的续延重入 —— `while` 的 `cond := c ()` 就靠这个,
    /// 见 `lib/predefined.rav`)是允许的;另一个节点写同名才是错。
    /// 引擎内部直接调 `Define` 的那些(装类、绑 `this`…)不填,按老规矩覆盖。</summary>
    public Statement? Site { get; internal set; }
    /// <summary>类型约束。可写:BuiltinClasses 建树分两趟,String/Object 这些类对象
    /// 在第二趟才存在,那时才能给 parent/name 这些机制成员挂上约束。</summary>
    public ObjectVal TypeConstraint { get; internal set; } = typeConstraint;
    public RuntimeValue Value { get; private set; } = initialValue;
    private readonly HashSet<string> _attrs = [];

    public bool HasAttr(string a) => _attrs.Contains(a);
    public void SetAttr(string a) => _attrs.Add(a);
    public IEnumerable<string> Attrs => _attrs;

    public void Assign(RuntimeValue newValue, Func<ObjectVal, bool>? alsoAccepts = null)
    {
        CheckWritable();
        CheckAssignable(newValue, alsoAccepts);
        Value = newValue;
    }

    /// <summary>这个变量能不能写。**每个写入点都得问它一次** ——
    /// `by` 属性的赋值走的是 setter(`WriteVariable` / `StepByCompoundAssign` 推的那帧),
    /// 不经过 <see cref="Assign"/>;漏了的话 `readonly` 在属性上就是句空话。
    /// (从前 `Assign` 和 <see cref="ReplaceSlot"/> 各抄了一遍那句消息。)</summary>
    public void CheckWritable()
    {
        if (HasAttr(Attr.Readonly)) throw new RuntimeException("无法给只读变量 '" + Name + "' 赋值");
    }

    /// <summary>换掉槽里的东西(`by a = X`)。
    ///
    /// **不走 setter,也不查类型约束** —— 那个约束管的是"写进属性的值"(`by a: int = …`),
    /// 而这里换的是**属性本身**(一份 property 当然不是 int)。
    /// `by` 标记留着:`a` 之后照样走新的 getter/setter。
    /// readonly 照样挡(只读的是这个槽,不只是它的值)。</summary>
    public void ReplaceSlot(RuntimeValue newValue)
    {
        CheckWritable();
        Value = newValue;
    }

    /// <summary>值能不能写进这个变量(按类型约束)。
    ///
    /// 单独的,因为 **`by` 属性的写入也要过这一关**:`by n: int = property …` 里那个注解
    /// 管的就是"谁能写进来",而那条路不经过 <see cref="Assign"/>(它调的是 setter)。
    /// 两处各写一份措辞,迟早分叉。
    ///
    /// `alsoAccepts` 是**要当前作用域才能回答**的那半:接口不在继承链上,但当前作用域里
    /// 有生效中的实现把它接到这个值上时,注解也该收下(判据见 `BuiltinClasses.HasTrait`,
    /// 和 `x is T` 同一个)。Variable 拿不到解释器,所以那一半由调用方递进来;
    /// **名义链够不着时才问它**,不传就照旧只按名义链判。</summary>
    public void CheckAssignable(RuntimeValue newValue, Func<ObjectVal, bool>? alsoAccepts = null)
    {
        if (TypeConstraint == BuiltinClasses.Any || newValue.Type.IsAssignableTo(TypeConstraint)) return;
        if (alsoAccepts != null && alsoAccepts(TypeConstraint)) return;
        throw new RuntimeException(
            "类型错误: 无法将 " + newValue.Type + " 赋值给 '" + Name + "' (声明为 " + TypeConstraint + ")");
    }

    public override string ToString() => Name + ": " + TypeConstraint + " = " + Value;
}
