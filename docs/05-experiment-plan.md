# 05 · 实验计划（Experiment Plan）

> Phase 5 交付物 · 2026-09-19
> 状态：**已完成（纯文档，零代码）**
> 用途：**Phase 6 的施工蓝图、Phase 7 的执行脚本**。本文档不解释概念，只定义"怎么跑、看什么数、什么算过、什么算废"。
> 前置依赖：`01`（五类冲突 + 特征签名 + H-A~H-E）、`02`（原生链参数）、`03`（RBM 证据）、`04`（通用机制 + H-F~H-J）。

---

## 0. 本文档的验收标准（自我约束）

一个实验条目的质量由以下四点判定，**缺一即视为未完成**：

1. **有假设**：挂到某个假设编号（H-*），而不是"跑一下看看"。
2. **有判别力**：明确指出本实验要**区分哪两个机制**；若两个竞争机制在本实验下产出同样数据，实验无效。
3. **有成废阈值**：Success / Failure Criteria 写具体数值与比较符，不允许"明显下降""大致稳定"。
4. **有 Telemetry**：声明本实验必录字段与至少一条 DecisionTrace 样例。

---

## 1. 通用实验装置规范（Harness）

所有实验共用一套装置。**装置本身必须先过 §2 的 D0 门禁，D0 不通过则后面全部数据作废。**

### 1.1 虚拟时间与可重复性契约

| 项 | 规定 |
|---|---|
| 时间源 | `VirtualClock`，**唯一**时间来源。禁止 `DateTime.Now`、禁止任何计时器作为判定依据 |
| 禁止项 | **禁止依赖 `Thread.Sleep`（或等价的真实等待）作为主要测试机制**。实验推进方式必须是"显式推进虚拟时间" |
| 分辨率 | 默认 `dt = 0.1 s`；涉及相位差的实验改用 `dt = 0.05 s` |
| 默认时长 | 每个 scenario `60 s` 虚拟时间；长时实验（starvation / 抖动疲劳）延长至 `300 s` 并在条目中注明 |
| 随机源 | 单一播种 RNG（`seed`），**所有随机必须通过它**，禁止框架内隐式随机（Dictionary 遍历顺序、哈希顺序等不允许影响输出） |
| 遍历顺序 | Source / Task / Writer 的遍历顺序必须是**显式注册序**且可配置；用于 E1 证明"顺序敏感"这一性质本身 |
| 可重放 | 同一 `(scenario_id, seed, 配置)` 必产出**逐字节相同**的 DecisionTrace 序列，用 `replay_hash` 校验 |
| 输出 | 每次运行产出：① DecisionTrace 全量序列 ② 指标汇总 ③ `replay_hash` |

### 1.2 中性对象与命名表（红线 D2）

| 概念 | 实验内表示 | 禁止出现 |
|---|---|---|
| 被控实体 | `E0`（可增至 `E1` 用于 Plan / 跨域场景） | Formation / 阵型 / 部队类名 |
| 控制域 | `Movement` / `Facing` / `Arrangement`（**仅此三者**，本阶段不引入 Firing/Form/Riding） | 任何宿主枚举名 |
| 命令值 | `MOVE_A` / `HOLD` / `ADVANCE` / `RETREAT`（纯符号，**无战术语义**） | Charge / Brace / Skirmish / Protect / 骑兵 / 弓兵保护 / 士气 |
| 决策源（Lab 侧） | `Source A` / `Source B` | 任何具体战术模块名 |
| 决策源（宿主侧） | `NativeSource`（记作 **Ns**） | 宿主具体类名 |
| 授权源 | `OwnerId ∈ {Native, Lab, None}` | — |

> **自检规则**：若某实验的执行或说明中出现了"骑兵冲锋""弓兵保护""士气崩溃"等语义，该实验作废并重写。

### 1.3 NativeSource（Ns）仿真模型 —— **预测力的来源**

Ns 不是"随便一个第二个写者"，它的参数直接来自 Phase 2 的 IL 取证结果。**这是本套实验能对真实宿主行为产生预测力的唯一原因。**

| 参数 | 取值 | 来源（`02`） | 实验中可否调 |
|---|---|---|---|
| 评估周期 `T_eval` | **0.5 s** | FormationAI `Timer(..., 0.5, true)` | 可（敏感性分析） |
| 相位错峰 | `T_eval × 0.1 × index` | 按 TeamIndex/FormationIndex 错峰 | 可 |
| 迟滞 | 非当前行为受 `PreserveExpireTime` 折扣 | 迟滞机制 | 可开关（E7 用） |
| **受控写入（gated）** | behavior 侧 Movement / Facing 写入，`IsAIControlled` 可门控 | 门控矩阵 | 固定 |
| **自维护写入（non-gated）** | Arrangement 双 Timer + `GetSubstituteOrder` 兜底回写，**不受 `IsAIControlled` 门控** | 门控矩阵 + §8.2 更正 | 固定 |
| 全局闸 | `AllowAiTicking`（可关闭全部 tick） | 全局闸 | 可开关 |
| 写入口数量 | 6（本阶段只用 3：Movement/Facing/Arrangement） | 写入口枚举 | 固定 |

**关键建模约束（必须遵守）**：Ns 的 **gated 写入**与 **non-gated 写入**必须实现为**两条独立路径**。这是 E8 能预测"`IsAIControlled=false` 后 Arrangement 是否仍有残留覆盖"（HANDOFF §10.6 的阻断级 PoC）的前提。若合并实现，E8 失去预测力。

### 1.4 必录 Telemetry 字段

每次**写入尝试**（含被拒的）都必须记录一条：

```text
Timestamp | Entity | Domain | WriterId | OwnerAtWrite | AuthorityLevel
Command | PrevValue | NewValue | LeaseId | LeaseToken | LeaseExpiryAt
OriginalMethodExecuted | OrderChanged | Rejected | RejectReason
TaskId | TaskState | IntentId | Gen
```

**DecisionTrace 样例**（承接 HANDOFF §8 格式，本阶段补齐 AuthorityLevel / LeaseToken / TaskId）：

