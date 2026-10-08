# Phase 11 证据复核报告

日期：2026-10-07（北京时间）。复核责任：TacticalCommandLab 开发组。
本轮游戏运行：**`runtime=NOT_RUN`**（本轮未启动游戏、未部署 DLL、未修改运行时代码）。
本报告引用的原始实测：`run04-census-final` 至 `run11-rel-after-release`，八场各自 `runtime=RUN`。两者严格分开。

复核对象：`docs/phase11-design.md`（**开工基线** SHA-256 `042ef0fbc5ed3eaa452cba67bd0b442ed442c33f00c531fe391519b6a3759a74`；**上一轮整改版本** `dd13fbaf97d1642ca14639a9f36320ef7ef096793b7c85a8de465f0aecae54f5`；**当前设计版本** `2c049e6d737f65caf813f655654ca3b12e4ff66ee76302e7fe152d2c8af78358`）§2 所依据的八场输入。

**本报告认证归档一致性，不认证全部实验结论。**

---

## 1. 八场身份与实际记录

### 1.1 开工身份

| 项 | 实际值 |
|---|---|
| 分支 | `codex/local-phase10-verification-20261006` |
| HEAD | `f8b1f173f05bf054b42eb2e31527deb1a83aa613`（"Close Phase 10 evidence with original-metric attribution and conditional conclusions"） |
| 工作树 | `git status --porcelain` 仅未跟踪项，**无 tracked 修改**（`git diff` 为空） |
| 实测源码身份 | 八场一律 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0` + 逐场 `git-diff.txt` + 实际加载 DLL |

> **设计 §2.1 记录 HEAD=`e57e157e839e334f532d7f875224491af6bd140d`，实际已前进到 `f8b1f17`。** 按派单 §2 要求以实际记录为准。八场实测身份**仍为 `adab4cf`**，不得倒写成测量时运行 `f8b1f17`。

### 1.2 设计文件与派单身份

| 文件 | SHA-256 | 长度 |
|---|---|---|
| `docs/phase11-design.md` | `042ef0fbc5ed3eaa452cba67bd0b442ed442c33f00c531fe391519b6a3759a74` | 144 行 |
| `_dispatch/2026-10-07-phase11-development-prompt.md` | `aadc20923744f7dd0ce603c107391cedcdc18919429b7be2029f871589707cfd` | 126 行 |
| `docs/phase11-handoff-template.md` | `ba79c8d3574f7c39f16f99f9e853f7d6d66633fc84ccb8b5673f98a9676231bf` | 82 行 |

### 1.3 八场 assessment 身份（逐场实测）

| run | script | runtime | classification | expected_counterexample | game_version | game_pid | dll_hashes_match | instrument_gates_pass |
|---|---|---|---|---|---|---|---|---|
| run04-census-final | census-baseline | RUN | PASS | false | v1.4.8.119303 | 19180 | true | true |
| run05-h2-suppression | h2-suppression | RUN | **FAIL** | false | v1.4.8.119303 | **36348** | true | true |
| run06-h2-coverage | h2-coverage | RUN | **FAIL** | false | v1.4.8.119303 | **39760** | true | true |
| run07-h3-route-b | h3-route-b | RUN | PASS | false | v1.4.8.119303 | 38156 | true | true |
| run08-rel-none | rel-none | RUN | PASS | false | v1.4.8.119303 | **40564** | true | true |
| run09-rel-reapply | rel-reapply | RUN | PASS | false | v1.4.8.119303 | **38548** | true | true |
| run10-rel-reapply-resetweights | rel-reapply-resetweights | RUN | PASS | false | v1.4.8.119303 | **18632** | true | true |
| run11-rel-after-release | rel-after-release | RUN | **FAIL** | **true** | v1.4.8.119303 | 19252 | true | true |

> **`game_pid` 更正（审查指出）**：早期版本把 run05/06/08/09/10 一并记为 `19180`。上表为逐场 `assessment.json` 实测值，**八场 PID 各不相同**（除 run04 外均非 19180）。

`active_modules`（八场一致）：`Bannerlord.Harmony, Bannerlord.ButterLib, Bannerlord.UIExtenderEx, Bannerlord.MBOptionScreen, Native, SandBoxCore, Sandbox, StoryMode, CustomBattle, CombatBrainAutonomousTestHarness, CommandLabProbe` —— **含 `CommandLabProbe`，不含 `CombatBrain`**，隔离成立。

---

## 2. 输入核对：manifest、assessment 哈希

### 2.1 `artifact-manifest.json` 路径 / 长度 / SHA-256

核对脚本：`_audit/verify_phase11_evidence.ps1`。核对脚本的结构提示：**manifest 是 bare JSON 数组**（非对象包裹），字段名为 `relative_path` / `sha256` / `length` / `last_write_utc`（不是 `path`）。

样例条目：

```json
{"relative_path":"assessment.json","sha256":"0fcaed84f2a7bdbe012ec9de9f673bd860d2dbcb6847c2192655294b2b483ab2","length":2005,"last_write_utc":"2026-10-06T14:09:44.3704853Z"}
```

| run | entries | ok | missing | mismatch |
|---|---|---|---|---|
| run04-census-final | 61 | 61 | 0 | 0 |
| run05-h2-suppression | 61 | 61 | 0 | 0 |
| run06-h2-coverage | 61 | 61 | 0 | 0 |
| run07-h3-route-b | 61 | 61 | 0 | 0 |
| run08-rel-none | 62 | 62 | 0 | 0 |
| run09-rel-reapply | 62 | 62 | 0 | 0 |
| run10-rel-reapply-resetweights | 61 | 61 | 0 | 0 |
| run11-rel-after-release | 62 | 62 | 0 | 0 |
| **合计** | **491** | **491** | **0** | **0** |

491 = **61×5 + 62×3**（61 条目：run04/05/06/07/10；62 条目：run08/09/11），**与设计 §2.1 声明的“共 491 个文件”一致**。结果：**无缺失、无不匹配**。

> **更正（Codex 审查 §3 第 4 项）**：本行原文写作 `61×6 + 62×2`，与上表逐行条目数不符（上表合计仍为 491）。已按实测条目数更正为 `61×5 + 62×3`；上表逐行数值未变，核对结论未变。

### 2.2 assessment 指定的 metrics / telemetry 哈希

| run | metrics 文件 | metrics_sha256 | telemetry 文件 | telemetry_sha256 | 结果 |
|---|---|---|---|---|---|
| run04 | `metrics-20261006-215243-census-baseline.txt` | `051522bebe6593c36d8257e835fb0817e7f18b972618373e1296aa452368f52a` | `telemetry-20261006-215243-census-baseline.txt` | `df47950fa5a3a282c8e6e209bbd466e91e0adfc6dde5c9467508802185466e46` | OK |
| run05 | `metrics-20261006-215546-h2-suppression.txt` | `b37a56b95881507397e5665c637338998dff2de38983be62ecae422bb62ab962` | `telemetry-20261006-215546-h2-suppression.txt` | `9242c6576846709dcc302c26c1f2d1155f4aa35b33ecbc645a947c3d473bc35e` | OK |
| run06 | `metrics-20261006-215736-h2-coverage.txt` | `f7ab7084150b4376b412c1d6d58234d1be59a304623129967b72180fce5d6082` | `telemetry-20261006-215736-h2-coverage.txt` | `c2f41b3f6c8814f04cd43d08c8c6a1e86a72273ffd417eb86fca79340e1d6f3b` | OK |
| run07 | `metrics-20261006-215927-h3-route-b.txt` | `f47bda68f0f0941d67863367b1328527af1e2b697147d3b2d550f848033a492f` | `telemetry-20261006-215927-h3-route-b.txt` | `57205e643e42418a67ffaf1039a1acd5d05141df7045af104b4c88ca4d0d252b` | OK |
| run08 | `metrics-20261006-220128-rel-none.txt` | `d6a41036033fb3eae322017843dc3531669ed143bd37dc13753fe40b1e0afa42` | `telemetry-20261006-220128-rel-none.txt` | `81236f848b42e12337f734f46924cd35a86f2cf0f4a6c6b674e3c852e5cdc66c` | OK |
| run09 | `metrics-20261006-220327-rel-reapply.txt` | `311a19ee1167050eaa7e51ba975794be6c4ba87780258576d3af20a587436fb0` | `telemetry-20261006-220327-rel-reapply.txt` | `ffb7e5b12b6590edd64b21dc8d4042df0f165557b00cc8bb3a7fea5c7f9f5ef8` | OK |
| run10 | `metrics-20261006-220518-rel-reapply-resetweights.txt` | `715958718d8bf6b4606f5cb2e48591bf1cda2c50f8fb7fe9f1712cd4406e901b` | `telemetry-20261006-220518-rel-reapply-resetweights.txt` | `4e92417cedc223b2caefdc03739dc3648184d2b0394a7da25b23c619eea1fc55` | OK |
| run11 | `metrics-20261006-220719-rel-after-release.txt` | `3364ae7c9e4c5a8c4dd6d8f67073e6e9ba44b76fdecf5d6aca4eba488bb4c6fd` | `telemetry-20261006-220719-rel-after-release.txt` | `0c121a76be088647650d424cf3649c8779da5aa8e8f8f02563f7d698975041da` | OK |

**八场全部匹配，无缺失、无不匹配。**

### 2.3 `script_sha256` 的指向（一处需要澄清的口径）

按“`script_sha256` 应对应 `script-source.txt`”比对时，八场全部报不一致。**逐场实测后确认这不是缺陷，而是字段指向不同**：

assessment 的 `script_sha256` 指的是**播种后**的 `script-seeded.txt`，不是播种前的 `script-source.txt` 模板（模板里的实体名与路径由 ScriptRunner 在播种时替换）。

例 run05：

| 文件 | length | SHA-256 |
|---|---|---|
| `script-seeded.txt` | 593 | `af3655b3b78f68acb9f27297c43999b621f14242ce61cf78e8ea8dad4ce2079e` == `assessment.script_sha256` |
| `script-source.txt` | 585 | `5661d549dae37c3c32411b3ca9841c8cd112c0b6e8436c88c8a505ee3ac590e7` |

run07 `17a351b2fe83e086e261aba15717a5e1034d585218a695a2df290e2713c61fec`、run11 `38d29cac4d6d43805df6ec16cd8dec5763fdf03318d69810db94a7f280a457c3` 同样与各自 `script-seeded.txt` 相符。

**两份脚本都已归档，因此两者都可核对。** 该口径澄清**不改变任何实验判定**。

---

## 3. 只读重算复核

### 3.1 `tools/recompute_telemetry.py`（八场）

命令：`C:\Python314\python.exe tools\recompute_telemetry.py <telemetry 绝对路径>`，逐场执行，捕获真实退出码。
输出：`_audit/2026-10-07-phase11-evidence/recompute/<run>.recompute.json`（**本轮独立目录，不覆盖任何他方审计目录**）。

| run | exit | `input.sha256` 与 assessment 一致 | `assessment_status` | first_latency_s | accepted_lab | rejected | canonical_attempts |
|---|---|---|---|---|---|---|---|
| run04 | 0 | 是 | `NO_RELEVANT_EVIDENCE` | null | 0 | 0 | 1548 |
| run05 | 0 | 是 | `EVIDENCE_FOUND` | 24.5216 | 1 | 50 | 1649 |
| run06 | 0 | 是 | `EVIDENCE_FOUND` | 24.5228 | 1 | 50 | 1642 |
| run07 | 0 | 是 | `EVIDENCE_FOUND` | 24.5045 | 1 | 3 | 1556 |
| run08 | 0 | 是 | `EVIDENCE_FOUND` | 24.5183 | 1 | 50 | 1884 |
| run09 | 0 | 是 | **`EVIDENCE_INSUFFICIENT`** | null | 2 | 50 | 1864 |
| run10 | 0 | 是 | **`EVIDENCE_INSUFFICIENT`** | null | 2 | 50 | 1849 |
| run11 | 0 | 是 | `EVIDENCE_FOUND` | 24.5237 | 1 | 51 | 1868 |

> **退出码口径（必须写明）**：该工具末尾为 `return 1 if result["assessment_status"] == "FORMAT_ERROR" else 0`。**`EVIDENCE_INSUFFICIENT` 仍返回 `exit=0`**。因此“八场 exit=0”**不表示八场结论一致**；run04/09/10 的 `INSUFFICIENT` 必须从 JSON 的 `assessment_status` 字段读出。

逐场诊断原文：

- run04：`no accepted effective Lab write; first-override latency cannot be established` —— census 场本就无 Lab 写入，**符合预期**。
- run09 / run10：`accepted Lab writes have no later same-entity/domain Native-tagged effective write` —— 这是**证据充分性状态（`EVIDENCE_INSUFFICIENT`）**：重发写入被接受，但之后没有可归因于 Native 的后续**有效**写入可供配对。**它不是 REL 判据的失败**：run09/run10 的原 REL 判据 `release_recovery_failures == 0` 均为 **PASS**；四场 REL 中**只有 run11** 是预期失败的反例（`REL=1`、`expected_counterexample=true`）。此处**不得**把它归类为"REL 实验预期失败侧证据"。
- 其余五场：无诊断条目。

**八场 `first_override` 明细（更正）**：**run04 / run09 / run10 为 `pairs = 0`**（故 `first_latency_s = null`）；run05/06/07/08/11 各 **1 对**，且该对**全部是 `entity=T1F0, domain=Movement`**（`lab_t` 分别 3.5045 / 3.5020 / 3.5004 / 3.5051 / 3.5014）。这与**其余七场**脚本一律 `acquire N0 Movement Full 30` + `cmd N0 Movement HOLD`（`N0` = `T1F0`）一致；**run04 是例外，其 census 脚本只有 `policy` + `dump`，不含 `acquire`/`cmd`**，故无 `first_override` 配对。`docs/10` §14 已登记 Multi-Team 时应用全名 `T{t}F{i}`、不使用 `N0`。

**资格输出的边界**：`assessment_status` 与解析成功**不能替代装置门禁**（P0/P0b/P0c），**也不能认证完整覆盖率**。

### 3.2 `tools/phase10_evidence`（G3 失败来源解释）

命令（**显式传第三个参数，避免默认目录**）：

```powershell
dotnet run --project tools/phase10_evidence/Phase10Evidence.csproj -- `
  "<run05 telemetry 绝对路径>" "<run06 telemetry 绝对路径>" `
  "_audit/2026-10-07-phase11-evidence/g3-rebuild"
