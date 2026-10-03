# 02 · Bannerlord 原生控制链研究

> Phase 2 交付物 · 2026-09-19
> 状态：**已完成（只读取证 + 建模，零代码）**
> 取证对象：`TaleWorlds.MountAndBlade.dll`（`E:\SteamLibrary\...\bin\Win64_Shipping_Client\`，文件时间戳 2026-08-23；**具体游戏版本号未采集，待补**）

---

## 0. 结论先行

五项判定结论：

| # | 问题 | 结论 | 证据等级 |
|---|---|---|---|
| 1 | Formation 的最终合法 Writer 是谁 | **不存在单一最终 Writer**。Formation 是**多入口共享可变资源**：Behavior / FormationAI / Formation 自维护 / OrderController / Team / MissionController 六类入口都能写 | IL实证 + 工程推断 |
| 2 | Tactic 如何影响 Formation | **Tactic 从不直接写 Order**。只通过 `FormationAI::ResetBehaviorWeights()` + `SetBehaviorWeight<T>()` 改权重 | IL实证 |
| 3 | FormationAI 如何选 Behavior | `w = GetAIWeight() × WeightFactor`，对**非当前行为**施加 `PreserveExpireTime` 迟滞折扣；由 **0.5s Timer** 驱动 | IL实证 |
| 4 | Behavior 何时写 Order | `CalculateCurrentOrder()` **不写**，只置 `CurrentOrder`；实际写入在 `TickOccasionally`（Movement+Facing）与 `OnBehaviorActivatedAux`（Movement+Facing+Arrangement+Firing+Form） | IL实证 |
| 5 | Order 写入口全集 | **6 个**：Movement / Facing / Arrangement / Firing / Form / Riding。传统"三域"只是子集 | IL实证 |

**最重要的结构性发现**：`IsAIControlled` 是一道**覆盖 Behavior 侧但不覆盖 Formation 自维护侧**的门控。这为 Phase 1 的「类 5 Native Pipeline Conflict」提供了具体的物理形态，也直接决定了 Phase 8 路线 B/C 的可行性边界。

---

## 1. 取证方法与工具状态

手段：`E:\MBmod\CAI\.tools\ilscan`（基于 `System.Reflection.Metadata` 的 IL 反汇编 + 调用方反查），两种模式：

- `dump <dll> [typeFilter] [methodFilter]` — 反汇编 IL
- `ref <dll> <methodName>` — 反查"谁调用了这个方法"（IL 子串匹配）

**本 Phase 修复了 ilscan 的 3 个缺陷**（原文件备份为 `ilscan.cs.orig`，已重新构建）：

| # | 缺陷 | 后果 | 修复 |
|---|---|---|---|
| 1 | `OperandSize` 用 `EndsWith("_S")` 匹配短格式指令，实际枚举名为 `_s` 大小写不一致 | 短分支（`Brfalse_s` 等）操作数未被跳过 → **整条方法体解码错位**，产出 `Conv_u8` / `Calli` 等伪指令 | 改 `EndsWith("_S", OrdinalIgnoreCase)` |
| 2 | `Switch` 的 `n` 未校验，`n*4` 可 int 溢出为负 | `il[i]` 索引越界 → **整个 scan 崩溃**（在 `Formation` 类型上必现） | 校验 `0 ≤ n ≤ (len-i-4)/4`，越界即放弃该方法 |
| 3 | `Ldc_R4` 浮点常量被当作元数据 token 解析 | 周期/阈值等常量不可读 | 补 `BitConverter.ToSingle` 分支 |

另加 per-method `try/catch`，单点解码失败不再中断整体扫描。

> ⚠️ **对后续 Phase 的提醒**：修复前产出的任何 IL 结论都不可信。若历史文档引用过 ilscan 输出，应复核。

**证据等级标注约定**（本 Phase 严格执行）：

- `[IL]` = ilscan 反汇编/反查直接得到
- `[推断]` = 由 IL 结构做的工程推断，未经运行时验证
- `[待验]` = 本 Phase 无法判定，需 Phase 10 Telemetry 实测

---

## 2. 原生控制链全景（已验证）

```text
Mission.AllowAiTicking (全局闸) ───────────────┐
Mission.ForceTickOccasionally (强制) ──────────┤
                                              ▼
