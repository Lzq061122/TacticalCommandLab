# 07a · Architecture A / B / C 对比（Architecture Comparison）

> Phase 8 交付物。生成时间：2026-09-20 GMT+8（会话 8）
> 依据：`05-experiment-plan.md` §6 判别力矩阵、`06-experiment-results.md` §4/§5、本轮新跑的 **E12**。
> 硬约束：**不得预设赢家**（`04` C6：第 5 类 Native Pipeline 在通用文献中无任何对应机制，A/B/C 无法从文献获得背书）；**每个维度的优劣必须引用实验编号**，不允许"理论上应当更好"。
> 红线：零 `CombatBrain.*` / `TaleWorlds.*` 引用；零真实战术语义。

---

## 0. 结论先行

**不存在"三选一"的干净赢家，也不需要三选一。** 证据指向的是一个**分层组合**，而三个候选架构各自在其中占据不同位置：

| 候选 | 硬门限判定 | 定位 |
|---|---|---|
| **A · External Commander（双 Writer）** | ❌ **判负** | 不是架构选项，是被 E7 7f 与 E9 直接证伪的红线手法 |
| **B · Full Authority Takeover（写入口抑制）** | ✅ **唯一过门限** | 受限的"夺取通道"，代价是复杂度与 Harmony 耦合 |
| **C · Native Pipeline Integration（出 Intent）** | ❌ 过不了第二条门限 | 常态"影响力通道"，Writer 净增 0、耦合最低 |

**三条可直接引用的裁决：**

1. **只有 B 能同时满足 `unauthorized_writes == 0` 与 `intent_hold_share ≥ 95%`**（E7 7d + E12 ① ②）。A 两条都不满足（E9 9b：120 / Oscillating；E12：hold 0%–50%），C 过不了第二条（E12：非独占授权的 hold 不可保证）。
2. **B 的夺取通道必须声明 Level 5（Full），不得停在 Level 4（Override）或 Level 2（Recommend）**（E12 ④ ⑤ ⑥ ⑧）。这是 **H-I 的裁决**，并且**修正**了 `04` §3.2.8 的"要么 2 要么 5"——Level 2 的"稳定"是意图零落地的伪稳定。
3. **B 能达到的控制上限由"写入口覆盖率"决定，而不是由 Level 决定**（E12 ⑨：Level 5 下 gated 域 unauth=0，non-gated 域 unauth=20）。因此**必须按域分别选择通道**，而不是整实体二选一——这与 E8 对 H-C 的"方向支持"一致。

---

## 1. 三个候选架构的精确定义

判定规则用**"写入者数量"与"强制点位置"**两个可观测变量给出，避免用形容词定义架构。

| | **A · External Commander** | **B · Full Authority Takeover** | **C · Native Pipeline Integration** |
|---|---|---|---|
| 写入者数量（相对原生） | **净增 +1**（与原生链并列写入） | 净增 0（替换产出）或 −1（抑制原生写） | **净增 0**（出 Intent，由原生侧决定执行） |
| 控制权载体 | 无（靠优先级 / 频率 / 时序） | **Ownership + Lease + Token**，强制点在写入口 | 无（Mod 只有影响力，仲裁权留给原生） |
| 冲突解决机制 | Arbitration / Last-Writer-Wins | **Reject（写入口）** | 原生侧仲裁（权重 / Behavior 选择） |
| Mod 需要 patch 原生方法吗 | 否 | **是**（写入口，Prefix 抑制） | 是（权重 / 纯计算函数），但**不抑制** |
| 失败模式 | 抖动、饥饿、归因缺失 | 覆盖率不足 → 部分域失控；Release 残留 | **意图不落地**（被原生权重无视） |

> **命名与 `03` 的关系**：`03` 判定 RBM 是**路线 C 的真实工程案例**（Writer 净增 0、`typeof(FormationAI)` 零命中、仲裁权完全留给原生）。本 Phase 不因此给 C 加分——`03` 同时明确"RBM 只是一个案例，Phase 8 不得预设赢家"。

---

## 2. E12 · Authority Level 谱系对照（本轮新增证据，补 H-I）

