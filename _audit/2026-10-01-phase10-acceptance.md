# Tactical Command Lab — Phase 10 验收报告

> 验收时间：2026-10-01 23:50 GMT+8
> 验收范围：`E:\MBmod\TacticalCommandLab`（Phase 1–10）
> 验收方式：**复跑 + 源码/产物交叉取证**，不采信文档自述
> 结论：**Phase 1–9 交付真实；Phase 10 不能进入 H1/H2/H3，存在一个未登记的 P0 缺陷。**

---

## 0. 结论先行

| 判定 | 对象 | 依据 |
|---|---|---|
| ✅ **通过** | Phase 1–8 全部交付物（仿真侧） | 本地复跑：D0 通过、E1–E11 **51/51**、E12 **10/10**，`replay_hash` = `d9853e28…` 与文档一致 |
| ✅ **通过** | Phase 9 宿主探针工程 | `dotnet build` → **0 error / 0 warning**；红线自检 5 项全清 |
| ⬜ **积压未做** | Phase 11（`07-final-architecture.md`）、Phase 12（`08-integration-report.md`） | 文件不存在，与 HANDOFF 登记一致 |
| 🚫 **不通过** | Phase 10 实测数据 | 已命中的 P0 缺陷导致除 `[census]`/`[patches]` 外全部指标不可采信 |

**阻塞项：`ProbeRuntime.EntityIdOf()` 生成的 EntityId 不唯一，多个真实 Formation 被归并成同一个受控实体。**

---

## 1. 为这么做验收

文档说"已完成"不等于"状态正确"。本轮全部结论来自：

1. 实际编译 + 运行离线测试套件
2. 对 `tests/artifacts/host/` 下实测产物做 7 MB 级数据复算
3. 回读源码确认判据与判据实现的一致性
4. 重跑红线自检 grep

---

## 2. 已验证通过的部分（Level A）

### 2.1 离线仿真侧 —— 复跑实证

```
=== CommandLab Phase 6 · D0 装置门禁 ===
[PASS] replay determinism 3/3 :: attempts=147, sim=60.0s
D0-S1 replay_hash = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199
=== 判据汇总：51/51 通过，失败 0 项 ===   （E1–E11）
=== 判据汇总：10/10 通过，失败 0 项 ===   （E12）
```

`replay_hash` 与 HANDOFF §Phase 8 记录的 `d9853e28…` 逐字一致 ⇒ **Core / Simulation / Telemetry / tests 四个工程自会话 8 以来确实零语义改动**，文档的回归声明属实。

### 2.2 Phase 9 工程 —— 实机构建

```
dotnet build src/CommandLab.BannerlordProbe/CommandLab.BannerlordProbe.csproj
→ 已成功生成。0 个警告，0 个错误（2.09s）
```

### 2.3 红线自检 —— 5 项重跑

| 红线 | 判据 | 实测 |
|---|---|---|
| ① Core/Sim/Telemetry/tests 零 `TaleWorlds` 引用 | ==0 | **0** ✅ |
| ② 全仓零 `CombatBrain` 引用 | ==0 | **0** ✅ |
| ③ 零真实战术词（cavalry/morale/brace/skirmish/shieldwall/flank/volley） | ==0 | **0** ✅ |
| ④ 零 `Thread.Sleep` 作为机制 | ==0 | 仅命中 `Scenario.cs:2` 的**契约注释文本**"禁止 Thread.Sleep"，非调用 ⇒ 假阳性，通过 ✅ |
| ⑤ 真实时钟 | 仅用于文件名 | 4 处，**全部在 `ProbeRuntime.cs`**（dump 时间戳 ×2、boot-marker、crash 报告），无一参与计算 ✅ ⚠️ 但 HANDOFF 记的是"1 处"，实际为 4 处（会话 10/13 新增三个哨兵），属文档滞后 |

### 2.4 Phase 10 的崩溃修复 —— **确认生效**

