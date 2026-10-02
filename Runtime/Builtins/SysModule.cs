namespace Ravel.Runtime;

/// <summary>`System` 模块的**装配**:三张"数据"表(类型别名 / 常量 / 控制内建)+ 把 `[Sys]`
/// 那一族(散在 `Runtime/Builtins/*.cs` 里的那些类)登记进来。
///
/// 它是唯一「用 C# 写死」的模块(其余模块都来自 .rav 文件),而且**启动就有** ——
/// 不像 `Math` 那样要显式 `using "math.rav"` 才有(那个走的是插件那条路,
/// 成员由官方扩展在 `using` 时装进来)。
///
/// **数据明着列、函数走特性**:类型别名和常量一眼看全比撒在各处好读,
/// 而函数是代码,一个方法一个家(`[Sys]`,见 <see cref="SysAttribute"/>)。
///
/// 和 `Math` 的对称处:`FillMath (scope)` 也是"把成员填进这个作用域"这一个签名 ——
/// 两条路的落点是同一件事。</summary>
internal static class SysModule
{
    /// <summary>往 `scope` 里填满整个 `System` 模块。`self` 只交给那几个要碰引擎状态的
    /// 内置(错误钩子、当前作用域、模块加载状态…),别的方法都是纯函数,用不着它。</summary>
    public static void Fill(Interpreter self, Scope scope)
    {
        Types(scope);
        Constants(scope);
        Controls(scope);
        SysRegistry.Register(self, (name, fn) => Def(scope, name, BuiltinClasses.Function, fn));
    }

    private static void Def(Scope scope, string name, ObjectVal type, RuntimeValue value) => scope.Define(name, type, value);

    // 类对象自己就是那个值,不再包一层
    private static void DefType(Scope scope, string name, ObjectVal type) => Def(scope, name, BuiltinClasses.Type, type);

    private static void DefControl(Scope scope, string name, ControlKind kind, int arity)
        => Def(scope, name, BuiltinClasses.Function, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

    /// <summary>类型与错误族 —— `System.Integer` 那些**权威名**(小写别名在 predefined.rav)</summary>
    private static void Types(Scope scope)
    {
        DefType(scope, "Integer", BuiltinClasses.Int);
        DefType(scope, "String", BuiltinClasses.String);
        DefType(scope, "Char", BuiltinClasses.Char);
        DefType(scope, "Bool", BuiltinClasses.Bool);
        DefType(scope, "Float", BuiltinClasses.Float);
        DefType(scope, "BigInteger", BuiltinClasses.BigInt);
        DefType(scope, "Fraction", BuiltinClasses.Fraction);
        DefType(scope, "BigFraction", BuiltinClasses.BigFraction);
        DefType(scope, "Scope", BuiltinClasses.ScopeType);   // 作用域那个类型
        DefType(scope, "Range", BuiltinClasses.Range);
        DefType(scope, "List", BuiltinClasses.List);
        DefType(scope, "Set", BuiltinClasses.Set);
        DefType(scope, "Dict", BuiltinClasses.Dict);
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
        DefType(scope, "BaseInterface", BuiltinClasses.BaseInterface);   // 所有接口的基类
        DefType(scope, "ValueType", BuiltinClasses.ValueType);
        DefType(scope, "Any", BuiltinClasses.Any);
        DefType(scope, "Every", BuiltinClasses.Every);
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

    /// <summary>常量(那几个"顺手"的建库钩子现在都有自己的家了 —— 见 `SysCore`)</summary>
    private static void Constants(Scope scope)
    {
        Def(scope, "True", BuiltinClasses.Bool, new BoolVal(true));
        Def(scope, "False", BuiltinClasses.Bool, new BoolVal(false));
        // 特殊浮点值。和 True/False 同款:系统模块里的一个值,`predefined.rav` 给全局别名
        // (`-Inf` 不用另设,一元 `-` 对 Float 就是取负)
        Def(scope, "NaN", BuiltinClasses.Float, new FloatVal(double.NaN));
        Def(scope, "Inf", BuiltinClasses.Float, new FloatVal(double.PositiveInfinity));
        Def(scope, "Default", BuiltinClasses.Every, DefaultVal.Instance);
    }

    /// <summary>控制内建 —— 收满参数后由求值器推控制帧</summary>
    private static void Controls(Scope scope)
    {
        // if/while/foreach 不在这里——它们在 predefined.rav 用 Ravel 写(靠可调用的 true/false + callcc)
        DefControl(scope, "With", ControlKind.With, 2);
        DefControl(scope, "CallCC", ControlKind.CallCC, 1);
        DefControl(scope, "Using", ControlKind.Using, 1);
        DefControl(scope, "Eval", ControlKind.Eval, 1);
    }
}
