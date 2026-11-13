namespace Ravel.Runtime;

internal static class SysModule
{
    public static void Fill(Interpreter self, Scope scope)
    {
        Types(scope);
        Constants(scope);
        Controls(scope);
        SysRegistry.Register(self, (name, fn) => Def(scope, name, BuiltinClasses.Function, fn));
    }

    private static void Def(Scope scope, string name, ObjectVal type, RuntimeValue value) => scope.Define(name, value);

    // 类对象自己就是那个值,不再包一层
    private static void DefType(Scope scope, string name, ObjectVal type) => Def(scope, name, BuiltinClasses.Type, type);

    private static void DefControl(Scope scope, string name, ControlKind kind, int arity)
        => Def(scope, name, BuiltinClasses.Function, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

    private static void Types(Scope scope)
    {
        DefType(scope, "Integer", BuiltinClasses.Int);
        DefType(scope, "String", BuiltinClasses.String);
        DefType(scope, "Char", BuiltinClasses.Char);
        DefType(scope, "Bool", BuiltinClasses.Bool);
        DefType(scope, "Real", BuiltinClasses.Real);
        DefType(scope, "BigInteger", BuiltinClasses.BigInt);
        DefType(scope, "Fraction", BuiltinClasses.Fraction);
        DefType(scope, "BigFraction", BuiltinClasses.BigFraction);
        DefType(scope, "Scope", BuiltinClasses.ScopeType);   // 作用域那个类型
        DefType(scope, "Range", BuiltinClasses.Range);
        DefType(scope, "List", BuiltinClasses.List);
        DefType(scope, "Set", BuiltinClasses.Set);
        DefType(scope, "Dict", BuiltinClasses.Dict);
        DefType(scope, "Pair", BuiltinClasses.Pair);   // 值类型的一对(见 PairVal)
        DefType(scope, "Waitable", BuiltinClasses.Waitable);   // 等待句柄(见 SysTask)
        DefType(scope, "Object", BuiltinClasses.Object);
        // 模块的类对象 —— **所有模块共用这一个**(`Math` / `System` / 各库的模块都是它的
        // 实例,不再是各建一个子类,见 ModuleVal)。所以要问"这是不是个模块"就一句
        // `x is Ravel`。
        DefType(scope, "Ravel", BuiltinClasses.Ravel);
        DefType(scope, "Function", BuiltinClasses.Function);
        DefType(scope, "Continuation", BuiltinClasses.Continuation);   // callcc 交出来的那枚续延
        DefType(scope, "Void", BuiltinClasses.Void);
        DefType(scope, "Type", BuiltinClasses.Type);
        DefType(scope, "Interface", BuiltinClasses.Interface);
        DefType(scope, "IValue", BuiltinClasses.IValue);
        DefType(scope, "BaseInterface", BuiltinClasses.BaseInterface);   // 所有接口的基类
        DefType(scope, "Exception", BuiltinClasses.Exception);
        // 错误那一族 —— 引擎报错时挑的就是它们(见 Runtime/BuiltinClasses.Errors.cs)
        DefType(scope, "TypeError", BuiltinClasses.TypeError);
        DefType(scope, "NameError", BuiltinClasses.NameError);
        DefType(scope, "AttributeError", BuiltinClasses.AttributeError);
        DefType(scope, "IndexError", BuiltinClasses.IndexError);
        DefType(scope, "KeyError", BuiltinClasses.KeyError);
        DefType(scope, "ZeroDivisionError", BuiltinClasses.ZeroDivisionError);
        DefType(scope, "AssertionError", BuiltinClasses.AssertionError);
        DefType(scope, "AccessError", BuiltinClasses.AccessError);
        DefType(scope, "ArgumentError", BuiltinClasses.ArgumentError);
        DefType(scope, "ValueError", BuiltinClasses.ValueError);
        DefType(scope, "IoError", BuiltinClasses.IoError);
        DefType(scope, "RegexError", BuiltinClasses.RegexError);
        DefType(scope, "Json", BuiltinClasses.Json);
    }

    private static void Constants(Scope scope)
    {
        Def(scope, "True", BuiltinClasses.Bool, new BoolVal(true));
        Def(scope, "False", BuiltinClasses.Bool, new BoolVal(false));
        // 特殊浮点值。和 True/False 同款:系统模块里的一个值,`predefined.rav` 给全局别名
        // (`-Inf` 不用另设,一元 `-` 对 Real 就是取负)
        Def(scope, "NaN", BuiltinClasses.Real, new RealVal(double.NaN));
        Def(scope, "Inf", BuiltinClasses.Real, new RealVal(double.PositiveInfinity));
    }

    private static void Controls(Scope scope)
    {
        // if/while/foreach 不在这里——它们在 predefined.rav 用 Ravel 写(靠可调用的 true/false + callcc)
        DefControl(scope, "With", ControlKind.With, 2);
        DefControl(scope, "CallCC", ControlKind.CallCC, 1);
        DefControl(scope, "Using", ControlKind.Using, 1);
        DefControl(scope, "Eval", ControlKind.Eval, 1);
        // **拒收边界**:`try` 拿它把体套一层(见 `lib/exceptions.rav`)—— 里面写的 `reject`
        // 到这儿为止,不再往帧链外面找 `|` 的交替帧。除了这一条,和直接调 `f ()` 没两样。
    }
}
