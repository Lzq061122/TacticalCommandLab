# 07 · 最终架构（Final Architecture）

> Phase 11 交付物。日期：2026-10-07（北京时间）。
> 依据：`docs/phase11-design.md`（**开工基线** SHA-256 `042ef0fbc5ed3eaa452cba67bd0b442ed442c33f00c531fe391519b6a3759a74`；**上一轮整改版本** `dd13fbaf97d1642ca14639a9f36320ef7ef096793b7c85a8de465f0aecae54f5`；**当前设计版本** `2c049e6d737f65caf813f655654ca3b12e4ff66ee76302e7fe152d2c8af78358`）、`docs/01`–`docs/06`、`docs/07a`、`docs/10`，以及 `_audit/_local-phase10-20261006/run04`–`run11` 的原始 assessment / manifest / metrics / telemetry。
> 本文件落实设计，不重新选型；不改判据、阈值与历史数值。

---

## 0. 本轮范围：设计定稿 ≠ 运行时能力验收

**本轮交付的是设计定稿与证据边界，不是运行时能力验收。** 二者必须分开读：

| 项 | 本轮状态 | 含义 |
|---|---|---|
| 设计定稿 | ✅ 完成 | 架构选择、职责依赖、生命周期契约、指标口径、七问答案方向已固定 |
| 框架实现 | 🟨 部分存在 | Core / Simulation / Telemetry / BannerlordProbe 已存在，本轮未修改一行运行时源码 |
| 宿主运行时能力验收 | ❌ 未通过 | 只对 T1F0 Movement 单场窗口取得 Full 控制证据；Facing / Arrangement 无独立接管实测；G3 全场 FAIL 保留 |

“Phase 11 基本交付通过”**不等于**：三域接管已验收、全场稳定、所有恢复路径可用、产品集成可行。它表示已有证据能支持的架构选择已落实、未验证能力有明确边界、交接资料可复核（设计 §1）。

本轮没有启动游戏、**未重跑全量工程/宿主构建及测试套件**（首轮使用 `dotnet run` 构建并执行了单个诊断工程，见 §9.3）、没有部署 DLL、没有修改运行时代码、没有读取 CombatBrain。文中出现的 102/102 C#、23/23 Python、D0 15/15、E1–E11 51/51、E12 10/10 全部是 **2026-10-06 已有报告的结果**，不冒充本轮执行。

**实测源码身份**：run04–run11 的 `source_head` 一律为 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0`，配逐场 `git-diff.txt` 与实际加载 DLL 身份。交付时的仓库 HEAD 是 `f8b1f173f05bf054b42eb2e31527deb1a83aa613`，**不得倒写成“测量时运行 f8b1f17”**。

---

## 1. 设计依据与结论边界

### 1.1 先写清楚“证据能支持什么”

| 依据 | 已知事实 | 可以支持 | 不能支持 |
|---|---|---|---|
| E3/E4/E6/E7/E9/E12 + `docs/06`/`07a` | 写入口 Reject、Lease/Fencing、Replace/Append Writer 差异在既有仿真模型中有量化证据 | 框架职责划分、强制点位置、常态 C 与受限 B 的设计选择 | 把 Ns 模型结果写成任意游戏版本的实测事实 |
| run05 H2 | G1=0、G2=100%、T1F0 Movement HOLD 窗口 24.50 s；Native 尝试被拒**共 50 次**（其中落在有效意图窗口 `[3.5,28)` 内为 **49 次**）；G3=Oscillating FAIL | **此实体、此场景、此窗口**取得并保持目标控制 | 全场稳定、所有 Movement 路径完全覆盖、多场景泛化 |
| run06 H2b | G1/G2 PASS、G3 FAIL；T1F0 Arrangement sampled=2 / port=3 / unobserved=0；脚本只 acquire Movement | 没有观察到该计数口径下的缺口 | Arrangement Full 已验证；完整写入覆盖率 = 100% |
| run07 H3 | H3-a=60、H3-b=0、G2=100% 全 PASS；AI 控制关闭后目标窗口 Native Movement 尝试为 0；全场 grade 仍 Oscillating | 当前实验配置下的关联，以及关闭 AI 控制的抑制效果观察 | 路线 C 的 Behavior/Weight 集成已通过；实际 `AI.Tick` / 行为选择调用计数为 0 |
| `current-formation-tick-il.txt` | `AI.Tick` 门控为 `HasTeamAi && (IsAIControlled || IsPlayerSergeant)` | 普通非 PlayerSergeant 情形下关闭控制会跳过该路径的 **`FormationAI.Tick`**（`Formation.Tick` 自身继续执行其余编队簿记，见 IL 原件第 38 行）的静态路径代价 | 任意编队均停 Tick；运行时调用次数已经观测 |
| run08–run11 REL | None/Reapply/Reapply+Reset/AfterRelease 的 REL = 0/0/0/1，最后一项为预期失败 | 原始 Move 重发有效；先丢失所有权再以旧 Lab 身份恢复会被拒；自然恢复可出现 | Reapply 或 Reset 普遍必要；恢复全部内部状态；到期/异常恢复已验收 |

### 1.2 两条必须限定的历史强断言

Phase 8 有两处表述过强，在本文件中限定：

1. **C 不提供独占控制保证**——而不是“任意场景下 C 的 hold 都只能为 0% 或 50%”。E12 的 0%/50% 是**该装置、该竞争者配置**下的观测；`docs/07a` §8 遗留 2 已登记：竞争者优先级为 0 时 L2 与 L4 完全不可区分。C 的结构性结论是“非独占语义不保证独占”，不是某个具体百分比。
2. **A 被否定的是“保留竞争写入后靠频率、优先级和时序抢写”**这一手法——不是“所有外部决策源”。E7 7f（高频重写 attempts 61→600、writes 61→121、osc 2→4，三项全升）与 E9 9b（Append 并列 Writer → 120，Oscillating）证伪的是**并列 Writer 抢写**，不是“Mod 不得影响原生决策”。

---

## 2. 架构选择：A / B / C

### 2.1 三个候选的精确定义（沿用 `docs/07a` §1）

判定用**写入者数量**与**强制点位置**两个可观测变量，不用形容词。

| | A · External Commander | B · Full Authority Takeover | C · Native Pipeline Integration |
|---|---|---|---|
| 写入者数量（相对原生） | 净增 **+1**（与原生链并列写入） | 净增 0（替换产出）或 −1（抑制原生写） | 净增 **0**（出 Intent，原生侧决定执行） |
| 控制权载体 | 无（靠优先级/频率/时序） | **Ownership + Lease + Token**，强制点在写入口 | 无（只有影响力，仲裁权留给原生） |
| 冲突解决机制 | Arbitration / Last-Writer-Wins | **Reject（写入口）** | 原生侧仲裁（权重 / Behavior 选择） |
| 失败模式 | 抖动、饥饿、归因缺失 | 覆盖率不足 → 部分域失控；Release 残留 | **意图不落地**（被原生权重无视） |

### 2.2 本轮选择

**常态走 C；只有在目标域、场景、指令和恢复条件均有证据支持时才升级到受限 B；明确拒绝 A 的抢写做法。**

```
                  ┌─ 常态 ──────── C：在原生控制链中影响/替换决策产出（Writer 净增 0）
  单个 Command ───┤                       依据：E9 9a / docs/03 RBM 案例 / docs/04 C5
    · 单 Domain   │
                  └─ 需要确定控制 ── B：Ownership + Lease + Token + 写入口 Reject（Level 5 Full）
                                          约束：按域、带 Lease、必须可 Release
                                          依据：E7 7d / E3 / E4 4d / E6 6c / E12 ①②
  A 通道：判负，不列入架构选项
  依据：E7 7f（高频重写三项全升）、E9 9b（并列 Writer 劣化一个数量级）
