# 10 · Bannerlord 实测：Harmony Runtime 实验（H1 / H2 / H3）

> 生成时间：2026-09-20 23:40 GMT+8（会话 10）
> 证据等级标注：**LEVEL A = 已实测 / LEVEL B = IL 静态取证（可复核）/ LEVEL C = 预测（待实测）**
> 本文不含任何实测战斗数据 —— 采集需要人工进游戏，见 §7 SOP。**凡标 LEVEL C 的内容均为预测，不是结论。**

---

## 0. 本轮状态（先说清楚做到哪一步）

| 项 | 状态 |
|---|---|
| 静态取证（写入口形态 / 调用链 / TeamAI 装配） | ✅ 完成，LEVEL B，见 §2 与 §10 |
| 采集装置补全（hold / 普查 / 影子采样 / 覆盖率 / 判据自检 / 全量镜像） | ✅ 完成，见 §3 与 §7.8 |
| 编译离线 gate（0 warning / 0 error） | ✅ 通过 |
| Phase 7/8 仿真回归（D0 15/15、E1–E11 51/51、E12 10/10） | ✅ 不变（会话 10 与 13 均未改 Core） |
| H1/H2/H3/REL 脚本 | ✅ 9 份落盘 `deploy/CommandLabProbe/scripts/` |
| 部署到游戏 Modules | ✅ `Modules\CommandLabProbe`（新 DLL MD5 `4ca2d169…`，见 §11.4 的部署陷阱） |
| **首次进游戏实测** | ✅ **已执行**（2026-09-22 10:46）→ **P0 FAIL**，装置故障，本轮数据作废，见 §11 |
| **第二次实测（重跑）** | ❌ **未走通**（2026-09-22 11:57）—— 进战斗时游戏崩溃（CLR 异常 `0xe0434352`），**根因未定位**，见 §12。已补崩溃证据收集 + 启动哨兵，下次启动即可二分 |
| **P0 门禁** | ❌ **FAIL**（`actual=0`）→ 根因已定位并修复，**待重跑验证** |
| **P0b 门禁** | ❌ FAIL（受 P0 连带，非独立结论） |
| **H1/H2/H3 结论** | ❌ **无**。未拿到有效数据前不写任何实测结论（红线） |

**当前定位：Phase 10 的"装置修复后待重跑"阶段。** 下一步只有一件事 —— 重跑 `census-baseline` 直到 P0/P0b 双 PASS（见 §11.7）。P0/P0b 不过则后续 8 组一律不跑。

---

## 1. 为什么要先做静态取证

Phase 10 最大的数据风险不是"跑不出来"，而是**跑出来的数据看起来很干净但是假的**。两种典型假象：

1. Harmony patch 静默不匹配（方法签名变了）→ patch 没打上 → 观察到"原生一次都没写" → 误判为"夺取成功"。
2. 写入口覆盖不全 → 某些路径压根不经过 patch → 观察到"没有冲突写入" → 误判为"覆盖率足够"。

这两条恰好分别是 H2 的 G1 和 E12 ⑨ 的宿主版本。所以本轮先做**两个取证动作**把假象堵死：

- **GetPatchedMethods 取证**：让 Harmony 自己报出真正 patch 上的方法列表，与运行时流量分开记录。
- **影子采样（Shadow Sampling）**：不依赖 patch，直接每 tick 读宿主真值，用"采样到的变化次数 − 写入口记录的生效变化次数"量化绕过写入口的写入量。

---

## 2. 静态取证结果（LEVEL B，IL，v1.3.4）

取证对象：`bin\Win64_Shipping_Client\TaleWorlds.MountAndBlade.dll`，工具 `ilscan dump / ref`。

### 2.1 `Formation.SetControlledByAI` 只有一种形态 —— 更正 HANDOFF 旧登记

`HANDOFF` §5 / `docs/09` §10 曾登记"存在 1/2 参两种调用形态，需确认重载签名"。会话 10 实测：

```
== TYPE: TaleWorlds.MountAndBlade.Formation
  -- METHOD: SetControlledByAI            ← 全文仅此一处
         ...
        158: Ldarg_0
        159: Ldarg_2
        160: Stfld Formation::_enforceNotSplittableByAI
```

`Ldarg_2` 被写入 `_enforceNotSplittableByAI`，证明是 **2 个实参**（不是 1 参 + 可选参数）。
→ `[HarmonyPatch(typeof(Formation), nameof(Formation.SetControlledByAI))]` 精确匹配，**不存在重载歧义**。旧登记作废。

### 2.2 Arrangement 的自维护路径**不经过**写入口（关键）

`Formation.SetArrangementOrder` 的全部调用方（`ilscan ref`）清一色是 `Behavior*::TickOccasionally` 与 `Behavior*::OnBehaviorActivatedAux`，**没有任何 Formation 自维护 Timer 调用它**。

`Formation::Tick` 里两个 Timer 的实际走向（IL offset）：

| Timer | 触发的调用 | 是否经过写入口 |
|---|---|---|
| `_arrangementOrderTickOccasionallyTimer`（0.5s，`ResetArrangementOrderTickTimer` 里构造） | `ArrangementOrder::TickOccasionally(formation)` | ❌ 否 |
| `_arrangementTickOccasionallyTimer` | `IFormationArrangement::OnTickOccasionally()` | ❌ 否（改的是排列几何，不是 Order） |

→ **02 §8.2 的"Arrangement 双 Timer 不受门控"应精修为：不是"走了写入口但门控没生效"，而是"根本不走写入口"。**
→ 推论（LEVEL C，待实测）：把 `ArrangementPath` 从 NonGated 切成 Gated **也拦不住**这条路径。这就是 E12 ⑨「Level 上限由写入口覆盖率决定」在宿主侧的同构命题。

### 2.3 `SetControlledByAI` 两侧的副作用不对称（H3 的关键）

同一份 IL 里两个分支：

| 分支 | 行为 |
|---|---|
| `isAIControlled = true` | 强制 tick 一次 `AI.ActiveBehavior`；若 ActiveBehavior 变了 → `OnBehaviorActivated()`，然后 **`SetMovementOrder(AI.ActiveBehavior.CurrentOrder)`** ← 会写一次 Movement |
| `isAIControlled = false` | `_enforceNotSplittableByAI = 0`，然后 `ActiveBehavior?.OnLostAIControl()` ← **不写 MovementOrder** |

→ Route B（`SetControlledByAI(false)`）本身不产生 Movement 写入；它的抑制效果是**间接**的，来自 `Formation::Tick` 的下一处门控 ↓。

### 2.4 AI.Tick 门控：这是 Route B 真正起作用的地方

`Formation::Tick` IL offset 112–161：

```
if (Team.HasTeamAi) {
    if (IsAIControlled)                 AI.Tick();
    else if (Team.IsPlayerSergeant)     AI.Tick();
    else                                IsAITickedAfterSplit = true;   ← AI 完全不 tick
}
```

→ **IsAIControlled=false 且非 PlayerSergeant 队 ⇒ FormationAI 完全不 tick ⇒ 既不选 Behavior 也不写 Movement/Facing。**
→ Route B 的抑制力来自这里，而不是来自 `SetControlledByAI` 本身。这也意味着：**Route B 会连 Behavior 选择一起停掉**，代价远大于 Route A（写入口拦截只挡写入，不动 AI 决策）。这是架构层的取舍，不是实现细节。

### 2.5 SubstituteOrder 兜底循环**在 patch 覆盖范围内**

`Formation::Tick` IL offset 166–215 的 do-while（上限 10 次）：

```
170: SetMovementOrder(MovementOrder::GetSubstituteOrder(_movementOrder, this))   ← 会进我们的 patch
```

该循环**不在** IsAIControlled 门控内（无条件进入），符合 02 §8.2 更正后的说法。
→ 好消息：这条 non-gated 路径**能被 Movement patch 拦住**。Movement 域的覆盖率高于 Arrangement 域。

---

## 3. 采集装置本轮变更

### 3.1 修一处会导致假阴性结论的缺陷（必须记录）

`HoldMeter` 在 Phase 9 里 `Acquire` 时 new 出来，但**全工程没有任何地方调用 `Open()`**。
后果：`Intent` 恒为 `Unset`，`_open` 恒为 false，`Sample()` 直接 return → `intent_hold_share` **恒为 0%**。
若不修，H2 的 G2（hold ≥ 95%）**必然 FAIL**，会被误读成"夺取失败"。这是纯实现缺陷，与架构无关。

修复：`Command()` 成功返回后按 `(now, value)` 开窗口；支持多窗口累计；窗口关闭后进入 `_holdHistory`，Release 之后仍能在 `[hold]` 段读到。

### 3.2 新增能力

