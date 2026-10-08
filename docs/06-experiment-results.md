# 06 · 实验结果（Experiment Results）

> Phase 7 交付物 · 2026-09-20
> 状态：**已完成**（E1–E11 全部跑完，判据 **51/51 通过**）
> 前置：`01`（五类冲突 / H-A~H-E）、`02`（原生链参数）、`03`（RBM）、`04`（H-F~H-J）、`05`（实验计划）、Phase 6 工程
> 原始证据：`tests/artifacts/e1-e11-results.txt`（每次运行覆盖写）、`tests/artifacts/d0-gate.txt`（D0 门禁，**15/15**）
> 复现命令：`dotnet run --project tests\CommandLab.Tests\CommandLab.Tests.csproj`

---

## 0. 执行摘要（结论先行）

1. **本轮先做校准，再做采集**（`05` §9 遗留 2）。校准过程中发现并修复了 `average_command_lifetime` 的一处**指标定义缺陷**（§1.2），该缺陷会把"值从头到尾没变过"误判成"抖动"。修复后稳定性分级才可用。
2. **核心裁决点 E7 的结论是单向的**：六种手段里**只有 Ownership + Lease（7d）把 `unauthorized_writes` 压到 0 且分级为"稳定"**；迟滞 / 最短驻留 / 状态去抖三种手段把 `oscillation_frequency` 压到 0（降幅 100%），但 `unauthorized_writes` 仍为 1 —— **表观稳定，越权写入依旧存在**。
3. **红线"高频压制"被证伪得更彻底**：提高写入频率（周期 1.0s → 0.1s）后 `unauthorized_write_attempts` 61 → 600、`unauthorized_writes` 61 → 121、`oscillation_frequency` 2.00 → 4.00，**三项全部上升**。并且 7f-stall 变体证明：Ns 的写入**从未被真正阻止**，高频只是让 tick 末的采样值偏向自己。
4. **H-F / H-G / H-J / H-H 四条假设成立**；**H-E（部分）成立**；**H-C 获得弱证据（条件式）**。详见 §3。
5. **Reject / Fencing 的位置是决定性的**：写入口侧 0 次越权 vs 决策器侧 24 次越权（E3）；写入口侧 0 次 stale vs 决策器侧 4 次 stale（E6）。这与 `04` C2 的"强制点必须在被保护资源"完全吻合，且**首次有了本项目的量化数据**。
6. **E8 复现了 `02` 的门控矩阵**：Arrangement 域在三级所有配置下都出现 non-gated 自维护写入（86 次），说明 Ns 模型保真，E8 的预测力成立。但"接管面越大恢复越难"未能复现（三级恢复失败数全为 0），**判据④ 只是平凡成立**，真正的判别信号是跨域副作用 0 / 0 / 21 与全局分级 Stable → Marginal → Oscillating。已作为偏差登记（§6）。

---

## 1. 第一步：装置校准（`05` §9 遗留 2）

`05` 明确要求：稳定性阈值（0.05 / 0.5 / 0.2 / 2×T_eval）是先验设定值，**Phase 7 必须先用 E1 的分布校准再采集**。

### 1.1 D0 门禁（前置闸）

| 项 | 结果 |
|---|---|
| 同一 scenario 三次重放 `replay_hash` | 3/3 全同 |
| 置换注册序后的 hash | 与基础序**不同** → 顺序敏感这一性质可观测 |
| 空跑 60s | 无 NaN / Infinity，0 次写入 |
| 汇总 | **15/15 PASS** |

校准改动后已重跑门禁，仍 15/15。

### 1.2 校准发现：`average_command_lifetime` 的右删失缺陷（**本轮最实质的一处装置修改**）

**现象**：E7 的 7b（迟滞）/ 7c（最短驻留）/ 7e（状态去抖）三组 `oscillation_frequency` 已降到 0，却被判为 **Oscillating（抖动）**。

**根因**：这三组全程只发生过 1 次生效变更。`05` §1.5 把 `average_command_lifetime` 定义为"从生效写入到被下一次生效写入覆盖的时长均值"——**只有 1 次变更时没有样本**，Phase 6 的实现回退为 `0.0`。而"抖动"判据之一是 `average_command_lifetime < 2 × T_eval = 1.0`，于是 `0.0 < 1.0` 恒成立 → **"值从头到尾没变过"被判成"命令活不过两倍原生周期"**，语义完全颠倒。

**修正**（`src/CommandLab.Telemetry/Metrics.cs`）：对每个只发生过 1 次变更的 `(实体, 域)`，补一条**右删失**样本 `simulationSeconds − 首次变更时刻`。语义是"该值一直存活到仿真结束"。

