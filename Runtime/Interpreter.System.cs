namespace Ravel.Runtime;

/// <summary>内置注册:建 System 模块,填入类型别名、控制内建和核心函数。
/// 加一个内置 = 在对应分组里加一行(DefType/DefFn/DefControl/Def)。</summary>
public partial class Interpreter
{
    private void RegisterBuiltins()
    {
        var systemModule = BuildSystemModule();
        _modules["System"] = systemModule;
        _global.Define("System", systemModule.Type, systemModule);
    }

    /// <summary>组装 System 模块——它是唯一「用 C# 写死」的模块,其余模块都来自 .rav 文件</summary>
    private ModuleVal BuildSystemModule()
    {
        var moduleType = BuiltinClasses.NewModuleClass("System", BuiltinClasses.Ravel);
        var module = new ModuleVal(moduleType, new Scope(_global));
        var scope = module.ModuleScope;

        void Def(string name, ObjectVal type, RuntimeValue value) => scope.Define(name, type, value);
        // 类对象自己就是那个值,不再包一层
        void DefType(string name, ObjectVal type) => Def(name, BuiltinClasses.Type, type);
        void DefFn(string name, FunctionVal fn) => Def(name, BuiltinClasses.Function, fn);
        void DefControl(string name, ControlKind kind, int arity) => DefFn(name, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

        // ---- 类型(System.Integer 等权威名;小写别名在 predefined.rav) ----
        DefType("Integer", BuiltinClasses.Int);
        DefType("String", BuiltinClasses.String);
        DefType("Bool", BuiltinClasses.Bool);
        DefType("Float", BuiltinClasses.Float);
        DefType("BigInteger", BuiltinClasses.BigInt);
        DefType("Fraction", BuiltinClasses.Fraction);
        DefType("BigFraction", BuiltinClasses.BigFraction);
        DefType("List", BuiltinClasses.List);
        DefType("Set", BuiltinClasses.Set);
        DefType("Dict", BuiltinClasses.Dict);
        DefType("Object", BuiltinClasses.Object);
        DefType("Function", BuiltinClasses.Function);
        DefType("Void", BuiltinClasses.Void);
        DefType("Type", BuiltinClasses.Type);
        DefType("ValueTypeVal", BuiltinClasses.ValueType);
        DefType("AnyType", BuiltinClasses.Any);
        DefType("EveryType", BuiltinClasses.Every);
        DefType("ExceptionType", BuiltinClasses.Exception);

        // ---- 常量 ----
        Def("True", BuiltinClasses.Bool, new BoolVal(true));
        Def("False", BuiltinClasses.Bool, new BoolVal(false));
        // 特殊浮点值。和 True/False 同款:系统模块里的一个值,`predefined.rav` 给全局别名
        // (`-Inf` 不用另设,一元 `-` 对 Float 就是取负)
        Def("NaN", BuiltinClasses.Float, new FloatVal(double.NaN));
        Def("Inf", BuiltinClasses.Float, new FloatVal(double.PositiveInfinity));
        Def("Default", BuiltinClasses.Every, DefaultVal.Instance);

        // ---- 控制内建:收满参数后由求值器推控制帧 ----
        // if/while/foreach 不在这里——它们在 predefined.rav 用 Ravel 写(靠可调用的 true/false + callcc)
        DefControl("With", ControlKind.With, 2);
        DefControl("CallCC", ControlKind.CallCC, 1);
        DefControl("Using", ControlKind.Using, 1);
        DefControl("Eval", ControlKind.Eval, 1);

        // ---- 输出 ----
        DefFn("WriteLine", FunctionVal.From(a =>
        {
            Console.WriteLine(Show(a));
            return VoidVal.Instance;
        }));
        DefFn("Write", FunctionVal.From(a =>
        {
            Console.Write(Show(a));
            return VoidVal.Instance;
        }));
        DefFn("ReadLine", FunctionVal.From(_ => new StringVal(Console.ReadLine() ?? "")));

        // ---- 反射 / 作用域 ----
        DefFn("TypeOf", FunctionVal.From(a => a.Type));
        DefFn("currentScope", FunctionVal.From(_ => new ScopeVal(CurrentScope)));
        // 标记**当前**作用域:core 检查沿作用域链往上找标记,所以函数返回后标记自然失效
        DefFn("unsafe", FunctionVal.From(_ =>
        {
            UnsafeScopes.Add(CurrentScope);
            return VoidVal.Instance;
        }));

        // ---- 其他核心函数 ----
        DefFn("RandInt", FunctionVal.From((lo, hi) =>
        {
            if (lo is not IntVal l) throw new RuntimeException($"randint 的最小值需要 int，得到 {lo.Type}");
            if (hi is not IntVal h) throw new RuntimeException($"randint 的最大值需要 int，得到 {hi.Type}");
            // Random.Next 在 min > max 时抛 ArgumentOutOfRangeException——那是 C# 异常,
            // 会绕过 Ravel 层的 try 一路漏到顶层把程序打掉,所以自己先拦
            if (l.Value > h.Value)
                throw new RuntimeException($"randint 的最小值 {l.Value} 不能大于最大值 {h.Value}");
            return new IntVal(Random.Shared.Next(l.Value, h.Value));
        }));
        DefFn("property", FunctionVal.From((g, s) =>
        {
            if (g is not FunctionVal gf) throw new RuntimeException("property 需要 getter 函数");
            if (s is not FunctionVal sf) throw new RuntimeException("property 需要 setter 函数");
            return new PropertyVal(gf, sf);
        }));
        DefFn("Assert", FunctionVal.From((cond, msg) =>
        {
            if (cond is not BoolVal b) throw new RuntimeException("assert 需要 bool 参数");
            if (!b.Value) throw new RuntimeException(msg is StringVal s ? s.Value : "assertion failed: " + Show(cond));
            return VoidVal.Instance;
        }));
        DefFn("Exit", FunctionVal.From(a => throw new ExitException(a is StringVal s ? s.Value : "")));
        DefFn("RavelMod", FunctionVal.From(a => EnterModule(As<StringVal>(a, "ravel 的模块名").Value)));

        return module;
    }

    /// <summary>ravel "M":切换到命名模块的作用域(首次访问时创建),后续语句落在该模块里。
    /// `ravel ""` 是**回全局作用域**——文档就是这么用的(`ravel "MyMath"` … `ravel ""` … 引用它)。</summary>
    private RuntimeValue EnterModule(string name)
    {
        // 空名字特判成「回全局」。不特判的话会建出一个**名字叫 "" 的模块**,
        // 后面的定义全落在那儿;而模块作用域只挂到 _global 上,所以那些定义
        // 从别的模块根本看不见:`ravel ""` 之后 `b := 2`,再 `ravel "A"` 就读不到 b。
        if (name.Length == 0)
        {
            SetAmbientScope(_global);
            return VoidVal.Instance;
        }

        if (!_modules.TryGetValue(name, out var mv))
        {
            var mt = BuiltinClasses.NewModuleClass(name, BuiltinClasses.Ravel);
            mv = new ModuleVal(mt, new Scope(_global));
            // 成员是 C# 造的那几个模块(Math),在这里填 —— 所以它们**要显式 ravel 才有**
            if (ModuleFillers.TryGetValue(name, out var fill)) fill(mv.ModuleScope);
            _modules[name] = mv;
            _global.Define(name, mt, mv);
        }

        SetAmbientScope(mv.ModuleScope);
        return VoidVal.Instance;
    }
}