```text
t=32.10  E0  Domain=Movement  Owner=Lab  Level=5  Lease=L7  Token=41  Expiry=36.10
Writer=NativeSource  OriginalMethodExecuted=false  OrderChanged=false
Command=ADVANCE  PrevValue=HOLD  NewValue=HOLD
Rejected=true  Reason=StaleOrNonOwner(token 38 < max_seen 41)
Task=T12(HOLD_POSITION, Active)  Intent=I3  Gen=11
Decision: reject write
Reason: Movement Authority owned by Lab under Level-5 lease; writer token stale
```

> **注意**：`OriginalMethodExecuted` 在纯仿真中表示"宿主原始写入例程是否真的执行了"——它对应 Phase 10 里 Harmony Prefix 的 `return false`。字段名必须保持一致，才可与 Phase 10 实测数据同表对比。

### 1.5 指标定义与稳定性分级

指标取自 HANDOFF §8，此处给出**精确定义**（否则两组实验数据不可比）：

| 指标 | 精确定义 |
|---|---|
| `commands_per_second` | 写入尝试次数 / 仿真秒数（含被拒） |
| `order_changes_per_second` | `NewValue != PrevValue` 的**生效**写入数 / 秒 |
| `unauthorized_writes` | 写入时 `WriterId != OwnerAtWrite` 的**生效**写入总数（在写入口判定，不在决策器） |
| `native_overrides` | Owner=Lab 期间，Ns 成功改变了该域的值的次数 |
| `lab_overrides` | Owner=Native 期间，Lab 成功改变值的次数 |
| `authority_transfers` | 成功 Acquire 次数（含初始授予） |
| `preemptions` | Task 由 Active → Preempted 的次数 |
| `average_command_lifetime` | 值保持时长均值（从生效写入到被下一次生效写入覆盖） |
| `oscillation_frequency` | `A→B→A` 型**回归翻转对**数 / 秒（只数成对回归，不数单次变化） |
| `reaction_latency` | 从 Acquire 请求到首次生效写入的虚拟时间 |
| `stale_command_execution` | 携带 `token < max_seen` 却仍被**接受**的写入数（**应当恒为 0**，非 0 即设计失败） |
| `task_starvation` | 仿真结束时既未 Complete 也未 Cancelled 的 Task 数，附最长 Pending 时长 |
| `release_recovery_failures` | Release 后，`2 × T_eval` 内 Ns 状态未回到 acquisition 前基线的次数 |
| `intent_hold_share` | **有效意图窗口内"实体实际值 == 意图值"的累计时长 ÷ 该窗口累计时长**。**有效意图窗口**：同一 `(EntityId, Domain)` 上从**首个被接受的 Lab 写入**起算，至该 Lab 归属结束（Release 或到期）为止；窗口的起止与该累计时长必须逐 `(EntityId, Domain)` 记录。**无有效意图窗口、窗口长度为零、或窗口内未采样到任何样本时不能判 PASS**，只能记"无窗口"。多目标时**逐目标分别报告**，不得以 BestHold 代表所有目标。**这是第 15 项指标，本 Phase 5 计划成文时未列入上表，于 Phase 8 E12 补入、Phase 11 在此正式补记**（来源 `docs/07a-architecture-abc-comparison.md` §2.1、§7；实测口径 `docs/09-bannerlord-probe.md`；宿主实测见 `HANDOFF.phase11.md`、`docs/07-final-architecture.md` 与 `_audit/2026-10-07-phase11-evidence-review.md`）。 |
| `intent_hold_share`（**事件归因窗口，不参与上述分母**） | 与上项**分开、不得混用**的另一组切分：`pre_acquire`（Acquire 前）／`acquire_to_first_accepted_lab_hold`（Acquire 到首个被接受的 Lab 写入）／`first_lab_hold_to_release`（首个被接受的 Lab 写入到 Release）。**前两段没有当前有效 Lab 意图，属事件归因窗口**，仅用于把 telemetry 事件按阶段归类与对齐时间轴，**不是 hold 的分母**。若按完整归属时间（已知 run05/06 为 Acquire≈3.00 → Release≈28.00）计算，与按有效意图时间（≈3.50 → 28.00）计算，G2 的数值含义不同，**两者不得互相替代或混算**。 |
| **E12 仿真窗口 vs Probe 采样实现（口径差异，登记）** | **E12 历史仿真不使用窗口打开/延长语义**：`tests/CommandLab.Tests/Scenarios/ArchitectureComparison.cs:43-58` 的 `IntentHoldShare` 在**固定 `[2.0,22.0)` 区间**上积分——筛选该域被接受且 `OrderChanged` 的事件、按时间排序重建时间线，**窗口前状态延续进来**（第 51 行 `if (a.Timestamp <= from) { cur = a.NewValue; continue; }`），在区间内累计状态等于 HOLD 的时长，**分母固定 20 s**（第 58 行 `to - from`）；第 201 行调用参数为 `from=2.0, to=22.0`。**全程不调用 `HoldMeter.Open`，也不按每次变更延长窗口。** 宿主 Probe 则是 `HoldMeter` 的 tick 采样，其 Open 条件见下条代码差距。**两者的实验窗口与实现须分别标明**，引用 hold 数值必须写明来自哪一侧，**不得把 Probe 的窗口规则倒写为 E12 的历史行为**。 |

**稳定性分级（全文档统一约定，不得在各实验里各自解释）**：

| 分级 | 判据（须同时满足） |
|---|---|
| **稳定** | `unauthorized_writes == 0` 且 `oscillation_frequency ≤ 0.05 /s` 且 `order_changes_per_second ≤ 0.2` |
| **临界** | 不满足"稳定"且不满足"抖动"→ 记为临界，**必须补跑 300 s 长时 scenario 再判** |
| **抖动** | `oscillation_frequency ≥ 0.5 /s` **或** `average_command_lifetime < 2 × T_eval`（即命令活不过两倍原生决策周期） |

**硬门限 G1 / G2（Phase 8 引入，Phase 11 正式补记）**：

| 门限 | 判据 | 说明 |
|---|---|---|
| **G1** | `unauthorized_writes == 0` | 在写入口判定的越权**生效**写入必须为零 |
| **G2** | `intent_hold_share ≥ 95%` | 必须与 G1 **同表判读**；只看 G1 时"从未抢到控制权"与"完全控制"同形 |