**修正前后的对照**（同一批 E7 数据）：

| 组 | 修正前 grade | 修正后 grade | `average_command_lifetime` |
|---|---|---|---|
| 7b-迟滞15% | Oscillating ❌ | **Marginal** | 0.0000 → **59.90 s** |
| 7c-最短驻留2s | Marginal | Marginal | 2.4000 → 40.73 s |
| 7e-状态去抖 | Oscillating ❌ | **Marginal** | 0.0000 → **39.93 s** |
| 7a-无措施 | Oscillating | Oscillating | 0.4950 → 1.4610 s |

修正后的分级语义才是 `05` 想要的：**7b/7c/7e 是"表观稳定但存在越权写入" → 判为"临界"，不是"稳定"**；只有 7d 才是"稳定"。这一条直接影响 E7 的裁决。

### 1.3 阈值本身的校准结论

E1 基线（`order_changes_per_second = 2.02`、`oscillation_frequency = 1.98`、`average_command_lifetime = 0.50 s`）落在"抖动"档，且 `0.50 s ≈ T_eval` 而非任何任务周期 —— 与 `01` §3.2 的特征签名一致。**四个阈值的方向均正确，本轮不作调整。**

---

## 2. E1–E11 结果

> 所有数值取自 `tests/artifacts/e1-e11-results.txt`。判据对账由程序自身执行（51 项），下表为人工复核后的要点。

### E1 · Last Writer Wins（Baseline）

| 变体 | 终值 | `order_changes_per_second` | `oscillation_frequency` | `average_command_lifetime` | 分级 | 三次重放 |
|---|---|---|---|---|---|---|
| E1a AB/phase0.00/60s | ADVANCE | 2.0167 | 1.9833 | 0.4996 s | Oscillating | hash 全同 |
| E1b AB/phase0.35/60s | ADVANCE | 2.0167 | 1.9833 | 0.4996 s | Oscillating | hash 全同 |
| E1c BA/phase0.00/60s | ADVANCE | 2.0333 | 2.0000 | 0.4955 s | Oscillating | hash 全同 |
| E1d BA/phase0.35/60s | ADVANCE | 2.0167 | 1.9833 | 0.4996 s | Oscillating | hash 全同 |

**判据对账**：① ✔ 有变更 ② ✔ 三次重放全同 ④ ✔ lifetime ≈ 0.50 s ≪ 1.0 s ③ **需重做后才成立**。

**③ 的处理（触发 `05` 的 Failure Criteria）**：4 组终值全部收敛到 ADVANCE，原因是周期结构（A=1.0s / B=0.7s）使 60s 的最后一 tick 无碰撞，最后一写者恒为 A → 终值对注册序不敏感。按 `05` 要求换配置重做：把仿真终止在 **t = 56.0 s**（phase 0.00 时 A 与 B 在 7.0 的整数倍同刻提交，56.0 恰为碰撞点）：

- `E1e AB/tie56.0 → RETREAT`
- `E1f BA/tie56.0 → ADVANCE`

**终值分叉 → 顺序敏感性被证实。**

> **登记为 `05` 的修订项**：`05` §E1 指定的 4 组配置对"终值"这一口径**结构性不敏感**（hash 口径敏感，终值口径不敏感）。判据③ 必须依赖"最后一 tick 恰为两源同刻提交"的对齐变体才能成立。建议 Phase 11 修订 `05` 时补上这条配置约束。

**结论**：LWW 下最终值由执行顺序决定，与任何语义无关；`average_command_lifetime ≈ T_eval` 而非任务周期，与 `01` §3.2 的 Authority 冲突特征签名吻合。

---

### E2 · Priority Arbitration

| 子场景 | 关键数据 | 判据 |
|---|---|---|
| **2a 固定优先级** A(3)>B(2)>Ns(1) | `osc/s = 0.0000`、`chg/s = 0.0333`、`LostArbitration × 252`、三次重放 hash 全同 | ✔ 仲裁是确定的 |
| **2b Utility 带噪** Δu=0.02/0.01 < 0.05，σ=0.03 | `osc/s = 0.7333`（≥0.5 ✔）、oscPairs=44、`chg/s = 1.1833` | ✔ 噪声抖动被证实 |
| **2c 长期任务** A 30s vs B 每 4s 短任务 | `task_starvation = 15`、`max_pending = 58.00 s`（≥25 ✔）；任务终态 `Pending×16, Active×1, Done×1` | ✔ 饥饿被证实 |
| **2d Emergency** t=20s 优先级 99 插队 | `reaction_latency = 0.0000 s`（≤2×dt ✔），紧急写入生效 | ✔ |

