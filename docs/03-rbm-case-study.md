# 03 · RBM 工程案例研究

> Phase 3 交付物 · 2026-09-19
> 状态：**已完成（只读取证 + 建模，零代码）**
> 取证对象：`E:\MBmod\FIX\rbm_decompiled\RBMAI\`（59 个 .cs）+ `E:\MBmod\IDEA\RBM\`

---

## 0. 结论先行：9 个必答项

| # | 问题 | 答案 | 关键证据 |
|---|---|---|---|
| 1 | 用了哪些 Harmony Patch | **81 个 patch 方法**（40 Prefix / 41 Postfix），覆盖 **51 个目标类型** | `RBMAiPatcher.cs:18` `harmony.PatchAll()`，patch 以属性声明 |
| 2 | Patch Tactic 还是 Behavior | **两者都 Patch**：**17 个 Behavior 类** + **10 个 Tactic / TacticComponent 类**。但**写入只发生在 Behavior 侧** | 类型枚举 |
| 3 | 是否改 Weight | **是**。大量 `SetBehaviorWeight<T>` + `ResetBehaviorWeights()`，集中在 Tactic patch 内；并**完全替换** `TacticComponent::SetDefaultBehaviorWeights`（`return false`） | `TacticDefensiveEngagementPatch.cs:25-72`；`Tactics.cs:655-676` |
| 4 | 是否替换 `CalculateCurrentOrder()` | **是**，10+ 个 Behavior 的该方法被 **Prefix `return false`** 整体替换 | `OverrideBehaviorAdvance.cs:31` 等 |
| 5 | 是否直接 SetOrder | **是**，但**仅在 Behavior patch 内部**（`TickOccasionally` / `OnBehaviorActivatedAux`）。**无任何外部独立写入点** | `OverrideBehaviorAdvance.cs:378/406/410` |
| 6 | 是否 Prefix `return false` | **是，且是主要手段**（`CalculateCurrentOrder` / `TickOccasionally` / `SetDefaultBehaviorWeights` / `ManageFormationCounts` 全部 `return false`） | `OverrideBehaviorAdvance.cs:345/411` |
| 7 | 是否仍用 FormationAI 仲裁 | **是**。`typeof(FormationAI)` **零命中**——RBM 从不 Patch FormationAI | 全库 grep |
| 8 | 是否建独立 Commander | **否**。无 `Commander` / `Arbiter` / `Proposal` / `Executor` 类 | grep 零命中 |
| 9 | 是否减少 Writer 数量 | **不是"减少"，而是"不增加"（净增 0）** | 见 §5 |

### H-B 假设判定

> **H-B（原始表述）**：RBM 不抢指令不是因为 Arbiter 更强，而是因为更深集成进原生链、结构上减少了独立 Writer。

**判定：方向支持，表述需修正。**

修正后的准确表述：

> RBM 的稳定性来自 **"替换 Writer 而非新增 Writer"**：它用 Prefix `return false` 逐个接管 Behavior 的实现，把原生逻辑换成自己的逻辑，**写入点位置不变、Writer 数量不变（净增 0）**，仲裁权仍完全留给 `FormationAI`。

**为什么这个区分重要**：减少 Writer 与不增加 Writer 是两种不同的工程路径，后者**可复制性远高于前者**——因为"减少"意味着要对抗原生链（Phase 8 路线 B），而"不增加"只需要接入原生链（Phase 8 路线 C）。RBM 走的是后者。

---

## 1. 取证范围与限制

- 主源：`E:\MBmod\FIX\rbm_decompiled\RBMAI\`（59 个 .cs，含 `RBMAI/`、`RBMAI.AiModule/`、`AiModule.RbmBehaviors/`、`AiModule.RbmTactics/`）
- 工具：静态 grep / 源码阅读（未编译、未运行）
- **限制**：
  1. 反编译产物含大量 `//IL_xxxx: Unknown result type` 注释，说明**局部类型推断失败**，但不影响控制流与 API 调用的可读性；
  2. **未做运行时验证** —— "RBM 实际不抖动"这一经验结论本 Phase **既不证实也不证伪**，属 Phase 10 范畴；
  3. RBM 版本号未采集。

---

## 2. 逐项详答

### Q1 · Harmony Patch 清单

