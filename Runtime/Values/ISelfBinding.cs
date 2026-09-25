namespace Ravel.Runtime;

/// <summary>自绑定成员:值是"self → 可用的东西",读成员时**必须先把接收者绑上**。
///
/// 内置方法(<see cref="BuiltinMethodVal"/>)和类运算符工厂
/// (<see cref="ClassOperatorFactory"/>)都是这一类 —— 它们住在**类对象的成员表**里,
/// 而实例作用域里放的是类体里定义的 lambda,那些已经捕获好了作用域、原样返回即可。
/// 漏了这条区分的话,`type.Parent ()` 会把未绑定的内置方法当成结果返回(`self` 是 `()`),
/// 而给一个普通 lambda 硬绑会抛 C# 的 InvalidCastException(lambda 的 Body 是占位)。
///
/// **和"同步快路径"不是一回事**:`Binary.cs` 里那三处 `is BuiltinMethodVal` 问的是
/// "绑完能不能直接算",类运算符工厂也要绑、但绑完得推 ClassOp 帧。两个问题混成一个标记
/// 会让 `a + 5` 交回一个"绑好的标记"而不是算出来的数。</summary>
internal interface ISelfBinding;