> G1/G2 与上表的"稳定性分级"是**两套并存的判据**：分级刻画抖动程度，G1/G2 刻画控制权是否真正落地。E12 Authority Level 谱系（`docs/07a` §2.1）给出的是二者不可互推的直接证据：L2 档 `unauthorized_writes == 0` 且判为稳定，但 `intent_hold_share = 0.0%` —— 即 **`unauthorized_writes == 0` 且 Stable 是"取得控制权"的必要非充分条件**。G2 的 95% 阈值与 §1.4 的 Telemetry 字段口径均由 Phase 10 宿主装置落实（`docs/09-bannerlord-probe.md`、`docs/10-harmony-runtime-experiments.md` §5 判据表）。

> 第三条直接来自 Phase 1 §3.2 的特征签名：Authority 冲突的典型值是"`average_command_lifetime ≈ 原生评估周期而非任务周期`"。**把这条写成分级阈值，是为了让"稳定"不再靠感觉。**

> **与现有 Probe 实现的一处差距（登记为待验证，本轮不改算法）**：`ProbeRuntime.Command`（`ProbeRuntime.cs:378-384`）打开意图窗口的条件实际是 `LastAttempt is not null && domain == CommandDomain.Movement` —— 它**只检查该次写入尝试是否发生（非 null），并不检查 `Rejected`**。因此"现有实现仅在成功/有效命令提交后打开窗口"**不能无条件宣称**。正常 H2 接受写入打开窗口有本轮证据（run05/06 `t=3.5045/3.5020` 首个被接受的 Lab 写入，见 `_audit/2026-10-07-phase11-evidence-review.md` §5.1）；但**被拒命令是否同样打开/延长窗口，本轮未实测**，登记为待验证。此处与设计 §5.1 的描述同属简化表述，本行按代码实际行为更正。

---

## 2. D0 · 装置自证（前置门禁，**不计入 8 组实验**）

| 段 | 内容 |
|---|---|
| **RQ** | 装置本身是否确定性、是否能如实区分"有序"与"无序"？ |
| **Hypothesis** | H0：给定相同 `(scenario, seed, 配置)`，装置输出逐字节可复现；改变源码侧遍历顺序会改变输出——即装置对顺序敏感这一性质是可观测的，从而可用于 E1 判定非确定性。 |
| **Test** | ① 同一 scenario 跑 3 次，比较 `replay_hash`；② 显式置换 Source 注册序（A,B）→（B,A）跑同 scenario，比较 `replay_hash`；③ 空跑 60 s 无写入，断言所有分母型指标为 0 而非 NaN。 |
| **Metric** | `replay_hash` 相等率（要求 3/3）；② 的 hash 差异与否要求**记录在案**（允许相同，但必须记录该 scenario 是否对顺序敏感）。 |
| **Success** | 三次完全相同的 replay_hash 相同；无 NaN；② 的结论被记录（用于 E1 解释）。 |
| **Failure** | 任意一次 hash 不一致 → **后续全部数据作废**，先修装置（三个常见嫌疑：`Dictionary` 等非确定容器的遍历顺序、未播种的随机源、任何依赖真实时钟的逻辑）。 |

---

## 3. 实验总览表

| # | 实验 | 对应假设 | 区分哪两个机制 | 主判据指标 |
|---|---|---|---|---|
| E1 | Last Writer Wins（基线） | 基线（证 H0 / 建立可比基线） | 无仲裁 vs 有仲裁 | `order_changes_per_second`、`oscillation_frequency`、重放差异 |
| E2 | Priority Arbitration | H-E（部分）、Phase 1 §3.1 | **Arbitration vs Ownership**（先证明前者做不到） | `task_starvation`、`oscillation_frequency`、重放确定性 |
| E3 | Ownership | Phase 1 §3.2、H-F | **Reject 在写入口 vs 在决策器** | `unauthorized_writes`、`native_overrides`、所有者唯一性断言 |
| E4 | Lease | H-F（配套性） | **Lease 单独 vs Lease+Token 配套** | 锁死时长、`stale_command_execution`、恢复时延 |
| E5 | Preemption 恢复语义 | **H-G** | **Resume / Restart / Cancel** | `release_recovery_failures`、残留状态、`commands_per_second` 增量 |
| E6 | Generation / Fencing | **H-J**、Phase 1「旧 Command 能否执行」 | **写入口校验 vs 决策器校验** | `stale_command_execution`（须为 0） |
| E7 | Oscillation 五手段 | **H-F** | Hysteresis / MinLifetime / Lease / State / Priority | `unauthorized_writes`（唯一硬指标）+ 分级 |
| E8 | Domain Ownership 三级递进 | **H-C（弱证据）**、HANDOFF §10.6 | **整实体授权 vs 分域授权** | 各域 `native_overrides`、`release_recovery_failures` |
| E9 | Replace vs Append Writer | Phase 3 H-B 修正 | **替换 Writer vs 并列 Writer** | `unauthorized_writes`、Writer 净增 |
| E10 | Value Transform 层能力边界 | Phase 3「第六类冲突」异议 | **Transformation 是正交机制 vs 是一类冲突** | `unauthorized_writes` 是否变化 |
| E11 | Blackboard vs Command Bus 语义 | **H-H**（`04` C7） | **多源共享写 vs 单一权威写入口** | 归因完整率（每个变化是否有 recorded writer+reason） |

---

## 4. 实验详单（8 组必做）

### E1 · Last Writer Wins（Baseline）