`RBMAiPatcher.DoPatching()` 只有三行，靠属性批量注册：

```csharp
Harmony harmony = new Harmony("com.rbmai");
harmony.UnpatchAll(harmony.Id);
harmony.PatchAll();                       // ← 全部 patch 以 [HarmonyPatch] 属性声明
```

另有 `FirstPatch()` 手工注册两条 `EarlyStart` Postfix（用于注入自定义 Tactic 的时机）。

**规模**：40 Prefix + 41 Postfix，51 个目标类型。前几位：`Mission`(7)、`Agent`(7)、`HumanAIComponent`(5)、`TacticComponent`(4)、`Formation`(4)、`MissionState`(2)、`ArrangementOrder`(2)。

### Q2 · Patch 的是 Tactic 还是 Behavior

**两者都 Patch，但分工明确**：

| 层 | Patch 数 | 作用 |
|---|---|---|
| **Tactic / TacticComponent** | 10 个类 | **只改权重**：`SetBehaviorWeight<T>` / `ResetBehaviorWeights()`；替换 `SetDefaultBehaviorWeights` / `ManageFormationCounts`；注册自定义 Tactic |
| **Behavior** | 17 个类 | **替换决策与写入**：`CalculateCurrentOrder` / `TickOccasionally` / `OnBehaviorActivatedAux` / `GetAiWeight` |
| **Formation / MovementOrder / ArrangementOrder** | 各 1–4 | 值变换与定位修正（**不新增 Writer**，见 §4） |

被 Patch 的 Behavior（17）：`BehaviorAdvance` `AssaultWalls` `CautiousAdvance` `Charge` `Defend` `HoldHighGround` `HorseArcherSkirmish` `MountedSkirmish` `ProtectFlank` `PullBack` `Regroup` `ScreenedSkirmish` `ShootFromCastleWalls` `SkirmishLine` `UseSiegeMachines` `Vanguard` `WaitForLadders`

**自定义 Tactic 的注册走原生机制**：

```csharp
item.AddTacticOption((TacticComponent)(object)new RBMTacticEmbolon(item));      // Tactics.cs:222
item.AddTacticOption((TacticComponent)(object)new RBMTacticAttackSplitInfantry(item));
// ...共 7 个 RBMTactic*
```

→ RBM **新增战术但不新增战术机制**，仍挂在原生 `TeamAIComponent` 的战术选择里。

### Q3 · 是否改 Weight：**是，且这是主要影响通道**

```csharp
// TacticDefensiveEngagementPatch.cs:25-28
____archers.AI.SetBehaviorWeight<BehaviorSkirmish>(0f);
____archers.AI.SetBehaviorWeight<BehaviorSkirmishLine>(0f);
____archers.AI.SetBehaviorWeight<BehaviorScreenedSkirmish>(1f);
____archers.AI.SetBehaviorWeight<BehaviorRegroup>(1.25f);
// ... 每个兵种编队挨个设置，配合 ResetBehaviorWeights()
```

并**整体替换**默认权重表：

```csharp
// Tactics.cs:655-676
[HarmonyPrefix][HarmonyPatch("SetDefaultBehaviorWeights")]
private static bool PrefixSetDefaultBehaviorWeights(ref Formation f) {
    if (f != null) {
        f.AI.SetBehaviorWeight<BehaviorCharge>(f.QuerySystem.IsRangedFormation ? 0.2f : 1f);
        f.AI.SetBehaviorWeight<BehaviorPullBack>(0f);
        f.AI.SetBehaviorWeight<BehaviorStop>(0f);
        f.AI.SetBehaviorWeight<BehaviorReserve>(0f);
    }
    return false;                     // ← 原生默认权重表不再执行
}
```

**对照 Phase 2 结论**：Phase 2 已证 `FindBestBehavior` 的评分是 `GetAIWeight() × WeightFactor`。RBM 正是只改这里的 `WeightFactor` 与 `GetAIWeight`（后者通过 Patch 若干 Behavior 的 `GetAiWeight`），**完全契合原生设计意图**。

### Q4 · 是否替换 `CalculateCurrentOrder()`：**是**

以 `BehaviorAdvance` 为例（`OverrideBehaviorAdvance.cs:31`）：

