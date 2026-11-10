namespace Ravel.Runtime;

using System.Reflection;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class SysAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