2026-09-22 会话 13 定位的"延迟 patch"修复，在下一轮实测中被验证：

```
tests/artifacts/host/valid-20260923-2154-census-baseline/patch-marker.txt
patchedCount       = 7
patchFailure       = (none)
missionCurrentNull = no        ← 修复的自证字段
```

7 个写入口全部 patch 成功、游戏**不再崩溃**、跑完 30 秒完整战斗。**这是实质性进展，应当肯定。**

---

## 3. 🚫 阻塞缺陷 D1：EntityId 不唯一（P0）

### 3.1 现象

2026-09-23 21:54 那组 `census-baseline`（唯一一组跑通的实测）的 `[verdict]`：

```
P0     harmony patched methods > 0                actual=7              PASS
P0b    write-port traffic > 0                     actual=2541           PASS
X1..X4 记录值                                                            PASS
X5     unobserved_writes (采样到但写入口没记录)             actual=131470         FAIL
```

`131470` 会被自然读成"宿主绕过了写入口产生了 13 万次写入"这一**架构结论**，从而污染 Phase 11 的最终裁决。

### 3.2 证据链（由实测数据倒推）

**证据 1 —— 同一 tick 内同一实体同一域重复计数**

对 7 MB telemetry 做分组统计：

```
总 SHADOW 行数            : 131,579
同一 (tick, 实体, 域) 内出现 ≥2 次的行数 : 131,559   ← 99.98%
每 tick 只有一次变化的行数 :      20
```

**证据 2 —— 值集合只有两个，且方向完全对称**

```
N-1|Arrangement : 7039 次 Obs0→Obs1，7038 次 Obs1→Obs0
N8 |Movement    : 3663 次 HOLD→Obs0，3663 次 Obs0→HOLD
N0 |Facing      : 3375 次 Obs0→Obs1，3375 次 Obs1→Obs0
```

真实 Order 写入不可能在两个值之间每帧严格乒乓且次数对称。这是**交替 Ping-Pong**，不是业务变化。

**证据 3 —— 同一 EntityId 下挂着多个 Formation 对象**

`[projection]` 段按 binding 枚举，统计每个 EntityId 的重复次数：

| 实体 | binding 数（mission-start 时） | binding 数（30s 后） |
|---|---|---|
| P0–P9（玩家侧） | 各 **1** | 各 **1** |
| N0–N9（非玩家侧） | 各 **4** | 各 **5** |
| N-1 | **4** | **6** |

**P 侧全部唯一、N 侧系统性重复 ⇒ 暴露的就是命名方案的缺陷。**

**证据 4 —— 同一 EntityId 下混入了两个不同阵营的 Formation**

```
E=N0 的 HOST 行：
  308 行  team=nonplayer
  294 行  team=player
```

`HostContext.TeamOf()` 与 `ProbeRuntime.EntityIdOf()` 用的是**同一个判据**（`Team.IsPlayerTeam`）。同一 Formation 不可能同时是两者 ⇒ `"N0"` 这个名字下确实压着来自不同阵营的多个真实 Formation。

### 3.3 根因

```csharp
// ProbeRuntime.cs:224-230
public string EntityIdOf(Formation formation)
{
    string side;
    try { side = formation.Team is not null && formation.Team.IsPlayerTeam ? "P" : "N"; }
    catch { side = "N"; }
    return side + formation.Index.ToString(CultureInfo.InvariantCulture);
}
```

`side` 只有两个取值，而 `formation.Index`（FormationIndex）在**每个 Team 内部各自从 0 开始计数**。
自定义战斗里除了攻守双方本体，还有各自的**盟友 Team**（`Mission.Teams` 数量 > 2）⇒ 每个非玩家 Team 的 Index 0 全部塌缩成同一个 `"N0"`。

### 3.4 影响面（比 X5 严重得多）

X5 只是最先报警的那个。**真正致命的是 Core 侧按 EntityId 寻址**：

