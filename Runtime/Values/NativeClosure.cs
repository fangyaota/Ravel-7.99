namespace Ravel.Runtime;

/// <summary>原生闭包:像 lambda 一样在**调用点的作用域**里跑,但体是 C#。
///
/// 内置类的预设类体需要它:`type` 的默认建类函数要读写**正在构造的那个对象**的
/// `this`(把 parent/block 装上去),而 `this` 只存在于实例作用域里。
/// 普通的 `FunctionVal` 拿不到调用点作用域(它的 Scope 是固定的),
/// 而 `LambdaVal` 的体必须是 Ravel 代码块。这个补上中间那格。
///
/// 分派走 `CallInto`(和 LambdaVal 一样自己推作用域),自身不驱动求值。</summary>
public sealed record NativeClosure(string ParamName, ObjectVal ParamType, Func<Scope, RuntimeValue, RuntimeValue> Fn)
    : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    /// <summary>盖掉 record 的自动 dump</summary>
    public override string ToString() => base.ToString();
}