**背景**：`06` §3 把 H-I 标为"未验证"，`06` §6 遗留 8 要求 Phase 8 补 Level 2 / 4 / 5 对照。本轮补跑 E12（`tests/artifacts/e12-level-spectrum.txt`，**判据 10/10 通过**）。

**装置**：Lab 于 t=2 取得 Movement 授权（ttl=25），t=22 释放；Ns 每 0.5s 写 ADVANCE（gated）；竞争者仲裁优先级标定为 3（介于 Level 2 与 4 之间，理由见 §8 遗留 2）。

**新增两个口径**（Phase 7 的 14 项指标不足以支撑本对比）：
- `intent_hold_share` = 归属窗口内"实体值 == Lab 意图值"的时长占比 —— **控制是否真的落地**
- `lab_delivery_rate` = Lab 生效变更 / Lab 写入尝试 —— 意图是否被写入口放行

### 2.1 主矩阵

| Level | unauthW(Mv) | osc (/s) | ocps | avg_life (s) | **hold** | deliver | 分级 |
|---|---|---|---|---|---|---|---|
| 1 Monitor | 0 | 0.0000 | 0.0333 | 29.900 | **0.0%** | 0.0% | Stable |
| 2 Recommend | 0 | 0.0000 | 0.0333 | 29.900 | **0.0%** | 0.0% | Stable |
| 3 Approval | 20 | 1.3000 | 1.3667 | 0.535 | **50.0%** | 95.2% | Oscillating |
| 4 Override | 20 | 1.3000 | 1.3667 | 0.535 | **50.0%** | 95.2% | Oscillating |
| **5 Full** | **0** | **0.0333** | 0.1000 | 11.200 | **100.0%** | 4.8%* | **Stable** |

\* `deliver` 在 L5 下仅 4.8% 是**口径缺陷**而非控制失败：21 次尝试中 20 次因"值已是 HOLD"不产生变更。该口径不得单独作判据，一律以 `hold` 为准（已登记，§8 遗留 1）。

### 2.2 H-I 裁决：**成立，且强于 `04` 原表述**

- `osc`：L2 = 0.0000 < **L4 = 1.3000** > L5 = 0.0333 → **H-I 的核心不等式成立**（判据 ④）。
- 但 `04` 的推论"要么 2 要么 5，不要停在 4"**需要加限定**：
  - **L2 的"稳定"是伪稳定**：`unauthW=0` 且 Stable，但 **hold = 0%** —— Lab 的意图一次都没落地，实体全程服从 Ns。它在 Phase 7 的全部指标上与 L5 **完全同形**（判据 ⑧）。
  - **L4 是真实抖动**：hold = 50%，osc = 1.3/s —— 赢下同刻碰撞却挡不住非碰撞写入。
  - **只有 L5 同时满足"稳定 + 落地"**（判据 ① ② ⑥）。

> **修正表述（供 Phase 11 采用）**：Level 2 只在"你本来就不打算取得控制权"时成立；**一旦目标是取得控制权，Level 2 不构成 Level 5 的替代方案**。

### 2.3 方法论结论（影响 Phase 10 的判读方式）

> **`unauthorized_writes == 0` 且 Stable 是"取得控制权"的必要非充分条件。**
> 本装置下 L1 / L2 / L5 三者在该指标与分级上完全相同（0、Stable），只有 `intent_hold_share` 能把"从未抢到"（0%）与"完全控制"（100%）分开。
> **移交 Phase 10**：H1/H2/H3 的 Telemetry 必须同时采集 `intent_hold_share`，否则实测会被"零越权"误导为"控制成功"。

### 2.4 写入口覆盖率（判据 ⑨，直接约束 B）

Lab 以 **Level 5 + Full** 同时接管 Movement（gated）与 Arrangement（non-gated）：

| 域 | 路径 | unauthW |
|---|---|---|
| Movement | gated | **0** |
| Arrangement | non-gated（自维护 Timer + 兜底回写） | **20** |

→ **Level 再高也管不到未被写入口覆盖的路径**。全局分级因此从 Stable 劣化到 Oscillating。这与 E8 8c（整实体接管在 Arrangement 域失效，unauthW=21）**独立复现**。

