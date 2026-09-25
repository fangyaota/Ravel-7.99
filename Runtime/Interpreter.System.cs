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
        var moduleType = RuntimeType.Define("System", RuntimeType.Ravel);
        var module = new ModuleVal(moduleType, new Scope(_global));
        var scope = module.ModuleScope;

        void Def(string name, RuntimeType type, RuntimeValue value) => scope.Define(name, type, value);
        void DefType(string name, RuntimeType type) => Def(name, RuntimeType.Type, new TypeVal(type));
        void DefFn(string name, FunctionVal fn) => Def(name, RuntimeType.Function, fn);
        void DefControl(string name, ControlKind kind, int arity) => DefFn(name, new ControlFunction(kind, arity, RList<RuntimeValue>.Empty));

        // ---- 类型(System.Integer 等权威名;小写别名在 predefined.rav) ----
        DefType("Integer", RuntimeType.Int);
        DefType("String", RuntimeType.String);
        DefType("Bool", RuntimeType.Bool);
        DefType("Float", RuntimeType.Float);
        DefType("BigInteger", RuntimeType.BigInt);
        DefType("Fraction", RuntimeType.Fraction);
        DefType("BigFraction", RuntimeType.BigFraction);
        DefType("List", RuntimeType.List);
        DefType("Set", RuntimeType.Set);
        DefType("Dict", RuntimeType.Dict);
        DefType("Object", RuntimeType.Object);
        DefType("Function", RuntimeType.Function);
        DefType("Void", RuntimeType.Void);
        DefType("Type", RuntimeType.Type);
        DefType("ValueTypeVal", RuntimeType.ValueType);
        DefType("AnyType", RuntimeType.Any);
        DefType("EveryType", RuntimeType.Every);
        DefType("ExceptionType", RuntimeType.Exception);

        // ---- 常量 ----
        Def("True", RuntimeType.Bool, new BoolVal(true));
        Def("False", RuntimeType.Bool, new BoolVal(false));
        Def("Default", RuntimeType.Every, DefaultVal.Instance);

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
        DefFn("TypeOf", FunctionVal.From(a => new TypeVal(a.Type)));
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
            var mt = RuntimeType.Define(name, RuntimeType.Ravel);
            mv = new ModuleVal(mt, new Scope(_global));
            _modules[name] = mv;
            _global.Define(name, mt, mv);
        }

        SetAmbientScope(mv.ModuleScope);
        return VoidVal.Instance;
    }
}