**口径澄清**：E2 关闭了 `EnforceAuthorityOnGated`（本实验无 Ownership 语义），实测 **NotOwner 拒绝数 = 0**，`unauthorized_writes` 在本实验不适用（N/A）——`OwnerAtWrite` 恒为默认占位值，按定义统计非默认源的写入没有意义。

**结论（H-E 部分）**：仲裁确实使重放确定（2a/2b/2c/2d 均可复现），但**不能消除权限侧抖动**（2b 抖动 0.73/s），且固定优先级**必然产生饥饿**（2c 中 15 个任务从未被激活、也从未被取消 —— 这正是 `01` §3.3「表面健康但功能从未生效」的 Intent 签名）。

---

### E3 · Ownership（**Reject 位置对照**）

| 变体 | `unauthorized_writes` | `native_overrides` | `order_changes_per_second` | `oscillation_frequency` | 分级 |
|---|---|---|---|---|---|
| **3a** 非 Owner 直写 | 0（**尝试 31 次全部被拒**，原因 `NotOwner ×31`） | 0 | — | — | — |
| **3b** Reject 在**写入口** | **0** | **0** | 0.1333 | 0.0333 | **Stable** |
| **3c** Reject 在**决策器** | **24** | **24** | 1.6667 | 1.5667 | **Oscillating** |
| **3d** Transfer | — | — | — | — | — |

- **3a**：`unauthorized_write_attempts = 31`，每条都带 `RejectReason`。**"能看到违规行为"这一能力本身成立**（这是 `05` 判据① 的真实意图；`05` 写作 `unauthorized_writes ≥ 1`，但 Phase 6 已把该指标拆成 attempts / writes 两个口径，被拒写入不改变值，故落在 attempts 口径上）。
- **3b vs 3c = 0 vs 24**：**Reject 的位置是决定性的**。3c 中 Lab 侧有自查通道、Ns（宿主侧）没有 —— 这正对应 `04` C2 的"持锁者侧自查无效"：**Mod 改不了宿主的决策函数，所以强制点只能落在写入口**。
- **3d**：owner 变更序列 `Native@0.2 → Lab@5.0 → Native@15.0`；`auth_invariant_violation = 0`；**同 tick 多 owner 帧 = 0**；`transfers = 2`；`reaction_latency = 0.0000 s`。转移在单 tick 内原子完成（actions 先于 `FlushTick`，故同一 tick 内所有写入看到同一个 owner 值）。

**Telemetry 样例（3b 被拒写入，`05` §1.4 格式）**：

```text
t=0.10  E0  Domain=Movement  Owner=Native  Level=5  Lease=-  Token=0  Expiry=-
Writer=Lab  Path=Gated  OriginalMethodExecuted=false  OrderChanged=false
Command=HOLD  PrevValue=Unset  NewValue=Unset
Rejected=true  Reason=NotOwner  max_seen_token=0  gen=0/0
Decision: reject write
Reason: Movement authority owned by Native under Full lease
```

---

### E4 · Lease

| 子场景 | 关键数据 | 判据 |
|---|---|---|
| **4a expiry**（TTL=4s，不续约） | 回收时延 **4.00 s** | ✔ ≤ TTL+2×dt = 4.20 s |
| **4b renewal**（每 1.5s 续约） | `renew_ok = 18`、`renew_denied = 0`、`expire = 0`、`unauthW = 0`、Stable | ✔ 全程 Owner 不变 |
| **4c release**（t=10 显式释放） | `release@10.00`、恢复失败 **0**、回收时延 0 | ✔ ≤ 2×dt，恢复 0 失败 |
| **4d lost owner ON**（有 Token/Fencing） | `stale_command_execution = 0`，拒绝 29 次（`StaleLease ×29`） | ✔ 漏洞被堵住 |
| **4d lost owner OFF**（无 Token） | `stale_command_execution = 29` | ✔ 漏洞可复现 |

**4d 的最小 fencing 证据**（无 fencing 时漏过的旧 token 写入）：

```text
t=7.00  E0  Domain=Movement  Owner=Native  Level=5  Lease=L1  Token=1  Expiry=27.00
Writer=Lab  Path=Gated  OriginalMethodExecuted=true  OrderChanged=true
Command=HOLD  PrevValue=ADVANCE  NewValue=HOLD
Rejected=false  Reason=-  max_seen_token=2  gen=1/3
Decision: apply write
```