**对 B 的含义**：B 的可行性不取决于"能不能拿到 Level 5"，而取决于 **patch 面是否覆盖全部写入口**。按 `02`，写入口是 6 个而非 3 个，且 Arrangement 侧的自维护 Timer 不受 `IsAIControlled` 门控 —— 这是 B 的头号风险，**必须由 Phase 10 H2 实测**。

---

## 3. 比较维度与证据化评判

> 维度数说明：`HANDOFF` §5 写"12 项"但实际列出 13 项（Stability / Authority clarity / Implementation complexity / Maintainability / Testability / Native coupling / Harmony coupling / Compatibility / Performance / Debuggability / Decision explainability / Command oscillation / Recovery after release）。本表按 **13 项**执行，差异已登记（§8 遗留 3）。
> 评分：**++ 优 / + 较优 / 0 中 / − 较差 / −− 差**。每格必须带实验编号，无编号者不得写入。

| # | 维度 | A | B | C | 关键证据 |
|---|---|---|---|---|---|
| 1 | **Stability** | −− | **++**（条件*） | − | E7 7d（0/Stable，唯一）；E9 9b（120/Oscillating）；E2 2b（osc 0.73/s）；E12 L5（hold 100%）vs L2/L4（0%/50%） |
| 2 | **Authority clarity** | −− | **++** | + | `04` C1（Ownership 决定控制权）；E4 4d（Token 0 vs 29）；E12（Level 谱系语义清晰）；C 的 clarity 来自"明确知道自己没有控制权"（`04` C5 / `03` 仲裁权不下沉） |
| 3 | **Implementation complexity** | + | −− | 0 | `02`：路线 B 的 patch 面**至少 6 处且分散**；E5（Release 需处理宿主内部状态/孤儿定时器）；`03`：`CalculateCurrentOrder` 是唯一"只算不写"的纯函数注入点 → C 中等 |
| 4 | **Maintainability** | − | 0 | **+** | 红线"无限叠加 Priority 是失败路径"（E2 2c：starvation 15）；B 集中但脆弱（宿主改签名即崩）；C 接入点稳定 |
| 5 | **Testability** | − | **++** | 0 | E3 3b vs 3c（0 vs 24）、E6 6b vs 6c（4 vs 0）：**写入口是可断言的单点**；`replay_hash` 可重放（D0）；A 依赖时序（E1 顺序敏感）；C 需宿主运行时 |
| 6 | **Native coupling** | **+** | −− | − | A 不碰原生；B 必须 patch 原生写入口；C 依赖 Behavior/Weight 契约 |
| 7 | **Harmony coupling** | **+** | −− | 0 | `03`：Prefix `return false` 是 B 的主要手段（接管型 + 条件型）；C 改造但不抑制；A 无 patch |
| 8 | **Compatibility** | − | −− | **+** | E9 9a（Replace，Writer 净增 0）vs 9b（Append，劣化一个数量级）；`03`：RBM 81 patch 说明写入口附近生态拥挤，B 的 patch 冲突风险最高 |
| 9 | **Performance** | −− | **+** | **+** | E7 7f：高频重写 attempts 61→600、writes 61→121、osc 2→4 **三项全升**；B 在入口早退；C 随原生周期无额外写入 |
| 10 | **Debuggability** | − | **++** | 0 | E11：归因完整率 **50%（Blackboard）vs 100%（Command Bus）** —— 只有"所有变更经单一写入口"的架构可达 100%；C 的归因链断在原生执行侧 |
| 11 | **Decision explainability** | − | **++** | + | E1：LWW 下终值由执行顺序决定，无法解释"为什么是这个值"；B 的 DecisionTrace 可给出 owner/lease/token/reason；C 可解释 Intent，但"为何未被执行"不可解释（E2 2c：max_pending 58s） |
| 12 | **Command oscillation** | −− | **++**（条件*） | − | E12 L4 osc 1.3/s vs L5 0.033/s；E2 2b 0.73/s；E9 9b Oscillating |
| 13 | **Recovery after release** | + | − | **+** | E5：H-G 成立，(T,Cancel)=1 失败（孤儿定时器 5）→ **B 有真实恢复负担**；A/C 无需 release。⚠ A 的"无需恢复"与"从未取得控制权"是同一枚硬币（E12 ⑤）——**不得把这一格算作 A 的优势** |

