# Tactical Command Lab — Phase 10 下一轮派单（可直接下发）

> 派单日期：2026-10-02
> 依据：`_audit/2026-10-01-phase10-acceptance.md`（复跑 + 实测产物取证）
> 本轮目标：**修掉 D1（EntityId 不唯一）与 D2（门禁漏判），让 Phase 10 拿到第一组可采信的实测数据**
> 性质：**修装置，不是做实验。本轮不产出任何 Phase 10 的架构结论。**

---

## 0. 启动提示词（复制到新会话）

```text
继续推进 Tactical Command Lab 项目，本轮任务是「修装置」，不是「做实验」。

先读这两个文件，它们是本轮的唯一依据：
  E:\MBmod\TacticalCommandLab\_dispatch\2026-10-02-phase10-next-prompt.md   ← 本派单（含逐步计划与验收条件）
  E:\MBmod\TacticalCommandLab\_audit\2026-10-01-phase10-acceptance.md      ← 上一轮验收报告（含缺陷证据链）

再读 E:\MBmod\TacticalCommandLab\HANDOFF.md 了解项目全貌。
注意：HANDOFF.md 第 2 节的进度表已过时（Phase 10 仍写"待重跑"，实际 2026-09-23 已跑通一轮），
以本派单和验收报告为准。

严格按本派单的 Step 0 → Step 6 顺序执行，每个 Step 做完必须满足其「完成判据」才能进入下一个。
不要跨 Step，不要顺手重构无关代码，不要在装置修好之前跑 H1/H2/H2b/H3/REL。

硬性约束（违反即返工）：
- 不引用 CombatBrain.*、不引用 TaleWorlds.*（除 BannerlordProbe 工程外）
- 不用骑兵/弓兵保护/士气/Brace/Skirmish/Protect 等真实战术做基础实验
- 不靠"每 Tick 重复 SetOrder"压过 Vanilla 来实现 Authority
- 不写没有 Telemetry 支撑的任何结论
- Core / Simulation / Telemetry / tests 四个工程零改动；本轮改动全部落在 BannerlordProbe + docs

完成后必须回写 HANDOFF.md 的「当前进度」与「下一步」，并在 .workbuddy/memory/ 追加工作日志。
```

---

## 1. 为什么必须有这一轮

2026-09-23 那组 `census-baseline` 虽然 P0/P0b 双 PASS、游戏不再崩溃（延迟 patch 修复确认生效），但 `X5 FAIL = 131470`，且经取证该数字 **99.98% 是假数据**：

```
131,579 条 SHADOW 行中，131,559 条是同一 tick 内同 (实体,域) 的重复计数，真实变化只有 20 条
N-1|Arrangement 仅在 Obs0↔Obs1 之间乒乓，7039 / 7038 次对称
同一 EntityId 下挂多个真实 Formation：P0–P9 各 1 个，N0–N9 各 5 个，N-1 有 6 个
E=N0 的 HOST 行里 team=nonplayer(308) 与 team=player(294) 并存
```

根因在 `ProbeRuntime.EntityIdOf()`（`src/CommandLab.BannerlordProbe/ProbeRuntime.cs:224-230`）：
`side` 只有 `P`/`N` 二值，而 `formation.Index` 在**每个 Team 内部各自从 0 计数**；自定义战斗里非玩家 Team 不止一个（含盟友队）⇒ 各队的 Index 0 全部塌缩成同一个 `"N0"`。

因为 `WritePort.Evaluate` 用 `_entities[req.EntityId]` 取唯一共享状态（`src/CommandLab.Core/WritePort.cs:209`），多个 Formation 共用一份 ⇒ 除 `[patches]` 与 `[census]` 之外的**全部指标失真**，包括未来 H2 的 G1/G2。

**本轮不修就跑 H1，后续 8 组数据全部作废。**

---

## 2. 改动范围与方案选型

先看清影响面，避免改错方向：

| 影响对象 | 数量 | 说明 |
|---|---|---|
| `EntityIdOf()` | **1 处**（`ProbeRuntime.cs:224-230`） | 唯一出口，改动面小 ✅ |
| 实验脚本里的 `N0` | **23 处**（acquire 7 / cmd 9 / release 7，跨 8 个脚本） | 若改命名格式则全部要改 ❌ |
| `docs/10` 里提及实体名的行 | **31 行** | 同上 ❌ |

⇒ **因此选「EntityId 全局唯一 + 短名保留为脚本别名」方案**，而不是全局重命名。具体：

1. **EntityId（给 Core / Telemetry / 覆盖率聚合用）改为全局唯一**：`T{teamSeq}F{formationIndex}`，例如 `T2F0`。
   `teamSeq` = `Mission.Teams` 列表中的序号，在 `BindAll()` 遍历时取，**并在 Binding 创建时固化**（已存在的 binding 不重算）。