同一 `WriterId=Lab` 在 t=2.0 持 `Token=1`，被 Ns 于 t=7.0 抢占（`max_seen_token=2`、`gen=3`）后仍携带旧 token 写入并生效。**这正是 `04` §4.3「无 fencing 的锁只是 efficiency lock，不是安全锁」的可执行复现。**

---

### E5 · Preemption 恢复语义（**H-G**）

| 对象类 | Cancel | Resume | Restart |
|---|---|---|---|
| **Class S**（幂等 sink，无内部状态） | 恢复失败 **0** | 恢复失败 **0** | 恢复失败 **0** |
| **Class T**（持有宿主内部定时器） | 恢复失败 **1**（`orphanTimers=5`，`created=3`） | 恢复失败 **0** | 恢复失败 **0** |

- `(S, Cancel)` 无残留 ✔ / `(T, Cancel)` 有残留 ✔ / `(T, Resume)`、`(T, Restart)` 无残留 ✔ —— **H-G 的预测被完整证实**：选择依据是"执行体是否持有宿主内部状态"，不是"哪个更快"。
- **Restart 的成本被量化**：`commands_per_second` 11.600 → 16.650，**增量 +43.5%**（≥20% ✔）。
- Class T 的残留是**孤儿定时器**：Cancel 不做清理，3 个由写入创建的定时器（+2 个基线定时器）全部变成孤儿并持续产生 non-gated 写入，Release 后仍在跑。

> **建模声明**：Restart 的"重放成本"用一条周期 0.1 s 的 replay 源量化（丢弃上下文 = 必须把已发出的命令重放一遍）。**周期取值是建模选择，不是实测值** —— 结论只支持"Restart 的成本显著高于 Resume"这一定性判断，**+43.5% 这个具体数字不可外推到真实宿主**。

---

### E6 · Generation / Fencing（**H-J**）

代次变更点刻意设在 **t = 3.6 s**（落在 Lab 两次发射 3.1 与 4.1 之间），Lab `ActiveUntil = 3.5`（隔离变量：只有变更**前**发出的命令才可能在途）。

| 投递延迟 | 6a 无机制 | 6b 决策器侧校验 | 6c 写入口校验 |
|---|---|---|---|
| 0.1 s | 0 | 0 | 0 |
| 0.5 s | 0 | 0 | 0 |
| **1.0 s** | **1** | **1** | **0** |
| **1.5 s** | **1** | **1** | **0** |
| **2.0 s** | **2** | **2** | **0** |

- **阈值：`delay > 0.5 s`**（= 代次变更时刻 3.6 − 最后一次发射 3.1）。低于该阈值，命令在代次变更之前就已投递完成，不产生在途跨越。
- 6a 合计 4 次 stale（可复现）；**6b 与 6a 完全同数（4 次）→ 决策器侧校验对此类在途命令零效果**；6c 全档位 0。
- 6c 的拦截证据：`Reason = stale lease: token 1 < max_seen 2`，`OriginalMethodExecuted=false`。

**结论（H-J 成立）**：命令一旦离开决策器，决策器侧的检查就再也管不到它。**校验必须在写入口。**

---

### E7 · Oscillation 五手段对照（**本套实验的核心裁决点**）

固定冲突强度：Ns（gated Movement/Facing + non-gated Arrangement）vs Lab（Movement, HOLD），注册序统一 `[Lab, Ns]`。

| 组 | `unauthorized_writes` | `unauthorized_write_attempts` | `oscillation_frequency` | `order_changes_per_second` | `average_command_lifetime` | 分级（60s） | 分级（300s） |
|---|---|---|---|---|---|---|---|
| **7a 无措施**（基线） | 61 | 61 | 2.0000 | 2.0667 | 1.4610 s | Oscillating | Oscillating |
| **7b 迟滞 15%** | **1** | 61 | 0.0000 | 0.0500 | 59.90 s | **Marginal** | Marginal |
| **7c 最短驻留 2s** | **1** | 61 | 0.0000 | 0.0667 | 40.73 s | **Marginal** | Marginal |
| **7d Ownership + Lease** | **0** | 120 | 0.0000 | 0.0667 | 40.23 s | **Stable** | **Stable** |
| **7e 状态去抖** | **1** | 1 | 0.0000 | 0.0667 | 39.93 s | **Marginal** | Marginal |
| **7f 高频（Lab 先）** | 121 | 600 | 4.0000 | 4.0667 | 0.7395 s | Oscillating | Oscillating |
| **7f 高频（Lab 后）** | 121 | 600 | 4.0000 | 4.0667 | 0.7395 s | Oscillating | Oscillating |

