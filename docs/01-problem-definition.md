# 01 · 问题定义：Tactical Command 中的五类冲突

> Phase 1 交付物 · 2026-09-19
> 状态：**已完成（纯文档，零代码）**
> 用途：为 Phase 5（实验设计）、Phase 7（8 组实验）、Phase 12（CombatBrain 归因）提供可判定的问题模型。

---

## 0. 本文档的核心论断（先给结论）

**排序机制（priority / utility / arbitration）只能解决「Command Conflict」一类冲突。其余四类都是二元或结构性问题，用连续权重去解必然产生抖动、饥饿或归因错误。**

这一论断是可检验的：本文为每一类冲突给出独立的 **Telemetry 特征签名**，使后续可以用数据回答"某次异常到底属于哪一类"，而不是凭直觉归类。

若后续实验推翻该论断（例如发现 Authority Conflict 也能被加权机制稳定解决），本文档应被修订而非维护。

---

## 1. 决策栈分层模型

五类冲突不是平级的五个"情况"，而是分布在决策栈的五个层次上。先立模型，再谈冲突。

| 层 | 名称 | 处理单元 | 时间尺度 | 该层回答的问题 |
|---|---|---|---|---|
| L4 | **Pipeline**（宿主控制链） | 注入点 / 抑制范围 | 永久（结构性） | 我是否在对方的控制结构内？ |
| L3 | **Plan**（计划 / 约束） | 跨实体约束集 | 阶段 / 整场 | 各局部决策组合起来是否自洽？ |
| L2 | **Intent / Task**（目标 / 承诺） | 任务（含终止条件） | 任务周期（秒~分钟） | 这个目标还要不要？ |
| L1 | **Command**（具体命令） | 一次写入值 | Tick | 这一刻写什么？ |
| L0 | **Entity / Domain**（资源与权限域） | Domain Lease | 秒~分钟 | 谁被允许写？ |

**层间关系**：L3 产生/约束 L2，L2 派生 L1，L1 需要 L0 的许可才能落到实体。L4 是宿主自己的一套完整 L0–L3，与 Mod 的那套并行存在且互不感知。

**关键推论**：L1 的冲突不能在 L1 解决干净，因为它的成因可能在 L0/L2/L3/L4。这就是"抢指令"久治不愈的结构性原因——症状出现在 L1（Order 变了），病根往往在 L0 或 L4。

---

## 2. 术语表（后续 Phase 统一口径）

| 术语 | 定义 | 备注 |
|---|---|---|
| **Entity** | 被控制对象（实验中为中性 `ControlledEntity`，实战为 Formation） | 中性抽象 |
| **CommandDomain** | Entity 上可独立授权的一组状态（Movement / Facing / Arrangement） | Ownership 的原子单位 |
| **Command** | 对某 (Entity, Domain) 的一次具体写入值（HOLD/ADVANCE/RETREAT） | L1 |
| **Intent** | 一个带终止条件的目标（ENGAGE / DISENGAGE） | L2 |
| **Task** | Intent 的运行时实例，含生命周期（Pending/Active/Preempted/Done/Cancelled） | L2 |
| **Writer** | 实际执行写入的代码路径 | 可为 Native 或 Mod |
| **Owner** | 当前被授权写某 (Entity, Domain) 的控制源 | L0 |
| **Lease** | Owner 身份的时间边界（Acquire / Expiry / Renew / Release） | L0 |
| **Authority** | "是否被允许写"的二元属性，与"写得对不对"正交 | L0 |

---

## 3. 五类冲突

### 3.1 Command Conflict（命令冲突）

**定义**：同一时刻、同一 (Entity, Domain) 上存在 ≥2 个**不同的具体写入值**，且这些值来自合法候选源。

**性质**：偏好冲突（preference）。候选之间可以比较优劣。

**触发条件**
1. ≥2 个决策源在同一 Tick 对同一 Domain 产出不同 Command；
2. 且两者都持有或被假定持有写权限（权限本身不是争议点）。

