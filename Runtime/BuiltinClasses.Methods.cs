namespace Ravel.Runtime;

using System.Linq;

/// <summary>内置方法的注册（端口自旧的 RuntimeType.Methods.cs）。</summary>
internal static partial class BuiltinClasses
{

    /// <summary>注册各内置类型的方法(按类型分组,加方法时直接跳对应那块)</summary>
    private static void RegisterMethods()
    {
        RegisterObjectMethods();
        RegisterIntMethods();
        RegisterStringMethods();
        RegisterRangeMethods();
        RegisterJsonMethods();
        RegisterListMethods();
        RegisterSetMethods();
        RegisterDictMethods();
        // 序列方法:三种容器共用那一份(将来 IEnumerable 的落点)。
        // `items` = 「按枚举顺序取出元素」——字典给的是**值**(按键找用 `Has` / `Keys ()`)
        RegisterSequenceMethods(List, s => [.. ((ListVal)s).Elements]);
        RegisterSequenceMethods(Set, s => [.. ((SetVal)s).Elements]);
        RegisterSequenceMethods(Dict, s => [.. ((DictVal)s).Entries.Values]);
        RegisterHigherOrderMethods(List);
        RegisterHigherOrderMethods(Set);
        RegisterHigherOrderMethods(Dict);
        RegisterTypeMethods();
        RegisterFunctionMethods();
        RegisterScopeMethods();
        RegisterPropertyMethods();
    }

    private static void RegisterObjectMethods()
    {
        Object.DefineMethod("ToString", (s, _) => new StringVal(s.ToString()));
        Object.DefineMethod("Copy", (s, _) => CopyValue(s));
        // **枚举那个 Scope** —— 所有成员(字段和方法一视同仁)都在它里面,
        // 没有"猜哪张表"这一步。这里从前是两圈:一圈扫实例作用域里的**数据字段**,
        // 一圈沿类型链扫**方法名**,于是同一个类体里定义的东西一个列得出、一个列不出
        // (运算符恰好在类对象表里另有一份,所以它在;用户写的方法就凭空消失了)。
        // 现在两段判据只有一份,和成员查找共用(见 MemberView)。
        Object.DefineMethod("Fields", (s, _) =>
            new ListVal([.. s.MemberScope.MemberNames.Select(n => (RuntimeValue)new StringVal(n))]));
        // 谁大谁小 —— 库里的 `IComparable`(`lib/sorting.rav`)就架在这条上:内建那些(数值、字符串)
        // 由引擎那把尺子(`Less`,也就是 `<` 的口径)说话,用户类在自己类体里写一条 `CompareTo`
        // 就把它盖掉 —— 和 `ToString` 一个规矩。交回 -1 / 0 / 1,和 C# 的 IComparable 同约定,
        // 比不了照样当场说人话(`Less` 那句「比不了 X 与 Y」)。
        Object.DefineMethod("CompareTo", (s, a) => new IntVal(
            Less(s, a) ? -1 : Less(a, s) ? 1 : 0));
        // `Key` —— "**我当键时等于什么**"。默认这把尺子只对值类型有话说:数、字符串交回**它自己**
        // (于是 `1`/`"1"`/`1.0` 是三个键,相等的两个字符串是同一个键);别的类型当场报错 ——
        // 对象没有天然的"值形式",要拿它当键就得自己讲清楚(在类里写一条 `Key`,和 `CompareTo`
        // / `ToString` 一个规矩)。**必须是这一条**(同步、只看类型)才够得着:引擎没有"同步调
        // Ravel 函数"的路,用户那条 `Key` 只能由库(`lib/keys.rav`)在 Ravel 里调。
        Object.DefineMethod("Key", (s, _) =>
            s.Type.IsAssignableTo(ValueType)
                ? s
                : throw new RuntimeException(
                    $"Key: {s.Type} 没有默认的键（只有值类型有：数、字符串）。"
                    + "要拿它当键，就在类里写一条 Key，交回一个值类型的键"));
    }