| 能力 | 实现 | 回答什么问题 |
|---|---|---|
| **写入口普查 census** | 每个 hook 计数（含放行），`[census]` 段 | 6 个写入口各自有多少流量 |
| **patch 生效性取证** | `Harmony.GetPatchedMethods()`，`[patches]` 段 | 区分"没流量"与"patch 没打上"（P0 门禁） |
| **影子采样 shadow** | 每 tick 读宿主真值，`SHADOW\|` 行 | 检出绕过写入口的写入，不依赖 patch |
| **覆盖率 coverage** | `sampled_changes − port_changes`，`[coverage]` 段 | E12 ⑨ 的实测版：量化覆盖率缺口 |
| **观测型 patch** | `SetControlledByAI` / `SetFormOrder` / `SetFiringOrder` / `SetRidingOrder`，只计数不拦截 | H3 Route B 取证 + 写入口级联取证 |
| **首次覆盖延迟** | `NativeOverrideLatency` | H1 的"多久被覆盖" |
| **判据自检 verdict** | `[verdict]` 段 PASS/FAIL | 数据一出来就能判，不靠人读表 |

红线遵守：观测型 patch 一律 `return true`（永不拦截）；所有判定仍在 `Core.WritePort.Evaluate` 内；没有"每 tick 重复 SetOrder"。

### 3.3 写入口级联（顺带取证）

`SetArrangementOrder` IL offset 156–192：当 `FormOrder.OrderEnum != 3` 且新 Arrangement `!= 1` 时，会连带
`SetFormOrder(FormOrderCustom(CalculateDesiredWidth()), 0)`。
→ **写入口之间存在级联**。观测型 patch 记录 `SetFormOrder` 就是为了在实测里验证这一条。

---

## 4. 实验协议

全部为六段式：RQ / Hypothesis / Test / Metric / Success / Failure。装置 = 同一份 `script.txt`，只有一行 `policy` 或 `acquire` 之差。

### H1 · External Write（不夺取）

- **RQ**：不取得控制权时，外部写入能存活多久？被谁覆盖？
- **Hypothesis (LEVEL C)**：必被覆盖，且延迟 ≤ 一个 Behavior 决策周期（0.5s 量级）。
- **Test**：`t=3.0 cmd HOLD` / `t=8.0 cmd HOLD`，不 acquire，30s 采样。
- **Metric**：`native_overrides`、`NativeOverrideLatency`、归因行 `HOST|...|behavior=|weight=`。
- **Success**：`native_overrides > 0`。
- **Failure**：`native_overrides == 0` → 说明 N0 期间原生根本没流转 Former **或 patch 没打上**，须先查 P0/P0b 再谈结论。

### H2 · Native Suppression（Level 5 + Lease）

- **RQ**：夺取后能否把原生冲突写挡在写入口外，且意图真正落地？
- **Hypothesis (LEVEL C)**：G1 与 G2 同时成立（E12 ⑧ 的宿主版：仅 G1 成立不算取得控制权）。
- **Test**：`acquire N0 Movement Full 30` → `cmd HOLD` → 25s → `release`。
- **Metric**：G1 `unauthorized_writes == 0`、G2 `intent_hold_share ≥ 95%`、G3 `grade != Oscillating`。
- **Failure**：G1 PASS 而 G2 FAIL → 与 E12 ⑤「伪稳定」同形，须在文档里明确区分。

### H2b · 写入口覆盖率对照（阻断级 PoC）

- 唯一差别：`enforceNonGated=true arrangementPath=Gated`。
- **Hypothesis (LEVEL C，由 §2.2 推出)**：即使切成 Gated，`[coverage]` 段 Arrangement 域 `unobserved > 0` —— 因为那条路径不走写入口。
- **反向情况的处理**：若实测 `unobserved == 0`，则说明自维护 tick 不改 `OrderEnum`，或版本行为已变。两种结果都记，都不当作"实现有误"。

### H3 · Route B（`SetControlledByAI(false)`）

- 唯一差别：`controlledByAiRoute=true`。
- **Hypothesis (LEVEL C，由 §2.4 推出)**：原生 Movement 写入量显著下降（AI 不再 tick），但 Arrangement 自维护仍可能继续。
- **必须同表记录的代价**：Route B 同时停掉了 Behavior 选择（AI 完全不 tick），这不是"更彻底的拦截"，而是"换了一种控制模型"。

### REL · Acquire/Release 恢复语义（4 组 + 1 个设计出来的反例）

| 脚本 | restore | restoreBeforeRelease | 预期（LEVEL C） |
|---|---|---|---|
| `rel-none` | None | true | 观测"不恢复会怎样"基线 |
| `rel-reapply` | ReapplyLastNativeOrder | true | failures=0 |
| `rel-reapply-resetweights` | +ResetBehaviorWeights | true | failures=0；验证是否必须重置权重 |
| `rel-after-release` | ReapplyLastNativeOrder | **false** | failures>0（已是设计出来的反例：交还后写入必被拒） |

⚠ Phase 7/8 已登记"释放后失败数恒为 0 属平凡成立，不作判据"。宿主侧必须用 `rel-after-release` 这类反例证明装置**有能力检出失败**，否则 0 没有意义。

---

## 5. 判据表（`[verdict]` 段，全部由 Telemetry 算出）

| ID | 判据 | 阈值 | 说明 |
|---|---|---|---|
| **P0** | `harmony patched methods > 0` | >0 | 前置门禁。FAIL ⇒ 本轮全部结论作废 |
| **P0b** | 写入口流量 > 0 | >0 | FAIL ⇒ "没有原生写入"是假象 |
| **P0c** | EntityId 唯一性 | 唯一数 == 绑定总数 | FAIL ⇒ 同一 EntityId 挂了多个 Formation，除 `[patches]`/`[census]` 外全部指标失真 |
| H1-a | `native_overrides` | >0 | 不夺取必被覆盖 |
| H1-c | 首次覆盖延迟 | 记录值 | H1 核心量 |
| H1-d | `lab_overrides` | 记录值 | Lab 反覆盖原生的次数 |
| **G1** | `unauthorized_writes` | ==0 | 必要非充分（E12 ⑧） |
| **G2** | `intent_hold_share` | ≥95% | 与 G1 必须同表读 |
| G3 | `grade` | !=Oscillating | |
| H3-a | `SetControlledByAI` 被观测到 | >0 | |
| REL | `release_recovery_failures` | ==0 | `rel-after-release` 预期为 FAIL（反例） |
| X5 | `unobserved_writes` | ==0 | 覆盖率缺口总量 |

---

## 6. 产物格式

一次 dump 落两个文件到 `%USERPROFILE%\Documents\CommandLabProbe\`：

- `telemetry-<时间戳>-<tag>.txt` —— Core `WriteAttempt` 行（与仿真逐字段一致）+ `HOST|` + `CENSUS|` + `SHADOW|` + `NOTE|`
- `metrics-<时间戳>-<tag>.txt` —— `[config] [metrics] [verdict] [hold] [census] [coverage] [patches] [projection]`

指标是**从任务开始累计**，不是区间量。每 10 秒还有一个 `tick` 标签的自动 dump 便于中途观察。

---

## 7. 数据采集 SOP

### 7.0 环境前提：load order 必须精简（2026-09-22 新增，**硬要求**）

本实验的目标是 **Vanilla 原生写入口流量基线**。任何修改 Formation AI 的 mod 都会污染它，
且污染方式不是"多一点噪声"，而是**改变被测量的定义**。

**必须关闭**：

| mod | 为什么必须关 |
|---|---|
| **CombatBrain** | DLL 字符串级取证（2026-09-22）：该 DLL 引用了我们观测的**全部 4 个写入口**（`SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` / `SetControlledByAI`）且引用 `Harmony`。若它同样 patch 了这些方法，两个 `Prefix` 叠加在同一方法上 ⇒ 取决于 Harmony 的 patch 执行顺序，**写入可能被它提前拦截、根本到不了我们的 `Gate()`** ⇒ `[census]` 计数失真。此外它是本项目 Phase 12 的**集成对象**，同进程运行等于"实验者与被测者同处一身" |
| **CAI / RBM / FrontlineMod** | 同类 Formation AI 改造，直接改变原生写入的频率、时机与分布 |
| **ImmersiveBattlefields / ChaosBattle / PolicyExpansion / CommanderWindow / ClanControl** | 涉及战斗行为链与命令下发路径 |

**推荐做法：用最小 load order 跑。**

```
Native + SandBoxCore + Sandbox + StoryMode + CustomBattle + Bannerlord.Harmony + CommandLabProbe
```

`CommandLabProbe` 的 `SubModule.xml` 声明的 `DependedModules` 正好就是这 7 个，无需额外依赖。

**降级方案**（若不便精简整个 load order）：至少关掉上表的 **CombatBrain**，
并在结论里登记"实验环境含 N 个战斗类 mod，'是否为纯 Vanilla 基线'存疑"。

> 📌 **已登记的环境事实**：2026-09-22 10:46 的首跑（P0 FAIL）中 **CombatBrain 处于开启状态**。
> 该轮数据已因 **P0 FAIL** 独立作废（根因是 Harmony 形参名笔误，见 §11.2，与 CombatBrain 无关），
> 因此这一环境事实不改变作废结论；但它记录了"该轮环境未受控"，**后续所有有效采集必须关掉它**。

> ⚠️ 这里对 CombatBrain 的分析属**环境风险取证**（读运行环境的 DLL 符号），
> **不构成对 `CombatBrain.*` 的代码引用**——本项目源码与文档中的类型/命名空间引用红线仍然成立。

### 7.1 战前设置

**A. 代码有改动时，必须先重新构建并部署**（见 §11.4 的实测教训——构建了但没拷进 `Modules\`，游戏跑的还是旧 DLL）：

```
dotnet build "E:\MBmod\TacticalCommandLab\src\CommandLab.BannerlordProbe\CommandLab.BannerlordProbe.csproj"
copy /Y "E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\bin\Win64_Shipping_Client\CommandLab.BannerlordProbe.dll" ^
        "<Bannerlord>\Modules\CommandLabProbe\bin\Win64_Shipping_Client\"
