namespace Ravel.Runtime;

/// <summary>跑**一层类体**时用的作用域 —— 「定义落进实例表,名字按写的地方找」。
///
/// 为什么要它:一个类的各层类体(顶祖先 → 自身)要跑进**同一个**实例表(字段是平铺的,
/// 继承来的也在那一层),可各层**写在哪**可能不同 —— 父类常常写在 `lib/` 另一个作用域里。
/// 一个 `Scope` 只有一个 `Parent`,两件事就打架:
///
///     make := () => { Helper := 7; class { x: int = Helper } }   # 这个类体写在 make 里
///     P := make ()
///     Q ::= class (P) { }        # 子类写在顶层
///     Q ()   →  从前是「未定义的变量 'Helper'」:P 的类体被按 **Q** 的写法处解析了
///
/// 所以每层跑在自己的 `BodyScope` 上:
/// - **词法父 = 这一层的 `CaptureScope`**(它写在哪就看哪)✓;
/// - **`Define` / `DefineOrReplace` 转发给实例表**(`use` / `unsafe` 那些也走定义,一起落回去)✓;
/// - **`LookupHere` 先问实例表本层**(字段优先,和从前一致),没有再 `base`(自己的写法处)✓;
/// - `InstanceScope` 报出实例表,让 `CheckFieldAccess` 的"在不在这个对象的类里"照旧成立
///   (否则类体里读 `private` 会从"读得到"变成"读不到")✓。
///
/// 它自己**不装东西**:`Variables` 永远是空的,块里 `:=` 出来的东西全在实例表上 ——
/// 这正是"平铺"的实现方式,也是为什么类体里的 lambda 捕获它之后,调起来既能读到字段、
/// 又能看见定义处的自由名字。
///
/// **接口体不用它**(见 `StepClassInit` 里那个 `LayerScope`):接口体是"给实现用的",
/// 它和实现块共用一个环境(实现写在哪就在哪解析),那是 `Reparent` 的活。</summary>
internal sealed class BodyScope(Scope lexical, Scope instance) : Scope(lexical)
{
    /// <summary>这一层的定义都落在这张表上(它是那个实例的成员表)。</summary>
    internal Scope Instance => instance;

    internal override Scope? InstanceScope => instance;

    protected override Variable? LookupHere(string name)
        => instance.LookupField(name) ?? base.LookupHere(name);

    public override Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue,
                                     Statement? site = null)
        => instance.Define(name, typeConstraint, initialValue, site);

    public override Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => instance.DefineOrReplace(name, typeConstraint, initialValue);
}
