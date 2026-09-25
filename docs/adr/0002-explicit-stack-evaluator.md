# ADR-0002: 显式持久帧栈求值器 —— 替换 CPS trampoline

- **状态**: Accepted（已实现，测试全绿）
- **日期**: 2026-08-14

## 背景

原求值器是 CPS trampoline：`Step`（Done/More/Escape/Error/CallCC）+ `Then`/`OrElse`/`Finally` 组合子 + `Step.Run` while 循环。压测发现**深度函数递归爆栈**（~4k 层即溢出）：`Then`/`Finally` 的延迟闭包链在 C# 栈上层层嵌套，没有真正被 trampoline 迭代。`while` 循环安全（每轮经 `ToMore` 延迟），但纯递归（经 lambda body 的 `ToMore` + 内部 `Then` 直连）会嵌套。

老 Ravel（`D:\Codes\Ravel Old`）用的**显式持久化调用栈**天然无此问题：帧是堆上的不可变链表，C# 栈恒平。决定移植这个架构。

## 决策

### 1. 帧 = 不可变持久链

```
Frame (Parent, Scope, Results)
├── NodeFrame        求值一个 AST 节点（语句/表达式）
├── BlockExecFrame   执行一个代码块（逐语句）
└── ControlFrame     控制帧（with/callcc/using/eval/alternate/class-init/compose/class-op/call-assign/call-return/ctor-apply）
```

> 控制帧清单后来缩过：`if`/`while`/`foreach` 已搬进 `lib/predefined.rav` 用 Ravel 实现
> （条件靠可调用的 `true`/`false`，循环靠 callcc 跳转），`ControlKind` 因此只剩 11 个值。
> `ctor-apply` 是后来加的（构造器柯里化）。详见 `CONTEXT.md` 的「控制流」一节。

- `Results` 是持久化单链表（`RList`），`WithResult` 生成新帧共享父节点。
- **Scope 可变**（`ravel` 模块切换 `SetAmbientScope` 换 scope），Parent/Results 不可变。
- 不可变 → callcc 捕获 = 存帧链引用，多发射天然支持。

### 2. StepOnce() 扁平循环

```csharp
_top = new BlockExecFrame(整个程序) { Scope = _rootScope };
while (_top != null) StepOnce();
```

- 整个程序一个块根帧——**帧链包含剩余语句**，callcc 续延才能完整恢复。
- 每帧按 `Results.Count`（已完成子结果数）走状态机：count 0 → 推子帧；count N → 算结果 `Return`（结果交给父帧）。

### 3. 调用分派统一

`CallInto(sink, fn, arg)` 按值类型分派：

| 值 | 调用行为 |
|---|---|
| `BuiltinMethodVal`/同步内建 | `Trampolined(arg)` 拿值 |
| `LambdaVal` | 推 body 帧（scope 绑 self+param） |
| `BlockVal` | 推块执行帧 |
| `TypeVal`（`Body != null`） | 推 ClassInit 构造帧 |
| `ControlFunction` | 收满参数 → 推控制帧 |
| `ContinuationVal` | 还原帧链 |
| `BoundClassOp` | 推 ClassOp 帧（动态找实例里同名的运算符成员） |
| `ComposeVal`（prepend/append） | 推 Compose 帧 |

### 4. 控制内建 = 纯数据

`if`/`while`/`with`/`foreach`/`callcc`/`using`/`eval` 是接收代码块的内建，必须**驱动块执行**（与「算值返回」的普通内建有本质区别）。控制逻辑集中在求值器（`StepIf`/`StepWhile`/…），函数值是数据：

```csharp
ControlFunction(Kind, Arity, Args)   // Kind=控制种类, Arity=参数个数, Args=已收集块
```

`((if c) t) e` 逐层调用累积块（`[c]`→`[c,t]`→`[c,t,e]`），**收满才推 If 控制帧**——与所有柯里化函数「接到所有参数后再调用」一致，只是中间阶段是数据而非闭包。备选「闭包持求值器」（闭包直接推帧）被否：控制逻辑散布闭包、帧不再纯数据、需「已推帧」标记约定。

### 5. callcc 单发/多发

- **单发**（`CallccActive`，续延在 body 内调用）：`k.Captured.WithResult(arg)` → abort body，callcc 返回 arg 给消费者（完整恢复，程序继续）。
- **多发**（body 外调用）：**重跑局部 onDone**（值消费者帧消费 arg，如重定义 `first`）+ **把 arg 返回给调用者**，不重跑程序剩余部分。对照旧实现压测确认。

## 后果

### 删除

- `Step.cs` 精简为只剩 `Done`（同步调用结果）。
- `Interpreter.Expr.cs` 整文件删除。
- `Then`/`OrElse`/`Finally`/`ToMore`/`EvalLambda`/`EvalBlockStmts`/`EvalWhile`/`EvalIf`/`EvalCallCC` 全部移除。
- `MakeInstanceInitializer`（CPS 版）移除 → 类实例化走 `StepClassInit` 控制帧。

### 新增

- `Runtime/RList.cs`、`Runtime/Frame.cs`、`Runtime/Interpreter.Stack.cs`（求值器核心）。
- `Values/LambdaVal.cs`（lambda 数据化：Param+Body+闭包 scope）、`Values/ControlFunction.cs`、`Values/ComposeVal.cs`。

### 收益

- **深度递归 20 万层通过**（旧实现 ~4k 层爆栈），`while` 100 万次通过。

> 补记：20 万层是 **C# 调用栈**不爆，内存还是要的——每层约 1KB，20 万层 ≈ 250MB。
> 没有尾调用优化，所以它在默认堆下能过、在测试用的 256MB 上限下会 OutOfMemory。
- 31 passed / 0 failed / 18 todo（测试已按特性合并），行为与旧实现完全一致。
- callcc 多发射语义保留（85-89 测试）。

### 取舍

- 控制内建是一种特殊化，但它是「帧纯数据 + 控制逻辑在求值器」的最小表达。替代（闭包持求值器 / 恢复语法节点）均被否。
- `Scope` 可变是 `ravel` 模块切换的代价（callcc 捕获共享可变 scope，与旧设计一致）。