```
构建目标 `CopyProbeToDeploy` **只把产物拷到 `deploy\`**，不会拷进游戏 `Modules\` —— 这一步必须手做。

B. 游戏侧设置（一次性）：

| 项 | 设置 | 原因 |
|---|---|---|
| **启动器** | 勾选 **CommandLab Probe**，置于 `Bannerlord.Harmony` 之后 | `SubModule.xml` 已声明 `DependedModules: Bannerlord.Harmony`，顺序反了会 patch 失败 |
| **模式** | **Custom Battle / 自定义战斗**（不要用战役、不要用攻城） | 攻城会引入 `TeamAISiegeAttacker/Defender` 与 Siege 专属 Behavior，污染 `behavior=` 归因；战役走 `BaseBattleMissionController`，装配路径不同 |
| **地图** | **平坦野战图**，无攻城器械、无城墙 | 同上 |
| **玩家角色** | **当小兵 / 士兵（Sergeant）或普通参战者，不要当将军** | 玩家当将军会引入玩家侧手动指挥链路，且 `isPlayerSergeant=false` 会改变 `Formation::Tick` 的门控分支 |
| **是否暂停** | **不要暂停游戏**（TAB 菜单可以，但别按暂停键） | 时钟按 `OnMissionTick` 累加，暂停即停表 |
| **是否手动下令** | **全程不要下任何命令** | 手动下令 = 引入第二个写入源，`native_overrides` 归因就不干净了 |

### 7.2 兵力与兵种：**没有硬性要求**（重要）

这是本 SOP 最容易被误解的一条，明确写清：

1. **兵力规模不影响任何判据内核**。所有指标都是**按 tick 累加的比例量或计数**，`intent_hold_share` 是时长占比、`unauthorized_writes` 是次数、`grade` 是稳定性分级 —— 都不含绝对兵力项。**兵力的唯一作用是让战斗别太快结束**（兵力太少，一方 10 秒内被打崩，Mission 结束，采样窗口被截断）。
2. **兵种完全不做要求，也不影响结论**。本框架只观测 **Formation 级** 的 `SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` 三个写入口，**不区分兵种**。但**推荐用步兵**：纯骑兵/纯弓兵会走 SoA(UserFormations) 之外的单位行为抢占，容易把 `behavior=` 归因搅浑。
3. **具体建议（按重要性排序）**：
   - **双方各 3–4 个步兵编队**（这样 `N0`/`N1`/`N2`/`P0` 都真实存在，`FindBinding` 才找得到）。
   - **每队 ≥ 20 人**（F1 推荐的默认建议值）。理由：`FormationAI` 周期 0.5s，规模太小 AI 还没决策完战斗就结束了。
   - **双方兵力接近**（例如各 100–150 人，1:1 或 1.2:1）。理由：让战斗持续 ≥ 35s，完整覆盖脚本时间线。差距悬殊会导致一方rush崩溃。
   - **总兵力别超过 800**（含玩家与友军），避免低配机掉帧影响 tick 频率。
4. **不要用 1:1 单一编队的小战斗**（双方各 1 队 20 人）。这类战斗中 `N1` 等 index 不存在，而且 AI 往往直接 `<BehaviorCharge>`，`Arrangement` 域几乎无流量 —— 不影响判据，但会让 census 的"6 个写入口哪些有流量"这个问题得不到完整答案。

### 7.3 每场战斗的完整操作流程

**每场约 35–40 秒**，共 9 次启动（`census-baseline` + `h1-external-write` + `h2-suppression` + `h2-coverage` + `h3-route-b` + 4 组 `rel-*`）。
**⚠ 一脚本一重启游戏，不是连续跑八把 —— 理由见 §7.6。**

```text
【准备】① 代码有改动时先按 §7.1A 重新构建并部署；按 §7.1B 设好启动器与游戏设置
        ② 复制脚本（**必须在启动游戏之前**，游戏中途换 script.txt 不生效）：
           copy "E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\scripts\<脚本名>.txt" ^
                "%USERPROFILE%\Documents\CommandLabProbe\script.txt"

【入场】③ 启动游戏 → 自定义战斗 → 按 §7.2 配好双方兵力 → 开始战斗
        ④ 载入完成后**立刻不要动**。脚本从第一次 OnMissionTick 起算，t=0 即开始

【挂机】⑤ 全程不按暂停、不下命令，看着打完。脚本到 t=30~38 会自动 dump
        ⑥ 每 10 秒还会自动 dump 一次 tick 快照，用于中途观察（不必处理）

【收尾】⑦ 退出战斗 → **退出游戏**（必须先退出，见 §7.6）
        ⑧ 取 %USERPROFILE%\Documents\CommandLabProbe\metrics-<时间戳>-<tag>.txt
           （这是唯一要看的文件；telemetry-* 仅在归因时才看）
        ⑨ 把 metrics 文件重命名带上脚本名（如 metrics-xxx-h1.txt），拷到
           E:\MBmod\TacticalCommandLab\tests\artifacts\host\
        ⑩ 回到 ② 换下一个脚本（含重新启动游戏），重复
