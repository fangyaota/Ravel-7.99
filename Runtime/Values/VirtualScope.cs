using System.Collections.Generic;
using System.Linq;

namespace Ravel.Runtime.Values;

public class VirtualScope(RuntimeValue owner, bool writable = false) : Scope
{
    protected RuntimeValue Owner { get; } = owner;

    protected bool Writable { get; } = writable;

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

    public override void Assign(string name, RuntimeValue value)
    {
        if (!Writable)
            throw new RuntimeException($"值类型的成员表是只读的：不能改写 '{name}'", ErrorKind.Access);

        var slot = LookupField(name);
        if (slot is null)
            throw new RuntimeException($"无法给未定义变量 '{name}' 赋值", ErrorKind.Name);

        slot.Assign(value);
    }
}

internal sealed class MemberTable(ObjectVal self) : VirtualScope(self, writable: true);

internal class ValueTable(RuntimeValue value) : VirtualScope(value);
