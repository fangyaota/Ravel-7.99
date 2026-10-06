namespace Ravel.Runtime;

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
/// 类对象的成员(`parent`/`block`/`name`/各层方法)全在自己的成员表里,和别的对象一样。</summary>
public record ClassVal : FunctionVal
{
    /// <param name="classType">元类(创建它的那个类对象)。`null` 表示**自指** ——
    /// 只有根元类 `type` 是这样:建它的时候它还不存在,先自指、之后由 `BuiltinClasses.Link` 回填。</param>
    /// <param name="members">成员表。</param>
    public ClassVal(ClassVal? classType, Scope members)
        : base(null!, (_, _) => VoidVal.Instance, members)
    {
        ClassType = classType ?? this;
    }

    /// <summary>**给实例的表** —— 「以我为类型的那些值读得到的成员」。
    ///
    /// 一个类对象有**两张表**,分工是结构性的(不用标记、不用过滤):
    ///
    /// - <see cref="ObjectVal.Scope"/>(基类那张)＝**类对象自己**的成员:它是元类 `type` 的
    ///   实例,建类时平铺进来的 `name` / `parent` / `block` / `init` / `==` / `!=`,用户事后
    ///   挂上去的(`C.func := …`),以及类那一侧的 API(`Json.FromString`)。从**类对象**上
    ///   读成员读的就是这张;
    /// - **这一张**＝给它那些**实例**用的成员:内置方法(`Add` / `Map` / `Kind`…)、类运算符、
    ///   序列方法、`GetImplements ()` 这类查询。只有**引擎**往里写
    ///   (`BuiltinClasses.EngineMember`:内置方法 / 类运算符 / 序列方法 / `TraitQuery`;
    ///   用户类体里那句 `+ := f` 也由 `Install` 扫出来落到这儿),只有**实例**读得到
    ///   (`MemberView.LookupInClassChain` 沿 `parent` 链读的就是它)。
    ///
    /// 于是 `list.Add 2` 从**类那一侧**根本找不到(`Add` 不在 `List.Scope` 里,
    /// 再往上也没有),报的是干净的「类型 'Type' 没有方法 'Add'」——从前得靠
    /// `forInstance` + `private` 两个标记去挡,而那两个标记的判据其实就是"住哪张表"。
    ///
    /// 每张表一个类一份、内容是空的直到引擎挂东西,所以便宜;`Copy ()` 也不需要管它
    /// (类对象走 `CopyValue` 的恒等分支,按身份拷)。</summary>
    public Scope InstanceTable { get; } = new();

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

    /// <summary>这张表里**有哪些方法名**(给 `Fields ()` 和类链查找共用同一份判据)。
    ///
    /// 机制名照旧排掉(`IsMethodName`):这张表里理论上不会有它们,留着这条是保险
    /// —— `this` / `parent` 那种被当方法绑到非对象 receiver 上,是会打穿程序的 C# 异常。
    /// 只要 `FunctionVal`:表里本该全是方法,数据成员读出去没法绑 `self`。</summary>
    internal IEnumerable<string> MethodNames => InstanceTable.Variables
        .Where(kv => kv.Value.Value is FunctionVal && ObjectVal.IsMethodName(kv.Key))
        .Select(kv => kv.Key);

    /// <summary>类对象打印自己的名字(`print C` → `C`,没名字就是 `class`)。
    ///
    /// **这句不能省**:record 会为**每一个** record 类型合成 `ToString`/`PrintMembers`
    /// (覆盖基类那个,包括 `FunctionVal.ToString`)。合成的那个会把所有字段 dump 出来,
    /// 其中 `ClassType` 是个类对象 —— 而 `type` 的元类是它自己,于是无限递归到栈溢出。
    /// 凡是继承树里新加 record,都别忘了这一条。</summary>
    public override string ToString() => DisplayName;
}
