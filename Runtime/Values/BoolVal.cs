namespace Ravel.Runtime;

/// <summary>bool 值。它**必须是 FunctionVal** —— 类型表里 `Bool &lt;: Function`
/// (true/false 可调用:`true {a} {b}` 选一个块跑),值这边不跟上就会出现
/// 「类型系统说有这个方法、值却接不住」:走 inherits 查到 Function 的
/// Name/scope/prepend,拿到 self 是 BoolVal,`(FunctionVal)s` 直接抛
/// InvalidCastException —— 它不是 RuntimeException,Ravel 的 try 接不住,程序被打掉。
/// BlockVal / TypeVal 一直是这么接的,BoolVal 是漏掉的那个。
/// Body 不会被调用:求值器在 CallInto 里按类型分派(见 BoolVal 分支)。</summary>
public record BoolVal(bool Value) : FunctionVal(null!, (_, _) => VoidVal.Instance)
{
    public override ObjectVal Type => BuiltinClasses.Bool;
    public override string ToString() => Value.ToString().ToLower();
}