```csharp
[HarmonyPrefix][HarmonyPatch("CalculateCurrentOrder")]
private unsafe static bool PrefixCalculateCurrentOrder(
    ref BehaviorAdvance __instance,
    ref MovementOrder ____currentOrder,        // 直接改私有字段
    ref FacingOrder ___CurrentFacingOrder)
{
    ...
    ____currentOrder = MovementOrder.MovementOrderMove(cachedMedianPosition5);
    ___CurrentFacingOrder = FacingOrder.FacingOrderLookAtDirection(val9);
    return false;                              // ← 原生实现不执行
}
```

**注意形态**：它**只写私有字段**（`____currentOrder` / `___CurrentFacingOrder`），**不碰 Formation**。这与 Phase 2 的结论完全一致——`CalculateCurrentOrder` 本就是"只算不写"的环节，RBM 保持了这一契约。

另有 `TickOccasionally` 通过反射调用被 patch 过的版本：

```csharp
CalculateCurrentOrderMethod.Invoke(__instance, new object[0]);   // OverrideBehaviorAdvance.cs:376
```

### Q5 · 是否直接 SetOrder：**是，但只在 Behavior 内部**

全部 `Set*Order` 调用点（grep 结果）**无一例外位于 `OverrideBehavior*.cs` 的 `TickOccasionally` 或 `OnBehaviorActivatedAux` 内**：

```csharp
// OverrideBehaviorAdvance.cs:376-410（TickOccasionally，Prefix return false）
CalculateCurrentOrderMethod.Invoke(__instance, new object[0]);
Formation.SetMovementOrder(CurrentOrder);                    // ①
if (IsInfantryFormation) {
    switch (ArrangementOrder.OrderType - 16) { ... SetFormOrder(...) }   // ②
    if (dist < 150f && count >= 30) {
        SetFacingOrder(___CurrentFacingOrder);                // ③
        Utilities.DecideArrangementOrderForFormation(...);     // ④
        SetMovementOrder(____currentOrder);                   // ⑤
    }
}
Formation.SetMovementOrder(CurrentOrder);                     // ⑥ 又写一次
return false;
```

**三个观察**：

1. **写入点位置与原生完全对应**（原生 `BehaviorAdvance::TickOccasionally` 也是写 Movement + Facing）——RBM 是**就地替换**，不是另起通道。
2. **一个 Tick 内对同一域写了 3 次**（①⑤⑥）。因为 RBM 是唯一 Writer，重复写**不产生冲突，但也完全冗余**——这正好印证红线「靠更高频写入实现 Authority」的荒谬：在无人竞争时，多写几次毫无意义；在有人竞争时，多写几次只是竞速。
3. `Utilities.DecideArrangementOrderForFormation()` 内部会 `SetArrangementOrder`（`OverrideBehaviorRegroup.cs:58/79` 等处亦可见）——**Arrangement 的写入权仍在 Behavior 手里**，与原生一致。

### Q6 · 是否 Prefix `return false`：**是，且是主要手段**

| 被抑制的原方法 | 位置 |
|---|---|
| `Behavior*::CalculateCurrentOrder` | 10+ 处 |
| `Behavior*::TickOccasionally` | `OverrideBehaviorAdvance.cs:353` 等 |
| `Behavior*::OnBehaviorActivatedAux` | `ProtectFlank:378`、`ScreenedSkirmish:91`、`SkirmishLine:23`、`Vanguard:50` |
| `TacticComponent::SetDefaultBehaviorWeights` | `Tactics.cs:655` |
| `TacticComponent::ManageFormationCounts` | `Tactics.cs:680` |
| `MovementOrder::GetSubstituteOrder` | `OverrideMovementOrder.cs:32`（**条件性** return false） |

**关键区分**：`return false` 有两种用法，RBM 两种都用了，但目的不同——

- **接管型**（Behavior / Tactic）：无条件 `return false`，**替换**原逻辑；
- **条件型**（`GetSubstituteOrder`）：仅在特定条件下 `return false`，否则放行原生逻辑：

```csharp
// OverrideMovementOrder.cs:33-54
if (formation != null && (IsInfantryFormation || IsRangedFormation) && OrderType == 5) {
    __result = (ClosestSignificantlyLargeEnemy != null)
        ? MovementOrderChargeToTarget(...)
        : MovementOrderCharge;
    return false;              // 只在这种情况下接管
}
return true;                   // 其余放行原生
```

