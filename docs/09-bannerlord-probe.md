# 09 — BannerlordProbe 实现（Phase 9）

> 生成时间：2026-09-20 GMT+8（会话 9）
> 状态：**Phase 9 完成**。工程可离线构建（`net472`，零 NuGet 三方包）；Core/Telemetry 新增 `netstandard2.0` 目标后 `replay_hash` 与全部判据无回归（见 §2.3）。
> 尚未进游戏运行 —— **任何实测结论都必须等 Phase 10**，本文件不写任何 Telemetry 支撑之外的稳定性判断。

---

## 1. 定位与红线

**它是 Phase 10 的被测对象，不是产品。**

| 项 | 约定 |
|---|---|
| 不是 | 不是 CombatBrain，不实现任何战术，不做 Commander / Arbiter / Executor |
| 只支持 | `HOLD` / `ADVANCE` / `RETREAT` 三条中性指令（Movement 域） |
| 观察不干预 | Facing / Arrangement 只做"原生写入量 + 是否可拦截"的观测，probe 从不下达这两域指令 |
| 唯一引用宿主 | `src/CommandLab.BannerlordProbe/` 是全套工程里唯一允许出现 `TaleWorlds.*` 的工程（D1） |
| 判定位置 | 是否放行**只由 `CommandLab.Core` 的 `WritePort.Evaluate` 决定**，probe 不复制任何判定逻辑 |
| 不靠竞速 | 没有任何"每 tick 重复 SetOrder"，也没有靠 `return false` 之外的手段压过 Vanilla |

---

## 2. 关键工程决策

### 2.1 为什么必须让 Core / Telemetry 多目标 `netstandard2.0`

Bannerlord 加载的是 **.NET Framework 4.7.2** 程序集，而既有实验室工程全部是 `net10.0`。若不改造，probe 只有两条路：要么复制一份指标定义（仿真与实测随即失去可比性，直接违反 Phase 9 要点 4），要么放弃复用。两者都不可接受，因此选择**给 Core / Telemetry 追加一个 `netstandard2.0` 目标**：

```text
CommandLab.Core      net10.0;netstandard2.0
CommandLab.Telemetry net10.0;netstandard2.0
CommandLab.Simulation    net10.0          （不上宿主，保持纯净）
CommandLab.BannerlordProbe  net472        （消费 netstandard2.0 目标）
```

改造内容是三类，全部为**兼容替换**（不改变输出字节，只改变取得方式）：

| # | 改动 | 原因 |
|---|---|---|
| 1 | 新增 `Compat/Polyfills.cs`（`IsExternalInit` / `RequiredMemberAttribute` / `CompilerFeatureRequiredAttribute` / `SetsRequiredMembersAttribute`），在 Telemetry 中以链接方式共享 | `init` 与 `required` 是 net5+/net7+ 编译器特性，netstandard2.0 缺标记类型 |
| 2 | `Enum.Parse<T>()` → `(T)Enum.Parse(typeof(T), …)`；`string.Create(…)` → `string.Format(…)`；`Convert.ToHexString` → `BitConverter.ToString(...).Replace("-","").ToLowerInvariant()` | 三个 API 均不存在于 netstandard2.0 |
| 3 | `WritePort` 依赖由 `VirtualClock` 改为新接口 `ILabClock`；新增 `SubmitImmediate(WriteRequest)` 与 `SetPolicy(WritePortPolicy)` | 宿主侧需要"任务时间时钟"与"同步取决策"（见 §3.2 / §3.3） |

### 2.2 新增的 Core 语义（均为纯追加）

| 项 | 说明 |
|---|---|
| `ILabClock` | `Dt / Step / Now`。`VirtualClock`（确定性）与 `ProbeClock`（任务时间）对 `WritePort` / `Metrics` 完全等价 —— **这是仿真与实测可同表对比的结构保证** |
| `WritePort.SubmitImmediate` | 单候选立即裁决并返回 `WriteAttempt`。宿主必须同步知道放行/拦截，不能等帧末 `FlushTick`。判定逻辑位置不变 |
| `WritePort.SetPolicy` | 整体替换策略对象，用于脚本在运行时翻转对照条件（见 §6） |
| `CommandValue.Obs0..Obs15` | 中性观察槽（取值 16..31），见 §4.2。纯追加，不触碰 0..4 |

### 2.3 回归证据（改造后重跑）

```text
replay_hash        = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199  （与会话 8 完全相同）
D0 装置门禁         = 15/15 PASS
E1–E11 判据        = 51/51 通过，失败 0
E12 判据           = 10/10 通过，失败 0
构建               = 0 error / 0 warning（net10 × 3 + netstandard2.0 × 2 + net472 × 1）
```