```

### 7.4 必跑顺序与门禁

| 序 | 脚本 | 看什么 | 门禁 |
|---|---|---|---|
| 1 | `census-baseline` | P0（patch 打上了）、P0b（有流量）、X5（无覆盖率缺口）、P0c（EntityId 唯一） | **P0 / P0b / X5 / P0c 任一 FAIL ⇒ 停，先修装置，后面全部作废** |
| 2 | `h1-external-write` | H1-a `native_overrides` > 0、H1-c 首次覆盖延迟 | H1-a FAIL ⇒ 说明 N0 没被原生流转，先查 P0b |
| 3 | `h2-suppression` | **G1 ∧ G2 必须同表读** | 仅 G1 PASS 而 G2 FAIL = E12 ⑤「伪稳定」，不是成功 |
| 4 | `h2-coverage` | `[coverage]` 段 Arrangement 域 `unobserved` | 两种结果都记，不当作"实现有误" |
| 5 | `h3-route-b` | H3-a `SetControlledByAI` 被观测到、原生 Movement 写入量是否下降 | 必须记录"Behavior 选择被一起停掉"这一代价 |
| 6–9 | `rel-none` / `rel-reapply` / `rel-reapply-resetweights` / `rel-after-release` | `release_recovery_failures` | 最后一组**预期 FAIL**（设计出来的反例），其余预期 0 |

> ⚠️ **`rel-after-release` 必须 FAIL**。如果它 PASS，"其他组 failures=0"就是平凡成立、毫无意义。
> 这一条是验证"装置有能力检出失败"的唯一手段。

### 7.5 判据读法

**只看 `metrics-*.txt` 的 `[verdict]` 段**，PASS/FAIL 已由 Telemetry 算出，不用读画面、不用自己算。

要归因（"被谁覆盖的"）时才看 `telemetry-*.txt`：
- `HOST|` 行 —— `hook=` 哪个写入口、`behavior=` 哪个 Behavior、`weight=` 权重
- `CENSUS|` 行 —— 观测型 patch 的原始值
- `SHADOW|` 行 —— 绕过写入口的写入（值变化的原始轨迹）

### 7.6 换脚本的硬约束：**一脚本一重启游戏**（不是连续跑八把）

**这一条必须遵守，否则八场数据全部作废。**

`ScriptRunner` 只在 `ProbeSubModule.OnSubModuleLoad`（游戏启动那一刻）构造一次，
`script.txt` 之后改成什么都不生效。**整个进程内只有一份脚本**。

因此正确循环是：

```
改 script.txt → 启动游戏 → 进自定义战斗 → 挂机 35s → 退出游戏 → 改 script.txt → 再启动 …
```

**不是**"启动一次游戏、连续进出八场战斗"——那样八场跑的都是同一个脚本。

> **为什么不能把八个脚本拼成一个大脚本**（省启动次数）：
> 1. **时间轴不重置**。`t` 从第一次 `OnMissionTick` 累加，一场内线性推进；第二场战斗进来时
>    时钟继续往前走，`t=33.0 dump` 早在第一场就触发过了，第二场不会再触发。
> 2. **`ProbeRuntime` 是单例**（静态 `Current`，`_bindings` / `_leases` / `_census` /
>    `_sampledChanges` 全部跨战斗存活）。第一场的 lease、hold 累计、写入口计数会被带进第二场，
>    `unobserved`、`unauthorized_writes`、`intent_hold_share` 全部不可信。
>
> 拼接方案只有在"每段前重启进程"时才有意义 —— 那就等于退回一脚本一启动。

**同理**，§7.7 第 3 条的"同脚本重放比对"（`09` §11 偏差 7）**也必须重启游戏**，
不能在同一进程里跑两遍。

合计启动次数：8 个脚本 = 9 次（含 `rel-*` 四组）＋建议挑 `census-baseline` 与 `h2-suppression`
各重放一次 = 11 次。

### 7.7 收尾

1. 全部 metrics 拷到 `E:\MBmod\TacticalCommandLab\tests\artifacts\host\`。
2. 用实测数据回填 §4 的 LEVEL C 预测（升级为 LEVEL A 或推翻）。
3. 同一脚本**同战斗跑两次**做空跑重放比对（`09` §11 偏差 7）——时间线与计数须一致，否则数据不可比。**必须重启游戏，见 §7.6。**

### 7.8 观测对象从哪来：全量镜像（含一次错误修复的更正）

**问题**：`[census]` 的计数发生在 `Harmony Prefix → Gate() → Bind(formation)` 之后，
`SampleShadow` 也只遍历 `_bindings`。若没有任何 formation 被绑定，两段全空，
**会被误读成"原生没有写入"这一架构事实**。

**会话 12 的错误修复（已撤销）**：当时在脚本里加了三行
`t=1.0/1.05/1.10 cmd P0|N0|N1 Movement HOLD`，理由是"未 Acquire 时写入口放行、不夺取控制权"。

**该修复无效**，因为漏看了解析链路：

```
ScriptRunner.TryCommand → Resolve(entityId) → FindBinding
  找不到 ⇒ 静默 return   ← 断在这里
Command() 根本没被调用 ⇒ Bind() 根本没被调用
而 Bind() 此前只在 patch 触发时发生 ⇒ 鸡生蛋死锁
```

脚本动作**本身也要求实体已绑定**，所以它不可能用来建立绑定。

**正确修复（[全量镜像](ProbeRuntime.BindAll)）**：`ProbeRuntime.Tick` 每 tick 遍历

```
Mission.Current.Teams → team.FormationsIncludingEmpty → Bind(formation)
```

放在 `SampleShadow` 与 `_script.Tick` **之前**（前者遍历 `_bindings`，后者按名字解析实体）。
`Bind` 只做 Core 侧注册（`_port.RegisterEntity` + 字典写入），**不触碰宿主任何状态**；
每 tick 调用，内部 `TryGetValue` 短路，成本可忽略。不过滤空编队：漏绑会重新引入
"没有观测对象"，而空编队的真值读出来是 `Unset`、会被跳过，无污染。

配套：`ScriptRunner.Resolve` 解析失败时写 `NOTE|SCRIPT_ENTITY_UNRESOLVED <名>`
——**消除静默失败**。此前"实体名写错 / 编队不存在 / 绑定未建立"与"指令执行了但无效果"
在产物上完全同形，是这次排查困难的放大器。

---

## 8. 已知限制（不要绕开，写进结论）

1. **影子采样的时间分辨率 = Mission tick 频率**。同一帧内两次变化只算一次 ⇒ `unobserved` 是**下限估计**。
2. **战斗本身不可复现**：同一份脚本在不同战斗里真值不同 ⇒ 跨战斗不能做数值比对，只能做**同一场战斗内的对照**（这也是 `policy` 运行时切换存在的原因）。
3. **N0 的选择依赖 TeamAI**：没有 TeamAI 的队伍 `AI.Tick` 根本不跑，H1 会假阴性。跑之前用 census 确认有流量。自定义战斗已取证为"双方都有 TeamAI"（§10），**但 N0 仍须以 `[census]` 实测流量为准**——取证只证明 AI 会 tick，不证明它一定会写 MovementOrder。
4. **观测型 patch 不改宿主行为**，但增加了 4 个方法的 patch 面积 ⇒ 与"零 patch 基线"相比有极小性能扰动，不接受作为性能结论的依据。

---

## 9. 下一步

0. **重跑第 1 组 `census-baseline`，直到 `P0` / `P0b` 双 PASS**（见 §11.7）。装置已于 2026-09-22 修复并重新部署。**P0/P0b 不过则下面 1–4 一律不做。**
1. 执行 §7 SOP 采集其余 8 组数据（人工，约 30 分钟，**一脚本一重启游戏**）。
2. 写一个离线解析器把 `metrics-*.txt` 的 `[metrics]` 段与 Phase 7/8 仿真侧同表并排（两边字段一致，可直接对齐）。
3. 用实测数据回填 §4 的 LEVEL C 预测，升级为 LEVEL A 或推翻。
4. 只有拿到 H2 的 G1∧G2 与 H2b 的 coverage 结论后，才进 Phase 11（架构定型）。

---

## 10. 附录：自定义战斗的 TeamAI 装配取证（LEVEL B，IL，v1.3.4）

**缘起**：`§7` 早期版本写过"自定义战斗双方都带 TeamAI"（现见 §7.1B / §8 第 3 条），
用户质疑「双方怎么可能带 TeamAI」。该表述此前无取证支撑，属**未验证假设**。
本轮做 IL 溯源闭环，结论：**该表述成立，但机理此前完全说错**。

### 10.1 取证链（四步，全部可复现）

| 步 | 目标 | 命令 | 关键 IL |
|---|---|---|---|
| ① | `BannerlordMissions::OpenCustomBattleMission` 如何装配 MissionBehavior | `ilscan dump <TWMAB.dll> BannerlordMissions OpenCustomBattleMission` | offset 241–255：把闭包 `<>c__DisplayClass9_0` 的委托 `Ldftn` 后传给 `MissionState::OpenNew`，**方法体内没有 `MissionCombatantsLogic` 的 `Newobj`** |
| ② | 闭包方法里到底 new 了什么 | `ilscan dump <TWMAB.dll> c__DisplayClass9_0 OpenCustomBattleMission` | offset 130–188：`Ldnull` → `playerParty`/`enemyParty`（按 `isPlayerAttacker` 二选一）→ **`Ldc_i4_1`** → `isPlayerSergeant` → `Newobj MissionCombatantsLogic::.ctor` |
| ③ | 该 `Ldc_i4_1` 是第几个参数 | `ilscan dump <TWMAB.dll> MissionCombatantsLogic .ctor` | 签名 7 参（this, BattleCombatants, PlayerBattleCombatant, DefenderLeader, AttackerLeader, `TeamAIType`, `IsPlayerSergeant`）；offset 55–58：`Ldarg_s 5` → `Stfld TeamAIType`。按 IL 求值栈顺序 `Ldc_i4_1` 正好落在第 6 个实参位 ⇒ **`TeamAIType = 1`** |
| ④ | `TeamAIType = 1` 触发什么 | `ilscan dump <TWMAB.dll> MissionCombatantsLogic EarlyStart` | offset 16–26：`Stloc_0` → `Ldc_i4_1` → `Sub` → `Switch count=3`；分支一（原值 1）走 offset 48–130：遍历 `Mission.Current.Teams`，对**每一支队伍** `new TeamAIGeneral(mission, team, 10f, 1f)` + `AddTeamAI(0)` |

### 10.2 结论（三条）

1. **自定义战斗的 `MissionTeamAITypeEnum` = `SinglePlayer`（值 1）**，硬编码在闭包里，不随 `isPlayerAttacker` / `isPlayerSergeant` / 部队规模变化。
2. **`EarlyStart` 的 `SinglePlayer` 分支是"遍历全部 Team 无条件 AddTeamAI"** —— 不存在"只给玩家一方"或"只给有指挥官的队伍"的分支。因此**攻守双方对 AI 而言完全对称**：两边的 Formation 都会走 `Formation::Tick` → `if (IsAIControlled) AI.Tick()`。
3. **此前"双方都带 TeamAI"的说法结果对、机理错**。它不是因为 `BaseBattleMissionController::CreateTeams`（那是战役任务的基类，自定义战斗不走它，见 §10.3），而是因为这四步装配链。

### 10.3 两个易混淆点（此前踩过）

- **`BaseBattleMissionController::CreateTeams`**（IL offset 147–239）确实也 `GetTeamAI(5f,1f)` + `AddTeamAI` 给了攻守双方 —— 但它是**战役 Mission 的基类**，自定义战斗走 `BannerlordMissions::OpenCustomBattleMission` → `MissionState::OpenNew`，**不经过它**。把这条当依据是碰巧对上了结论、逻辑是错的。
- **`MissionTeamAITypeEnum` 枚举的成员名与值**：本轮未能从 IL 直接读出成员名字符串（枚举字段是编译期常量，`ilscan dump` 只输出 IL 不输出常量表）。值 `1` 对应的 `SinglePlayer` 名字**来自语义推断**（分支一装配 `TeamAIGeneral`，这是野战/单机战斗的通用 AI）。若后续需要该名字作为硬依据，应改用反射 dump（`Enum.GetNames`）而非 IL。

### 10.4 对 Phase 10 的影响（不含任何实测结论）

- §7 第 3 条的"双方都带 TeamAI"**保留**，但已升级为 LEVEL B 取证表述。
- **不构成"census 一定有流量"的证明**：TeamAI 在场只说明 `FormationAI::Tick` 会被调用，不说明它一定会写 MovementOrder（`FormationAI` 有迟滞与 `PreserveExpireTime`，且周期 0.5s）。**N0 仍必须由 `[census]` 实测流量裁定**，P0/P0b 门禁不放宽。
- 「实验场景可用自定义战斗」这一判断**成立**，无需替换场景。

---

## 11. 首次实测记录：P0 FAIL 与根因（2026-09-22）

项目**第一次真实进游戏运行**。结果：**P0 门禁 FAIL，本轮数据全部作废** —— 但这正是门禁存在的意义。

### 11.1 实测产物

用户跑完 `census-baseline`（战斗时长 164s，远超预期的 35s 窗口；脚本自身的 `t=30.0 dump` 按时触发
于 10:47:00 —— 说明**失败发生在更早的 SubModule 加载阶段**，时间线本身是正常的）。

> ⚠️ **该轮环境未受控**：CombatBrain 处于开启状态。它**不是**本次失败的原因（根因见 §11.2，
> 是编译期就成立的形参名笔误），但这一事实独立地会让该轮数据即使 P0 PASS 也不可用 ——
> 见 §7.0「环境前提」。

```text
[verdict]
P0     harmony patched methods > 0    actual=0    FAIL
P0b    write-port traffic > 0         actual=0    FAIL
[census]    （空）
[coverage]  （空）
[patches]   （空）

