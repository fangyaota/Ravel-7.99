namespace Ravel.Runtime;

using System.Linq;

/// <summary>内置方法的注册（端口自旧的 RuntimeType.Methods.cs）。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>注册各内置类型的方法(按类型分组,加方法时直接跳对应那块)</summary>
    private static void RegisterMethods()
    {
        RegisterJsonMethods();
        // 其余各内置类的方法已经搬进 `Runtime/Members/*.cs`(每类一份,跟着类走):
        // `Object`/`Ravel` → ObjectMembers,`String` → StringMembers,`Char` → CharMembers,
        // `Range` → RangeMembers,`Type` → TypeMembers,`Function` → FunctionMembers,
        // `Scope` → ScopeMembers,`Property` → PropertyMembers,
        // `list`/`set`/`dict` 的序列方法 → SequenceMembers(三种容器共用一份实现)。
        // 那些名字现在由 `ClassVal.Members` 那份成员工厂给出,这里不再注册。
        //
        // 下面这两批**必须留在注册表里**:它们挂的不是普通 `BuiltinMethodVal`,而是
        // `SeqMethod` / `TraitQuery` 这类"求值器要认出"的值 —— `[ClassMethod]` 只能绑方法,
        // 表达不了它们,只能在类型表上直接放一个值。
        RegisterHigherOrderMethods(List);
        RegisterHigherOrderMethods(Set);
        RegisterHigherOrderMethods(Dict);
        RegisterTypeMethods();
    }

    // `Object` / `Ravel` 的成员搬到了 `Runtime/Members/ObjectMembers.cs`（按类型自己管）。
    // 这里不再有它们的注册 —— 那些名字由 `ClassVal.Members` 那份成员工厂给出。



    /// <summary>字符串的参数:要字符串,或者**一个字符**(`"abc".Contains 'b'` 一样通)。
    /// 拼接、查找、切分这些地方两种写法都自然,所以都收。
    ///
    /// **留在 `BuiltinClasses` 而不是搬进 `StringMembers`**:`BuiltinClasses.Json.cs`
    /// 也在用它(JSON 取成员时那些按名字查的入口),两处共用这一份规矩就没分叉。
    /// 所以是 `internal` 不是 `private` —— 新文件里写 `BuiltinClasses.TextArg`。</summary>
    internal static string TextArg(RuntimeValue a, string what) => a switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"{what} 需要 string 或 char 参数，得到 {a.Type}", ErrorKind.Argument),
    };

    /// <summary>类型上那两条**不能用 `[ClassMethod]` 表达**的成员,只能留在这张注册表里。
    ///
    /// 其余(`Default` / `Subtypes`)已搬进 `Runtime/Members/TypeMembers.cs` —— 那是普通方法。
    /// 这两条挂的是 `TraitQuery`(和 `SeqMethod` 同一个家族):它们**不是** `BuiltinMethodVal`,
    /// 真正算它们的是求值器(实现登记在作用域里),`[ClassMethod]` 只能绑 `MethodInfo`。
    ///
    /// 挂的位置不能变:`Type` 的**实例表**上 —— `Option.GetImplements ()` 里的 `Option`
    /// 是 `type` 的实例,沿 `parent` 往上兜到这一张(`Option` 自己的表里没有,`Type` 那张里有)✓</summary>
    private static void RegisterTypeMethods()
    {
        EngineMember(Type, "GetImplements", new TraitQuery("GetImplements", TraitQueryKind.Implements));
        EngineMember(Type, "GetImplementors", new TraitQuery("GetImplementors", TraitQueryKind.Implementors));
    }


    /// <summary>**能不能当字典的键** —— 判据只有这一处,`KeyArg` 和 `Object` 上那条默认的
    /// `Key` 都问它;`lib/keys.rav` 的 `Of` 是同一句话在 Ravel 那边,改要一起改。
    /// 语法层还有一处**生成**这句判断的(`Syntax/Lowering.cs`,字典模式的"当得了键吗"),
    /// 那儿发的是 `key : IValue` —— 三处说的是同一件事。
    ///
    /// 判据是 **`ClassVal.IsValueLike`** 那格索引,也就是"它实现了 `IValue` 吗"
    /// (写它的是 `BuiltinClasses.SealBuiltins` 与 `[RavelClass(Implements = "IValue")]`)。
    ///
    /// **为什么读索引而不是问 `HasTrait`**:这条路跑在 `dict.SysGet` / `SysSet` 那些
    /// **同步 C#** 操作里,那儿**拿不到解释器**。而且 `bool` / `()` 从前进不来
    /// (`true` / `false` 可调用、按 lisp 那套挂在 `Function` 下;`()` 挂在 `Object` 下)——
    /// 单继承链放不下"按值比"和"挂在谁下面"两个正交的分类。改成接口 + 索引之后,
    /// 那句手写的 `|| Bool || Void` 没了。
    ///
    /// 为什么限死在"按值比"的那些:.NET 的 `Dictionary` 要一个**同步**的比较器
    /// (`RuntimeValue` 是记录,用的是结构比较),而用户写的 `Key ()` 是 Ravel 函数
    /// (调它得推帧,`Interpreter.CallInto` 那条路)。对象/列表当键不稳(里面挂着作用域,
    /// 比的是引用)—— 那要走 `lib/keys.rav` 那层(`Keys.Set` / `Keyed`),
    /// 先把对象规范成一个值类型再进表。</summary>
    internal static bool CanBeKey(ObjectVal t) => t is ClassVal c && c.IsValueLike;

    internal static RuntimeValue KeyArg(RuntimeValue a, string what)
        => CanBeKey(a.Type)
            ? a
            : throw new RuntimeException($"{what}得是值类型（数 / 串 / 字符 / 布尔 / `()`），得到 {a.Type}", ErrorKind.Type);

    /// <summary>**属性的默认值** —— 一对什么都不做的函数:
    ///
    ///     Get = () => { 空函数; }
    ///     Set = (_ : object) => { (); }
    ///
    /// 和 `int default` 给 `0`、`function default` 给空函数是同一个道理:
    /// `by a: int = default` 里那个 `default` 就是它 —— 槽里先放一个**能读能写、
    /// 但什么都不做**的属性,之后用 `by a = property g s` 把真实现换上。
    ///
    /// **getter 交回的是空函数,不是 `()`** —— 和 `function default` 那份
    /// (`ConvertDirect` 里那条)共用同一个 <see cref="EmptyFunction"/>。
    /// 从前交 `()`(顺手在 getter 的返回值上写了个 `()`),于是 `by Map : function = default`
    /// 没人填时,`x.Map f` 报的是「值 () 不是函数,不能调用」—— 读的人得自己猜到
    /// "原来这条槽没人填";而**用普通成员声明的同一条槽**(`Map : function = default`)
    /// 读出来是个能调的空函数,两条路两个读法。现在两条一样了。
    ///
    /// 它**没有状态**,所以 `with` / `Copy ()` 让副本和原件共享同一个也无害
    /// (`CopyScope` 那条复制路不用为它做任何事)。</summary>
    internal static PropertyVal DefaultProperty()
        => new(FunctionVal.From(_ => EmptyFunction()), FunctionVal.From(_ => VoidVal.Instance));

    /// <summary>**空函数**:收什么都行、什么都不做、交回 `()`。
    ///
    /// 两处共用:`function default`(见 `ConvertDirect`)和 `by X = default` 那条默认属性的
    /// getter —— "这条槽还没有实现"读出来就该是个**能调**的东西,不是 `()`。</summary>
    internal static FunctionVal EmptyFunction() => FunctionVal.From(_ => VoidVal.Instance);

    /// <summary>拷贝作用域（with / Copy 用）：逐字段浅拷贝，词法父照搬。
    /// 字段已平铺在同一个 scope 里，所以不再需要沿继承链递归深拷贝。</summary>
    /// <summary>把指回原实例 scope 的闭包重绑到新 scope。方法(lambda)直接重绑;
    /// `by` 属性要连 getter/setter 一起——它们也是 lambda,只是装在 PropertyVal 里。</summary>
    private static RuntimeValue Rebind(RuntimeValue value, Scope dst) => value switch
    {
        LambdaVal lam => lam with { CaptureScope = dst },
        PropertyVal pv => pv with { Getter = RebindFn(pv.Getter, dst), Setter = RebindFn(pv.Setter, dst) },
        _ => value,
    };

    /// <summary>换掉一个函数体的作用域。**`BlockVal` 也算一种 lambda 体** ——
    /// `CallInto` 对它是 `blk.CaptureScope.Push()`,漏了它就会**静默**看错外层的名字
    /// (`Copy ()` 那样:副本的块还在读原件的字段);接口那条激活格同理,漏了就静默看错 `instance`。
    /// 其余(`FunctionVal.From` 造的 native / 内置方法)原样 —— 它们的体不看名字。</summary>
    private static FunctionVal RebindFn(FunctionVal f, Scope dst) => f switch
    {
        LambdaVal lam => lam with { CaptureScope = dst },
        BlockVal blk => blk with { CaptureScope = dst },
        _ => f,
    };

    /// <summary>浅拷贝一个值 —— `obj.Copy ()` 和 `with` **共用这一份**。
    ///
    /// 只有"带字段的数据"才有副本可谈:
    /// - **函数/类对象**"拷"出来只会变成一个不可调用的普通对象,**模块**被拷成普通对象后
    ///   `EnterModule`/`_modules` 就认不出它了,**属性/作用域值**同理 —— 一律原样交回。
    /// - **原子值**是值语义的 record,交回自己和交回副本在 `==` 下没有区别,不必逐个列。
    ///
    /// 后两条都靠"原样交回"表达,所以调用方 `ReferenceEquals(结果, 原值)` 就能问出
    /// "到底拷没拷"(`with` 用它决定块跑在副本的成员表里还是自己的捕获作用域里)。
    /// **会交出不同引用的只有**下面那三条容器臂和 `ObjectVal` 那条 —— 副本一律是 `ObjectVal`。</summary>
    internal static RuntimeValue CopyValue(RuntimeValue v) => v switch
    {
        // 容器的成员表跟着副本走 —— 和 CopyObject 一个道理(它们也是 ObjectVal)
        ListVal l => CopyTableInto(new ListVal([.. l.Elements]), l),
        SetVal s => CopyTableInto(new SetVal([.. s.Elements]), s),
        DictVal d => CopyTableInto(new DictVal(new Dictionary<RuntimeValue, RuntimeValue>(d.Entries)), d),
        // Json 值:**浅拷**——那棵 `JToken` 树一起用(Ravel 这边没有改它的手段,`Get`/`At`
        // 也是包个新 Json 指向同一棵树),要拷的是**成员表**(副本上 `tag = …` 才找得到)
        JsonVal j => CopyTableInto(new JsonVal(j.Token), j),
        // 必须排在 ObjectVal 之前:这四类现在也是 ObjectVal
        FunctionVal or ModuleVal or PropertyVal or ScopeVal => v,
        ObjectVal o => CopyObject(o),
        _ => v,
    };

    /// <summary>这个值有**自己**的成员表吗 —— 也就是 <see cref="CopyValue"/> 会真的拷一份的
    /// 那些(数据对象 / 容器 / Json)。函数、类对象、模块、属性、作用域值都不算,
    /// 原子值更是连表都没有 —— 它们的成员表要么不存在,要么不属于"这一个值"。
    ///
    /// `with` 用它决定块跑在谁的作用域上(见 `Interpreter.StepWith`)。</summary>
    internal static bool HasOwnTable(RuntimeValue v)
        => v is ObjectVal and not (FunctionVal or ModuleVal or PropertyVal or ScopeVal);

    /// <summary>拷贝一个对象:新对象先建出来(成员表跟着 `Scope` 懒建),再把原表逐格搬过去
    /// (方法闭包重绑,见 <see cref="CopyTableInto"/>)+ `this` 指向副本。
    /// 漏掉重绑 `this` 的话,副本里写 `this.v = n` 会落到原对象上,
    /// 而裸写 `v = n` 却是对的,行为自相矛盾。</summary>
    internal static ObjectVal CopyObject(ObjectVal src)
        => CopyTableInto(new ObjectVal(src.ClassType, null), src);

    /// <summary>把 <paramref name="src"/> 那张成员表逐格搬进 <paramref name="copy"/> 的表。
    ///
    /// 表必须**先有对象才有**(`MemberTable` 的 owner 就是那个对象),所以拷的顺序是
    /// "建对象 → 搬格子",不是从前的"先拷一张 Scope 再塞进构造器"。
    ///
    /// 词法父照搬(类体自由名字要按原来那处解析);方法闭包重绑到新表 —— 浅共享会让
    /// 副本上调方法读写到原对象的字段,`with` 就白拷了。</summary>
    private static T CopyTableInto<T>(T copy, ObjectVal src) where T : ObjectVal
    {
        var dst = copy.Scope;
        // 源表可能没有词法父(顶层对象):这时副本也就不挂父,别拿 null 去 Reparent
        if (src.Scope.Parent is { } lexical) dst.Reparent(lexical);
        foreach (var kv in src.Scope.Variables)
        {
            var value = Rebind(kv.Value.Value, dst);
            // attrs 也要带过去:丢了 by 的话副本上的 `v = x` 不走 setter,
            // 丢了 readonly/private/core 就等于副本绕过了这些约束
            var v = dst.DefineOrReplace(kv.Key, kv.Value.TypeConstraint, value);
            foreach (var a in kv.Value.Attrs) v.SetAttr(a);
        }

        dst.DefineOrReplace(ObjectVal.ThisMember, src.ClassType, copy);
        return copy;
    }
}