```

四条硬约束（沿用 `docs/07a` §6，本轮未放宽）：

1. **默认走 C，只在需要确定性时升级到 B。** 常态下 Writer 净增必须为 0（E9 9a）；升级必须**按域**（E8 / E12 ⑨）。
2. **B 的升级一律声明 Level 5 (Full) + Lease + Token**，禁止停在 Level 4 (Override) 或 Level 2 (Recommend)（E12 ④⑤⑧，H-I 裁决）。
3. **Reject 与 Fencing 一律落在写入口**，不得放在 Mod 侧决策函数（E3 0 vs 24；E6 0 vs 4；`docs/04` C2）。
4. **Release 必须显式建模恢复语义**（E5：Cancel 会遗留孤儿定时器 → 1 次恢复失败）。B 的“能夺取”与“能安全交还”必须同时验收。

关于 A 被判负的**性质**：不是“较差”，是**被证伪**。E7 7f 表明提高写入频率连表观指标都不改善；7f-stall 进一步证明 Ns 的写入从未被真正阻止。

### 2.3 不把“增加频率”当作控制权补救

默认拒绝 A。**不把增加频率、反复 `SetOrder`、无限加 Priority 作为控制权补救。** C 意图落地不佳时，**不自动关闭 AI，也不自动升级为 B**——那条路是 H3 的关闭 AI 控制方式（见 §4.4）。

需要确定控制的请求，只有在目标域、场景、指令和恢复条件均有证据支持时才能采用 B；否则**明确报告能力未支持**。它不能返回“确定控制成功”后默默退回建议模式。

---

## 3. 逐域表：Movement / Facing / Arrangement

| 域 | 常态设计 | 需要确定控制时 | 当前宿主支持范围 | 尚未支持的能力 |
|---|---|---|---|---|
| **Movement** | C：在原生控制链中影响/替换决策产出，常态 Writer 净增 0 | B：Full + 有限 Lease + Token/Generation，写入口 Reject | **H2 已支持 T1F0、HOLD、单场 24.50 s 窗口**；作为受限候选 | 完整覆盖率、泛化与全场稳定；**不能宣布为默认产品能力** |
| **Facing** | C 为设计选择，保留原生合法写入关系 | B 的概念契约与 Movement 相同，但**待专门验证** | 当前探针可以观察/拦截相关入口（`SetFacingOrder` patch 已生效并计数）；**没有独立目标命令与接管实测** | Full 接管、独立 hold 与 Release 验收 |
| **Arrangement** | C 为设计选择，必须考虑**原生自维护路径** | B 只有覆盖自维护/兜底写入后才能验收 | 当前仅有观测与计数对照（run05/run06 的 `[coverage]` 三行） | Full 接管、完整写入口覆盖、恢复与跨域副作用 |

### 3.1 为什么 Movement 是唯一有接管证据的域

run04–run11 中，**run05/06/07/08/11 各有 1 对 `first_override`，且都是 `entity=T1F0, domain=Movement`；run04（census，零干预纯观测）与 run09/run10 为 `pairs=0`**。有 Lab 意图的七场，其 HOLD 窗口都是 `T1F0|Movement`，窗口 24.50 s。这七场的脚本一律 `acquire N0 Movement Full 30` + `cmd N0 Movement HOLD`——**没有任何一场对 Facing 或 Arrangement 下发过目标命令**。run04 只含 `policy` + `dump`，**不含 `acquire`/`cmd`**。

因此：

- **当前宿主 Full 控制证据仅限 T1F0 Movement 和既有单场 HOLD 窗口。** 这是硬边界，不能被其他场的 PASS 稀释。
- run06 的 Arrangement 三行（`sampled_changes=2 port_changes=3 unobserved=0`）是**观测结果**，不是接管验收：脚本对该域零命令，Arrangement 的写入全部来自原生侧。
- Facing 在 run05 有 `T1F0|Facing sampled_changes=1 port_changes=2 unobserved=0`，同样是纯观测。

### 3.2 Arrangement 的已知结构性风险（静态取证，非已发生故障）

`Formation::Tick` 内的两条自维护计时器路径**不走写入口**：

- `_arrangementOrderTickOccasionallyTimer` → `ArrangementOrder::TickOccasionally(formation)`
- `_arrangementTickOccasionallyTimer` → `IFormationArrangement::OnTickOccasionally()`

且 `SetArrangementOrder` 的调用方**清一色是 Behavior**（`TickOccasionally` / `OnBehaviorActivatedAux`）。⇒ 把 Arrangement 路径切成 Gated **也拦不住**这两条路径。这与 E12 ⑨（Level 5 下 non-gated 域 unauthW=20）在方向上一致，但**这是代码阅读结论，不是本轮实测到的故障**。

> **`docs/10` §13 原预测在 run06 未获得支持（本轮登记）**：`docs/10` 第 176–177 行把“切成 Gated 后 Arrangement 域 `unobserved > 0`”列为可判伪的 LEVEL C 预测。run06 实测为 `unobserved == 0`，即**没有支持该预测的正缺口**。第 177 行的“两种结果都记，都不当作实现有误”处理是正确的。本文件据此**降低**该结论强度，不改变 run06 的任何原始判定。
>
> **不得以“该场没有 Lab Arrangement 命令”否定该预测的可检验性。** 覆盖率缺口的测量口径为 `ProbeRuntime.SampleShadow`（`ProbeRuntime.cs:704-730`）——它遍历 `_bindings` 的全部实体并对 `d = 0..2` 三个域逐一比较宿主真值，**不要求 Lab 在该域持有 Lease 或提交过命令**；`PortEffectiveChanges`（`ProbeRuntime.cs:733-741`）同样只统计该实体该域的生效写入，不区分写入者是否有意图。因此纯 Native 的 Arrangement 状态变化也足以产生采样/入口计数缺口，零干预的 census 场本身就是这类观测的例子。**Arrangement 域缺少独占意图，确实无法验收 Arrangement Full；但由此推不出“覆盖率缺口预测不可检验”。**
>
> 至于本场为何未出现缺口——自维护路径是否实际改动所采 `OrderEnum`、采样是否漏检、该构型是否根本未触发这两条 Timer——**现有信息不足以强行选定其中一个原因**，登记为证据限制。

---

## 4. 职责、依赖与既有实现

### 4.1 职责表

| 既有职责 | 现有位置 | 最终契约 | 本轮处理 |
|---|---|---|---|
| **Intent / Task** | `Core/Planning.cs` | 目标、终止条件、任务状态与 Resume/Restart/Cancel；**不得代替写入口授权** | 复用与说明，不新增另一套任务框架 |
| **Ownership / Lease** | `Core/Authority.cs` | `(EntityId, Domain)` 授权；Acquire/Renew/Release/Expire；单调 Token 与变更 Generation | 复用；明确宿主恢复尚有缺口（§5.2） |
| **写入许可与合法候选选择** | `Core/WritePort.cs` | Ownership 与 Fencing 在**写入口**；**最终契约要求**许可检查**先于**合法候选的 Arbitration，所有尝试留痕 | 不搬到 Source 或决策器；不改实验开关。**注意：这是最终契约，不是现有行为**——现有 `WritePort.FlushTick` 实际**先** `Resolve`（按优先级淘汰）**后** `Evaluate`（权限/Fencing），见 §5.4 差距 7 与 §5.5 |
| **中性仿真** | `Simulation/` | 虚拟时钟、播种、确定性与 Ns 条件模型 | 保留；**不作为游戏能力证明** |
| **宿主接入** | `BannerlordProbe/` | 类型翻译、入口拦截、宿主状态观察与恢复；**Harmony 不承担战术决策** | 描述当前探针边界，**不称产品执行框架** |
| **遥测与判读** | `Telemetry/` + 离线工具（`tools/recompute_telemetry.py`、`tools/phase10_evidence`） | 写入尝试、归属事件、任务事件、hold 与稳定性证据 | 统一文档口径；**不改字段/算法/阈值** |
| **Plan / Hierarchy** | `docs/01` 的 L3 说明 | 跨目标约束/重规划负责 Intent/Plan Conflict | **保留设计职责；尚无完整实现**，不本轮新增 |

### 4.2 依赖方向

- Core **不反向引用** Telemetry。
- Simulation / Telemetry **可以依赖** Core。
- **只有 BannerlordProbe 接触宿主类型**。
- Core 中不出现 `TaleWorlds.*` 类型依赖。宿主方法名（如 `Formation.SetMovementOrder`、`Formation::Tick`）在本文档中只作为**证据引用**，不引入 Core。

### 4.3 三级写入路径与强制点位置

```
原生控制链                        写入口（Probe patch 面，7 个）              仲裁
─────────────                     ─────────────────────────────────         ──────
FormationAI (Behavior 选择)  ──┐
                               ├─→ Formation.SetMovementOrder    ─┐