| 段 | 内容 |
|---|---|
| **RQ** | 两个合法源无仲裁地写同一域，会产生什么可观测行为？结果是否可复现？ |
| **Hypothesis** | H0：最终值取决于执行顺序 ⇒ 改变遍历顺序会改变结果，且 `average_command_lifetime` 接近源的评估周期而非任何任务周期。这是 Phase 1 §3.1「同一 Tick 多合法候选 + 重放得不同结果」的基线证明。 |
| **Test** | Source A 每 `1.0 s` 评估并写 `ADVANCE`；Source B 每 `0.7 s` 评估并写 `RETREAT`；`E0.Movement`；60 s。<br>变体：`(注册序, 初相位)` ∈ {(A,B),(B,A)} × {0, 0.35 s}，共 4 组，同 seed。 |
| **Metric** | `order_changes_per_second`、`oscillation_frequency`、`average_command_lifetime`、`replay_hash`（4 组各自）、各配置终值。 |
| **Success Criteria** | ① 至少一组 `order_changes_per_second > 0`；② 给定配置内部三次重放 `replay_hash` 全同（装置确定）③ 改变注册序/相位后，**至少一组终值不同**（证明结果依赖于非语义因素）④ `average_command_lifetime < 1.0 s`（远短于任一源的意图周期）。 |
| **Failure Criteria** | 若 4 组终值全相同且 `replay_hash` 全同 ⇒ 说明本 scenario 对该配置不敏感，**必须换相位/周期组合重做**，不得据此宣称"LWW 是确定的"。 |
| **Telemetry** | 全字段；样例需要展示同一 tick 内两条写入，其中后者覆盖前者。 |

> **本实验的意义**：它不给结论，它给**可比的基线**。后续所有机制的收益都必须以它为分母。

---

### E2 · Priority Arbitration

| 段 | 内容 |
|---|---|
| **RQ** | 只加 Arbitration（不加 Ownership），能否消除抖动？它对饥饿与紧急插队的表现如何？ |
| **Hypothesis** | **H-E 的一部分**：Arbitration 可使重放确定（同输入同输出），但**不能**消除来自权限侧的抖动；且固定优先级会产生 `task_starvation`。 |
| **Test** | 四个子场景，Ns 参与（启用 gated 写入路径）：<br>**2a 固定优先级**：Source A > Source B > Ns，每 tick 重评估。<br>**2b Utility 带噪**：两者效用差 |Δu| < 0.05，注入 seeded 噪声 σ=0.03。<br>**2c 长期任务**：A 提交 `DURATION_LONG` 任务（意图周期 30 s），B 每 0.5 s 提交短任务。<br>**2d Emergency**：Ns 在 t=20 s 插入一次性最高优先级写入。 |
| **Metric** | `order_changes_per_second`、`oscillation_frequency`、`task_starvation`（含最长 Pending）、`average_command_lifetime`、`reaction_latency`（2d）、`unauthorized_writes`（**应为 N/A 或 0——因本实验无 Ownership 概念，若不为 0 说明机制串味了**）。 |
| **Success Criteria** | ① 2a/2c/2d 同配置三次重放 hash 全同（Arbitration 是确定的）；② 2b `oscillation_frequency ≥ 0.5 /s`（噪声环境下的抖动被证实）；③ 2c 出现 `task_starvation ≥ 1` 且最长 Pending ≥ 25 s（饥饿被证实）；④ 2d `reaction_latency ≤ 2 × dt`。 |
| **Failure Criteria** | 若 2b 的 `oscillation_frequency < 0.05 /s`（即 Arbitration 自己就稳定）⇒ 说明 chosen utility gap 不够小，**实验无判别力**，须缩小 Δu 重做；若 2c 无饥饿 ⇒ 说明任务模型未真正竞争同一资源，须修正 fixture。 |
| **Telemetry** | 需记录每次仲裁的 `Decision + Reason`（判给谁、依据哪个效用/优先级）。2c 的 trace 必须能显示某 Task 长期 Pending **且从未 Cancelled**——这正是 Phase 1 §3.3 的 Intent 签名（"表面健康但功能从未生效"）。 |

> **判别力**：本实验与 E3 构成对照——**同一冲突强度下，Arbitration 与 Ownership 的指标差异**是本套实验的第一次关键裁决点。

---

### E3 · Ownership

| 段 | 内容 |
|---|---|
| **RQ** | Ownership 能否使非法源**停止生效写入**？Reject 的判定位置（写入口 vs 决策器）是否影响结果？ |
| **Hypothesis** | **H-F 的核心**：Ownership + 写入口 Reject 可把 `unauthorized_writes` 降到 0 并把 `native_overrides` 降到 0；仅在决策器侧自查则无效。 |
| **Test** | `E0.Movement` 默认 Owner=Ns。<br>**3a 非 Owner 直写**：Lab 未 Acquire 直接写 → 期望被拒并记录 violations。<br>**3b Reject 在写入口**：Lab Acquire(level=5, TTL=10 s) → Ns 持续评估并尝试写（含 gated 路径）→ 期望写入口拦截。<br>**3c Reject 在决策器**：同 3b，但只在 Lab/Ns 的决策函数里判断"我是不是 Owner"，**写入口不做校验**。<br>**3d Transfer**：Lab Release → Ns 重新 Acquire → 断言转移原子性。<br>**不变式断言**：每个虚拟时刻 `count(Owner of Movement) ≤ 1`，违反即记录 `auth_invariant_violation`。 |
| **Metric** | `unauthorized_writes`、`native_overrides`、`authority_transfers`、`order_changes_per_second`、`oscillation_frequency`、`auth_invariant_violation`（自定义，须为 0）。 |
| **Success Criteria** | ① 3a `unauthorized_writes ≥ 1` 且每条都有 RejectReason（**"能看到违规行为"本身就是能力**）；② 3b `unauthorized_writes == 0` 且 `native_overrides == 0` 且分级为**稳定**；③ 3c 出现 `unauthorized_writes > 0`（证明决策器侧校验不够）；④ 3d `auth_invariant_violation == 0` 且 `reaction_latency ≤ 2 × dt`。 |
| **Failure Criteria** | 若 3b 与 3c 指标相同 ⇒ **本次 Ownership 实现位置无差别**，须明确写出原因（多半是 Ns 绕过了写入口直接改状态），否则 E3 废。若 3d 出现 0 owner 或 2 owner 的帧 ⇒ 转移非原子，记为架构缺陷。 |
| **Telemetry** | 3b 必须给出 §1.4 格式的样例；每条拒绝必须带 `OwnerAtWrite` 与 `WriterId` 的对比，否则无法归因。 |

---