---

## 3. 数据流

### 3.1 写入路径

```text
宿主 Formation.SetMovementOrder(MovementOrder)
        │
        ▼  Harmony Prefix（OrderWritePatches）
ProbeRuntime.Gate(formation, domain, path, hook, value, raw)
        │   ├─ 把宿主 Order 翻译成中性 CommandValue
        │   ├─ WriterId = Lab（在 LabWrite 线程作用域内）或 Native
        │   └─ 组装 WriteRequest（Lab 写带 LeaseId/Token/Generation；Native 写不带）
        ▼
Core.WritePort.SubmitImmediate  ──► Evaluate（归属 / Level / Fencing / 迟滞 / 最短驻留）
        │                                   │
        │                                   └─► TelemetryCollector.OnWriteAttempt（含被拒）
        ▼
返回 !attempt.Rejected  ──► true = 放行原方法，false = 拦截
        │
        └─► HostContext 追加一行 HOST|…（宿主专有字段，不污染 Core 行）
```

**字段对齐（HANDOFF §8 要求）**：`WriteAttempt.OriginalMethodExecuted` 与 Harmony Prefix 的 `return false` 是同一个布尔 —— 这一条从 Phase 5 起就被锁定，本 Phase 首次在真实宿主侧兑现。

### 3.2 时钟

`ProbeClock` 累加 `MissionBehavior.OnMissionTick(float dt)` 的 dt。它是全套工程里唯一接触真实推进的实现，只做累加，**不参与任何判定**。文件名时间戳用到 `DateTime.Now`（dump 输出目录之外无用途，已登记）。

### 3.3 路径归类（依据 `02` §5 门控矩阵）

| 域 | 默认路径 | 依据 |
|---|---|---|
| Movement | `Gated` | Behavior 侧写入，`IsAIControlled` 覆盖得到 |
| Facing | `Gated` | 同上 |
| Arrangement | `NonGated` | `Formation` 自维护双 Timer 不受门控（`02` §8.2） |

`ProbeConfig.ArrangementPath` 可切到 `Gated` —— 这一切换就是 Phase 10 H2 的覆盖率对照本身。

---

## 4. 中性指令与观察口径

### 4.1 Movement（唯一可下达的域）

| CommandValue | 宿主构造 | 判定方式 |
|---|---|---|
| `HOLD` | `MovementOrder.MovementOrderStop` | `order.OrderType == MovementOrder.MovementOrderStop.OrderType` |
| `ADVANCE` | `MovementOrder.MovementOrderAdvance` | 同上 |
| `RETREAT` | `MovementOrder.MovementOrderRetreat` | 同上 |

**不引用任何 `MovementOrderEnum` 成员名**，全部通过与静态构造值比较得出，规避版本间枚举命名漂移。

### 4.2 中性观察槽（`Obs0..Obs15`）

Facing / Arrangement 以及"无法归入三条中性指令"的 Movement 值，按首次出现顺序投影到 `Obs0..Obs15`。

- 目的只有一个：让 Core 侧的 `OrderChanged` / oscillation 判定成立（依赖值不等）。
- 槽位是**中性符号，不含战术语义**，不因 `ArrangementOrderShieldWall` 这类宿主命名而获得任何含义。
- **已知缺陷**：某域出现超过 16 个不同值后回绕，回绕会把不同值判成同值 → **低估变化次数**。`HostOrderProjection.WrapCount` 已计数，dump 的 `[projection]` 段会打印；Phase 10 见到 `wrap>0` 必须把它写进结论的不确定项。

---

## 5. Telemetry

### 5.1 Core 行（与仿真逐字段一致）

`TelemetryCollector.CanonicalLines()` 原样输出，字段与 Phase 7/8 完全相同。**这使得 Phase 10 的实测可以直接喂给 `MetricsCalculator`，与仿真结果同表对比。**

### 5.2 HOST 行（宿主专有，另起一行）

```text
HOST|t=<sec>|E=<entity>|D=<domain>|hook=<写入口>|team=player|idx=<Formation.Index>
    |behavior=<当前 Behavior 类型名>|weight=<权重>|raw=<原生值>|prev=<前值>|new=<新值>|ome=0|chg=0
```

- `behavior` 取 `Formation.AI.ActiveBehavior` 的类型名；
- `weight` 用反射取 `Weight` / `WeightFactor`，取不到记 `-`（**口径限制，已登记**）；
- `NOTE|…` 行用于自由留痕（如 `HARMONY_PATCH_FAILED`），**不进入 `WriteAttempt` 流，因此不影响 14 项指标**。

### 5.3 `intent_hold_share`（Phase 8 的硬门限 G2）

