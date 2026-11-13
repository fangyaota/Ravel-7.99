# `===`：同一性判断

## 需求（用户原话）

> 添加===作为引用相等比较，拒绝重载。

## 语义

| 两边是 | `===` 交回 |
|--------|-----------|
| 运行期类型不同 | `false` —— 不做跨族归一，`1 === 1.0` 为 `false`（`1 == 1.0` 仍为 `true`）|
| 值语义（`IValue` 索引里的：数值、布尔、字符串、字符）| 比值，等价于同类型下的 `==` |
| 对象（列表、集合、字典、类实例、函数、模块、类型）| 比**引用**：`xs === xs` 为 `true`，`[1 2] === [1 2]` 为 `false` |

与 `==` 同档（10），因此能串：`a === b === c` 读作 `(a === b) && (b === c)`。

## 实现落点

| 文件 | 改动 |
|------|------|
| `Syntax/TokenType.cs` | 加 `EqualEqualEqual` |
| `Syntax/Lexer.cs` | `("===", …)` 排在 `("==", …)` **前面**，否则读成 `==` + `=` |
| `Syntax/OperatorTable.cs` | `Add("===", 10, …)`；`IsCompare` 加 `"==="`（让它进链式比较）|
| `Syntax/Parser.Statements.cs` | `IsOperatorToken` 收 `EqualEqualEqual`（能被 `x.===` 取到）；`ParseOperatorDefinition` 显式拒绝并给文案 |
| `Runtime/BuiltinClasses.Operators.cs` | `StrictSame(a, b)`：类型不同 false；`ObjectVal` 且类型不是 `ClassVal.IsValueLike` 的比引用，其余 `Equals` |
| `Runtime/Interpreter.Binary.cs` | `StepBinaryOp` 里在 `BindOperator` **之前**分派 `===` |

## 为什么拒绝重载

`===` 不进 `OperatorSymbols.All`，也不进成员表 —— 同 `:`：判定的含义由引擎保证。类体里写 `=== := …` / `=== = …` 在解析期就报：

```
'===' 不支持自定义 —— 它是内建的同一性判断，和 `:` / `!` / `&&` / `||` 一样
```

## 测试与文档

- 用例：`tests/class/64_operator_class.rav` 新增 `ravel "64_identity"`（12 条：值/跨族/字符串/列表身份/字典/类实例/链式/拒绝重载）。
- 文档：`docs/tutorial/03-operators.md` 新增 3.2「同一性 `===`」，`docs/tutorial/11-cheatsheet.md` 一行。

## 没做

- `!==`：用 `!(a === b)`；真要加再说。
- 让容器 `==` 变结构相等：那是一次语义变更（会动 `==` 的期望），不混在这次里。