```

`exit=0`，输出 11 文件 + `console.txt`（UTF-16LE，24744 B）。

**独立重建复现了 G3 FAIL**：

| 场 | `OrderChangesPerSecond` | `oscillation_pairs` | `oscillation_frequency` | `ReportedGrade` |
|---|---|---|---|---|
| run05（`console.txt` 第 17/26/191 行） | 3.696969696969697 | 37 | 1.121212121212121 | **Oscillating** |
| run06（`console.txt` 第 208/217/382 行） | 3.6363636363636362 | 35 | 1.0606060606060606 | **Oscillating** |

阈值（同场内自述）：`stableOscillationFrequencyMax=0.05`、`stableOrderChangesPerSecondMax=0.2`、`oscillatingFrequencyMin=0.5`、`oscillatingLifetimeLessThanSeconds=1`。

**既有 `tools/phase10_evidence` 输出必须写到新目录**：该工具第 3 个参数才是输出目录，默认写 `_audit/_phase10-closure-20261007`。本轮已显式传参到 `_audit/2026-10-07-phase11-evidence/g3-rebuild/`。

> **一处已发生并已补救的操作偏差（如实登记，损失评估已按审查意见限定）**：本轮首次调用该工具时未传第三个参数，默认输出目录 `_audit/_phase10-closure-20261007` 下的生成物被重写（`LastWriteTime` 变为 2026-10-07 10:22:54）。**这违反了派单"既有工具输出必须写到本轮独立目录、不得覆盖他方审计目录"的约定，本报告不为该次覆盖开脱。**
>
> **工具实际写出范围（来自工具自身写文件代码，非推断）**：`tools/phase10_evidence/Program.cs` 第 17 行写 `results.json`，第 143–151 行按每个 run 各写四份 CSV 与一份 diagnostics JSON，**合计恰好 11 个文件**：
>
> ```text
> results.json
> run05-diagnostics.json / run06-diagnostics.json
> run05-keys.csv / run06-keys.csv
> run05-changes.csv / run06-changes.csv
> run05-triples.csv / run06-triples.csv
> run05-lifetimes.csv / run06-lifetimes.csv
> ```
>
> **`h3-same-run.json` 不在其中**——该工具的代码不生成此文件。**`final-freeze-manifest.json`、`final-vs-adab4cf.patch` 等非本工具产物亦未受影响**（时间戳仍为 1:46:06）。
>
> **分三层限定**：
> 1. **已逐字节比对认证的部分**：重新生成的 **8 份 CSV**（见下表）与 tracked 冻结副本 `docs/evidence/phase10-closure-20261007/` **全部 IDENTICAL**。**范围仅限这 8 份 CSV**；`results.json` 与 `run05|run06-diagnostics.json` 未被逐字节认证（只作可重生成项，见第 2 层）。`h3-same-run.json` 与冻结副本相同这一点**可以独立记录**，但它**既非该工具输出、也不能用于认证被该工具覆盖的范围**。
> 2. **可重生成的部分**：`results.json` 与 `run05|run06-diagnostics.json` 可由同一输入（run05/run06 telemetry）与同一工具版本确定性重算，本轮已在 `g3-rebuild/` 独立产出完整副本（11 文件 + `console.txt`）备查。
> 3. **不能认证的部分**：**覆盖前未记录各文件的 SHA-256/副本**，因此**无法对被覆盖的 11 个文件作逐字节的前后一致性认证**；时间戳等元信息确定已变更且不可恢复。**不再泛称"所有 run05/run06 系列文件均被覆盖"。**
>
> **"未被 Git 跟踪"不等于"覆盖无害"**——未跟踪只说明没有版本库层面的可比对基线，不能替代逐字节认证。本轮一致性结果**只支持"本次输入与本次工具版本下输出确定"**，**不能泛化为任意输入下的工具认证**。
>
> 已纠正为显式传参，后续所有工具输出均落本轮独立目录 `_audit/2026-10-07-phase11-evidence/`。

重建产物与冻结副本的逐字节比对（全部 IDENTICAL）：

| 文件 | SHA-256 |
|---|---|
| `run05-changes.csv` | `d5ae2ae90b81ca3484b4e07b07e4d520baafa42d05fda23c6911dd5bf6b61028` |
| `run05-keys.csv` | `b4f047c9baccec2593a3bddbae1a14adf33254dc94347969ff8c08e1140f5ade` |
| `run05-lifetimes.csv` | `9129159ef07736445838080633eef31945b3a35aa3a433d9a21366c97a73ce58` |
| `run05-triples.csv` | `adb20118edb3c30b64169bfd203701c7dc54fd6eb0beb6cda34a7d1cbc36bd24` |
| `run06-changes.csv` | `4ce9e13fa63c17951f5eb2810a8270f3cd8d472de0a8631e2fedf988c9321666` |
| `run06-keys.csv` | `c5af98ab3aa3e3299e0b8eb195846631b61d86ea577914730fa12d0fac98f9e2` |
| `run06-lifetimes.csv` | `c79b56dc5d733741b3eee32f88eaa6ec0bf7248bb958463a26b48579d77c5715` |
| `run06-triples.csv` | `9ca493b534b83b86ad0970861af5f4f054e8f9ba88acb0dd0696d9fbc6f3fabe` |

本轮 `results.json` = `a29bd216c78df6b8a74fedb481f414a9fe17f9e7b0df37f539c92502adda87e6`（len 1085294）；冻结 `docs/evidence/phase10-closure-20261007/g3-summary.json` = `27fda22bea7bd685a3017a199671362bf9a80f55976b36b900808b174df79b78`（len 30937），两者结构同为 2 元素数组。

**该工具只用于解释原 G3 失败来源并保留原判定。工具的存在和输出本身不替代复核，也不新建另一套诊断框架。**

---

## 4. 失败保留：原判据与结果

**本节保留全部 FAIL 与预期失败，不调阈值制造 PASS。**

### 4.1 H2 与 H2b：G3 FAIL

| 项 | run05 H2 | run06 H2b |
|---|---|---|
| `[config]` | `level=Full ttl=30s autoRenew=True enforceGated=True enforceNonGated=False patch(mv/fa/ar)=True/True/True arrangementPath=NonGated controlledByAiRoute=False restore=None restoreBeforeRelease=True tEval=0.5` | 同上，但 `enforceNonGated=True`、`arrangementPath=Gated` |
| `P0` | 7 PASS | 7 PASS |
| `P0b` | 2086 PASS | 2077 PASS |
| `P0c` | 29/29 PASS | 29/29 PASS |
| `G1` `unauthorized_writes == 0` | 0 **PASS** | 0 **PASS** |
| `G2` `intent_hold_share >= 95%` | 100.0% **PASS** | 100.0% **PASS** |
| `G3` `stability grade != Oscillating` | **Oscillating FAIL** | **Oscillating FAIL** |
| `X1`..`X5` | 0 / 1.1211 / 3.6965 / 0.4999 / 0 全 PASS | 0 / 1.0605 / 3.6359 / 0.4963 / 0 全 PASS |
| `[hold]` | `T1F0\|Movement CLOSED intent=HOLD hold=100.0% window=24.50s samples=3503 matches=3503` | 同，`samples=3669 matches=3669` |

**G3 FAIL 是全场级判定。** 判据来自 `src/CommandLab.BannerlordProbe/ProbeRuntime.cs:944`（`m.Grade != StabilityGrade.Oscillating`），`Grade` 是全场 MetricsCalculator 输出。§3.2 的独立重建已复现该 FAIL。

### 4.2 H3：原判据 PASS + 独立全场 grade

run07 的 `[verdict]` 段**没有 G3 行**（`ProbeRuntime.cs:947-958` 的 `h3` 分支只输出 `H3-a` / `H3-b` / `G2`）。其全场 grade 需从 `[metrics]` 段单读：

| 项 | 值 | 结果 |
|---|---|---|
| `H3-a` `SetControlledByAI 被观测到` | 60 | **PASS** |
| `H3-b` `unauthorized_writes == 0` | 0 | **PASS** |
| `G2` `intent_hold_share >= 95%` | 100.0% | **PASS** |
| **独立全场 grade** | `stability_grade = Oscillating` | （无 G3 判据；作为**独立事实**记录，未纳入 PASS） |
| `[hold]` | `T1F0\|Movement CLOSED intent=HOLD hold=100.0% window=24.50s samples=3325 matches=3325` | — |

run07 `scope_note` 原文：`"H3 verdicts pass; global metrics grade remains separately recorded, and AI tick count is not directly instrumented."`

**⇒ 写法要求**：H3 记“**原判据 PASS**”，**同时另记**全场 grade 为 `Oscillating` 这一独立事实。**不得写成“H3 全场通过”，也不得用 H3-a/H3-b 的三项 PASS 掩盖全场仍 Oscillating。**

### 4.3 REL：0 / 0 / 0 / 1

| run | `[config]` 的 restore | `REL` 判据（`release_recovery_failures == 0`） | 结果 |
|---|---|---|---|
| run08 `rel-none` | `restore=None` | 0 | **PASS** |
| run09 `rel-reapply` | `restore=ReapplyLastNativeOrder` | 0 | **PASS** |
| run10 `rel-reapply-resetweights` | `restore=ReapplyLastNativeOrder, ResetBehaviorWeights` | 0 | **PASS** |
| run11 `rel-after-release` | `restore=ReapplyLastNativeOrder`，`restoreBeforeRelease=false` | **1** | **FAIL** |

**⇒ 必须写成：REL = 0 / 0 / 0 / 1，最后一项同时记“实验预期失败、反例辨识成功”。不得写 REL 四组全部 PASS。**

run11 `assessment.json` 原文：`"classification": "FAIL"`、`"expected_counterexample": true`、`"scope_note": "FAIL is expected; discrimination succeeds when the restore attempt is NotOwner rejected."`；`rel-after-release.txt` 脚本注释：“预期 `release_recovery_failures > 0`，即 REL 判据 FAIL —— 这是设计出来的反例”。

run11 的逐行证据（“反例辨识成功”的机器级依据）：

```
t=28.0049|tick=4012|E=T1F0|D=Movement|P=Gated|W=Lab|O=Native|L=5|lease=-|tok=0|max=1|gen=2|rgen=2|exp=-Infinity|cmd=Obs0|prev=HOLD|new=HOLD|ome=0|chg=0|rej=1|why=NotOwner
t=28.0251|tick=4015|E=T1F0|D=Movement|P=Gated|W=Native|O=Native|L=5|lease=-|tok=0|max=1|gen=0|rgen=2|exp=-Infinity|cmd=Obs0|prev=HOLD|new=Obs0|ome=1|chg=1|rej=0|why=-
AUTH|t=28.0049|Release|E=T1F0|D=Movement|own=Native|prev=Lab|L=5|lease=|tok=0|gen=2|why=released to Native
```

两条限定：

1. **原始 Move 重发按完整缓存执行**，不是从 Obs 投影重建。
2. **AfterRelease 被拒之后的 Native 自行恢复，不能算那次恢复写入成功。** 28.0049 的 Lab 尝试以 `rej=1 why=NotOwner` 结束；28.0251 的 `chg=1`（`HOLD → Obs0`）由 `W=Native` 写下，与 28.0049 被拒的 Lab 尝试是**两件事**。

---

## 5. 设计 §5.2 证据列：关键结论

“类型”取 `仿真 / 静态 / 宿主实测 / 设计选择 / 未验证` 之一。全部哈希见 §2。

### 5.1 目标 Movement 接管

| 列 | 内容 |
|---|---|
| 结论原文 | 在 T1F0、Movement 域、HOLD 意图下，Lab 取得并保持了目标控制 |
| 证据类型 | **宿主实测** |
| 实验 / run | run05 H2（`h2-suppression`） |
| 输入路径与 SHA-256 | `_audit/_local-phase10-20261006/run05-h2-suppression/telemetry-20261006-215546-h2-suppression.txt` = `9242c6576846709dcc302c26c1f2d1155f4aa35b33ecbc645a947c3d473bc35e`；`metrics-20261006-215546-h2-suppression.txt` = `b37a56b95881507397e5665c637338998dff2de38983be62ecae422bb62ab962` |
| 实体 / 域 / 时间窗口 | `T1F0` / `Movement` / HOLD 窗口 **24.50 s**（`samples=3503 matches=3503`） |
| 实际配置 | `level=Full ttl=30s autoRenew=True enforceGated=True enforceNonGated=False arrange-mentPath=NonGated controlledByAiRoute=False restore=None restoreBeforeRelease=True tEval=0.5` |
| 原判据及结果 | `G1 unauthorized_writes==0` → 0 **PASS**；`G2 intent_hold_share>=95%` → 100.0% **PASS**；`X5 unobserved_writes==0` → 0 **PASS** |
| 支持范围 | 该实体、该场景、该窗口 |
| 不能推导 | 全场稳定；所有 Movement 路径完全覆盖；多场景泛化；Facing/Arrangement 接管；默认产品能力 |

run06 是同构的第二次运行（`samples=3669 matches=3669`，G1/G2 同样 PASS），可作重复性参考，但**不扩大支持范围**。

### 5.2 G3 范围

| 列 | 内容 |
|---|---|
| 结论原文 | G3 是**全场**级不稳定判定，HOLD 窗口的成功不覆盖它；H2/H2b 均 FAIL，H3 无 G3 判据但全场仍 Oscillating |
| 证据类型 | 宿主实测 + 离线重建复现 |
| 实验 / run | run05、run06、run07 |
| 输入路径与 SHA-256 | 见 §2.2；重建输入即 run05/run06 telemetry |
| 实体 / 域 / 时间窗口 | **全场** `simulation_seconds = 33.00`，`TEval = 0.5`（非目标局部） |
| 实际配置 | 同上三场 |
| 原判据及结果 | `G3 stability grade != Oscillating`：run05 `Oscillating` **FAIL**；run06 `Oscillating` **FAIL**；run07 无 G3 判据，`stability_grade=Oscillating` 另记 |
| 支持范围 | 全场级不稳定客观存在，且**未被目标 HOLD 窗口覆盖** |
| 不能推导 | 单点控制失败；目标 hold 可冲抵 G3；G3 阈值应下调 |

**分层读法**：`hold` 回答“该实体该域是否服从目标意图”，G3 回答“全场是否稳定”。当前证据是**单点控制成功 + 全场不稳定**，二者不矛盾。

### 5.3 完整覆盖率不足

| 列 | 内容 |
|---|---|
| 结论原文 | 没有独立全量写入分母，**不能计算或宣称完整覆盖率 100%**；只能给出缺口下界 |
| 证据类型 | **未验证**（设计边界）+ 宿主采样的下限口径 |
| 实验 / run | 八场全部（recompute）+ run05/run06 的 `[coverage]` 段 |
| 输入路径与 SHA-256 | `_audit/2026-10-07-phase11-evidence/recompute/*.recompute.json`；telemetry 哈希见 §2.2 |
| 实体 / 域 / 时间窗口 | 八场全部 `x5_coverage_gap_lower_bound.count = 0` |
| 实际配置 | 八场各自 |
| 原判据及结果 | `X5 unobserved_writes == 0` 八场全 PASS |
| 支持范围 | 该采样口径下**未观测到正缺口** |
| 不能推导 | **完整写入覆盖率 = 100%**；Arrangement Full 已验证；所有非 gated 路径已覆盖 |

机器级依据（八场 recompute JSON 均含）：

```json
"x5_coverage_gap_lower_bound": {
  "count": 0,
  "interpretation": "zero means no observed positive gap; it does not prove complete coverage"
}
```

两项结构性限定：

1. `X5` 上游是**影子采样**，时间分辨率 = Mission tick 频率 ⇒ **同一帧内两次变化只算一次**，本身即**下限估计**（`docs/10` §420 已登记）。
2. `X5` 判据（`ProbeRuntime.cs:975-985` `TotalUnobserved()`）只在 `sampled > port` 时累加差值 —— 它测**缺口**，不是**分母**。

**run06 的 Arrangement 三行是观测而非接管验收**：`T1F0|Arrangement sampled_changes=2 port_changes=3 unobserved=0`，而 run06 脚本只 `acquire N0 Movement`、`cmd N0 Movement HOLD`，对 Arrangement 域**零命令**。

> **`docs/10` §13 原登记的 Arrangement 预测在 run06 未获得支持（本轮登记）**：`docs/10` 第 176–177 行把“切成 Gated 后 Arrangement 域 `unobserved > 0`”列为可判伪的 LEVEL C 预测。run06 实测 `unobserved == 0`，即**没有支持该预测的正缺口**。第 177 行“两种结果都记，都不当作实现有误”的处理本身是正确的。**本报告据此降低该结论强度，不改变 run06 的任何原始判定。** 已同步登记到 `docs/07-final-architecture.md` §3.2 与 §10.1 第 9 项。
>
> **不得以“该场没有 Lab Arrangement 命令”否定该预测的可检验性。** 覆盖率缺口的测量口径是 `ProbeRuntime.SampleShadow`（`ProbeRuntime.cs:704-730`）：它遍历 `_bindings` 的全部实体、对 `d = 0..2` 三个域逐一比较宿主真值并累计变化，**不要求 Lab 在该域持有 Lease 或提交过命令**；`PortEffectiveChanges`（`ProbeRuntime.cs:733-741`）只统计该实体该域的生效写入，同样不区分写入者是否带意图。故**纯 Native 的 Arrangement 状态变化也可以产生采样/入口计数缺口**，零干预的 census 场即是这一类观测的例子。
>
> **正确区分**：没有 Arrangement 独占意图，确实**无法验收 Arrangement Full**；但**不能由此推出覆盖率缺口预测不可检验**。本场观测为 `sampled=2 / port=3 / unobserved=0`，因而没有支持原预测的正缺口。至于是自维护路径未充分触发、是否改变了所采 `OrderEnum`、还是采样漏检——**现有信息不足以强行选定其中一个原因**，登记为证据限制。

### 5.4 H3 性质

| 列 | 内容 |
|---|---|
| 结论原文 | H3 是**关闭编队 AI 控制**的实验，不是 C 的 Behavior/Weight 集成验收；普通非 PlayerSergeant 情况下影响**整个 `FormationAI.Tick`** |
| 证据类型 | 宿主实测（run07）+ **静态**（IL 门控） |
| 实验 / run | run07（`h3-route-b`）；静态依据 `current-formation-tick-il.txt` |
| 输入路径与 SHA-256 | `run07-h3-route-b/telemetry-20261006-215927-h3-route-b.txt` = `57205e643e42418a67ffaf1039a1acd5d05141df7045af104b4c88ca4d0d252b`；`metrics-20261006-215927-h3-route-b.txt` = `f47bda68f0f0941d67863367b1328527af1e2b697147d3b2d550f848033a492f` |
| 实体 / 域 / 时间窗口 | `T1F0` / `Movement` / HOLD 窗口 24.50 s（`samples=3325 matches=3325`） |
| 实际配置 | `level=Full ttl=30s autoRenew=True enforceGated=True enforceNonGated=False arrangementPath=NonGated **controlledByAiRoute=True** restore=RestoreControlledByAi restoreBeforeRelease=True tEval=0.5` |
| 原判据及结果 | `H3-a` → 60 **PASS**；`H3-b unauthorized_writes==0` → 0 **PASS**；`G2` → 100.0% **PASS**；**独立全场 grade = Oscillating** |
| 支持范围 | 当前实验配置下的关联，以及关闭 AI 控制的抑制效果观察 |
| 不能推导 | **C 的 Behavior/Weight 集成已通过**；实际 `AI.Tick` / 行为选择调用计数为 0；H3 的关闭方式可作常态默认 |

三项必须保留的限定：

1. **`controlledByAiRoute` 是关闭编队 AI 控制的实验开关，不能与 A/B/C 比较中的 C 集成路线混用。** 脚本名 `h3-route-b` 沿用历史名称。
2. **`Formation::Tick` 门控**：`if (IsAIControlled) AI.Tick(); else if (Team.IsPlayerSergeant) AI.Tick(); else IsAITickedAfterSplit = true;` ⇒ 该开关作用于**整个 `FormationAI`**，可能同时影响多个域的行为选择。**PlayerSergeant 例外必须保留。**
3. **实际 `AI.Tick` / 行为选择调用计数未被装置直接插桩**（run07 `scope_note` 原文：“AI tick count is not directly instrumented”），故 run07 的 `H3-b=0` 只说明该场该域没有越权生效写入，**不证明调用次数为 0**。

### 5.5 REL 恢复顺序与自然恢复

| 列 | 内容 |
|---|---|
| 结论原文 | 原始 Move 重发**有效且可执行**；**先丢失所有权再以旧 Lab 身份恢复会被拒**；**自然恢复可出现**；但 Reapply/Reset 的**必要性未获支持** |
| 证据类型 | **宿主实测** |
| 实验 / run | run08 / run09 / run10 / run11 |
| 输入路径与 SHA-256 | run08 `81236f848b42e12337f734f46924cd35a86f2cf0f4a6c6b674e3c852e5cdc66c`；run09 `ffb7e5b12b6590edd64b21dc8d4042df0f165557b00cc8bb3a7fea5c7f9f5ef8`；run10 `4e92417cedc223b2caefdc03739dc3648184d2b0394a7da25b23c619eea1fc55`；run11 `0c121a76be088647650d424cf3649c8779da5aa8e8f8f02563f7d698975041da`（文件与 metrics 哈希见 §2.2） |
| 实体 / 域 / 时间窗口 | `T1F0` / `Movement` / 四场同构：Acquire t≈3.00 → cmd t=3.5 → Release t=28.00 → dump t=33/38 |
| 实际配置 | 四场差异仅在 `restore=` 与 `restoreBeforeRelease=`：None / Reapply / Reapply+ResetBehaviorWeights / Reapply+`restoreBeforeRelease=false` |
| 原判据及结果 | `REL release_recovery_failures == 0`：**0 / 0 / 0 / 1**，第四项为**设计出来的反例** |
| 支持范围 | 原始 Move 重发有效；先丢所有权后以旧身份恢复**被拒**；自然恢复可出现；四场 G1/G2 全 PASS（hold 均 100.0%，窗口 24.50 s） |
| 不能推导 | Reapply 或 Reset **普遍必要**；恢复全部内部状态（Behavior/Timer 基线）；到期/异常恢复已验收；REL=0 证明仿真定义要求的完整基线恢复 |

**“reapply 可执行”不证明“必需”的逐行依据**：

```
run09: t=3.5000  W=Lab O=Lab lease=L1 tok=1 gen=1 exp=11.5000 cmd=HOLD prev=Obs0 new=HOLD chg=1 rej=0
       t=28.0016 W=Lab O=Lab lease=L1 tok=1 gen=1 exp=36.0016 cmd=Obs0 prev=HOLD new=Obs0 chg=1 rej=0
       t=28.0281 W=Native O=Native gen=0 rgen=2 exp=-Infinity cmd=Obs0 prev=Obs0 new=Obs0 chg=0
run10: t=28.0019 W=Lab O=Lab ... cmd=Obs0 prev=HOLD new=Obs0 chg=1 rej=0
       t=28.0288 W=Native O=Native ...
run08: t=28.0234 W=Native O=Native rgen=2 cmd=Obs0 prev=HOLD new=Obs0 chg=1 rej=0   ← 无任何恢复动作，同样恢复
```

- reapply 写在 t=28.0016/28.0019 **被接受**（`chg=1 rej=0`，且仍持有效 Lease `exp=36.0016`，因 `restoreBeforeRelease=true`），但 **约 0.0265/0.0269 s 之后（下一 tick，t=28.0281/28.0288）Native 即以 `rgen=2` 回到 `Obs0`**，与 run08 无动作时的 t=28.0234 结果相同 ⇒ **该场本来就会自然恢复**。该后续 Native 行为 `chg=0`（同值投影）。
- run09 与 run10 的差别**未显示出恢复收益**：run09 `oscillation_frequency=0.9210 / order_changes_per_second=3.1314`，run10 `=0.9209 / =3.1312`，差异在 0.0001 量级。**既不能据此断言"必须 Reset 才能恢复"，也不能反向断言 Reset 有害**——本轮**未证明 Reset 的恢复收益或必要性**。

`recompute_telemetry.py` 对 run09/run10 的 `EVIDENCE_INSUFFICIENT` 与诊断 `accepted Lab writes have no later same-entity/domain Native-tagged effective write`，是这两场**证据充分性**的机器级表述：重发被接受，但之后没有可归因于 Native 的后续**有效**写入可供配对。**它不是 REL 判据失败**——run09/run10 原 REL 判据均为 PASS，四场中只有 run11 是预期失败的反例。该后续 Native 行读数为 `chg=0`（同值投影），只证明该次尝试被放行，**不能独立证明全部真实 Order 字段/位置/内部状态已恢复**；恢复的直接证据是 run08 的自然恢复与 `REL_ORDER` 的原始 Move 重发记录。

### 5.6 Proposal 裁决

| 列 | 内容 |
|---|---|
| 结论原文 | 当前最小框架**不新增** Proposal、独立 Executor 或新的 Command Bus |
| 证据类型 | **设计选择**（依据为既有仿真 + 本轮宿主实验） |
| 实验 / run | E1–E12（仿真）、run04–run11（宿主）；见 `docs/06`、`docs/07a`、`docs/phase11-design.md` §3.3 |
| 输入路径与 SHA-256 | `docs/06-experiment-results.md`、`docs/07a-architecture-abc-comparison.md`（本报告不重算其输入；其原始产物在 `tests/artifacts/`，本轮未复核） |
| 实体 / 域 / 时间窗口 | — |
| 实际配置 | — |
| 原判据及结果 | 无独立判据；结论为“没有逼出该抽象” |
| 支持范围 | **本轮范围内“不需要新增”** |
| 不能推导 | “永远不需要”；否定宿主执行职责本身；`Proposal` 在任何未来场景都不必要 |

设计 §3.3 明确：“这是本轮范围内‘不需要新增’的设计裁决，不是‘永远不需要’的定理。仅当后续真实需求证明现有对象无法表达、给出最小失败例和验收标准时再讨论。”宿主实际执行动作仍存在，由现有接入层承担；**不因没有名为 Executor 的类而否认执行职责**。

---

## 6. 事实边界与不可推导项汇总

| # | 事实 | 不可推导 |
|---|---|---|
| 1 | 八场 manifest 491 条路径/长度/SHA-256 全匹配 | 全部实验结论正确 |
| 2 | 八场 assessment 指定的 metrics/telemetry 哈希全匹配 | 装置门禁以外的一切；完整覆盖率 |
| 3 | T1F0 Movement HOLD 窗口 hold=100%，G1=0 | 全场稳定；三域接管；默认产品能力 |
| 4 | G3 全场 FAIL（H2/H2b），独立重建复现 | 单点控制失败；阈值应下调 |
| 5 | H3 原判据三项 PASS | C 的 Behavior/Weight 集成已通过；`AI.Tick` 调用计数为 0 |
| 6 | REL = 0/0/0/1 | 四组全 PASS；Reapply/Reset 普遍必要；完整基线恢复已验证 |
| 7 | X5 = 0 八场全 PASS | 完整写入覆盖率 = 100% |
| 8 | `recompute_telemetry.py` 八场 exit=0 | 八场结论一致（`INSUFFICIENT` 仍返回 0） |
| 9 | `lab_delivery_rate` 在 E12 L5 仅 4.8% | 控制失败（口径缺陷，不作判据） |
| 10 | `docs/10` §13 的 Arrangement Gated 预测在 run06 **未获得支持**（实测 `unobserved == 0`，无正缺口） | 应据此改 run06 判定；应删除该段；**该预测不可检验**（覆盖率口径不要求 Lab 在该域有意图，故可检验，只是本场未观测到缺口） |

**未复核项（如实列出）**：

- `docs/06` / `docs/07a` 的仿真产物（`tests/artifacts/e1-e11-results.txt`、`e12-level-spectrum.txt`）**本轮未重新执行**。本报告只引用其已登记的结论，未独立复算。
- 2026-10-06 的 102/102 C#、23/23 Python、D0 15/15、E1–E11 51/51、E12 10/10 均为**已有报告的结果**，本轮未运行。
- **证据可访问性**：`_audit/_local-phase10-20261006/` 在 `.gitignore` 覆盖下，**跨机器交付必须另带可访问副本；只有路径不构成交接**。本轮独立产物在 `_audit/2026-10-07-phase11-evidence/`（同样未跟踪）。两处均未提交。

---

## 7. 复现命令与产物

| 执行内容 | 环境 | 退出码 | 输出路径 | 检查边界 |
|---|---|---|---|---|
| `_audit/verify_phase11_evidence.ps1` | PowerShell / 2026-10-07 | 0 | `_audit/2026-10-07-phase11-evidence/manifest-verification.json`、`assessment-verification.json` | 只认证归档一致性，不认证实验结论 |
| `C:\Python314\python.exe tools\recompute_telemetry.py <telemetry>` ×8 | Python 3.14.7 / 2026-10-07 | 逐场 0 | `_audit/2026-10-07-phase11-evidence/recompute/*.recompute.json` | 不替代装置门禁；`INSUFFICIENT` 仍返回 0 |
| `dotnet run --project tools/phase10_evidence/Phase10Evidence.csproj -- <run05> <run06> <out>` | .NET SDK 10.0.400 / 2026-10-07 | 0 | `_audit/2026-10-07-phase11-evidence/g3-rebuild/` | 只解释原 G3 失败来源，保留原判定。**`dotnet run` 默认包含构建**，故本行表示未重跑**全量工程/宿主构建与测试套件** |
| 游戏运行 | — | — | — | **本轮 `runtime=NOT_RUN`** |
| 全量构建 / 离线测试套件 | — | — | — | **本轮未重跑**（本轮为文档与证据复核；`dotnet run` 只构建了 `Phase10Evidence` 单个工程） |

---

## 8. 结论

| 项 | 状态 |
|---|---|
| 八场输入身份（manifest + assessment 哈希） | ✅ **全部核对通过**（491/491；16/16 哈希匹配） |
| 只读重算复核 | ✅ 八场完成；`input.sha256` 逐场一致 |
| G3 独立重建 | ✅ 复现 run05/run06 双 `Oscillating`；8 份 CSV 与冻结副本逐字节相同 |
| 失败保留 | ✅ H2/H2b G3 FAIL、H3 原判据 PASS + 独立全场 grade Oscillating、REL=0/0/0/1 全部保留 |
| 设计 §5.2 六项证据列 | ✅ 覆盖（§5.1–§5.6） |
| 本轮游戏运行 | **`runtime=NOT_RUN`**；引用原始 run04–run11 各自 `runtime=RUN` |
| 窗口时长口径（更正） | H2/H2b 判读时刻的原时长打印为 **`simulation_seconds = 33.00`**（run04 census 为 30.00），**不是"全程 30 s"**；八场 `ttl`：run04 `8s`、其余 `30s` |
| 未复核 | `docs/06`/`07a` 的仿真产物；全量构建与离线测试套件 |

**本报告不自行宣称最终通过。** 已按 Codex 审查报告（`_audit/2026-10-07-phase11-review.md`）完成文档整改，**整改后状态为"待复审"**，最终验收结论由 Codex 给出。