**判据对账**：
- ① **仅 7d 满足 `unauthorized_writes == 0` 且分级 = Stable** ✔
- ② 7b / 7c / 7e 的 `oscillation_frequency` 相对 7a **下降 100%**（≥50% ✔），但 `unauthorized_writes = 1 > 0` ✔
- ④ 七组 300s 与 60s 分级**完全一致**（无疲劳效应）✔

**③ 反例演示组（红线验证）—— 结果比 `05` 的预测更强**：

| 指标 | 7a（周期 1.0s） | 7f（周期 0.1s） | 变化 |
|---|---|---|---|
| `unauthorized_write_attempts` | 61 | **600** | ↑ ×9.8 |
| `unauthorized_writes` | 61 | **121** | ↑ ×2.0 |
| `oscillation_frequency` | 2.0000 | **4.0000** | ↑ ×2.0 |
| `order_changes_per_second` | 2.0667 | **4.0667** | ↑ ×2.0 |

> **与 `05` 预测的偏差（已登记）**：`05` §E7 ③ 预测"7f 的 `oscillation_frequency` 下降但 `unauthorized_writes` 不下降"。实测为**两项都不下降，反而全部上升**。这不是实验失败，而是**更不利于该手法的证据**：在本装置下，提高写入频率既没有让系统变稳（连表观指标都没降），还把越权写入量放大了一个数量级。

**7f-stall 补充实验**（高频方在 t=30~31 s 停止写入 1 s）：Ns 的生效变更数 `[29,30) = 2` vs `[30,31) = 2`，**完全不变**。说明 Ns 的写入自始至终都在生效，高频方从未真正阻止它 —— 高频只是让 **tick 末的采样值**偏向自己。一旦采样点变化或高频方掉一帧，Ns 的值立刻可见。**这是竞速，不是 Authority。**

---

### E8 · Domain Ownership 三级递进（**条件式预测**）

| 级别 | Movement | Facing | Arrangement | 全局分级 | 恢复失败 |
|---|---|---|---|---|---|
| **8a** Movement=Lab | 接管：`unauthW=0` | 保留：`unauthW=0`, 无 Lab 写入 | 保留：`unauthW=0`, `nonGated=86` | **Stable** | 0 |
| **8b** Movement+Facing=Lab | 接管：`unauthW=0` | 接管：`unauthW=0` | 保留：`unauthW=0`, `nonGated=86` | **Marginal** | 0 |
| **8c** Full=Lab | 接管：`unauthW=0` | 接管：`unauthW=0` | **接管失败**：`unauthW=21`, `nativeOv=21`, `nonGated=86` | **Oscillating** | 0 |

**判据对账**：
- ① 8a/8b 被接管的域 `unauthorized_writes == 0` ✔
- ② 8a 保留给 Ns 的域 `lab_overrides == 0`，且**未接管域的 Lab 写入尝试 = 0** ✔
- ③ **Arrangement 域在 8a 与 8c 均出现 `nonGated = 86` 次自维护写入** ✔ → **复现 `02` 门控矩阵，Ns 模型保真**
- ④ 跨域副作用（Lab 已接管域内仍发生的越权生效写入）**0 / 0 / 21，单调不减** ✔

**关键发现**：**8c 中 Lab "接管" Arrangement 实际是失败的** —— Ns 的 non-gated 自维护路径不受门控，仍产生 21 次越权生效覆盖。整实体授权（Full）不但没有多拿到控制权，还把全局分级从 Stable 拖到 Oscillating。

> **结论形式（严格遵守 `05` §E8 强约束）**：以上全部是「**若宿主行为如 Ns 模型所述，则……**」的条件式预测，**不得**用于回答 `02` §8.1 与 HANDOFF §10.6 的宿主实测问题（`SetControlledByAI(false)` 后 Facing/Arrangement 是否仍有残留覆盖）。该问题留给 Phase 10 的 Harmony Telemetry。

**偏差登记**：`05` 判据④ 期望 `release_recovery_failures` 随接管面扩大而单调不减（"接管越多恢复越难"）。实测三级**全为 0** —— Ns 的 non-gated 自维护路径会自我愈合，所以恢复总是成功。**判据④ 只是平凡成立**（0 ≤ 0 ≤ 0），不构成证据。本实验真正的判别信号是跨域副作用 0/0/21 与全局分级 Stable → Marginal → Oscillating。

---

### E9 · Replace vs Append Writer（承接 `03` 的 H-B 修正）

| 变体 | Writer 集合 | `unauthorized_writes` | `order_changes_per_second` | `oscillation_frequency` | 分级 |
|---|---|---|---|---|---|
| **9a Replace** | `[Native]`（**净增 0**） | **0** | 0.0500 | 0.0000 | **Stable** |
| **9b Append** | `[Native, Lab]`（**净增 1**） | **120** | 4.0500 | 3.9833 | **Oscillating** |