`HoldMeter` 在**归属窗口内**按 Mission tick 采样宿主的**真值**（`GetReadonlyMovementOrderReference()` / `FacingOrder.OrderEnum` / `ArrangementOrder.OrderEnum`），累加"实际值 == 意图值"的时长。

- **不是读镜像**：读镜像会高估控制权（镜像只记录被放行的写入）。
- **末段处理**：最后一次采样到 `Close` 之间按"状态未变"外推一次，等价假设是采样频率高于 FormationAI 的 0.5s 周期。`OnMissionTick` 的 dt 远小于此，成立。
- 窗口为 0 时返回 0% 而非 100%（避免"刚夺取就释放"被判成完全控制）。

---

## 6. Acquire / Release 全流程

```text
Native owns ──► acquire(entity, domain, Full, ttl)
                  │  [可选] SetControlledByAI(false, IsSplittableByAI)  ← Route B 对照开关
                  │  AuthorityService.Acquire（Level + Lease + 单调 Token + Generation）
                  │  开启 HoldMeter
              ──► cmd(entity, Movement, HOLD)      ← 唯一可下达的域
              ──► Lab 持有时：冲突的原生写入在写入口被拒（return false）
              ──► release(entity, domain)
                  │  [RestoreBeforeRelease=true ] 先做恢复，再交还（仍是 owner，写入放行）
                  │  [RestoreBeforeRelease=false] 先交还再做恢复（写入会被拒 → 计入恢复失败）
                  └─► Native 恢复
```

**恢复动作开关**（`ReleaseRestoreMode`，可按位组合）：

| 开关 | 动作 | 对应 Phase 10 的问题 |
|---|---|---|
| `ReapplyLastNativeOrder` | 原样重发最后一次观测到的原生 MovementOrder | 不重发能不能恢复？ |
| `ResetBehaviorWeights` | `FormationAI.ResetBehaviorWeights()` | **是否必须重置权重才能恢复** |
| `RestoreControlledByAi` | 把 `SetControlledByAI` 还原为夺取前的值 | 有无残留状态 |

每一步被拒或抛异常都计入 `ReleaseRecoveryFailures`（对应 `06` 的第 14 项指标口径）。

---

## 7. 脚本化时间线

Phase 10 要可复现，因此所有动作走 `%USERPROFILE%\Documents\CommandLabProbe\script.txt`：

```text
t=2.0   acquire P1 Movement Full 30
t=2.5   cmd     P1 Movement HOLD
t=25.0  release P1 Movement
t=30.0  dump    h2-suppress
```

| 动词 | 参数 |
|---|---|
| `acquire` | `<entity> <domain> [level] [ttl]` |
| `cmd` | `<entity> <domain> <HOLD\|ADVANCE\|RETREAT>` |
| `release` / `renew` | `<entity> <domain>` |
| `policy` | `enforceGated=`、`enforceNonGated=`、`patchMovement/ Facing/Arrangement=`、`arrangementPath=`、`level=`、`ttl=`、`autoRenew=`、`controlledByAiRoute=`、`restore=`、`restoreBeforeRelease=` |
| `dump` | `[tag]` |

`policy` 走 `WritePort.SetPolicy` 整体替换，**判定逻辑位置不变**。Entity 命名：`P`/`N` + `Formation.Index`。

---

## 8. 构建与部署

```powershell
cd E:\MBmod\TacticalCommandLab
dotnet build CommandLab.slnx
```

