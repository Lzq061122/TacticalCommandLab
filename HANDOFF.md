# Tactical Command Lab — 会话交接文件（Handoff）

> 生成时间：2026-09-19 01:50 GMT+8
> 生成原因：本周期 Hy4 免费额度已用 **35,695,579 token**（撞顶区间 2,000–4,900 万），继续跑会被 429 中断且上下文全丢。
> 最后更新：2026-10-04 19:56 GMT+8（会话 15，H1 已完成并跑出结论；纯 Vanilla 前提首次成立）
> **2026-10-04 晚 状态更新（覆盖下方 01:33 的旧状态）**：**H1 · External Write 已完成并给出结论 —— 不夺取控制权时外部写入仅存活约 0.15 秒**（run7 `h1-external-write`，纯 Vanilla，H1-c=0.149s / 0.150s）。过程中定位并修复了**两类判据缺陷**：① `native_overrides` 与「不 acquire」在 Core 归属语义里互斥（未 acquire ⇒ Owner=Native ⇒ Lab 写入必 `NotOwner` 拒，被拒写入不计入指标）② 即使放行，Movement 域 Owner 恒为 Native ⇒ `differentOwner=false` ⇒ 该计数结构性恒 0 ⇒ 已降级为记录值 H1-e。同时按验收报告补了 §7.0 合规入口 `bannerlord148-nocb`（去掉 CombatBrain）并**隔离重跑**：run6 `census-baseline` 与 run7 `h1-external-write` 两者 `active_modules` 均不含 CombatBrain，**这是本项目第一批可作为实验结论（而非仅装置自检）的实测**。剩余 H2/H2b/H3/REL 四组未跑。详见 `docs/10` §14.10–14.11 与本文件「会话 15」。
> **旧（2026-10-04 01:33，会话 14）**：会话 14 三轮工作 —— ①Step 0 建 git 基线；②Step 1-3 修 EntityId 唯一性并加 P0c 门禁；③Step 5 用 **CBATH 全自动**进战斗实测两轮。**最终实测 `metrics-20261004-013303-census-baseline.txt`：P0=7 PASS、P0b=1957 PASS、P0c=29/29 PASS、X5=0 PASS**。装置现已可用，可开跑 H1/H2/H2b/H3/REL。详见 `docs/10` §13。
> 状态：**Phase 10 进行中。四次实测均未走通，现终于拿到完整堆栈并定位真根因。** ①首次（10:46）：P0 门禁 FAIL（Harmony 形参名笔误）→ 已修。②③④（11:57 / 12:03 / 13:11）：**进战斗崩溃**。**真根因（有完整堆栈）**：`Harmony.PatchAll()` 处理 `Formation.SetMovementOrder` 时需解析参数类型 `MovementOrder`，**触发其静态构造函数**；而 `MovementOrder..ctor` 的 IL 里有 `Mission::get_Current` → `get_CurrentTime` ⇒ **`OnSubModuleLoad` 阶段 `Mission.Current` 为 null ⇒ NRE** ⇒ **该类型被 CLR 永久标记为不可初始化** ⇒ 进战斗时 `Team.Initialize → Formation.ResetAux` 一接触就抛 `TypeInitializationException` ⇒ 必崩。**证据链**：`MovementOrder` 签名 `b1dfded2` 4 次崩溃；每次致命崩溃前启动阶段都有一次 FirstChance NRE（11:53:29→11:56:56 等 4 对）；**而 10:46 那次 patch 全部失效、启动阶段无此 NRE**。异常被 Harmony 内部吞掉 ⇒ 所以 `patchFailure = (none)`、我们的兜底全部抓不到。**修复**：**把 patch 推迟到第一次 Mission tick**（此时 `Mission.Current` 必非 null）。**§12.8 的 `GetHashCode` 结论已撤回**（该风险真实、加固保留，但不是本次原因）。**当前阻塞：游戏进程 PID 46556（3.9 GB）正占用 DLL** —— 需用户先退出游戏才能部署。**回归：Core/Simulation/Telemetry/tests 本轮零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变。** **版本号偏差**：实际 `v1.4.8.119303`，文档旧记 v1.3.4。根因已定位并修复：Harmony `Prefix` 形参名笔误两处（`SetMovementOrder` 的形参是 `input` 不是 `order`；`SetControlledByAI` 的是 `isControlledByAI` 不是 `isAIControlled`），导致 `PatchAll()` 整体中止、7 个 patch 生效 0 个。一并修掉三个装置缺陷：①异常被常量吞掉（现输出 `HARMONY_PATCH_CAUSE` 异常链）②`Applied` 在 `PatchAll()` 之后才清空（现无论成败都刷新 `GetPatchedMethods()`）③缺**全量镜像**导致 `[census]`/`[coverage]` 无观测对象（现 `BindAll()` 每 tick 绑定全部 formation）。**会话 12 那个"脚本 cmd 触碰建立绑定"的修复已被证实无效并撤销**——`ScriptRunner.TryCommand` 先用 `FindBinding` 解析实体、找不到即静默 return，而绑定只在 patch 触发时创建 ⇒ 鸡生蛋死锁。同时修掉构建部署陷阱：`CopyProbeToDeploy` 只拷到 `deploy\`，**必须手工拷进 `Modules\`**（此前构建了但游戏跑旧 DLL）。装置侧其余前置（会话 10 完成）：修 `HoldMeter` 从未 `Open()`；写入口普查 / 影子采样 / 覆盖率 / `[verdict]` 自检 / 4 个观测型 patch / 8 份脚本。**回归：Core/Simulation/Telemetry/tests 本轮零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变。** 新工具 `E:\MBmod\CAI\.tools\sigscan`（Mono.Cecil 读方法签名含参数名，核对 Harmony 匹配用）。**当前无任何实测结论，§4 全部是 LEVEL C 预测。** 待办：重跑第 1 组 `census-baseline` 直到 P0/P0b 双 PASS，再跑 H1/H2/H2b/H3/REL。**

---

## 0. 给新会话的启动提示词（直接复制这一段）

```text
继续推进 Tactical Command Lab 项目。

先读 E:\MBmod\TacticalCommandLab\HANDOFF.md，那是完整的交接文件，
包含项目目标、红线、12 个 Phase、目录结构、环境约定和已知坑。
读完按里面的「下一步」字段确认当前该做哪个 Phase，然后开工。

本轮只做当前 Phase，不要跨 Phase 推进。做完把进度回写到 HANDOFF.md
的「当前进度」与「下一步」两个字段，并追加一条工作日志，方便下次续跑。

