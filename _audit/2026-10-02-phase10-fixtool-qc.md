# Phase 10 修装置 · Step 1–4 质检报告（t3）

- 日期：2026-10-02
- 范围：Step 1（EntityId 唯一性）、Step 2（P0c 门禁 + 别名歧义拒绝）、Step 3（docs/10 门禁补齐）
- 执行：Team Captain 亲自执行（qc 成员 attempt 1/2 三次唤醒后仍未落盘本报告，`t3` 已由 `reassign_task` 转交 captain，attempt 3）
- 结论：**Step 1–3 通过**；Step 4 绿门通过；**存在 1 项运行期验证债 + 3 项登记风险，均需 Step 5 游戏内实测解锁**

---

## 1. 结论

| 项 | 结论 |
|---|---|
| Step 1 EntityId 唯一性 | PASS（代码级） |
| Step 2 P0c 门禁 / 别名歧义 | PASS（代码级） |
| Step 3 docs/10 §5 + §7.4 | PASS（字面核对） |
| Step 4 build 0w0e + 三条判据字面量 | PASS（captain 亲自跑） |
| 红线（四工程零 diff / TaleWorlds 不外溢 / 不引用 CombatBrain） | PASS |
| **运行期验证状态** | **未完成 —— 见第 5 节验证债** |

---

## 2. 构建与回归（Step 4，captain 亲自执行于 `E:\MBmod\TacticalCommandLab`）

```
dotnet build src/CommandLab.BannerlordProbe/CommandLab.BannerlordProbe.csproj -v q --nologo
→ 已成功生成。 0 个警告 0 个错误（00:00:00.66）

cd tests\CommandLab.Tests ; dotnet run --nologo
→ === 汇总：15/15 通过，失败 0 项 ===
→ === 判据汇总：51/51 通过，失败 0 项 ===
→ === 判据汇总：10/10 通过，失败 0 项 ===
→ D0-S1 replay_hash      = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199
```

三条验收字面量与 Step 0 基线（`commit 555a9af`）**逐字一致**，无回归。

---

## 3. 红线自检

```
git status --porcelain
 M docs/10-harmony-runtime-experiments.md
 M src/CommandLab.BannerlordProbe/ProbeRuntime.cs
 M src/CommandLab.BannerlordProbe/ScriptRunner.cs
 → 3 files changed, 111 insertions(+), 15 deletions(-)

git diff --stat HEAD -- src/CommandLab.Core src/CommandLab.Simulation src/CommandLab.Telemetry tests/CommandLab.Tests deploy/CommandLabProbe/scripts
 → （空）

grep 'using TaleWorlds|CombatBrain' src
 → 5 命中，全部位于 CommandLab.BannerlordProbe 自身：
   ProbeRuntime.cs:13 / ProbeSubModule.cs:10 / OrderWritePatches.cs:21 / NeutralOrders.cs:8 / HostContext.cs:8
 → 无 CombatBrain.* 命中
```

派单 §6 三条红线自检**全部通过**。

---

## 4. Step 1–3 实现核对（对照派单逐项）