TeamAI ──选 Tactic──▶ Tactic
                       │  ResetBehaviorWeights()
                       │  SetBehaviorWeight<T>()      ← 【不写 Order，只改权重】
                       ▼
              FormationAI._tickTimer (周期 0.5s)
                       ▼
              FormationAI::TickOccasionally
                       ├─ FindBestBehavior()          ← 评分选行为（含迟滞）
                       │      └─ set_ActiveBehavior()
                       │            ├─ 旧行为 OnBehaviorCanceled()
                       │            ├─ 新行为 OnBehaviorActivated() ──门控──▶ OnBehaviorActivatedAux()
                       │            │                                          └─ 写 5 个 Order
                       │            └─ PreserveExpireTime = CurrentTime + Δ
                       │
                       └─ if (Formation.IsAIControlled)  ──门控──▶ ActiveBehavior.TickOccasionally()
                                                                    └─ 写 Movement / Facing
                                       ┌──────────────────────────────┐
Formation::Tick（每 tick，无门控）────▶│ SetMovementOrder(GetSubstituteOrder(_movementOrder)) │
                                       │ ArrangementOrder::TickOccasionally                  │
                                       │ IFormationArrangement::OnTickOccasionally           │
                                       └──────────────────────────────┘
OrderController（玩家 / Sergeant）────▶ Set*Order（6 个入口全覆盖）
Team::Tick / RearrangeFormationsAccordingToFilter ──▶ SetMovementOrder
```

---

## 3. 判定结论 1：谁是最终合法 Writer

### 3.1 事实：没有单一 Writer `[IL]`

`Formation` 上 6 个公开写入口，调用方统计（`ilscan ref` 去重后的方法数）：

| 写入口 | 调用方数量 | 主要来源 |
|---|---|---|
| `SetMovementOrder` | **108** | ~40 个 `Behavior*`；`FormationAI::TickOccasionally`；`Formation::Tick`；`Formation::CopyOrdersFrom` / `ResetAux` / `SetControlledByAI`；`OrderController`（7 处）；`Team::Tick`；`Team::RearrangeFormationsAccordingToFilter`；多个 MissionController |
| `SetFacingOrder` | 71 | `Behavior*`；`OrderController`；`BattleDeploymentHandler` |
| `SetArrangementOrder` | 58 | `Behavior*`；`Formation::CopyOrdersFrom` / `ResetAux`；`OrderController` |
| `SetFiringOrder` | 54 | `Agent`；`Formation::AddUnit` / `RemoveUnit` / `CopyOrdersFrom` / `ResetAux`；`OrderController` |
| `SetFormOrder` | 60 | `Formation::ReapplyFormOrder`；**`Formation::SetArrangementOrder` 内部**；`Mission::SetFormationPositioningFromDeploymentPlan`；`OrderController` |
| `SetRidingOrder` | 8 | `Agent`；`Formation::AddUnit` / `RemoveUnit`；`Mission::SpawnAgent`；`OrderController` |

### 3.2 判定 `[推断]`

按"控制权"而非"调用次数"分层，实际存在 **4 类 Writer**：

| 类别 | 代表 | 是否有权被调度 | 是否受 AI 门控 |
|---|---|---|---|
| **决策 Writer** | `Behavior::TickOccasionally` / `OnBehaviorActivatedAux` | 需被 `FormationAI` 选中 | ✅ 是 |
| **准入 Writer** | `FormationAI::TickOccasionally`（切换时写 `CurrentOrder`） | 自身即调度者 | ✅ 是 |
| **常驻自维护 Writer** | `Formation::Tick`（SubstituteOrder 回写）、`ArrangementOrder::TickOccasionally`、IFormationArrangement | 无需调度 | ❌ **否** |
| **外部 Writer** | `OrderController`（玩家）、Mod（任意 `Set*Order`） | 无需调度 | ❌ 否 |

> **对 Phase 1 的回喂**：这正是「类 2 Authority Conflict」与「类 5 Native Pipeline Conflict」的物理基础。Mod 与外部 Writer 属于同一层——**原生链不认识 Mod 的 Authority 概念，也没有任何机制阻止 Mod 写入**。反过来说，Mod 也无法阻止后两类 Writer。

---

## 4. 判定结论 2：Tactic 如何影响 Formation

### 4.1 Tactic 不写 Order `[IL]`

在 `SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` 的全部调用方中，**没有任何 `Tactic*` 类型**（唯一含 "Tactic" 的匹配是 `BehaviorTacticalCharge`，它是 Behavior 不是 Tactic）。

`TacticComponent` 内出现的写入相关 API 仅：

```text
[SetDefaultBehaviorWeights]  → Callvirt FormationAI::SetBehaviorWeight
[SplitFormationClassIntoGivenNumber] → Call TacticComponent::SetDefaultBehaviorWeights
                                     → Callvirt FormationAI::ResetBehaviorWeights