2. **Alias（可读性，给脚本与 dump 显示用）保留现有 `P0` / `N0` 形式**，不改动任何脚本文件与文档。
3. **别名解析必须做歧义检测**：候选数 ≠ 1 时一律拒绝执行并留痕，严禁沿用当前"取第一个匹配"的行为（`FindBinding`）。

**为什么不用备选方案 B（保持 `N0`，重名时自动加后缀 `N0#2`）**：这会让"脚本里的 N0 到底指谁"取决于 `BindAll` 的遍历顺序，实验对象不可复现，违反 Phase 5 的可重放原则。**不采用。**

---

## 3. 逐步计划

### Step 0 — 版本控制与备份（先做，成本 10 秒）

目标：别再把 Phase 10/11 的成果裸奔在磁盘上。

```powershell
cd E:\MBmod\TacticalCommandLab
git init
git add -A
git commit -m "Phase 1-10 baseline before EntityId fix"
```

> ⚠️ 不要提交 `bin/` `obj/` 别 directories —— 仓库里目前没有 `.gitignore`，先加一个最小版：
> `bin/` `obj/` `deploy/*/bin/`。

**完成判据**：`git log -1` 能看到上述 commit；新跑出来的实测产物不会被覆盖丢失。

---

### Step 1 — 修复 EntityId 唯一性（D1 核心）

文件：`src/CommandLab.BannerlordProbe/ProbeRuntime.cs`

| 位置 | 改动 |
|---|---|
| `FormationBinding`（第 18-59 行） | 增加 `Alias`（`P0`/`N0` 形式的可读名）字段；`EntityId` 改为全局唯一的 `T{teamSeq}F{idx}` |
| `EntityIdOf()`（第 224-230 行） | 拆成两部分：`EntityIdOf(formation, teamSeq)` 生成全局唯一 id；`AliasOf(formation)` 保留现有 `P/N + Index` |
| `BindAll()`（第 547-570 行） | 遍历时带 team 序号：`for (int ti = 0; ti < mission.Teams.Count; ti++)`，把 `ti` 传给 `Bind` |
| `Bind()`（第 232-241 行） | 签名加 `teamSeq` 参数；已存在的 Formation 直接返回既有 binding（**不重算 EntityId**，避免 Teams 顺序漂移导致 id 变化） |
| `SampleShadow()`（第 622-648 行） | 聚合 key 改用新的全局唯一 `EntityId`（现在用的是会冲突的 `entityId + "|" + domain`） |
| `SampleHolds()`（第 694-708 行） | `FindBinding(lease.EntityId)` 改为按 EntityId **精确匹配**（不得按 Alias） |
| `GateCore()`（第 278-338 行） | 无需改动 —— 它已经用 `binding.EntityId`，自动跟随修正 |
| `Observe()`（第 601-615 行） | 同上，无需改动 |

**完成判据**：
- [ ] `EntityIdOf` 在存在多个非玩家 Team 时对同一 `Index` 返回不同字符串
- [ ] `[projection]` 段里每个 EntityId **只出现一次**
- [ ] 8 份脚本**一个字都不用改**仍能解析到实体（靠 Alias）

---

### Step 2 — 加 P0c 门禁 + 别名歧义检测

这一条的价值：**本轮是靠人工分析 7 MB telemetry 才发现 D1 的，成本不可接受。必须让装置自己报警。**

文件：`ProbeRuntime.cs` 与 `ScriptRunner.cs`

1. `ProbeRuntime` 增加自检验数：统计 `_bindings` 中 **唯一 EntityId 数** 与 **binding 总数**。
2. `VerdictRows()`（`ProbeRuntime.cs:779-848`）新增一行：

```
P0c    EntityId uniqueness (bindings == unique ids)   actual=21/21   PASS
```

冲突时 FAIL **并把重复清单直接打印在 `[verdict]` 里**（不要让人再去翻 telemetry）。
3. `ScriptRunner.Resolve()`（`ScriptRunner.cs:104-109`）改为按 **Alias** 解析：
   - 候选 0 个 → `NOTE|SCRIPT_ENTITY_UNRESOLVED <name>`（保留现有行为）
   - 候选 **> 1 个** → `NOTE|SCRIPT_ALIAS_AMBIGUOUS <name> candidates=N` 且**拒绝执行**（现在会静默取第一个，这是致命行为）
   - 候选 = 1 个 → 正常执行
4. `ScriptRunner.cs` 第 12 行的语法注释同步更新，说明短名是 Alias、多 Team 场景下可能歧义。

**完成判据**：
- [ ] 在有多个非玩家 Team 的战斗里，`acquire N0 Movement` 会产生 `SCRIPT_ALIAS_AMBIGUOUS` 并拒绝执行
- 或者：[ ] 若该场战斗 N0 唯一，则正常执行且无上述笔记（两种都可接受，但**必须有一种被实测到**）

