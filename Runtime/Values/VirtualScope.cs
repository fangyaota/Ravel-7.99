using System.Collections.Generic;
using System.Linq;

namespace Ravel.Runtime.Values;

/// <summary>**成员表**：一个对象、一个值,各自一张。
///
/// 读一个成员名 `n` 的顺序只有这一条:
///   1. 本层**已经造好**的那一格(读过一次就有了);
///   2. 本层记住的那些格子(`_made`,造过一次就不再造);
///   3. **类型自己**那份成员工厂(`ClassVal.Members`)—— "我这个类型准备了什么";
///   4. 值类型那条 `init`(值不会被实例化,构造器只能由类型自己交出来);
///   5. `Object` 那一套公共尾巴;
///   6. null(调用方去报"没有方法")。
///
/// **没有第二次跳转**:父类、接口、祖先链在运行期一个都不走 —— 继承是成员工厂自己
/// 顺着链问(`MemberSet` 的 `_parent`),问一次就到底。
///
/// 写权限由 <see cref="Writable"/> 分两种:对象/类对象的表可写;值的表只读
/// (值的成员是整个类型共享的,写进去等于改掉类型)。</summary>
/// <param name="owner">这张表是**谁的**成员表 —— `Owner.Type` 决定问哪个类型的成员集。</param>
/// <param name="writable">能不能往这张表里定义/改写。</param>
public class VirtualScope(RuntimeValue owner, bool writable = false) : Scope
{
    protected RuntimeValue Owner { get; } = owner;

    /// <summary>这张表可写吗。值 = 只读:它们的成员定义是所有值共享的,写进去等于改掉类型。</summary>
    protected bool Writable { get; } = writable;

    /// <summary>从类型那份成员工厂造出来的那几格。**不进 `_vars`**:`_vars` 是"这个对象
    /// 自己有的成员",这几格是全类型共享的 —— 混进去就等于"读一次 `MemberScope` 就白得一个
    /// 成员"(实测:`Variables ()` 里会多出 `MemberScope`,`Fields ()` 也会)。
    ///
    /// 造是**懒**的:`(5).+` 读到 `+` 才造那一格,一个只参与算术的整数连一张表都不建
    /// (<see cref="RuntimeValue.MemberScope"/> 那边懒);造过就记住,同一个值读第二次不再造。</summary>
    private readonly Dictionary<string, Variable> _made = [];

    public override Variable? LookupField(string name)
    {
        if (Stored(name) is { } here) return here;
        if (_made.TryGetValue(name, out var cached)) return cached;

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

            // 最后才是 `Object` 那一套:一切类的公共尾巴。
            if (ObjectMembers.Any.Create(type, name) is { } inherited)
            {
                _made[name] = inherited;
                return inherited;
            }
        }

        return null;
    }

    /// <summary>列名单:已存的格子 → 类型那份成员(自己的在前、父类的在后)→
    /// `init`(值类型才有)。顺序稳定、去重(同一个名字在别处也有时取先出现的那个位置)。</summary>
    public override IEnumerable<string> MemberNames
    {
        get
        {
            var seen = new HashSet<string>();
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
}

/// <summary>**对象**的成员表(含类对象):可写。构造时建一张空的,类体逐层把成员定义进这张表。</summary>
internal sealed class MemberTable(ObjectVal self) : VirtualScope(self, writable: true);

/// <summary>**值**的成员表:只读。名字和造法都来自它那个类型的成员工厂(`ClassVal.Members`)。</summary>
internal class ValueTable(RuntimeValue value) : VirtualScope(value);