    private static void RegisterIntMethods()
    {
        Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));
    }

    /// <summary>字符串的参数:要字符串,或者**一个字符**(`"abc".Contains 'b'` 一样通)。
    /// 拼接、查找、切分这些地方两种写法都自然,所以都收。</summary>
    private static string TextArg(RuntimeValue a, string what) => a switch
    {
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        _ => throw new RuntimeException($"{what} 需要 string 或 char 参数，得到 {a.Type}", ErrorKind.Argument),
    };

    /// <summary>区间那几条。都只跟那**一对边界 + 开闭**打交道,所以都是 O(1)
    /// (只有 `ToList ()` 是 O(n) —— 那是用户自己要铺成表的)。
    ///
    /// 它同时也是一个 `IEnumerable`(impl 在 lib/iterator.rav,枚举走生成器的光标、惰性),
    /// 所以 `Map` / `Where` / `Fold` / `Take` 那整套是白拿的;这里只把**该更快或者
    /// 该更直白**的几条先摆出来(`Count` / `Contains` / `ToList` 与接口那份语义一致,
    /// 只是不必走一遍迭代)。</summary>
    private static void RegisterRangeMethods()
    {
        // 端点:写出来那两个数(**任意数值** —— 开区间的那端不在元素里,要元素用 First / Last)
        Range.DefineMethod("Start", (s, _) => ((RangeVal)s).Start);
        Range.DefineMethod("End", (s, _) => ((RangeVal)s).End);
        // 个数:装得下给 int、装不下给 bigint
        Range.DefineMethod("Count", (s, _) =>
        {
            var n = ((RangeVal)s).CountValue();
            return n >= int.MinValue && n <= int.MaxValue ? new IntVal((int)n) : new BigIntVal(n);
        });
        Range.DefineMethod("IsEmpty", (s, _) => new BoolVal(((RangeVal)s).IsEmpty()));
        Range.DefineMethod("Contains", (s, a) =>
            new BoolVal(((RangeVal)s).ContainsValue(a)));

        // 和 `Contains` 分开的两个问法:`Contains` 是"里头有没有这个**元素**"(和别的容器
        // 一个意思,元素是那些整数),`Covers` 是"这个值**落不落在这段里**"(按端点比,
        // 小数也算数)。Ruby 也是这么分的(`include?` / `cover?`)。
        Range.DefineMethod("Covers", (s, a) =>
            new BoolVal(((RangeVal)s).CoversValue(a)));

        // 铺成表 —— 只有这一条是 O(n)。空区间给空表(不是报错)
        Range.DefineMethod("ToList", (s, _) =>
        {
            var r = (RangeVal)s;
            var (first, last, up) = r.Walk();
            var out_ = new List<RuntimeValue>();
            for (var i = first; up ? i <= last : i >= last; i += up ? 1 : -1) out_.Add(r.Element(i));
            return new ListVal(out_);
        });

        // `Text ()` 就是那个记号本身(和 `print` 一致)
        Range.DefineMethod("Text", (s, _) => new StringVal(s.ToString()!));

        // 头一个 / 末一个**元素**(不是那两个端点 —— 开区间的端点在区间外)。
        // 接口默认那份要迭代一遍,这两条 O(1);空的照旧报错,措辞和默认那份一致。
        Range.DefineMethod("First", (s, _) =>
        {
            var r = (RangeVal)s;
            if (r.IsEmpty()) throw new RuntimeException("First: 元素不够（一共 0 个）", ErrorKind.Index);
            return r.FirstElement();
        });
        Range.DefineMethod("Last", (s, _) =>
        {
            var r = (RangeVal)s;
            if (r.IsEmpty()) throw new RuntimeException("Last: 元素不够（一共 0 个）", ErrorKind.Index);
            return r.LastElement();
        });
    }

    /// <summary>把一个区间折成**半开**的 `[from, to)` —— 切片那套(字符串的 `Slice`)的通用口径:
    /// 左端开就起点挪一格,右端闭就终点挪一格。倒过来的区间(`Start > End`)和空区间切出空串,
    /// 越界(拿到的那个 from/to 落在字符串外面)照旧当场报错 —— 和两个数字那种写法一条规矩。</summary>
    private static (int From, int To, bool Reverse) SliceBounds(RangeVal rng, int length, string what)
    {
        // 按**元素**那边折算(区间里那些整数):`[1.5..3.5]` 切的是 2..3 那一段。
        // 区间是**降序**的话(`[4..0]`)拿到的是同一段、但**倒着**切 —— 方向也照区间的意思走。
        var (first, last, up) = rng.Walk();
        if (up ? first > last : first < last) return (0, 0, false);      // 空区间 → 空的一段

        var lo = System.Numerics.BigInteger.Min(first, last);
        var hi = System.Numerics.BigInteger.Max(first, last);
        if (lo < 0 || hi + 1 > length)
            throw new RuntimeException($"{what}: {rng} 越界（长度 {length}）", ErrorKind.Index);

        return ((int)lo, (int)(hi + 1), !up);           // 半开:`hi` 那个元素也要,所以 +1
    }

    private static void RegisterStringMethods()
    {
        String.DefineMethod("Length", (s, _) => new IntVal(((StringVal)s).Value.Length));
        String.DefineMethod("IsEmpty", (s, _) => new BoolVal(((StringVal)s).Value.Length == 0));

        // ── 取字符 ──
        // `At i` 给**字符**(和 `[1 2].At 0` 给元素对称);越界当场报错,不悄悄给个 `()`
        String.DefineMethod("At", (s, a) =>
        {
            var v = ((StringVal)s).Value;
            var i = IntArg(a, "s.At");
            if (i < 0 || i >= v.Length)
                throw new RuntimeException($"s.At: 索引 {i} 超出范围（长度 {v.Length}）");
            return new CharVal(v[i]);
        });
        // 拆成一串字符(想逐个处理就它,别拿 `At` 配下标手摇)
        String.DefineMethod("Chars", (s, _) =>
            new ListVal([.. ((StringVal)s).Value.Select(c => (RuntimeValue)new CharVal(c))]));

        // ── 找 ──
        String.DefineMethod("Contains", (s, a) =>
            new BoolVal(((StringVal)s).Value.Contains(TextArg(a, "s.Contains"), StringComparison.Ordinal)));
        String.DefineMethod("StartsWith", (s, a) =>
            new BoolVal(((StringVal)s).Value.StartsWith(TextArg(a, "s.StartsWith"), StringComparison.Ordinal)));
        String.DefineMethod("EndsWith", (s, a) =>
            new BoolVal(((StringVal)s).Value.EndsWith(TextArg(a, "s.EndsWith"), StringComparison.Ordinal)));
        // 找不到是**报错**,不是给 -1 —— 和别处"没有就是错"一个口径;明知可能没有用 `Find`
        String.DefineMethod("IndexOf", (s, a) =>
        {
            var v = ((StringVal)s).Value;
            var i = v.IndexOf(TextArg(a, "s.IndexOf"), StringComparison.Ordinal);
            if (i < 0) throw new RuntimeException($"s.IndexOf: 找不到 {TextArg(a, "s.IndexOf")}");
            return new IntVal(i);
        });
        String.DefineMethod("Find", (s, a) =>
            new IntVal(((StringVal)s).Value.IndexOf(TextArg(a, "s.Find"), StringComparison.Ordinal)));   // 没有给 -1
        String.DefineMethod("LastIndexOf", (s, a) =>
        {
            var v = ((StringVal)s).Value;
            var i = v.LastIndexOf(TextArg(a, "s.LastIndexOf"), StringComparison.Ordinal);
            if (i < 0) throw new RuntimeException($"s.LastIndexOf: 找不到 {TextArg(a, "s.LastIndexOf")}");
            return new IntVal(i);
        });

        // ── 切 ──
        // `s.Slice [1..3)` —— **收一个区间**,开闭由那对括号说了算(从前"两个数字 + 半开"
        // 那个写法已经删掉,只留这一种:谁想说不含哪一端,写括号就行)。
        String.DefineMethod("Slice", (s, a) =>
        {
            if (a is not RangeVal rng)
                throw new RuntimeException($"s.Slice 收一个区间（如 `s.Slice [1..3)`），得到 {a.Type}");

            var v = ((StringVal)s).Value;
            var (from, to, reverse) = SliceBounds(rng, v.Length, "s.Slice");
            var part = v[from..to];
            if (reverse) part = string.Concat(part.Reverse());
            return new StringVal(part);
        });
        String.DefineMethod("Take", (s, a) => new StringVal(((StringVal)s).Value[..Math.Clamp(IntArg(a, "s.Take"), 0, ((StringVal)s).Value.Length)]));
        String.DefineMethod("Skip", (s, a) => new StringVal(((StringVal)s).Value[Math.Clamp(IntArg(a, "s.Skip"), 0, ((StringVal)s).Value.Length)..]));

        // ── 变 ──
        String.DefineMethod("Trim", (s, _) => new StringVal(((StringVal)s).Value.Trim()));
        // 大小写:**不跟区域设置走**(土耳其语的 i/İ 那种会让同一段程序换台机器就换个结果)
        String.DefineMethod("ToUpper", (s, _) => new StringVal(((StringVal)s).Value.ToUpperInvariant()));
        String.DefineMethod("ToLower", (s, _) => new StringVal(((StringVal)s).Value.ToLowerInvariant()));
        String.DefineMethod("Replace", (s, a) =>
        {
            var from = TextArg(a, "s.Replace");
            return FunctionVal.From(b => new StringVal(((StringVal)s).Value.Replace(from, TextArg(b, "s.Replace"), StringComparison.Ordinal)));
        });
        String.DefineMethod("Repeat", (s, a) => new StringVal(string.Concat(
            Enumerable.Repeat(((StringVal)s).Value, Math.Max(0, IntArg(a, "s.Repeat"))))));
        String.DefineMethod("Reverse", (s, _) =>
            new StringVal(new string([.. ((StringVal)s).Value.Reverse()])));
        String.DefineMethod("Split", (s, a) =>
        {
            var sep = TextArg(a, "s.Split");
            if (sep.Length == 0)
                throw new RuntimeException("s.Split: 分隔符不能是空串（想逐个字符就用 s.Chars ()）");
            return new ListVal([.. ((StringVal)s).Value
                .Split(sep, StringSplitOptions.None)
                .Select(p => (RuntimeValue)new StringVal(p))]);
        });

        // ── 字符自己那几个 ──(判类不管大小写:数字/字母就是数字/字母)
        static RuntimeValue CharMethod(string name, Func<char, bool> f)
        {
            Char.DefineMethod(name, (s, _) => new BoolVal(f(((CharVal)s).Value)));
            return VoidVal.Instance;
        }

        CharMethod("IsDigit", char.IsDigit);
        CharMethod("IsLetter", char.IsLetter);
        CharMethod("IsUpper", char.IsUpper);
        CharMethod("IsLower", char.IsLower);
        CharMethod("IsSpace", char.IsWhiteSpace);
        Char.DefineMethod("Code", (s, _) => new IntVal(((CharVal)s).Value));          // `int c` 的显式版
        Char.DefineMethod("ToString", (s, _) => new StringVal(((CharVal)s).Value.ToString()));
    }

    /// <summary>取下标之前的统一检查:收下索引、卡边界,顺手把列表交回去。
    /// 以前五处各写一遍检查、消息都只有「索引超出范围」四个字——越的是哪个界、
    /// 在几号元素上越的,全看不出来。allowEnd 给 Insert 用(插到末尾是合法的)。</summary>
    private static ListVal Indexed(RuntimeValue s, RuntimeValue a, string what, bool allowEnd = false)
    {
        if (a is not IntVal i) throw new RuntimeException($"{what} 需要 int 参数", ErrorKind.Argument);
        var lst = (ListVal)s;
        var n = lst.Elements.Count;
        if (i.Value < 0 || i.Value > (allowEnd ? n : n - 1))
            throw new RuntimeException($"{what} 的索引 {i.Value} 越界 (列表长度 {n})", ErrorKind.Index);
        return lst;
    }

    private static void RegisterListMethods()
    {
        List.DefineMethod("At", (s, a) =>
        {
            var lst = Indexed(s, a, "list.At");
            return lst.Elements[((IntVal)a).Value];
        });
        List.DefineMethod("Add", (s, a) =>
        {
            ((ListVal)s).Elements.Add(a);
            return VoidVal.Instance;
        });
        List.DefineMethod("Remove", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Remove");
            var idx = ((IntVal)a).Value;
            var v = lst.Elements[idx];
            lst.Elements.RemoveAt(idx);
            return v;
        });
        List.DefineMethod("Insert", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Insert", allowEnd: true);
            var idx = ((IntVal)a).Value;
            return FunctionVal.From(v =>
            {
                lst.Elements.Insert(idx, v);
                return VoidVal.Instance;
            });
        });
        List.DefineMethod("Set", (s, a) =>
        {
            var lst = Indexed(s, a, "list.Set");
            var idx = ((IntVal)a).Value;
            return FunctionVal.From(v =>
            {
                lst.Elements[idx] = v;
                return VoidVal.Instance;
            });
        });

        // 下面这些是**列表才有**的:要么带下标,要么依赖"顺序是有意义的"
        // (集合/字典没有这条 —— 所以它们只在序列方法那一批里出现,见 BuiltinClasses.Sequences.cs)
        List.DefineMethod("IndexOf", (s, a) => new IntVal(((ListVal)s).Elements.IndexOf(a)));
        // `RemoveAt i` 和 `Remove i` 是同一件事:名字对齐 C#(`List.RemoveAt` 按下标、
        // `List.Remove` 按值),老名字留着 —— 库和好几个用例都在用
        List.DefineMethod("RemoveAt", (s, a) =>
        {
            var lst = Indexed(s, a, "list.RemoveAt");
            var idx = ((IntVal)a).Value;
            var v = lst.Elements[idx];
            lst.Elements.RemoveAt(idx);
            return v;
        });
        List.DefineMethod("AddRange", (s, a) =>
        {
            ((ListVal)s).Elements.AddRange(ElementsOf(a, "AddRange"));
            return VoidVal.Instance;
        });
        List.DefineMethod("Clear", (s, _) =>
        {
            ((ListVal)s).Elements.Clear();
            return VoidVal.Instance;
        });
        // 排序**交回新的**(和 Map/Where 那批一个规矩),稳定的,比不了当场说人话(见 SortByKey)
        List.DefineMethod("Sort", (s, _) => SortByKey(((ListVal)s).Elements, x => x));
    }

    private static void RegisterSetMethods()
    {
        Set.DefineMethod("Add", (s, a) =>
        {
            ((SetVal)s).Elements.Add(a);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Remove", (s, a) =>
        {
            ((SetVal)s).Elements.Remove(a);
            return VoidVal.Instance;
        });
        Set.DefineMethod("Clear", (s, _) =>
        {
            ((SetVal)s).Elements.Clear();
            return VoidVal.Instance;
        });
        // 集合代数(C# 的 HashSet 同名方法):交回**新的 set**,原集合不动
        Set.DefineMethod("Union", (s, a) => new SetVal([.. ((SetVal)s).Elements, .. ElementsOf(a, "Union")]));
        Set.DefineMethod("Intersect", (s, a) =>
        {
            var other = ElementsOf(a, "Intersect").ToHashSet();
            return new SetVal([.. ((SetVal)s).Elements.Where(other.Contains)]);
        });
        Set.DefineMethod("Except", (s, a) =>
        {
            var other = ElementsOf(a, "Except").ToHashSet();
            return new SetVal([.. ((SetVal)s).Elements.Where(x => !other.Contains(x))]);
        });
        Set.DefineMethod("IsSubsetOf", (s, a) =>
        {
            var other = ElementsOf(a, "IsSubsetOf").ToHashSet();
            return new BoolVal(((SetVal)s).Elements.All(other.Contains));
        });
    }

    /// <summary>字典的键**只能是值类型**(数 / 字符串) —— 这里是唯一的把关处。
    ///
    /// 为什么限死在这一支:.NET 的 `Dictionary` 要一个**同步**的比较器,而用户写的 `Key ()`
    /// 是 Ravel 函数(调它得推帧,`Interpreter.CallInto` 那条路)。要拿对象当键,走
    /// `lib/keys.rav` 那层(`Keys.Set` / `Keyed`)—— 它先把对象规范成一个值类型再进表。</summary>
    internal static RuntimeValue KeyArg(RuntimeValue a, string what)
        => a.Type.IsAssignableTo(ValueType)
            ? a
            : throw new RuntimeException($"{what}得是值类型（数 / 字符串），得到 {a.Type}");

    /// <summary>字典的**底层**操作,名字一律带 `Sys` —— 它们只吃**值类型**键(见 <see cref="KeyArg"/>),
    /// 因为这里是同步的 C#:`Key ()` 是 Ravel 函数,调它要推帧,这儿调不了。
    ///
    /// 日常敲的 `d.Get k` / `d.Set k v` 是**库注入**上来的(`lib/keys.rav` 的 `IDict` 接口槽):
    /// 它先把键**规范**成值类型、再转到这几个 `Sys*` 上 —— 于是"内容一样的两个对象"是同一条。
    /// **名字腾出来是前提**:成员查找先看类链,链上没有 `Get` 才轮到接口槽(`TraitSlot`)。
    /// `Keys` / `Values` / `HasValue` / `Count` / `Clear` 不吃键,不用注入,就留在这一层。
    /// </summary>
    private static void RegisterDictMethods()
    {
        Dict.DefineMethod("SysGet", (s, a) =>
        {
            var key = KeyArg(a, "dict.SysGet 的键");
            var d = (DictVal)s;
            if (d.Entries.TryGetValue(key, out var v)) return v;
            throw new RuntimeException($"键不存在: {key}", ErrorKind.Key);
        });
        Dict.DefineMethod("SysSet", (s, a) =>
        {
            var key = KeyArg(a, "dict.SysSet 的键");
            return FunctionVal.From(v =>
            {
                ((DictVal)s).Entries[key] = v;
                return VoidVal.Instance;
            });
        });
        Dict.DefineMethod("SysHas", (s, a) =>
            new BoolVal(((DictVal)s).Entries.ContainsKey(KeyArg(a, "dict.SysHas 的键"))));
        Dict.DefineMethod("Keys", (s, _) => new ListVal([.. ((DictVal)s).Entries.Keys]));
        Dict.DefineMethod("Values", (s, _) => new ListVal([.. ((DictVal)s).Entries.Values]));
        Dict.DefineMethod("SysRemove", (s, a) =>
            // 有没有删掉(C# 也交回 bool)
            new BoolVal(((DictVal)s).Entries.Remove(KeyArg(a, "dict.SysRemove 的键"))));
        Dict.DefineMethod("HasValue", (s, a) => new BoolVal(((DictVal)s).Entries.ContainsValue(a)));
        // `d.GetOr "k" 0` —— 有就取值,没有就给替代值(C# 的 GetValueOrDefault)。
        // 默认的 `Get` 是**响亮**的(键不存在就报错),这个是"明知可能没有"时用的。
        Dict.DefineMethod("SysGetOr", (s, a) =>
        {
            var key = KeyArg(a, "dict.SysGetOr 的键");
            var d = (DictVal)s;
            return d.Entries.TryGetValue(key, out var v)
                ? FunctionVal.From(_ => v)        // 有:替代值收下但不用
                : FunctionVal.From(fallback => fallback);
        });
        Dict.DefineMethod("Clear", (s, _) =>
        {
            ((DictVal)s).Entries.Clear();
            return VoidVal.Instance;
        });
    }

    private static void RegisterTypeMethods()
    {
        // 这里**不能**再注册 `name` 方法:类名现在是类对象 Scope 里的 `name` 成员
        // (见 ObjectVal.Name),和它同住一个作用域的方法会把它覆盖掉。
        // 而且那个方法本来就是死代码 —— GetMember 的 `name` 伪成员排在方法查找之前,
        // 读 `int.name` 永远走伪成员,够不着方法。读走伪成员、写走成员赋值,行为不变。
        //
        // `Parent ()` 那个**方法**也删掉了:它和 `parent` 成员是同一格数据
        // (`C.parent` / `C.Parent ()` 都返回 `Object`),留两个名字只会让人以为
        // 一个给类、一个给实例。要读父类就是 `C.parent`(机制成员,类自己那层;
        // 实例上读不到 —— 它不沿链继承)。
        // `Is` 这个**方法**删掉了:它问类型、`is` 问值,两个长得像的东西各管一头,
        // 最常踩的是拿实例去调(`C.Is (C ())` 从前静默给 false,读起来还像"这个实例是不是 C")。
        // 要问类型之间的关系就用运算符:`int <: object` / `object :> int`(两边都得是类型);
        // 要问值就用 `x is T`。
        // 类型与接口之间的两个方向:`T.GetImplements ()`(这个类型实现了哪些接口)与
        // `I.GetImplementors ()`(哪些类型实现了这个接口)。实现登记在作用域里,所以真正算它们的
        // 是求值器 —— 这里只挂上那个"绑好接收者、调用时由 CallInto 认出"的成员值。
        Type.DefineMethod("Default", (s, _) => ConvertDirect((ObjectVal)s, DefaultVal.Instance));
        // 挂在 `Type` 的**实例表**上:`Option.GetImplements ()` 里的 `Option` 是 `type` 的实例,
        // 沿 `parent` 往上兜到这一张(`Option` 自己的表里没有,`Type` 那张里有)✓
        EngineMember(Type, "GetImplements", new TraitQuery("GetImplements", TraitQueryKind.Implements));
        EngineMember(Type, "GetImplementors", new TraitQuery("GetImplementors", TraitQueryKind.Implementors));
        Type.DefineMethod("Subtypes", (s, _) =>
        {
            var t = (ObjectVal)s;
            var subs = new List<RuntimeValue>();
            foreach (var sub in AllTypes)
            {
                if (sub != t && sub.IsAssignableTo(t))
                    subs.Add(sub);
            }

            return new ListVal(subs);
        });
    }

    /// <summary>取接收者当函数用。**不能硬转 `(FunctionVal)s`**:类对象往上查到 `Function` 那层
    /// (type <: function),所以 `Fields ()` 会把 Function 的方法列成类的可用方法,
    /// 而类对象是 `ObjectVal` —— 硬转就抛 InvalidCastException 漏到顶层。
    /// 换句话说,这是 BoolVal 那个「类型说有、值却接不住」的**反向**同款。</summary>
    private static FunctionVal AsFunction(RuntimeValue s, string what)
        => s as FunctionVal ?? throw new RuntimeException($"{what} 只对函数有意义，{s.Type} 不行");

    private static void RegisterFunctionMethods()
    {
        // Name 落在成员表里(ObjectVal.Name),函数和类对象都有,不用转
        Function.DefineMethod("Name", (s, a) =>
        {
            var f = (ObjectVal)s;
            if (a is StringVal sv)
            {
                f.Name = sv.Value;
                return VoidVal.Instance;
            }

            return new StringVal(f.Name ?? "");
        });
        // 捕获作用域是**函数独有**的:对象没有捕获作用域,它那半边叫 MemberScope。
        // 没有捕获作用域的值(类对象不是闭包)给一个**空 Scope** —— **类型恒定是 Scope**,
        // 不拿 `()` 顶替,也不报错:`x.scope ()` 拿到的东西该始终能当作用域用。
        Function.DefineMethod("Scope", (s, _) => new ScopeVal(AsFunction(s, "Scope").CaptureScope ?? new Scope()));
        // body:这个函数/类的**体**。同样类型恒定是 Block —— 没有体的(内置方法、
        // 原生闭包、没类体的内建类)给一个**空块**,而不是 `()`:`f.body` 始终能当块用
        // (`(f.body) 1` 就是跑一遍它)。
        Function.DefineMethod("Body", (s, _) => s switch
        {
            LambdaVal lam => new BlockVal(lam.Block, lam.CaptureScope),
            ClassVal c => c.ClassBody ?? EmptyBody,
            BlockVal b => b,
            _ => EmptyBody,
        });
        // 换作用域 / prepend / append 只有真函数能做(类对象的作用域是它的实例作用域,不能换)
        Function.DefineMethod("SetScope", (s, a) =>
        {
            if (a is not ScopeVal sv) throw new RuntimeException("setScope 需要 Scope 参数", ErrorKind.Argument);
            AsFunction(s, "SetScope").CaptureScope = sv.Inner;
            return VoidVal.Instance;
        });
        Function.DefineMethod("Prepend", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("prepend 需要代码块参数", ErrorKind.Argument);
            return AsFunction(s, "Prepend").Prepend(p);
        });
        Function.DefineMethod("Append", (s, a) =>
        {
            if (a is not BlockVal p) throw new RuntimeException("append 需要代码块参数", ErrorKind.Argument);
            return AsFunction(s, "Append").Append(p);
        });
    }

    private static void RegisterScopeMethods()
    {
        ScopeType.DefineMethod("Push", (s, _) =>
        {
            var scope = ((ScopeVal)s).Inner;
            return new ScopeVal(scope.Push());
        });
        ScopeType.DefineMethod("Define", (s, a) =>
        {
            if (a is not StringVal name)
                throw new RuntimeException("scope.Define 需要字符串名称", ErrorKind.Argument);
            var scope = ((ScopeVal)s).Inner;
            return FunctionVal.From(tv =>
            {
                if (tv is not ObjectVal t)
                    throw new RuntimeException("scope.Define 需要 type 参数", ErrorKind.Argument);
                scope.Define(name.Value, t, VoidVal.Instance);
                return VoidVal.Instance;
            });
        });
        ScopeType.DefineMethod("Lookup", (s, a) =>
        {
            if (a is not StringVal name)
                throw new RuntimeException("scope.Lookup 需要字符串参数", ErrorKind.Argument);
            return Wrap(((ScopeVal)s).Inner.Lookup(name.Value));
        });
        ScopeType.DefineMethod("Variables", (s, _) =>
        {
            var scope = ((ScopeVal)s).Inner;
            var d = new Dictionary<RuntimeValue, RuntimeValue>();
            foreach (var kv in scope.Variables)
            {
                if (kv.Key is ObjectVal.ThisMember or ObjectVal.BlockMember or ObjectVal.ThisTypeMember) continue;
                d[new StringVal(kv.Key)] = Wrap(kv.Value);      // 变量名本来就是字符串
            }

            return new DictVal(d);
        });
    }

    /// <summary>**属性的默认值** —— 一对什么都不做的函数:
    ///
    ///     Get = () => { (); }
    ///     Set = (_ : object) => { (); }
    ///
    /// 和 `int default` 给 `0`、`function default` 给空函数是同一个道理:
    /// `by a: int = default` 里那个 `default` 就是它 —— 槽里先放一个**能读能写、
    /// 但什么都不做**的属性(读出来是 `()`、写进去丢掉),之后用 `by a = property g s`
    /// 把真实现换上。
    ///
    /// 它**没有状态**,所以 `with` / `Copy ()` 让副本和原件共享同一个也无害
    /// (`CopyScope` 那条复制路不用为它做任何事)。</summary>
    internal static PropertyVal DefaultProperty()
        => new(FunctionVal.From(_ => VoidVal.Instance), FunctionVal.From(_ => VoidVal.Instance));

    /// <summary>把一个变量包成 property(getter 读、setter 写),attrs 原样带上。
    /// `scope.Lookup` 和 `scope.Variables` 都要这一套 —— 从前各写了一遍。
    ///
    /// 直接收 `Variable`(而不是名字)是故意的:`Variables` 正在遍历的那份变量就是它,
    /// 再 `Lookup` 一次等于把同一个名字查两遍(而且是沿链查,不是查那层那一格)。</summary>
    private static PropertyVal Wrap(Variable vr)
        => new(
            FunctionVal.From(_ => vr.Value),
            FunctionVal.From(v =>
            {
                vr.Assign(v);
                return VoidVal.Instance;
            }),
            vr);

    private static void RegisterPropertyMethods()
    {
        // attrs 只有一份,在变量上(见 PropertyVal.Var);裸的 property 没挂变量 -> 空表
        Property.DefineMethod("Attrs", (s, _) =>
            new ListVal([.. (((PropertyVal)s).Var?.Attrs ?? []).Select(a => new StringVal(a))]));
    }

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
        ListVal l => new ListVal([.. l.Elements], CopyScope(l.Scope)),
        SetVal s => new SetVal([.. s.Elements], CopyScope(s.Scope)),
        DictVal d => new DictVal(new Dictionary<RuntimeValue, RuntimeValue>(d.Entries), CopyScope(d.Scope)),
        // Json 值:**浅拷**——那棵 `JToken` 树一起用(Ravel 这边没有改它的手段,`Get`/`At`
        // 也是包个新 Json 指向同一棵树),要拷的是**成员表**(副本上 `tag = …` 才找得到)
        JsonVal j => new JsonVal(j.Token, CopyScope(j.Scope)),
        // 必须排在 ObjectVal 之前:这四类现在也是 ObjectVal
        FunctionVal or ModuleVal or PropertyVal or ScopeVal => v,
        ObjectVal o => CopyObject(o),
        _ => v,
    };

    /// <summary>拷贝一个对象:新实例 scope(方法闭包重绑,见 CopyScope)+ `this` 指向副本。
    /// 漏掉重绑 `this` 的话,副本里写 `this.v = n` 会落到原对象上,
    /// 而裸写 `v = n` 却是对的,行为自相矛盾。</summary>
    internal static ObjectVal CopyObject(ObjectVal src)
    {
        var copy = new ObjectVal(src.ClassType, CopyScope(src.Scope));
        copy.Scope.DefineOrReplace(ObjectVal.ThisMember, src.ClassType, copy);
        return copy;
    }

    internal static Scope CopyScope(Scope src)
    {
        var dst = new Scope(src.Parent);
        foreach (var kv in src.Variables)
        {
            var value = kv.Value.Value;
            // 方法(lambda)的闭包 Scope 指向原实例。浅拷贝共享它的话,
            // 在副本上调方法会读写到原对象的字段——`with` 就白拷了。
            value = Rebind(value, dst);
            // attrs 也要带过去:丢了 by 的话副本上的 `v = x` 不走 setter,
            // 丢了 readonly/private/core 就等于副本绕过了这些约束
            var v = dst.Define(kv.Key, kv.Value.TypeConstraint, value);
            foreach (var a in kv.Value.Attrs) v.SetAttr(a);
        }

        return dst;
    }
}