```

### 4.2 Tactic 的唯一影响通道 `[IL]`

`ResetBehaviorWeights()` + `SetBehaviorWeight<T>()`。调用方全部是 Tactic 的战术分支方法（`Advance` / `Attack` / `Defend` / `Engage` / `OrganizedRetreat` …）。

### 4.3 Behavior 集合的注册时机 `[IL]`

`AddAiBehavior` 的调用方只有：

```text
TeamAIGeneral          :: OnUnitAddedToFormationForTheFirstTime
TeamAISallyOutAttacker :: OnUnitAddedToFormationForTheFirstTime
TeamAISallyOutDefender :: OnUnitAddedToFormationForTheFirstTime
TeamAISiegeAttacker    :: OnUnitAddedToFormationForTheFirstTime
TeamAISiegeDefender    :: OnUnitAddedToFormationForTheFirstTime
```

→ **Behavior 集合在单位首次入队时一次性注册；此后 Tactic 只改权重，不增删 Behavior。**

**意义**：这解释了为什么"改权重"是原生推荐的影响方式（HANDOFF §10.6 的 Route A）——因为 Behavior 集合本身是组建期确定的，运行期唯一可调的就是权重。

### 4.4 未取证项

Tactic 的**切换周期与选择机制**（TeamAI 侧，如 `GetTacticWeight`）本 Phase 未取证。理由：Tactic 不直接写 Order，对 Authority 问题的影响是间接的（通过权重）。**列为 Phase 3（RBM 案例）顺带补齐，不阻断 Phase 5。**

---

## 5. 判定结论 3：FormationAI 如何选 Behavior

### 5.1 驱动节奏 `[IL]`

```il
// FormationAI::Tick
if (!Mission.Current.AllowAiTicking) return;                 // 全局闸
if (Mission.Current.ForceTickOccasionally
    || _tickTimer.Check(Mission.Current.CurrentTime))
        TickOccasionally(_tickTimer.PreviousDeltaTime);
```

**周期常量来自 `FormationAI::.ctor`** `[IL]`（浮点已解码）：

```il
Ldc_r4 0.5 ; Ldc_i4_1 ; Newobj TaleWorlds.Core.Timer::.ctor
Stfld FormationAI::_tickTimer
```
→ **周期 = 0.5 s（约 2 Hz）**

**错峰**：`stagger = 0.1 × FormationIndex`，另有 `0.5 × TeamIndex × 0.1` 项，起始时间为 `CurrentTime + 0.5 × stagger`。目的是避免所有 Formation 同帧重算 `[推断]`。

> **量化价值**：Phase 1 判定「Authority Conflict 的 oscillation ≈ 冲突源评估频率」——现在有了具体预期值：**与原生链冲突时，抖动周期应约为 0.5 s**。这使 Phase 7/10 的 `oscillation_frequency` 指标有了可检验的基线。

### 5.2 选行为算法（`FindBestBehavior`）`[IL]`

```il
best = null; bestW = 0
foreach (b in _behaviors):
    if (b.WeightFactor != 0):
        w = b.GetAIWeight() * b.WeightFactor                 // 核心评分
        if (b != ActiveBehavior):
            ... b.PreserveExpireTime, Mission.Current.CurrentTime ...  // 迟滞折扣
        ... b.NavmeshlessTargetPositionPenalty ...            // 惩罚：先除
        b.PrecalculateMovementOrder()                         // 预计算
        ... * NavmeshlessTargetPositionPenalty                // 再乘回
        if (w > bestW): best = b; bestW = w
