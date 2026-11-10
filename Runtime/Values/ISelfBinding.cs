namespace Ravel.Runtime;

/// <summary>自绑定成员:值是"self → 可用的东西",读成员时**必须先把接收者绑上**。
///
/// 内置方法(<see cref="BuiltinMethodVal"/>)、类型查询(<see cref="TraitQuery"/>)和
/// 容器的高阶序列方法(<see cref="SeqMethod"/>)都是这一类 —— 它们住在**类型的成员工厂**里,
/// 而实例作用域里放的是类体里定义的 lambda,那些已经捕获好了作用域、原样返回即可。
/// 漏了这条区分的话,`C.Fields ()` 会把未绑定的内置方法当成结果返回(`self` 是 `()`),
/// 而给一个普通 lambda 硬绑会抛 C# 的 InvalidCastException(lambda 的 Body 是占位)。
///
/// **和"同步快路径"不是一回事**:`Binary.cs` 里那几处 `is BuiltinMethodVal` 问的是
/// "绑完能不能直接算",而 `TraitQuery` / `SeqMethod` 也要绑、但绑完是推控制帧的标记。
/// 两个问题混成一个标记会让 `a + 5` 交回一个"绑好的标记"而不是算出来的数。</summary>
internal interface ISelfBinding;