### E4 · Lease（expiry / renewal / release / lost owner）

| 段 | 内容 |
|---|---|
| **RQ** | Lease 是否解决"持有者消失"？没有 Token 配套的 Lease 会漏掉什么？ |
| **Hypothesis** | **H-F 的配套部分**：Lease 单独能解决"拿不回来"，但**必须配 Fencing/Token** 才能拦住"假死后复活"的持有者的在途写入（`04` §4.3：未配套者只是 efficiency lock，不是安全锁）。 |
| **Test** | TTL = 4 s，Renew 周期 = 1.5 s。<br>**4a expiry**：Lab Acquire 后停止 renew → 观察所有权何时回收。<br>**4b renewal**：正常 renew → 期望长期持有且指标稳定。<br>**4c release**：显式 Release → 期望立即回收 + Ns 恢复。<br>**4d lost owner（关键）**：Lab Acquire → 在虚拟时间上"停滞 6 s"（不发 Renew **也不发 Release**，模拟 GC pause / Mod 卡死）→ Ns 接管并写入 → Lab 苏醒并携带**旧 LeaseId/Token** 再写。分别跑 **有 Token 校验** 与 **无 Token 校验** 两个变体。 |
| **Metric** | 所有权回收时延、`authority_transfers`、`stale_command_execution`、`release_recovery_failures`、`oscillation_frequency`。 |
| **Success Criteria** | ① 4a 回收时延 ≤ `TTL + 2 × dt`；② 4b 全程 Owner 不变且分级稳定；③ 4c 回收时延 ≤ `2 × dt` 且 `release_recovery_failures == 0`；④ **4d 无 Token 变体中 `stale_command_execution ≥ 1`（漏洞被复现）**；**4d 有 Token 变体中 `stale_command_execution == 0`（漏洞被堵住）**。 |
| **Failure Criteria** | 若两个 4d 变体结果相同 ⇒ 要么停滞模拟失败（Ns 没真的接管），要么 Token 未真正参与写入口判定 ⇒ **本实验失去判别力**，须修好后重跑。若 4a 永不回收 ⇒ 该架构等于永久所有权，直接登记为"Release 失败 → 资源锁死"（`04` §4.3 表第 2 行）。 |
| **Telemetry** | 4d 的 trace 必须能显示"同一 WriterId 在 t1 与 t2 携带不同 LeaseToken，第二次被拒"——这是 fencing 的最小证据。 |

---

### E5 · Preemption 恢复语义（Resume / Restart / Cancel）

| 段 | 内容 |
|---|---|
| **RQ** | 被抢占的旧任务，应该 Resume / Restart / Cancel 哪一种？依据是什么？ |
| **Hypothesis** | **H-G**：**选择依据是"执行体是否持有宿主内部状态"，而不是"哪个更快"**。幂等 sink 类任务 Cancel 安全；持有宿主内部状态的任务 Cancel 会留下残留。<br>（依据 `04` §4.4：实时系统 Mode Change 协议——Immediate/Bounded/Phased 的选择理由是 *"in order to preserve data consistency"*，非性能。） |
| **Test** | 两类被控对象：<br>**Class S（幂等 sink）**：写入即终值，中断不留内部状态（对应 Movement 目标值语义）。<br>**Class T（持状态）**：被写时会创建内部定时器/子状态，且该状态在 Ns 侧持续 tick（对应 `02` 已证的 Arrangement 双 Timer、`GetSubstituteOrder` 兜底回写这类**常驻自维护**路径）。<br><br>三种语义 × 两类对象 = 6 组，每组跑 `抢占 → 执行 → Release → 观测 Ns 是否回到基线`，观测窗口 `2 × T_eval`。 |
| **Metric** | `release_recovery_failures`、`BehaviorState 残留计数`（自定义：重点 observing T 类对象的内部定时器是否仍在运行 / 是否重复创建）、`commands_per_second` 增量、`average_command_lifetime`。 |
| **Success Criteria** | ① (S, Cancel)：`release_recovery_failures == 0` 且无残留；② (T, Cancel)：`release_recovery_failures ≥ 1`（**预测并被证实** → H-G 成立）；③ (T, Resume) 与 (T, Restart)：`release_recovery_failures == 0`；④ Restart 相对 Resume 的 `commands_per_second` 增量 ≥ 20%（重放的成本必须被量化，不能只说"会多一点"）。 |
| **Failure Criteria** | 若 (T, Cancel) 也无残留 ⇒ **H-G 被推翻**，应记录为"该模型下 Cancel 是安全的"，并回头检查 Class T 是否真的持有状态（很可能状态建模不足）→ 此时不得宣称结论，须改进 fixture 重跑。若 Resume/Restart 也失败 ⇒ 说明 Release 语义本身缺失清理步骤，登记为 Phase 6 must-fix。 |
| **Telemetry** | 残留必须可查：Release 后 trace 里若仍出现源发自 Preempted Task 的写入，即为残留证据（`OriginalMethodExecuted` 字段在此关键）。 |

---

### E6 · Generation / Fencing

| 段 | 内容 |
|---|---|
| **RQ** | Gen10 时代发出的旧 Command/Proposal，能否在 Gen11 生效？校验该放在哪一层？ |
| **Hypothesis** | **H-J**：只有**写入口**侧的 Generation/Token 校验能把 `stale_command_execution` 压到 0；决策器侧校验做不到（因为在途指令已离开决策器）。 |
| **Test** | Task T12 在 Gen=10 发出 `MOVE_A`，在飞行途中 Generation 升到 11（由抢占/Release 触发）。三个变体：<br>**6a 无 Generation 机制**（对照）<br>**6b 仅在决策器侧校验**（写入前先看 Gen，但写入口不拦）<br>**6c 仅在写入口校验**（持有 token/generation 比对，落后即拒）<br>每个变体：让命令的交付延迟从 `0.1 s` 逐步拉长到 `2.0 s`（虚拟延迟队列），覆盖不同竞争窗口。 |
| **Metric** | `stale_command_execution`（**核心，成功架构须恒为 0**）、`order_changes_per_second`、`unauthorized_writes`。 |
| **Success Criteria** | ① 6a `stale_command_execution ≥ 1`（漏洞可复现，且在延迟 ≥ 某阈值后必现，需给出该阈值）；② 6b 在**至少一个**延迟档位出现 `stale_command_execution ≥ 1`（证明决策器侧不够）；③ 6c **所有**延迟档位 `stale_command_execution == 0`。 |
| **Failure Criteria** | 若 6b 与 6c 同数 ⇒ 本实验未真正产生"在途"语义（命令被同步执行了），须引入真实的延迟队列重做。若 6a 也不出现 stale ⇒ fixture 无效。 |
| **Telemetry** | 每条 stale 尝试必须记录 `LeaseToken`/`Gen` 与 `max_seen` 的对比值——**没有这两个数的 trace 无法归因**。 |