### Step 1 · EntityId 全局唯一性
- `ProbeRuntime.cs:233` `public string EntityIdOf(Formation formation, int teamSeq = -1)` → `"T{teamSeq}F{formation.Index}"`
- 格式**不含 `|`** ⇒ 不破坏 `CoverageRows()`（`:712` `key.Split('|')`）与 `TotalUnobserved()`（`:933`）的 key 解析口径 ✅
- `ProbeRuntime.cs:250` `private static int TeamSeqOf(Formation)`：按 **引用相等**遍历 `Mission.Current.Teams`；`team is null` / `mission is null` / `teams is null` / 抛异常 **一律 -1**。刻意避开 `Formation.GetHashCode()` 在 Team==null 时的 NRE（docs/10 §12.8）✅
- `ProbeRuntime.cs:22` `FormationBinding(Formation, string entityId, string alias, ControlledEntity)`；`:35` 新增只读 `Alias` ✅
- `ProbeRuntime.cs:241` `AliasOf(Formation)` 保留原 `P/N + Index` 逻辑 ⇒ **8 个实验脚本里 23 处 `N0` 等短名无需改动**（派单 §2 影响面表）✅
- `ProbeRuntime.cs:273` `Bind(Formation, int teamSeq = -1)`：保留 `_bindings.TryGetValue` 短路（`:275`）⇒ 已存在的 binding 不因调用点不同而重算 EntityId ✅
- `ProbeRuntime.cs:588` `BindAll()`：`ti` 计数器在 `team is null` / `formations is null` 分支也自增（`:598`/`:600`）⇒ 序号恒等于 `mission.Teams` 下标 ✅
- `SampleShadow`（`:665-691`）与 `SampleHolds`（`:737-751`）：两者都以 `binding.EntityId` 为 key（`Key()` 见 `:224`，字典均 `StringComparer.Ordinal`）⇒ EntityId 改唯一后自动跟随，**无需改动**，dev 的判断成立 ✅
- `FindBinding`（`:753`）已是 Ordinal 精确匹配；`SampleHolds:745` 用它按 EntityId 定位 ✅

### Step 2 · P0c 门禁 + 别名歧义
- `ProbeRuntime.cs:862` P0c 行用 `Row()`（格式 `"{0,-6} {1,-42} actual={2,-14} {3}"`），`actual = 唯一数/绑定总数`，`pass = 两者相等`
- **位置正确**：插在 P0b（`:846`）之后、`prefix = tag.Split('-')[0]`（`:875`）**之前** ⇒ census-baseline（prefix=`census`，不落任何 if 分支）**必然输出 P0c 行** ✅
- FAIL 时下一行附重复清单（`:872`），格式 `P0c    duplicated entity_id: T2F0 x3, …(…(+N))`，最多 12 项 ✅
- `ScriptRunner.cs:110` `Resolve(string name)` 四分支：全名命中 → Alias 唯一 → Alias 歧义（NOTE `SCRIPT_ALIAS_AMBIGUOUS <name> candidates=N` + 拒绝）→ NOTE `SCRIPT_ENTITY_UNRESOLVED`
- 4 个调用点（`TryAcquire:118` / `TryCommand:141` / `TryRelease:150` / `TryRenew:159`）均为 `if (binding is null) return;` ⇒ Resolve 返回 null 即拒绝执行，**无需改动** ✅
- `ScriptRunner.cs:12-14` 头注释已更新为「短名 + 全名 + 歧义改用全名」✅
- `[projection]` 段（`ProbeRuntime.cs:1000`）改为 `"{0}  alias={1}  mv={2}  fa={3}  ar={4}"` ⇒ Step 5 可按派单 §4 阶段 1.5 的 SOP 从该段挑 `T{t}F{i}` 全名 ✅

### Step 3 · docs/10 门禁补齐（diff 仅 2 个 hunk）
- §5 判据表新增第 200 行：`| **P0c** | EntityId 唯一性 | 唯一数 == 绑定总数 | FAIL ⇒ 同一 EntityId 挂了多个 Formation，除 [patches]/[census] 外全部指标失真 |`
- §7.4 序 1 第 321 行改为四项门禁：`P0（patch 打上了）、P0b（有流量）、X5（无覆盖率缺口）、P0c（EntityId 唯一）` / `**P0 / P0b / X5 / P0c 任一 FAIL ⇒ 停，先修装置，后面全部作废**`
- ID 一致性核对：`Row("P0")` `:838` / `Row("P0b")` `:846` / `Row("P0c")` `:862` / `Row("X5")` `:923` —— 四个 ID 与文档逐字一致 ✅
- D2 缺陷（X5 FAIL=131470 拦不住流程）已由该门禁行闭合 ✅

---

## 5. 运行期验证状态（验证债 —— 不得视为已完成）