\* B 的第 1、12 两项的"++"**带条件**：仅在写入口覆盖率足够时成立（E12 ⑨）。覆盖率不足时 B 退化为 C 的语义（只有影响力）。

---

## 4. 硬门限判定（谁能算"取得控制权"）

Phase 7 只给了第一条例据充分的门限。本轮 E12 证明它**不充分**，故补第二条：

| 门限 | 内容 | 依据 | A | B | C |
|---|---|---|---|---|---|
| **G1** | `unauthorized_writes == 0` | E7 7d：唯一能把越权生效写入压到 0 的是 Ownership+Lease+写入口 Reject | ❌ | ✅ | ❌ |
| **G2** | `intent_hold_share ≥ 95%` | E12 ① ② ⑧：G1 单独会把"从未抢到"与"完全控制"判为同形 | ❌ | ✅ | ❌ |

- **A 的 G1 失败证据**：E9 9b（Append 并列 Writer → 120，Oscillating）；E12 L4（20，osc 1.3/s）。
- **C 的 G2 失败证据**：E12 —— C 在结构上等价于"非独占授权参与原生仲裁"，其 hold 只能落在 **0%（权重不足，意图被无视）或 50%（权重同级，抖动）**，无法达到 95%。这不是实现质量问题，是**非独占语义的结构上限**。
- **B 的通过条件**：G1/G2 在 gated 域成立；non-gated 域（E12 ⑨）不成立的**唯一补救是扩大 patch 覆盖率**，而不是提高 Level。

---

## 5. 综合评分卡

| 架构 | 优（++） | 较优（+） | 中（0） | 较差（−） | 差（−−） | 过门限 |
|---|---|---|---|---|---|---|
| **A** | 0 | 3 | 0 | 2 | 6 | ❌ G1+G2 均不过 |
| **B** | 6 | 0 | 2 | 0 | 5 | ✅（覆盖率条件） |
| **C** | 1 | 4 | 4 | 4 | 0 | ❌ G2 不过 |

**读法**：B 的"++ 最多、−− 也最多"是同一个事实的两面——**它把控制权问题集中到了一处，因此既最好测也最难建**。C 的分布最平均，代价是"没有控制权"。

---

## 6. 结论：分层组合架构（不是三选一）

证据不支持"选一个"，支持"**分域 + 分场景切换通道**"：

```
                    ┌─ 常态 ───── C 通道（出 Intent，Writer 净增 0，仲裁权留给原生）
   单个 Command ────┤                    E9 9a / 03 Writer 净增 0 / 04 C5
      Domain        │
                    └─ 需要确定性控制 ── B 通道（Ownership + Lease + 写入口 Reject，Level 5 Full）
                                         E7 7d / E3 / E4 4d / E6 6c / E12 ①②
                                         约束：按域、带 Lease、必须可 Release（E5 H-G）

   A 通道：判负，不列入架构选项
   依据：E7 7f（高频重写三项全升，且 7f-stall 证明写入从未被真正阻止）、E9 9b（并列 Writer 劣化一个数量级）
```

**四条可执行的架构约束（给 Phase 11 定稿用）：**

1. **默认走 C，只在需要确定性时升级到 B。** 常态下 Writer 净增必须为 0（E9 9a）；升级必须按域（E8 / E12 ⑨）。
2. **B 的升级一律声明 Level 5（Full）+ Lease + Token**，禁止用 Level 4 (Override) 或 Level 2 (Recommend)（E12 ④ ⑤ ⑧，H-I 裁决）。
3. **Reject 与 Fencing 一律落在写入口，不得放在 Mod 侧决策函数**（E3 0 vs 24；E6 0 vs 4；`04` C2）。
4. **Release 必须显式建模恢复语义**（E5：Cancel 会遗留孤儿定时器 → 1 次恢复失败）。B 的"能夺取"与"能安全交还"必须同时验收，不可只验前者（红线：永久关闭原生 AI 不算解决）。

**A 被判负的性质**：不是"较差"，是**被证伪**。E7 7f 表明提高写入频率连表观指标都不改善（三项全升），7f-stall 进一步证明 Ns 的写入从未被真正阻止——这正是 `HANDOFF` §7 红线"靠谁写得更快实现 Authority"的量化证据。

