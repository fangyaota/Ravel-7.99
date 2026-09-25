namespace Ravel.Runtime;

/// <summary>可调用的东西。<see cref="FunctionVal"/>(函数/lambda/代码块/内置方法/…)
/// 和 <see cref="ObjectVal"/>(对象,只要它有 `call` 成员)都实现它,
/// C# 层因此可以把"调用"一视同仁,不必先问"这是个函数还是个对象"。
///
/// **能不能调用在 Ravel 层的判据不止是类型**:对象还要有 `call` 成员
/// (见 <see cref="ObjectVal.HasCall"/>)。这条判据不写死"什么是类",
/// interface / shape 这类"要求某组成员"的概念可以直接建在它上面。</summary>
public interface IFunction
{
    /// <summary>捕获作用域。函数是它定义处的那个,对象是自己的实例 scope。</summary>
    Scope Scope { get; }

    /// <summary>名字。函数的落在自己身上;类对象的落在它 Scope 的 `name` 成员上
    /// (`::=` 命名走的就是这条,见 Interpreter.StepVarDef)。</summary>
    string? Name { get; set; }
}