telemetry-…-mission-start.txt  全部内容只有一行：
NOTE|t=0.0000|HARMONY_PATCH_FAILED
```

产物已归档至 `tests/artifacts/host/invalid-20260922-1046-p0fail/`（38 个文件 + `README-why-invalid.md`）。

### 11.2 根因：Harmony Prefix 形参名与宿主元数据不匹配（确定性）

Harmony 的 `Prefix` 除 `__instance` / `__0` / `___field` 等特殊名外，**一律按参数名匹配原方法形参**；
名字对不上会抛异常，并使 `PatchAll()` **整体中止**。

用新增的 `sigscan`（Mono.Cecil 读元数据）读出 v1.3.4 的真实签名，与 `Prefix` 逐字比对：

| patch | 我们的 Prefix 声明 | 宿主真实形参 | 结果 |
|---|---|---|---|
| `SetMovementOrder` | `MovementOrder order` | `MovementOrder **input**` | ✗ **笔误** |
| `SetControlledByAI` | `bool **isAIControlled**` | `bool **isControlledByAI**` | ✗ **笔误** |
| `SetFacingOrder` | `FacingOrder order` | `FacingOrder order` | ✓ |
| `SetArrangementOrder` | `ArrangementOrder order` | `ArrangementOrder order` | ✓ |
| `SetFormOrder` | `FormOrder order`（部分声明） | `(FormOrder order, Boolean updateDesiredFileCount)` | ✓ |
| `SetRidingOrder` | `RidingOrder order` | `RidingOrder order` | ✓ |
| `SetFiringOrder` | `FiringOrder order` | `FiringOrder order` | ✓ |

**两处都是我们的错误，不是版本问题** —— 写 patch 时凭印象填了参数名。
`PatchAll` 遇到任一失败即中止，因此 7 个 patch 里实际生效 0 个。

> 复核工具：`E:\MBmod\CAI\.tools\sigscan`（本会话新建）
> 用法：`sigscan <assembly> <typeName> [methodPrefix]` → 打印方法签名（**含参数名**）。
> 与 `ilscan` 的分工：`ilscan` 出 IL 指令流，`sigscan` 出元数据签名。核对 Harmony 参数名必须用后者。

### 11.3 一并修掉的三个装置缺陷

| # | 缺陷 | 后果 | 修复 |
|---|---|---|---|
| 1 | `ProbeSubModule` 的 `catch` **只写常量 `HARMONY_PATCH_FAILED`**，吞掉异常 | 排查变成盲猜（本次靠 sigscan 反推才定位） | `Patches.Failure` 暴露异常链；`NOTE` 写 `HARMONY_PATCHED count=N` + `HARMONY_PATCH_CAUSE <异常链>`；`[verdict]` 的 P0 行下方直接打印 cause |
| 2 | `Applied` 的填充在 `PatchAll()` **之后** | `PatchAll` 抛异常时 `Applied.Clear()` 不执行、列表保持空 —— 而**部分 patch 可能已生效**（Harmony 中止前的成功项不回滚）⇒ `[patches]` 空**不等于**"一个都没打上"，会误导排查方向 | 改为 try/catch 包裹 `PatchAll`，**无论成败都无条件刷新** `GetPatchedMethods()`；`[patches]` 段新增 `failure =` 与 `count =` 两行 |
| 3 | 无**全量镜像**，绑定只在 patch 触发时创建 | `[census]`/`[coverage]` 全空 ⇒ 伪装成"原生没有写入" | `ProbeRuntime.BindAll()` 每 tick 主动绑定全部 formation（详见 §7.8）；配套 `ScriptRunner.Resolve` 失败写 `NOTE`，消除静默失败 |

### 11.4 构建/部署陷阱（实际踩到）

`dotnet build` 成功后，新 DLL 落在 `deploy\CommandLabProbe\bin\Win64_Shipping_Client\`（10:51:39），
但游戏加载的 `Modules\CommandLabProbe\bin\Win64_Shipping_Client\` **仍是两天前的旧版**（09-20 23:47）。

`CopyProbeToDeploy` 这个 MSBuild target **只拷到 `deploy\`，不拷进游戏目录** —— 必须手工 copy。
已写入 §7.1A。

> 这正是调试协议第 ① 步（"确认正在运行正确的新构建产物"）的价值：
> 若跳过这步，下一次实测仍会 P0 FAIL，而我们会去怀疑已经修好的参数名。

### 11.5 门禁价值评估

**P0 门禁按设计工作。** 它把一次会污染后续全部 8 组数据的装置故障拦在了第一组，
且 `P0b=0` 没有被误读为"宿主没有原生写入"这一架构结论 —— 因为 P0 的 FAIL 明确指示了方向。

**反面教训**：若当初只设计了 `P0b` 而没有 `P0`，"零流量"会看起来像一条硬结论
（"原生根本不写 MovementOrder"），并可能直接写进 Phase 11 的架构定型依据。
这是"装置故障伪装成架构事实"的一个实例，验证了 HANDOFF §2 关于 P0/P0b 必须成对存在的判断。

### 11.6 本轮装置变更清单

| 文件 | 变更 |
|---|---|
| `OrderWritePatches.cs` | 修 2 处 Prefix 形参名；`Applied` 填充时机改为无论成败都刷新；新增 `Failure` + `Describe(Exception)`（拉平异常链） |
| `ProbeSubModule.cs` | `catch` 保留兜底；新增 `HARMONY_PATCHED count=` / `HARMONY_PATCH_CAUSE` 两条 NOTE |
| `ProbeRuntime.cs` | 新增 `BindAll()`（全量镜像）并在 `Tick` 中于 `SampleShadow` 之前调用；`[patches]` 段输出 `failure` / `count`；P0 行下方条件输出 failure cause |
| `ScriptRunner.cs` | `Resolve` 解析失败写 `NOTE\|SCRIPT_ENTITY_UNRESOLVED <名>` |
| `census-baseline.txt` | 撤销 2026-09-21 的三行 `cmd` 触碰（该修法无效，见 §7.8），回归零干预纯观测 |

**回归**：Core / Simulation / Telemetry / tests **零改动** ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变。

### 11.7 下一步

部署已更新，`script.txt` 已播种修正版 `census-baseline`。
**重跑第 1 组**：`census-baseline` → 看 `[verdict]` 的 P0 是否 PASS、`[patches]` 的 `count` 是否为 7、
`[census]` 是否有 6 个写入口计数、`[coverage]` 是否有行。
**P0/P0b 双 PASS 后**才继续 H1/H2/H2b/H3/REL。

> 该"下一步"执行后**未走通** —— 见 §12。装置在第二次尝试时改为 `4d2e2d4b…`。

---

## 12. 第二次实测：进战斗崩溃 + 诊断基建（2026-09-22 11:57）

修完形参名后的第二次实测**没有走通**：**进入战斗时游戏崩溃**。本节记录已排除项、无法定位的原因，
以及为此新增的诊断基建。**尚未定位根因。**

### 12.1 现象与已取得的证据

| 项 | 值 |
|---|---|
| 崩溃时间 | 2026-09-22 **11:57:30**（异常码 `0xe0434352` = **CLR 未处理异常**，出错模块 `KERNELBASE.dll`） |
| 首次启动 | 11:53:32（MCM 加载完成） |
| `ButterLib` GameScope | 11:55:35（≈ 进入场景/战斗的时刻） |
| 崩溃前环境 | 用户已关闭 CombatBrain；`script.txt` = 修正版 `census-baseline` |
| 装置版本 | `4ca2d169…`（形参名已修，`Gate()` **无**异常兜底） |

**关键观察：`Documents\CommandLabProbe\` 里没有任何新产物** —— 连 `mission-start` dump 都没有。

`ProbeMissionBehavior.OnMissionTick` 的**第一件事**就是 `Dump("mission-start")`，
没有这个文件 ⇒ **`OnMissionTick` 从未执行到第一行** ⇒ 崩溃发生在 **Mission 初始化序列中**，
早于任何 tick。

### 12.2 为什么首崩无法定位（三个证据通道全部失效）

| 通道 | 结果 | 原因 |
|---|---|---|
| Windows 事件日志 | 只有 `Application Error`（异常码），**无堆栈** | 本机没有记录 `.NET Runtime` provider 的 1026 事件；`Documents\...\crashes\` 目录不存在 |
| Bannerlord 日志 | 只有 MCM/ButterLib 的行，**完全没有 `CommandLabProbe` 的痕迹** | Bannerlord 默认日志**不记录普通 SubModule 的加载** ⇒ 无法据此判断 SubModule 是否加载 |
| 探针自身产物 | **空** | 产物只在 Mission tick 里落盘，而崩溃早于 tick；`_hostLines` 全在内存里随之丢失 |

⇒ **无法区分"SubModule 没加载（崩溃与探针无关）"与"加载了但崩在 Mission 里"**，也无法知道
抛异常的具体位置。这是本节新增基建要解决的核心问题。

### 12.3 已做过的静态分析（未定论）

`Gate()` 是首要嫌疑：它在宿主调用写入口时**同步**执行，而**当时没有 try/catch** ⇒
异常会直接穿透到宿主调用栈（与崩溃形态吻合）。且时间线自洽 ——
**首跑时 patch 未生效、`Gate()` 从未执行，游戏正常；形参名修好后 `Gate()` 首次真实执行，随即崩溃。**

`Gate()` 链路里无保护的索引访问有两处：

- `ProbeRuntime` → `WritePort.SubmitImmediate` → `Evaluate` 的 `var entity = _entities[req.EntityId];`
  （`KeyNotFoundException`，但 `Gate()` 前一步 `Bind()` 已 `RegisterEntity`，理论上应已存在）
- `Evaluate` 内部 `entity[req.Domain]`

**但这只是"最可能的抛点"，不是证据** —— 按调试协议，两轮分析后仍未区分假设就必须换手段，
因此**不据此改代码**，改为装基建取证据。同理，也**不能排除崩溃与探针无关**（用户同时改动了 load order）。

### 12.4 新增诊断基建（本轮实际改动）

| 基建 | 作用 | 实现 |
|---|---|---|
| **启动哨兵** `boot-marker.txt` | **确定性地**回答"SubModule 到底加载了没有" | `ProbeSubModule.OnSubModuleLoad` 末尾调 `WriteBootMarker()`，**立即落盘**，不等 Mission。含时间戳 / `harmonyId` / `patchedCount` / `patchFailure` / 已 patch 方法清单 |
| **崩溃报告** `crash-<ts>.txt` | 拿堆栈 + 现场 | `ProbeRuntime.RecordCrash(where, ex, context)`：**独立文件、不走 Dump 流程**（因为崩溃时可能还没到 dump 时机），内容 = 异常链（逐层 Message + StackTrace）+ `_hostLines` 尾部 200 行 + 当前 binding 列表。带去重（第 1 次 + 每第 50 次写盘），避免同一异常每 tick 写爆磁盘 |
| **`Gate()` 异常兜底** | 探针绝不能弄崩宿主 | `Gate` 改为 try/catch 包装器，转发 `GateCore`；异常 → `RecordCrash` + **`return true`（放行）** |
| **`Observe()` 异常兜底** | 同上（观测型 patch 入口同样在宿主 Prefix 里同步执行） | 同上模式 |
| **`Tick()` 异常兜底** | 每帧入口 | `Tick` → try/catch → `TickCore`；异常 → `RecordCrash`，跳过本帧 |
| **`ProbeMissionBehavior.OnMissionTick` 兜底** | 覆盖 `Mission.Current` 访问与周期 dump | 同上模式 |
| **`AppDomain.UnhandledException` 处理器** | 崩溃发生在宿主代码里（不在上述兜底范围）时也留痕 | 在 `OnSubModuleLoad` 挂一次，处理器内**直接落盘**（不是只 `Note`）—— 进程随即终止，内存里的 `_hostLines` 会丢失 |

> **设计取舍**：兜底 `return true`（放行）会让"该拦的没拦"，但这是探针而非产品的正确取舍 ——
> 实验装置不能弄崩用户的游戏。同时兜底**必须留痕**，否则就变成"用防御性代码掩盖真实缺陷"，
> 反而违反本项目"不写没有 Telemetry 支撑的结论"的红线。

### 12.5 下一步：一次启动即可二分

部署已更新为 `4d2e2d4b…`。重跑第 1 组 `census-baseline`，然后**只看磁盘**：

| 观察 | 结论 |
|---|---|
| 有 `boot-marker.txt`，且 `patchedCount = 7` | SubModule 加载成功、patch 全部生效 ⇒ 失败在更后面 |
| 有 `boot-marker.txt`，但 `patchedCount` ≠ 7 | patch 问题，看 `patchFailure` |
| **没有** `boot-marker.txt` | SubModule **未加载** ⇒ 崩溃与探针无关（去查 load order / 其他 mod） |
| 有 `crash-*.txt` | **直接拿到抛异常的位置与堆栈**，据此定位 |
| 崩溃消失且正常产出 metrics | 说明确由 `Gate()` 链路异常所致（已被兜底放行），从 `[census]` 缺失项反推 |

**独立补充手段**：若仍崩，可先在启动器里**取消勾选 `CommandLabProbe`** 再进一次战斗 ——
不崩则确定与探针相关，仍崩则确定与探针无关（或存在共因）。这是成本最低的二分。

### 12.6 本轮变更清单

| 文件 | 变更 |
|---|---|
| `ProbeRuntime.cs` | 新增 `WriteBootMarker()` / `RecordCrash()` / `RecordFatal()` / `SafeIndex()`；`Gate` → `Gate`+`GateCore`；`Observe` 加兜底；`Tick` → `Tick`+`TickCore` |
| `ProbeSubModule.cs` | `OnSubModuleLoad` 挂 `AppDomain.UnhandledException` + 调 `WriteBootMarker`；`OnMissionTick` → `OnMissionTick`+`TickCore` |

**回归**：Core / Simulation / Telemetry / tests **零改动** ⇒ D0 15/15、E1–E11 51/51、E12 10/10 逐字不变。

### 12.7 第三次实测（12:03）仍崩 + 兜底层级缺陷 + 撤回一个推断

装置版本 `4d2e2d4b`，**仍然崩**。但这次拿到了决定性证据。

**① `boot-marker.txt`（12:03:08）证明装置侧完全正常：**

```text
patchedCount  = 7
patchFailure  = (none)
--- patched methods ---
TaleWorlds.MountAndBlade.Formation.SetArrangementOrder(ArrangementOrder)
TaleWorlds.MountAndBlade.Formation.SetControlledByAI(Boolean, Boolean)
TaleWorlds.MountAndBlade.Formation.SetFacingOrder(FacingOrder)
TaleWorlds.MountAndBlade.Formation.SetFiringOrder(FiringOrder)
TaleWorlds.MountAndBlade.Formation.SetFormOrder(FormOrder, Boolean)
TaleWorlds.MountAndBlade.Formation.SetMovementOrder(MovementOrder)
TaleWorlds.MountAndBlade.Formation.SetRidingOrder(RidingOrder)
```

⇒ SubModule 加载成功、**7 个 patch 全部生效**、`PatchAll` 无异常。
同时**没有 `crash-*.txt`** —— `RecordCrash` 与 `WriteBootMarker` 用同一套写入方式，
而 boot marker 写成功了，故 `RecordCrash` 也具备写入能力 ⇒ **我们的兜底与 AppDomain 处理器
确实没有捕获到任何托管异常**。

**② 发现一个确定的真实缺陷：兜底放错了层级。**

旧代码把 try/catch 加在 `ProbeRuntime.Gate()` / `Observe()` **内部**，但 **Prefix 方法体里
在调用它们之前的表达式**落在兜底之外：

| patch | 兜底之外的表达式 |
|---|---|
| `SetMovementOrder` | `runtime.Bind(__instance).MovementProjection.Project(input.OrderType.ToString())` |
| `SetFacingOrder` | `runtime.Bind(__instance).FacingProjection.Project(order.OrderEnum.ToString())` |
| `SetArrangementOrder` | `runtime.Bind(__instance)` + `Project(...)` |
| 4 个观测型 | `order.OrderEnum.ToString()` 等实参求值 |

这些表达式在宿主调用写入口时**同步**执行，一旦抛异常就**穿透到宿主调用栈** ⇒ 崩游戏，
而且 `ProbeRuntime` 内部的 try/catch **根本来不及覆盖**。

⇒ **兜底必须加在 Prefix 方法体这一层**（从参数解引用到返回值构造的全部代码）。
已修：7 个 Prefix 全部包 try/catch，异常 → `RecordFatal` + `return true`（拦截型）或仅记录（观测型）。

> 这个缺陷本身就说明此前的"防崩"设计不到位 —— **它完全可能就是崩溃原因**。

**③ 撤回一个错误推断（诚实记录）。**

曾据 sigscan 的 "`OrderType` 为 `Public, Sealed` 且无 `SequentialLayout`" 推断它是**引用类型**，
进而猜测 `input.OrderType.ToString()` 在 null 时抛 `NullReferenceException` 是崩溃根因。

**该推断已被编译器证伪**：对 `OrderType` 使用 `?.` 报
`CS0023「运算符"?"无法应用于"OrderType"类型的操作数」` ⇒ `OrderType` 是**值类型**，永远非 null。

**NRE 假设作废，不作为崩溃解释。**

> 方法论教训：`Sealed` + 无 `SequentialLayout` **不能**作为"引用类型"的判据。
> 判定类型要么读 `IsValueType`，要么**让编译器说话**（写一行 `?.` 试编译）。

**④ 当前阻塞：游戏进程残留。**

部署新版本时报 `Device or resource busy` —— `tasklist` 显示 `Bannerlord.exe` PID 36196
**仍在运行但只占 56 K 内存**（崩溃后未退出干净的残留进程）。

> 🚫 **新增操作纪律**：崩溃后**必须先确认 `Bannerlord.exe` 完全退出**，再部署、再重跑。
> 否则会出现"部署看似成功但实际被占用 / 跑的是旧 DLL"的假象 —— 本次即为实例。

**⑤ 下一步（两条，建议都走）。**

1. **最快确定责任归属**：启动器里**取消勾选 `CommandLabProbe`**，进一次战斗。
   不崩 ⇒ 与探针相关；仍崩 ⇒ 与探针无关（去查 load order）。
2. **拿根因**：结束残留进程 → 部署带 Prefix 层兜底的新版本 → 重跑。
   若崩，这次会在**异常源头**捕获并写 `crash-*.txt`，直接拿到异常链与堆栈。

**状态**：新版已编译成功，但**因文件占用未部署**；`Modules\` 当前仍是 `4d2e2d4b`。

### 12.8 分水岭实验：确认崩溃由探针引起 + 根因定位

#### (1) 分水岭实验结果：**确认与探针相关**

用户执行了 §12.7 的"A 方案"（启动器**取消勾选 `CommandLabProbe`**），12:59:57 进了一场新战斗 —— **不崩**。

证据来自 `BattleSizeResized` 的 `VLogs`，它如实记录了实际加载的模块清单：

| 战斗 | `detectedModules=` 中的关键项 |
|---|---|
| **10:46**（不崩） | …SandBoxCore, CustomBattle, Sandbox, StoryMode, BattleSizeResized, **CombatBrain**, CrashDoctor, RTSCamera, RTSCamera.CommandSystem, **CommandLabProbe** |
| **12:59**（不崩） | …SandBoxCore, CustomBattle, Sandbox, StoryMode, BattleSizeResized, CrashDoctor, RTSCamera, RTSCamera.CommandSystem |

**12:59 的清单里 `CommandLabProbe` 不存在** ⇒ 该场战斗**没有加载探针** ⇒ 不崩。

**⇒ 结论：崩溃由 `CommandLabProbe` 引起。这是受控实验（唯一变量 = 是否加载探针），证据明确。**

**顺带排除的两项**：
- **`CombatBrain` 不是原因**：10:46 它开着且不崩（当时探针 patch 未生效）；12:59 它也关着但不崩。
- **兵力规模不是原因**：10:46 为 `totals=150/150`，12:59 为 `totals=408/408`，两个不同规模都不崩。

> 📌 **同时登记的偏差**：该日志报告 `Game Version: v1.4.8.119303`，而项目文档此前一直记"宿主 v1.3.4"。
> 此项**不影响**已有取证结论（sigscan / ilscan 都是对实际 DLL 做的），但**版本号口径需要更正**。

#### (2) 根因：`Formation.GetHashCode()` 在 `Team == null` 时抛 NRE

**定位链**（每步都有证据）：

1. **`boot-marker.txt` 证明装置侧正常**：`patchedCount = 7`、`patchFailure = (none)`；
   `RecordCrash` 与 `WriteBootMarker` 同一套写入方式，boot marker 能写成功 ⇒ 崩溃**不是**被我们兜底捕获的异常。
2. **⇒ 崩溃点必然在兜底覆盖不到的地方**：即宿主回调路径上、**同步执行**的表达式。
3. **检查 `Formation` 是否重写 `GetHashCode`**（因为我们的 `_bindings` 是 `Dictionary<Formation, _>`）：
   `sigscan` 显示 **`Formation` 重写了 `public Int32 GetHashCode()`**，且**未重写 `Equals`**。
4. **`ilscan dump` 出它的 IL**：

   ```text
   0: Ldarg_0
   1: Ldfld    Formation::Team            ← 直接读字段
   6: Callvirt Team::get_TeamIndex        ← ★ Team 为 null ⇒ NullReferenceException
   11: Ldc_i4_s 10
   13: Mul                                ← TeamIndex * 10
   14: Ldarg_0
   15: Ldfld    Formation::FormationIndex
   20: Add                                ← + FormationIndex
   21: Ret
   ```

5. **而 `Bind()` 的第一行就触发它**：

   ```csharp
   public FormationBinding Bind(Formation formation)
   {
       if (_bindings.TryGetValue(formation, out var existing)) return existing;  // ← Dictionary 查找会调 GetHashCode()
       var id = EntityIdOf(formation);        // ← 这里虽然有 try/catch，但已经晚了
       ...
       _bindings[formation] = binding;        // ← 索引器赋值也会调 GetHashCode()
   ```

**完整因果链**：Mission 初始化阶段宿主把 `Team == null` 的 `Formation` 传进写入口
→ `Prefix` 调 `runtime.Bind(__instance)` → `Dictionary.TryGetValue` 调 `formation.GetHashCode()`
→ **NRE** → 旧版本里这段在 `Gate()` 的 try/catch **之外**（见 §12.7 ②）→ **穿透宿主调用栈崩游戏**
→ 崩得比任何 dump 时机都早 ⇒ **磁盘上无痕迹**。

**这解释了全部观测**：崩在 Mission 初始化、间歇性（取决于该阶段是否出现 `Team == null` 的对象）、
兜底抓不到、`boot-marker` 却正常。

**为什么"间歇"而不是必崩**：10:46 那次 patch 未生效（走不到 `Bind`）；12:19 那次启动了但未进战斗；
12:59 那次没加载探针。只有"patch 生效 **且** 进入了战斗"才会走到这一步。

#### (3) 修复：改用引用相等比较器

`_bindings` 的构造改为传入自定义比较器：

```csharp
private sealed class ByReference : IEqualityComparer<Formation>
{
    public static readonly ByReference Instance = new ByReference();
    public bool Equals(Formation x, Formation y) => ReferenceEquals(x, y);
    public int GetHashCode(Formation obj) =>
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
```

**语义不变**：`Formation` 未重写 `Equals`，默认即引用比较，所以原行为本就等价于引用比较。
换比较器**只做两件事**：① 绕开会抛异常的 `GetHashCode`；② 省掉一次 `Team.get_TeamIndex` 调用
（该属性可能落到 native，在高频的 `SetMovementOrder` 路径上是可观开销）。

**同时保留 §12.7 ② 的 Prefix 层兜底** —— 两者互补：比较器消除已知抛点，兜底防未知抛点。

#### (4) 尚未验证的剩余风险（登记，不据此改代码）

- **并发访问**：Phase 2 已取证原生 `doAsyncAITick → TickOccasionally` 跑 **worker 线程**。
  若宿主从 worker 线程调用这些写入口，则 `_bindings` / `_hostLines` / `_census` 等
  非线程安全集合会被主线程（`Tick`）与 worker 线程（`Gate`）**并发访问**。
  本轮**未**加锁，也**未**加线程观测 —— 因为已定位到确定性根因，一次只改一个变量便于验证。
  **若修复后仍崩，下一轮第一件事是加线程 ID 观测**（低成本取证），确认后再决定是否加锁。
- `Formation.GetHashCode()` 的 native 依赖说明：**任何以 `Formation` 为键的 `Dictionary`/`HashSet` 都有同样风险**，
  本项目已全部改用引用比较器。

### 12.9 真正的崩溃根因：`PatchAll()` 触发 `MovementOrder` 静态初始化（含完整证据链）

**§12.8 的 `GetHashCode` 修复方向错误** —— 那个 NRE 风险真实存在（已修，属正当加固），
**但它不是本次崩溃的原因**。第四次实测（13:11:47）仍崩，促使我们换手段取证，从而拿到完整堆栈。

#### (1) 证据来源：CrashDoctor

本机装有 `CrashDoctor`（崩溃诊断 mod）。它的 `Modules\CrashDoctor\managed_crashes.jsonl` +
`managed_reports\<sig>\report.txt` 记录了托管异常。**这是此前三个通道（事件日志 / Bannerlord 日志 /
探针产物）全部失效时唯一有效的证据源。**

第 4 次崩溃的完整堆栈：

```text
System.TypeInitializationException:
  The type initializer for 'TaleWorlds.MountAndBlade.MovementOrder' threw an exception.
  ---> System.NullReferenceException: Object reference not set to an instance of an object.
     at TaleWorlds.MountAndBlade.MovementOrder..ctor(MovementOrderEnum orderEnum)
     at TaleWorlds.MountAndBlade.MovementOrder..cctor()
     --- End of inner exception stack trace ---
     at TaleWorlds.MountAndBlade.Formation.ResetAux()
     at TaleWorlds.MountAndBlade.Formation.Reset()
     at TaleWorlds.MountAndBlade.Team.Initialize()
     at TaleWorlds.MountAndBlade.Mission+TeamCollection.Add(...)
     at TaleWorlds.MountAndBlade.MissionCombatantsLogic.AddPlayerTeam(BattleSideEnum playerSide)
     at TaleWorlds.MountAndBlade.MissionCombatantsLogic.OnBehaviorInitialize()
     at TaleWorlds.MountAndBlade.Mission.AfterStart()
     at TaleWorlds.MountAndBlade.MissionState.FinishMissionLoading()
     at TaleWorlds.MountAndBlade.MissionState.TickLoading(Single realDt)
```

CrashDoctor 统计：签名 `b1dfded2` 共崩 **4 次** —— 11:56:56 / 12:06:03 / 12:20:42 / 13:11:47。

#### (2) `MovementOrder..ctor` 的 IL：NRE 的确切来源

```text
101: Ldarg_0
102: Call     Mission::get_Current            ← ★ Mission.Current
107: Callvirt Mission::get_CurrentTime        ← null 时 → NullReferenceException
112: Ldc_r4   0.5
118: Newobj   TaleWorlds.Core.Timer::.ctor
123: Stfld    MovementOrder::_tickTimer
```

**`MovementOrder` 的静态构造（`..cctor` 创建 `MovementOrderNull`/`Charge`/`Retreat`/`Stop`/`Advance`/`FallBack`）
要求 `Mission.Current != null`** —— 否则构造里的 `Mission::get_Current.CurrentTime` 抛 NRE。

#### (3) 关键判据：每次致命崩溃前，启动阶段都有一次被捕获的 NRE

`CrashDoctor\crashdoctor.log` 里的 `[FirstChance] NullReferenceException` 与致命崩溃的配对：

| 启动阶段 FirstChance NRE | → | 进战斗 FATAL |
|---|---|---|
| 11:53:29 | → | 11:56:56 |
| 12:03:08 | → | 12:06:03 |
| 12:19:32 | → | 12:20:42 |
| 13:11:37 | → | 13:11:47 |

（12:17:32 另有一次 NRE，无对应 FATAL —— 那次启动了但没进战斗。）

**而 10:46 那次（Harmony 形参名笔误、patch 全部失效）启动阶段完全没有这个 NRE。**

⇒ **模式成立：`PatchAll()` 成功 ⇒ 启动阶段 `MovementOrder..cctor()` 失败 ⇒ 该类型被永久标记为
初始化失败 ⇒ 进战斗时 `Formation.ResetAux()` 一接触就抛 `TypeInitializationException` ⇒ 必崩。**

#### (4) 机制

`Harmony.PatchAll()` 处理 `[HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]`
时需要解析目标方法的参数类型 `MovementOrder`（一个 struct），这一过程**触发了它的静态构造函数**。
而 `OnSubModuleLoad` 阶段 `Mission.Current` **必然是 null**（还没进任何战斗）
⇒ `..cctor()` 抛 NRE ⇒ 异常被 Harmony 内部吞掉（故表现为 `FirstChance`，`PatchAll` 本身"成功"）
⇒ **但 CLR 已把该类型标记为不可初始化，且这是进程级、不可恢复的**。

**这解释了全部此前无法解释的观测**：
- 崩在 Mission 初始化（`Team.Initialize → Formation.ResetAux`）
- 有 patch 才崩、无 patch 不崩（`PatchAll` 是触发器）
- `patchFailure = (none)` 却崩（异常被 Harmony 吞成 FirstChance）
- 我们所有兜底都抓不到（异常不在我们的托管调用栈里，发生在 Harmony 内部 + 之后由游戏代码抛出）

#### (5) 修复：把 patch 推迟到第一次 Mission tick

`OnSubModuleLoad` **不再** patch；改由 `ProbeMissionBehavior.OnMissionTick` 的**首次执行**调用
`ApplyPatchesNow()`。此时 Mission 已在运行 ⇒ `Mission.Current != null` ⇒ `..cctor()` 可成功，
且游戏自己早已初始化过该类型，此时 patch 安全。

新增 `patch-marker.txt` 哨兵，记录 patch 实际结果 + **`missionCurrentNull` 字段**
（直接自证"patch 时 `Mission.Current` 是否为 null"）。

**代价（登记）**：Mission `AfterStart` 阶段（`Formation.ResetAux` 等）的少量初始写入落在 patch 生效之前，
不会被观测。对"稳态写入口流量普查"无影响 —— census 关心的是 AI 稳态期的写入分布。

#### (6) 教训

1. **`Harmony.PatchAll()` 会解析目标方法的参数类型 ⇒ 可能触发宿主的静态构造函数。**
   若该静态构造依赖运行时状态（如 `Mission.Current`），**patch 时机就成了正确性问题**，
   而不仅是性能问题。
2. **`PatchAll()` "成功"不等于无害**：内部被吞掉的 FirstChance 异常可能已经造成进程级不可逆损害。
   `patchFailure = (none)` 与"游戏能跑"都不是充分证据。
3. **一台机器上装着的崩溃诊断 mod 是宝贵证据源** —— 当事件日志与游戏日志都不给堆栈时，
   `CrashDoctor` 的 `managed_crashes.jsonl` 直接给出了完整托管堆栈。**排查卡住时应优先检查这类工具是否已有记录。**