---

## 7. 移交项

| 去向 | 要求 | 原因 |
|---|---|---|
| **Phase 10 · H2** | 必须实测 **non-gated 路径能否被 patch 覆盖**（Arrangement 自维护 Timer 与兜底回写） | E12 ⑨ 是条件预测，其前提来自 `02` 的门控矩阵而非实测；若覆盖率做不到，B 只能覆盖 gated 域 |
| **Phase 10 · H1/H2/H3** | Telemetry 必须同时采集 **`intent_hold_share`**，与 `unauthorized_writes` 同表判读 | E12 ⑧：单看越权数会把"从未抢到"误判为"控制成功" |
| **Phase 10 · H3** | 需实测**是否存在权重同级竞争者** | E12 ⑩：竞争者优先级为 0 时 L2 与 L4 完全不可区分 —— Level 谱系的语义依赖同级竞争者存在（宿主侧对应"是否存在另一个带权重的 Behavior"） |
| **Phase 11** | 裁定：① 三域各自的默认/夺取通道选择表 ② `Proposal` 必要性（D5，弱结论"Proposal 不必要"）③ 把 `intent_hold_share` 写入指标集（作为第 15 项） | 本 Phase 只做对比，不定稿 |
| **`05` / `06` 修订** | 补入 `intent_hold_share` 定义与 G2 门限 | E12 暴露了 G1 的不充分性 |

---

## 8. 遗留与偏差登记（本轮新增）

| # | 遗留项 | 影响 | 建议 |
|---|---|---|---|
| 1 | **`lab_delivery_rate` 口径缺陷**（写同值不计变更 → L5 仅 4.8%） | 该口径会低估"已取得并保持控制"的情形 | 不作为判据；Phase 11 若保留该指标，须改为"意图值在位"口径（即 `hold`） |
| 2 | **E12 竞争者优先级标定为 3** | 若竞争者优先级为 0，L2 与 L4 完全不可区分（判据 ⑩）→ **Level 谱系的可观测性依赖同级竞争者存在** | 已作为判别力边界登记，不是结论依据；宿主侧是否存在同级竞争者须由 Phase 10 实测 |
| 3 | **`HANDOFF` §5 维度数写"12 项"实际列出 13 项** | 表述不一致 | 本表按 13 项执行；建议下次回写 HANDOFF 时改为 13 |
| 4 | **释放恢复 0 失败属平凡成立** | "Level 越高恢复越难"未获支持 | 沿用 `06` 遗留 4 的处理：不作判据；Phase 10 实测若出现恢复失败再回修 |
| 5 | **Ns 仍是模型而非实测**（`06` 遗留 1，未消除） | 本轮全部结论仍带"若宿主行为如 Ns"的前提 | Phase 10 复核，尤以 non-gated 路径为要 |
| 6 | **L3 (Approval) 与竞争者同优先级时胜负由 WriterId 字典序决定** | 该档位的裁决无语义基础 | 登记；Phase 11 定稿时建议不启用 Level 3（与"不要停在 4"同源） |

---

## 9. 红线自检

| 红线 | 本轮落实 |
|---|---|
| 预设 A/B/C 赢家 | ✔ 未预设；结论是"分层组合"，A 判负由 E7 7f / E9 直接证伪，非先验 |
| 用"理论上应当更好"下结论 | ✔ 13 个维度 × 3 架构，每格均挂实验编号；无编号的论断已剔除 |
| 引用 `CombatBrain.*` / `TaleWorlds.*` | ✔ 本文档零引用（架构层不出现宿主命名空间） |
| 用真实战术做基础实验 | ✔ E12 仅用 `Lab` / `Native`、`HOLD` / `ADVANCE` / `MOVE_A` |
| 靠"每 Tick 重复 SetOrder"压过原生 | ✔ E7 7f + 7f-stall 已证伪，并据此把 A 判负 |
| 没有 Telemetry 支撑的稳定性结论 | ✔ 全部结论来自 `tests/artifacts/e12-level-spectrum.txt` 与 `e1-e11-results.txt` |
| 永久关闭原生 AI 宣称已解决 | ✔ G1/G2 之外新增约束 4：Release 与恢复必须同时验收 |