---

### Step 3 — 补 D2：把 X5 与 P0c 写进顺序门禁

文件：`docs/10-harmony-runtime-experiments.md`

现在 §7.4 第 1 组只写「P0 或 P0b FAIL ⇒ 停」，而 §5 判据表明确写着 X5 的定义是 `==0`。
⇒ X5 FAIL 拦不住流程，污染数据会一路走进 Phase 11。

把 §7.4 表格第 1 行改为：

```
| 1 | `census-baseline` | P0、P0b、**X5**、**P0c** | 任一 FAIL ⇒ 停，先修装置，后面全部作废 |
```

同时 §5 判据表补一行 `P0c`。

**完成判据**：文档里第 1 组门禁明确列出 4 项且与 `VerdictRows()` 输出的 id 一致。

---

### Step 4 — 本地构建 + 离线回归

```powershell
cd E:\MBmod\TacticalCommandLab
dotnet build src/CommandLab.BannerlordProbe/CommandLab.BannerlordProbe.csproj -v q --nologo
```
须 **0 error / 0 warning**。

```powershell
cd E:\MBmod\TacticalCommandLab\tests\CommandLab.Tests
dotnet run --nologo
```

**完成判据（必须与上一轮验收记录逐字一致）**：
- [ ] `=== 判据汇总：51/51 通过，失败 0 项 ===`
- [ ] `=== 判据汇总：10/10 通过，失败 0 项 ===`
- [ ] `D0-S1 replay_hash = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199`

> 三者任一变化 ⇒ 说明误改了 Core/Simulation/Telemetry/tests，**立即回退**（本轮这四个工程应当零改动）。

---

### Step 5 — 重跑 `census-baseline` 验证

部署纪律（上一轮踩过的坑，逐条照做）：

1. `dotnet build` **只输出到 `deploy\`**，`CopyProbeToDeploy` 这个 target **不会拷进 `Modules\`** ⇒ 必须手工 copy。
2. copy 前**确认 `Bannerlord.exe` 已完全退出**。崩溃残留的半死进程会占着 DLL：
   `tasklist | findstr Bannerlord` —— 若进程在但内存只有几十 K、无窗口，说明是残留，杀掉后立刻重试部署。
3. 部署后做双向校验：`deploy\` 与 `Modules\...\CommandLabProbe\bin\Win64_Shipping_Client\` 下 `CommandLab.BannerlordProbe.dll` **字节数一致**。
4. 把 `census-baseline.txt` 播种到 `%USERPROFILE%\Documents\CommandLabProbe\script.txt`，工作目录里此前的产物先归档（避免误把旧文件当新结果）。
5. **load order 必须精简**：关掉 CombatBrain / CAI / RBM / FrontlineMod 等所有改动 Formation AI 的 mod（`docs/10` §7.0 硬要求）。它们若同样 patch 写入口，两个 Prefix 叠加会让 `[census]` 计数失真。
6. 一脚本一重启。

**判读（只看 `[verdict]`）**：

| 判据 | 期望 |
|---|---|
| `P0` patched methods | `actual=7` PASS |
| `P0b` write-port traffic | `>0` PASS |
| `P0c` EntityId uniqueness | `21/21` 之类，分子==分母 PASS |
| `X5` unobserved_writes | **`==0` PASS** |

**若 X5 仍 FAIL**，此时才可能是真实的架构事实 —— 但要先核对 §7 的归因清单再下结论，**不得直接写成结论**。

---

### Step 6 — 判定与回写（必须做，这是上一轮断掉的地方）

上一轮最大的流程问题：**9-23 跑通的那组实测没有回写任何文档**，导致 HANDOFF 与 memory 全部停在 09-22。

必做：
1. 归档本轮产物到 `tests/artifacts/host/valid-<yyyymmdd-hhmm>-census-baseline/`
2. 更新 `docs/10` §11 → 新增「第二次实测」小节（本轮结果 + D1/D2 的修复验证）
3. 更新 `HANDOFF.md` 第 2 节 Phase 10 那一行（现在写的是"⬜ 待重跑"，已与事实不符）
4. `.workbuddy/memory/` 追加当日工作日志
5. `git commit`

**只有做完 Step 6，本轮才算完成。**

---

### Step 7（可选，需先确认）— 清理 0 字节垃圾文件

工作区根目录有三个 0 字节文件，创建于 2026-09-20 00:01，是 Markdown 正文被误写进了文件名：

```
Phase
取证对象：E:MBmodIDEARBM`
状态：**已完成（只读取证
```

其中两个名字含换行符，需脚本处理。**上轮未擅自删除，待确认后再动。**

---

## 4. 之后的实验阶段（本轮不做，仅登记）

Step 0–6 全部 PASS 之后才允许开跑，且**第一步是先从 `[projection]` 里挑明确实体**（这是新增的 SOP 步骤）：

