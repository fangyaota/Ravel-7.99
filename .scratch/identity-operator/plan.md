# `===` / `!==`：同一性判断

## 需求（用户原话）

> 添加===作为引用相等比较，拒绝重载。

随后改成：

> 都要进入成员表，是普通的运算符

即：语义（同一性）不变，但实现上它是 `Object` 成员表里的一条普通运算符，
与 `==` / `!=` 同级，可被类覆盖，不再走解释器特判、也不再在解析期拒绝。

## 语义

| 两边是 | `===` 交回 |
|--------|-----------|
| 运行期类型不同 | `false` —— 不做跨族归一，`1 === 1.0` 为 `false`（`1 == 1.0` 仍为 `true`）|
| 值语义（`IValue` 索引里的：数值、布尔、字符串、字符）| 比值，等价于同类型下的 `==` |
| 对象（列表、集合、字典、类实例、函数、模块、类型）| 比**引用**：`xs === xs` 为 `true`，`[1 2] === [1 2]` 为 `false` |

`!==` 是它的逐字取反（同样类型不同即 `true`）。

与 `==` 同档（10），因此能串：`a === b === c` 读作 `(a === b) && (b === c)`。

## 实现落点

| 文件 | 改动 |
|------|------|
| `Syntax/TokenType.cs` | 加 `EqualEqualEqual` / `NotEqualEqual` |
| `Syntax/Lexer.cs` | `("===", …)` / `("!==", …)` 排在 `("==", …)` / `("!=", …)` **前面**，否则只读到两个字符 |
| `Syntax/OperatorTable.cs` | `Add("===", 10, …)` / `Add("!==", 10, …)`；`IsCompare` 收它们（进链式比较）|
| `Syntax/Ast.cs` | `OperatorSymbols.All` 加 `"==="` / `"!=="`（`MemberSet.Scan` 装运算符时的校验名单）|
| `Syntax/Parser.Statements.cs` | `IsOperatorToken` 收这两个 token（`x.===` 取得出来、类体能写 `=== := …`）；`ParseOperatorDefinition` 不再拒绝 |
| `Runtime/Members/ObjectOperatorMembers.cs` | `[ClassOp("===")] Identical` / `[ClassOp("!==")] NotIdentical`，紧跟 `==` / `!=` 之后 |
| `Runtime/BuiltinClasses.Operators.cs` | `StrictSame(a, b)`：类型不同 false；`ObjectVal` 且类型不是 `ClassVal.IsValueLike` 的比引用，其余 `Equals` |
| `Runtime/Interpreter.Binary.cs` | 无特判 —— 与 `==` 一样走 `BindOperator` 查成员表 |

## 因为它是普通运算符，所以

- `xs.===` 能取到那个函数，`f := xs.===` 之后 `f xs` 是 `true`；
- 类里写 `Iffy ::= class { === := (o) => { "mine"; } }` 能让 `i === i` 交出 `"mine"`；
- 覆盖 `===` 不影响 `!==`（两条成员各是各的）；
- 代价：`Fields ()` 会多两格 —— `tests/lib/271_system_members.rav` 的 `System.Fields ().Count ()` 由 133 变 135，
  `tests/lib/280_native.rav` 的 69/51 变 71/53，`tests/class/64_operator_class.rav` 的 `Math.Fields ()` 由 55 变 57。

## 测试与文档

- 用例：`tests/class/64_operator_class.rav` 的 `ravel "64_identity"`（值/跨族/字符串/列表身份/字典/类实例/链式/成员取值/类覆盖）。
- 文档：`docs/tutorial/03-operators.md` 3.2「同一性 `===`」，`docs/tutorial/11-cheatsheet.md` 一行。