**结论**：`03` 修正后的 H-B（"替换 Writer 而非新增 Writer"）**获得实验支撑**。并列追加一个写入者，即便逻辑正确、错峰、效用更高，也会立刻引入第二个写入源并把分级从 Stable 拖到 Oscillating。这与 `03` 从 RBM 归纳的"仲裁权不下沉 + 替换而非并列"两条原则一致 —— 但**此处是仿真内的独立证据，不再只是 RBM 的案例**。

---

### E10 · Value Transform 层能力边界

| 组 | 结果 | 判据 |
|---|---|---|
| **10a 单写者** | 终值 `MOVE_A`（`ADVANCE` 被变换） | ✔ 功能成立 |
| **10b 双写者冲突** | transform ON `unauthW = 61` vs OFF `unauthW = 61`（**完全相同**），`osc/s` 同为 2.0000 | ✔ **未解决权限问题** |
| **10c 与 Ownership 组合** | `unauthW = 0`、`nativeOv = 0`、Stable（与无 transform 时一致） | ✔ **两者正交、可共存** |

**结论**：`04` §6.1 的立场**成立**。Transformation 只改值、从不抑制，它不改变"谁在写"，因此**不解决任何一类 Authority 冲突**。建议按 `04` 的意见，在 Phase 11 将其登记为「**与 Authority 正交的第四种机制**」，而非 Phase 1 的第六类冲突。

---

### E11 · Blackboard vs Command Bus（**H-H**）

同一组写入序列（Lab 每 1.0s 写 HOLD，Ns 每 0.5s 写 ADVANCE）注入两种底层语义：

| 语义 | 注入写入 | 值变更 | 可归因 | **归因完整率** |
|---|---|---|---|---|
| **11a Blackboard** | 60 | 40 | 20 | **50.0%** |
| **11b Command Bus** | 62 | 42 | 42 | **100.0%** |

- 11a 中 **20 条变更无法归因**（全部为 Ns 发起，trace 中查不到 writer 与 reason）。观测方只有事后快照，只能看到"值变了"，看不到"谁改的、为什么"。
- 11b 中每一次变更都携带 `WriterId` + `Reason` + `LeaseId/Token`。

**结论（H-H 成立）**：`04` C7 的洞察得到验证 —— **宿主把 Formation 当 Blackboard（多源共享可变状态、无 owner、无单点入口），而 Mod 需要的是 Command Bus 语义（单一权威写入口）。归因能力的差异是可量化的（50% vs 100%）。** 这为"第 5 类冲突的深层成因可能是架构风格错配"提供了第一条实证支持。

---

## 3. 假设裁决表

| 假设 | 内容 | 裁决 | 证据 |
|---|---|---|---|
| **H-F** | Lease + 资源侧 Reject 能把 `unauthorized_writes` 降到 0；迟滞/限流不能 | ✅ **成立** | E3 3b(0) vs 3c(24)；E7 7d(0) vs 7b/7c/7e(1)；E4 4d(0 vs 29) |
| **H-G** | 恢复语义取决于执行体是否持有宿主内部状态，而非性能 | ✅ **成立** | E5 (S,Cancel)=0 失败 vs (T,Cancel)=1 失败（孤儿定时器 5）|
| **H-J** | Generation 校验必须落在写入口 | ✅ **成立** | E6 6c 全档位 0 vs 6b 与 6a 同数（4）|
| **H-E**（部分） | 仲裁可使重放确定，但不能消除权限侧抖动；固定优先级产生饥饿 | ✅ **成立** | E2 2b osc=0.73/s；2c starvation=15 / max_pending=58s；2a/2d 重放确定 |
| **H-H** | Blackboard ↔ Command Bus 风格错配是第 5 类冲突的深层成因 | ✅ **成立（机制侧）** | E11 归因完整率 50% vs 100% |
| **H-C**（弱证据） | 分域授权优于整实体授权 | 🟡 **方向支持（条件式）** | E8：整实体授权在 Arrangement 域**失效**（21 次越权覆盖），且全局分级劣化到 Oscillating |
| `03` H-B 修正 | 「替换 Writer」而非「并列新增 Writer」 | ✅ **获得仿真支撑** | E9 9a(0, Stable) vs 9b(120, Oscillating) |
| `04` §6.1 立场 | Transformation 是与 Authority 正交的机制，不是第六类冲突 | ✅ **成立** | E10 10b `unauthW` 61 vs 61（不变），10c 与 Ownership 共存 |
| **H-I** | Level 4 稳定性显著低于 2 与 5 | ⬜ **未验证** | 本 Phase 只用 Level 5；留 Phase 8 |
| **H-A / H-B(host)** | — | ⬜ **不在本 Phase** | 分别属 Phase 12 / Phase 10 |