硬性约束（违反即返工）：
- 不引用 CombatBrain.*、不引用 TaleWorlds.*（除 BannerlordProbe 项目外）
- 不用骑兵/弓兵保护/士气/Brace/Skirmish/Protect 等真实战术做基础实验
- 不靠"每 Tick 重复 SetOrder"压过 Vanilla 来实现 Authority
- 不写没有 Telemetry 支撑的稳定性结论
```

---

## 1. 项目目标（一句话）

> 研究并验证一个**通用的 Tactical Command / Authority Framework**，解决「多个决策源如何安全、稳定、可解释地控制同一个 Formation」，以及「Mod 如何与 Bannerlord 原生 `Tactic → FormationAI → Behavior → Order` 控制链建立清晰的控制权关系」。

**首要身份是 R&D，其次才是 Bannerlord Mod 开发。**

---

## 2. 当前进度

| Phase | 名称 | 状态 |
|---|---|---|
| 1 | 问题定义（01-problem-definition.md） | ✅ **完成**（2026-09-19 会话 2） |
| 2 | Bannerlord 原生控制链研究（02） | ✅ **完成**（2026-09-19 会话 2，IL 实证） |
| 3 | RBM 案例研究（03） | ✅ **完成**（2026-09-19 会话 3，静态源码取证） |
| 4 | 通用 Command Authority 架构调研（04） | ✅ **完成**（2026-09-19 会话 4，文献取证 22 源） |
| 5 | 实验设计（05-experiment-plan.md） | ✅ **完成**（2026-09-19 会话 5，E1–E11 + D0 门禁 + 装置规范） |
| 6 | 实现纯 C# Command Lab | ✅ **完成**（2026-09-20 会话 6，D0 门禁 15/15 通过） |
| 7 | 运行 8 组实验（06-results.md） | ✅ **完成**（2026-09-20 会话 7，E1–E11 全跑，**判据 51/51**） |
| 8 | Architecture A/B/C 对比 | ✅ **完成**（2026-09-20 会话 8，13 维度 + E12 补 H-I，判据 10/10） |
| 9 | BannerlordProbe 实现 | ✅ **完成**（2026-09-20 会话 9，net472 离线可构建，红线自检通过） |
| 10 | Harmony Runtime 实验 H1/H2/H3 | 🟨 **第 1 组门禁已全 PASS（2026-10-04 01:33）**：P0=7 / P0b=1957 / **P0c=29/29** / **X5=0**，四项全 PASS（详见 `docs/10` §13，归档 `tests/artifacts/host/valid-20261004-0133-census-baseline/`）。**H1 已完成（2026-10-04 19:56，run7 纯 Vanilla）：H1-a=2 / H1-c=0.149s / H1-d=2 全 PASS，结论「外部写入存活约 0.15 秒」**（详见 `docs/10` §14.11 + 本文件「会话 15」）。⬜ **下一步：跑 H2/H2b/H3/REL** ← **当前** |
| 11 | 最终架构（07-final-architecture.md） | ⬜ 未开始 |
| 12 | CombatBrain 集成（08-integration-report.md） | ⬜ 未开始 |

**已完成：立项书（HANDOFF.md）+ Phase 1–5 交付物（`docs/01` ~ `05`）+ Phase 6 工程（`src/` 三个库 + `tests/` 门禁）+ Phase 7 实验结果（`docs/06` + `tests/Scenarios/Experiments.cs`）+ Phase 8 架构对比（`docs/07a` + `tests/Scenarios/ArchitectureComparison.cs` + E12 证据）+ Phase 9 宿主探针（`src/CommandLab.BannerlordProbe/` + `deploy/CommandLabProbe/` + `docs/09`）。**

### Phase 10 产出摘要（装置就绪 + 首次实测已执行并暴露缺陷 → 已修复待重跑）

**会话 13（2026-09-22）：首次实测 → P0 FAIL → 根因修复。** 详见 `docs/10` §11。

| 项 | 内容 |
|---|---|
| **首次实测结果** | `census-baseline` 的 **P0 FAIL**（`actual=0`），`[patches]`/`[census]`/`[coverage]` 三段全空，telemetry 仅一行 `NOTE\|HARMONY_PATCH_FAILED`。**本轮数据作废**，已归档 `tests/artifacts/host/invalid-20260922-1046-p0fail/`（38 个文件，完整现场）|
| **根因（确定性）** | Harmony `Prefix` **形参名笔误 2 处**：`SetMovementOrder` 的宿主形参是 `input` 而写成了 `order`；`SetControlledByAI` 的是 `isControlledByAI` 而写成了 `isAIControlled`。Harmony 除特殊名外按**参数名**匹配，不符即抛异常并使 `PatchAll()` **整体中止** ⇒ 7 个 patch 生效 **0** 个。**是我们的错误，不是版本问题** |
| **装置缺陷①** | `catch` 只写常量 `HARMONY_PATCH_FAILED`，**吞掉异常** ⇒ 排查变成盲猜。现输出 `Patches.Failure` 异常链：`NOTE\|HARMONY_PATCHED count=N`、`NOTE\|HARMONY_PATCH_CAUSE <链>`，且 `[verdict]` 的 P0 行下方直接打印 cause |
| **装置缺陷②** | `Applied` 在 `PatchAll()` **之后**才 `Clear()`+填充 ⇒ 抛异常时列表保持空，而 **Harmony 中止前的成功项不回滚** ⇒ `[patches]` 空**不等于**"一个都没打上"。现改为 try/catch 包裹、**无论成败都无条件刷新** `GetPatchedMethods()`；`[patches]` 增加 `failure =` / `count =` 两行 |
| **装置缺陷③** | 缺**全量镜像** ⇒ `[census]`/`[coverage]` 无观测对象、伪装成"原生没有写入"。现 `ProbeRuntime.BindAll()` 每 tick 遍历 `Mission.Teams → Team.FormationsIncludingEmpty → Bind`，置于 `SampleShadow` 与 `_script.Tick` 之前；配套 `ScriptRunner.Resolve` 失败写 `NOTE\|SCRIPT_ENTITY_UNRESOLVED` 消除静默失败。**会话 12 的"脚本 cmd 触碰建立绑定"修法已证实无效并撤销**（`TryCommand` 先用 `FindBinding`、找不到即静默 return ⇒ 鸡生蛋死锁）|
| **构建/部署陷阱** | `CopyProbeToDeploy` 只拷到 `deploy\`，**不拷进 `Modules\`** ⇒ 构建成功但游戏跑旧 DLL（实测：新 DLL 10:51:39 vs Modules 里 09-20 23:47）。已写入 `docs/10` §7.1A |
| **新工具** | `E:\MBmod\CAI\.tools\sigscan` —— Mono.Cecil 读**方法签名含参数名**，专用于核对 Harmony 匹配。改任何 patch 后都应跑一次 |
| 交付物 | `docs/10-harmony-runtime-experiments.md`（§7 SOP 7.1–7.8 + §10 TeamAI 取证 + **§11 首次实测记录**）；`deploy/CommandLabProbe/scripts/` 9 份脚本；已部署 `Modules\CommandLabProbe\`（MD5 `4ca2d169…`）|
| **修掉的假阴性缺陷** | `HoldMeter` 在 Phase 9 只被 `new` 从未 `Open()` ⇒ `Intent` 恒 `Unset`、`Sample()` 直接 return ⇒ `intent_hold_share` **恒 0%**。不修则 H2 的 G2 必然 FAIL，会被误读成"夺取失败"。现改为：`Command()` 成功后开窗，支持多窗口累计，Release 后进 `_holdHistory` 仍可在 `[hold]` 段读出 |
| **静态取证①：更正旧登记** | v1.3.4 `Formation.SetControlledByAI` **只有一个方法且是 2 参**（IL：`Ldarg_2` → `_enforceNotSplittableByAI`），不存在 HANDOFF 旧记的"1/2 参重载歧义"。但**形参名是 `isControlledByAI`**（会话 13 用 sigscan 补齐，此前笔误导致 P0 FAIL）|
| **静态取证②：最关键** | `Formation.SetArrangementOrder` 的调用方**清一色是 Behavior**（`TickOccasionally`/`OnBehaviorActivatedAux`）。`Formation::Tick` 里 `_arrangementOrderTickOccasionallyTimer` 调 `ArrangementOrder::TickOccasionally(formation)`、`_arrangementTickOccasionallyTimer` 调 `IFormationArrangement::OnTickOccasionally()` —— **两条都不走写入口**。⇒ 02 §8.2 应精修为"不是走了写入口但门控没生效，而是根本不走写入口"；⇒ 预测（LEVEL C）：把 `ArrangementPath` 切成 Gated 也拦不住这条路径 |
| **静态取证③：Route B 的真实机理** | IL 两分支不对称：`SetControlledByAI(true)` 会 `SetMovementOrder(ActiveBehavior.CurrentOrder)`；`SetControlledByAI(false)` **不写 MovementOrder**，只置 `_enforceNotSplittableByAI=0` + `ActiveBehavior?.OnLostAIControl()`。真正起抑制作用的是 `Formation::Tick` 的门控：`if (IsAIControlled) AI.Tick(); else if (Team.IsPlayerSergeant) AI.Tick(); else IsAITickedAfterSplit=true;` ⇒ **Route B 连 Behavior 选择一起停掉**，代价远大于 Route A。这是架构取舍，不是实现细节 |
| **静态取证④：好消息** | `Formation::Tick` 的 `MovementOrder.GetSubstituteOrder` 兜底循环调用的是 `Formation.SetMovementOrder` ⇒ **Movement 域的这条 non-gated 路径在 patch 覆盖范围内** |
| **静态取证⑤：TeamAI 装配（会话 11）** | 自定义战斗 `MissionCombatantsLogic` 收到 `TeamAIType = 1`（硬编码 `Ldc_i4_1`），`EarlyStart` 遍历 `Mission.Current.Teams` 对每支队伍 `new TeamAIGeneral(mission, team, 10f, 1f)` + `AddTeamAI` ⇒ **攻守双方 AI 均 tick**。更正了此前归于 `BaseBattleMissionController::CreateTeams` 的错误机理（那是战役基类，自定义战斗不走它）。见 `docs/10` §10 |
| **新增采集能力** | `[census]` 写入口普查计数；`[patches]` `GetPatchedMethods()` 生效性取证（**P0 门禁**）+ `failure`/`count`；`SHADOW\|` 影子采样（每 tick 读宿主真值）；`[coverage]` `sampled_changes − port_changes` = 绕过写入口的写入量；`[verdict]` 判据自检 PASS/FAIL；`NativeOverrideLatency`（H1 核心量）|
| **新增观测型 patch** | `SetControlledByAI` / `SetFormOrder` / `SetFiringOrder` / `SetRidingOrder` —— **只计数，一律 `return true` 永不拦截**。附带取证：`SetArrangementOrder` IL 里会级联 `SetFormOrder(FormOrderCustom(CalculateDesiredWidth()),0)` ⇒ 写入口之间存在级联 |
| 回归实证 | D0 **15/15**、E1–E11 **51/51**、E12 **10/10**，逐字不变（会话 10 与 13 均**未改 Core**，改动全在 Probe）|
| **未完成** | ~~重跑 `census-baseline` 直至 P0/P0b 双 PASS~~ **已于 2026-10-04 完成（P0/P0b/P0c/X5 四项全 PASS，见 `docs/10` §13）**；剩余：跑 H1/H2/H2b/H3/REL。**未拿到实测数据前，本报告不写任何 Phase 10 架构结论**（红线）|

### 会话 14（2026-10-02 ~ 10-04）：装置修复 → census-baseline 门禁四项全 PASS

**这是一次纯装置修复（Step 0-7），不产出任何 Phase 10 架构结论。** 三轮迭代：

| 轮次 | 日期 | 结果 |
|---|---|---|
| 设计+编码 | 2026-10-02 | Step 1-3：EntityId 唯一化 + Alias 保留 + P0c 门禁 + 脚本别名歧义 + docs 门禁四项 |
| 实测 1 | 2026-10-03 23:58 | **门禁 FAIL**：`P0c 30/41`、`X5 15036` |
| 实测 2 | 2026-10-04 01:33 | **门禁全 PASS**：`P0 7`、`P0b 1957`、`P0c 29/29`、`X5 0` |

**实测 1 FAIL 的根因（确定性）**：12 个 `Formation` 的 `Team == null` 且 `Index == -1` ⇒ `EntityIdOf` 全算出 `"T-1F-1"`；`SampleShadow` 按 `Key(EntityId, domain)` 聚合 ⇒ 12 个对象共享一个采样 key、互相把对方的值变化当成自己的变化，在 `Unset ↔ Obs0` 乒乓 ⇒ 造出 15036 条假 unobserved。

**修法**：`Bind()` 作唯一收口点，拒绝为「无法唯一标识的 Formation」建绑定（`formation is null` / 读 `Team` 为 null 或抛异常 / `Index < 0`）⇒ 返回 null，不写 `_bindings`、不 `RegisterEntity`；每个对象首次跳过留痕 `NOTE|ORPHAN_FORMATION_SKIPPED`。实测 2 里该 NOTE **恰好 12 行**、`[projection]` 41→29 行、`T-1F-1` 消失 —— 修复自证。

**关键工程事实（供后续复用）**：

- **启动通道不是启动器**：`bin\Win64_Shipping_Client\ModMasterStarter.bat` 用 `_MODULES_*…*_MODULES_` 命令行直接传模块列表，**绕过启动器** ⇒ `LauncherData.xml` 里搜不到 `CommandLabProbe` 属正常，不需要去启动器勾 mod。
- **`engine_config.txt` 的 `safely_exited` 必须是 1**：为 0 时游戏弹 `Safe Mode` 窗口、不加载任何 mod、GABP 端口不 listen（表现极像"CBATH 坏了"）。**游戏每次退出都会把它写回 0 ⇒ 每次启动前必须重写。**
- **无人值守进战斗已跑通**：用 CBATH（`E:\MBmod\CombatBrain Autonomous test harness\tools\zero-touch-3x.ps1` 的同款流程），驱动脚本 `E:\MBmod\TacticalCommandLab\_audit\_tcl-step5.ps1`。前提是把 `*CommandLabProbe*` 加进 gabs `config.json` 的 `_MODULES_` 列表。
- **一个反直觉的观测**：29 个绑定里有 10 个是 `T-1F0..T-1F9`（Team 非 null 但 `TeamSeqOf` 定位失败）。它们各自唯一、不撞名，故本轮不处理，但说明**从 `[projection]` 挑实验实体时应当挑 `T0F*` / `T1F*`，多 Team 必须用 `T{t}F{i}` 全名，不用 `N0`**。

**回归**：Core / Simulation / Telemetry / tests **零改动** ⇒ D0 15/15、E1–E11 51/51、E12 10/10、`D0-S1 replay_hash = d9853e28…d8199` 逐字不变。
### 会话 15（2026-10-04）：H1 实测 → 判据缺陷定位与修复 → 纯 Vanilla 隔离重跑（run6/run7）


**H1（External Write）已跑出结论，但过程中暴露两类缺陷并已修复。**

#### 1. H1 前两轮（r1/r2）：H1-a 恒 0

- r1（`h1-external-write-t1f3.txt`，`enforceGated` 默认 true）：Lab 的 `cmd T1F3 Movement HOLD` **全部被拒**（`rej=1 why=NotOwner`）⇒ `native_overrides = 0` ⇒ H1-a FAIL。
- r2（`h1-external-write-rerun.txt`，唯一差异 `enforceGated=false`）：Lab 写入**获准**（`lab_overrides = 2`）、覆盖真实发生（H1-c = 0.159s），但 `native_overrides` **仍为 0**。

两层根因（源码实证，与装置健康无关）：

1. r1：`ProbeRuntime.cs:174` 默认 Owner = `Native` 且 `Authority.GetOrCreate` 给 `Level = Full`（独占）⇒ 未 acquire 的 Lab 写入必 `NotOwner` 拒；`Metrics.cs:105` `if (a.Rejected) continue;` ⇒ 被拒写入完全不计数。H1 的设计是「不 acquire」，与 `native_overrides > 0` 在 Core 语义里**互斥**。
2. r2（与 enforce 无关）：覆盖记录的 `OwnerAtWrite` 也是 `Native` ⇒ `Metrics.cs:102` `differentOwner = WriterId != OwnerAtWrite` = false ⇒ 进不了 `unauthWrites`，更进不了 `native_overrides`（`:127` 要求 `differentOwner && WriterId == NativeWriterId`）。**`native_overrides` 的语义是「非所有者改动」，而 Movement 域 Owner 恒为 Native ⇒ 该计数结构性恒 0。**

#### 2. 验收报告（8 条）处置

`_audit\2026-10-04-development-acceptance.md`。已改 6 条、登记 1 条、待办 1 条：

| # | 发现 | 处置 |
|---|---|---|
| ① | r1/r2/r3 的 `active_modules` 均含 **CombatBrain**，违反 `docs/10` §7.0 | gabs `config.json` 新增入口 `bannerlord148-nocb`（load order 去掉 `*CombatBrain*`）；驱动脚本在 `get_build_identity` 后**硬校验**，命中即 ABORT |
| ② | `HostContext.cs:19` 的 `behavior` 是 `formation.AI?.ActiveBehavior`，非调用栈 | §14.6 归因**降级**为「部分回答」：只确证写入口（`hook=`）与约 0.5s 频率，具体 Behavior 归属待补 `new StackTrace()` |
| ③ | H1-a 判据与归属语义不符 | H1-a 改为 `accepted_lab_writes > 0`（新增 `AcceptedLabChanges`）；`native_overrides` 降级为 **H1-e 记录值** |
| ④ | H1-c 无条件 PASS 会把 `no-override` 误报成功 | H1-c 改为 `NativeOverrideLatency >= 0` 才 PASS |
| ⑤ | `NativeOverrideLatency` 用全局 `_lastLabChangeAt`，未按实体+域关联 | 改为 `Dictionary<string,double>`，键 = `Key(entityId, domain)` |
| ⑥ | 部署 DLL 与重建产物同尺寸但 SHA256 不同（Cecil 逐项比对差异 0） | 判为**非确定性构建**，不是部署错误；以「部署后重跑门禁」验收，不以哈希一致性验收 |
| ⑦ | HANDOFF 未反映 H1 已跑 | **本段** |
| ⑧ | 建议 5 步顺序 | 采纳；第 1–3 步已完成，第 4 步本节，第 5 步（H2/H2b/H3/REL）待跑 |

#### 3. 纯 Vanilla 隔离重跑（run6 / run7，2026-10-04 19:52–19:56）

`active_modules` 两次均不含 CombatBrain（打印 `pure-Vanilla check ok (CombatBrain absent)`）。

**run6 `census-baseline`**（基线复核，20 文件归档）：

```
P0=7 PASS  P0b=2090 PASS  P0c=29/29 PASS
X1=0  X2=1.2332  X3=4.2660  X4=0.0000  X5=0   全 PASS
```

⇒ 去掉 CombatBrain 后装置仍健康（对比 run3：P0b 1957→2090，同量级）。

**run7 `h1-external-write`**（H1 正式结论，34 文件归档）：

```
P0=7 PASS   P0b=2244 PASS   P0c=29/29 PASS
H1-a accepted_lab_writes   actual=2      PASS
H1-b Movement 原生流量       actual=939    PASS
H1-c first override latency actual=0.149  PASS
H1-d lab_overrides          actual=2      PASS
H1-e native_overrides       actual=0      PASS（记录值，语义恒 0）
X1..X5 全 PASS（X5=0）
```

覆盖对（修复后按实体+域关联计时）：

```
t=3.0064|tick=401|E=T1F3|D=Movement|W=Lab   |cmd=HOLD|prev=Obs0|new=HOLD|chg=1|rej=0
t=3.1559|tick=423|E=T1F3|D=Movement|W=Native|cmd=Obs0|prev=HOLD |new=Obs0|chg=1|rej=0
t=8.0049|tick=1131|...W=Lab...   →  t=8.1549|tick=1152|...W=Native...
```

#### 4. H1 结论（可作为实验结论，非仅装置自检）

- **RQ「不夺取控制权时外部写入能存活多久」→ 约 0.15 秒**（run7 0.149s / 0.150s）。远小于 §4 Hypothesis 的「一个 Behavior 决策周期 0.5s 量级」⇒ **原生在下一个常规 tick 就把值改回去了**。
- **RQ「被谁覆盖」→ 部分回答**：确证「经 `Formation.SetMovementOrder` 写入口、约 0.5s 周期的高频原生 tick」；`behavior=BehaviorMountedSkirmish|weight=1` **不是调用栈证据**，只说明覆盖发生时的 ActiveBehavior。
- **H1 判据已修复并生效**：r1/r2 的 H1-a 恒 FAIL、H1-c 假 PASS；run7 的 H1-a=2 PASS、H1-c=0.149 PASS（已为真门禁）。

#### 5. 本轮工程事实增补（复用价值高）

- **纯 Vanilla 校验必须精确匹配**：`-match 'CombatBrain'` 会把 `CombatBrainAutonomousTestHarness` 误判 ⇒ 自伤 ABORT。改为按 `,` 拆分后 `-contains 'CombatBrain'`。
- **首次冷启动（新 load order）到 bridge 可用需约 4 分钟**：`rgl_log_<pid>.txt` 在 `reading sound_files` 与 `rglShader_manager::read_compressed_shader_cache_package` 之间有 4 分钟空档（着色器/资源编译）⇒ `PollTimeoutSec` 必须给 300；同一 load order 再启动只要 ~7 秒。
- **`_audit\_tcl-step5.ps1` 用 edit 工具改完必须补 BOM**：edit 剥掉 UTF-8 BOM，PowerShell 5.1 按 ANSI 读中文注释 ⇒ 解析错误 `L7: Missing expression after ','`。修法：`[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($true)))`。
- **脚本实体名**：多 Team 时所有 N 系/P 系 alias 都可能歧义（`T1Fx` 与 `T-1Fx` 同名）⇒ 脚本一律用 `T{t}F{i}` 全名（如 `T1F3`）。

**回归**：Core / Simulation / Telemetry / tests 零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10、`D0-S1 replay_hash = d9853e28…d8199` 逐字不变；`ProbeRuntime.cs` 改动 build **0 警告 0 错误**。

**产物**：`_audit\_runs\run6-census-baseline-nocb\`（20 文件）、`_audit\_runs\run7-h1-external-write-nocb\`（34 文件）、`docs\10-harmony-runtime-experiments.md` §14.10–14.11。

**下一步**：跑 H2（suppression / coverage）、H2b、H3、REL 四组；脚本 `-t1f3.txt` 副本已就位（8 份）。

### Phase 9 产出摘要（供 Phase 10 引用）

| 项 | 内容 |
|---|---|
| 交付物 | `docs/09-bannerlord-probe.md`；工程 `src/CommandLab.BannerlordProbe/`（net472，8 个 .cs）+ `deploy/CommandLabProbe/`（SubModule.xml + 脚本样例）。**构建 0 error / 0 warning** |
| **关键工程决策** | 为让宿主侧复用同一套写入口与指标（Phase 9 要点 4 的硬要求），**Core / Telemetry 追加 `netstandard2.0` 目标**：①新增 `Compat/Polyfills.cs`（`IsExternalInit` / `RequiredMember*` / `CompilerFeatureRequired*` / `SetsRequiredMembers*`，Telemetry 以链接方式共享）②三处 net5+ API 替换为等价实现（`Enum.Parse<T>`→`(T)Enum.Parse`、`string.Create`→`string.Format`、`Convert.ToHexString`→`BitConverter.ToString`）③Simulation 保持 net10.0 不上宿主。**回归实证：`replay_hash` 仍 `d9853e28…`、D0 15/15、E1–E11 51/51、E12 10/10，全部逐字不变** |
| **Core 三处纯追加** | `ILabClock`（`VirtualClock`/`ProbeClock` 对 WritePort·Metrics 等价 —— **这是仿真与实测可同表对比的结构保证**）；`WritePort.SubmitImmediate`（宿主必须同步知道放行/拦截，不能等帧末 FlushTick）；`WritePort.SetPolicy`（脚本运行时翻转对照条件，判定逻辑位置不变） |
| **新增 `CommandValue.Obs0..Obs15`** | 中性观察槽（取值 16..31）。Facing/Arrangement 与"无法归入三条中性指令"的 Movement 值按首次出现顺序投影进来，目的只有一个：让 `OrderChanged`/oscillation 判定成立。**超过 16 个不同值会回绕 → 低估变化次数**，`HostOrderProjection.WrapCount` 已计数并进 dump |
| 中性指令 | 只有 `HOLD`/`ADVANCE`/`RETREAT`（Movement 域），通过与 `MovementOrder.MovementOrderStop/Advance/Retreat` 的 `OrderType` 比较得出，**不引用任何 `MovementOrderEnum` 成员名**（规避版本命名漂移）。Facing/Arrangement 只观测、从不下达 |
| **路径归类** | Movement/Facing = `Gated`，Arrangement = `NonGated`（`02` §8.2 门控矩阵）；`ArrangementPath` 可切到 Gated —— 这个切换本身就是 Phase 10 H2 的覆盖率对照 |
| **Telemetry 对齐** | Core 行（`CanonicalLines()`）逐字段不变 → 实测可直接喂 `MetricsCalculator` 与仿真同表对比；宿主专有字段另起 `HOST|` 行（hook / team / behavior / weight / raw / prev / new / ome / chg）；留痕走 `NOTE|` 行，**不进 WriteAttempt 流故不影响 14 项指标**。`OriginalMethodExecuted` 与 Harmony `return false` 是同一个布尔 —— Phase 5 锁定的约定首次在真实宿主侧兑现 |
| **`intent_hold_share` 采样** | `HoldMeter` 读宿主**真值**（`GetReadonlyMovementOrderReference()` / `FacingOrder.OrderEnum` / `ArrangementOrder.OrderEnum`）而非镜像，否则会高估控制权；末段按"状态未变"外推一次（采样频率远高于 FormationAI 的 0.5s 周期，假设成立）；窗口为 0 返回 0% 而非 100% |
| **Acquire/Release 全流程** | `acquire(Full,ttl) → cmd → release` 全通；恢复动作三开关可按位组合：`ReapplyLastNativeOrder` / `ResetBehaviorWeights`（**是否必须重置权重才能恢复**）/ `RestoreControlledByAi`；`RestoreBeforeRelease` 决定恢复在交还前（放行）还是后（被拒 → 计入 `ReleaseRecoveryFailures`） |
| 脚本化驱动 | `%USERPROFILE%\Documents\CommandLabProbe\script.txt` 按任务时间触发：`acquire/cmd/release/renew/policy/dump`，`policy` 可运行时翻转全部对照开关 —— Phase 10 的 H1/H2/H3 靠换脚本产生对照，不需要重编译 |
| 红线自检 | `grep` 实证：Core/Simulation/Telemetry/tests 内 **零 `TaleWorlds`**；全仓 **零 `CombatBrain`**；战术词 cavalry/morale/brace/skirmish/shieldwall/flank/volley **零命中**（`protect` 唯一命中是关键字 `protected`，假阳性）；真实时钟仅 `DateTime.Now` 1 处且只用于 dump 文件名 |
| 未 patch 入口（登记） | ⚠️ **会话 10 已更正**：`Formation.SetControlledByAI` 在 v1.3.4 **只有一个方法且是 2 参**（IL：`Ldarg_2` → `_enforceNotSplittableByAI`），旧记的"1/2 参重载歧义"**不成立**，`nameof` 可精确匹配。会话 10 已补观测型 patch（只计数不拦截），并连同 `SetFormOrder`/`SetFiringOrder`/`SetRidingOrder` 一起做成写入口普查（`02`：写入口是 6 个不是 3 个）。另见会话 10 的关键更正：**Arrangement 自维护 Timer 根本不经过 `SetArrangementOrder`** |
| 偏差登记 8 项 | ①未进游戏运行，本 Phase 无任何实测结论 ②Behavior 权重靠反射取 `Weight`/`WeightFactor`，取不到记 `-` ③Obs 槽回绕会低估变化 ④宿主侧用 `SubmitImmediate`，同刻多候选不做批仲裁（提交序是真实的）⑤镜像只记"被放行的请求值"，未用 Postfix 回读真值（hold 已改读真值，故不影响 G2）⑥`DateTime.Now` 仅用于文件名 ⑦战斗本身可复现性未检验 ⑧probe 纯逻辑无离线单测（依赖 net472+宿主类型，挂不上既有 net10 测试工程） |

### Phase 8 产出摘要（供后续 Phase 引用）

| 项 | 内容 |
|---|---|
| 交付物 | `docs/07a-architecture-abc-comparison.md`；证据 `tests/artifacts/e12-level-spectrum.txt`（**判据 10/10 通过**）。E1–E11 保持 **51/51**、D0 保持 **15/15**、`replay_hash` **未变**（`d9853e28…`） |
| **裁决形态** | **不是三选一，是分层组合**：A 判负 / B 是受限夺取通道 / C 是常态影响力通道。结论由证据导出，未预设赢家 |
| **H-I 裁决：成立且强于 `04` 原表述** | osc：L2=0.0000 < **L4=1.3000** > L5=0.0333。但 `04` 的"要么 2 要么 5"须加限定：**L2 的稳定是伪稳定**（`unauthW=0` 且 Stable 但 **hold=0%**，意图零落地，与 L5 在旧指标上完全同形）；**L4 是真实抖动**（hold=50%、osc=1.3/s）；**只有 L5 同时满足"稳定+落地"**（hold=100%）→ 目标若是取得控制权，**L2 不构成 L5 的替代方案** |
| **新增第 15 项指标口径** | `intent_hold_share`（归属窗口内"实体值 == 意图值"时长占比）。**方法论结论：`unauthorized_writes==0` 且 Stable 是"取得控制权"的必要非充分条件** —— 必须同表判读 hold，否则"从未抢到"与"完全控制"在指标上同形。**移交 Phase 10：H1/H2/H3 必须同时采集 hold** |
| **硬门限 G1+G2** | G1 `unauth_writes==0`（E7 7d）+ **G2 `intent_hold_share ≥95%`**（E12 新增）。A：两条都不满足（E9 9b 120/Oscillating；E12 hold 0–50%）／B：两条都过（E12 ①②）／**C：过不了 G2** —— 非独占语义的结构上限，hold 只能落在 0% 或 50% |
| **E12 ⑨ 写入口覆盖率** | Level 5 + Full 下 gated 域 unauth **0** vs non-gated 域 **20**，全局分级 Stable→Oscillating。**Level 再高也管不到未覆盖路径 → B 的可行性由 patch 覆盖率决定，不由 Level 决定**（与 E8 8c 独立复现） |
| **E12 ⑩ 判别力边界** | 竞争者优先级为 0 时 **L2 与 L4 完全不可区分**（20/1.3 两者相同）→ Level 谱系语义依赖同级竞争者存在。宿主侧对应"是否存在另一个带权重的 Behavior"，**须 Phase 10 H3 实测** |
| 13 维度评分卡 | A：++0/+3/−2/−−6（Stability/oscillation/performance 全 −−）／B：++6/−−5（最好测也最难建，**带覆盖率条件**）／C：++1/+4/0 4/−4（分布最平均，代价是没有控制权） |
| **给 Phase 11 的 4 条架构约束** | ①默认走 C、按需按域升级到 B，常态 Writer 净增须为 0（E9 9a）②B 的升级一律 Level 5+Lease+Token，禁止停在 Level 4/2（E12 ④⑤⑧）③Reject/Fencing 一律在写入口（E3 0 vs 24、E6 0 vs 4）④Release 须显式建模恢复语义，夺取与交还必须同时验收（E5 H-G） |
| 红线自检 | `grep` 实证：`src/`+`tests/` 内 **零 `CombatBrain.*` / `TaleWorlds.*`**、零战术词、**零真实时钟**（唯一命中是 `Scenario.cs` 第 2 行的契约注释） |
| 偏差登记 6 项 | ①`lab_delivery_rate` 口径缺陷（写同值不计变更 → L5 仅 4.8%，不得作判据）②E12 竞争者优先级标定为 3（判别力边界）③`HANDOFF` §5 维度数"12"实为 13 ④释放恢复 0 失败平凡成立 ⑤Ns 仍是模型（沿用 `06` 遗留 1）⑥L3 与竞争者同优先级时胜负由 WriterId 字典序决定，无语义基础 |

### Phase 7 产出摘要（供后续 Phase 引用）

| 项 | 内容 |
|---|---|
| 交付物 | `docs/06-experiment-results.md`；证据 `tests/artifacts/e1-e11-results.txt`（**判据 51/51 通过**）、`tests/artifacts/d0-gate.txt`（**D0 15/15**） |
| **先校准后采集（`05` §9 遗留 2）** | 第一步是校准，并借此发现并修复了 `Metrics.cs` 的一处**指标定义缺陷**：只发生 1 次生效变更时 `average_command_lifetime` 无样本、回退为 0.0，导致"值从头到尾没变过"被判成"抖动"（`0 < 2×T_eval`）。已改为**右删失**补 `sim_seconds − 首次变更时刻`。修复后 7b/7e 由 Oscillating 正为 **Marginal**，E7 的裁决才成立。四个阈值（0.05/0.5/0.2/2×T_eval）经 E1 分布校准**方向正确，未调整** |
| **E7 核心裁决** | 仅 **7d Ownership+Lease** 满足 `unauthorized_writes==0` 且 Stable；7b 迟滞 / 7c 最短驻留 / 7e 状态去抖 `oscillation_frequency` **降 100%** 但 `unauthorized_writes=1` → 判 Marginal。**三者彼此不可区分**（登记为遗留 6）。7f 高频组：attempts 61→600、writes 61→121、osc 2→4，**三项全升**；7f-stall 证明 Ns 的写入从未被真正阻止（[29,30)=2 vs [30,31)=2），高频只是改变 tick 末采样值 |
| **E3 / E6 位置裁决** | Reject 在写入口 **0** vs 在决策器 **24**；Fencing 在写入口 **0** vs 在决策器 **4**（阈值 `delay>0.5s`，由"最后一次发射 3.1 → 代次变更 3.6"决定）。与 `04` C2 吻合，首次有本项目量化数据 |
| E4 | 4a 回收 4.00s（≤4.20）／4b 18 次续约 0 拒绝 ／4c 恢复 0 失败 ／**4d fencing ON 0 vs OFF 29**（`StaleLease ×29`） |
| E5（H-G） | (S,Cancel) 0 失败 vs **(T,Cancel) 1 失败（孤儿定时器 5）**；(T,Resume)/(T,Restart) 0。Restart 相对 Resume `cps` **+43.5%**（**该数字是建模值，只保留定性结论**） |
| E8（条件式） | Arrangement 域在 8a/8c 均出现 `nonGated=86` → **复现 `02` 门控矩阵，Ns 模型保真**。**8c 整实体接管 Arrangement 实际失败**（`unauthW=21`, `nativeOv=21`），全局分级 Stable→Marginal→Oscillating。⚠️ 判据④（恢复失败单调不减）**全为 0、平凡成立**，未支持"接管越多恢复越难" |
| E9 / E10 / E11 | 9a Replace（Writer 净增 0，Stable）vs 9b Append（净增 1，`unauthW=120`，Oscillating）→ **H-B 修正获仿真支撑**；10b transform 开/关 `unauthW` **61 vs 61**（不解决权限问题）→ `04` §6.1 降格建议成立；11a Blackboard 归因 **50%** vs 11b Command Bus **100%** → **H-H 成立** |
| 假设裁决 | **H-F / H-G / H-J / H-E(部分) / H-H 成立**；H-C 弱证据方向支持；**H-I 未验证**（本 Phase 只用 Level 5，Phase 8 需补 Level 2/4/5 对照）。**D5：无任何实验需要 Proposal/Executor** → 弱结论"Proposal 不必要"，待 Phase 11 裁定 |
| 三处偏差已登记 | ①`05` §E1 的 4 组配置对"终值"口径结构性不敏感，判据③ 靠"最后一 tick 为同刻碰撞点"的对齐变体（56.0s）才成立 ②E7 7f 实测与 `05` 预测方向不一致（预测 osc 下降，实测同升）——结论更强 ③E8 判据④ 平凡成立 |
| 红线自检 | `grep` 实证：`src/`+`tests/` 内 **零 `CombatBrain.*` / `TaleWorlds.*` 引用**，零 cavalry/morale/brace/skirmish/protect 等战术词 |

### Phase 6 产出摘要（供后续 Phase 引用）

| 项 | 内容 |
|---|---|
| 工程 | `CommandLab.slnx`：`CommandLab.Core` / `CommandLab.Simulation` / `CommandLab.Telemetry` / `CommandLab.Tests`，**net10.0，零第三方包依赖**（离线可构建，无 NuGet restore） |
| 施工依据 | 严格按 `05` §8 的 11 项必需清单实现；**`Proposal` / `Executor` 未实现**（D5 保留"Proposal 不必要"结论的可能性） |
| **硬约束 1 落地方式** | `ControlledEntity` 的状态 setter 是 **internal**，只有同 assembly 的 `WritePort` 能改 → Source 在**结构上**无法旁路写入口。Reject + 归属 + Token/Generation 全部在 `WritePort.Evaluate()` 内部 |
| **硬约束 2 落地方式** | `AuthorityLevel` 六档枚举（None/Monitor/Recommend/Approval/Override/Full）+ `CanWrite`/`IsExclusive`/`ArbitrationPriority` 权限表。**无任何布尔 HasAuthority** |
| **Ns 双路径** | `WritePath.Gated`（Behavior 侧 Mv/Fa，`EnforceAuthorityOnGated` 默认 true）与 `WritePath.NonGated`（自维护双 Timer + SubstituteOrder 兜底，`EnforceAuthorityOnNonGated` 默认 **false** = 复现 `02` 门控矩阵）**两条独立代码路径**，参数 `T_eval=0.5s` / 错峰 / 迟滞 / `AllowAiTicking` 均可配 |
| 仲裁 | `ArbitrationMode {None, Priority, Utility}` 实现在**写入口内部、归属校验之后**（对应 `04` C1 的"Ownership 先筛非法源，Arbitration 再选值"）。同 tick 多候选缓冲后统一裁决，**同分按 WriterId 字典序** → 与提交序无关，故仲裁本身顺序无关（E2 要证的性质） |
| E7 五手段旋钮 | `WritePortPolicy`：`HysteresisRatio` / `MinLifetimeSeconds` / 归属+Lease / `LabSource.StateDebounce` / `ArbitrationMode.Priority`。全部为**配置**，不是新抽象 |
| **E6 延迟队列** | `CommandDelayQueue`（Simulation 层）：`LabSourceConfig.DeliveryDelay>0` 时经队列投递，产生真实"在途"语义。这是 `05` E6 Failure Criteria 的硬要求，缺它 E6 会退化成同步执行 |
| E3/E6 对照组开关 | `EnforceAuthorityOnGated`（3b vs 3c）、`LabSource.CheckOwnershipInDecider`、`EnforceFencing`（4d/6b/6c）、`LabSource.CheckGenerationInDecider` —— 保证"判别力"是可切换的而非靠改代码 |
| E10 / E5 支撑 | `WritePortPolicy.ValueTransform`（只改值、永不抑制、异常兜底）；`NativeSource` 的 `OrphanAllTimers` / `RestoreTimerState` / `ResetToBase` + `RecoveryProbe`（Release 后比对基线快照 + 定时器数） |
| Telemetry | `TelemetryCollector` 记录**每一次写入尝试（含被拒）**；字段与 `05` §1.4 一致（含 `OriginalMethodExecuted`，不得改名）；`replay_hash` = 规范化行序列的 SHA256；`DecisionTrace` 渲染格式承接 §1.4 样例 |
| 指标 | `MetricsCalculator` 实现 `05` §1.5 全部指标 + 稳定性三级分级（`2×T_eval` 判据已编码）。**新增两项派生口径**：`unauthorized_write_attempts`（含被拒，对应 E3 3a 的"能看到违规行为"）与 `unauthorized_writes`（仅生效变更） |
| **D0 门禁结果** | **15/15 PASS**。证据 `tests/artifacts/d0-gate.txt` |

### Phase 5 产出摘要（供后续 Phase 引用，详见 `docs/05-...` 全文）

| 项 | 内容 |
|---|---|
| 规模 | 9 节。**D0 装置门禁 + E1–E11 共 11 个实验**（8 组必做 + E9/E10/E11 承接 Phase 3/4 遗留），每个写全 **RQ / Hypothesis / Test / Metric / Success / Failure** 六段 |
| **装置规范（§1）** | 虚拟 `VirtualClock`（`dt=0.1s`，默认 60s）；**禁止 `Thread.Sleep` 作为主要机制**；单一播种 RNG；显式注册序可调（用于证明"顺序敏感"本身）；`replay_hash` 校验可重放 |
| **Ns 原生仿真模型（关键）** | 参数直接取自 Phase 2 IL 取证：`T_eval=0.5s`、按 index 错峰 ×0.1、`PreserveExpireTime` 迟滞可开关、**gated 写入（Behavior 侧 Mv/Fa）与 non-gated 写入（Arrangement 双 Timer + SubstituteOrder 兜底）实现为两条独立路径**、6 写入口取 3。**这是全套实验能对真实宿主产生预测力的唯一原因；若把两条路径合并实现，E8 立即失去预测力** |
| **指标精确化** | 把 §8 的 14 项指标给出**可计算公式**，并定义**稳定性三级分级**：稳定（`unauthorized_writes==0` 且 `oscillation_frequency ≤0.05/s` 且 `order_changes_per_second ≤0.2`）/ 临界（须补跑 300s）/ 抖动（`oscillation_frequency ≥0.5/s` **或** `average_command_lifetime < 2×T_eval`）。**第三条把 Phase 1 §3.2 的特征签名直接写成了阈值** |
| Telemetry 补齐 | 承接 §8 格式，本阶段补 `AuthorityLevel / LeaseToken / TaskId`；**每次写入尝试（含被拒）都必须记录**；字段名与 Phase 10 保持一致（`OriginalMethodExecuted` ↔ Harmony Prefix `return false`），保证仿真与实测可同表对比 |
| **判别力矩阵（§6）** | 11 条"哪两个机制由哪个实验区分"的对照表。**Phase 8 的 A/B/C 必须引用实验编号作为证据，不允许用"理论上应当更好"下结论** |
| 三处可量化的新 Prediction | ①**E7 7f 是反例演示组**：必须跑出"提高写入频率后表面变稳但 `unauthorized_writes` 不降"→ 这是"被竞速掩盖"的可辨识证据（对应红线）②**E4 4d lost-owner**：有/无 Token 两个变体必须得出不同结果，否则本实验无判别力 ③**E8 三级递进**：必须复现 `02` 门控矩阵（Arrangement 域仍出现 non-gated 写入），否则判定 Ns 模型失真、**须回修 Ns 并重跑受其影响的 E2/E3/E5/E7** |
| ⚠️ E8 权限声明 | E8 只能给**条件式预测**（"若宿主行为如 Ns 模型，则…"），**不得**用于回答 §10.6 的实测问题（`02` §8.1 已锁定 Phase 10） |
| Phase 6 施工清单（§8） | 按"哪些实验逼着你实现"排序。必需 11 项；**`Proposal` / `Executor` 无实验强制需要 → 不实现**（D5：允许得出"Proposal 不必要"）。两条硬约束：①**Reject 与 Token 校验必须在 `WritePort` 内部**，不许放在 Source 决策函数里（否则 E3 3b/3c、E6 6b/6c 全部失去判别力）②Authority 必须按 **Level 声明**，不得做成布尔 `HasAuthority`（否则 H-I 无法验证） |
| 假设覆盖 | 本计划验证 **H-D、H-E、H-F、H-G、H-J（仿真侧）+ H-H（E11）+ H-C（弱证据，条件式）**；**H-A / H-B 不在本 Phase**（分别属 Phase 12 / Phase 3·E9） |
| 遗留 6 项 | Ns 是模型非实测；**稳定性阈值是先验设定值尚未被数据校准**（Phase 7 应先校准再采集）；E5 的 Class T 建模深度决定 H-G 结论真实性；E2/E7 的噪声/效用差参数需预调，否则无判别力（已写入 Failure Criteria 强制重做） |

### Phase 4 产出摘要（供后续 Phase 引用，详见 `docs/04-...` 全文）

| 项 | 内容 |
|---|---|
| 规模 | 9 节 + 来源清单 **S1–S22**（Game AI 侧 7 类机制 + 通用系统侧 9 类机制），全部标 `资料明确证明` / `源码分析` / `工程推断` / `假设` 四档证据等级 |
| **C1 核心结论** | **成熟系统确定控制权用 Ownership，不是 Arbitration**；Arbitration 只在 Ownership 已确定后的合法候选集内部使用。两者**串联而非互斥**：Ownership 先筛非法源，Arbitration 再选值 |
| C2/C3 | Ownership 必须带 **Lease**（Chubby session lease：持有者崩溃后自动回收；KeepAlive 占 93% RPC）；**强制点必须在被保护资源（写入口）**，持锁者侧自查无效 → 直接推导：Reject 必须写在 `Set*Order` 那一级，不能写在决策器里 |
| **C5** | **通用文献中不存在"Peer 仲裁"**——Blackboard 有 scheduler、Contract Net 有 announcer、Blackboard==都要集中控制器。与 Phase 3 从 RBM 归纳的"**仲裁权不下沉**"**独立吻合**（跨领域交叉验证） |
| **C6（重要）** | **Phase 1 第 5 类 Native Pipeline 在通用文献中无任何对应机制**。最接近：Actor 的"外部只能经消息进入" + CQRS 的单一权威写入口 → **Phase 8 的 A/B/C 无法从文献获得背书，必须由 Phase 7/10 Telemetry 裁决**（这是"不得预设赢家"的正面理由） |
| **C7（新洞察）** | 冲突的**深层语义错配**：宿主把 Formation 当 **Blackboard**（多源共享可变状态、无 owner、Phase 2 已实证），而 Mod 需要的是 **Command Bus** 语义（单一权威写入口、CQRS 的写模型）。两者是不同架构风格 → 冲突根因可能是风格错配而非实现缺陷 |
| **四组对照（硬性要求）** | ①**Arbitration vs Ownership**：判别规则 = "**我能不能让输的一方停止产出候选？**" 能→Arbitration；不能→必须 Ownership + 写入口拦截 ②**Hierarchy vs Peer**：Hierarchy 失效=下级无 veto 通道（BT 的解法是"在高层加安全分支"=让上层看见，不是下层上报）；Peer 不能省控制器 ③**Lease vs Permanent**：Lease 管"拿不回来"，**Fencing 管"回来的路上旧写入还在飞"**，必须配套；无 fencing 的锁只是 efficiency lock ④**Preemption 恢复语义**：严格来源=实时系统 Mode Change 三协议 **Immediate→Cancel / Bounded→Resume / Phased→Restart**，**选择依据是数据一致性而非性能**；工程模板=K8s `SIGTERM→graceful 30s→SIGKILL`（Bounded 降级为 Immediate） |
| 六种机制范式 | Arbitration / Ownership+Lease / **Conflict Detection（MVCC，事后检测——不适用，宿主 Order 无版本无回滚）** / Hierarchy / Commitment+Cancel / **Transformation** |
| ⚠️ 对 Phase 3 的修正建议 | **Transformation 表述过重**：它不解决任何一类冲突（不改"谁在写"），建议改登记为"**与 Authority 正交的第四种机制**"而非"第六类冲突"。通用证据=BT Decorator（只改子节点返回状态）+ RBM `OverrideSetMovementOrder`；**仍待 E10 定稿** |
| 成熟度一副落差 | 第 2 类的解法在文献里是**标准工程实践级**（锁+租约+fencing），而 Mod 生态普遍在做 Arbitration 或频率压制 → **本项目收益可能主要来自"搬过来"而非"发明新机制"** |
| Authority Level 谱系 | 迁移 Sheridan/Playbook 的 7 级授权 → 本项目 0 None / 1 Monitor / 2 Recommend(=路线C) / 3 Approval / 4 Override(⚠️**文献明确警告的不稳定级**) / 5 Full(=路线B)。**推论：要么 2 要么 5，不要停在 4** |
| **新增假设** | **H-F**（Lease+资源侧 Reject 能把 `unauthorized_writes` 降到 0，迟滞/限流不能）/ **H-G**（恢复语义取决于执行体是否持有宿主内部状态）/ **H-H**（Blackboard↔Command Bus 风格错配是第 5 类根因）/ **H-I**（Level 4 稳定性显著低于 2 与 5）/ **H-J**（Generation 校验必须落在写入口）→ 全部待 Phase 7/8/10 验证 |
| 给 Phase 6 的 5 条硬约束 | ①必须显式建模 **Transition 语义**（Resume/Restart/Cancel），不能只有 Acquire/Release ②Reject 在写入口 ③Lease 与 Token 配套 ④Authority 按 **Level 声明**而非布尔值 ⑤Domain 粒度授权，但能否细分到三域由 Phase 10 实测决定 |
| 遗留 7 项 | RecoilEngine 为二手转述未核源码；MRTA/拍卖机制未深入；MVCC 对照未系统取证；HTN 运行时控制权语义弱；Orkin orders-as-goals 等价性是外推；自动化等级谱系跨域未实测；**无任何实测数据支撑** |

### Phase 3 产出摘要（供后续 Phase 引用，详见 `docs/03-...` 全文）

| 项 | 内容 |
|---|---|
| 规模 | **81 个 Harmony patch**（40 Prefix / 41 Postfix），覆盖 **51 个目标类型**；入口 `RBMAiPatcher.DoPatching()` 用 `PatchAll()` |
| 9 必答项 | ①81 patch/51 类型 ②**Tactic 与 Behavior 都 Patch**（17 个 Behavior + 10 个 Tactic/TacticComponent），但**写入只在 Behavior 侧** ③**改 Weight 是主要通道**，且整体替换 `SetDefaultBehaviorWeights`(return false) ④**替换** `CalculateCurrentOrder`（只写私有字段，不碰 Formation）⑤**直接 SetOrder，但仅在 Behavior patch 内部** ⑥**Prefix `return false` 是主要手段**（接管型 + 条件型两种）⑦**仍用 FormationAI 仲裁**（`typeof(FormationAI)` **零命中**）⑧**无独立 Commander**（Commander/Arbiter/Proposal/Executor 全零命中）⑨**Writer 净增 = 0** |
| **H-B 判定** | **方向支持，表述需修正**：不是"减少 Writer"，而是 **"替换 Writer 而非新增 Writer"**——写入点位置与数量不变，只替换内容，仲裁权仍完全留给 FormationAI。区分重要：前者要对抗原生链（路线 B），后者只需接入（路线 C），**后者可复制性远高于前者** |
| 对路线 A/B/C | **RBM = 路线 C** 的真实工程案例（不动 FormationAI，挂在 Behavior/Tactic 权重上）。A 与 B 均无 RBM 背书。**但只是一个案例，Phase 8 不得预设赢家** |
| 三条可迁移原则 | ①仲裁权不下沉（不动 FormationAI 就不用自己解决谁上）②替换而非并列（要改就 return false 替换，不在旁边加写入者）③计算与写入分离（严格保持原生契约） |
| **新发现：值变换层** | `OverrideSetMovementOrder` = Prefix on `SetMovementOrder`，**只改 `ref input` 的值，永远 `return true` 从不抑制**，带 try/catch 兜底。**与 Authority 完全正交** → 建议 Phase 1 增补第六类冲突「Content Transformation」，**待 Phase 5 用 E10 验证后再决定是否纳入** |
| 新增候选实验 | E9 Replace vs Append（替换 Writer vs 并列 Writer 的 `unauthorized_writes` 差异）；E10 Value Transform 层的能力边界 |
| 对 CombatBrain 的含义 | **CombatBrain 的 Proposal/Arbiter/Executor 与 RBM 是两条相反的路**——不等于 CB 错，但 **CB 的结构无法从 RBM 获得背书**（Phase 12 用） |
| ⚠️ Phase 2 更正 | `Formation::Tick` 的 SubstituteOrder **不是**每 tick 无条件写入，而是**兜底循环**（`IsApplicable==false` 才触发，单帧上限 10 次）。已同步改 `02` 文档 §8.2 → **§8.1 对 §10.6 PoC 的倾向性判断理由被削弱**，但 Arrangement 双 Timer 不受门控仍成立，PoC 仍需 Phase 10 实测 |
| 遗留 | RBM 实际运行时是否真不抖动（**Phase 10**，不得以本 Phase 代替）；RBM 版本号未采集 |

### Phase 2 产出摘要（供后续 Phase 引用，详见 `docs/02-...` 全文）

| 项 | 内容 |
|---|---|
| 核心结论 | **Formation 不存在单一最终 Writer** —— 它是多入口共享可变资源（决策 Writer / 准入 Writer / 常驻自维护 Writer / 外部 Writer 四类） |
| ① 最终 Writer | 6 个写入口 `SetMovementOrder`(108 调用方) / `SetFacingOrder`(71) / `SetArrangementOrder`(58) / `SetFiringOrder`(54) / `SetFormOrder`(60) / `SetRidingOrder`(8)。**传统"三域"只是子集** |
| ② Tactic | **从不直接写 Order**，只走 `ResetBehaviorWeights()` + `SetBehaviorWeight<T>()`；Behavior 集合在 `OnUnitAddedToFormationForTheFirstTime` 时一次性注册 |
| ③ FormationAI | 周期 **0.5 s**（`Timer(..., 0.5, true)`，按 TeamIndex/FormationIndex 错峰 ×0.1）；评分 `GetAIWeight() × WeightFactor`；非当前行为受 `PreserveExpireTime` 迟滞折扣；`NavmeshlessTargetPositionPenalty` 参与；全局闸 `Mission.AllowAiTicking` |
| ④ Behavior | `CalculateCurrentOrder()` **不写 Order**，只置 `CurrentOrder`；写入在 `TickOccasionally`(Movement+Facing) 与 `OnBehaviorActivatedAux`(Movement+Facing+Arrangement+Firing+Form)；非当前 Behavior 只参与计算不写入 |
| ⑤ 门控矩阵 | `IsAIControlled` **覆盖 Behavior 侧但不覆盖 Formation 自维护侧**（`Formation::Tick` 的 `GetSubstituteOrder` 回写、Arrangement 两个独立 Timer 均不受门控） |
| 对 §10.6 PoC | 倾向于"会有残留"（`[推断]`），但**明确要求 Phase 10 Telemetry 实测**，不得沿用本 Phase 推断也不得沿用旧推论 |
| 可复用 Telemetry 点 | `OnBeforeMovementOrderApplied` / `OnAfterArrangementOrderApplied` / `OnActiveBehaviorChanged` 三个现成事件 |
| 对 Phase 8 的影响 | 路线 B 的 Patch 面**至少 6 处且分散** → 复杂度高于直觉；路线 C 的挂载点明确，其中 `CalculateCurrentOrder` 是唯一"只算不写"的纯函数式注入点 |
| 遗留 | `MovementOrderEnum` 值 2 语义未定；Tactic 切换周期未取证（不阻断 Phase 5）；游戏版本号未采集 |

### Phase 1 产出摘要（供后续 Phase 引用）

| 项 | 内容 |
|---|---|
| 核心论断 | **排序机制（priority/utility/arbitration）只能解决 Command Conflict**；其余四类为二元/结构性问题，用连续权重解必然产生抖动、饥饿或归因错误 |
| 分层模型 | L4 Pipeline / L3 Plan / L2 Intent·Task / L1 Command / L0 Entity·Domain。**症状出现在 L1，病根常在 L0 或 L4** |
| 五类冲突 | 每类含：定义、性质、触发条件、中性最小复现、"为什么不能用其他机制"、适用机制、误配失败模式、**Telemetry 特征签名** |
| 正交判定表 | 五类在「层 / 性质 / 可加权 / 解决单元 / 时间尺度 / 正确机制 / 误用后果」七个维度上的取值 |
| 诊断表 | 6 条症状 → 类别（含判别指标），Phase 7/10/12 直接复用 |
| Q7 分解方法 | 按签名对异常 Order 事件归类计数 → 量化"多少是 Arbitration、多少是 Authority Architecture" |
| 待验证假设 | H-A~H-E。**H-E 是本文档的证伪条件**：若签名不可区分，须回头修订分类而非调阈值 |
| 明确不做 | 无代码 / 不读 CombatBrain / 不查 DLL / 不判 A·B·C 胜负 |

---

## 3. 已创建 / 修改的文件

| 路径 | 说明 |
|---|---|
| `E:\MBmod\TacticalCommandLab\HANDOFF.md` | 本文件。交接用，不属于交付物清单。 |
| `E:\MBmod\TacticalCommandLab\docs\01-problem-definition.md` | **Phase 1 交付物**（会话 2 产出）。五类冲突模型 + 正交判定表 + 诊断表 + H-A~H-E 假设。 |
| `E:\MBmod\TacticalCommandLab\docs\02-bannerlord-control-pipeline.md` | **Phase 2 交付物**（会话 2 产出）。原生控制链 IL 取证 + 门控矩阵 + 8 项待验证清单。**会话 3 已更正 §8.2**（SubstituteOrder 为兜底循环而非每 tick 写入）。 |
| `E:\MBmod\TacticalCommandLab\docs\03-rbm-case-study.md` | **Phase 3 交付物**（会话 3 产出）。RBM 81 个 Harmony patch 全枚举 + 9 必答项 + H-B 判定与修正 + 值变换层新发现 + 附录 A（Phase 2 更正与 `MovementOrderEnum`）。 |
| `E:\MBmod\TacticalCommandLab\docs\04-command-authority-survey.md` | **Phase 4 交付物**（会话 4 产出）。通用 Command Authority 调研：控制权的 **Subject/Object/Duration/Transition 四要素模型** + Game AI 侧 7 类 + 通用系统侧 9 类机制取证（**来源 S1–S22**）+ 四组对照（Arbitration vs Ownership / Hierarchy vs Peer / Lease vs Permanent / Preemption 三语义）+ 与 Phase 1 五类冲突的**覆盖率映射** + 六种机制范式总表 + 新增假设 **H-F ~ H-J** + 给 Phase 6 的 5 条硬约束 + 遗留 7 项。**零 `CombatBrain.*` / `TaleWorlds.*` 引用。** |

| `E:\MBmod\TacticalCommandLab\docs\05-experiment-plan.md` | **Phase 5 交付物**（会话 5 产出）。实验计划：装置规范（虚拟时钟 / 播种 RNG / `replay_hash` / 禁 Sleep）+ **Ns 原生仿真模型（参数取自 Phase 2 IL：`T_eval=0.5s`、迟滞可开关、gated 与 non-gated 双写入路径）** + 14 项指标的**精确定义与稳定性三级分级** + Telemetry 补齐字段（`AuthorityLevel`/`LeaseToken`/`TaskId`，与 Phase 10 同名可同表对比）+ **D0 装置门禁** + **E1–E11 共 11 个实验六段式详单** + **判别力矩阵**（规定 Phase 8 必须引用实验编号作证据）+ 红线自检表 + **Phase 6 施工清单**（必需 11 项；`Proposal`/`Executor` 因无实验强制需要而**不实现**；两条实现硬约束） |
| `E:\MBmod\TacticalCommandLab\docs\06-experiment-results.md` | **Phase 7 交付物**（会话 7 产出）。校准（含 `average_command_lifetime` 右删失缺陷修复）+ E1–E11 全部结果 + 假设裁决表 + 判别力矩阵回填 + 给 Phase 8 的 6 条可引用结论 + 8 项遗留。**判据 51/51 通过。零 `CombatBrain.*` / `TaleWorlds.*` 引用、零真实战术语义。** |
| `E:\MBmod\TacticalCommandLab\tests\CommandLab.Tests\Scenarios\Experiments.cs` | **Phase 7 实验执行器**（会话 7 产出）。E1–E11 全部场景 + 51 项 Success/Failure 判据自检程序化执行；`Program.cs` 在 D0 通过后自动调用。 |
| `E:\MBmod\TacticalCommandLab\tests\artifacts\e1-e11-results.txt` | **Phase 7 原始证据**（每次运行覆盖写）：各实验指标、判据 PASS/FAIL、DecisionTrace 样例。 |
| `E:\MBmod\TacticalCommandLab\docs\07a-architecture-abc-comparison.md` | **Phase 8 交付物**（会话 8 产出）。A/B/C 精确定义（用"写入者净增数 × 强制点位置"两个可观测变量界定，不用形容词）+ **E12 Authority Level 谱系对照**（补 H-I：L1–L5 五档，`unauthW` / osc / hold 三口径）+ **13 维度证据化评分卡**（每格必挂实验编号）+ **硬门限 G1/G2** + 分层组合架构结论 + 4 条给 Phase 11 的约束 + Phase 9/10/11 移交项 + 6 项偏差登记。**零 `CombatBrain.*` / `TaleWorlds.*` 引用。** |
| `E:\MBmod\TacticalCommandLab\tests\CommandLab.Tests\Scenarios\ArchitectureComparison.cs` | **Phase 8 实验执行器**（会话 8 产出）。E12 主矩阵（L1–L5）× 写入口覆盖率组（B）× 判别力对照组（C）+ 10 项判据自检；新增 `intent_hold_share` / `lab_delivery_rate` 两个口径；`Program.cs` 在 E1–E11 之后调用。 |
| `E:\MBmod\TacticalCommandLab\tests\artifacts\e12-level-spectrum.txt` | **Phase 8 原始证据**（每次运行覆盖写）：五档指标、reject 原因直方图、释放恢复、H-I 裁决文本。 |
| `E:\MBmod\TacticalCommandLab\src\CommandLab.Simulation\NativeSource.cs` | **会话 8 唯一改动的 src 文件**：新增 `NativeSourceConfig.Priority`（默认 **0.0**，与既有实验完全一致，E1–E11 结果与 `replay_hash` 均未变）。用途是让 Level 谱系在"存在同级竞争者"条件下可观测。 |
| `E:\MBmod\TacticalCommandLab\docs\09-bannerlord-probe.md` | **Phase 9 交付物**（会话 9 产出）。定位与红线 + **多目标 netstandard2.0 的工程决策与回归证据** + 写入流数据流图 + 中性指令与 Obs 观察槽口径 + Telemetry 对齐表（Core 行不变 / HOST 行）+ `intent_hold_share` 采样口径 + Acquire/Release 全流程与三档恢复开关 + 脚本语法 + 构建部署步骤 + Phase 10 移交项 + 8 项偏差登记。**零 `CombatBrain.*`。** |
| `E:\MBmod\TacticalCommandLab\src\CommandLab.BannerlordProbe\` | **Phase 9 工程**（会话 9 产出，net472，唯一允许引用 `TaleWorlds.*`）：`ProbeClock.cs` / `NeutralOrders.cs` / `ProbeConfig.cs` / `ProbeRuntime.cs` / `HostContext.cs` / `OrderWritePatches.cs` / `ScriptRunner.cs` / `ProbeSubModule.cs`。构建后自动输出到 `deploy/`。 |
| `E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\` | **Phase 9 部署骨架**：`SubModule.xml`（依赖 `Bannerlord.Harmony` v2.4.2.248）+ `script.sample.txt`（H1/H2/覆盖率/恢复四组样例）+ `bin/Win64_Shipping_Client/`（构建产物）。拷进 `Modules/` 即可用。 |
| `E:\MBmod\TacticalCommandLab\src\CommandLab.Core\Compat\Polyfills.cs` | **会话 9 新增**：netstandard2.0 目标需要的编译期标记类型（`IsExternalInit` / `RequiredMember*` / `CompilerFeatureRequired*` / `SetsRequiredMembers*`）。`NET5_0_OR_GREATER` 下整文件为空，对 net10.0 零影响；Telemetry 以链接方式共享同一份。 |
| `E:\MBmod\TacticalCommandLab\docs\10-harmony-runtime-experiments.md` | **Phase 10 交付物（装置部分 + 首次实测记录）**。§0 状态表 / §1 为何先静态取证 / §2 静态取证 4 条（含"Arrangement 自维护路径不走写入口"、"`SetControlledByAI` 只有 2 参一个方法"）+ §3 装置变更 / §4 H1/H2/H2b/H3/REL 五组六段式协议 / §5 判据表（P0/P0b/G1/G2/G3/X5）/ §6 产物格式 / **§7 采集 SOP（7.1A 构建部署、7.1B 游戏设置、7.2 兵力兵种、7.3 每场流程、7.4 顺序门禁、7.5 判据读法、7.6 一脚本一重启、7.7 收尾与重放、7.8 观测对象=全量镜像）** / §8 已知限制 / §9 下一步 / **§10 自定义战斗 TeamAI 装配取证（LEVEL B，IL）** / **§11 首次实测记录：P0 FAIL 与根因（Harmony 形参名笔误 ×2、三个装置缺陷、部署陷阱、门禁价值评估）**。**LEVEL C 预测均已标注；§11 之后的实测结论仅限装置自身。** |
| `E:\MBmod\CAI\.tools\sigscan\` | **新建的取证工具**（会话 13）。Mono.Cecil 读程序集元数据，打印方法签名**含参数名**。用法 `sigscan <assembly> <typeName> [methodPrefix]`。**用途**：核对 Harmony `Prefix`/`Postfix` 形参名 —— 这是 `PatchAll()` 能否成功的决定性因素（2026-09-22 因两处笔误导致 7 个 patch 生效 0 个）。与 `ilscan` 分工：`ilscan` 出 IL 指令流，`sigscan` 出元数据签名。依赖 `Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\Mono.Cecil.dll`。 |
| `E:\MBmod\TacticalCommandLab\tests\artifacts\host\invalid-20260922-1046-p0fail\` | **作废实测产物归档**（38 个文件）。首次进游戏运行的完整现场：`metrics-*`（P0/P0b 双 FAIL、三个空段）+ `telemetry-*`（唯一内容 `NOTE\|HARMONY_PATCH_FAILED`）。保留用于回归比对与 §11 的证据溯源。 |
| `E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\scripts\` | **会话 10 新增 10 个文件**：`_README.txt` + `census-baseline` / `h1-external-write` / `h2-suppression` / `h2-coverage` / `h3-route-b` / `rel-none` / `rel-reapply` / `rel-reapply-resetweights` / `rel-after-release`（最后一组是刻意设计的反例，用于证明装置能检出失败）。 |
| `src/CommandLab.BannerlordProbe/`（**会话 10 修改 2 个文件**） | `ProbeRuntime.cs`：修 `HoldMeter` 从未 `Open()` 的缺陷（hold 恒 0）→ 多窗口累计 + `_holdHistory` + census（`_census`）+ 影子采样（`SampleShadow`/`SHADOW|`）+ 覆盖率（`CoverageRows`/`TotalUnobserved`）+ `GetPatchedMethods` 取证入口 + `[verdict]` 判据自检 + `NativeOverrideLatency`。`OrderWritePatches.cs`：`Patches.Applied`（P0 门禁）+ 观测型 patch ×4（`SetControlledByAI` / `SetFormOrder` / `SetFiringOrder` / `SetRidingOrder`，**只计数永不拦截**）。**Core / Simulation / Telemetry 本轮零改动。** |
| `E:\SteamLibrary\...\Modules\CommandLabProbe\` | **会话 10 已部署**（非仓库内文件）：3 个 DLL + SubModule.xml + `scripts/`。`%USERPROFILE%\Documents\CommandLabProbe\script.txt` 已播种为 `census-baseline`。 |
| `src/CommandLab.Core/` | `Primitives.cs`（**ILabClock** / VirtualClock / CommandDomain / CommandValue + Obs 观察槽 / ControlledEntity）、`Authority.cs`（AuthorityLevel 六档 + Lease + 单调 Token + Generation + AuthorityService）、`Planning.cs`（Intent / CommandTask / PreemptionPolicy / TaskRegistry）、`WritePort.cs`（**单一写入口**，Reject+归属+Fencing 全在其内部；`SubmitImmediate` / `SetPolicy` 为会话 9 新增）、`Compat/Polyfills.cs`（netstandard2.0 编译期标记） |
| `src/CommandLab.Telemetry/` | `TelemetryCollector.cs`（ILabObserver 实现 + 规范化行 + `replay_hash` + DecisionTrace 渲染）、`Metrics.cs`（14 项指标 + 稳定性三级分级） |
| `src/CommandLab.Simulation/` | `Scenario.cs`（ScenarioDefinition / Runner / Context / **CommandDelayQueue** / RecoveryProbe）、`Sources.cs`（SeededRandom splitmix64 / LabSource）、`NativeSource.cs`（**gated + non-gated 双路径**） |
| `tests/CommandLab.Tests/Program.cs` | D0 门禁 + 三处开关接线自检；控制台跑，失败 exit 1 |
| `tests/artifacts/d0-gate.txt` | **D0 证据**（每次运行覆盖写） |

无其他文件。CombatBrain 主仓（`E:\MBmod\CombatBrain 2.0`）**未做任何改动**。

> ⚠️ 项目根目录有 3 个 **0 字节**垃圾文件（文件名分别以 `Phase`、`取证对象：E:MBmodIDEARBM`、`状态：**已完成（只读取证` 开头），是上一会话 shell 重定向误触产物，**尚未删除**，待用户确认后清理。

**会话 2 顺带修复的外部工具**（非本项目文件，但后续 Phase 依赖）：`E:\MBmod\CAI\.tools\ilscan\ilscan.cs`（3 处缺陷，原文件备份为同目录 `ilscan.cs.orig`，已重新构建）。详见 §10.5。

---

## 4. 关键决策与理由

| # | 决策 | 理由 |
|---|---|---|
| D1 | 项目独立于 CombatBrain，不引用 `CombatBrain.*` / `TaleWorlds.*`（Core 层） | 防止 CombatBrain 现有结构预设实验答案；保证结论是"验证出来的"而非"兼容出来的" |
| D2 | 基础实验只用中性抽象对象（Source A/B、MOVE_A/HOLD/ADVANCE/RETREAT） | 目的是测**控制权架构**，不是测某个战术对不对 |
| D3 | Telemetry 是核心交付物而非辅助 | 禁止"看着游戏画面猜 Writer"。必须有 `谁改了什么、原方法是否执行、Order 是否变化` 的可查记录 |
| D4 | Harmony 定位为**控制权集成工具**，不承载战术逻辑 | Prefix 里塞 `if cavalry && distance<80 → flank` 是红线 |
| D5 | 不预设 Proposal / Arbiter / Executor 是正确答案 | 允许实验推翻它们，包括"Proposal 没必要"这个结论 |
| D6 | 目录放在 `E:\MBmod\TacticalCommandLab\`，与 `CombatBrain 2.0` 平级而非嵌套 | 物理隔离强化 D1 |

---

## 5. 未完成事项与下一步

### 👉 下一步（新会话第一个动作）

> ✅ **2026-10-04 01:33 已完成**：第 1 组 `census-baseline` **门禁四项全 PASS**（`P0 7` / `P0b 1957` / `P0c 29/29` / `X5 0`）。
> 详见 `docs/10` §13，产物 `tests/artifacts/host/valid-20261004-0133-census-baseline/`。
> **下方第 1~3 条为历史记录；直接从第 4 条开始按顺序跑 H1/H2/H2b/H3/REL。**

**跑 H1/H2/H2b/H3/REL（按顺序）**。此前状态（已被上面覆盖）：

1. **装置已就绪**（旧记）：`Modules\CommandLabProbe\bin\Win64_Shipping_Client\CommandLab.BannerlordProbe.dll` 已更新为 MD5 `4ca2d169…`（46592 字节，10:53:18）；`%USERPROFILE%\Documents\CommandLabProbe\script.txt` 已播种为**修正版** `census-baseline`（零干预纯观测）；旧产物已归档到 `tests/artifacts/host/invalid-20260922-1046-p0fail/`。依赖 `Modules\Bannerlord.Harmony` v2.4.2.248，宿主 v1.3.4。

1. **装置已就绪**：`Modules\CommandLabProbe\bin\Win64_Shipping_Client\CommandLab.BannerlordProbe.dll` 已更新为 MD5 `4ca2d169…`（46592 字节，10:53:18）；`%USERPROFILE%\Documents\CommandLabProbe\script.txt` 已播种为**修正版** `census-baseline`（零干预纯观测）；旧产物已归档到 `tests/artifacts/host/invalid-20260922-1046-p0fail/`。依赖 `Modules\Bannerlord.Harmony` v2.4.2.248，宿主 v1.3.4。
2. **跑第 1 组**：启动游戏 → 自定义战斗 → 挂机 → 取 `metrics-*-census-baseline.txt`。
3. **只看三处即可判定装置是否康复**：
   - `[verdict]` 的 **P0** → 须 PASS（`actual=7`）
   - `[patches]` 的 `failure = (none)`、`count = 7`（若不是 7，看 failure 里的异常链直接定位）
   - `[census]` 是否列出 6 个写入口的调用计数；`[coverage]` 是否有行
4. **P0/P0b 双 PASS 后**才按顺序跑：`h1-external-write` → `h2-suppression` → `h2-coverage` → `h3-route-b` → `rel-none` / `rel-reapply` / `rel-reapply-resetweights` / `rel-after-release`。
   切脚本（**必须在启动游戏之前**）：`copy "E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\scripts\<名>.txt" "%USERPROFILE%\Documents\CommandLabProbe\script.txt"`
5. 每组只需读 `metrics-*-<tag>.txt` 的 **`[verdict]` 段**；要归因时看 telemetry 的 `HOST|` 行（`hook`/`behavior`/`weight`）。
6. **改代码后必须先构建 + 手工部署**（`docs/10` §7.1A）——`CopyProbeToDeploy` 只拷到 `deploy\`，**不会**拷进 `Modules\`：
   `copy /Y "E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\bin\Win64_Shipping_Client\CommandLab.BannerlordProbe.dll" "<Bannerlord>\Modules\CommandLabProbe\bin\Win64_Shipping_Client\"`