MovementOrder.GetSubstituteOrder┘   Formation.SetFacingOrder      │  【最终契约】
（非门控兜底循环，写入入口在覆盖内）  Formation.SetArrangementOrder │  许可检查（Ownership）
                                    Formation.SetFormOrder        ├─→ 先于合法候选 Arbitration
                                    Formation.SetFiringOrder      │  → Reject / 放行
                                    Formation.SetRidingOrder      │  → 留痕（Telemetry）
                                    Formation.SetControlledByAI  ─┘
                                    ［观测型，一律 return true］
```

> **上图的"许可检查先于 Arbitration"是最终契约，不是现有实现的行为。** 现有 `WritePort.FlushTick` 的实际顺序是**先 `Resolve`（按 Priority/Utility 淘汰）后 `Evaluate`（所有权与 Fencing）**，因而一次非法的高优先级候选可以在权限检查之前把合法的低优先级候选淘汰掉。差距、影响与最小复现见 §5.4 差距 7 与 §5.5；修复属独立授权任务，本轮不动 Core。
**7 个已生效 patch**（run04–run11 每场 `[patches] failure=(none) count=7`）：`SetArrangementOrder`、`SetControlledByAI(Boolean, Boolean)`、`SetFacingOrder`、`SetFiringOrder`、`SetFormOrder(FormOrder, Boolean)`、`SetMovementOrder`、`SetRidingOrder`。

**级联需注意**：`SetArrangementOrder` 在 IL 里会级联 `SetFormOrder(FormOrderCustom(CalculateDesiredWidth()), 0)` ⇒ 写入口之间存在级联，一次域级写入可能在另一域留下痕迹。

### 4.4 H3 的真实机理与代价（必须保留的限定）

H3 脚本 `h3-route-b` 沿用历史名称。其中 `controlledByAiRoute` 是**关闭编队 AI 控制的实验开关**，**不能与 A/B/C 比较中的 C 集成路线混用**。

- `Formation.SetControlledByAI(false, ...)` **不写 MovementOrder**：只置 `_enforceNotSplittableByAI = 0` 并调 `ActiveBehavior?.OnLostAIControl()`。
- 真正起抑制作用的是 `Formation::Tick` 的门控：`if (IsAIControlled) AI.Tick(); else if (Team.IsPlayerSergeant) AI.Tick(); else IsAITickedAfterSplit = true;`

⇒ **H3 是关闭 AI 控制的实验，不是 C 的 Behavior/Weight 集成验收。** 在普通非 PlayerSergeant 情况下，它跳过的是 **`FormationAI.Tick`**（连 Behavior 选择一起停掉，可能同时影响多个域的行为选择）。**注意范围**：`Formation.Tick` 自身在该分支之后仍继续执行其余编队簿记（见 `_audit/_local-phase10-20261006/current-formation-tick-il.txt` 第 38 行），**不是"跳过整个 `Formation.Tick`"**。因此：

- **H3 的关闭 AI 控制方式不选作常态默认**（设计 §3.1）。
- **PlayerSergeant 例外必须保留**（`else if (Team.IsPlayerSergeant) AI.Tick();`）。
- 逻辑上“按域授权”**不意味着宿主物理效果已按域隔离**——这是 Q4 的关键限定。
- run07 的 `H3-b unauthorized_writes == 0` 只说明**该场该域**没有越权生效写入，**不证明**实际 `AI.Tick` / 行为选择调用计数为 0（该计数未被装置直接插桩，见 `docs/10` §14 与 run07 assessment 的 `scope_note`）。

---

## 5. Proposal / Executor 裁决与控制权生命周期

### 5.1 不新增 Proposal / 独立 Executor / 新 Command Bus

**当前最小框架不新增 Proposal、独立 Executor 或新的 Command Bus。**

理由：E1–E12 与既有宿主控制实验**没有逼出这些抽象**。Intent/Task、WriteRequest、AuthorityService、WritePort 已能表达当前问题。

**这是本轮范围内“不需要新增”的设计裁决，不是“永远不需要”的定理。** 重新讨论的触发条件：仅当后续真实需求证明现有对象无法表达，并且给出**最小失败例**和**验收标准**时再讨论。

宿主实际执行动作仍然存在，由现有接入层（BannerlordProbe）承担；**不因没有名为 Executor 的类而否认执行职责**。

### 5.2 五类冲突的落点（沿用 `docs/01`，不新增第六类）

| 冲突类 | 落点 |
|---|---|
| Command Conflict | 合法候选 Arbitration |
| Authority Conflict | Ownership / Lease / Fencing（写入口） |
| Intent Conflict | 任务与上层约束 |
| Plan Conflict | 重规划 |
| Native Pipeline Conflict | 接入位置与覆盖范围 |

**Content Transformation 保留为与 Authority 正交的值变换机制，不新增第六类冲突。**

### 5.3 控制权生命周期契约 vs 现有差距

下表的“契约”是**设计要求**，“差距”是**代码阅读结论**。**不得把契约写成已验证事实。**

| 阶段 | 设计契约 | 现有实现与差距 |
|---|---|---|
| **① 进入 B** | 先确认目标域的能力与恢复前提，再取得 Full Lease。**Acquire 未成功不得宣称获得控制，也不得留下已变更的宿主 AI 状态**。Lease 保持有限到期时间。 | 🟨 `ProbeRuntime.Acquire` **在申请 Lease 前关闭 AI 控制**；Acquire 被拒时是否回滚宿主状态**缺乏保证**。登记为阻塞产品化的项，本轮不修补。 |
| **② 控制中** | 每条受控请求携带创建时的 Owner/LeaseId/Token/Generation，写入口以**当前记录**校验。旧请求**不能在入口被重新盖上当前代次**而变“新”。Renew 失败、到期或归属变化后，不再以旧权限提交。 | 🟨 `GateCore` 以**当前** `_leases` 和**当前记录** Generation 构造 Lab 请求 ⇒ 当前同步探针**不能证明在途旧请求身份完整保留**。保留 E6 仿真结论；**宿主延迟请求能力未验证**。 |
| **③ 释放** | 停止本方新命令；如选择重发，**必须仍有有效 Lab 权限**且使用**缓存的原始命令**，不能从 Obs 投影重建；恢复本方实际改变的 AI 状态；交还归属、使旧代次失效、清理本方缓存；观察 Native 后续合法流量及未接管域影响。 | 🟨 `Release` **未检查** `_authority.Release` 返回值；`ReapplyLastNative` 在宿主原方法**异常时**的路径仍需核对 ⇒ 当前 REL 零计数**不证明异常路径的完整成功认证**。 |
| **④ 恢复选择** | **不默认** Reapply 或 ResetBehaviorWeights。 | ✅ run08 支持某场景**可以自然恢复**；run09/run10 支持动作**可执行**，**不证明必需**（第 ④ 条下一步即：`reapply` 写入被接受后，下一 tick 约 **0.0265/0.0269 s** 即以 `rgen=2` 回到原地，且该 Native 行 `chg=0` 为同值投影）。H3 若修改过 AI 控制，恢复夺取前状态是**交还契约**；**不能永久关闭原生 AI**。 |
| **⑤ 失败与边界** | 动作被拒、抛异常、没有有效缓存、租约已失效，**均不得被计为该动作成功**。显式 Release、Expire、实体退出和 Mission 结束应当能够终止本方控制；已到期不能复用旧 Lab Token 强行重发。恢复失败须留痕，**避免持续续约或重复强写把失败掩盖**。 | 🟨 `TickCore` 的 `SweepExpired` + `DropDeadLeases` 只移除租约/关闭 hold，**后者未调用 Restore** ⇒ **自动到期不能等同显式 Release 的宿主清理**。`WritePort.Evaluate` **不直接检查** `ExpiryAt`，租约到期回收依赖外部 Sweep 调用时序 ⇒ 写入口时刻的到期拒绝**不能仅凭有 Sweep 方法认定成立**。**具体宿主异常恢复实现待专门开发和验证。** |
| **⑥ 成功确认** | 夺取与交还**分别**提供证据；原方法获准、原方法完成、宿主真值变化和后续自然恢复**不能互相替代**。 | 🟨 恢复旧命令成功**不等于**所有 Behavior/Timer 内部状态回到基线。宿主记录的 OME 目前主要表达 **Prefix 放行意图**，不能据此证明其他 Harmony Prefix 没有拦截，或原方法已成功返回。 |

### 5.4 必须公开的代码差距（设计 §4.1 逐项）

| # | 源码观察 | 与最终契约的差距 | 本轮裁决 |
|---|---|---|---|
| 1 | `ProbeRuntime.Acquire` 在申请 Lease 前关闭 AI 控制 | Acquire 被拒时是否回滚宿主状态缺乏保证 | 记录阻塞该方式产品化；**不本轮修补** |
| 2 | `TickCore` 的 `SweepExpired` + `DropDeadLeases` 只移除租约/关闭 hold；后者未调用 Restore | 自动到期**不能等同**显式 Release 的宿主清理 | 记录待验证/待实现；**不称全部生命周期完成** |
| 3 | `WritePort.Evaluate` 不直接检查 `ExpiryAt`，依赖外部 Sweep 时序 | 写入口时刻的到期拒绝**不能仅凭有 Sweep 方法认定成立** | 列出时序前提与待验证边界；不本轮改算法 |
| 4 | `GateCore` 以当前 `_leases` 和当前记录 Generation 构造 Lab 请求 | 当前同步探针**不能证明在途旧请求身份完整保留** | 保留 E6 仿真结论；宿主延迟请求能力未验证 |
| 5 | `Release` 未检查 `_authority.Release` 返回值；`ReapplyLastNative` 异常路径仍需核对 | 当前 REL 零计数**不证明**异常路径的完整成功认证 | 单列风险与后续验收；**不自行修源码** |
| 6 | `_inLabWrite` 与相关集合的并发身份/访问 | 单场无崩溃**不证明** worker 写入归因与并发安全 | 接入生产前补直接线程/归因验证；**不凭临时变量名推断** |
| **7** | **`WritePort.FlushTick`（`src/CommandLab.Core/WritePort.cs:157-183`）先 `Resolve` 后 `Evaluate`**：`foreach (var p in Resolve(key.Entity, key.Domain, list)) Evaluate(p);`，而 `Resolve`（`WritePort.cs:185-209`）按 `Priority`/`Utility` 排序后，对落选者以 `RejectReason.LostArbitration` 拒绝、只返回 `ranked[0]`；`Evaluate`（`WritePort.cs:211+`）才做 Ownership 与 Fencing 检查 | **先仲裁后许可**：一次**非法**的高优先级候选可在权限检查之前**淘汰合法的低优先级候选**，使该次写入既不生效、又在矩阵中排挤合法 Owner。与 §4.1/§4.3 声明的最终契约（许可检查先于 Arbitration）相反 | 登记差距与最小复现（§5.5）；**修 Core 属独立授权任务，本轮不改**。**H2 系列探针以 Arbitration=None 运行，本项不能用来倒推 H2 的实测数据无效** |

> **这张表的来源是代码阅读。** 除 REL 已有反例外，**不表示这些故障已在本轮游戏中发生**。差距阻塞对应能力产品化，**不阻塞本轮如实完成设计文档**。不得把到期、回滚、延迟请求、并发和异常恢复写成“已完成”。

### 5.5 差距 7 的最小复现（先仲裁后许可）

在 Core 上构造一次批内竞争即可复现，无需访问宿主：

| 步 | 批内候选 | `FlushTick` 结果 | 说明 |
|---|---|---|---|
| 1 | 仅合法 Lab 候选（Owner=Lab，intent=HOLD） | **接受**，`NewValue=HOLD` | 作为对照基线 |
| 2 | 追加 Native 候选 `Priority=100`（**越权**，Owner 仍为 Lab）+ 合法 Lab 候选 `Priority=1` | **Lab 在 `Resolve` 阶段以 `LostArbitration` 被拒**（排序后 `ranked[0]` 为 Native，低优先级 Lab 被淘汰）；**Native 在 `Evaluate` 阶段以 `NotOwner` 被拒**（它是写入口检查的写入者，但并非 Owner） | 最终该 `(Entity, Domain)` 落到 `Unset` |

> 即：**越权候选自身没有生效，却成功把合法 Owner 的候选一并挤掉**。本项为审查方独立构建最小诊断并在当前 Core 上实际运行确认（输出见 `_audit/2026-10-07-phase11-codex-review/arbitration-reproduction.txt`：`Lab: Rejected=true, RejectReasonText=LostArbitration`；`Native: Rejected=true, RejectReasonText=NotOwner`；`final value Unset`）。**后续独立验收至少应覆盖：非法高优先级候选不得淘汰合法低优先级候选；权限与 stale 身份检查须在批量仲裁之前。**

---

## 6. 指标、证据与判定边界

### 6.1 指标集与统一口径

原 14 项指标继续保留；**新增到正式文档的第 15 项是既有 `intent_hold_share`，不是新增算法**。

| 指标 | 口径 | 边界 |
|---|---|---|
| **`intent_hold_share`（第 15 项）** | 同一实体、同一域、**有效意图窗口内**，宿主真值与意图值相同的累计时长 ÷ 该窗口累计时长。**有效意图窗口 = 从该 `(EntityId, Domain)` 首个被接受的 Lab 写入起，到该 Lab 归属结束**（与 `docs/05` §1.5 一致）。 | 不能从 Acquire 到首次被接受的 Lab 写入之间补算 hold；`pre_acquire` 与 `acquire_to_first_accepted_lab_hold` 是**事件归因窗口，不是 hold 的分母**。窗口起止与分母须逐 `(EntityId, Domain)` 记录；缺窗口/零长/未采样只能记"无窗口"。宿主采用 **tick 采样加时间累计**；匹配样本比例只作**辅助**，不等于普遍意义下的连续真值证明。 |
| **③ 与既有实现的差距（登记）** | `ProbeRuntime.Command`（`ProbeRuntime.cs:378-384`）打开意图窗口的条件为 `LastAttempt is not null && domain == CommandDomain.Movement`——**只检查该次写入尝试存在，未检查 `Rejected`**。 | 因此**"仅在成功/有效命令提交后打开窗口"不能无条件宣称**。接受写入打开窗口有本轮证据（run05/06 首个被接受的写入 `t=3.5045/3.5020`）；**被拒命令是否同样打开/延长窗口本轮未实测**，登记为待验证。E12 历史仿真是**固定 `[2.0,22.0)` 区间积分**（`ArchitectureComparison.cs:43-58`，第 201 行调用 `from=2.0, to=22.0`；筛选该域被接受且 `OrderChanged` 的事件重建时间线，窗口前状态延续进来，**分母固定 20 s**，不调用 `HoldMeter.Open`、不按写入延长窗口）；宿主 Probe 是 `HoldMeter` tick 采样。**两者的实验窗口与实现须分别标明**，引用 hold 数值须注明来源侧，**不得把 Probe 的窗口规则倒写为 E12 历史行为**。 |
| **G1 `unauthorized_writes`** | 沿用既有**生效变更**口径；**写同值放行不计变化** | 故还需审查 rejected/OME 与目标 Native 尝试；**不能仅凭 G1 认证所有不合法调用都被阻断** |
| **G2 `intent_hold_share`** | 仍为 **hold ≥ 95%** | 没有意图窗口、窗口为零、未采样或目标不存在，**不能凭 0 次越权判成功**。多目标时**逐目标报告**，不能拿 BestHold 代表所有目标；当前实验只对其指定目标作结论。 |
| **G3 全场 grade** | 沿用现有全场 MetricsCalculator，**不改为目标局部分级，不裁掉 Acquire 前数据，不改阈值** | 目标 hold、全场 grade、事件窗口归因**必须分别写明** |
| **X5 / unobserved** | 采样变化与入口计数缺口的**下界** | **没有独立全量写入分母，不能计算或宣称完整覆盖率 100%** |
| **`lab_delivery_rate`** | **不作控制成功判据** | 它低估写同值且持续保持的情况（E12 L5 仅 4.8%）。保留作历史诊断，**不重命名成 hold，也不改历史数值**。 |
| **`release_recovery_failures`（两种口径）** | 仿真口径的状态恢复窗口 与 探针 REL 的恢复动作失败计数 | **分别列出，本轮不强行合并为一个数**。探针 REL=0 **不能**认证仿真定义所要求的完整基线恢复。 |
| **OME** | 宿主记录的 OME 目前主要表达 Prefix 放行意图 | 不能据此证明其他 Harmony Prefix 没有拦截，或原方法已成功返回。Probe crash / driver exception / 身份不明**必须登记**，不能用零计数填补缺失证据。 |

### 6.2 “hold=100% 与 G3 FAIL 并存”不是矛盾——是分层判据

run05/run06 H2 同时给出：

```
[hold]    T1F0|Movement   CLOSED intent=HOLD  hold= 100.0%  window=24.50s  samples=3503  matches=3503
[verdict] G1 actual=0 PASS / G2 actual=100.0% PASS / G3 actual=Oscillating FAIL
```

G3 判据来自 `ProbeRuntime.cs:944`——`m.Grade != StabilityGrade.Oscillating`，而 `Grade` 是**全场** MetricsCalculator 的输出。独立重建已复现该 FAIL：

| 场 | `OrderChangesPerSecond` | `oscillation_pairs` | `oscillation_frequency` | `ReportedGrade` |
|---|---|---|---|---|
| run05 | 3.696969696969697 | 37 | 1.121212121212121 | **Oscillating** |
| run06 | 3.6363636363636362 | 35 | 1.0606060606060606 | **Oscillating** |

阈值（重建产物 `console.txt` 同场内自述）：`stableOscillationFrequencyMax=0.05`、`stableOrderChangesPerSecondMax=0.2`、`oscillatingFrequencyMin=0.5`、`oscillatingLifetimeLessThanSeconds=1`。

**⇒ 目标 hold = 100% 不能覆盖全场 G3 FAIL。** 二者回答的是不同问题：hold 回答“该实体该域是否服从目标意图”，G3 回答“全场是否稳定”。当前证据是：**单点控制成功 + 全场不稳定**。

### 6.3 X5 = 0 不能推完整覆盖率 = 100%

`X5 / unobserved_writes` 是**下界**：`ProbeRuntime.TotalUnobserved()`（`ProbeRuntime.cs:975-985`）只对 `sampled > port` 的 key 累加差值，即“**采样到的变化数超过写入口记录数**”才计入。它测的是**缺口**，不是**分母**。

`tools/recompute_telemetry.py` 对全部八场输出同一句机器级限定：

```
"interpretation": "zero means no observed positive gap; it does not prove complete coverage"
```

且 X5 的上游是影子采样，其时间分辨率 = Mission tick 频率 ⇒ **同一帧内两次变化只算一次**，本身即下限估计。

**⇒ X5=0 表示“未观测到正缺口”，不表示“完整写入覆盖率 = 100%”。** 没有独立全量写入分母，任何 100% 覆盖率的宣称都不成立。

### 6.4 事件窗口归因

run05/run06 的分段窗口（`console.txt`）：

```
run05: AcquireT=3.0046  FirstAcceptedLabHoldT=3.5045  FirstAcceptedLabHoldRow=170
       窗口: pre_acquire / acquire_to_first_accepted_lab_hold / first_lab_hold_to_release