```csharp
// WritePort.cs:139
public void RegisterEntity(ControlledEntity e) => _entities[e.Id] = e;   // 同 id 静默覆盖
// WritePort.cs:209-211
var entity = _entities[req.EntityId];     // 所有同 id Formation 共享同一份状态
var state  = entity[req.Domain];
```

⇒ 后果清单：

| 受影响对象 | 后果 |
|---|---|
| `order_changes_per_second` | 多个 Formation 交替写同一份状态 ⇒ 每次交替都判"变化"，**被显著放大** |
| `oscillation_frequency` / `stability_grade` | 同上；**零干预原生基线被判成 `Oscillating`（1.1664/s）**，会被误读为"宿主自身在抖" |
| `[coverage]` 与 X5 | 完全失真（已实证 99.98% 为假） |
| `intent_hold_share`（未来 H2 的 G2） | `FindBinding(entityId)` 返回**第一个**匹配 binding ⇒ 脚本控制的与采样的是不是同一个 Formation，**不可知** |
| `Authority / Lease` | `GetOrCreate(entityId, domain)` 按 id ⇒ Acquire 一个 N0 等于同时管制全部 5 个 N0，**实验对象不可辨识** |

**判据：修复前跑 H1/H2/H2b/H3/REL 得到的 G1/G2/G3 全部不可采信。** 这比 Phase 10 延期严重，因为它会把假数据送进 Phase 11 的架构裁决。

### 3.5 修复方案（建议，未实施）

1. **EntityId 必须含 Team 全局标识**。不要继续用 `IsPlayerTeam` 二分 —— 它既非唯一也非稳定（见证据 4，`team=player` 与 `team=nonplayer` 混在 `N0` 下）。建议 `Mission.Current.Teams` 中的稳定序号 + `formation.Index`，或直接采用 `Team.TeamIndex` 这类全局编号。
2. **`SampleShadow` 的聚合 key 不得比 EntityId 更粗**。当前用 `entityId + "|" + domain` 聚合，天然受 id 冲突影响；应绑定到 binding 实例或修好的全局唯一 id。
3. **加门禁 `P0c`**：`_bindings.Count == 唯一 EntityId 数`，冲突时直接 FAIL 并把重复清单写进 `[verdict]`。
   理由：本轮要靠人工分析 7 MB telemetry 才发现这个问题，成本不可接受，且下一次同样的假数据会再次静默通过。
4. 修完务必用 `sigscan` 之外的手段复核（该工具查的是 Harmony 匹配，与本问题无关）。

### 3.6 验收条件（修复后）

- [ ] `[projection]` 段每个 EntityId 出现次数**恒为 1**
- [ ] `[verdict]` 新增 `P0c` 且 PASS
- [ ] 重跑 `census-baseline`：**`X5` 必须 PASS**（此时 `[coverage]` 里 Arrangement 域若有残留 unobserved，才可能是真实的架构事实 —— 这与 `02` §8.2 的静态预测方向一致）
- [ ] `[coverage]` 中 Movement / Facing 域 `unobserved` 应为 0
- [ ] 回归不变：D0 通过、E1–E11 51/51、E12 10/10（`src/CommandLab.Core` 等未改动时）

---

## 4. 🚫 缺陷 D2：顺序门禁漏掉了 X5

`docs/10` §5 判据表里 X5 明确定义为 `unobserved_writes == 0`，但 §7.4 的顺序门禁写的是：

| 序 | 脚本 | 门禁 |
|---|---|---|
| 1 | `census-baseline` | **P0 或 P0b FAIL ⇒ 停**，后面全部作废 |
| 2 | `h1-external-write` | H1-a FAIL ⇒ 先查 P0b |
| … | | |

**第 1 组的门禁只写了 P0 / P0b，没有 X5。**

⇒ 按现行 SOP，`X5 FAIL`（actual=131470）**不会阻断流程**，团队会带着这份被污染的数据继续跑 H1–REL，并最终把它写进 Phase 11。