7. 产物拷到 `tests\artifacts\host\`；然后用实测回填 `docs/10` §4 的 LEVEL C 预测。
8. **G1 单独成立不算"取得控制权"**（E12 ⑧）——必须与 G2 hold 同表判读。
9. `rel-after-release` **预期 FAIL**（刻意设计的反例），否则"其他组 failures=0"属平凡成立。

> ✅ **2026-10-04 更新**：装置已可用，上述阻塞已解除。当前已部署 DLL 为 54784 字节（SHA256 `3E3D1D13754ED322…`），`census-baseline` 门禁四项全 PASS。
> **下一步：按第 4 条顺序跑 `h1-external-write` → `h2-suppression` → `h2-coverage` → `h3-route-b` → `rel-*`。**
> 从本次 `[projection]`（29 行）挑实验实体：**多 Team 必须用 `T{t}F{i}` 全名**（如 `T1F0`），不要用 `N0`（别名在多 Team 下可能歧义）。
> 历史记录（已过时）：~~真根因已确认并修复，新版待部署（游戏进程 `Bannerlord.exe` PID 46556 / 3.9 GB 占用 DLL，请先退出游戏）~~
> 先看两个新哨兵：`boot-marker.txt`（SubModule 已加载）与 **`patch-marker.txt`**（patch 实际结果）。
> **`patch-marker.txt` 里的 `missionCurrentNull` 必须为 `no`** —— 若为 `YES`，说明 patch 时 `Mission.Current` 仍为 null，
> `MovementOrder` 静态构造还会失败，延迟策略无效。
> 再看 `[verdict]` 的 **P0**（须 PASS，`actual=7`）、`[patches]` 的 `failure = (none)` / `count = 7`、
> `[census]` 是否有 6 个写入口计数。
>
> 📌 **真根因（`docs/10` §12.9，有完整堆栈）**：`Harmony.PatchAll()` 处理
> `[HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]` 时需解析参数类型 `MovementOrder`，
> **该过程触发了 `MovementOrder` 的静态构造函数**；而 `MovementOrder..ctor` 的 IL（offset 101–123）为
> `Call Mission::get_Current` → `Callvirt Mission::get_CurrentTime` → `Newobj Timer::.ctor` ⇒
> **`OnSubModuleLoad` 阶段 `Mission.Current` 为 null ⇒ NRE** ⇒ **该类型被 CLR 永久标记为不可初始化** ⇒
> 进战斗时 `Team.Initialize → Formation.ResetAux` 一接触就抛 `TypeInitializationException` ⇒ 必崩。
> **异常被 Harmony 内部吞掉**，所以表现为 FirstChance、`patchFailure = (none)`，且我们的兜底全都抓不到。
> **修复 = 把 patch 推迟到第一次 Mission tick**（`ProbeMissionBehavior.OnMissionTick` 首次执行时才 `ApplyPatchesNow()`），
> 此时 `Mission.Current` 必非 null。
>
> ⚠️ **§12.8 的结论已撤回**：`Formation.GetHashCode()` 的 NRE 风险**真实存在**（`Team` 为 null 时必抛，
> 且 `Dictionary<Formation,_>` 的查找会触发它），**加固保留**（已改用引用相等比较器）；
> 但**它不是本次崩溃的原因** —— §12.8 的因果链判断错误。
>
> ⚠️ **仍未验证的剩余风险**：Phase 2 已取证原生 `doAsyncAITick → TickOccasionally` 跑 **worker 线程**。
> 若宿主从 worker 线程调用写入口，`_bindings` / `_hostLines` / `_census` 会被主线程与 worker 线程**并发访问**
> （本项目集合都不是线程安全的）。本轮**未加锁**；若延迟 patch 后仍崩，下一轮第一件事是加**线程 ID 观测**。
>
> ⚠️ **版本号口径待更正**：VLog 报告实际为 `Game Version: v1.4.8.119303`，而文档多处记"宿主 v1.3.4"。
> 不影响已有取证结论（sigscan / ilscan 都是对实际 DLL 做的），但引用时应以实测为准。
>
> 🚫 **环境前提（硬要求）**：本实验测的是 **Vanilla 原生写入口流量基线**，**必须关闭所有修改 Formation AI 的 mod**。字符串级取证显示 **CombatBrain.dll 引用了本实验观测的全部 4 个写入口**（`SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` / `SetControlledByAI`）且引用 `Harmony` —— 若它同样 patch 这些方法，两个 `Prefix` 叠加在同一方法上，取决于 Harmony 执行顺序，**写入可能被它提前拦截、根本到不了我们的 `Gate()`** ⇒ `[census]` 计数失真。它还是本项目 Phase 12 的集成对象，同进程运行等于"实验者与被测者同处一身"。同类须关的还有 `CAI` / `RBM` / `FrontlineMod` / `ImmersiveBattlefields` / `ChaosBattle` / `PolicyExpansion` / `CommandWindow`。**推荐最小 load order**：`Native + SandBoxCore + Sandbox + StoryMode + CustomBattle + Bannerlord.Harmony + CommandLabProbe`（正好是 `SubModule.xml` 声明的依赖集）。详见 `docs/10` §7.0。
> ⚠️ **注意区分两件事**：首跑的 P0 FAIL **不是** CombatBrain 造成的 —— 根因是 Harmony 形参名笔误（编译期事实）。**但关掉 CombatBrain 是独立且必要的**，否则即使 P0 PASS，census 的**数值**也不能当作 Vanilla 基线。
> ⚠️ **Harmony `Prefix` 形参名必须与宿主元数据逐字一致**（`docs/10` §11.2）。`__instance` 之外的名字按**参数名**匹配，对不上 ⇒ `PatchAll()` 整体中止。核对工具：`E:\MBmod\CAI\.tools\sigscan`（`sigscan <dll> <typeName> [methodPrefix]`，输出含参数名）。**改任何 patch 后都应跑一次核对。**
> ⚠️ **`[patches]` 为空 ≠ 一个都没打上**（`docs/10` §11.3 缺陷 2）：Harmony 中止前的成功项不回滚，所以必须读 `[patches]` 的 `count =` 与 `failure =`，而不是看列表是否为空。
> ⚠️ 被测实体 `N0`（非玩家队 index 0）依赖 `Team.HasTeamAi`，**该前提已于会话 11 完成 IL 取证（LEVEL B）**：自定义战斗 `MissionTeamAITypeEnum` = 单机(1)，`EarlyStart` 遍历 `Mission.Current.Teams` 给每支队伍 `AddTeamAI` ⇒ 双方 AI 均 tick。见 `docs/10` §10。
> ⚠️ **但取证 ≠ 有流量**：TeamAI 在场只说明 `FormationAI::Tick` 会被调用（含 0.5s 周期与迟滞），不说明它一定写 MovementOrder。**P0/P0b 仍以 `[census]` 实测为唯一裁定依据。**
> ⚠️ **观测对象由 `BindAll()` 保证**（`docs/10` §7.8）：每 tick 遍历 `Mission.Teams → FormationsIncludingEmpty → Bind`。会话 12 那个"脚本 `cmd` 触碰建立绑定"的修法**已被证实无效并撤销**——`TryCommand` 先用 `FindBinding`、找不到即静默 return，而绑定当时只在 patch 触发时创建 ⇒ 鸡生蛋死锁。
> ⚠️ **兵力/兵种：无硬性要求**（`docs/10` §7.2）。所有指标都是按 tick 累加的比例量或计数，不含绝对兵力项；兵力唯一作用是别让战斗提前结束（需 ≥35s）。推荐纯步兵（骑兵/弓兵会污染 `behavior=` 归因）。建议：双方各 3–4 个步兵编队、每队 ≥20 人、双方各 100–150 人、总兵力 ≤800。
> ⚠️ **一脚本一重启游戏，不是连续跑八把**（`docs/10` §7.6）。`ScriptRunner` 只在 `OnSubModuleLoad` 构造一次 ⇒ 整个进程内只有一份脚本。也**不能拼成一个大脚本**：①`t` 不重置；②`ProbeRuntime` 是单例，`_bindings`/`_leases`/`_census`/`_sampledChanges` 跨战斗存活。重放比对同样必须重启。**合计 9 次启动（+建议重放 2 次 = 11 次）。**

### Phase 8 结论速查（Phase 9/10/11 直接引用）

```text
裁决：不是三选一，是分层组合 —— 默认走 C，需要确定性控制时按域升级到 B，A 判负。