### Q7 · 是否仍用 FormationAI 仲裁：**是**

`typeof(FormationAI)` **在全部 59 个文件中零命中**。RBM 对仲裁层**一行未动**。

→ 行为选择仍完全由 `FormationAI::FindBestBehavior` 决定，RBM 只能通过 Phase 2 已证的两个旋钮影响它：`GetAIWeight()` 与 `WeightFactor`。

### Q8 · 是否建独立 Commander：**否**

`Commander` / `Arbiter` / `Proposal` / `Executor` 类名 grep **全部零命中**。

RBM 的控制结构里**没有任何集中的决策或仲裁组件**——决策分散在 17 个 Behavior patch 里，仲裁交给原生 FormationAI。

> **对 CombatBrain 的直接含义**：CombatBrain 现有的 `Proposal / Arbiter / Executor` 结构**与 RBM 的做法是两条相反的路**。这不代表 CombatBrain 错了（RBM 只是案例，不是最佳实践），但**它意味着 CombatBrain 的结构无法从 RBM 那里获得背书**。

### Q9 · Writer 数量对比

| Writer | 原生 | RBM 之后 | 变化 |
|---|---|---|---|
| `Behavior::TickOccasionally` | 1（ActiveBehavior） | 1（**被替换内容**） | 数量不变 |
| `Behavior::OnBehaviorActivatedAux` | 1 | 1（**被替换内容**） | 数量不变 |
| `FormationAI` 切换写 | 1 | 1（未动） | 不变 |
| `Formation::Tick` SubstituteOrder 兜底 | 1（条件） | 1（**条件性接管**） | 不变 |
| `OrderController` | 1 | 1（未动） | 不变 |
| **Mod 独立 Commander** | — | **0（不存在）** | **+0** |

→ **净增 Writer = 0**。RBM 改变的是"**写什么**"，不是"**谁来写**"。

### 一个特殊的非 Writer 组件：写入口值变换

```csharp
// OverrideSetMovementOrder.cs —— Prefix on Formation::SetMovementOrder
[HarmonyPrefix][HarmonyPatch("SetMovementOrder")]
private static bool PrefixSetOrder(Formation __instance, ref MovementOrder input) {
    try {
        if (__instance == null || __instance.IsDeployment || ...) return true;
        if (Mission.Current.IsFieldBattle && (int)input.OrderType == 4
            && __instance.QuerySystem.ClosestSignificantlyLargeEnemyFormation != null)
                input = MovementOrder.MovementOrderChargeToTarget(...);   // ← 只改值
        return true;                                                       // ← 原方法照常执行
    } catch (Exception) { return true; }                                   // 失败即放行
}
```

**这是本次调查最有价值的新发现**：一个**与 Writer 完全解耦的值变换层**——

- 它不改"谁有权写"，也不改"写几次"；
- 它只改"写进去的内容"；
- 它**从不 `return false`**（永远放行），因此不破坏任何 Writer；
- 它带 try/catch 兜底，失败即退化为原生行为。

→ **这是一个独立于 Authority 的正交维度**，现有 Phase 1 的五类冲突模型**没有涵盖它**。见 §6。

---

## 3. RBM 控制权架构模式提炼

```
                 ┌─────────────────────────────────────────────┐
                 │  Mission.Current / TeamAI（原生，未动）        │
                 └──────────────────┬──────────────────────────┘
                                    │
        ┌───────────────────────────▼───────────────────────────┐
        │ Tactic（原生 + RBM 自定义，走 AddTacticOption 注册）      │
        │   SetBehaviorWeight / ResetBehaviorWeights             │
        │   ★ RBM 替换 SetDefaultBehaviorWeights（return false）   │
        └───────────────────────────┬───────────────────────────┘
                                    │ 只传权重
        ┌───────────────────────────▼───────────────────────────┐
        │ FormationAI（★ RBM 一行未动 —— 仲裁权完全保留）           │
        │   FindBestBehavior: GetAIWeight × WeightFactor + 迟滞    │
        └───────────────────────────┬───────────────────────────┘
                                    │ 选中 ActiveBehavior
        ┌───────────────────────────▼───────────────────────────┐
        │ Behavior（★ RBM 用 Prefix return false 就地替换）        │
        │   CalculateCurrentOrder → 只写私有字段（不碰 Formation）  │
        │   TickOccasionally      → SetMovementOrder / Facing ...  │
        └───────────────────────────┬───────────────────────────┘
                                    │
        ┌───────────────────────────▼───────────────────────────┐
        │ Formation::SetMovementOrder                            │
        │   ★ RBM Prefix 只做值变换，永远 return true              │
        └───────────────────────────────────────────────────────┘
```

