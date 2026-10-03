# 十二、并发（`Tasks`）

`Tasks` 是**协作式任务**。**要显式引用**：`using "tasks.rav"`。

#### 实例

```ravel
using "tasks.rav"
Tasks.Cycle [
    (Tasks.Task (group: Tasks.TaskGroup) => {
        i := 0
        while { i < 3; } { i += 1; print ("  A" + (string i)); group.Await (Tasks.After 0); }
    })
    (Tasks.Task (group: Tasks.TaskGroup) => {
        i := 0
        while { i < 3; } { i += 1; print ("  B" + (string i)); group.Await (Tasks.After 0); }
    })
]
```

执行以上程序会输出如下结果：

```
  A1
  B1
  A2
  B2
  A3
  B3
```

**一个 OS 线程**，任务只在**挂起点**（`group.Await`）换人。于是等待能重叠（两个 400ms 的等待叠起来 ≈ 0.6s，串起来 0.8s 起），而**交替顺序是钉死的** —— 这是协作式的好处：谁先跑由代码决定，不是碰运气。

注意：`Tasks.Cycle [...]` 的元素要**加括号**。不加的话列表会把它读成"两件"：任务类 + 一个 lambda。

## 三种「会完」的东西

| 写法 | 等的是 |
|------|--------|
| `Tasks.Task (group) => { … }` | 「**那段代码跑完**」|
| `Tasks.After ms` | 「**那个计时器走完**」|
| `Tasks.Signal ()` | 「**别人跟我说一声**」|

前两种是绝大多数；第三种是"别人通知我"那类（见下面）。

## 结果在任务自己身上

组管调度，**不管收成** —— `Tasks.Cycle` / `Run ()` 交回 `()`。要结果就把任务先兜在变量里。

| 写法 | 意思 |
|------|------|
| `t.IsDone` | **不阻塞**，随手探 |
| `t.Value` | **检查过的读**：完了就交值，没完**报错**（它不等）|
| `group.Await t` | **唯一的挂起点**：等它，交回它的结果 |
| `group.Add t` | 往组里加一个（**不立刻跑**，排到队尾；跑着的时候也能加）|

#### 实例

```ravel
using "tasks.rav"
Tasks.Cycle [
    (Tasks.Task (group: Tasks.TaskGroup) => {
        a := Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After 0); "甲"; }
        b := Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After 0); "乙"; }
        print ("先等到 b:" + (string (group.Await b)))
        print ("a 的值:" + (string (group.Await a)))
    })
]
```

执行以上程序会输出如下结果：

```
先等到 b:乙
a 的值:甲
```

注意：**自创任务不用先 `Add`** —— `Await` 会顺手把它挂进这个组（"等到它完"就蕴含"得有人推它"，而此刻只有这一个组在跑）。

注意：**一个任务只能跑一遍**。它的续延是多发的，再跑一遍就是跳回上一次留下的那条帧链 —— `Add` 那一道闸会当场拦下来。

## 让一轮：`Tasks.After 0`

要"我先歇一下，让别人跑"就 `group.Await (Tasks.After 0)` —— 句柄**当场就算完成**，不碰计时器。

注意：**`After ms` 的精度是计时器的精度**，Windows 上约 **15 毫秒** —— `After 5` 和 `After 10` 谁先醒**说不准**（常常同一拍）。要分先后，间隔拉到几十上百毫秒。

## 三件常用活儿

#### 实例

```ravel
using "tasks.rav"
mk := (name: string ms: int) => { Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After ms); name; } }
Tasks.Cycle [
    (Tasks.Task (group: Tasks.TaskGroup) => {
        a := mk "甲" 0
        b := mk "乙" 0
        print ("All:" + (string (group.Await (Tasks.All [a b]))))
    })
]
```

执行以上程序会输出如下结果：

```
All:[甲 乙]
```

| 写法 | 交回 |
|------|------|
| `Tasks.All [t1 t2 …]` | **一起跑**，按**交进去的顺序**交回结果表 |
| `Tasks.Any [t1 t2 …]` | 等**最先完成的那个**，交回**它本身**（`.Value` 拿结果，也看得出是谁）|
| `Tasks.Chan ()` | 能等的队列：`Put` / `Take` / `Close` / `Count` / `IsShut` |

注意：`All` 的那个"一起跑"是字面意思 —— 它先把几个都排进队再收，所以它们**真的并着**。