**最小复现**（中性对象）

```text
Tick 10   Entity=F0   Domain=Movement
  Source A → ADVANCE   (utility 0.71)
  Source B → RETREAT   (utility 0.64)
无仲裁 → Last Writer Wins → 结果取决于执行顺序，不可复现
```

**为什么不能用其他机制解决**
- 用 **Authority/Ownership**：需预先指定唯一 Owner，等于取消多源决策能力；Owner 不覆盖的域会退回无仲裁状态。
- 用 **Plan 一致性检查**：单个 ADVANCE 与单个 RETREAT 各自都不违反任何全局约束（若无 Plan），检查没有输入，恒为通过。
- 用 **Native 抑制**：与宿主链无关，无从下手。

**适用机制**：确定性 Arbitration（优先级 / Utility / 投票 / 元策略）。**硬性要求：同输入必须同输出**，否则不可复现、不可调试。

**失败模式（误配）**：用写入顺序隐式仲裁 → 非确定性；用无限叠加 priority 规则 → 规则爆炸且不可解释。

**Telemetry 特征签名**

```text
unauthorized_writes == 0
同一 Tick 内合法 Proposal 数 >= 2
重放（改变源遍历顺序）→ 得到不同最终 Order
```

---

### 3.2 Authority Conflict（权限冲突）

**定义**：对同一 (Entity, Domain)，存在 ≥2 个**都认为自己有权写**的控制源，且系统只能"覆盖写"，无法使其中一方**停止写**。

**性质**：权限冲突（permission），**二元，不可加权**。

**与 Command Conflict 的分界（关键点）**：
> Command Conflict 的前提是"多个**合法候选**，选一个"。
> Authority Conflict 问的是"哪些源**根本不该成为候选**"。
> **仲裁的前提是参与者合法。** 对不合法的参与者做仲裁，等于每 Tick 重复一次注定无意义的投票。

**触发条件**
1. 外部 Writer（Mod）与原生 Writer 同时写同一 Domain；
2. 系统只能覆盖写，不能令对方不再写。

**最小复现**

```text
T=0.0   Native Behavior:Advance → SetMovementOrder(ADVANCE)
T=0.1   Lab              → SetMovementOrder(HOLD)      ← 覆盖
T=1.0   Native 重新评估  → SetMovementOrder(ADVANCE)   ← 覆盖回来
T=1.1   Lab              → SetMovementOrder(HOLD)
...
→ order_changes_per_second 飙升，实际行为在两值间持续抖动
```

**为什么不能用其他机制解决**
- 用 **Priority 仲裁**：输的一方下个 Tick 还会再提，仲裁每 Tick 重跑 → 抖动。**这不是仲裁失败，是问题问错了**：问的是"谁的值更好"，实际问题却是"谁不该写"。
- 用 **更高频写入 / 放宽 CPS**：竞速，不是 Authority。（红线）
- 用 **Plan 一致性**：与全局约束无关。

**适用机制**：Ownership + Domain 划分 + 显式 Acquire/Release + Lease + **对非 Owner 写入显式 Reject 并记录**。

**失败模式（误配）**：把权限问题当偏好问题 → 抖动 → 团队加 CPS 限流 → 只是降低抖动频率、未解决权限 → 症状被掩盖而非消除。**可判别特征：提高写入频率后"看起来变稳"。**

**Telemetry 特征签名**

```text
unauthorized_writes > 0
oscillation_frequency ≈ 冲突源的评估频率（或两者周期的最小公倍数）
average_command_lifetime ≈ 原生评估周期（而非任务周期）
```

---

### 3.3 Intent Conflict（意图冲突）

**定义**：两个**目标**不可同时成立（接敌 vs 脱离），即使它们不在同一 Tick 写入。

**性质**：承诺冲突（commitment）。注意：**它不必然是优先级问题**——这正是它易被误诊的原因。

**触发条件**
1. 两个 Task 的终止条件互斥；
2. 且两者同时挂载在同一 Entity（或共享同一资源）。

