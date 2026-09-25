namespace Ravel.Runtime;

/// <summary>类对象 —— 一个类就是一个 `ClassVal`。实例仍然是 <see cref="ObjectVal"/>。
///
/// **类对象是一种函数**(`ClassVal : FunctionVal`),所以"能不能调用"落回**类型关系**
/// (是不是函数),和 `IsClass`(元类链上有没有 `type`)同一个路子 —— 不必再在 Scope 里
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

    /// <summary>类对象打印自己的名字(`print C` → `C`,没名字就是 `class`)。
    ///
    /// **这句不能省**:record 会为**每一个** record 类型合成 `ToString`/`PrintMembers`
    /// (覆盖基类那个,包括 `FunctionVal.ToString`)。合成的那个会把所有字段 dump 出来,
    /// 其中 `ClassType` 是个类对象 —— 而 `type` 的元类是它自己,于是无限递归到栈溢出。
    /// 凡是继承树里新加 record,都别忘了这一条。</summary>
    public override string ToString() => DisplayName;
}
