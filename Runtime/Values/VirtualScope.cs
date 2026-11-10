using System.Collections.Generic;
using System.Linq;

namespace Ravel.Runtime.Values;

/// <summary>**成员表**：一个对象、一个值,各自一张。
///
/// 读一个成员名 `n` 的顺序只有这一条:
///   1. 本层**已经造好**的那一格(读过一次就有了);
///   2. <see cref="Create"/> —— "我是谁"自己按名字造一格(值类型在这儿放运算符、`ToString`、`init`);
///   3. 类注册表(<see cref="BuiltinClasses.InstanceMemberOf"/>)—— "我这个类型,引擎给我准备了什么";
///   4. null(调用方去报"没有方法")。
///
/// **没有第二次跳转**:从前借类链读(`MemberView` 沿 `parent` 一节一节问 `InstanceTable`),
/// 现在"类型给我什么"是建表时/收尾时**并进注册表**的一次性合并,读的时候一次就够
/// (见 `BuiltinClasses.InheritTable`)。父类、接口、祖先链在运行期一个都不走。
///
/// 写权限由 <see cref="Writable"/> 分两种:对象/类对象的表可写;值的表只读
/// (往一份共享的注册表里写等于改掉整个类型)。</summary>
/// <param name="owner">这张表是**谁的**成员表 —— `Owner.Type` 决定问哪个类的注册表。</param>
/// <param name="writable">能不能往这张表里定义/改写。</param>
public class VirtualScope(RuntimeValue owner, bool writable = false) : Scope
{
    protected RuntimeValue Owner { get; } = owner;

    /// <summary>这张表可写吗。值 = 只读:它们的成员定义是所有值共享的,写进去等于改掉类型。</summary>
    protected bool Writable { get; } = writable;

    /// <summary>按名字**造**一格成员。造不出来(这个名字不归我)返回 null,让查找继续往下。
    ///
    /// 只在"真的有人来读"的时候调 —— 值是懒的:`(5).+` 读到 `+` 才造那一格,
    /// 一个只参与算术的整数连一张表都不建(<see cref="RuntimeValue.MemberScope"/> 那边懒)。</summary>
    protected virtual Variable? Create(string name) => null;

    /// <summary>"我这一层**本来就有**哪些名字"(不管造没造出来)。值类型在这里列它的静态清单;
    /// 对象那层返回空 —— 对象的名单就是"已经存下来的那些"。</summary>
    protected virtual IEnumerable<string> AllNames => [];

    /// <summary>造出来/从注册表取来的那几格。**不进 `_vars`**:`_vars` 是"这个对象自己
    /// 有的成员",注册表那份是全类共享的 —— 混进去就等于"读一次 `MemberScope` 就白得一个
    /// 成员"(实测:`Variables ()` 里会多出 `MemberScope`,`Fields ()` 也会)。</summary>
    private readonly Dictionary<string, Variable> _made = [];

    public override Variable? LookupField(string name)
    {
        if (Stored(name) is { } here) return here;
        if (_made.TryGetValue(name, out var cached)) return cached;

        if (Create(name) is { } made)
        {
            _made[name] = made;
            return made;
        }

        // "我是谁"自己管:类型带着一份成员(见 `ClassVal.Members`),按名字造一格。
        if (Owner.Type is ClassVal type)
        {
            if (type.Members?.Create(type, name) is { } own) { _made[name] = own; return own; }

            // 值类型那条 `init`:**值不会被实例化**,类体跑不到它们头上,
            // 所以构造器得由**它自己那份**成员直接交出来(可见性同 `PresetCtor` 那条)。
            if (name == ObjectVal.InitMember && type.IsValueLike && type.Members?.Ctor is { } ctor)
            {
                var slot = new Variable(name, BuiltinClasses.Function, ctor);
                slot.SetAttr(Attr.Protected);
                _made[name] = slot;
                return slot;
            }

            // 过渡:注册表还没迁完的那几类。**排在 `Object` 前面** ——
            // 插件类往注册表里挂的 `ToString` 要盖掉 `Object` 的那一条。
            if (BuiltinClasses.InstanceMemberOf(Owner.Type, name) is { } registered)
            {
                _made[name] = registered;
                return registered;
            }

            // 最后才是 `Object` 那一套:一切类的公共尾巴。
            if (ObjectMembers.Any.Create(type, name) is { } inherited)
            {
                _made[name] = inherited;
                return inherited;
            }
        }

        return null;
    }