**本轮全程离线（src 为 net472 且依赖游戏目录下 TaleWorlds/0Harmony DLL，`Formation` 无法在 tests 工程 net10.0 中构造），无任何游戏内实测。**

| 待验真值 | 派单期望 | 是否已有证据 | 解锁条件 |
|---|---|---|---|
| P0c 是否真 PASS | 唯一数 == 绑定总数 | ❌ 无（仅有代码推断） | Step 5 进游戏跑 census-baseline |
| X5 是否真降到 0 | `==0`（上轮 FAIL=131470） | ❌ 无（131470 中 99.98% 判为假数据，但未被实测证伪） | 同上 |
| P0 是否仍是 7 | =7 PASS | ❌ 无 | 同上 |
| `SCRIPT_ALIAS_AMBIGUOUS` 是否真能触发 | 多 Team 下拒绝执行 | ❌ 无 | Step 5 用短名脚本在多 Team 战斗里跑一轮 |

`tests/CommandLab.Tests` 的 51/51、10/10、15/15 **不覆盖**本次改动（该工程不引用 BannerlordProbe），因此这些绿**仅证明未回归，不证明改动正确**。

---

## 6. 残留风险登记（不据此改代码，交给 Step 5 实测证伪）

1. **`TeamSeqOf` 返回 -1 时的塌缩**：若多个 `Team == null` 的 Formation 且 Index 相同，会同时得到 `T-1F{index}` ⇒ P0c FAIL。这**正是 P0c 门禁要检出的场景**，属设计内；Step 5 若出现 `T-1Fx` 重复，应按docs/10 §12.8 归因（Mission 初始化期 patch 命中未挂 Team 的对象），不是回归。
2. **Teams 集合中途变动**：`Bind` 有 TryGetValue 短路 ⇒ 已绑定的 EntityId 冻结不变；仅**尚未绑定**且 Teams 列表恰在两个 Bind 调用之间重排时才可能编号漂移。概率极低，且会被 P0c 暴露。
3. **`BindAll` 与 patch 路径的序号同源性**：`OrderWritePatches.cs:123/147/170` 的 `runtime.Bind(__instance)` 走 teamSeq=-1 的 `TeamSeqOf`；`BindAll` 走显式 `ti`。两者都以 `Mission.Current.Teams` 的同一枚举为源，理论上一致；这是本次唯一无法离线证明的一致性点，**必须靠 Step 5 的 P0c PASS 背书**。
4. **文档漂移（次要）**：`docs/10-harmony-runtime-experiments.md:779/782` 仍引用旧签名 `public FormationBinding Bind(Formation formation)` / `EntityIdOf(formation)`（该段是 §12.8 的历史取证记录，属引用旧代码的说明性文字）。本轮不在 Step 3 范围内，登记待 Step 6 回写时一并处理。

---

## 7. 下一步

Step 5（须用户进游戏执行，captain 与成员均不代跑）：
1. `dotnet build` 后把 `deploy\CommandLabProbe\bin\Win64_Shipping_Client\` 下三个 DLL 手工 copy 到 `Modules\CommandLabProbe\bin\Win64_Shipping_Client\` 并双向校验（派单 §5 坑 5）
2. 精简 load order（关掉 CombatBrain / CAI / RBM / FrontlineMod）
3. 跑 `census-baseline`，判读四项：P0=7 PASS、P0b>0 PASS、P0c 分子==分母 PASS、**X5==0 PASS**
4. 若 X5 仍 FAIL，**先核对派单 §7 归因清单再下结论**，不得直接写成结论
5. 产物归档为 `tests\artifacts\host\valid-<yyyymmdd-hhmm>-census-baseline\`

Step 6（依赖 Step 5 产物）：docs/10 §11「第二次实测」/ HANDOFF.md 第 2 节 / `.workbuddy\memory\` 日志 / git commit。
Step 7（待用户确认）：删除 3 个 0 字节垃圾文件。