if (best != null):
    set_ActiveBehavior(best)
    _behaviors.Remove(best); _behaviors.Insert(0, best)       // 选中项移到队首
    return true
return false
```

**三个要点**：

1. **评分 = `GetAIWeight() × WeightFactor`** —— 这是 Tactic 影响 Formation 的唯一落点。
2. **存在迟滞（hysteresis）**：非当前 Behavior 会被 `PreserveExpireTime` 相关逻辑打折，即**当前行为有保持优势**。这是原生自带的防抖机制，与 Phase 1 的「类 7 Oscillation」直接相关——**原生链并非每 0.5s 无条件切换行为**。
3. `NavmeshlessTargetPositionPenalty` 参与评分（先除后乘，`PrecalculateMovementOrder()` 夹在中间）`[推断]`：这是对"目标点不在导航网格上"的行为惩罚。

### 5.3 切换时发生什么（`set_ActiveBehavior`）`[IL]`

```il
if (_activeBehavior == value) return;                 // 幂等
if (_activeBehavior != null) _activeBehavior.OnBehaviorCanceled();
_activeBehavior = value;
_activeBehavior.OnBehaviorActivated();                // ← 内含 IsAIControlled 门控
_activeBehavior.PreserveExpireTime = Mission.Current.CurrentTime + Δ;
OnActiveBehaviorChanged?.Invoke(...);                 // 事件（可用于 Telemetry）
```

---

## 6. 判定结论 4：Behavior 何时写 Order

### 6.1 `CalculateCurrentOrder()` 不写 Order `[IL]`

```il
// BehaviorComponent::CalculateCurrentOrder
0: Ret                    // 基类为空虚方法，子类重写
```
```il
// BehaviorComponent::PrecalculateMovementOrder
Callvirt BehaviorComponent::CalculateCurrentOrder
Call BehaviorComponent::get_CurrentOrder
Stloc_0
... MovementOrder::GetPosition ...
Ret
```
→ `CalculateCurrentOrder()` 只负责**计算并置 `CurrentOrder` 属性**，不触碰 Formation。

### 6.2 真正写入的位置 `[IL]`（以 `BehaviorAdvance` 为样本）

| 方法 | 行为 |
|---|---|
| `CalculateCurrentOrder` | `set_CurrentOrder`（不写 Formation） |
| `TickOccasionally` | `CalculateCurrentOrder()` → `SetMovementOrder` + `SetFacingOrder` |
| `OnBehaviorActivatedAux` | `CalculateCurrentOrder()` → `SetMovementOrder` + `SetFacingOrder` + `SetArrangementOrder` + `SetFiringOrder` + `SetFormOrder` |

`OnBehaviorActivatedAux` 基类为空虚方法（`Ret`），由子类重写 `[IL]`；调用链为 `OnBehaviorActivated → OnBehaviorActivatedAux`（唯一调用方）`[IL]`。

### 6.3 非当前 Behavior 是否仍产单 `[IL]`

**否**。`FindBestBehavior` 只对候选项调用 `PrecalculateMovementOrder()`（计算），**写入只发生在 `ActiveBehavior` 上**（`TickOccasionally` / `OnBehaviorActivatedAux` 均由 `ActiveBehavior` 触发）。

> 但注意：`PrecalculateMovementOrder()` 会被所有 `WeightFactor != 0` 的候选行为调用 → **非当前 Behavior 仍会执行计算，产生 CPU 开销，但不写 Order**。

### 6.4 三域是否分别维护 `[IL]`

- 状态上**分别维护**：`_movementOrder` / `FacingOrder` / `ArrangementOrder` 为独立字段与独立 setter。
- 但**写入时机耦合**：`OnBehaviorActivatedAux` 一次写齐 5 个域。
- **维护节奏不同**：
  - Movement：`Behavior` 决策（受 AI 门控）+ `Formation::Tick` 常驻回写（**不受门控**）
  - Arrangement：`ArrangementOrder::TickOccasionally` + `IFormationArrangement::OnTickOccasionally` 各自独立 Timer（**不受门控**）
  - Facing：未见常驻自维护路径，仅由 Behavior 写入

→ **这是 Q4「三域是否应有独立 Authority」的直接结构性依据**：三域的原生写入路径在结构与门控上本就不同，用同一个 Authority 覆盖三个域会与原生结构错配。

---

## 7. 判定结论 5：Order 写入口全集

**6 个**（传统"Movement / Facing / Arrangement"三域只是子集）：

```text
Formation::SetMovementOrder      ← Movement
Formation::SetFacingOrder        ← Facing
Formation::SetArrangementOrder   ← Arrangement（内部还会调 SetFormOrder）
Formation::SetFiringOrder        ← 射击
Formation::SetFormOrder          ← 阵型形状（内部被 Arrangement 触发）
Formation::SetRidingOrder        ← 骑乘
```

**其他影响 Formation 状态的入口**（非 Order，但对 Authority 同样重要）：

| 入口 | 说明 |
|---|---|
| `Formation::SetControlledByAI(bool, bool)` | **AI 门控开关**，调用方含 `OrderController::BeforeSetOrder` / `SetOrder`、`Team::DelegateCommandToAI`、`Team::AssignPlayerAsSergeantOfFormation`、`Team::SetPlayerRole`、`Team::AddTeamAI` |
| `Formation::SetPositioning` / `SetOrderPosition` | 定位类 |
| `Formation::CopyOrdersFrom` | 批量复制 6 类 Order + `SetControlledByAI` |
| `Formation::ResetAux` | 重置时写回多个 Order |
| `Formation::ReapplyFormOrder` | 重应用 FormOrder |
| `Mission::AllowAiTicking` | **全局 AI 闸**（字段） |

**可用于 Telemetry 的现成事件** `[IL]`：

```text
Formation::OnBeforeMovementOrderApplied    （add/remove 均存在）
Formation::OnAfterArrangementOrderApplied  （add/remove 均存在）
FormationAI::OnActiveBehaviorChanged       （add/remove 均存在）
```

→ Phase 9/10 的 Harmony 探针除 Patch `Set*Order` 外，还应挂这三个事件，可低成本获得"谁触发了 Order 变化"的上下文。

---

## 8. 门控矩阵：`IsAIControlled` 覆盖到哪里

**这是本 Phase 对 Phase 8/10 最有价值的产出。**

| 写入路径 | 受 `IsAIControlled` 门控? | 证据位置 |
|---|---|---|
| `ActiveBehavior.TickOccasionally()`（写 Movement/Facing） | ✅ **是** | `FormationAI::TickOccasionally`：`get_IsAIControlled` → `Brtrue_s` 后才调用 |
| `OnBehaviorActivatedAux()`（写 5 个 Order） | ✅ **是** | `BehaviorComponent::OnBehaviorActivated`：`get_IsAIControlled` → `Brfalse_s 6` 跳过 |
| FormationAI 切换时 `SetMovementOrder(CurrentOrder)` | ✅ **是** | `FormationAI::TickOccasionally` 内 `Brfalse_s` 跳过 |
| `AI.Tick()` 入口 | ✅ **是**（或 `Team.IsPlayerSergeant`） | `Formation::Tick`：`if (IsAIControlled \|\| Team.IsPlayerSergeant) AI.Tick()` |
| `Formation::Tick` 的 `SetMovementOrder(GetSubstituteOrder(...))` | ❌ **否** | 位于 `Formation::Tick` 非门控段，每 tick 执行 |
| `ArrangementOrder::TickOccasionally` | ❌ **否** | 独立 Timer，无门控 |
| `IFormationArrangement::OnTickOccasionally` | ❌ **否** | 独立 Timer，无门控 |
| `OrderController::SetOrder` 系列 | ❌ 否 | 玩家/指令路径 |
| `Team::Tick` / `RearrangeFormationsAccordingToFilter` | ❌ 否（未见门控） | — |

### 8.1 对 §10.6 阻断级 PoC 的回应

HANDOFF §10.6 遗留问题：**`SetControlledByAI(false)` 后，Facing / Arrangement 是否仍被原生残留覆盖？**

本 Phase 的 IL 证据给出**部分回答** `[推断]`：

- **Behavior 侧的 Facing / Arrangement 写入会被挡住**（`OnBehaviorActivatedAux` 门控 + `TickOccasionally` 门控）。
- **但 Arrangement 存在完全不受门控的常驻自维护路径**（`ArrangementOrder::TickOccasionally` + `IFormationArrangement::OnTickOccasionally`），Movement 也存在不受门控的 `GetSubstituteOrder` 回写。

因此 PoC 的答案**倾向于"会有残留"**，但 `[推断]` **不得作为结论**——原因：

1. 常驻自维护是否真的**改变 Order 值**（而非幂等重算）无法从 IL 判定；
2. `GetSubstituteOrder` 的语义依赖 `MovementOrderEnum` 取值（见 §9）；
3. 运行时是否还有其它未静态可见的写入路径未知。

**明确要求**：该 PoC **必须在 Phase 10 用 Telemetry 实测**（记录 `Original Method Executed?` / `Order Changed?` / `Previous Order` / `New Order`），不得沿用本 Phase 的推断，也不得沿用 §10.6 的旧推论。

### 8.2 一个有用的旁路发现 `[IL]`

`Formation::Tick` 的常驻回写：

```il
Ldflda Formation::_movementOrder
Call MovementOrder::GetSubstituteOrder
Call Formation::SetMovementOrder
```

`MovementOrder::GetSubstituteOrder` 的实现：

```il
if (OrderEnum == 2) return MovementOrderStop;
return MovementOrderCharge;
```

→ 当 MovementOrder 处于枚举值 2 时会被替换为 Stop，其余被替换为 Charge。

> ⚠️ **2026-09-19 更正（详见 `03-rbm-case-study.md` 附录 A1）**：本段原先判定「每 tick 运行的写入点」是**误读**。
> `Blt_s 215` 实为有符号 −41，跳回偏移 170 形成循环。真实结构是：
> ```csharp
> for (int i = 0; i < 10 && !MovementOrder.IsApplicable(ref _movementOrder, this); i++)
>     SetMovementOrder(MovementOrder.GetSubstituteOrder(ref _movementOrder, this));
> ```
> 即**兜底修复循环**：仅当当前 MovementOrder **不适用**时触发，单帧最多 10 次。
> 对合法 Order（HOLD/ADVANCE/RETREAT）**不会覆盖** → §8.1 的倾向性判断理由被削弱，
> 但 Arrangement 两个独立 Timer 不受门控仍成立，**PoC 仍需 Phase 10 实测**。

**枚举值 2 的语义**：据 RBM 源码副本应为 `AttackEntity` `[源码]+[推断]`，**非游戏端实测**，Phase 9 需在 Probe 中确认（见 `03-rbm-case-study.md` 附录 A2）。

---

## 9. 本 Phase 无法判定 / 待验证清单

| # | 项 | 为何未定 | 何时解决 |
|---|---|---|---|
| 1 | `MovementOrderEnum` 中值 `2` 的语义 | ilscan 不输出枚举字段 | Phase 9（Probe 里可直接读取枚举名） |
| 2 | Tactic 切换周期与 `GetTacticWeight` 机制 | 不直接写 Order，对 Authority 影响间接 | Phase 3 顺带 |
| 3 | `GetSubstituteOrder` 实际是否改变 Order 值 | 需运行时观测 | Phase 10 Telemetry |
| 4 | Arrangement 常驻自维护是否覆盖外部写入 | 同上 | **Phase 10（阻断级）** |
| 5 | Timer stagger 精确公式 | IL 可推但含 TeamIndex 分支，收益低 | 可不做 |
| 6 | 多人/网络路径（`GameNetwork` 分支） | 不在本项目范围 | 不处理 |
| 7 | `Behavior::TickOccasionally` 子类内是否有二次门控 | 样本只查了 `BehaviorAdvance` | Phase 3（RBM 自定义 Behavior 时顺带） |
| 8 | 游戏版本号 | DLL 时间戳 2026-08-23，未采集版本号 | 下次补（影响结论可复现性） |

---

## 10. 对后续 Phase 的输入

### 10.1 Phase 5（实验设计）

- **抖动基线可量化**：与原生链冲突时 `oscillation_frequency` 预期 ≈ 2 Hz（0.5s 周期）。
- **迟滞机制已有原生实现**：`PreserveExpireTime` + 非当前行为折扣。实验⑦（Oscillation）应设对照组：**"外部 Mod 无迟滞" vs "原生迟滞"**，验证迟滞是否是可迁移的通用机制。
- **写入口是 6 个不是 3 个**：实验⑧（Domain Ownership）若只按三域划分，会遗漏 Firing/Form/Riding 三个域的 Authority 问题。

### 10.2 Phase 8（Architecture A/B/C）

- **路线 C（Native Pipeline Integration）有了明确挂载点**：`SetBehaviorWeight<T>` / `ResetBehaviorWeights` / 自定义 Behavior + `AddAiBehavior` / 重写 `CalculateCurrentOrder`。其中 `CalculateCurrentOrder` 是**唯一"只计算不写入"的纯函数式注入点**，耦合最低。
- **路线 B（Full Takeover + Harmony 抑制）的 Patch 面可估算**：至少需覆盖 `Behavior::TickOccasionally`、`OnBehaviorActivatedAux`、`FormationAI::TickOccasionally` 的写入段、`Formation::Tick` 的 SubstituteOrder 回写、Arrangement 两个常驻 Timer。**Patch 点多且分散** → 路线 B 实现复杂度显著高于直觉。
- **路线 A（External Commander）几乎必然抖动**：因为它要同时对抗 6 类入口中的决策 Writer + 常驻 Writer。

### 10.3 Phase 9/10（Probe 与 Runtime 实验）

- 必挂 Telemetry 点：`Set*Order`（6 个）+ `FormationAI::TickOccasionally` + `OnBehaviorActivatedAux` + `Behavior::TickOccasionally` + `Formation::Tick`（SubstituteOrder 段）+ Arrangement 两个 Timer 回调。
- 必挂事件（低成本高信息量）：`OnBeforeMovementOrderApplied` / `OnAfterArrangementOrderApplied` / `OnActiveBehaviorChanged`。
- **Acquire/Release 全流程必测项新增一条**：Release 后不仅要验证 `IsAIControlled` 恢复，还要验证 `PreserveExpireTime` 与 `_behaviors` 队首顺序是否残留（因为 `FindBestBehavior` 会把选中行为 `Remove` 后 `Insert(0)`，这是**结构性副作用**）`[IL]`。

---

## 11. 本 Phase 明确的「不做」

- 未读 RBM 反编译源码（Phase 3）
- 未读 CombatBrain 源码（Phase 12）
- 未写任何代码（BannerlordProbe 属 Phase 9）
- 未做运行时验证（Phase 10）
- 未引用 `CombatBrain.*`；本 Phase 对 `TaleWorlds.*` 的**只读静态分析**属取证范畴，不涉及代码引用（红线 D1 针对的是 Core/Simulation/Telemetry 工程）
