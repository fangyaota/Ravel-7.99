namespace Ravel.Runtime;

internal sealed class BodyScope(Scope lexical, Scope instance) : Scope(lexical)
{
    internal Scope Instance => instance;

    internal override Scope? InstanceScope => instance;

    protected override Variable? LookupHere(string name)
        => instance.OwnField(name) ?? base.LookupHere(name);

    public override Variable Define(string name, RuntimeValue initialValue, Statement? site = null)
        => instance.Define(name, initialValue, site);

    public override Variable DefineOrReplace(string name, RuntimeValue initialValue)
        => instance.DefineOrReplace(name, initialValue);
}