注意：`Any` **输的那些照样跑完**（这门语言打不断已经出发的活儿）。它提前给你的是"结果"，不是"停掉其余的"。

注意：`Chan.Take` 空了就**真的挂起**，`Close ()` 把等着的人全放走，之后 `Take` **报错**（不是"取到空的"）。

## 等一件事的通知：`Signal`

`Tasks.Signal ()` 是一只**闸**：它自己不起任何作用，谁攥着它谁说了算。`Open` 一声，等在它上面的人全醒；一次性的（开过就一直开着）。

#### 实例

```ravel
using "tasks.rav"
sig := Tasks.Signal ()
Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { print ("甲:等到了 " + (string (g.Await sig))); })
    (Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After 0); sig.Open "甲的东西"; })
]
Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { print ("丙(开过才来):" + (string (g.Await sig))); })
]
```

执行以上程序会输出如下结果：

```
甲:等到了 甲的东西
丙(开过才来):甲的东西
```

**它也是"加一件常用活儿"该用的东西**：写能等的队列 / 事件 / 信号量的人摆一只闸就行，不用碰调度器的内脏。`examples/signal.rav` 里拿两版能等的队列对着看（手搓的那版两处伸手进调度器，用闸的那版一个字段都不碰 —— 跑出来一模一样）。

## `do` 块也能用（任务是 monad）

`Tasks` 在 `IMonad` 那一族里，所以 `do { x :< t; … }` 写得出来 —— 用 `Tasks.Return` 收尾。

#### 实例

```ravel
using "tasks.rav"
Tasks.Cycle [
    (Tasks.Task (group: Tasks.TaskGroup) => {
        t1 := Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After 0); 20; }
        t2 := Tasks.Task (g: Tasks.TaskGroup) => { g.Await (Tasks.After 0); 22; }
        print ("do:" + (string (group.Await (do { a :< t1; b :< t2; Tasks.Return (a + b); }))))
    })
]
```

执行以上程序会输出如下结果：

```
do:42
```

注意：**`do` 是串行的**（monad 的 `Bind` 本来就是一环扣一环）；要并发用 `Tasks.All`。两者不是替代关系 —— `do` 用于"后一件要前一件的结果"。

## 出错

- **任务里抛的错在"等它"那一头现形**（`group.Await` / `.Value`），一个任务把自己搞砸了**不会把整组带走**。
- **没人接的失败，组收尾时当场报**，而且**带出错位置**。
- **死锁**（互相等，或者等在没谁能完成的东西上）也**当场报**，不干等。

#### 实例

```ravel
using "tasks.rav"
bad := Tasks.Task (g: Tasks.TaskGroup) => { throw (Exception "活儿砸了"); }
Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => {
        try { g.Await bad; } (e: Exception) => { print ("接住:" + e.Message); }
        print "接住之后这一支还在跑";
    })
    bad
]
```

执行以上程序会输出如下结果：

```
接住:活儿砸了
接住之后这一支还在跑
```

## 和 IO 接起来

**会等的操作各有一副"交回任务"的面孔** —— 命名统一是**同步名 + `Task` 后缀**，而且两条路**压的是同一份实现**，所以**报错文案一字不差**。

| 同步 | 交回任务 |
|------|----------|
| `Http.Get` / `Request` / `Download` … | `Http.GetTask` / `RequestTask` / `DownloadTask` … |
| `cmd` | `cmdTask` |
| `f.Read ()` / `Write` / `Append` | `f.ReadTask ()` / `WriteTask` / `AppendTask` |
| `System.ReadText` / `WriteText` / `ReadBytes` … | `System.ReadTextTask` / … |

#### 实例

```ravel
using "tasks.rav"
using "io.rav"
f := Io.File "tut_task.txt"
Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { g.Await (f.WriteTask "任务里写的"); })
    (Tasks.Task (g: Tasks.TaskGroup) => { g.Await (f.AppendTask "，又一句"); })
]
print (f.Read ())
f.Delete ()
```

执行以上程序会输出如下结果：

```
任务里写的，又一句
```

几段真实的 IO 一起发就真重叠（三个 `Http.GetTask` 打同一台服务器，服务端报的并发计数 > 1；三条 `cmdTask` 各跑一条约 1 秒的命令，总共也只要 1 秒出头）。

**真活儿见** `examples/tasks.rav`（八节：交替、收成、跑着再加、文件、错在哪现形、没人接的失败、互相等、一个任务只能跑一遍）和 `tests/297`–`299`。