硬门限：G1 unauthorized_writes == 0（E7 7d）  +  G2 intent_hold_share >= 95%（E12 新增）
        A 两条都不满足 / B 两条都过（覆盖率条件）/ C 过不了 G2（非独占语义的结构上限）

授权等级：一律 Level 5（Full）+ Lease + Token；禁止停在 Level 4 或 Level 2
        L2 = 伪稳定（hold 0%）｜L4 = 真实抖动（hold 50%、osc 1.3/s）｜L5 = 稳定 + 落地（hold 100%）

关键风险：B 的上限由"写入口覆盖率"决定，不由 Level 决定（E12 ⑨：gated 0 vs non-gated 20）
        → Phase 10 H2 必须实测 non-gated 路径能否被 patch 覆盖
```

### Phase 6 工程速查（给 Phase 7/8 用）

```text
构建/运行（PowerShell 5.1，注意无 &&）:
  cd E:\MBmod\TacticalCommandLab
  dotnet build CommandLab.slnx
  dotnet run --project tests\CommandLab.Tests\CommandLab.Tests.csproj

关键类型位置:
  VirtualClock            src/CommandLab.Core/Primitives.cs
  ControlledEntity        src/CommandLab.Core/Primitives.cs   (状态 setter = internal，写入口唯一)
  AuthorityLevel/Lease    src/CommandLab.Core/Authority.cs
  AuthorityService        src/CommandLab.Core/Authority.cs    (Acquire/Renew/Release/SweepExpired)
  Intent/CommandTask      src/CommandLab.Core/Planning.cs     (含 PreemptionPolicy Resume/Restart/Cancel)
  WritePort               src/CommandLab.Core/WritePort.cs    (Reject + 归属 + Fencing + 仲裁 + 迟滞 + 最短驻留)
  Telemetry/replay_hash   src/CommandLab.Telemetry/TelemetryCollector.cs
  Metrics/稳定性分级      src/CommandLab.Telemetry/Metrics.cs
  Scenario/延迟队列       src/CommandLab.Simulation/Scenario.cs
  LabSource/RNG           src/CommandLab.Simulation/Sources.cs
  NativeSource 双路径     src/CommandLab.Simulation/NativeSource.cs