---

### E7 · Oscillation 五手段对照（**本套实验的核心裁决点**）

| 段 | 内容 |
|---|---|
| **RQ** | Hysteresis / Min lifetime / Lease(Ownership) / State / Priority 五种手段，各自解决什么？哪一个才真正消除冲突？ |
| **Hypothesis** | **H-F**：**只有 Ownership+Lease 能把 `unauthorized_writes` 降到 0**；其余手段只能把 `oscillation_frequency` 压到"看起来不高"，本质是改变表观而非消除写入者。<br>**可证伪推论**：若某非 Ownership 手段也能把 `unauthorized_writes` 压到 0，则 Phase 1 §0 的核心论断（"排序机制只能解决 Command Conflict"）被推翻，`01` 须修订而非维护。 |
| **Test** | 固定冲突强度（同 E1/E2 的 Scenario），Ns 启用 full 行为（gated + non-gated）。六组：<br>**7a 无措施**（基线，等同 E1）<br>**7b Hysteresis**（迟滞带 15%，承接 `02` 的 `PreserveExpireTime` 语义）<br>**7c Min lifetime**（命令最短驻留 2.0 s）<br>**7d Ownership + Lease**（含写入口 Reject + Token，等同 E3 3b）<br>**7e State 去抖**（状态只在满足显式转移条件时变更，等同 BT Memory Node 语义：`04` §3.1.2）<br>**7f Priority 提高 / 提高写入频率（对照，红线演示组）**<br>每组跑 60 s + 300 s 两档。 |
| **Metric** | 全指标，**以 `unauthorized_writes` 为唯一硬判据**；辅助看 `oscillation_frequency`、`order_changes_per_second`、`average_command_lifetime`、稳定性分级。 |
| **Success Criteria** | ① 仅 7d 满足 `unauthorized_writes == 0` 且分级=稳定；② 7b/7c/7e 的 `oscillation_frequency` 相对 7a **下降 ≥ 50%** 但 `unauthorized_writes > 0`；③ **7f 的 `oscillation_frequency` 下降但 `unauthorized_writes` 不下降**——这是"被竞速掩盖"的可辨识证据，与 Phase 1 §5 诊断表第 2 行一致；④ 所有组 300 s 结果与 60 s 分级一致（无疲劳效应）；若不一致，以 300 s 为准并记录分化点。 |
| **Failure Criteria** | 若出现第二个让 `unauthorized_writes == 0` 的非 Ownership 手段 ⇒ **不得掩饰**，直接登记为 Phase 1 §0 论断的反例，Phase 11 必须处理。若 7d 也做不到 0 ⇒ 说明 Reject 未落到实处（回到 E3 3b/3c 的位置问题）。 |
| **Telemetry** | 六组必须产出**同格式、可直接并排比较**的指标表；每组附一条代表性 DecisionTrace。 |

---

### E8 · Domain Ownership 三级递进

| 段 | 内容 |
|---|---|
| **RQ** | Movement / Facing / Arrangement 是否应有**独立** Authority？整实体授权与分域授权的差别是什么？ |
| **Hypothesis** | **H-C 的弱证据版**：分域授权能让"未接管域"继续由 Ns 正常驱动（不产生副作用）且"已接管域"达标稳定；整实体授权会产生跨域副作用（接管 Movement 时意外压制/释放 Arrangement 的自维护）。<br>**⚠️ 强约束**：本实验是**仿真内的证据**，**不得**用于回答 Phase 10 的宿主实测问题（`02` §8.1 与 HANDOFF §10.6 明确要求 Telemetry 实测）。本文只提供"如果宿主行为如 Ns 模型所述，则……"的条件式预测。 |
| **Test** | 三级递进，Ns 启用 gated + non-gated 双路径：<br>**8a**：`Movement=Lab, Facing=Native, Arrangement=Native`<br>**8b**：`Movement+Facing=Lab, Arrangement=Native`<br>**8c**：`Full=Lab`（三域全接管）<br>每级跑：Acquire → 稳态 20 s → Release → 观测 `2 × T_eval`。并在 8a 中额外断言：**Arrangement 的 non-gated 自维护写入是否仍在发生**（这是 Ns 模型中对 `02` 门控矩阵的复现）。 |
| **Metric** | 各域分别统计 `native_overrides`、`lab_overrides`、`unauthorized_writes`、`release_recovery_failures`、跨域副作用计数。 |
| **Success Criteria** | ① 8a/8b 中被 Lab 接管的域分级=稳定（`unauthorized_writes==0`）；② 8a 中被保留给 Ns 的域 `lab_overrides == 0`；③ 8a/8b Arrangement 域观察到 non-gated 自维护写入仍在发生（**复现 `02` 门控矩阵，证明 Ns 模型保真**）；④ 三级 `release_recovery_failures` 随接管面扩大**单调不减**（接管越多恢复越难，须给出每级数值）。 |
| **Failure Criteria** | 若 8a 的 Arrangement 域没有出现 non-gated 写入 ⇒ **Ns 模型与 `02` 不符**，E8 的预测力作废，必须先按 `02` 修正 Ns，再重跑 E8（并重跑受 Ns 影响的 E2/E3/E5/E7）。若 8c 的 `release_recovery_failures` 反而小于 8a ⇒ 说明恢复逻辑与接管面无关，须解释。 |
| **Telemetry** | 按域分表；每次 Release 后的观测窗口内必须列出"哪些域回到基线、哪些没有、为什么"。 |