**D5 相关**：E1–E11 全部跑完后，**没有任何一组实验需要 `Proposal` / `Executor` 抽象**才能达到其 Success Criteria。可记录的初步结论是「**在本装置与这批实验的覆盖范围内，Proposal 不是必需的**」——但这是**弱结论**（只说明"没被逼出来"，不证明"永远不需要"），须由 Phase 11 定稿时裁定。

---

## 4. 判别力矩阵回填（`05` §6 的实际结果）

| 要区分的两者 | 实验 | 实测结果 | 判别力 |
|---|---|---|---|
| LWW vs Arbitration | E1 vs E2 | LWW 下终值依赖执行顺序（E1e/f 分叉）；仲裁下重放确定 | ✔ 有 |
| **Arbitration vs Ownership** | E7 7b/7c/7e vs 7d | `unauthorized_writes` 1 vs 0，分级 Marginal vs Stable | ✔ **强** |
| Reject 在写入口 vs 在决策器 | E3 3b vs 3c | 0 vs 24 | ✔ **强** |
| Lease 单独 vs Lease+Token | E4 4d 两变体 | `stale` 29 vs 0 | ✔ **强** |
| Fencing 在写入口 vs 在决策器 | E6 6b vs 6c | 4 vs 0（阈值 0.5s 明确） | ✔ **强** |
| Resume vs Restart vs Cancel | E5 六组矩阵 | (T,Cancel)=1 失败，其余 0；Restart 成本 +43.5% | ✔ 有（成本数字为建模值）|
| 迟滞 / MinLife / State 三者之间 | E7 7b/7c/7e | 三者 osc 均降 100%，`unauthW` 均为 1 —— **彼此不可区分** | ⚠️ **三者在本装置下等效果**，需别的维度才能区分 |
| 整实体授权 vs 分域授权 | E8 三级 | 跨域副作用 0/0/21 + 全局分级 S→M→O | ✔ 有（但恢复失败数判据平凡）|
| 替换 Writer vs 并列 Writer | E9 | 0/Stable vs 120/Oscillating | ✔ **强** |
| Transform 是机制 vs 是冲突类 | E10 | 61 vs 61 | ✔ 有 |
| Blackboard vs Command Bus | E11 | 50% vs 100% | ✔ 有 |

> **Phase 8 提醒**：A/B/C 对比**必须引用上表的实验编号**作证据，不允许"理论上应当更好"。特别注意第 7 行 —— **迟滞、最短驻留、状态去抖在本装置下效果不可区分**，Phase 8 不得用"我们加了迟滞"作为架构优势的论据。

---

## 5. 给 Phase 8 可直接引用的结论

1. **唯一能把 `unauthorized_writes` 降到 0 的机制是 Ownership + Lease + 写入口 Reject**（E7 7d）。这是 Architecture A/B/C 对比的**硬门限**。
2. **Reject 与 Fencing 必须落在写入口**（E3 / E6），Mod 侧自查无效 —— 因为 Mod 改不了宿主的决策函数。这直接约束 A/B/C 的 Harmony patch 位置。
3. **"提高写入频率"不是 Authority**（E7 7f），且在本装置下连表观指标都不改善。**A/B/C 中任何依赖高频重写的方案应直接判负。**
4. **并列新增 Writer 会立刻劣化一个数量级**（E9），支持"集成原生链 / 替换产出"而非"旁路加一个写入者"的方向。
5. **Transformation 与 Authority 正交**（E10），可作为任一架构的附加层，但不参与胜负判定。
6. **归因能力是可量化的架构属性**（E11 50% vs 100%），应进入 A/B/C 的 Debuggability / Decision explainability 维度。
7. **hold（`intent_hold_share`）在本轮还不是判据，Phase 8/11 才成为判据**（Phase 11 正式补记）。本 Phase 的 14 项指标里没有 `intent_hold_share`，E1–E11 因此**无法区分"从未抢到控制权"与"完全控制"**——两者在 `unauthorized_writes == 0` + Stable 上同形。这正是 E12 必须补 Level 谱系对照（L2 档 `unauthW == 0` 且 Stable，但 hold = 0.0%）的原因。**`intent_hold_share` 与硬门限 G2（`intent_hold_share ≥ 95%`）的正式定义见 `docs/05-experiment-plan.md` §1.5（Phase 11 补记）**，E12 实测见 `docs/07a-architecture-abc-comparison.md` §2.1，宿主侧实测见 `docs/07-final-architecture.md` 与 `_audit/2026-10-07-phase11-evidence-review.md`。
8. **`lab_delivery_rate` 不得用作控制判据**（Phase 11 正式补记）。该变量的口径缺陷见 `docs/07a-architecture-abc-comparison.md` §8 遗留 1：L5 档仅 4.8%，原因是**写入同值不计变更**，分母统计的并非"交付动作"本身。因此它在 A/B/C 对比中**不参与胜负判定，也不得与 G1/G2 同表当作控制落地证据**；本轮 E1–E11 亦未使用该口径。

