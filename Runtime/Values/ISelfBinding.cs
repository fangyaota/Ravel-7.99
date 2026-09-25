namespace Ravel.Runtime;

/// <summary>自绑定成员:值是"self → 可用的东西",读成员时**必须先把接收者绑上**。
///
/// 内置方法(<see cref="BuiltinMethodVal"/>)和类运算符工厂
/// (<see cref="ClassOperatorFactory"/>)都是这一类 —— 它们住在**类对象的 Scope** 里。
/// 而实例 scope 里放的是类体里定义的 lambda,那些已经捕获好了作用域,原样返回即可。
/// 两者都住在 Scope 里之后,这一条区分不能少:漏了的话 `type.Parent ()`
/// 会把未绑定的内置方法当成结果返回(`self` 是 `()`),而不是真去调它。</summary>
internal interface ISelfBinding;
