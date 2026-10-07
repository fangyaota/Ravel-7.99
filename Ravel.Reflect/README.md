# Ravel.Reflect —— 让 Ravel 能反射 .NET

```ravel
using "plugins/Ravel.Reflect.dll"

t  := Reflect.Type "System.Console"
m  := Reflect.Type "System.Math"
print (Reflect.Call m "Sqrt" [16])            # 静态:第一个实参给**类型**

sb := Reflect.New (Reflect.Type "System.Text.StringBuilder") []
Reflect.Call sb "Append" ["hi"]               # 实例:第一个实参给**对象**
print (Reflect.Text sb)

print (Reflect.Call "hello" "Length" [])      # 属性也走 Call
```

## 门面:2 个类 + 5 个函数

| | |
|---|---|
| `DotNetType` | 一个 `System.Type`。`.Name ()` / `.FullName ()` / `.IsValue ()` / `.IsInterface ()` / `.Members ()` / `.Text ()` |
| `DotNetObject` | 一个任意 .NET 对象。`.TypeOf ()` / `.Text ()` / `.Unwrap ()` |
| `Reflect.Type name` | 名字 → `DotNetType`（核心库写 `"System.Console"`；别的程序集要**带程序集的全名**）|
| `Reflect.New t args` | 构造 |
| `Reflect.Call obj name args` | 调方法 / 读属性 |
| `Reflect.Text v` | `ToString ()`，Ravel 值也行 |
| `Reflect.Unwrap v` | 脱壳:.NET → Ravel（认识的就化成 Ravel 值）|

**两个类都别跟 Ravel 自己的名字撞**:`Type` 被占了（`x.Type` 是"它的类"），
所以一个叫 `DotNetType`、方法叫 `TypeOf`。挂错名字**不报错**，只是读不到。

## 规矩

**重载按"参数个数 + 绑得上"挑**:先按个数筛,再**依次试** —— 绑不上就换下一个。
`"3.7".Replace ["3" "4"]` 就是靠这条:它有两个 2 参数的重载 `(char, char)` 和
`(string, string)`,前者排在前面。

**被调方真抛的不吞**(`TargetInvocationException` 翻成人话);`ArgumentException`
才是"这个签名绑不上",那才换下一个。

## 值桥

| 方向 | 认识的 |
|---|---|
| Ravel → .NET | `int` `real` `bool` `string` `char` `()` `default` `list`(→ `object[]`) |
| .NET → Ravel | 上面那些 + 任何一串(→ `list`);**别的兜成 `DotNetObject`**,不报错 |

`.NET → Ravel` 兜底是有意的:反射回来一堆 `FileInfo` 是常态,当场炸掉没法用。

## 不在里面的(这一份是"最基本"的)

- **按参数类型逐个转**:`"a,b".Split [","]` 会失败 —— 它要 `char[]`,而桥只把
  `list` 转成 `object[]`。报错会说清给的是什么、要的是什么。
- **泛型类型实参**:`List<int>` 在 Ravel 层没有写法。
- **字典**:`dict` 不往 .NET 转（.NET 的 `IDictionary` 也不往回转,会兜成对象）。
- **委托方向 —— 硬边界**:引擎里写着三遍"原生闭包调不了 Ravel 函数(那是帧栈的活)",
  所以**把 Ravel 的 lambda 交给 C# 做不到**,只有反过来(.NET 委托 → Ravel 函数)才行。
  踩到这条时给的消息是专门的。
- **安全**:开了这个 = 任意 .NET(文件/网络/进程)。