**三条可迁移的原则** `[推断]`：

1. **仲裁权不下沉**：不动 `FormationAI`，就不需要自己解决"多个 Behavior 谁上"的问题。
2. **替换而非并列**：要改行为就 `return false` 替换原实现，而不是在旁边加一个写入者。
3. **计算与写入分离**：`CalculateCurrentOrder` 只算不写，写入留给 `TickOccasionally`——RBM 严格保持了这一原生契约。

---

## 4. 对 Phase 5（实验设计）的输入

新增两组候选实验，源于本次调查：

| 候选 | 研究问题 | 假设 |
|---|---|---|
| **E9 · Replace vs Append** | 替换 Writer（Prefix return false）与并列新增 Writer，在 `oscillation_frequency` / `unauthorized_writes` 上是否有量级差异？ | 替换式 `unauthorized_writes ≈ 0`；并列式显著 > 0 |
| **E10 · Value Transform 层** | 在写入口做值变换（不取得 Authority）能否达成 Mod 目标？它的能力边界在哪？ | 能达成"改写什么"，不能达成"不让他写" |

---

## 5. 对 Phase 8（Architecture A/B/C）的输入

| 路线 | 与 RBM 的关系 | 本 Phase 给出的证据 |
|---|---|---|
| **A · External Commander** | **RBM 明确没走这条路** | 无 Commander 类；无独立 SetOrder 点 |
| **B · Full Takeover + Harmony 抑制** | **RBM 没走这条路** —— 它不抑制原生，而是替换 | 所有 `return false` 都是"替换本方法"，不是"压制其他 Writer" |
| **C · Native Pipeline Integration** | **RBM 就是这条路** | 挂在 Behavior / Tactic 权重上；FormationAI 未动 |

→ **路线 C 获得了一个真实工程案例的支持**。但必须强调：这是**一个案例**，不构成证明；Phase 8 仍需按 12 个维度独立比较，**不得预设赢家**。

**额外发现**：RBM 的 `positionsStorage`（`static Dictionary<Formation, WorldPosition>`）是**挂在静态字典上的 Mod 侧状态**。这为 Phase 10 的「Release 后是否有残留状态」提供了一个真实的检查项——**Mod 侧缓存不会随控制权释放自动清理**。

---

## 6. Phase 1 冲突模型需要增补一类（重要）

RBM 的 `OverrideSetMovementOrder` 展示的机制**不属于 Phase 1 五类冲突中的任何一类**：

- 它不是 Command Conflict（没有两个候选值在竞争，而是**同一个值被改写**）
- 它不是 Authority Conflict（**没有取得任何权限**，也从不阻止谁写）
- 它不是 Intent / Plan / Pipeline Conflict

**建议增补为第六类**：

> **6. Content Transformation（内容变换）** — 在写入边界对命令值做等价替换，不改变写入者、不改变写入频率、不取得 Authority。
> 性质：**正交于控制权**。可与任意 Authority 方案共存。
> 解决手段：写入口 Prefix（永远放行）。
> 局限：**只能改"写什么"，不能改"谁在写""何时写""写不写"**。

**待决定**：是否增补需由 Phase 5（实验设计）判断——若 E10 能验证"值变换可以独立解决相当一部分 Mod 需求"，则该类应正式纳入模型；若实验中它能力过窄，则降级为"实现技巧"记入附录。**本 Phase 不擅自修改 Phase 1 的分类**（避免未经验证就改判定基准）。

---

## 7. 待验证清单

