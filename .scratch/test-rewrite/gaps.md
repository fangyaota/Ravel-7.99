# lang 组重写：现状与取舍

日期：2026-11-13。上限：每组 ≤20 文件、单文件 ≤5120B。

## 现状

20 个文件、合计 91,876B、最大 5,063B（`10_collections.rav`），全部在 5120B 以内；
`dotnet out/ravel.dll test lang` → **20 passed, 0 failed**。

| 文件 | 字节 |
| --- | --- |
| `01_core.rav` | 4876 |
| `02_control_flow.rav` | 4897 |
| `03_functions.rav` | 4964 |
| `04_numbers.rav` | 4968 |
| `05_strings.rav` | 5034 |
| `06_types.rav` | 4341 |
| `07_internal.rav` | 4996 |
| `08_callcc.rav` | 4793 |
| `09_eval.rav` | 5057 |
| `10_collections.rav` | 5063 |
| `11_continuation.rav` | 4843 |
| `12_containers.rav` | 4867 |
| `13_destructure.rav` | 4895 |
| `14_patterns.rav` | 4983 |
| `15_pattern_kinds.rav` | 4694 |
| `16_guards.rav` | 4936 |
| `17_protocol.rav` | 4977 |
| `18_lambdas.rav` | 4553 |
| `19_flow_flags.rav` | 3687 |
| `226_abort_mod.rav` | 452 |

## 已装回的旧用例

`231_nesting_sugar` / `136_fn_meta` / `177_param_type_mismatch` / `189_alternate_chain` → `01_core.rav`；
`118_metaclass_print` → `06_types.rav`；`Scope` 当 `IDict`/`IEnumerable`（旧 `m4`）→ `06_types.rav`；
枚举器 `HasNext`/`MoveNext`（旧 `m5`）→ `02_control_flow.rav`；
`225_callcc_scopes`（2,109B，去注释后）→ `02_control_flow.rav`。

## 放弃装回（附理由）

| 旧用例 | 理由 |
| --- | --- |
| `223_callcc_try` | 去注释后 1,467B；`02` 只剩 223B，其余可动文件无 1.5KB 连续余量（`19_flow_flags.rav` 带 `#program` 不能动、`226_abort_mod.rav` 是模块不能塞） |
| `196_math` | .NET `Math.*` 直包；`tests/lib/255_range.rav`、`tests/lib/280_native.rav` 已碰到 `Math` |
| `198_nan_inf` | `03_functions.rav:207-212` 已在测 `Inf`/`NaN` 的比较 |
| `287_raw_string` | `05_strings.rav` 有 4 处 `"""`，原文件本身带 `#slow` |
| `239_interpolation` | 引擎没实现插值，旧用例只剩报错文案 |
| `227_callcc_generator` / `228_callcc_init` | callcc 与 Generator/init 的组合，边际价值低 |
| Bits 段（`lib/bits.rav`） | thin wrapper；`<<`/`>>` 已在 `04_numbers.rav` 测过 |

## 整文件删除理由

`163_fields`（只建类建实例、从未调 `Fields()`，期望 0 行）、`233_precedence`（与 310 重复）、
`288/289`（并进 `05_strings`）、`326/332/333/364`（模式族同形覆盖）、`328`（帧数）、
`335`（自动名随路径变）、`353`（与 317 重复 0.45）、`355`（被 354 覆盖）、`363`、`366`。