**最小复现**

```text
Task T1 (Source A): Intent=ENGAGE,    终止条件 = 距敌 < 20
Task T2 (Source B): Intent=DISENGAGE, 终止条件 = 距敌 > 100
两者同时 Active → 无论仲裁判给谁，另一个的终止条件永远无法满足
→ 若优先级判给 T1：T2 永久 Pending，Source B 每 Tick 重新提交（除非有去重）
```

**为什么不能用其他机制解决**
- 用 **Priority / Arbitration**：只能决定此刻谁写，不能决定"另一个目标怎么办"。**压制 ≠ 取消** → 产生 zombie intent（既没被满足、也没被取消的目标）。
- 用 **Authority**：Owner 同样无法同时满足两个互斥终止条件，权限不解决目标矛盾。
- 用 **Plan 一致性**：Plan 层可以发现矛盾，但解决手段在 Plan 层（重规划/加约束），不是在 Intent 层加权重。

**适用机制**：Task 生命周期管理 + **显式 Cancellation 并回执**（让源知道目标被放弃了）+ 必要时 Replan。Arbitration 在此层只能做"临时让位"，不能做"目标消解"。

**失败模式（误配）**：长期 starvation；表现为"系统看起来稳定（无抖动、无报错），但某个功能从来没真正生效过"。

**Telemetry 特征签名**

```text
unauthorized_writes == 0   且  oscillation_frequency ≈ 0（表面健康）
task_starvation > 0：某些 Task 长期 Pending/Preempted，从未 Complete 也从未 Cancelled
average_command_lifetime 短，但对应 Task 的存活时间极长
```

---

### 3.4 Plan Conflict（计划冲突）

**定义**：单个决策**局部合理**，但其组合破坏全局计划（时序 / 资源 / 协同约束）。

**性质**：**一致性 / 约束满足**问题，不是排序问题。（HANDOFF 已点明：这是 Planning Consistency，不是 Command Arbitration。）

**触发条件**
1. 存在跨实体或跨时间的约束（如"F0 必须固守至 F1 完成侧翼机动"）；
2. 局部决策者看不到该约束，或看到了但没有否决/上报通道。

**最小复现**

```text
Plan: F0 = HOLD 直到 F1 到达 P 点（预计 T=30）；F1 = ADVANCE
T=12：F0 局部评估——敌情逼近 → RETREAT 的 utility 很高，判为合理
执行 RETREAT → F1 侧翼失去支撑 → 整体 Plan 失败
复盘：每一个 DecisionTrace 都"合法且理由充分"
```

**为什么不能用其他机制解决**
- 用 **Command Arbitration**：其输入只有候选命令与局部效用，没有全局约束，无法判断局部最优是否全局有害。
- 用 **Authority**：把 Movement Authority 给谁都解决不了——问题不在谁写，而在于**写的时机破坏了时序约束**。
- 用 **"提高 Plan 优先级让它赢"**：等价于让 Plan 变成全局独裁，取消局部反应能力。正确行为是：局部可以反应，但必须**上报 Plan 并触发重规划**，而不是静默覆盖或被静默覆盖。

**适用机制**：Plan 层 Constraint propagation / Coherence check / Replan trigger；局部决策者需要 **veto 上报通道**，而非直接写入权。

**失败模式（误配）**：每个模块都能自证清白、日志都合理，但整体打输；事后无法归因——因为 trace 里不包含 Plan 上下文。

**Telemetry 特征签名**

```text
单点 DecisionTrace 全部"合理"（Reason 成立、权限合法）
unauthorized_writes == 0，behavior_switch_frequency 局部正常
但全局指标（Plan 完成度 / 协同窗口达成）失败
```

---

### 3.5 Native Pipeline Conflict（原生流水线冲突）

**定义**：宿主引擎存在自己的完整决策链
`Mission → Team AI → Tactic → FormationAI → Behavior → Order → Formation`，
Mod 是链外实体。冲突的实质是"**链内谁才是最终合法 Writer**"以及"**Mod 应在哪一层介入**"。