```


### 后续 Phase 要点

- **Phase 2**：✅ **已完成**（会话 2，产出 `docs/02-bannerlord-control-pipeline.md`）。**当时要回答的问题**：Tactic（如何影响 Formation / 是否主要靠 Behavior Weight / 多久更新 / 切换时发生什么）、FormationAI（如何选 Behavior / Weight 如何参与 / 当前 Behavior 如何保存切换）、Behavior（`CalculateCurrentOrder()` 何时执行 / 是否直接 SetOrder / Movement·Facing·Arrangement 是否分别维护 / 非当前 Behavior 是否仍产单）、Order（`SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` 及其他写入口）。**必须回答：Formation 的最终合法 Writer 是谁。** 手段：官方资料、DLL 反编译、调试、Runtime telemetry、最小实验 Mod。
- **Phase 3**：✅ **已完成**（会话 3，产出 `docs/03-rbm-case-study.md`）。**当时的问题**：RBM 工程调查——用了哪些 Harmony Patch、Patch Tactic 还是 Behavior、是否改 Weight、是否替换 `CalculateCurrentOrder()`、是否直接 SetOrder、是否 Prefix `return false`、是否仍用 FormationAI 仲裁、是否建独立 Commander、是否减少了实际 Writer 数量。**待验证假设**：RBM 不抢指令不是因为 Arbiter 更强，而是因为更深集成进原生链、结构上减少了独立 Writer。不得当结论用。
- **Phase 4**：✅ **已完成**（会话 4，产出 `docs/04-command-authority-survey.md`）。调研覆盖 Behavior Arbitration / Utility AI / BT / GOAP / HTN / Blackboard / Actor Model / Resource Ownership / Lease / Preemption / Command Bus / Event Sourcing / RTS Squad AI。**四条矩阵性结论**：①Ownership 而非 Arbitration 决定控制权 ②Lease + Fencing 必须配套且强制点在写入口 ③通用文献**不存在 Peer 仲裁**（peer 结构必配集中控制器）④第 5 类 Native Pipeline **无文献对应机制** → Phase 8 不能预设赢家。异议：「Content Transformation」建议降格为正交机制而非第六类冲突（待 E10）。
- **Phase 5**：✅ **已完成**（会话 5，产出 `docs/05-experiment-plan.md`）。详见上方「下一步」。
- **Phase 6**：✅ **已完成**（会话 6，产出 `src/` 三库 + `tests/` 门禁，见 §2 Phase 6 摘要与 §5 工程速查）。**严格遵守的两条硬约束**：①Reject/Token/Generation 全部在 `WritePort` 内部，并用 `internal` 状态 setter 在**结构上**禁止旁路；②Authority 按 Level 六档声明，无布尔 HasAuthority。Ns 的 gated / non-gated 双路径独立实现（`EnforceAuthorityOnNonGated` 默认 false = 复现 `02` 门控矩阵）。额外补了三处**判别力自检**（fencing 开关 0 vs 9、在途代次 0 vs 8、双路径均非零），确认 E3/E4/E6 的对照开关不是死代码。`Proposal` / `Executor` 未实现。**D0 门禁 15/15 通过**，`replay_hash` 证据在 `tests/artifacts/d0-gate.txt`。
- **Phase 7**：✅ **已完成**（会话 7，产出 `docs/06-experiment-results.md`，**判据 51/51**）。E1–E11 全部跑完：①Last Writer Wins（baseline）②Priority Arbitration ③Ownership ④Lease ⑤Preemption ⑥Generation/Fencing ⑦Oscillation 五手段 ⑧Domain Ownership 三级递进 + E9/E10/E11。**第一步是校准不是采集**，并修复了 `average_command_lifetime` 的右删失缺陷（详见上方 Phase 7 摘要与 §12 日志）。**关键裁决：仅 Ownership+Lease 能把 `unauthorized_writes` 压到 0**（E7 7d）；迟滞/最短驻留/状态去抖只能压表观、且彼此不可区分；高频重写三项指标全升并被 7f-stall 证伪。E8 只给条件式预测。零 `Thread.Sleep`，全部虚拟时间驱动，`replay_hash` 校验。
- **Phase 8**：✅ **已完成**（会话 8，产出 `docs/07a-architecture-abc-comparison.md`，**E12 判据 10/10**；E1–E11 保持 51/51、D0 15/15、`replay_hash` 未变）。先按 `06` §6 遗留 8 补跑 **E12（Level 1/2/3/4/5 五档对照）**，用"竞争者优先级标定为 3"使谱系可观测。**H-I 成立且强于 `04` 原表述**：osc L2=0 < L4=1.3 > L5=0.033，但 L2 的"稳定"是 **hold=0% 的伪稳定**（与 L5 在旧指标上完全同形），L4 是 hold=50% 的真实抖动，只有 L5 同时满足"稳定+落地" → 修正为"**L2 只在不需要控制权时成立**"。由此新增第 15 项指标 `intent_hold_share` 与**硬门限 G2**，并得出方法论结论：`unauthorized_writes==0` 且 Stable 是**必要非充分条件**。E12 ⑨ 证明 **Level 上限由写入口覆盖率决定**（Level 5 下 gated 0 vs non-gated 20），E12 ⑩ 证明竞争者优先级为 0 时 L2 与 L4 不可区分（判别力边界）。A/B/C 按 **13 维度**（`HANDOFF` 原写"12 项"实为 13，已登记）逐一挂实验编号：**A 判负**（E7 7f 三项全升 + E9 9b 劣化一个数量级）、**B 唯一过门限但带覆盖率条件**、**C 过不了 G2 但耦合最低**。结论形态为**分层组合**而非三选一，并给出 4 条给 Phase 11 的架构约束。唯一改动 src 文件是 `NativeSource.cs` 新增 `Priority`（默认 0，无回归）。
- **Phase 9**：✅ **已完成**（会话 9，产出 `src/CommandLab.BannerlordProbe/` + `deploy/CommandLabProbe/` + `docs/09-bannerlord-probe.md`）。不是 CombatBrain，不实现真战术，**只支持 HOLD/ADVANCE/RETREAT**（Movement 域；Facing/Arrangement 只观测不下达）。Acquire/Release 全流程可用，`intent_hold_share` 已按**宿主真值**采样。为让宿主复用同一套写入口与指标，**Core/Telemetry 追加 `netstandard2.0` 目标**（`ILabClock` / `SubmitImmediate` / `SetPolicy` / `Obs0..15` 均为纯追加），回归实证 `replay_hash` 与 51/51、15/15、10/10 **全部不变**。构建 0 error/0 warning。**尚未进游戏运行，无任何实测结论。**
- **Phase 10**：H1 External Write（Lab 写 HOLD 不阻止 Native，观察 Native 是否覆盖、多久、哪个 Behavior、是否周期性重写）／H2 Native Suppression（Lab 持 Movement Authority，Harmony 阻止冲突写，验证稳定性与是否需 Patch 多点）／H3 Native Integration（改 Weight / 自定义 Behavior / 替换 Behavior / 替换 `CalculateCurrentOrder()`）。然后做 **Acquire/Release 全流程测试**：`Native owns → Lab Acquire → 原生冲突写停止 → Lab 控制 → Release → Native 恢复`，重点验证：能否正常恢复、是否需 ResetBehaviorWeights、是否需恢复 CurrentBehavior、有无残留状态、有无 Order 抖动、Release 是否安全、是否需要 cooldown。
- **Phase 11**：定稿最终框架。
- **Phase 12**：最后才读 CombatBrain，产出 `combatbrain-case-analysis.md`（历史失败案例 / 未来需求 / 集成目标三角色），再产出 `08-combatbrain-integration-report.md`。集成第一批测试只用 HOLD_POSITION / ADVANCE / RETREAT。

---

## 6. 最终必须能回答的 7 个问题

1. Bannerlord 中**谁真正控制 Formation**？
2. 外部 Mod **怎样安全取得 Formation 控制权**？
3. 控制权**怎样释放并交还 Native AI**？
4. Movement / Facing / Arrangement **是否应有独立 Authority**？
5. 多决策源应通过 **Arbitration、Ownership、Hierarchy 还是其他**协作？
6. CombatBrain 应建**独立 Commander 完整接管**，还是**集成 Native Pipeline**？
7. CombatBrain 的"抢指令问题"里，**多少属于 Command Arbitration，多少其实来自错误的 Authority Architecture**？

---

## 7. 红线清单（违反即返工）

| 禁止 | 说明 |
|---|---|
| 一开始改 CombatBrain 主仓 | Phase 12 之前不碰 |
| 直接复制 CombatBrain 的 CPS / Proposal / Arbiter / Executor | 允许实验"重新发现"它们，不允许抄 |
| 用 Protect / Cavalry / Morale / Brace / Skirmish 等真实战术做基础实验 | 用中性抽象对象 |
| 每 Tick 重复 SetOrder 压过 Vanilla | 这不是 Authority，是竞速 |
| 靠"谁写得更快"实现 Authority | 同上 |
| 无限叠加 Priority | 冲突一个加一个 if 是失败路径 |
| 用 Harmony Patch 承载整个战术 AI | Harmony 只做控制权集成 |
| 永久关闭所有 Vanilla AI 然后宣称 Authority 已解决 | 必须能 Release 并验证恢复 |
| 因为 RBM 用了某方案就当最佳实践 | RBM 只是案例，假设待验证 |
| 没有 Telemetry 就根据画面推测 Writer | 禁止 |
| 为兼容 CombatBrain 旧结构牺牲实验架构 | 反过来：让 CombatBrain 适配验证过的架构 |
| 仅写"感觉更稳"作为结论 | 必须有指标 |

---

## 8. 必测指标（Phase 7/10 采集）

```text
commands_per_second          order_changes_per_second    unauthorized_writes
native_overrides             lab_overrides               authority_transfers
preemptions                  average_command_lifetime    behavior_switch_frequency
oscillation_frequency        reaction_latency            stale_command_execution
task_starvation              release_recovery_failures
```

**Telemetry 必录字段**：Timestamp / Formation / Current Tactic / Current Behavior / Behavior Weight / MovementOrder / FacingOrder / ArrangementOrder / Authority Owner / Command Source / Harmony Hook / Original Method Executed? / Order Changed? / Previous Order / New Order。

**DecisionTrace 示例格式**（可调整，但必须可追踪、可重放、可解释）：

```text
Entity: Formation_0        Domain: Movement        Current Owner: LabController
Current Task: HOLD
Native State: BehaviorAdvance    Native Request: ADVANCE    Lab Request: HOLD
Decision: Reject Native write
Reason: Movement Authority currently owned by LabController
Authority Acquired: 32.10s    Lease Until: 36.10s
```

---

## 9. 目录结构（建议）

```text
E:\MBmod\TacticalCommandLab\
├─ HANDOFF.md                      ← 本文件，每次续跑都要更新
├─ docs/
│  ├─ 01-problem-definition.md
│  ├─ 02-bannerlord-control-pipeline.md
│  ├─ 03-rbm-case-study.md
│  ├─ 04-command-authority-survey.md
│  ├─ 05-experiment-plan.md
│  ├─ 06-experiment-results.md
│  ├─ 07a-architecture-abc-comparison.md    ← Phase 8 产出
│  ├─ 07-final-architecture.md
│  └─ 08-combatbrain-integration-report.md
├─ src/
│  ├─ CommandLab.Core/
│  ├─ CommandLab.Simulation/
│  ├─ CommandLab.Telemetry/
│  └─ CommandLab.BannerlordProbe/     ← net472，唯一允许 TaleWorlds.*
├─ deploy/
│  └─ CommandLabProbe/                ← 拷进游戏 Modules/ 即用（SubModule.xml + bin + script.sample.txt）
└─ tests/
   └─ CommandLab.Tests/