---

## 6. 遗留与不确定性（本轮新增/更新）

| # | 遗留项 | 影响 | 建议 |
|---|---|---|---|
| 1 | **Ns 是模型不是实测**（`05` 遗留 1，未消除） | E2/E3/E5/E7/E8 全部结论带"若宿主行为如 Ns"的前提 | Phase 10 必须用真实 Telemetry 复核 Ns 参数，**尤其 non-gated 路径**（E8 的关键预测完全依赖它）|
| 2 | **`05` §E1 的 4 组配置对"终值"口径不敏感** | 判据③ 必须靠对齐变体才成立 | 建议 Phase 11 修订 `05` 时补上"最后一 tick 须为两源同刻提交"的配置约束 |
| 3 | **E7 7f 实测与 `05` 预测方向不一致**（预测"osc 下降"，实测"osc 与 unauthW 同升"） | 结论更强，但说明 `05` 的预测不够准确 | 已登记；`05` 无需为此返工，但 Phase 11 应记录该偏差 |
| 4 | **E8 判据④ 平凡成立**（三级恢复失败均 0） | "接管越多恢复越难"未获支持 | 改用跨域副作用 + 全局分级作为判别信号；若 Phase 10 实测显示恢复确实会失败，再回修 Ns 的清理语义 |
| 5 | **E5 的 Restart 成本 +43.5% 是建模值** | 不可外推到真实宿主 | 只保留定性结论"Restart 成本显著高于 Resume" |
| 6 | **迟滞 / MinLife / State 三者在 E7 下不可区分** | 无法据此推荐其中任何一种 | 若 Phase 8 需要区分，须设计新的判别维度（如对突变信号的响应时延）|
| 7 | **`average_command_lifetime` 的删失处理是本轮新引入的** | 所有含"只发生 1 次变更"的场景的分级会变 | Phase 8/10 沿用同一口径；若 Phase 10 实测数据出现同现象，须确认口径一致 |
| 8 | **未验证 H-I**（Level 4 的不稳定性） | Phase 8 的授权等级谱系缺中间档数据 | Phase 8 需补 Level 2 / 4 / 5 的对照 |

---

## 7. 红线自检（`05` §7 对照）

| 红线 | 本轮落实 |
|---|---|
| 用真实战术做基础实验 | ✔ 全程仅 `E0` / `Source A/B` / `NativeSource` / `MOVE_A·HOLD·ADVANCE·RETREAT`，零战术语义 |
| 靠"每 Tick 重复 SetOrder"压过 Vanilla | ✔ E7 7f 明确设计为**反例演示组**，实测三项指标全部恶化，并加 7f-stall 证明其未真正阻止 Ns |
| 靠"谁写得更快"实现 Authority | ✔ 同上；且 7f-stall 直接给出反证 |
| 无限叠加 Priority | ✔ E2 2a/2b 展示了规则失效（252 次 LostArbitration、osc 0.73/s、starvation 15）|
| 永久关闭 Vanilla AI 后宣称已解决 | ✔ E3 3d / E4 4c / E5 / E8 全部测了 `release_recovery_failures` 与恢复窗口 |
| 没有 Telemetry 就推测 Writer | ✔ 每次写入尝试（含被拒）均记录；3b/4d/6c 各给出一条 `05` §1.4 格式样例 |
| 预设 Proposal/Arbiter/Executor 正确 | ✔ 全程未实现 Proposal/Executor；§3 登记了"Proposal 不必要"的弱结论 |
| 因为 RBM 用了某方案就当最佳实践 | ✔ E9 把"替换而非并列"做成可证伪实验，独立给出仿真证据 |
| 为兼容 CombatBrain 旧结构牺牲实验架构 | ✔ 零 `CombatBrain.*` 引用（Core/Simulation/Telemetry 层），零 `TaleWorlds.*` 引用 |