| 序 | 脚本 | 看什么 | 门禁 |
|---|---|---|---|
| 1 | `census-baseline` | P0 / P0b / **X5** / **P0c** | 任一 FAIL ⇒ 停 |
| 1.5 | **新增：从 `[projection]` 挑实体** | 确认要操作的实体名唯一且无歧义 | 有多 Team 时必须用 `T{t}F{i}` 全名，不用 `N0` |
| 2 | `h1-external-write` | H1-a `native_overrides > 0`、H1-c 首次覆盖延迟 | H1-a FAIL ⇒ 查 P0b |
| 3 | `h2-suppression` | **G1 ∧ G2 必须同表读** | 仅 G1 PASS 而 G2 FAIL = E12 ⑤「伪稳定」 |
| 4 | `h2-coverage` | `[coverage]` 段 Arrangement 域 `unobserved` | 两种结果都记，不算实现有误 |
| 5 | `h3-route-b` | H3-a `SetControlledByAI` 被观测到；Movement 原生写入量是否下降 | 必须记录"Behavior 选择被一起停掉"的代价 |
| 6–9 | `rel-*` 四组 | `release_recovery_failures` | **`rel-after-release` 必须 FAIL**（设计出来的反例，PASS 说明装置检不出失败） |

> ⚠️ H1/H2 的**每条一次只允许引入一个对照变量**，需要对照就换脚本文件，不要在同一份脚本里堆开关。
> ⚠️ Phase 未完成 P0∧P0b∧X5∧P0c 之前，**不许动 Phase 11（`07-final-architecture.md`）** —— 架构结论需要引 Phase 10 实测证据，现在一条都不能引。

---

## 5. 已知坑清单（本轮大概率还会踩）

| # | 坑 | 规避 |
|---|---|---|
| 1 | Harmony `PatchAll()` 只扫自身程序集；**Prefix 形参名必须逐字匹配宿主**（`SetMovementOrder` 是 `input`，`SetControlledByAI` 是 `isControlledByAI`） | 改任何 patch 后跑 `E:\MBmod\CAI\.tools\sigscan <assembly> <type> [methodPrefix]` 核对本名含参数名 |
| 2 | `Formation.GetHashCode()` 在 `Team == null` 时抛 NRE ⇒ `Dictionary<Formation, _>` 必须传引用相等比较器 | `ProbeRuntime.ByReference`（第 211-217 行）**保留，勿删**；任何以 Formation 为键的集合同理 |
| 3 | patch 时机是**正确性**问题：`OnSubModuleLoad` 阶段 patch 会触发 `MovementOrder..cctor()`，而它要求 `Mission.Current != null`，失败后该类型被 CLR 永久标记不可初始化 ⇒ 进战斗必崩 | patch 必须推迟到第一次 Mission tick，`patch-marker.txt` 的 `missionCurrentNull=no` 是自证字段 |
| 4 | Prefix 方法体里**调用 `Gate()` 之前的表达式**也在宿主栈上同步执行，异常直接崩游戏 | 7 个 Prefix 全部要有方法体级 try/catch，异常时 `return true` 放行 + `RecordCrash` 留痕 |
| 5 | `CopyProbeToDeploy` 只拷到 `deploy\`，不进 `Modules\` | 手工 copy + 双向校验 |
| 6 | 崩溃残留进程占着 DLL，`cp` 报 `Device or resource busy` | 看内存/窗口判半死；`Stop-Process` 报"still alive"也可能已释放句柄，**杀完立刻重试部署** |
| 7 | 实验环境未受控（其他 mod 同 patch 写入口） | load order 精简，见 `docs/10` §7.0 |
| 8 | Bannerlord 默认日志**不记 SubModule 加载**；Windows 事件日志无堆栈 | 用 `boot-marker.txt` / `patch-marker.txt` / `crash-*.txt`；本机装了 **CrashDoctor**，其 `Modules\CrashDoctor\managed_crashes.jsonl` 是崩溃堆栈的**唯一有效证据源** |

---

## 6. 本轮红线自检（收尾前做一遍）

```powershell
cd E:\MBmod\TacticalCommandLab
git status                                    # 只应有 BannerlordProbe 与 docs 的改动
find src/CommandLab.Core src/CommandLab.Simulation src/CommandLab.Telemetry tests/CommandLab.Tests -name "*.cs" | xargs grep -l TaleWorlds   # 须为空
git diff --stat HEAD -- src/CommandLab.Core src/CommandLab.Simulation src/CommandLab.Telemetry tests/CommandLab.Tests   # 须为空
```

最后一条是**硬约束**：`src/CommandLab.Core`、`Simulation`、`Telemetry`、`tests` 本轮必须零 diff。
若确有修改必要，先停下来说明原因，不要在 Coupled change 里夹带。