---

## 5. 扩展实验（E9–E11）

### E9 · Replace vs Append Writer（承接 Phase 3 H-B 修正）

| 段 | 内容 |
|---|---|
| **RQ** | 向既有控制链插入 Lab 时，"替换原 Writer"与"并列新增 Writer"的差别能否量化？ |
| **Hypothesis** | 延续 `03` 修正后的 H-B：替换式插入保持 `unauthorized_writes` 低且 Writer 净增为 0；并列式插入即使逻辑正确也会引入第二个写入源。 |
| **Test** | **9a Replace**：在 Ns 的决策→写入路径上替换产出（仿真语义等价于 Phase 10 的 Harmony `Prefix return false`）。**9b Append**：保留原路径，在其后并列追加一个 Lab 写入者，并尝试更高频/更高优先级。两者跑同一 Scenario（同 E7 冲突强度）。 |
| **Metric** | `unauthorized_writes`、`native_overrides`、Writer 净增、`order_changes_per_second`、稳定性分级。 |
| **Success Criteria** | 9a `unauthorized_writes == 0` 且 Writer 净增 = 0；9b `unauthorized_writes > 0` 或 Writer 净增 ≥ 1，且分级劣于 9a。 |
| **Failure Criteria** | 若 9b 与 9a 同指标 ⇒ **Phase 3 的 H-B 修正失去实验支撑**，`03` 中"替换而非并列"的原则降级为未验证主张，须在 Phase 11 明确标注其证据状态。 |

### E10 · Value Transform 层的能力边界（承接 Phase 3「第六类冲突」异议）

| 段 | 内容 |
|---|---|
| **RQ** | 只改值、从不抑制原方法的变换层（BT Decorator 语义 / RBM `OverrideSetMovementOrder` 语义），能否解决任何一类冲突？它是"冲突类别"还是"正交机制"？ |
| **Hypothesis** | `04` §6.1 的立场：Transformation **不解决任何一类冲突**（它不改变"谁在写"），因此应登记为**与 Authority 正交的第四种机制**，而非 Phase 1 的第六类冲突。 |
| **Test** | 在写入口前置一个变换层：只把 `input` 值改写为另一个合法值，永 远 `return true`（不抑制），带异常兜底。跑三组：**10a 单写者**（无冲突，检验功能性）、**10b 双写者冲突**（检验是否降抖动）、**10c 与 Ownership 组合**（检验正交性：能否共存且互不干扰）。 |
| **Metric** | 10a：终值是否符合预期（功能正确性）；10b：`unauthorized_writes`、`oscillation_frequency`；10c：Ownership 指标是否被 Transform 破坏。 |
| **Success Criteria** | ① 10a 功能成立（值确实被改写）；② **10b 的 `unauthorized_writes` 与无 Transform 时相同**（即未解决权限问题）；③ 10c Ownership 的 `unauthorized_writes` 仍为 0（两者正交、可共存）。 |
| **Failure Criteria** | 若 10b 的 `unauthorized_writes` 下降到 0 ⇒ **Transform 确实能解决权限类冲突**，`04` §6.1 的降格建议被推翻，应恢复"第六类冲突"的候选地位并提交 Phase 11 裁定。 |

### E11 · Blackboard vs Command Bus 语义（验证 `04` C7 / **H-H**）

| 段 | 内容 |
|---|---|
| **RQ** | Phase 1 第 5 类冲突的深层成因，是否真的是"宿主用 Blackboard 语义、Mod 期望 Command Bus 语义"的风格错配？ |
| **Hypothesis** | **H-H**：在 Blackboard 语义（多源共享可变状态、无 owner、无单点入口）下，**归因不完整**——存在"值变了但 trace 里查不到谁改的"事件；切换到 Command Bus 语义（单一权威写入口、所有变更经过入口并记录）后，归因完整率 100%。 |
| **Test** | 同一组 Source 与同一 Scenario，两种底层状态语义：<br>**11a Blackboard**：Source 直接写共享状态；观测方只能在事后比对快照。<br>**11b Command Bus**：所有变更经单一入口，入口记录 writer+reason+token。<br>两组注入**完全相同的写入序列**（保证可比）。 |
| **Metric** | **归因完整率** = (能查到 writer 与 reason 的变更数) / (总变更数)；`native_overrides` 的可辨识率；平均归因耗时（trace 检索步骤数）。 |
| **Success Criteria** | 11a 归因完整率 < 100%（存在无法归因事件，需给出具体条数与样例）；11b 归因完整率 = 100%。 |
| **Failure Criteria** | 若 11a 也是 100% ⇒ 说明观测层已经隐式承担了 command bus 的职责（很可能你在写影子日志），须去掉观测后门重做。**注意：这不是可取巧的地方——归因能力正是 Phase 3 反复强调 Telemetry 的本意。** |

---

## 6. 判别力矩阵（哪些实验能区分哪两个机制）

| 要区分的两者 | 承担实验 | 关键判据 |
|---|---|---|
| LWW vs Arbitration | E1 vs E2 | 重放 hash 与 Ram 结果是否依赖遍历顺序 |
| **Arbitration vs Ownership** | **E7（7b/7c/7e vs 7d）** | **`unauthorized_writes` 是否为 0** |
| Reject 在写入口 vs 在决策器 | E3（3b vs 3c） | 两者的 `unauthorized_writes` 差异 |
| Lease 单独 vs Lease+Token | E4（4d 两变体） | `stale_command_execution` |
| Fencing 在写入口 vs 在决策器 | E6（6b vs 6c） | `stale_command_execution` 是否为 0 |
| **Resume vs Restart vs Cancel** | **E5（6 组矩阵）** | `release_recovery_failures` 与 commands 增量 |
| 迟滞 / MinLife / State 三者之间 | E7（7b/7c/7e） | `oscillation_frequency` 的量级差（但**均不能降 `unauthorized_writes`**） |
| 整实体授权 vs 分域授权 | E8（三级递进） | 跨域副作用与 `release_recovery_failures` 的单调性 |
| 替换 Writer vs 并列 Writer | E9 | Writer 净增与 `unauthorized_writes` |
| Transform 是机制 vs 是冲突类 | E10 | `unauthorized_writes` 是否因它下降 |
| Blackboard vs Command Bus | E11 | 归因完整率 |