**性质**：**结构性问题**，不是决策问题。前四类的共同前提是"所有 Writer 都在我的框架内、遵守我的规则"；而原生链的 Writer **既不遵守也不知道**你的规则。

**触发条件**
1. 外部 Writer 与链内 Writer 共存于同一 Domain；
2. 外部 Writer 未被链接纳为合法节点。

**最小复现**

```text
Lab → SetMovementOrder(HOLD)          Order = HOLD
... 期间 Lab 无任何写入 ...
1.2s 后 Native FormationAI 重算 Behavior → SetMovementOrder(ADVANCE)
→ Order 变了，但 Lab 的决策 trace 里没有任何记录
→ 观测者只看到"HOLD 没保持住"，无法回答"谁改的、为什么改"
```

**为什么不能用其他机制解决**
- 前四类机制全部假设"Writer 在我的治理范围内"。原生链 Writer 不在 → 你的 Authority 对它无效，除非**集成进链**或**在写入点拦截**。
- 用每 Tick 重复 SetOrder 压过它 → 竞速，不是 Authority。（红线）

**适用机制**（对应 Phase 8 的 A/B/C，本阶段不预设赢家）
- **A · External Commander**：链外写，靠频率或抑制维持 → 结构与原生并存双 Writer。
- **B · Full Authority Takeover + Harmony 抑制**：持 Domain Authority，拦截原生冲突写。
- **C · Native Pipeline Integration**：Lab 只出 Intent，挂进链内（改 Behavior Weight / 自定义 Behavior / 替换 `CalculateCurrentOrder()`），由原生决定如何执行。

**失败模式（误配）**：以为解决了 1–4 类就自动解决第 5 类 → 实测抖动依旧 → 归因错误地回头去调优先级。

**Telemetry 特征签名**

```text
native_overrides > 0
original_method_executed == true
authority_transfers == 0（从来没有真正取得控制权）
Order 变化发生在 Lab 未发起任何写入的时刻
```

---

## 4. 正交判定表

| 类 | 层 | 冲突性质 | 可加权? | 解决单元 | 时间尺度 | 正确机制 | 误用 Priority 的后果 |
|---|---|---|---|---|---|---|---|
| Command | L1 | 偏好 preference | ✅ 可 | 一次写入 | Tick | 确定性 Arbitration | —（本类就该用） |
| Authority | L0 | 权限 permission | ❌ 二元 | Domain Lease | 秒~分钟 | Ownership + Lease + Reject | 持续抖动 + 每 Tick 重仲裁 |
| Intent | L2 | 承诺 commitment | ❌ 二元 | Task（含终止条件） | 任务周期 | Commitment + Cancel 回执 + Replan | zombie intent / starvation |
| Plan | L3 | 一致性 consistency | ❌ 约束满足 | Constraint Set | 阶段 / 整场 | Coherence check + Replan trigger | 局部最优、全局失败 |
| Native Pipeline | L4 | 结构 structural | ❌ 结构性 | 注入点 + 抑制范围 | 永久 | 集成点选择 + 抑制/接管 | 归因错误、治标不治本 |

---

## 5. 症状 → 类别 诊断表（Phase 7/10/12 直接使用）

| 观测症状 | 最可能类别 | 判别依据（看哪个指标） |
|---|---|---|
| Order 在两个值间高频来回 | Authority（若有外部+原生双写）／Command（若源顺序不定） | `unauthorized_writes` 是否 > 0 |
| **提高写入频率后"变稳"** | **一定是 Authority 未解决**（被竞速掩盖） | Command / Intent 不会因频率改善 |
| 某功能从未生效、但不报错 | Intent（被压制但未取消） | Task 是否长期 Pending 且永不 Complete/Cancelled |
| 每次决策日志都合理，整体打输 | Plan | 单点 trace 无法证伪，需全局指标 |
| **Lab 没写，Order 自己变了** | **Native Pipeline** | `original_method_executed=true` 且 `authority_transfers=0` |
| 重放同一输入得到不同结果 | Command（仲裁非确定性） | Authority/Intent 的失败是确定性的 |