| # | 项 | 为何未定 | 何时解决 |
|---|---|---|---|
| 1 | RBM 实际运行时是否真的不抖动 | 纯静态分析，无 Telemetry | **Phase 10**（不得以本 Phase 结论代替） |
| 2 | RBM 版本号 | 未采集 | 下次补 |
| 3 | `OverrideUpdateFormationOrders`（Patch `Agent::UpdateFormationOrders`）是否影响 Formation 级 Order | 属 Agent 级（单位级），本 Phase 未深入 | Phase 9（Probe 时若出现异常回查） |
| 4 | `Frontline.cs` 的两个 Formation Patch（`GetDirectionOfUnit` / `GetOrderPositionOfUnit`）是否构成"非 Order 写入口"的状态影响 | 属定位计算，不写 Order | Phase 10（可观测 Order 是否变化即可排除） |
| 5 | E9 / E10 两组候选实验是否纳入 | 需 Phase 5 评估 | Phase 5 |
| 6 | 第六类冲突是否纳入模型 | 需 E10 结果 | Phase 5/7 |

---

## 8. 本 Phase 明确的「不做」

- 未做运行时验证（Phase 10）
- 未写任何代码
- 未读 CombatBrain 源码（Phase 12）
- 未对 Architecture A/B/C 下结论 —— 本 Phase 只提供案例证据
- 未修改 Phase 1 的冲突分类（§6 仅为建议，待 Phase 5 决定）

---

## 附录 A · 对 Phase 2 文档的两处更正

本次调查顺带发现 Phase 2 文档有 1 处**事实错误**与 1 项**遗留问题解决**，已同步修正 `02-bannerlord-control-pipeline.md`。

### A1 · 更正：`Formation::Tick` 的 SubstituteOrder 不是"每 tick 无条件写入"

Phase 2 §8.2 原表述为「一个不受任何门控、**每 tick 运行**的原生 Movement 写入点」。**这是误读**。

实际结构（`Formation::Tick` 偏移 166–209）：

```il
166: Ldc_i4_0 ; Stloc_1            // i = 0
168: Br_s 18                       // → 跳到 188（先检查）
170: Ldarg_0
171: Ldflda _movementOrder
178: Call MovementOrder::GetSubstituteOrder
183: Call Formation::SetMovementOrder        // ← 循环体
188: Ldflda _movementOrder
195: Call MovementOrder::IsApplicable
200: Brtrue_s 9                    // 若适用 → 退出循环
202: i++
207: Ldc_i4_s 10
209: Blt_s -41                     // 若 i < 10 → 回到 170
```

等价于：

```csharp
for (int i = 0; i < 10 && !MovementOrder.IsApplicable(ref _movementOrder, this); i++)
    SetMovementOrder(MovementOrder.GetSubstituteOrder(ref _movementOrder, this));
```

→ 它是一个**兜底修复循环**：**仅当当前 MovementOrder 不适用时才触发**，且**单帧最多 10 次**。对 Mod 写入的合法 Order（HOLD / ADVANCE / RETREAT 应可通过 `IsApplicable`），**该路径不会覆盖**。

**影响**：Phase 2 §8.1 对 §10.6 PoC 的倾向性判断（"会有残留"）**理由被削弱**——Movement 侧的这个非门控路径实际上条件苛刻。但 **Arrangement 的两个独立 Timer 不受门控这一条仍然成立**，故 PoC 仍需 Phase 10 实测，结论不变。

### A2 · 解决：`MovementOrderEnum` 定义（Phase 2 遗留 #1）

RBM 在 `OverrideMovementOrder.cs:12-27` 复制了一份枚举：

```csharp
Invalid=0, Attach=1, AttackEntity=2, Charge=3, ChargeToTarget=4, Follow=5,
FollowEntity=6, Guard=7, Move=8, Retreat=9, Stop=10, Advance=11, FallBack=12
```

据此，Phase 2 的 `GetSubstituteOrder` 可读为：

```csharp
if (OrderEnum == AttackEntity) return MovementOrderStop;
return MovementOrderCharge;
```

⚠️ **证据等级：`[源码]`（RBM 复制的副本）+ `[推断]`**。该枚举是 **RBM 的副本，不保证等于游戏当前版本的真实定义**。Phase 9 应在 Probe 里直接读取真实枚举值确认；在确认前，该映射不得作为设计依据。