> **本矩阵的用途**：Phase 8 的 A/B/C 对比**必须引用本矩阵中的实验编号**作为证据，不允许用"理论上应当更好"下结论。

---

## 7. 红线自检表

| 红线（HANDOFF §7） | 本计划如何防范 | 检查点 |
|---|---|---|
| 用真实战术做基础实验 | §1.2 命名表 + 专门的禁止清单 | E1–E11 每个条目的 fixture 复核 |
| 靠"每 Tick 重复 SetOrder"压过 Vanilla | **E7 的 7f 组是"反例演示组"**：必须跑出"频率提高后表面变稳但 `unauthorized_writes` 不降"，并**明确标注该组不得作为成功方案** | E7 Success Criteria ③ |
| 靠"谁写得更快"实现 Authority | 同上；Ns 的写入频率在所有权实验中固定为 `04` §1.3 的建模值，不允许用频率调节充当方案 | E3/E7 配置冻结 |
| 无限叠加 Priority | E2 的 2a/2b 明确要求展示规则失效（`task_starvation`、噪声抖动），不允许以"调参到位"通过 | E2 Failure Criteria |
| 永久关闭 Vanilla AI 后宣称 Authority 已解决 | 每个含 Release 的实验（E3 3d / E4 4c / E5 / E8）都必测 `release_recovery_failures` | E3/E4/E5/E8 |
| 没有 Telemetry 就推测 Writer | §1.4 强制全字段写入尝试记录；被拒写入也必须记录 | 全实验；尤其 E11 |
| 预设 Proposal/Arbiter/Executor 正确（D5） | 本计划**不强制**实现 Proposal/Executor；仅 E2 需要"Arbitration 概念"，若某方案无需 Proposal 即可达标，**允许得出"Proposal 不必要"的结论** | E2/E7 |
| 因为 RBM 用了某方案就当最佳实践（H-B） | E9 专门把它做成可证伪实验，并列明失败处理 | E9 Failure Criteria |

（Phase 7 执行时请对照 HANDOFF §7 红线全表，本表只列与本计划强相关的 8 条。）

---

## 8. Phase 6 施工清单（本文档推导出的最小抽象集）

**原则（`04` §6.4 + HANDOFF §5 Phase 6）**：不为架构完整而提前堆抽象。**下表按"哪些实验逼着你必须实现"排序，未列入的一律不许实现。**

| 抽象 | 被哪些实验逼出来 | 必须性 |
|---|---|---|
| `VirtualClock` / 虚拟时间调度 | 全部 | **必需** |
| `ControlledEntity` + `CommandDomain`（3 域） | 全部 | **必需** |
| `Command`（中性值）+ 写入口（`WritePort`） | E1/E3/E6/E11 —— E3 与 E6 的实验成立**全靠"存在单一写入口"** | **必需** |
| `AuthorityOwner` / Lease（含 **单调 Token**） | E3/E4/E6 | **必需** |
| `AuthorityService`（Acquire / Renew / Release / Transfer，**含 Level**） | E3/E4（Level 用于 Phase 8 的 H-I，本阶段先支持 Level 5 与 Transfer） | **必需** |
| `Task` + 生命周期（Pending/Active/Preempted/Done/Cancelled）+ 终止条件 | E2 2c / E5 / E6 | **必需** |
| `Intent`（带终止条件） | E2 2c / E5 | **必需** |
| `DecisionTrace` + Telemetry 采集 + 指标计算 | 全部 | **必需** |
| `NativeSource`（Ns）含 **gated / non-gated 双写入路径** | E2/E3/E5/E7/E8 | **必需** |
| Scenario 脚本 + seed + `replay_hash` | D0 / 全部 | **必需** |
| `Proposal` / `Executor` | **无实验强制需要** | **候选，不实现**——若 E2/E7 显示 Proposal 无收益，结论"Proposal 不必要"是被允许的（D5） |
| `PreemptionPolicy`（Resume/Restart/Cancel 三选一） | E5 | **必需**（`04` §6.4 第 1 条：必须显式建模 Transition 语义） |

**两条实现硬约束（来自 `04`）**：

1. **Reject 与 Token 校验必须实现在 `WritePort` 内部**，不允许放在 Source 的决策函数里（否则 E3 3b/3c 与 E6 6b/6c 失去判别力）。
2. **Authority 必须按 Level 声明**（至少能表达 "无 / 独占" 两极），不得实现为布尔 `HasAuthority`（否则 Phase 8 的 H-I 无法验证）。

---

## 9. 遗留与不确定性（明确登记）

| # | 遗留项 | 影响 | 处理建议 |
|---|---|---|---|
| 1 | **Ns 是模型不是实测** | E2/E3/E5/E7/E8 的全部结论都带"若宿主行为如 Ns"的前提 | Phase 10 必须用真实 Telemetry 复核 Ns 参数（尤其 non-gated 路径） |
| 2 | **稳定性分级的阈值（0.05 / 0.5 / 0.2 / 2×T_eval）是设定值，不是推导值** | 若阈值选得不当，分级会失真 | Phase 7 先用 E1 的分布校准；若 E1 基线本身落在"临界"，须重设阈值并在 `06` 中说明理由 |
| 3 | E5 的 Class T 是否真的代表"持有宿主内部状态"，取决于建模深度 | 直接影响 H-G 结论 | 若 (T, Cancel) 无残留，先怀疑 fixture 不足，不要急着宣布 H-G 被推翻 |
| 4 | E8 不得越权回答 Phase 10 的问题 | 越权会污染 `02` §8.1 的待验证状态 | `06` 中 E8 结论必须写成**条件式预测** |
| 5 | E2/E7 的 noise / utility gap 参数需要预调才能真正显现出"抖动" | 参数不当则实验无判别力 | 设 Failure Criteria 强制重做（已写入条款） |
| 6 | 本文档所有阈值为**先验设定**，尚未被任何数据校准 | — | Phase 7 第一次运行时就应先校准再采集 |
