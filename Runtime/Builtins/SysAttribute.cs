namespace Ravel.Runtime;

using System.Reflection;

/// <summary>把一个方法登记成 `System` 里的内置函数:**方法上挂 `[Sys("名字")]`,扫一遍自己捡**。
///
/// 规矩就这么几条:
/// <list type="bullet">
/// <item>方法一律 **`static`** —— 装内置的那一族类**不持有状态**(状态在解释器身上);</item>
/// <item>**返回 `RuntimeValue`**,收 **1~3 个** `RuntimeValue`(参数个数由签名定,特性里不写 arity);
///   柯里化和"半成品"标记都交给 `FunctionVal` 那几个工厂;</item>
/// <item>要用引擎的(碰作用域、错误钩子、模块加载状态…)就把它当**第一个参数**收 ——
///   `[Sys("Unsafe")] static RuntimeValue Unsafe(Interpreter self, RuntimeValue _)`;
///   用不着的别写,那几个方法就是**纯函数**。</item>
/// </list>
///
/// **类型别名 / 常量 / 控制内建不走这条路**(它们在 `SysModule` 里明着列):那是**数据**,
/// 一眼看全的一张表比撒在各处的声明好读;函数是**代码**,一个方法一个家。</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SysAttribute(string name) : Attribute
{
    /// <summary>`System.xxx` 那个名字(方法名只管给 C# 看,不必和它对上)</summary>
    public string Name { get; } = name;
}