run06: AcquireT=3.0057  FirstAcceptedLabHoldT=3.5020  FirstAcceptedLabHoldRow=170
```

**不同流同时间戳的事件不得凭打印顺序推断跨流严格先后**；已打印时长的取整误差与重建时间须标明。跨窗口 ABA 三元组要保留跨界信息，**不切窗后假称全场通过**。

---

## 7. 最终七问：当前答案、设计建议与 Phase 12 边界

| 问题 | 本轮定稿方向 | 区分：当前答案 / 设计建议 / Phase 12 才能回答 |
|---|---|---|
| **Q1 谁控制 Formation** | 多入口共享可变资源；Native 常态控制链 + 显式域授权，**不能寻找一个万能最终 Writer** | **当前答案**：7 个写入口覆盖 + 静态门控取证。**设计建议**：分层而非单点。**Phase 12**：CombatBrain 具体接入点的竞争面 |
| **Q2 如何取得控制** | C 只给影响力；确定控制用**有证据范围**的 B，Full / Lease / Token / 入口 Reject | **当前答案**：Movement 限定实测（T1F0、HOLD、24.50 s）。**设计建议**：按域申请 B。**Phase 12**：三域全量接管验证 |
| **Q3 如何交还** | 显式结束与宿主状态恢复契约；重发必须合法，恢复动作必要性分别评估 | **当前答案**：REL=0/0/0/1；原始 Move 重发有效；先丢所有权再以旧身份恢复被拒。**设计建议**：契约六条（§5.3）。**Phase 12 / 后续**：到期/异常/完整状态恢复尚不足 |
| **Q4 三域独立 Authority** | 逻辑以 `(EntityId, Domain)` 为原子单位；**宿主接入副作用另验** | **当前答案**：仿真方向支持。**关键限定**：H3 表明**不能宣称物理效果完全分域**——关闭 AI 控制跳过整个 `FormationAI.Tick`（`Formation.Tick` 本身仍继续其余簿记）。**Phase 12**：宿主副作用隔离实测 |
| **Q5 多源如何协作** | 按五类冲突**分别**使用许可、合法候选仲裁、任务/计划与控制链接入 | **当前答案**：五类冲突落点表（§5.2）。**设计建议**：不以一个 Arbiter 代替所有职责。**Phase 12**：真实多源场景 |
| **Q6 CombatBrain 路线** | 本轮框架建议**常态 C、必要时受限 B** | **当前答案**：架构方向。**明确边界**：**没有本轮 CombatBrain 案例分析**，不给其具体迁移实现裁决。**Phase 12** |
| **Q7 冲突占比** | 沿用五类遥测签名的归因方法 | **当前答案**：方法。**明确边界**：**没有 CombatBrain 异常样本，不给百分比**。**Phase 12** |

---

## 8. 结论到证据的映射

每一行的“类型”取 `仿真 / 静态 / 宿主实测 / 设计选择 / 未验证` 之一。

| # | 结论 | 类型 | 实验 / run | 输入路径 | SHA-256 | 实体 / 域 / 窗口 / 配置 | 原判据与结果 | 支持范围 | 不能推导 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | **T1F0 Movement 取得并保持目标控制** | 宿主实测 | run05 H2 | `_audit/_local-phase10-20261006/run05-h2-suppression/telemetry-20261006-215546-h2-suppression.txt` | `9242c6576846709dcc302c26c1f2d1155f4aa35b33ecbc645a947c3d473bc35e` | T1F0 / Movement / HOLD 窗口 24.50 s / Full ttl=30 enforceGated=true enforceNonGated=false arrangementPath=NonGated controlledByAiRoute=false | G1=0 PASS；G2=100.0% PASS；X5=0 | **该实体、该场景、该窗口** | 全场稳定、所有 Movement 路径覆盖、多场景泛化、Facing/Arrangement 接管 |
| 2 | **G3 全场 FAIL 保留** | 宿主实测 | run05 H2 / run06 H2b | 同上 + `run06-h2-coverage/telemetry-20261006-215736-h2-coverage.txt` | run05 `metrics_sha256=b37a56b95881507397e5665c637338998dff2de38983be62ecae422bb62ab962`；run06 `telemetry_sha256=c2f41b3f6c8814f04cd43d08c8c6a1e86a72273ffd417eb86fca79340e1d6f3b` | 判读时刻原时长打印 **`simulation_seconds=33.00`**（非"全程 30 s"）/ TEval=0.5 | `ReportedGrade=Oscillating`（**独立重建复现**：run05 `OrderChangesPerSecond=3.696969696969697`；run06 `=3.6363636363636362`） | 全场级不稳定，**未被 HOLD 窗口覆盖** | 单点控制的失败；**不得**用目标 hold 冲抵 |
| 3 | **完整写入覆盖率不足（未证 100%）** | 设计边界 / 未验证 | run05/run06 + 八场 recompute | `_audit/2026-10-07-phase11-evidence/recompute/*.recompute.json` | 见 §9.1 | 八场全部 `x5_coverage_gap_lower_bound.count = 0` | `interpretation: "zero means no observed positive gap; it does not prove complete coverage"` | 未观测到正缺口 | **完整覆盖率 = 100%**；Arrangement Full 已验证 |
| 4 | **H3 的性质** | 宿主实测 + 静态 | run07 H3 | `run07-h3-route-b/telemetry-20261006-215927-h3-route-b.txt` | `57205e643e42418a67ffaf1039a1acd5d05141df7045af104b4c88ca4d0d252b` | T1F0 / Movement / `controlledByAiRoute=true restore=RestoreControlledByAi` | H3-a=60 PASS；H3-b=0 PASS；G2=100.0% PASS；**全场 grade 仍 Oscillating** | 当前配置下的关联 + 关闭 AI 控制的抑制效果观察 | **C 的 Behavior/Weight 集成已通过**；实际 `AI.Tick` 调用计数为 0 |
| 5 | **REL 恢复顺序与自然恢复** | 宿主实测 | run08–run11 | `run08-rel-none/telemetry-20261006-220128-rel-none.txt`、`run09-rel-reapply/…220327…`、`run10-rel-reapply-resetweights/…220518…`、`run11-rel-after-release/…220719…` | run08 `81236f848b42e12337f734f46924cd35a86f2cf0f4a6c6b674e3c852e5cdc66c`；run09 `ffb7e5b12b6590edd64b21dc8d4042df0f165557b00cc8bb3a7fea5c7f9f5ef8`；run10 `4e92417cedc223b2caefdc03739dc3648184d2b0394a7da25b23c619eea1fc55`；run11 `0c121a76be088647650d424cf3649c8779da5aa8e8f8f02563f7d698975041da` | 四场同构：Acquire t≈3.00 → cmd t=3.5 → Release t=28.00 → dump t=33（run08/09/10）/38（run11） | REL = **0 / 0 / 0 / 1**；run11 `expected_counterexample=true`。**run09/run10 的 `EVIDENCE_INSUFFICIENT` 是证据充分性状态，不是 REL 判据失败** | 原始 Move 重发有效；先丢所有权再以旧 Lab 身份恢复会被拒；自然恢复可出现 | Reapply/Reset **普遍必要**；恢复全部内部状态；到期/异常恢复已验收 |
| 6 | **Proposal / Executor 裁决** | 设计选择 | E1–E12 + 本轮宿主实验 | `docs/06`、`docs/07a`、`docs/phase11-design.md` §3.3 | — | — | 无证据逼出该抽象 | 本轮范围内**不需要新增** | “永远不需要”；否定执行职责本身 |

### 8.1 run11 反例的逐行证据（“实验预期失败、反例辨识成功”）

run11 是**设计出来的反例**（`rel-after-release.txt` 注释：“预期 `release_recovery_failures > 0`，即 REL 判据 FAIL —— 这是设计出来的反例”）。其 `assessment.json` 明确 `classification=FAIL`、`expected_counterexample=true`、`scope_note="FAIL is expected; discrimination succeeds when the restore attempt is NotOwner rejected."`。

| t | 事件 | 关键字段 |
|---|---|---|
| 28.0049 | Lab 恢复写入被拒 | `W=Lab` `O=Native` `gen=2` `rgen=2` `exp=-Infinity` `cmd=Obs0` `prev=HOLD` `rej=1` `why=NotOwner` |
| 28.0251 | Native 自行恢复 | `W=Native` `O=Native` `rgen=2` `chg=1`（`HOLD → Obs0`） |

同一时刻 `AUTH|t=28.0049|Release|E=T1F0|D=Movement|own=Native|prev=Lab|lease=|tok=0|gen=2|why=released to Native`。

**⇒ 顺序规则**：
1. **原始 Move 重发按完整缓存执行**——恢复写入用的是**缓存的原始 Native 命令**。`REL_ORDER` 留痕实测：`stage=reapply-request … rawType=Move position=794.721558,707.077637`，`stage=reapply-result … requestedType=Move requestedPosition=794.721558,707.077637 result=rejected actualType=StandYourGround`。**缓存的是 Native 的原始 `Move` 结构，不是 Lab 的 HOLD**，也**不是从 Obs 投影重建**。
2. **AfterRelease 被拒之后的 Native 自行恢复，不能算那次恢复写入成功。** 28.0049 的 Lab 尝试以 `rej=1 why=NotOwner` 结束；28.0251 的 `chg=1` 是 **Native**（`W=Native`）写下的，与 28.0049 被拒的 Lab 尝试**是两件事**。

### 8.2 run09/run10 “reapply 可执行”的逐行证据（不证明必需）

```
run09: t=3.5000  W=Lab O=Lab lease=L1 tok=1 gen=1 exp=11.5000 cmd=HOLD  prev=Obs0 new=HOLD chg=1 rej=0
       t=28.0016 W=Lab O=Lab lease=L1 tok=1 gen=1 exp=36.0016 cmd=Obs0  prev=HOLD new=Obs0 chg=1 rej=0
       t=28.0281 W=Native O=Native gen=0 rgen=2 exp=-Infinity cmd=Obs0 prev=Obs0 new=Obs0 chg=0
run10: t=3.0030  Acquire ；t=28.0019 W=Lab O=Lab ... cmd=Obs0 prev=HOLD new=Obs0 chg=1 rej=0
       t=28.0288 W=Native O=Native ...
```

**关键读数**：reapply 写在 t=28.0016/28.0019 **被接受**（`chg=1 rej=0`，且仍持有效 Lease `exp=36.0016`，因为 `restoreBeforeRelease=true`），**但约 0.0265/0.0269 s 之后（下一 tick）Native 就以 `rgen=2` 回到 `Obs0`**。该后续 Native 行的读数是 **`chg=0`（同值投影）**——它只证明**该次重发尝试被放行、且宿主事后投影为同值**，**不能独立证明全部真实 Order 字段/位置/内部状态都已恢复**（恢复的直接证据是 run08 的自然恢复与 `REL_ORDER` 的原始 Move 重发记录）。该场**本来就会自然恢复**（run08 无任何动作时 t=28.0234 同样 `chg=1 HOLD → Obs0`）。

⇒ Reapply **可执行**（动作成功），但**不证明必需**。同理 run10 的 `ReapplyLastNativeOrder, ResetBehaviorWeights` **相对 run09 未显示出恢复收益**：run09 `osc=0.9210 / ocps=3.1314`，run10 `osc=0.9209 / ocps=3.1312`——差异在 0.0001 量级，**既不构成"必须 Reset 才能恢复"的证据，也不宜据此反向断言 Reset 有害**。**正确表述是：本轮未证明 Reset 的恢复收益或必要性。**

> `tools/recompute_telemetry.py` 对 run09/run10 返回 `assessment_status=EVIDENCE_INSUFFICIENT`、诊断 `accepted Lab writes have no later same-entity/domain Native-tagged effective write`。**这是这两场的证据充分性状态，不是 REL 判据的失败**——run09/run10 的原 REL 判据（`release_recovery_failures == 0`）均为 **PASS**，四场 REL 中**只有 run11 是预期失败的反例**。

---

## 9. 证据身份与可访问性

### 9.1 八场身份（实测，本轮核对）

| run | script | runtime | source_head | assessment classification | metrics | telemetry |
|---|---|---|---|---|---|---|
| run04-census-final | census-baseline | RUN | `adab4cf008a8dd6e83f8e12c03f1423483cb98f0` | PASS | `metrics-20261006-215243-census-baseline.txt` | `telemetry-20261006-215243-census-baseline.txt` |
| run05-h2-suppression | h2-suppression | RUN | 同上 | **FAIL**（G3） | `…215546-h2-suppression.txt` | `…215546-h2-suppression.txt` |
| run06-h2-coverage | h2-coverage | RUN | 同上 | **FAIL**（G3） | `…215736-h2-coverage.txt` | `…215736-h2-coverage.txt` |
| run07-h3-route-b | h3-route-b | RUN | 同上 | PASS | `…215927-h3-route-b.txt` | `…215927-h3-route-b.txt` |
| run08-rel-none | rel-none | RUN | 同上 | PASS | `…220128-rel-none.txt` | `…220128-rel-none.txt` |
| run09-rel-reapply | rel-reapply | RUN | 同上 | PASS | `…220327-rel-reapply.txt` | `…220327-rel-reapply.txt` |
| run10-rel-reapply-resetweights | rel-reapply-resetweights | RUN | 同上 | PASS | `…220518-rel-reapply-resetweights.txt` | `…220518-rel-reapply-resetweights.txt` |
| run11-rel-after-release | rel-after-release | RUN | 同上 | **FAIL**（预期反例） | `…220719-rel-after-release.txt` | `…220719-rel-after-release.txt` |

八场共同项：`game_version=v1.4.8.119303`、`dll_hashes_match=true`、`instrument_gates_pass=true`、`P0=7`、`P0b` PASS、`P0c=29/29`、`X5=0`、`active_modules` 含 `CommandLabProbe` 而**不含 CombatBrain**。

各场 `game_pid` 与 assessment 指定的 metrics / telemetry SHA-256（本轮逐文件实测复算，全部匹配）：

| run | game_pid | metrics_sha256 | telemetry_sha256 |
|---|---|---|---|
| run04-census-final | 19180 | `051522bebe6593c36d8257e835fb0817e7f18b972618373e1296aa452368f52a` | `df47950fa5a3a282c8e6e209bbd466e91e0adfc6dde5c9467508802185466e46` |
| run05-h2-suppression | **36348** | `b37a56b95881507397e5665c637338998dff2de38983be62ecae422bb62ab962` | `9242c6576846709dcc302c26c1f2d1155f4aa35b33ecbc645a947c3d473bc35e` |
| run06-h2-coverage | **39760** | `f7ab7084150b4376b412c1d6d58234d1be59a304623129967b72180fce5d6082` | `c2f41b3f6c8814f04cd43d08c8c6a1e86a72273ffd417eb86fca79340e1d6f3b` |
| run07-h3-route-b | 38156 | `f47bda68f0f0941d67863367b1328527af1e2b697147d3b2d550f848033a492f` | `57205e643e42418a67ffaf1039a1acd5d05141df7045af104b4c88ca4d0d252b` |
| run08-rel-none | **40564** | `d6a41036033fb3eae322017843dc3531669ed143bd37dc13753fe40b1e0afa42` | `81236f848b42e12337f734f46924cd35a86f2cf0f4a6c6b674e3c852e5cdc66c` |
| run09-rel-reapply | **38548** | `311a19ee1167050eaa7e51ba975794be6c4ba87780258576d3af20a587436fb0` | `ffb7e5b12b6590edd64b21dc8d4042df0f165557b00cc8bb3a7fea5c7f9f5ef8` |
| run10-rel-reapply-resetweights | **18632** | `715958718d8bf6b4606f5cb2e48591bf1cda2c50f8fb7fe9f1712cd4406e901b` | `4e92417cedc223b2caefdc03739dc3648184d2b0394a7da25b23c619eea1fc55` |
| run11-rel-after-release | 19252 | `3364ae7c9e4c5a8c4dd6d8f67073e6e9ba44b76fdecf5d6aca4eba488bb4c6fd` | `0c121a76be088647650d424cf3649c8779da5aa8e8f8f02563f7d698975041da` |

> **`game_pid` 更正（审查指出）**：早期版本把 run05/06/08/09/10 一律记为 `19180`（误复制前一场身份）。上表为逐场 `assessment.json` 实测值，**八场 PID 各不相同**，除 run04 外均非 19180。身份核对以本表为准。

各场 `script_sha256`（指向播种后的 `script-seeded.txt`）与脚本长度：run05 `af3655b3b78f68acb9f27297c43999b621f14242ce61cf78e8ea8dad4ce2079e`（len 593）；run07 `17a351b2fe83e086e261aba15717a5e1034d585218a695a2df290e2713c61fec`；run11 `38d29cac4d6d43805df6ec16cd8dec5763fdf03318d69810db94a7f280a457c3`。

### 9.2 哈希核对结果（本轮实测）

- **`artifact-manifest.json` 路径/长度/SHA-256**：八场合计 **491 条**（**61×5 + 62×3** —— run04/05/06/07/10 各 61，run08/09/11 各 62），核对结果 **ok=491 / missing=0 / mismatch=0**。
- **assessment 指定的 metrics / telemetry 哈希**：八场 metrics、telemetry 逐场 **全部 OK**（文件名与 assessment 字段一致，长度与 SHA-256 匹配）。
- **`script_sha256` 的指向**：assessment 的 `script_sha256` 指的是**播种后**的 `script-seeded.txt`，**不是**播种前的 `script-source.txt` 模板。实测逐场确认（例 run05：seeded `af3655b3b78f68acb9f27297c43999b621f14242ce61cf78e8ea8dad4ce2079e` len=593 == `assessment.script_sha256`；source `5661d549dae37c3c32411b3ca9841c8cd112c0b6e8436c88c8a505ee3ac590e7` len=585）。**两份脚本都已归档，因此两者都可核对。**
- **本地忽略目录不随交付自动提供**：`_audit/_local-phase10-20261006/` 在 `.gitignore` 覆盖下。本轮的独立复核产物在 `_audit/2026-10-07-phase11-evidence/`（同样未跟踪），**跨机器交付必须另带可访问副本；只有路径不构成交接。**

### 9.3 本轮独立复算（runtime=NOT_RUN）

| 工具 | 命令 | 输入 | 输出 | 结果 |
|---|---|---|---|---|
| `tools/recompute_telemetry.py` | `C:\Python314\python.exe tools\recompute_telemetry.py <telemetry>`（逐场） | 八场 assessment 指定的 telemetry | `_audit/2026-10-07-phase11-evidence/recompute/*.recompute.json` | 八场 `exit=0`；`input.sha256` 与 assessment 声明逐场一致。**注意**：该工具退出码规则为 `1 if assessment_status == "FORMAT_ERROR" else 0`，**`EVIDENCE_INSUFFICIENT` 仍返回 0**，故 exit=0 不表示八场结论一致 |
| `tools/phase10_evidence` | `dotnet run --project tools/phase10_evidence/Phase10Evidence.csproj -- <run05 telemetry> <run06 telemetry> _audit/2026-10-07-phase11-evidence/g3-rebuild` | run05/run06 telemetry | 同目录（`results.json`、8 份 CSV、2 份 diagnostics、`console.txt`） | `exit=0`；**显式传入输出目录**，不落默认目录 |

**派生报告标 `runtime=NOT_RUN`** 表示该次离线复核未启动游戏，**不改原始实测 `runtime=RUN`**。

`tools/phase10_evidence` 的 8 份 CSV 与冻结副本 `docs/evidence/phase10-closure-20261007/` **逐字节相同**（工具确定性幂等）：run05-changes `d5ae2ae90b81ca3484b4e07b07e4d520baafa42d05fda23c6911dd5bf6b61028`、run05-keys `b4f047c9baccec2593a3bddbae1a14adf33254dc94347969ff8c08e1140f5ade`、run05-lifetimes `9129159ef07736445838080633eef31945b3a35aa3a433d9a21366c97a73ce58`、run05-triples `adb20118edb3c30b64169bfd203701c7dc54fd6eb0beb6cda34a7d1cbc36bd24`、run06-changes `4ce9e13fa63c17951f5eb2810a8270f3cd8d472de0a8631e2fedf988c9321666`、run06-keys `c5af98ab3aa3e3299e0b8eb195846631b61d86ea577914730fa12d0fac98f9e2`、run06-lifetimes `c79b56dc5d733741b3eee32f88eaa6ec0bf7248bb958463a26b48579d77c5715`、run06-triples `9ca493b534b83b86ad0970861af5f4f054e8f9ba88acb0dd0696d9fbc6f3fabe`。

**该工具只用于解释原 G3 失败来源并保留原判定。** 其存在和输出**不替代复核**；`docs/evidence/phase10-closure-20261007/g3-summary.json`（`27fda22bea7bd685a3017a199671362bf9a80f55976b36b900808b174df79b78`，len 30937）与本轮 `results.json`（`a29bd216c78df6b8a74fedb481f414a9fe17f9e7b0df37f539c92502adda87e6`，len 1085294）结构同为 2 元素数组。

---

## 10. 遗留项与后续能力放行条件

### 10.1 遗留项

| # | 遗留 | 影响 | 建议最小后续任务与验收 |
|---|---|---|---|
| 1 | **宿主恢复路径未验证**（到期 Restore、异常路径、`Release` 返回值） | 生命周期契约 ⑤ 不能称为已完成 | 补 `Expire` / 实体退出 / Mission 结束 三场的独立场次；验收：三类终止各自提供恢复证据，且失败必须留痕 |
| 2 | **请求身份未验证**（在途旧请求不得被重盖当前代次） | 契约 ② 只在仿真（E6）成立 | 构造宿主延迟请求场景；验收：旧 Token/Generation 请求被拒且留痕 |
| 3 | **并发与归因安全未验证**（`_inLabWrite`） | 接入生产前的阻塞项 | 直接线程/归因验证；验收：无凭临时变量名推断的结论 |
| 4 | **Acquire 回滚缺失** | 契约 ① 的“不得留下已变更宿主状态”无保证 | 定义拒绝路径的回滚验收；验收：Acquire 被拒后宿主状态与该域进入前一致 |
| 5 | **Facing / Arrangement Full 接管未验收** | 三域表后两列的“尚未支持” | 各补一场独立目标命令 + hold + Release；验收：各自的 G1/G2/G3 分开报告 |
| 6 | **完整写入覆盖率无分母** | 任何 100% 宣称不成立 | 引入独立全量写入分母或等价机制；验收：覆盖率可计算而非下界 |
| 7 | **Arrangement 自维护路径不在 patch 面内** | 该域 Full 接管的结构性障碍 | 静态确认后补 patch 覆盖或明确放弃该域 Full；验收：`unobserved` 可断言，而非零缺口假象 |
| 8 | **`Formation::Tick` 关 AI 控制的跨域副作用** | Q4 “物理效果分域”未成立 | 按域副作用隔离实测；验收：关闭 A 域不改变 B 域可观测行为 |
| 9 | **`docs/10` 第 176–177 行的预测在 run06 未获支持** | 影响 Arrangement 结论的措辞强度 | 已在 §3.2 登记（并更正为“未获支持”，非“不可检验”）；建议后续一并修正 `docs/10` 该段措辞（本轮不改历史文档结论） |

### 10.2 后续能力放行条件

| 能力 | 放行条件（全部满足） |
|---|---|
| Movement 域标为“产品候选” | 多场景（非单一 T1F0）+ 完整覆盖率可计算 + G3 不依赖关闭 AI 控制 |
| Facing / Arrangement 域接管 | 各自独立场次的目标命令、hold、Release 全部通过 + 该域写入口覆盖断言 |
| 生命周期契约 ①②⑤⑥ 标为“已实现” | §5.4 六项差距逐项关闭，且各自有独立场次证据 |
| CombatBrain 集成（Phase 12） | 本轮不放行；需先完成 Phase 12 案例分析与集成设计 |

**本轮不改判据、不改阈值、不改历史数值、不进入 Phase 12。**

---

## 11. 红线自检

| 红线 | 本轮落实 |
|---|---|
| 重新自由选型 | ✔ 完全沿用 `docs/phase11-design.md` 的设计裁决，未重新选 A/B/C |
| 为让实验全 PASS 改判据 | ✔ G3 阈值、G2 阈值、REL 判据、X5 口径全部原样；H2/H2b G3 FAIL 与 run11 REL=1 完整保留 |
| 把仿真结果写成实测事实 | ✔ 仿真结论一律标注“仿真”；宿主结论标注 run 编号与窗口 |
| 把契约写成已验证功能 | ✔ §5.3/§5.4 逐项标注差距；到期、回滚、延迟请求、并发、异常恢复全部写为未完成 |
| 把代码阅读风险写成已发生故障 | ✔ §3.2、§5.4 均注明“来源是代码阅读，不表示已在本轮游戏中发生” |
| 用 hold=100% 冲抵 G3 FAIL | ✔ §6.2 明确分层，两者并存陈述 |
| 由 X5=0 推完整覆盖率 100% | ✔ §6.3 引工具机器级限定，明确否定 |
| 把 AfterRelease 的 Native 自行恢复算作该次恢复成功 | ✔ §8.1 逐行拆开 28.0049（Lab 被拒）与 28.0251（Native 自行恢复） |
| 把 H3 写成 C 的 Behavior/Weight 集成验收 | ✔ §4.4 明确否定，并保留 PlayerSergeant 例外与整个 `FormationAI.Tick` 的副作用 |
| 引入宿主依赖到 Core | ✔ §4.2 明确依赖方向；宿主方法名仅作证据引用 |
| 新增术语/状态机/模块名 | ✔ 沿用已有 A/B/C、Authority、Lease、Token、Generation、hold、OME |
| 读取或修改 CombatBrain | ✔ 零读取、零修改 |
