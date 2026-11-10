namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>类对象 —— 一个类就是一个 `ClassVal`。实例仍然是 <see cref="ObjectVal"/>。
///
/// **类对象是一种函数**(`ClassVal : FunctionVal`),所以"能不能调用"落回**类型关系**
/// (是不是函数),和 `IsClass`(沿 `parent` 往上能不能查到 `type`)同一个路子 —— 不必再在 Scope 里
/// 塞一个 `call` 成员、读出来绑成 `BoundCall` 再转发到实例化帧。类对象自己就是那个
/// 可调用的东西,没有中间商。
///
/// `Body` 是**占位**(和 `BoolVal` 同款):实例化要沿祖先链跑各层类体、再调 `init`,
/// 那是帧栈上的异步过程,一个同步的 `Func<RuntimeValue, RuntimeValue>` 表达不了它。
/// 求值器在 `CallInto` 里按 `ClassVal` 这个类型先分派掉(推 ClassInit 帧),
/// 那个 Body 永远不会被读到。
///
/// 类对象的成员(`parent`/`block`/`name`/各层方法/类侧 API)全在自己的成员表里,和别的对象
/// 一样 —— 类对象没有第二张表。给**实例**的成员由 `BuiltinClasses` 按类记着,实例自己那张表
/// 读不到时才按名字取一格(见 <see cref="VirtualScope"/>) —— 所以 `list.Add 2` 从类这一侧
/// 找不到,报的是「类型 'Type' 没有方法 'Add'」,结构决定的,不靠标记。</summary>
public record ClassVal : FunctionVal
{
    /// <param name="classType">元类(创建它的那个类对象)。`null` 表示**自指** ——
    /// 只有根元类 `type` 是这样:建它的时候它还不存在,先自指、之后由 `BuiltinClasses.Link` 回填。</param>
    /// <param name="members">成员表(调用方已建好的那种);`null` = 让 `ObjectVal.Scope` 懒建。</param>
    public ClassVal(ClassVal? classType, Scope? members)
        : base(null!, (_, _) => VoidVal.Instance, members)
    {
        ClassType = classType ?? this;
    }

    /// <summary>这个类型的值**是不是"值类型"**(不可变、按值比、能当字典的键)。
    ///
    /// **它是接口 `IValue` 那支的物化索引**,不是另立一套。写这一格的就两处:
    /// `BuiltinClasses.SealBuiltins`(内置那 10 个值类型)与 `PluginApi`
    /// (`[RavelClass(Implements = "IValue")]`)。**用户代码写不进来** —— `IValue` 封了
    /// (见 `SealBuiltins`),`impl` 那条路进不去;从前 `lib/values.rav` 里那批 `impl` 也撤了。
    ///
    /// 为什么要有这么一格:判据那条路(`CanBeKey`)跑在 `dict.SysGet` / `SysSet` 那些
    /// **同步 C#** 操作里,那儿**拿不到解释器**,问不了 `HasTrait` —— 而为了问一句
    /// 就得把解释器一路传进字典底层,不值当。索引 O(1)、不吃解释器。
    ///
    /// **它和接口必须一起对**:谁往这一格写,`HasTrait (…, IValue)` /
    /// `Implements ()` / `Implementors ()` 三处就一起改了口径(都在 `BuiltinClasses.Interfaces.cs`)。
    /// (从前这是类型树上 `ValueType` 那一支,靠 `IsAssignableTo` 问 —— 那也正是
    /// `bool` / `()` 进不来的原因:单继承链放不下两个正交的分类。)</summary>
    internal bool IsValueLike;

    /// <summary>**这个类给它的实例准备什么** —— 一份成员工厂(见 <see cref="IMemberSet"/>),
    /// 不是表:它只记"名字 → 怎么造",造出来的那一格落在**值自己的成员表**里。
    ///
    /// 每个类自己带着它:内置类在 `BuiltinClasses` 里一句 `X.Members = XMembers.Set;` 挂上,
    /// 用户类在 `Install` 里照父类**按引用**接下(`self.Members = parent.Members`),
    /// 插件类由 `PluginApi` 扫它自己那个装载器类。谁也不去外面那张注册表问。</summary>
    internal IMemberSet? Members { get; set; }


    /// <summary>类对象打印自己的名字(`print C` → `C`,没名字就是 `class`)。
    ///
    /// **这句不能省**:record 会为**每一个** record 类型合成 `ToString`/`PrintMembers`
    /// (覆盖基类那个,包括 `FunctionVal.ToString`)。合成的那个会把所有字段 dump 出来,
    /// 其中 `ClassType` 是个类对象 —— 而 `type` 的元类是它自己,于是无限递归到栈溢出。
    /// 凡是继承树里新加 record,都别忘了这一条。</summary>
    public override string ToString() => DisplayName;
}
