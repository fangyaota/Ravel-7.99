# Ravel.Compile —— 把 C# 编成 dll（Roslyn 当库调）

```ravel
using "plugins/Ravel.Compile.dll"

src := "namespace Ravel.Runtime;
[RavelModule(\"NExtra\")]
internal static class NExtraAdaptor
{
    [RavelFn(\"Twice\")]
    public static RuntimeValue Twice (RuntimeValue x0)
        => PluginKit.Guarded (\"NExtra.Twice\", () => IntVal.Of (PluginKit.Int (x0, \"…\") * 2));
}"

Compile.Cs src "out/plugins/NExtra.dll" []      # 第三个参数：额外要引用的 dll（给 [] 就是不要）
```

## 门面：1 个函数

| | |
|---|---|
| `Compile.Cs 源码 输出路径 额外引用` | 源码 → dll。编不过**当场把诊断说出来**（带文件、行、列）|

## 为什么要有它

`dotnet build` 那条路要求机器上**有 SDK**。而 `out/` 是 **framework-dependent** 发布
—— 只有 runtime 的机器上 `dotnet build` 这个命令根本不存在。生成器要在那种机器上
把「`.rav` → `.cs` → `.dll`」走完，编译器就得带在身边。

于是在 `tools/mkadaptor.rav` 里成了两条路，按"现场有没有 SDK"挑：

```bash
# 有 SDK（在仓库里）
dotnet out/ravel.dll tools/mkadaptor.rav System.Math NMath Max Min Sqrt
bash build.sh

# 只有 out/（跟着 runtime 一起发的）
dotnet out/ravel.dll tools/mkadaptor.rav System.Math NMath Max Min Sqrt \
    --dll out/plugins/Ravel.Generated.dll
```

两条路编出来的是**同一个东西**：同一个 `.cs`、同一个模块名。

## 引用集是从当前进程取的

这条比"生成一个 csproj 再 restore"干净得多：

| 要引用谁 | 从哪儿取 |
|---|---|
| 框架那一套 | `TRUSTED_PLATFORM_ASSEMBLIES`（跑着的这个 runtime 的完整清单）|
| 引擎自己 | `typeof(RuntimeValue).Assembly.Location` |
| 要适配的第三方库 | 调用方从**第三个参数**传 —— **它多半已经在进程里了**：生成器刚用 `Reflect.Type` 把它加载过，`Assembly.Location` 现成 |

于是**编出来的 dll 引用的就是跑着的那个 `Ravel.dll`** —— 版本天然对齐，
而 `dotnet build` 那条路反而没这个保证（它编的可能指向另一份副本）。

## 代价

这一包 **9.5 MB**（`Microsoft.CodeAnalysis` + `Microsoft.CodeAnalysis.CSharp`），
所以它单开一个插件 —— 用不上的场合 `using` 都别加。

（`Ravel.Compile.csproj` 上那个 `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`
不能省：**库项目默认不把 NuGet 依赖拷到自己的 bin**，主项目那个通配就扫不到
`Microsoft.CodeAnalysis*.dll`，运行期是一句 `FileNotFoundException`。）

## 不在里面的

- **`unsafe`**：没开。
- **自动加引用**：不会去猜你要哪个 dll —— 要就自己传。宁缺毋滥，
  猜错的话症状是"编出来的 dll 引用了一个你没打算要的版本"。
- **加载**：它只管**编出来**。装上还是 `using "路径/xxx.dll"` 那条路。