    /// <summary>列名单:`AllNames`(值类型的静态清单)→ 已存的格子 → 类型那份成员
    /// (自己的在前、父类的在后)→ `init`(值类型才有)→ 注册表(过渡)。
    /// 顺序稳定、去重(同一个名字在清单里和别处都有时取先出现的那个位置)。</summary>
    public override IEnumerable<string> MemberNames
    {
        get
        {
            var seen = new HashSet<string>();
            foreach (var name in AllNames)
                if (seen.Add(name)) yield return name;
            foreach (var name in StoredHere.Select(kv => kv.Key))
                if (name != ObjectVal.ThisMember && seen.Add(name)) yield return name;

            if (Owner.Type is ClassVal type)
            {
                if (type.Members is { } own)
                {
                    foreach (var name in own.Names)
                        if (seen.Add(name)) yield return name;
                    if (type.IsValueLike && own.Ctor is not null && seen.Add(ObjectVal.InitMember))
                        yield return ObjectVal.InitMember;
                }

                foreach (var name in BuiltinClasses.MemberNamesOf(Owner.Type))
                    if (seen.Add(name)) yield return name;

                foreach (var name in ObjectMembers.Any.Names)
                    if (seen.Add(name)) yield return name;
            }
        }
    }

    /// <summary>列举成员**值**:只列已经存在的格子,不为了列举去造。
    /// 从前这里是"对每个名字 `LookupField` 一遍"—— 列一次 `Fields ()` 等于把整张表全造出来。</summary>
    public override IEnumerable<KeyValuePair<string, Variable>> Variables => StoredHere;

    public override Variable Define(string name, ObjectVal typeConstraint, RuntimeValue initialValue,
                                    Statement? site = null)
        => Writable
            ? base.Define(name, typeConstraint, initialValue, site)
            : throw new RuntimeException($"值类型的成员表是只读的：不能定义 '{name}'", ErrorKind.Access);

    public override Variable DefineOrReplace(string name, ObjectVal typeConstraint, RuntimeValue initialValue)
        => Writable
            ? base.DefineOrReplace(name, typeConstraint, initialValue)
            : throw new RuntimeException($"值类型的成员表是只读的：不能改写 '{name}'", ErrorKind.Access);

    /// <summary>可写表的 `=`:**只认这张表里有的名字**。
    ///
    /// 不走词法父、不在这里新开遮蔽格 —— 成员表不是"嵌套作用域",它的父是"类写在哪"
    /// (类体执行时要用),往那儿赋值是把类的局部变量改掉,不是改成员。</summary>
    public override void Assign(string name, RuntimeValue value, Func<ObjectVal, bool>? alsoAccepts = null)
    {
        if (!Writable)
            throw new RuntimeException($"值类型的成员表是只读的：不能改写 '{name}'", ErrorKind.Access);

        var slot = LookupField(name);
        if (slot is null)
            throw new RuntimeException($"无法给未定义变量 '{name}' 赋值", ErrorKind.Name);

        slot.Assign(value, alsoAccepts);
    }

    // ---- 值类型那几张注册表用的三件工具 -------------------------------------------

    protected Variable Operator(string name, Func<RuntimeValue, RuntimeValue, RuntimeValue> impl)
        => Field(name, new BuiltinMethodVal(impl) { Name = name });

    protected Variable Method(string name, Func<RuntimeValue, RuntimeValue> impl)
        => Field(name, new BuiltinMethodVal((self, _) => impl(self)) { Name = name });

    protected Variable Field(string name, RuntimeValue value)
    {
        var v = new Variable(name, BuiltinClasses.Function, value);
        v.SetAttr(Attr.Readonly);
        return v;
    }
}

/// <summary>**对象**的成员表(含类对象):可写。构造时建一张空的,类体逐层把成员定义进这张表。</summary>
internal sealed class MemberTable(ObjectVal self) : VirtualScope(self, writable: true);

/// <summary>**值**的成员表:只读。名字来自值类型自己的静态清单,或它那个类的注册表。</summary>
internal class ValueTable(RuntimeValue value) : VirtualScope(value);