这与 Phase 10 自己总结的教训同源 —— 会话 13 明确写过："若只有 `P0b` 没有 `P0`，零流量看起来就是一条硬结论"。**现在轮到 X5 了。**

**建议**：§7.4 第 1 组门禁改为 `P0 ∧ P0b ∧ X5`（以及新增的 `P0c`），全部 PASS 才准入 H1。

---

## 5. 治理问题

| # | 问题 | 实测 | 建议 |
|---|---|---|---|
| G1 | **无版本控制** | `git rev-parse` → `not a git repository`。全仓仅靠手工归档，且 `tests/artifacts/*.txt` 是**每次运行覆盖写** | 立刻 `git init`。实测产物 + 8 个 Phase 交付物全裸奔 |
| G2 | **9-23 的实测未回写任何交付物** | `docs/10` 的 §11 只写到 09-22 的 P0 FAIL，全文档无 `2026-09-23`；`.workbuddy/memory` 最新为 09-22；HANDOFF 停留在 09-22 13:15 | HANDOFF 第 2 节"当前进度"表里 Phase 10 仍写"⬜ 待重跑"，与事实（已跑通）不符。**这是交接断点**：下一个会话若只读文档，会重跑一遍 census-baseline |
| G3 | **工作区有 3 个 0 字节垃圾文件** | `Phase`、`取证对象：E:MBmodIDEARBM`` `、`状态：**已完成（只读取证` —— 大小均 0 字节，创建于 09-20 00:01，是 Markdown 正文被误写进了文件名 | 建议删除；但因含换行符需脚本处理，本次**未擅自改动**，待确认 |
| G4 | **08-23 后至 10-01 无任何产出** | 最近非产物文件为 09-22 13:15 | 见 §6 |

补充（版本核对）：memory 记录会话 13 部署版本为 MD5 `e1d3c8a0…`（51200 字节）；`Modules\` 当前实际为 **51712 字节、时间戳 09-23 01:18**，与 `deploy\` 下 09-22 13:14 的产物同尺寸（该次部署晚于 memory 最后一次记录）。⇒ 9-23 实跑的确实是带延迟 patch 修复的最新版本，**版本侧无偏差**。

---

## 6. 数据复用判定

`valid-20260923-2154-census-baseline` 这组产物不是全废：

| 段 | 可否复用 | 理由 |
|---|---|---|
| `[patches]`（7 个方法、failure=none） | ✅ | 与 EntityId 无关 |
| `[census]`（SetArrangementOrder 193 / SetControlledByAI 191 / SetFacingOrder 221 / SetFiringOrder 101 / SetFormOrder 988 / SetMovementOrder 756 / SetRidingOrder 91） | ✅ | `Bump(hook)` 纯计数，与 EntityId 无关。**这是首个可信的宿主写入口流量基线** |
| `[projection]` | ✅ **作为缺陷证据** | 正是它暴露了 binding 重复 |
| `[coverage]` / X5 | 🚫 | 99.98% 为乒乓假变化 |
| `[metrics]` 中 change/oscillation/grade | 🚫 | 多 Formation 共享同一份受控状态，聚合失真 |
| `[hold]` | ⬜ 空 | baseline 零干预，本应为空 |

---

## 7. 下一步建议（按优先级）

1. **修 D1（EntityId 唯一性）+ 加 P0c 门禁** —— 在跑 H1 之前，否则后续 8 组数据全废
2. **补 D2：把 X5（和 P0c）写进 §7.4 第 1 组门禁**
3. **回写 HANDOFF 与 memory**：登记 9-23 实测结果、D1/D2 两个缺陷 —— 否则下一个会话会重复这轮分析
4. **`git init`**（先做，成本 10 秒，收益是防止 Phase 10/11 的成果再次丢失）
5. 修完后按现有 SOP 重跑 `census-baseline` 验证，再进 H1

**不建议**：在 D1 修复前推进 Phase 11。`07-final-architecture.md` 的架构结论需要引用 Phase 10 的实测证据，而现在没有一条可以引用。