```

`src/CommandLab.Core`、`Simulation`、`Telemetry` **不得引用** `TaleWorlds.*` 与 `CombatBrain.*`。只有 `BannerlordProbe` 允许引用 `TaleWorlds.*`。

---

## 10. 环境约定与已知坑

### 10.1 用户终端（给命令时必须遵守）
- **Windows PowerShell 5.1**，不支持 `&&` / `||` / 三元 `?:`；用 `;` 或分行。
- **PATH 里没有 `python` 也没有 `py`** —— 一律写完整绝对路径：
  - 系统 Python：`C:\Python314\python.exe`
  - 内置 Python：`C:\Users\Administrator\.workbuddy\binaries\python\versions\3.13.12\python.exe`
- Windows 路径用反斜杠 `E:\...`。

### 10.2 Agent 侧工具通道
- **Bash 工具可用**（已修 coreutils 补丁）；PowerShell 工具的 stdout 会 100% 丢失只剩 exit code → **取输出优先 Bash，PowerShell 只用来拿 exit code**。
- Bash 里命令文本含 `powershell` 字样会被安全策略直接拒绝（连 `grep -i powershell` 都拦）。
- PowerShell `>` 重定向是 UTF-16，会被判为二进制 → 让 Python 自己用 `open(..., 'w', encoding='utf-8')` 写文件。
- 改文件前必须 `close_file`，否则 `os.replace` 报 WinError 5。

### 10.3 额度管理（重要）
- **Hy4 免费额度周期为当日 08:00 → 次日 08:00**，实测上限区间 2,000 万 – 4,900 万，动态浮动。
- **额度按模型隔离**：白天可用 Hy3 替代 Hy4，不占 Hy4 额度。
- 本项目应按 Phase 切会话，**每会话只做一个 Phase**，做完回写 HANDOFF.md。
- 新会话起步价中位约 3.7 万 token，单任务典型涨 4 倍；单会话累计调用 60–80 次就该换会话。

### 10.4 上下文预警
- 已部署 hook：`E:\AI\hooks\context-lifecycle-guard.mjs`，A 层上下文 WARN 100K / CRITICAL 180K / DANGER 320K，B 层额度 WARN 1500万 / CRITICAL 2500万 / BRAKE 3500万（**只告警不拦截**）。
- 当前周期用量可直接读 `~/.workbuddy/context-guard/quota-cache.json` 的 `total` 字段。

### 10.5 本机已有可复用取证资产（已验证在磁盘，Phase 2/3 直接用，别再重新找）
- **Bannerlord 程序集**：`E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.dll`
  → 查符号名最快手段：`grep -a -o '<符号>' <dll>`
- **RBM 反编译源码**：`E:\MBmod\FIX\rbm_decompiled\`（Phase 3 主力素材）
- **其他 Mod 反编译源码（真实 API 用法最强证据）**：`E:\MBmod\IDEA\_mod_research\`（RTSCamera / TAOM / ImmersiveBattlefields / BetterPikes / FrontlineMod…）、`E:\MBmod\IDEA\RBM\`
- **旧 CombatBrain 1.x 源码**：`E:\MBmod\overseer\_work\combatbrain-d-refactor\CombatBrain\`（54 个 .cs）—— Phase 12 历史失败案例的一手材料
- **IL 扫描工具**：`E:\MBmod\CAI\.tools\ilscan`
  - 用法：`dotnet <bin\Debug\net10.0\ilscan.dll> dump <dll> [typeFilter] [methodFilter]` / `ref <dll> <methodName>`
  - ⚠️ **2026-09-19 会话 2 修复了 3 处缺陷**（原文件备份 `ilscan.cs.orig`，已重建）：
    ① `OperandSize` 短格式指令 `_S` 大小写不匹配 → 解码错位产出伪指令；
    ② `Switch` 操作数 `n` 未校验 → int 溢出 → 扫描崩溃（`Formation` 类型必现）；
    ③ `Ldc_R4` 浮点被当 token → 常量不可读（已补 `ToSingle` 解码）。
    **修复前产出的任何 IL 结论不可信**，历史引用需复核。
  - 已知限制：不输出枚举字段值（故 `MovementOrderEnum` 语义读不到）；`Ldc_R8` 仍以 hex 显示。
- **方法签名扫描工具（2026-09-22 会话 13 新建）**：`E:\MBmod\CAI\.tools\sigscan`
  - 用法：`dotnet <bin\Debug\net10.0\sigscan.dll> <assembly> <typeName> [methodPrefix]`
  - 输出**方法签名含参数名**（Mono.Cecil 读元数据），例如：
    `public Void SetMovementOrder(MovementOrder input)` / `public Void SetControlledByAI(Boolean isControlledByAI, Boolean enforceNotSplittableByAI)`
  - **与 `ilscan` 的分工**：`ilscan` 出 IL 指令流（看"怎么算"），`sigscan` 出元数据签名（看"叫什么"）。
  - **必须用它核对的场景**：写 Harmony `Prefix`/`Postfix` 时。Harmony 除 `__instance`/`__0`/`___field` 外按**参数名**匹配宿主形参，名字不符会抛异常并使 `PatchAll()` **整体中止** —— 2026-09-22 因此让 7 个 patch 生效 0 个（`docs/10` §11.2）。
  - 依赖：`Mono.Cecil.dll` 取自 `Modules\Bannerlord.Harmony\bin\Win64_Shipping_Client\`。
- **离线回归工程参考**：`E:\MBmod\CAI\tests\CAIRegress`

### 10.6 与既有 Charter 的边界（避免重复造轮子）
- `E:\MBmod\CombatBrain 2.0\charter\` 已有功能拆解草案，其中 **CB-002「指挥执行网关」** 与本项目 Phase 11/12 高度重叠。
- 已验证结论（IL 三重实证，可直接作为 Phase 2 输入，但**本项目仍应独立复核**）：
  - AI 队无 OrderController（ctor 绑定 Agent owner；Team 只有 `_orderControllers[0/1]` = Master/Player）
  - 因此发令只有两条路：**Route A** = `FormationAI.AddAiBehavior` + `SetBehaviorWeight<T>` + `GetAiWeight`（**仅 Movement 域**）；**Route B** = `SetControlledByAI(false, enforceNotSplittableByAI)` + `Set*Order`
- 既有 Charter 遗留的阻断级 PoC：`SetControlledByAI(false)` 后 Facing / Arrangement 是否仍被原生残留覆盖 → 这正是本项目 Question 4（三域是否应有独立 Authority）的核心，**必须在 Phase 10 用 Telemetry 实测，不得沿用旧推论**。

### 10.7 CombatBrain 相关背景（Phase 12 才用得上）
- CombatBrain 是纯逻辑 + OfflineTests + build/deploy gate 工作流，HEAD 基线 `e318fd7`、447 PASS。
- 现有结构：Proposal / Arbiter / Executor，CPS（Command Per Second 限流）。
- X5 任务线：Formation Slot / 动态阵型控制（TroopClassifier / SlotLayout / TerrainWarp / SlotAssignment），子布局 ShieldWall / Line / RangedLoose。
- AI 智能度参照基准是 RBM mod。

---

## 11. 交付物清单（验收用）

```text
问题模型
+ Bannerlord 原生控制链研究
+ RBM 工程案例
+ 通用架构调研
+ 纯逻辑 Simulation
+ 自动测试
+ Harmony Runtime Probe
+ Architecture A/B/C 对比
+ 最终 Authority Framework
+ CombatBrain 集成建议
```

最终交付**不是一个 CombatBrain Patch**，而是一个独立、可实验、可测量、可解释的 Tactical Command Authority Framework。CombatBrain 是它的**第一个真实应用案例**，不是它存在的前提。

---

## 12. 工作日志（每次续跑追加）

| 日期 | 会话 | 完成内容 | 产出文件 |
|---|---|---|---|
| 2026-10-04 01:33 | 会话 14 | **Phase 10 装置修复收尾：`census-baseline` 门禁四项全 PASS（首次拿到可用实测基线）。** 承接派单 `_dispatch\2026-10-02-phase10-next-prompt.md`（本轮只修装置、不产架构结论）。**①Step 0**：TCL 首次 `git init` + `.gitignore`（`bin/` `obj/` `deploy/*/bin/`）+ 基线提交 `555a9af`。**②Step 1-3（dev 成员交付，captain 验收）**：修 EntityId 唯一性 —— `EntityIdOf` 由 `P/N + Formation.Index`（各 Team 内都从 0 计 ⇒ 塌缩成 `N0`）改为**全局唯一 `T{teamSeq}F{idx}`**，短名降级为 `Alias`（保留是为了 8 份实验脚本里 23 处 `N0` 不用改）；新增 **P0c 门禁**（`唯一 EntityId 数 == binding 数`，冲突 FAIL 并打印重复清单）；`ScriptRunner.Resolve` 四分支（全名 → 别名唯一 → 歧义 `NOTE\|SCRIPT_ALIAS_AMBIGUOUS <name> candidates=N` 且拒绝执行 → `SCRIPT_ENTITY_UNRESOLVED`）；`docs/10` §5 判据表加 P0c 行、§7.4 第 1 组门禁由「P0 或 P0b」改为「P0/P0b/X5/P0c 任一 FAIL ⇒ 停」。**③实测 1（2026-10-03 23:58）门禁 FAIL**：`P0c 30/41`、`X5 15036`。根因（三面交叉证据：`[projection]` 里 `T-1F-1 alias=N-1` 重复 12 次 / `[coverage]` 唯一 FAIL 行就是它 / telemetry HOST 行 `team=- idx=-1`）：12 个 `Formation` 的 `Team == null` 且 `Index == -1` ⇒ 全算出同一个 `"T-1F-1"`，`SampleShadow` 按该 key 聚合 ⇒ 12 个对象**互相把对方的值变化当成自己的变化**（`Unset ↔ Obs0` 乒乓）⇒ 造出 15036 条假 unobserved。**④修复（t4，dev 交付 + captain 裁决合同冲突）**：`Bind()` 作唯一收口点拒绝「无法唯一标识的 Formation」（`null` / 读 `Team` 为 null 或抛异常 / `Index < 0`）⇒ 返回 null、不写 `_bindings`、不 `RegisterEntity`；新增 `_orphans`（引用去重）+ `NoteOrphan` 每个对象只留一次 `NOTE\|ORPHAN_FORMATION_SKIPPED`；七个调用点全部处理 null；`OrderWritePatches.cs` 三处加 null 判定（**形参名/参数顺序/try-catch 一字未动** —— 不加会在宿主 patch 里 NRE → 被 try/catch 吞掉 → `RecordFatal` 落假崩溃报告淹没真实取证）。**否决"重名加自增后缀"**（派单明令：会让实验对象取决于遍历顺序，违反可重放）。**⑤实测 2（2026-10-04 01:33）门禁全 PASS**：`P0 7` / `P0b 1957` / **`P0c 29/29`** / `X1 0` / `X2 1.3664` / `X3 4.5325` / `X4 0.0000` / **`X5 0`**；6 个 dump 时间序列 P0c 恒 29/29、X5 恒 0；`ORPHAN_FORMATION_SKIPPED` **恰好 12 行**（与预测吻合）、`[projection]` 41→29 行 —— 修复自证。**⑥Step 6**：归档 `tests/artifacts/host/valid-20261004-0133-census-baseline/`（17 文件）、`docs/10` 新增 §13、HANDOFF §2/§12 回写、git 提交。**⑦Step 7**：删掉 TCL 根目录 3 个 0 字节垃圾文件。**关键工程事实（复用价值最高）**：①启动通道是 `bin\Win64_Shipping_Client\ModMasterStarter.bat`（`_MODULES_*…*_MODULES_` 命令行传模块列表，**绕过启动器**）⇒ `LauncherData.xml` 搜不到 `CommandLabProbe` 属正常；②`engine_config.txt` 的 **`safely_exited` 必须为 1**，为 0 时弹 `Safe Mode` 窗口不加载 mod、GABP 端口不 listen（极像"CBATH 坏了"），**且游戏每次退出都写回 0 ⇒ 每次启动前必须重写**；③无人值守进战斗已跑通 —— CBATH（`E:\MBmod\CombatBrain Autonomous test harness`）+ 驱动脚本 `_audit\_tcl-step5.ps1`，前提是给 gabs `config.json` 的 `_MODULES_` 加 `*CommandLabProbe*`；④29 个绑定里仍有 10 个 `T-1F0..T-1F9`（Team 非 null 但 `TeamSeqOf` 定位失败），各自唯一不撞名故不处理，**但挑实验实体时应挑 `T0F*`/`T1F*`，多 Team 必须用 `T{t}F{i}` 全名**。**回归**：Core/Simulation/Telemetry/tests 零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10、`D0-S1 replay_hash = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199` 逐字不变。**未写任何 Phase 10 架构结论**（红线）。 | `tests/artifacts/host/valid-20261004-0133-census-baseline/`（17 文件）、`docs/10-harmony-runtime-experiments.md`（新增 §13 + §5/§7.4）、`src/CommandLab.BannerlordProbe/ProbeRuntime.cs`、`OrderWritePatches.cs`、`ScriptRunner.cs`、`_audit/2026-10-02-phase10-fixtool-qc.md`、`_audit/\_tcl-step5.ps1`、`HANDOFF.md` |
| 2026-09-22 13:25 | 会话 13（续五） | **崩溃真根因确认并修复（零 Core 改动）—— 同时撤回 §12.8 的 GetHashCode 结论。** 第四次实测（13:11:47）仍崩 ⇒ 换手段取证。**证据来源转折点**：本机装有 **CrashDoctor**（崩溃诊断 mod），其 `Modules\CrashDoctor\managed_crashes.jsonl` + `managed_reports\<sig>\report.txt` 记录了**完整托管堆栈** —— 这是此前三个通道（Windows 事件日志 / Bannerlord 日志 / 探针产物）全部失效时唯一有效的证据源。**完整堆栈**：`System.TypeInitializationException: The type initializer for 'TaleWorlds.MountAndBlade.MovementOrder' threw an exception. ---> NullReferenceException at MovementOrder..ctor(MovementOrderEnum) at MovementOrder..cctor() --- at Formation.ResetAux() at Formation.Reset() at Team.Initialize() at Mission+TeamCollection.Add(...) at MissionCombatantsLogic.AddPlayerTeam(...) at MissionCombatantsLogic.OnBehaviorInitialize() at Mission.AfterStart() at MissionState.FinishMissionLoading()`。CrashDoctor 统计同一签名 `b1dfded2` 共崩 **4 次**（11:56:56 / 12:06:03 / 12:20:42 / 13:11:47）。**NRE 的确切来源**：`ilscan dump MovementOrder .ctor` 显示 IL offset 101–123 为 `Call Mission::get_Current` → `Callvirt Mission::get_CurrentTime` → `Newobj Timer::.ctor` → `Stfld _tickTimer` ⇒ **`MovementOrder` 的静态构造（`..cctor` 创建 6 个静态实例）要求 `Mission.Current != null`**，否则构造里读 `Mission.Current.CurrentTime` 抛 NRE。**关键判据**：`crashdoctor.log` 里每次致命崩溃**之前**，启动阶段都有一次被捕获的 `[FirstChance] NullReferenceException`（11:53:29→11:56:56、12:03:08→12:06:03、12:19:32→12:20:42、13:11:37→13:11:47），**而 10:46 那次（形参名笔误、patch 全部失效）启动阶段完全没有这个 NRE**。⇒ **模式成立：`PatchAll()` 成功 ⇒ 启动阶段 `MovementOrder..cctor()` 失败 ⇒ 该类型被 CLR 永久标记为不可初始化 ⇒ 进战斗时 `Formation.ResetAux()` 一接触就抛 `TypeInitializationException` ⇒ 必崩。** **机制**：`Harmony.PatchAll()` 处理 `[HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]` 时需解析参数类型 `MovementOrder`（struct），**该过程触发了它的静态构造函数**；而 `OnSubModuleLoad` 阶段 `Mission.Current` 必然为 null ⇒ NRE ⇒ 异常被 Harmony 内部吞掉（故只表现为 FirstChance，`PatchAll` 看起来"成功"、`patchFailure = (none)`）⇒ 但进程级损害已造成。**这解释了全部此前无法解释的观测**：崩在 Mission 初始化、有 patch 才崩、`patchFailure=(none)` 却崩、我们所有兜底都抓不到（异常不在我们的托管调用栈里）。**修复**：**把 patch 推迟到第一次 Mission tick** —— `OnSubModuleLoad` 不再 patch，改由 `ProbeMissionBehavior.OnMissionTick` 首次执行调 `ApplyPatchesNow()`；此时 Mission 已在运行 ⇒ `Mission.Current != null` ⇒ `..cctor()` 可成功。新增 `patch-marker.txt` 哨兵，含 **`missionCurrentNull` 自证字段**。**登记代价**：Mission `AfterStart` 阶段的少量初始写入落在 patch 生效之前、不被观测（对稳态流量普查无影响）。**撤回 §12.8**：`GetHashCode` 的 NRE 风险**真实存在且已加固**（引用比较器保留），但**它不是本次崩溃原因** —— §12.8 的因果链判断错误，已在 `docs/10` §12.9 明文更正。**教训三条**：①`PatchAll()` 会解析参数类型 ⇒ 可能触发宿主静态构造，**patch 时机是正确性问题而非性能问题**；②`PatchAll()` "成功"≠无害，被吞掉的 FirstChance 异常可能已造成不可逆损害；③**卡住时优先检查机器上已装的崩溃诊断工具是否已有记录**。**状态**：已编译（0 warning / 0 error）、回归不变，**待部署** —— 游戏进程（PID 46556，3.9 GB，正常在运行）占用 DLL。 | `src/CommandLab.BannerlordProbe/ProbeSubModule.cs`（patch 延迟到首次 Mission tick + `ApplyPatchesNow`）、`ProbeRuntime.cs`（新增 `WritePatchMarker`）、`docs/10-harmony-runtime-experiments.md`（新增 §12.9、更正 §12.8）、`HANDOFF.md`（§0/§5/§12） |
| 2026-09-22 13:10 | 会话 13（续四） | **崩溃根因定位并修复（零 Core 改动）。** 用户执行"取消勾选 CommandLabProbe"的分水岭实验 → 12:59:57 进战斗**不崩**。**证据**：`BattleSizeResized` 的 `VLogs` 里 `detectedModules=` 字段如实列出实际加载模块 —— **10:46 那次含 `CombatBrain` 与 `CommandLabProbe`；12:59 那次两者都不在** ⇒ 12:59 **未加载探针** ⇒ **确认崩溃由 `CommandLabProbe` 引起**（受控实验，唯一变量）。**顺带排除两项**：①`CombatBrain` 不是原因（10:46 它开着且不崩）②兵力规模不是原因（10:46 `totals=150/150`、12:59 `totals=408/408`，都不崩）。**根因（有 IL 证据的完整因果链）**：`Formation` **重写了 `GetHashCode()` 且未重写 `Equals`**，其 IL 为 `Ldfld Formation::Team` → `Callvirt Team::get_TeamIndex` → `*10` → `+ FormationIndex` ⇒ **`formation.Team == null` 时抛 `NullReferenceException`**；而我们的 `_bindings` 是 `Dictionary<Formation, FormationBinding>`，**`Bind()` 第一行 `TryGetValue` 与索引器赋值都会调用它** ⇒ Mission 初始化阶段宿主一旦把 `Team == null` 的 Formation 传进写入口，`Prefix` 里的 `runtime.Bind(__instance)` 就 NRE ⇒（旧版本该语句在 `Gate()` 的 try/catch **之外**，见 §12.7②）**穿透宿主调用栈崩游戏，且崩得比任何 dump 时机都早 ⇒ 磁盘无痕**（这解释了 boot marker 正常却无 crash 报告）。**修复**：`_bindings` 改用**引用相等比较器**（`ReferenceEquals` + `RuntimeHelpers.GetHashCode`，net472 无内置故自写 `ByReference`）—— **语义不变**（`Formation` 未重写 `Equals`，默认本就是引用比较），只绕开会抛异常的哈希并省掉一次 `Team.get_TeamIndex` native 调用。**与 §12.7② 的 Prefix 层兜底互补**：比较器消除已知抛点，兜底防未知抛点。**登记未验证风险（不据此改代码）**：Phase 2 已取证原生 `doAsyncAITick → TickOccasionally` 跑 **worker 线程**，若宿主从 worker 线程调用写入口，则 `_bindings`/`_hostLines`/`_census` 会被主线程与 worker 线程**并发访问**；本轮**未加锁也未加线程观测**（一次只改一个变量便于验证），**若仍崩，下一轮第一件事就是加线程 ID 观测**。**登记版本号偏差**：VLog 报告 `Game Version: v1.4.8.119303`，而项目文档一直记"宿主 v1.3.4"；**不影响已有取证**（sigscan/ilscan 都是对实际 DLL 做的），但口径需更正。**同时把该陷阱写进 skill**：`bannerlord-api-readonly-verification` 新增"🚨 `Formation.GetHashCode()` 会抛 NRE，绝不要拿 `Formation` 当 Dictionary 键"条目 + 无痕崩溃的排查链。**状态**：已编译成功（0 warning / 0 error）、回归不变（D0 15/15、E1–E11 51/51、E12 10/10），**但未部署** —— 游戏进程仍占用 DLL，需用户先退出游戏。 | `src/CommandLab.BannerlordProbe/ProbeRuntime.cs`（新增 `ByReference` 比较器 + `_bindings` 改用它）、`docs/10-harmony-runtime-experiments.md`（新增 §12.8）、`C:\Users\Administrator\.workbuddy\skills\bannerlord-api-readonly-verification\SKILL.md`、`HANDOFF.md`（§0/§5/§12） |
| 2026-09-22 12:15 | 会话 13（续三） | **第三次实测仍崩 → 拿到决定性证据 + 发现兜底层级缺陷 + 撤回一个错误推断。** 用户 12:03 用 `4d2e2d4b` 又跑一次，**仍崩**，并质疑"你刚才是不是改了什么东西，本来不崩的"。**用户的质疑成立**：唯一关键变化是**修 Harmonic 形参名让 patch 从 0 个生效变成 7 个生效** ⇒ 这套探针代码**第一次真正参与游戏运行**（10:46 那次"不崩"是因为它根本没运行）。**boot-marker.txt（12:03:08）给出决定性证据**：`patchedCount = 7`、`patchFailure = (none)`、7 个 Formation 写入口全部在列 ⇒ SubModule 加载成功、patch 全生效、`PatchAll` 无异常；**且没有 `crash-*.txt`** —— `RecordCrash` 与 `WriteBootMarker` 同一套写入方式，boot marker 能写成功说明前者也能写 ⇒ **我们的兜底与 AppDomain 处理器确实没捕获到任何托管异常**。**发现一个确定的真实缺陷：兜底放错了层级。** 旧代码把 try/catch 加在 `ProbeRuntime.Gate()`/`Observe()` **内部**，但 **Prefix 方法体里在调用它们之前的表达式**（`runtime.Bind(__instance)` / `Project(...)` / `input.OrderType.ToString()` / `order.OrderEnum.ToString()`）**全在兜底之外**；这些表达式在宿主调用写入口时**同步**执行，一旦抛异常就**穿透到宿主调用栈崩游戏**，而 ProbeRuntime 里的 try/catch 根本来不及覆盖。⇒ **已修：7 个 Prefix 全部在方法体层包 try/catch**（异常 → `RecordFatal` + 拦截型 `return true` / 观测型仅记录）。**这个缺陷本身就说明此前的"防崩"设计不到位，完全可能就是崩溃原因。** **同时撤回一个错误推断（诚实记录）**：曾据 sigscan 的"`OrderType` 为 `Public, Sealed` 且无 `SequentialLayout`"推断它是引用类型、猜测 `input.OrderType.ToString()` 遇 null 抛 NRE 是根因 —— **该推断已被编译器证伪**（对 `OrderType` 用 `?.` 报 `CS0023「运算符"?"无法应用于"OrderType"类型的操作数」⇒ 它是**值类型**，永远非 null），**NRE 假设作废**。方法论教训：`Sealed`+无 `SequentialLayout` **不能**当引用类型的判据，判定类型要么读 `IsValueType` 要么让编译器说话。**还发现一个操作陷阱**：部署新版本时报 `Device or resource busy` —— `tasklist` 显示 **`Bannerlord.exe` PID 36196 仍在运行但只占 56 K 内存**（崩溃后未退出干净的残留进程）⇒ **崩溃后必须先确认进程完全退出再部署再重跑**，否则会出现"部署看似成功但被占用 / 跑的是旧 DLL"的假象。**下一步两条**：①最快确定责任归属 —— 启动器**取消勾选 `CommandLabProbe`** 进一次战斗（不崩⇒与探针相关；仍崩⇒与探针无关，去查 load order）；②拿根因 —— 结束残留进程 → 部署新版 → 重跑（若崩会在**异常源头**捕获并写 `crash-*.txt`）。**新版已编译成功但未部署**（文件占用）；`Modules\` 当前仍是 `4d2e2d4b`。回归不变：D0 15/15、E1–E11 51/51、E12 10/10。 | `src/CommandLab.BannerlordProbe/OrderWritePatches.cs`（7 个 Prefix 全部加兜底 + 撤回 NRE 相关改动）、`docs/10-harmony-runtime-experiments.md`（新增 §12.7）、`HANDOFF.md`（§0/§5/§12） |
| 2026-09-22 12:05 | 会话 13（续二） | **第二次实测进战斗崩溃 + 新增崩溃诊断基建（零 Core 改动）。根因未定位。** 用户修完形参名重跑第 1 组 `census-baseline` → **进战斗时游戏崩溃**（11:57:30，异常码 `0xe0434352` = CLR 未处理异常，出错模块 `KERNELBASE.dll`）。**关键观察：`Documents\CommandLabProbe\` 无任何新产物，连 `mission-start` dump 都没有** —— 而 `OnMissionTick` 的**第一件事**就是 `Dump("mission-start")` ⇒ **`OnMissionTick` 从未执行到第一行** ⇒ 崩溃发生在 **Mission 初始化序列中**，早于任何 tick。**三个证据通道全部失效**：①Windows 事件日志只有 `Application Error`（无堆栈），本机无 `.NET Runtime` 1026 事件、`crashes\` 目录不存在；②Bannerlord 默认日志**不记录普通 SubModule 加载**，故里面完全没有 `CommandLabProbe` 痕迹，无法据此判断是否加载；③探针产物只在 Mission tick 落盘，崩溃早于 tick，`_hostLines` 随进程丢失。⇒ **无法区分"SubModule 没加载（崩溃与探针无关）"与"加载了但崩在 Mission 里"**，也无法知道抛异常位置。**静态分析（不等于证据）**：`Gate()` 首要嫌疑 —— 它在宿主调用写入口时**同步**执行且**当时无 try/catch**，异常会穿透到宿主调用栈（与崩溃形态吻合）；时间线自洽：首跑 patch 未生效、`Gate()` 从未执行所以游戏正常，形参名修好后 `Gate()` 首次真实执行即崩。链路里无保护的索引访问两处（`WritePort.Evaluate` 的 `_entities[req.EntityId]`、`entity[req.Domain]`）。**但按调试协议两轮分析后仍未区分假设，必须换手段，故不据此改代码**；同样**不能排除崩溃与探针无关**（用户同时改动了 load order）。**新增诊断基建**：①**启动哨兵** `boot-marker.txt`（`OnSubModuleLoad` 末尾立即落盘，含 `patchedCount`/`patchFailure`/方法清单）——**确定性回答"SubModule 加载了没有"**，解决通道②失效；②**崩溃报告** `crash-<ts>.txt`（`RecordCrash`，独立文件不走 Dump，含异常链逐层 Message+StackTrace + `_hostLines` 尾部 200 行 + binding 列表，带去重防写爆磁盘）；③**四处异常兜底** `Gate`/`Observe`/`Tick`/`OnMissionTick` 全部改 try/catch 包装（`Gate` 异常时 **`return true` 放行** —— 探针是实验装置，绝不能弄崩用户游戏；但**必须留痕**，否则就成了"用防御性代码掩盖真实缺陷"，反而违反本项目红线）；④**`AppDomain.UnhandledException`** 处理器（崩溃发生在宿主代码里时也留痕，处理器内**直接落盘**而非只 `Note`，因进程随即终止）。**下一步一次启动即可二分**：看有无 `boot-marker.txt` / `crash-*.txt` 即可定性（无 boot marker ⇒ 与探针无关；有 crash 报告 ⇒ 直接拿堆栈）。**独立补充手段**：若仍崩，先在启动器取消勾选 `CommandLabProbe` 再进一次战斗（不崩 ⇒ 与探针相关；仍崩 ⇒ 与探针无关/有共因）。**部署**：新版 MD5 `4d2e2d4b…`（50176 字节）已写入 `Modules\`，双向校验一致；工作目录已清空只留 `script.txt`。**回归**：Core/Simulation/Telemetry/tests 零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变。 | `src/CommandLab.BannerlordProbe/ProbeRuntime.cs`（`WriteBootMarker`/`RecordCrash`/`RecordFatal`/`SafeIndex` + `Gate`/`Observe`/`Tick` 兜底）、`ProbeSubModule.cs`（`AppDomain` 处理器 + boot marker + `OnMissionTick` 兜底）、`docs/10-harmony-runtime-experiments.md`（新增 §12 + §0 状态行 + §11.7 补注）、`tests/artifacts/host/crash-events-20260922.txt`、`HANDOFF.md`（§5/§12） |
| 2026-09-22 11:40 | 会话 13（续） | **环境取证：发现实验环境未受控，新增 §7.0「环境前提」（零代码改动）。** 用户告知首跑时 **CombatBrain 处于开启状态**，并认为这可能是 P0 FAIL 的原因。**判断：该归因不成立，但用户提出的问题本身是重要的独立缺陷。** ①**不成立的理由**：P0 FAIL 的根因是 Harmony Prefix 形参名笔误（`SetMovementOrder` 宿主形参 `input` 写成 `order`、`SetControlledByAI` 的 `isControlledByAI` 写成 `isAIControlled`），而 `PatchAll()` **只扫描自身程序集**内的 patch 类，这两个笔误**在编译期就决定了失败**，与运行时加载哪些 mod 无关；且已部署的修正版已修掉它们。②**但环境问题独立成立且更严重**：对 `Modules\CombatBrain\bin\...\CombatBrain.dll` 做字符串级取证，**4 个被观测写入口全部 FOUND**（`SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` / `SetControlledByAI`）且 `Harmony` FOUND（`PatchAll` 未 FOUND ⇒ 它走逐个 `Patch()` 而非 `PatchAll()`）；同时确认它**不自带 `0Harmony.dll`**（find 只找到自己的 DLL），故不存在 Harmony 版本冲突。⇒ 若 CombatBrain 同样 patch 这些方法，**两个 `Prefix` 叠加在同一方法上**，取决于 Harmony 的 patch 执行顺序，**原生写入可能被它提前拦截、根本到不了我们的 `Gate()`** ⇒ `[census]` 计数失真；且 CombatBrain 是 Phase 12 的**集成对象**，同进程运行=实验者与被测者同处一身。**结论：实验必须关闭所有修改 Formation AI 的 mod。** 新增 `docs/10` §7.0 列出必须关闭清单（CombatBrain / CAI / RBM / FrontlineMod / ImmersiveBattlefields / ChaosBattle / PolicyExpansion / CommandWindow / ClanControl）+ **推荐最小 load order**（`Native + SandBoxCore + Sandbox + StoryMode + CustomBattle + Bannerlord.Harmony + CommandLabProbe`，正好是 `SubModule.xml` 声明的依赖集）+ 降级方案（至少关 CombatBrain，并在结论登记"环境含 N 个战斗类 mod，纯 Vanilla 基线存疑"）。归档目录新增 `README-why-invalid.md`，明确写下"**P0 FAIL 与 CombatBrain 无关**"与"**该轮环境未受控**"两件事，避免下次误读。**红线把关**：该分析属**运行环境风险取证**（读 mod 的 DLL 符号），**不构成对 `CombatBrain.*` 的代码引用** —— 项目源码与文档的类型/命名空间引用红线仍成立，已在 `docs/10` §7.0 显式声明。**零代码改动**；装置保持已修复状态（DLL MD5 `4ca2d169…`）。 | `docs/10-harmony-runtime-experiments.md`（新增 §7.0、§11.1 补环境事实）、`tests/artifacts/host/invalid-20260922-1046-p0fail/README-why-invalid.md`（新建）、`HANDOFF.md`（§5/§12） |
| 2026-09-22 10:55 | 会话 13 | **Phase 10 首次实测执行 + P0 FAIL 根因定位与修复（零 Core 改动）。** 用户跑完第 1 组 `census-baseline` 后交回产物。**实测结果：P0 FAIL**（`harmony patched methods > 0 actual=0`）、`P0b FAIL`、`[patches]`/`[census]`/`[coverage]` 三段全空，telemetry 唯一内容是一行 `NOTE\|t=0.0000\|HARMONY_PATCH_FAILED`。**关键判断：这是装置故障，不是架构事实 —— 门禁按设计拦住了，本轮数据作废**（归档 `tests/artifacts/host/invalid-20260922-1046-p0fail/`，38 文件保留完整现场）。**根因定位（确定性）**：`Patches.Apply` 的 `catch` 只写常量、把异常吞了，无从直接得知原因；于是先做静态排除 —— 用 `ilscan dump` 确认 7 个 patch 目标方法在 v1.3.4 全部存在且无重载（`SetMovementOrder`/`SetFacingOrder`/`SetArrangementOrder`/`SetFormOrder`/`SetRidingOrder`/`SetFiringOrder`/`SetControlledByAI`），排除"方法不存在"；既然名字都对，剩下的只能是**签名匹配**。**为此新建工具 `E:\MBmod\CAI\.tools\sigscan`**（`Mono.Cecil.dll` 取自 Harmony 模块，net10.0 控制台，约 60 行）：`sigscan <assembly> <typeName> [methodPrefix]` 直接打印方法签名**含参数名**。跑出来立刻见底 —— **Harmony `Prefix` 除 `__instance`/`__0`/`___field` 外按参数名匹配宿主形参，名字不符会抛异常并使 `PatchAll()` 整体中止**；而 `Formation.SetMovementOrder` 的宿主形参叫 **`input`**（我写成了 `order`）、`Formation.SetControlledByAI` 的叫 **`isControlledByAI`**（我写成了 `isAIControlled`）—— 两处笔误，7 个 patch 生效 0 个。**两处都是我们的错误，不是版本问题。** **一并修掉三个装置缺陷**：①异常吞没 → 新增 `Patches.Failure` + `Describe(Exception)`（拉平异常链），`SubModule` 输出 `NOTE\|HARMONY_PATCHED count=N` 与 `NOTE\|HARMONY_PATCH_CAUSE <链>`，`[verdict]` 的 P0 行下方直接打印 cause；②`Applied` 的填充时机在 `PatchAll()` **之后** ⇒ 抛异常时 `Clear()` 不执行、列表保持空，而 **Harmony 中止前的成功项不回滚** ⇒ "`[patches]` 空"被误当作"一个都没打上"，会把人引向错误方向 → 改为 try/catch 包裹 `PatchAll`、**无论成败都无条件刷新** `GetPatchedMethods()`，`[patches]` 增加 `failure =` / `count =`；③**缺全量镜像** ⇒ `[census]`/`[coverage]` 连观测对象都没有 → 新增 `ProbeRuntime.BindAll()` 每 tick 遍历 `Mission.Teams → Team.FormationsIncludingEmpty → Bind`，置于 `SampleShadow` 与 `_script.Tick` **之前**（前者遍历 `_bindings`，后者按名解析实体）；配套 `ScriptRunner.Resolve` 失败写 `NOTE\|SCRIPT_ENTITY_UNRESOLVED` **消除静默失败**（此前"实体名写错/编队不存在/未绑定"与"指令执行了但无效果"在产物上完全同形，是本次排查困难的放大器）。**同时撤销会话 12 的错误修复**：当时以为在脚本里加 `cmd P0\|N0\|N1 Movement HOLD` 可"建立绑定"，**漏看了解析链路** —— `ScriptRunner.TryCommand` 先 `Resolve`→`FindBinding`、找不到即静默 return，`Command()` 根本不被调用 ⇒ `Bind()` 也不被调用 ⇒ 而 `Bind()` 此前只在 patch 触发时发生 ⇒ **鸡生蛋死锁**，脚本动作不可能用来建立绑定。`census-baseline.txt` 已回归零干预纯观测。**还踩到一个构建/部署陷阱**：`dotnet build` 成功后新 DLL 只在 `deploy\CommandLabProbe\bin\Win64_Shipping_Client\`（10:51:39），而游戏加载的 `Modules\CommandLabProbe\bin\...` 仍是 **09-20 23:47 的旧版** —— `CopyProbeToDeploy` 这个 MSBuild target **只拷到 `deploy\`，不拷进游戏目录**，必须手工 copy；已写入 `docs/10` §7.1A（这正是调试协议第①步"确认运行的是正确新构建产物"的价值：跳过它下次仍会 P0 FAIL，而我们会去怀疑已经修好的参数名）。**Docs**：`docs/10` 新增 **§11 首次实测记录**（产物 / 根因比对表 / 三个缺陷 / 部署陷阱 / 门禁价值评估 / 变更清单 / 下一步）；§7.8 改写为"观测对象从哪来：全量镜像"并**明文更正会话 12 的无效修复**；§7.1 拆为 7.1A 构建部署 + 7.1B 游戏设置。**顺带产出**：`[patches]` 段现在同时给 `failure` 与 `count`，`census-baseline` 注释登记完整修订链。**部署**：新 DLL MD5 `4ca2d169…`（46592 字节）已写入 `Modules\`（MD5 双向校验一致）；`script.txt` 播种修正版 `census-baseline`；旧产物归档不删除。**回归实证：Core/Simulation/Telemetry/tests 本轮零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变**（E5 的 `[E5] FAIL orphanTimers:5` 是 T-Cancel 变体的**预期**失败，判据② PASS，非回归）。**红线遵守**：零 `CombatBrain.*`；`TaleWorlds.*` 仅出现在 `src/CommandLab.BannerlordProbe`；未引入"每 tick 重复 SetOrder"（`BindAll` 只做 Core 侧注册，不触碰宿主状态）；**未写任何实测结论** —— 本轮全部结论都是关于装置自身的。 | `src/CommandLab.BannerlordProbe/OrderWritePatches.cs`（修 2 处形参名 + `Failure`/`Describe` + `Applied` 时机）、`ProbeSubModule.cs`（NOTE 输出异常链）、`ProbeRuntime.cs`（新增 `BindAll()` + `[patches]` 两行 + P0 cause）、`ScriptRunner.cs`（`Resolve` 留痕）、`docs/10-harmony-runtime-experiments.md`（新增 §11、改 §7.1/§7.8）、`deploy/.../census-baseline.txt`（撤销无效触碰）、`tests/artifacts/host/invalid-20260922-1046-p0fail/`（38 文件归档）、**新建** `E:\MBmod\CAI\.tools\sigscan\`、`HANDOFF.md`（§0/§2/§3/§5/§10.5/§12） |
| 2026-09-21 15:20 | 会话 12 | **采集 SOP 操作化 + 修掉第二个会毁掉 P0b 门禁的装置缺陷（零 Core 改动）。** 用户问"具体怎么跑、每场战斗有没有兵力或兵种要求、要设置什么"，读脚本与 `ProbeRuntime` 时**发现原 `census-baseline.txt` 存在实质性缺陷**（非文档问题）：它只含 `policy patchMovement=...` + `dump` 两行，**不产生任何 `Bind()`**；而 `_census` / `_sampledChanges` 的写入全部发生在 `Harmony Prefix → Gate() → Bind(formation)` 之后（`Gate` 第 229 行 `Bind`，第 236 行 `Bump(hook)`），`SampleShadow` 第 506 行也只 `foreach (var kv in _bindings)`；`CoverageRows()` 更直接以 `_sampledLast.Keys` 为行集。⇒ **无绑定 ⇒ `[census]` 与 `[coverage]` 两段全空 ⇒ P0b 判 FAIL**，但真实原因是"没有观测对象"而非"原生没有写入"——正是 Phase 10 头号风险"数据看起来干净但是假的"的镜像版（伪装成"没流量"的装置故障）。**修法**：脚本加 `t=1.0/1.05/1.10 cmd P0|N0|N1 Movement HOLD` 三次触碰。安全性论证：此三行在 `Acquire` 之前，`Gate` 走未夺取分支、按 Native 归属放行（返回 true），对 P0 等于下发一次 Stop（与玩家指挥一致），对 N0/N1 是 AI 本就会做的中性动作；**不夺取任何控制权**，`unauthorized_writes` 语义不变。`script.txt` 已重新播种为修正版。**同时把 SOP 从"7 条一句话"重写为可照做的操作手册**（`docs/10` §7 拆成 7.1 战前设置表 / 7.2 兵力与兵种 / 7.3 每场完整流程 / 7.4 必跑顺序与门禁 / 7.5 判据读法 / 7.6 收尾 / 7.7 缺陷登记）。**兵力与兵种结论：无硬性要求** —— 所有指标是按 tick 累加的比例量（`intent_hold_share` 为时长占比）或计数（`unauthorized_writes` 为次数）或分级（`grade`），**不含绝对兵力项**；兵力唯一作用是防止战斗在 35s 采样窗口内提前结束。兵种不做要求（框架只观测 Formation 级三个写入口，不区分兵种），但**推荐纯步兵**（纯骑兵/纯弓兵走 SoA 之外的单位行为，污染 `behavior=` 归因）。具体建议：双方各 3–4 个步兵编队（保证 N0/N1/N2/P0 真实存在，`FindBinding` 才找得到）、每队 ≥20 人、双方各 100–150 人（1:1~1.2:1 让战斗持续 ≥35s）、总兵力 ≤800。**关键操作约束（已写入 SOP）**：脚本只在 `ProbeSubModule.OnSubModuleLoad` 读取 ⇒ **必须先复制脚本再启动游戏**，游戏中途换 `script.txt` 不生效（此项此前未登记，属提问暴露出的隐性前置）。`_README.txt` 同步重写。**红线遵守**：零 `CombatBrain.*`；`TaleWorlds.*` 仅出现在 `src/CommandLab.BannerlordProbe`；未写任何实测结论（本轮仍是纯装置/SOP 工作）；未引入"每 tick 重复 SetOrder"。 | `docs/10-harmony-runtime-experiments.md`（§7 重写为 7.1–7.7）、`deploy/CommandLabProbe/scripts/census-baseline.txt`（加绑定触碰）、`deploy/CommandLabProbe/scripts/_README.txt`（重写）、`%USERPROFILE%\Documents\CommandLabProbe\script.txt`（重新播种）、`HANDOFF.md`（§5/§12） |
| 2026-09-20 02:15 | 会话 11 | **自定义战斗 TeamAI 装配取证（LEVEL B，IL）— 更正一条前提错误，零代码改动。** 缘起：用户质疑「双方怎么可能带 teamai」—— 该质疑成立，此前 `docs/10` §7 第 3 条写的"自定义战斗双方都带 TeamAI"**结论对但机理完全错**（当时归于 `BaseBattleMissionController::CreateTeams`）。四步 IL 溯源闭环：①`BannerlordMissions::OpenCustomBattleMission`（`TaleWorlds.MountAndBlade.dll` v1.3.4）IL offset 241–255 —— 闭包 `<>c__DisplayClass9_0` 的委托 `Ldftn` 后传给 `MissionState::OpenNew`，**方法体内无 `MissionCombatantsLogic` 的 `Newobj`**，装配发生在闭包委托里；②闭包方法 `<OpenCustomBattleMission>b__0` IL offset 130–188 —— `Ldnull`(PlayerBattleCombatant) → 按 `isPlayerAttacker` 二选一压 `playerParty`/`enemyParty`(守方) → **`Ldc_i4_1`** → `playerParty`/`enemyParty`(攻方) → `isPlayerSergeant` → `Newobj MissionCombatantsLogic::.ctor`；③`MissionCombatantsLogic::.ctor` 签名 7 参，offset 55–58 `Ldarg_s 5` → `Stfld TeamAIType`，按 IL 求值栈顺序 `Ldc_i4_1` 正落第 6 实参位 ⇒ **`TeamAIType = 1`**（硬编码，不随攻守/军士/规模变化）；④`MissionCombatantsLogic::EarlyStart` offset 16–26 做 `Ldc_i4_1`→`Sub`→`Switch count=3`，分支一（原值 1）offset 48–130 **遍历 `Mission.Current.Teams` 对每一支队伍** `new TeamAIGeneral(mission, team, 10f, 1f)` + `AddTeamAI(0)` ⇒ **攻守双方对 AI 完全对称**，两边 Formation 都走 `Formation::Tick` 的 `if(IsAIControlled) AI.Tick()`。**结论三条**：①自定义战斗 `MissionTeamAITypeEnum` = 单机(1)；②`EarlyStart` 单机分支不存在"只给玩家一方"的支路；③"结果对、机理错"已更正 —— `BaseBattleMissionController::CreateTeams` 虽也无条件给双方 `AddTeamAI`，但它是**战役 Mission 基类**，自定义战斗不走它，拿它当依据属碰巧命中。**登记两处不确定**：`MissionTeamAITypeEnum` 成员名 `SinglePlayer` 是**语义推断**（`ilscan` 只出 IL 不出编译期常量表，硬依据需反射 `Enum.GetNames`）；`TeamAIGeneral` 构造第 3 参 `10f` 的来源未追。**对 Phase 10 的影响（不含任何实测结论）**：`docs/10` §7 第 3 条与 §8 第 3 条已改写为取证表述 + 明确"**取证 ≠ 有流量**"（TeamAI 在场只说明 `FormationAI::Tick` 会被调用，且它带 0.5s 周期与迟滞，不保证写 MovementOrder）⇒ **N0 仍以 `[census]` 实测流量为唯一裁定依据，P0/P0b 门禁不放宽**。新增 `docs/10` §10 附录（取证链四步表 + 三条结论 + 两个易混淆点 + 对 Phase 10 影响）。**红线遵守**：零 `CombatBrain.*`；`TaleWorlds.*` 仅出现在 `docs` 与 `src/CommandLab.BannerlordProbe`；零代码改动、零测试改动（本轮纯只读取证）；未写任何实测结论。 | `docs/10-harmony-runtime-experiments.md`（新增 §10 + 改 §7/§8）、`HANDOFF.md`（§0/§5/§12） |
| 2026-09-20 23:45 | 会话 10 | **Phase 10 装置完备化 + 部署**（**未完成：**进游戏采集实测数据）。第一件事是查 HANDOFF §5「下一步」并要求补 `SetControlledByAI` patch，于是先做**取证**而不是先写代码 —— 结果直接更正了两条旧登记。①**`Formation.SetControlledByAI` 在 v1.3.4 只有一个方法且是 2 参**（IL offset 158–160：`Ldarg_2` → `Stfld _enforceNotSplittableByAI`），不存在 HANDOFF §5/§9 记的"1/2 参重载歧义"，`nameof` 即可精确匹配，`PatchAll` 静默不匹配的风险排除。②**最关键的一条**：`SetArrangementOrder` 的全部调用方（`ilscan ref`）清一色是 `Behavior*::TickOccasionally`/`OnBehaviorActivatedAux`；`Formation::Tick` 里 `_arrangementOrderTickOccasionallyTimer` 调 `ArrangementOrder::TickOccasionally(formation)`、`_arrangementTickOccasionallyTimer` 调 `IFormationArrangement::OnTickOccasionally()` —— **两条都不走写入口**。⇒ 把 `02` §8.2 的"Arrangement 双 Timer 不受门控"精修为"不是走了写入口但门控没生效，而是根本不走写入口"；⇒ Level C 预测：把 `ArrangementPath` 切成 Gated **也拦不住**（E12 ⑨「Level 上限由覆盖率决定」的宿主同构命题）。③Route B 机理：同一份 IL 两分支不对称 —— `SetControlledByAI(true)` 会 `SetMovementOrder(ActiveBehavior.CurrentOrder)`，`SetControlledByAI(false)` **不写 MovementOrder**（只 `_enforceNotSplittableByAI=0` + `ActiveBehavior?.OnLostAIControl()`）；真正起抑制的是 `Formation::Tick` 的门控 `if(IsAIControlled) AI.Tick(); else if(Team.IsPlayerSergeant) AI.Tick(); else IsAITickedAfterSplit=true;` ⇒ **Route B 连 Behavior 选择一起停掉**，是架构取舍而非实现细节。④好消息：`Formation::Tick` 的 `GetSubstituteOrder` 兜底循环调的是 `Formation.SetMovementOrder` ⇒ Movement 这条 non-gated 路径在 patch 覆盖内。**接着修掉一个会造成假阴性结论的实现缺陷**：Phase 9 的 `HoldMeter` 只被 `new` 从未 `Open()`，`Intent` 恒 `Unset` ⇒ `intent_hold_share` **恒 0%** ⇒ H2 的 G2 必然 FAIL，会被误读成"夺取失败"（纯实现缺陷，与架构无关）。改为 `Command()` 成功后开窗 + 多窗口累计 + 关闭后入 `_holdHistory`（Release 之后仍可读）。**新增五项采集能力，全部针对"数据看起来干净但是假的"这一 Phase 10 头号风险**：写入口普查 `[census]`（每 hook 计数，含放行）；`GetPatchedMethods()` 生效性取证 `[patches]` + **P0 门禁**（区分"没流量"与"patch 没打上"）；**影子采样** `SHADOW|`（每 tick 读宿主真值，不依赖 patch）；覆盖率 `[coverage]`（`sampled_changes − port_changes` = 绕过写入口的写入量，E12 ⑨ 的实测版）；`[verdict]` 判据自检 PASS/FAIL（`P0/P0b/H1-a/c/d/G1/G2/G3/H3-a/REL/X1–X5`）+ `NativeOverrideLatency`（H1 的"多久被覆盖"）。**补 4 个观测型 patch**（`SetControlledByAI` / `SetFormOrder` / `SetFiringOrder` / `SetRidingOrder`）—— 一律只计数、永不拦截、不改宿主行为；顺带取证 `SetArrangementOrder` IL offset 156–192 会级联 `SetFormOrder(FormOrderCustom(CalculateDesiredWidth()),0)`，证明写入口之间存在级联。产出 **8 份脚本**（`census-baseline`/`h1`/`h2-suppression`/`h2-coverage`/`h3-route-b`/`rel-none`/`rel-reapply`/`rel-reapply-resetweights`/`rel-after-release`），其中 `rel-after-release` 是**刻意设计的反例**（restoreBeforeRelease=false ⇒ 恢复写入必被拒 ⇒ 预期 FAIL），用于破解 Phase 7/8 已登记的"释放后失败数恒为 0 属平凡成立"。**已部署**到 `Modules\CommandLabProbe\`（3 DLL + SubModule.xml + scripts/），`script.txt` 播种为 census-baseline。**回归实证：Core 本轮零改动 ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变**（E5 的 `[E5] FAIL orphanTimers:5` 是 T-Cancel 变体的**预期**失败，判据② PASS，非回归）。**红线遵守**：未引用 `CombatBrain.*`；`TaleWorlds.*` 仅出现在 BannerlordProbe；无每 tick 重复 SetOrder；**未采集前不写任何实测结论**，`docs/10` 里全部预测均标 LEVEL C。回写 §0/§2/§3/§5/§12。 | `docs/10-harmony-runtime-experiments.md`、`deploy/CommandLabProbe/scripts/`（10 个文件）、改 `src/CommandLab.BannerlordProbe/ProbeRuntime.cs` + `OrderWritePatches.cs`；部署到 `Modules\CommandLabProbe\`（**Core/Simulation/Telemetry/tests 本轮零改动**） |
| 2026-09-20 02:05 | 会话 9 | **Phase 9 完成**：BannerlordProbe 工程落地（net472 + Harmony 2.4.2，**离线构建 0 error/0 warning**；宿主 v1.3.4）。**第一个动作是取证宿主 API**（`MovementOrder.MovementOrderStop/Advance/Retreat` 为静态值、`MovementOrder.OrderType` vs `Facing/ArrangementOrder.OrderEnum`、`Formation.GetReadonlyMovementOrderReference()`、`formation.AI.ActiveBehavior`、`OnMissionTick(float)`、`Mission.Current.CurrentTime`），避免靠猜写代码。接着撞上本项目最大的一个工程约束：Bannerlord 只加载 **net472** 程序集，而实验室三库全是 net10.0 → 要么复制一份指标定义（仿真与实测随即失去可比性，直接违反 Phase 9 要点 4），要么给 Core/Telemetry 追加 **`netstandard2.0` 目标**。选后者，改造分三类且全为兼容替换：①新增 `Compat/Polyfills.cs`（`IsExternalInit`/`RequiredMember*`/`CompilerFeatureRequired*`/`SetsRequiredMembers*`，Telemetry 链接共享）②三处 net5+ API 换成等价实现（`Enum.Parse<T>`→`(T)Enum.Parse`、`string.Create`→`string.Format`、`Convert.ToHexString`→`BitConverter.ToString`）③把 `WritePort` 的时钟依赖抽象为 **`ILabClock`**（`VirtualClock`/`ProbeClock` 对 WritePort·Metrics 等价 —— **这是仿真与实测可同表对比的结构保证**）。**回归实证：`replay_hash` 仍 `d9853e28…`、D0 15/15、E1–E11 51/51、E12 10/10，逐字不变。** 另加三处纯追加：`WritePort.SubmitImmediate`（宿主必须同步知道放行/拦截，不能等帧末 FlushTick）、`WritePort.SetPolicy`（脚本运行时翻转对照条件，判定逻辑位置不变）、`CommandValue.Obs0..Obs15`（中性观察槽，让 Facing/Arrangement 的 `OrderChanged`/oscillation 判定成立；>16 个不同值回绕会低估变化，`WrapCount` 已计数）。**红线落地**：只有 `HOLD/ADVANCE/RETREAT` 三条中性指令且只走 Movement 域，Facing/Arrangement 只观测不下达；判定**只**发生在 Core 的 `WritePort.Evaluate`，probe 不复制任何判定；无每 tick 重复 SetOrder；`OriginalMethodExecuted` 与 Harmony `return false` 是同一个布尔（Phase 5 锁定的约定首次在真实宿主侧兑现）。**`intent_hold_share` 按宿主真值采样**（`GetReadonlyMovementOrderReference()` 等）而非镜像 —— 读镜像会高估控制权。Acquire/Release 全流程可用，恢复动作三开关可按位组合（`ReapplyLastNativeOrder`／`ResetBehaviorWeights`／`RestoreControlledByAi`），`RestoreBeforeRelease` 可制造"交还后才恢复 → 写入被拒"的对照。全部对照条件走脚本 `policy`，Phase 10 换脚本即可产生对照、无需重编译。写入口归类 Movement/Facing=Gated、Arrangement=NonGated（`02` §8.2），`ArrangementPath` 可切 —— 这个切换本身就是 H2 的覆盖率对照。**未 patch 入口已登记**：`SetControlledByAI` 有 1/2 参两种调用形态，必须先定重载否则 `PatchAll` 静默不匹配会产出"看起来没写入"的假数据；`SetFiringOrder`/`SetFormOrder`/`SetRidingOrder` 留作覆盖率扩展（`02`：写入口是 6 个不是 3 个）。**尚未进游戏运行 —— 本 Phase 无任何实测结论。** 8 项偏差登记。`grep` 实证：Core/Simulation/Telemetry/tests 零 `TaleWorlds`、全仓零 `CombatBrain`、战术词零命中（`protect` 唯一命中是关键字 `protected`）、真实时钟仅 `DateTime.Now` 1 处且只用于 dump 文件名。回写 §2/§3/§5/§12。 | `docs/09-bannerlord-probe.md`、`src/CommandLab.BannerlordProbe/`（8 个 .cs + csproj）、`deploy/CommandLabProbe/`（SubModule.xml + script.sample.txt + 构建产物）（另改 `src/CommandLab.Core/`：Primitives 加 ILabClock+Obs 槽、WritePort 加 SubmitImmediate+SetPolicy、新增 Compat/Polyfills.cs、Authority 换 Enum.Parse；`src/CommandLab.Telemetry/TelemetryCollector.cs` 换 3 处 API；两个 csproj 改多目标；`CommandLab.slnx` 加工程） |
| 2026-09-20 01:50 | 会话 8 | **Phase 8 完成**：Architecture A/B/C 对比。**第一步按 `06` §6 遗留 8 补跑 E12**（Level 1–5 五档对照，判据 **10/10**；E1–E11 保持 **51/51**、D0 **15/15**、`replay_hash` 未变 `d9853e28…`）。装置上把竞争者仲裁优先级标定为 3（介于 L2 与 L4 之间）使谱系可观测，并新增两个口径：`intent_hold_share`（归属窗口内"实体值==意图值"时长占比）与 `lab_delivery_rate`。**H-I 裁决：成立且强于 `04` 原表述** —— osc L2=0.0000 < **L4=1.3000** > L5=0.0333，但 L2 是 **hold=0% 的伪稳定**（`unauthW=0` 且 Stable，与 L5 在旧指标上完全同形，判据⑧）、L4 是 hold=50% 的真实抖动、**只有 L5 同时满足"稳定+落地"（hold=100%）** → 修正 `04` 的"要么 2 要么 5"为"**L2 只在不需要控制权时成立**"。由此提出**方法论结论**：`unauthorized_writes==0` 且 Stable 是"取得控制权"的**必要非充分条件**，必须同表判读 hold，并新增**硬门限 G2（hold≥95%）**。E12 ⑨ 证明 **Level 上限由写入口覆盖率而非 Level 决定**（Level 5 下 gated 0 vs non-gated 20，全局 Stable→Oscillating，与 E8 8c 独立复现）；E12 ⑩ 证明竞争者优先级为 0 时 L2 与 L4 **完全不可区分**（判别力边界，宿主侧对应"是否存在同级权重 Behavior"，交 Phase 10 H3）。A/B/C 按 **13 维度**（原 HANDOFF 写"12 项"实为 13，已登记）逐格挂实验编号，无编号论断已剔除：**A 判负**（E7 7f 三项全升 + 7f-stall + E9 9b 劣化一个数量级）、**B 唯一过 G1+G2 但带覆盖率条件**（++6/−−5，最好测也最难建）、**C 过不了 G2**（非独占语义的结构上限，hold 只能 0% 或 50%）。结论形态是**分层组合**（默认 C、按需按域升级到 B、A 不列入选项）而非三选一，未预设赢家。给 Phase 11 留 4 条约束（默认 Writer 净增 0／升级一律 Level 5+Lease+Token／Reject 在写入口／夺取与交还必须同时验收）。**唯一改动的 src 文件**是 `NativeSource.cs` 新增 `NativeSourceConfig.Priority`（默认 0.0，E1–E11 结果与 hash 均未变）。6 项偏差登记：`lab_delivery_rate` 口径缺陷（写同值不计变更 → L5 仅 4.8%，不得作判据）、竞争者优先级标定、维度数 12 vs 13、释放恢复 0 失败平凡成立、Ns 仍是模型、L3 同优先级时胜负由 WriterId 字典序决定。`grep` 实证零 `CombatBrain.*`/`TaleWorlds.*`、零战术词、零真实时钟。回写 §2/§3/§5/§9/§12。 | `docs/07a-architecture-abc-comparison.md`、`tests/CommandLab.Tests/Scenarios/ArchitectureComparison.cs`、`tests/artifacts/e12-level-spectrum.txt`、`tests/CommandLab.Tests/Program.cs`（挂钩 E12）（另改 `src/CommandLab.Simulation/NativeSource.cs` 加 `Priority`） |
| 2026-09-20 00:55 | 会话 7 | **Phase 7 完成**：E1–E11 全部跑完，**判据 51/51 通过**，D0 保持 15/15。严格执行 `05` §9 遗留 2 —— **先校准后采集**，并借此挖出一处**指标定义缺陷**：只发生 1 次生效变更时 `average_command_lifetime` 无样本、回退 0.0，使"值从头到尾没变过"被判成"抖动"（0 < 2×T_eval）；已改为**右删失**补 `sim_seconds − 首次变更时刻`，7b/7e 由 Oscillating 正为 Marginal，E7 的裁决才成立。四个稳定性阈值经 E1 分布校准方向正确，未调整。**核心裁决（E7）**：仅 **7d Ownership+Lease** 达 `unauthorized_writes==0` 且 Stable；7b 迟滞 / 7c 最短驻留 / 7e 状态去抖把 `oscillation_frequency` 降 100% 但 `unauthorized_writes` 仍为 1 → Marginal（**且三者彼此不可区分**，登记遗留）；7f 高频组 attempts 61→600、writes 61→121、osc 2→4 **三项全升**，加 7f-stall 证明 Ns 的写入从未被真正阻止（[29,30)=2 vs [30,31)=2）—— 比 `05` 预测的"表观变稳"更强地否定了该红线手法。**位置裁决**：Reject 在写入口 0 vs 决策器 24（E3）、Fencing 在写入口 0 vs 决策器 4（E6，阈值 `delay>0.5s`）—— 与 `04` C2 吻合，首次有本项目量化数据。E4 4d fencing ON 0 vs OFF 29。E5 证实 H-G：(S,Cancel) 0 失败 vs (T,Cancel) 1 失败（孤儿定时器 5），Restart 成本 +43.5%（**建模值，只保留定性结论**）。E8 **复现 `02` 门控矩阵**（Arrangement `nonGated=86`），并发现**整实体接管 Arrangement 实际失败**（8c `unauthW=21`），全局分级 S→M→O；但判据④（恢复失败单调不减）全为 0、**平凡成立**，已作为偏差登记。E9 支撑 `03` 的 H-B 修正（Replace 0/Stable vs Append 120/Oscillating）；E10 支撑 `04` §6.1 降格建议（transform 开/关 `unauthW` 61 vs 61）；E11 支撑 **H-H**（归因完整率 50% vs 100%）。**假设裁决：H-F/H-G/H-J/H-E(部分)/H-H 成立，H-C 弱证据方向支持，H-I 未验证**（本 Phase 只用 Level 5，Phase 8 须补 2/4/5）。**D5：无任何实验需要 Proposal/Executor** → 弱结论"Proposal 不必要"待 Phase 11 裁定。三处偏差登记：①`05` §E1 的 4 组配置对"终值"口径结构性不敏感，判据③ 靠"最后一 tick 为同刻碰撞点"的 56.0s 对齐变体才成立 ②E7 7f 实测方向与 `05` 预测不一致 ③E8 判据④ 平凡成立。`grep` 实证零 `CombatBrain.*`/`TaleWorlds.*`、零战术词。回写 §2/§3/§5/§12。 | `docs/06-experiment-results.md`、`tests/CommandLab.Tests/Scenarios/Experiments.cs`、`tests/artifacts/e1-e11-results.txt`（另改 `src/CommandLab.Telemetry/Metrics.cs` 修右删失缺陷） |
| 2026-09-20 00:40 | 会话 6 | **Phase 6 完成**：纯 C# Command Lab 工程落地（net10.0，零第三方包）。严格按 `05` §8 的 11 项必需清单实现，**`Proposal`/`Executor` 未实现**（保留 D5 的"Proposal 不必要"出口）。两条硬约束的落地方式：①`ControlledEntity` 状态 setter 设为 `internal`，使 `WritePort` 成为**结构上唯一**的写入通道——Reject/归属/Token/Generation 校验全在 `WritePort.Evaluate()` 内，Source 侧只留 `CheckOwnershipInDecider` / `CheckGenerationInDecider` 两个**对照组开关**（E3 3c / E6 6b 用）；②`AuthorityLevel` 六档 + `CanWrite`/`IsExclusive`/`ArbitrationPriority` 权限表，无布尔 HasAuthority。Ns 按 `02` 参数建模（`T_eval=0.5s`、错峰、迟滞、`AllowAiTicking`），**gated 与 non-gated 两条独立路径**，`EnforceAuthorityOnNonGated` 默认 false 以复现门控矩阵（Arrangement 自维护写入不受门控）。仲裁实现在写入口内、归属校验之后（对应 `04` C1「Ownership 先筛非法源，Arbitration 再选值」），同 tick 多候选缓冲后统一裁决且**同分按 WriterId 字典序**，故仲裁本身与提交序无关——这正是 E2 要证的性质。补了 `05` E6 Failure Criteria 强制要求的 **`CommandDelayQueue`**（虚拟投递延迟，产生真实"在途"语义），否则 E6 会退化成同步执行而失去判别力。指标侧新增 `unauthorized_write_attempts`（含被拒，对应 E3 3a「能看到违规行为」）与 `unauthorized_writes`（仅生效变更）两项派生口径。**D0 门禁 15/15 通过**：三次重放 hash 全同（`d9853e28…`）、置换注册序后 hash 改变且终值同（顺序敏感被观测到）、空跑无 NaN、Ownership 期间越权生效写入 0 且 51 条被拒均带原因、Ns 双路径均非零。**另加三处判别力自检（不是实验结论）**：Fencing 开/关 = 0 vs 9、在途代次开/关 = 0 vs 8、Ns 双路径计数 —— 提前排掉"E4 4d / E6 6b-6c 无判别力"这两个会让 Phase 7 整体返工的风险。踩过一个坑已修正并写入 §5：`DefaultOwnerId` 必须等于 Ns 的 `WriterId`，否则所有 Ns 写入被误判为越权（已加 `ScenarioResult.Warnings` 提示）。零 `CombatBrain.*` / `TaleWorlds.*` 引用、零真实战术语义、零真实时钟（已 grep 自检）。回写 §2/§3/§5/§12。 | `CommandLab.slnx`、`src/CommandLab.Core/`、`src/CommandLab.Telemetry/`、`src/CommandLab.Simulation/`、`tests/CommandLab.Tests/`、`tests/artifacts/d0-gate.txt` |
| 2026-09-19 23:59 | 会话 5 | **Phase 5 完成**：实验计划。先建**通用装置规范**——虚拟 `VirtualClock`（dt=0.1s/60s）、禁 `Thread.Sleep`、单一播种 RNG、显式可调注册序、`replay_hash`；核心增量是 **Ns 原生仿真模型**，参数全部取自 Phase 2 IL 取证（`T_eval=0.5s`、错峰 ×0.1、`PreserveExpireTime` 迟滞可开关、**gated 与 non-gated 双写入路径**、6 写入口取 3），**这是全套仿真能对真实宿主产生预测力的唯一原因**。把 §8 的 14 项指标给出精确公式，并定义**稳定性三级分级**（稳定/临界/抖动），其中"抖动"判据 `average_command_lifetime < 2×T_eval` 直接把 Phase 1 §3.2 的特征签名写成了阈值。Telemetry 补 `AuthorityLevel/LeaseToken/TaskId`，并锁定 `OriginalMethodExecuted` 字段名以便 Phase 10 的 Harmony 实测可同表对比。产出 **D0 装置门禁 + E1–E11 共 11 个实验六段式详单**（8 组必做 + E9/E10/E11 承接 Phase 3/4 遗留）。三处具备证伪能力的设计：①**E7 的 7f 组是反例演示组**，必须跑出"提高写入频率后表面变稳但 `unauthorized_writes` 不降"，否则"被竞速掩盖"这一现象无法被量化；②**E4 4d lost-owner** 的有/无 Token 变体必须分叉，否则本实验无判别力；③**E8 三级递进**必须复现 `02` 门控矩阵（Arrangement 域仍出现 non-gated 写入），否则判定 Ns 失真并回修重跑。给出**判别力矩阵**（11 条"哪两个机制由哪个实验区分"），规定 Phase 8 必须引用实验编号作证据。给出 **Phase 6 施工清单**：必需 11 项，**`Proposal`/`Executor` 因无实验强制需要而不实现**（留"Proposal 不必要"这条结论的可能性）；两条硬约束：Reject/Token 校验必须在 WritePort 内、Authority 必须按 Level 声明。**明确 E8 只能给条件式预测，不得越权回答 §10.6 实测问题。** 遗留 6 项（Ns 是模型非实测、**稳定性阈值为先验设定未校准**、Phase 7 须先校准后采集）。零代码。回写 §2/§3/§5/§12。 | `docs/05-experiment-plan.md` |
| 2026-09-19 02:50 | 会话 4 | **Phase 4 完成**：通用 Command Authority 架构调研（非 Bannerlord 专有）。先做 9 轮资料取证（有结果者 8 轮），建立来源清单 **S1–S22**，覆盖 Game AI 侧 7 类机制（Subsumption / BT / Utility / GOAP·HTN / Blackboard / Contract Net / RTS 分层指挥链）与通用系统侧 9 类（Actor / Lease·Chubby / Fencing Token / Preemption·K8s / Cancellation·结构化并发 / Mode Change Protocol / CQRS·Event Sourcing / Authority Levels·Sheridan-Playbook / Conflict Detection·MVCC）。建立**控制权四要素模型 Subject×Object×Duration×Transition**，指出当前方案设计的最大缺口是**未定义 Transition 语义**。给出 8 条判定结论 C1–C8，其中四条为矩阵性：①**Ownership 而非 Arbitration 决定控制权**（Arbitration 只在合法候选集内部使用，两者串联）②**Lease+Fencing 必须配套，且强制点必须在写入口**（"校验写在你自己身上，管不住已发出的在途命令"）③**通用文献不存在 Peer 仲裁**（Blackboard 有 scheduler、Contract Net 有 announcer）→ 与 Phase 3 从 RBM 归纳的"仲裁权不下沉"**跨领域交叉验证**；④**第 5 类 Native Pipeline 在通用文献无对应机制** → Phase 8 无法获得文献背书，必须由 Telemetry 裁决。完成硬性要求的四组对照，其中 Preemption 三语义找到**唯一严格来源**＝实时系统 Mode Change 协议（Immediate→Cancel / Bounded→Resume / Phased→Restart，选择依据是数据一致性而非性能），并用 K8s graceful period 给出工程模板。**C7 新洞察**：宿主把 Formation 当 Blackboard（多源共享可变状态、无 owner，Phase 2 已实证），而 Mod 需要 Command Bus 语义（CQRS 单一权威写入口）——第 5 类冲突的深层成因可能是**架构风格错配**。**向 Phase 3 提异议**：建议把「Content Transformation」从"第六类冲突"降格为"与 Authority 正交的第四种机制"（它不改"谁在写"，故不解决任何一类冲突），证据为 BT Decorator + RBM `OverrideSetMovementOrder`，待 E10 定稿。与 Phase 1 五类冲突做**覆盖率映射**：1–3 类有充分文献背书，第 4 类缺"veto 上报通道"成熟样板，**第 5 类无**。登记新增假设 **H-F~H-J**。给出 Phase 6 的 5 条硬约束。遗留 7 项（含 RecoilEngine 二手转述未核源码、MRTA 未深入、**无任何实测数据**）。零代码、零 `CombatBrain.*`/`TaleWorlds.*` 引用。回写 §2/§3/§5/§12。 | `docs/04-command-authority-survey.md` |
| 2026-09-19 01:50 | 会话 1 | 立项书下发；因额度 BRAKE 未开工；建立交接文件 | `HANDOFF.md` |
| 2026-09-19 03:10 | 会话 3 | **Phase 3 完成**：静态取证 `E:\MBmod\FIX\rbm_decompiled\RBMAI\`（59 个 .cs）。枚举出 **81 个 Harmony patch**（40 Prefix/41 Postfix，51 个目标类型）。9 个必答项全部给出「是/否 + 文件:行号」：Tactic 与 Behavior 都 Patch（17 个 Behavior + 10 个 Tactic/TacticComponent），但**写入只在 Behavior 侧**；改 Weight 是主要通道且整体替换 `SetDefaultBehaviorWeights`(return false)；**替换** `CalculateCurrentOrder`（只写私有字段）；直接 SetOrder 但仅在 Behavior patch 内；**`typeof(FormationAI)` 零命中 → 仲裁权完全留给原生**；**无 Commander/Arbiter/Proposal/Executor**；**Writer 净增 = 0**。**H-B 判定：方向支持但表述需修正** —— 不是"减少 Writer"而是"替换而非新增 Writer"（前者要对抗原生链=路线 B，后者只需接入=路线 C）。**新发现**：`OverrideSetMovementOrder` 是与 Authority 正交的**值变换层**（只改 ref input、永远 return true、try/catch 兜底）→ 建议 Phase 1 增补第六类「Content Transformation」，待 E10 验证后再定。**更正 Phase 2 事实错误**：`Formation::Tick` 的 SubstituteOrder 是**兜底循环**（`IsApplicable==false` 才触发、上限 10 次/帧），非每 tick 无条件写入 → §8.1 对 §10.6 PoC 的倾向性理由被削弱，已同步改 `02` §8.2。零代码。回写 §2/§3/§5/§12。 | `docs/03-rbm-case-study.md`（附更正 `docs/02` §8.2） |
| 2026-09-19 02:45 | 会话 2 | **Phase 2 完成**：用 ilscan 对 `TaleWorlds.MountAndBlade.dll` 做 IL 取证。产出原生控制链全景图与 5 项判定结论：①**Formation 不存在单一最终 Writer**（4 类 Writer：决策/准入/常驻自维护/外部）；②**Tactic 从不直接写 Order**，只走 `ResetBehaviorWeights`+`SetBehaviorWeight`，Behavior 集合在 `OnUnitAddedToFormationForTheFirstTime` 一次性注册；③FormationAI 周期 **0.5s**（`Timer(...,0.5,true)`，错峰 ×0.1），评分 `GetAIWeight()×WeightFactor` + `PreserveExpireTime` 迟滞 + `NavmeshlessTargetPositionPenalty`，全局闸 `Mission.AllowAiTicking`；④**`CalculateCurrentOrder()` 不写 Order**，写入在 `TickOccasionally`(Mv+Fa) 与 `OnBehaviorActivatedAux`(Mv+Fa+Arr+Firing+Form)，非当前 Behavior 只算不写；⑤**写入口是 6 个不是 3 个**（+Firing/Form/Riding）。产出 `IsAIControlled` 门控矩阵：覆盖 Behavior 侧，**不覆盖** `Formation::Tick` 的 `GetSubstituteOrder` 回写与 Arrangement 两个独立 Timer。对 §10.6 PoC 给出倾向性推断但明确要求 Phase 10 实测。零代码。回写 §2/§3/§5/§10.5/§12。 | `docs/02-bannerlord-control-pipeline.md` |
| 2026-09-19 02:10 | 会话 2 | **Phase 1 完成**：建立 L0–L4 决策栈分层模型；定义五类冲突（Command/Authority/Intent/Plan/Native Pipeline），每类给出定义·性质·触发条件·中性最小复现·"为何不能用其他机制"·适用机制·误配失败模式·Telemetry 特征签名；产出正交判定表与 6 条症状诊断表；给出 Q7 的可操作量化分解方法；登记待验证假设 H-A~H-E（H-E 为本分类的证伪条件）。零代码。回写 §2/§3/§5/§12。 | `docs/01-problem-definition.md` |

## 13. 云端离线开发区验证（2026-10-06）

本节记录云端 Linux 离线验证，不改变 Phase 状态，也不替代 Bannerlord 实机实验。复跑入口：`python3 tools/run_offline.py`；需要时可用 `--dotnet /path/to/dotnet` 指定 .NET 10 SDK。SDK 版本为 `10.0.401`。工具在临时净副本按原 csproj 目标逐项构建 Core、Telemetry、Simulation、Tests（共 6 个目标，含 Core/Telemetry 的 `netstandard2.0`），然后执行 C# 门禁、离线测量和 Python 遥测工具测试；每次运行均生成唯一的 `tests/artifacts/offline-<run-id>/` 报告目录，不覆盖历史 artifacts。

本次冻结版云端验收报告：`tests/artifacts/offline-20261006T031840.425502Z-b7f0f0ed/`（云端运行产物，未纳入 Git）。六个构建目标均为 0 warnings / 0 errors；D0 **15/15**、E1–E11 **51/51**、E12 **10/10**、MEASURE **26/26**，C# 合计 **102/102**。D0-S1 `replay_hash` 仍为 `d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199`。冻结版验证源码 SHA-256：`8ca81d904c7ea52c7d911c9d1483a99f657e1f64f2b0dd4c73d27689dd81fe82`。Python 遥测工具测试 **9/9**。

**本次 Bannerlord runtime = NOT_RUN**。Phase 10 已完成的 H1 证据保持原记录；H2、H2b、H3、REL 仍待有效实机验证，属于 Phase 10 后续工作。Phase 11 和 Phase 12 未推进。本云端验证仅证明跨目标构建与离线逻辑/遥测门禁通过，不产生新的宿主运行时或战术架构结论。