构建后自动落到 `deploy\CommandLabProbe\bin\Win64_Shipping_Client\`（含 `CommandLab.Core.dll` / `CommandLab.Telemetry.dll`）。

部署：把 `deploy\CommandLabProbe\` 整个目录拷进
`E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\`，
再把 `script.sample.txt` 复制为 `%USERPROFILE%\Documents\CommandLabProbe\script.txt`。

输出：`%USERPROFILE%\Documents\CommandLabProbe\telemetry-<时间戳>-<tag>.txt` 与 `metrics-<时间戳>-<tag>.txt`。

**依赖**：`Modules\Bannerlord.Harmony`（v2.4.2.248，本机已装）。宿主版本 v1.3.4。

---

## 9. 文件清单

| 文件 | 职责 |
|---|---|
| `src/CommandLab.BannerlordProbe/CommandLab.BannerlordProbe.csproj` | net472；宿主程序集一律 `Private=false`（不复制，避免加载冲突）；构建后自动拷到 deploy |
| `ProbeClock.cs` | `ILabClock` 的任务时间实现 |
| `NeutralOrders.cs` | 三条中性指令 ↔ `MovementOrder`；`HostOrderProjection`（Obs 槽 + 回绕计数） |
| `ProbeConfig.cs` | 全部对照开关 + `ToPolicy()`（字段名与 `WritePortPolicy` 对齐） |
| `ProbeRuntime.cs` | `FormationBinding` / `HoldMeter` / 运行时主体（Gate / Command / Acquire / Renew / Release / Tick / Dump） |
| `HostContext.cs` | HOST 行与宿主专有字段（behavior / weight / team） |
| `OrderWritePatches.cs` | 三个写入口 Prefix + 未 patch 入口的登记说明 |
| `ScriptRunner.cs` | 时间线脚本解析与执行 |
| `ProbeSubModule.cs` | `MBSubModuleBase` 入口 + `MissionBehavior`（tick 驱动 / 周期 dump / 结束 dump） |
| `deploy/CommandLabProbe/SubModule.xml` | 模块清单 |
| `deploy/CommandLabProbe/script.sample.txt` | 脚本样例（含 H1/H2/覆盖率/恢复四组） |

---

## 10. 移交 Phase 10

1. **H1 External Write**：`policy` 全部默认（不夺取），脚本只 `cmd HOLD` → 看 `native_overrides`、多久被覆盖、来自哪个 hook。
2. **H2 Native Suppression**：`acquire … Full` + `cmd HOLD` → 判 **G1 `unauthorized_writes==0`** 与 **G2 `intent_hold_share≥95%`**；再翻 `enforceNonGated=true arrangementPath=Gated` 做覆盖率对照（E12 ⑨ 的实测版）。
3. **H3 Native Integration**：`controlledByAiRoute=true`（Route B）与纯 Route A 对照。
4. **Acquire/Release 全流程**：`restore` 四组 × `restoreBeforeRelease` 两组，记录 `release_recovery_failures`。
5. **必须同表判读 hold**：G1 单独成立不构成"取得控制权"（E12 ⑧）。

**未 patch 的入口（Phase 10 需补时先解决签名问题）**：

- `Formation.SetControlledByAI` —— Phase 9 曾登记"存在 1/2 参两种调用形态"。**会话 10 IL 实证更正：v1.3.4 只有一个 2 参方法**（`Formation.SetControlledByAI(bool, bool)`，IL 中 `Ldarg_2` 写入 `_enforceNotSplittableByAI`），`nameof` 可精确匹配、不存在重载歧义。会话 10 已补观测型 patch（只计数不拦截），详见 `docs/10` §2.1。
- `SetFiringOrder` / `SetFormOrder` / `SetRidingOrder` —— 写入口覆盖率实验的扩展项（`02` §2：写入口是 6 个不是 3 个）。

---

## 11. 已知限制与偏差登记

| # | 项 | 影响 |
|---|---|---|
| 1 | **未进游戏运行** | 本文件不含任何实测结论；所有稳定性判断留到 Phase 10 |
| 2 | Behavior 权重靠反射取 `Weight`/`WeightFactor`，取不到记 `-` | 宿主字段"Behavior Weight"可能缺失；不阻断 G1/G2 |
| 3 | `Obs` 槽超过 16 个不同值回绕 | 低估变化次数；`WrapCount` 已在 dump 中打印 |
| 4 | 同 tick 内不做跨提交批仲裁（用 `SubmitImmediate`） | 与仿真的 `Submit+FlushTick` 在"同刻多候选"上语义不同；宿主侧提交序是真实的，故按真实序裁决 |
| 5 | 镜像 `ControlledEntity` 只记录"被放行的请求值"，未用 Postfix 回读宿主实际值 | 镜像可能滞后于真值；**hold 已改为读真值**，故不影响 G2 |
| 6 | `DateTime.Now` 仅用于 dump 文件名 | 不进入判定路径 |
| 7 | 脚本按"任务时间"触发，战斗本身是否可复现未经检验 | Phase 10 需先做一次空跑重放比对 |
| 8 | probe 的纯逻辑（投影 / HoldMeter / 脚本解析）无离线单测 —— 它依赖 net472 + 宿主类型，无法挂在既有 net10 测试工程上 | 若 Phase 10 出现口径疑点，需单独搭一个 net472 测试宿主 |

---

## 12. 红线自检（grep 实证）

```text
Core / Simulation / Telemetry / tests 内 "TaleWorlds"        : 0 命中
src + tests + deploy 内 "CombatBrain"（含 xml）              : 0 命中
战术词 cavalry / morale / brace / skirmish / shieldwall / flank / volley : 0 命中
        （"protect" 的唯一命中是 C# 关键字 protected，假阳性）
真实时钟 DateTime.Now : 仅 1 处，位于 ProbeRuntime 的 dump 文件名（不进入判定路径）
```