---

## 6. 与「最终 7 个问题」的映射

| # | 问题 | 主导冲突类 |
|---|---|---|
| Q1 | Bannerlord 中谁真正控制 Formation？ | **5 · Native Pipeline** |
| Q2 | 外部 Mod 怎样安全取得控制权？ | **2 + 5**（先有 Ownership 语义，再解决链内集成点） |
| Q3 | 控制权怎样释放并交还 Native AI？ | **2**（Lease/Release 语义）+ **5**（原生链状态是否干净恢复） |
| Q4 | Movement/Facing/Arrangement 是否应有独立 Authority？ | **2** 的粒度问题——Domain 是 Ownership 的原子单位 |
| Q5 | 多决策源用 Arbitration / Ownership / Hierarchy 还是其他？ | **取决于类别分布**：Command→Arbitration，Authority→Ownership，Intent/Plan→Hierarchy+Replan，Pipeline→Integration |
| Q6 | CombatBrain 应独立 Commander 接管，还是集成 Native Pipeline？ | **5** |
| Q7 | "抢指令"里多少是 Command Arbitration，多少是 Authority Architecture？ | **本分类提供的分解方法**：按签名对异常事件归类计数 |

**Q7 的可操作分解方法**（Phase 12 使用）：采集一段时间内所有异常 Order 变化事件，按 §3 各类的 Telemetry 签名逐条归类并计数，得到形如「62% Authority / 24% Native Pipeline / 9% Intent / 5% Command」的量化结论。**这是可测量的，不是拍脑袋的。**

---

## 7. 待验证假设（记录，不作为结论使用）

| ID | 假设 | 验证 Phase |
|---|---|---|
| H-A | CombatBrain 的"抢指令"主要属于类 2 / 类 5，而非类 1 | Phase 12（旧源码复核） |
| H-B | RBM 的稳定性来源于结构性减少独立 Writer（≈ 类 5 的 C 路线），而非更强的仲裁器 | Phase 3 |
| H-C | Movement / Facing / Arrangement 需要独立 Authority | Phase 10（Telemetry 实测，不得沿用旧推论） |
| H-D | Intent 层不可省略，否则必然产生 zombie intent | Phase 7 实验 ③⑤ |
| H-E | 五类冲突的 Telemetry 签名互不重叠（即分类是可判定的） | Phase 7（若重叠，本分类需修订） |

**H-E 是对本文档自身的证伪条件**：若实验发现两类签名不可区分，说明分类维度选错，必须回头修订 §3，而不是强行调阈值。

---

## 8. 本 Phase 明确的「不做」

- 不写任何代码（Phase 6 才是实现）
- 不读 CombatBrain 源码（Phase 12）
- 不查 Bannerlord DLL / 反编译（Phase 2）
- 不对 Architecture A/B/C 下结论（Phase 8）
- 不引用 `CombatBrain.*` / `TaleWorlds.*`（红线 D1）

---

## 9. 对下一 Phase 的输入要求

Phase 2（Bannerlord 原生控制链研究）必须产出以下**可判定**结论，否则 Phase 5 无法设计实验：

1. **Formation 的最终合法 Writer 是谁**（具体到方法名）；
2. Tactic 是否主要通过 Behavior Weight 影响 Formation、更新周期是多少、切换时发生什么；
3. Behavior 的 `CalculateCurrentOrder()` 何时执行、是否直接 SetOrder、非当前 Behavior 是否仍产出命令；
4. Movement / Facing / Arrangement 三个 Order 是否分别维护；
5. 是否存在除 `SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` 之外的写入口。

以上五项直接决定 §3.5 中路线 B（需 Patch 几个点）与路线 C（挂在哪一层）的可行性边界。
