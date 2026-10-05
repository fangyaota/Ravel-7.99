# 要不要把 `Split` / `CheckHead` 删掉，让整行命令行都归 CLI

Status: needs-triage

## 现在是什么样

`Cli/Program.cs` 的骨架是 `Spectre.Console.Cli` 的 `CommandApp`（照老 Ravel 那份
`Program.cs`），但命令行**不是**整行交给框架的 —— `Program.Split` 先在**脚本名**处切一刀：

```
ravel --warn x.rav a --warn   →   前半 [--warn x.rav]   后半 [a --warn]
```

前半给框架认，后半**一个字不动**地进 `System.Args ()`。`test` / `strip` 是子命令、
没有"脚本"，不切。

另外 `Program.CheckHead` 把脚本名**之前**那几个开关自己先认一遍名字 —— 因为框架的解析
错误是英文的（`Error: Unknown option 'x'.`），而这儿报错一律中文。

## 为什么现在是这么写的

框架在**整行**任意位置认开关，不认识的还**静默吞掉**。不切的话：

| 输入 | 框架给的 | 脚本本该收到 |
|---|---|---|
| `ravel x.rav --warn` | `--warn` 被抢走 | `[--warn]` |
| `ravel x.rav a --warn b` | `args=[a b]`（抽走又拼回去） | `[a --warn b]` |
| `ravel x.rav --foo -x` | `args=[]`（**连报错都没有**） | `[--foo -x]` |

试过的死路：`CommandArgumentAttribute` 没有 `RemainingArguments`；
`ConvertFlagsToRemainingArguments` 只是把不认识的开关收进 `Remaining` —— 位置照样是乱的。

留住这一刀的四个理由（详见 2026-10-04 那次讨论）：

1. **CLI 不可能知道脚本的开关表** —— 规格是脚本运行时自己传的
   （`Args.ParseWith (System.Args ()) {"port": "int"}`），同一个 `--foo` 一会儿是选项
   一会儿是位置参数。静态看命令行判断不了，所以只有"全给"或"全不给"两种自洽做法。
2. **名字会撞** —— 脚本完全可以自己就认 `--warn` / `--fast` / `--help`。解释器先咬一口的话，
   那个名字就永远轮不到脚本。
3. **静默丢是最糟的错**（上表第三行）。
4. **现实里已经有人靠它** —— `examples/serve.rav` 拿 `System.Args ().At 0` 当端口、
   `examples/site.rav` 当输出目录；`lib/args.rav` 整库就是给脚本解析自己的开关用的。

## 待定的那个决定

用户 2026-10-04 说过「什么乱七八糟的规矩，用就是了」—— 也就是**不排除**接受"整行都是 CLI 的"。
真要删，代价是删掉 `Program.Split` + `Program.CheckHead` 约 30 行，
换来的行为是：

- 脚本只能用**不像开关**的参数（`--` 之后那一段或许还能救，待验）；
- 或者从此没有"脚本自己的开关"这回事，`lib/args.rav` 只剩位置参数那半边能用。

## 决定之前要定的

- `lib/args.rav` 要不要留？它是这条规矩唯一的现实用户。
- 若删，`System.Args ()` 的语义要重写进 `CONTEXT.md`「System 模块」和
  `docs/tutorial/09-modules.md`，`tests/lib/249_args_env.rav` / `276_args.rav` 也要跟着改。
- 加进 `.scratch` 是不是这个仓库要长期保留的东西？（`.scratch/` 目前是空目录、
  没进 `.gitignore`。）

## Comments

- 2026-10-04：这一刀是 `Cli/Program.cs` 换成 `CommandApp` 那次（提交 `a745baf`）加的。
  当时先问过要不要「抽成命令表 + 手写 --help」，用户回「什么乱七八糟的规矩，用就是了」——
  于是骨架整个用了框架，只留了保住 `System.Args ()` 的这两处手写。
