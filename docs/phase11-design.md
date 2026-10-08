# Phase 11 设计与基本交付约定

日期：2026-10-07（北京时间）。设计责任：Codex。本文件完成本轮设计选择；开发组负责证据复核、正式架构文档落地和交接，由 Codex 最终审查。

## 1. 本轮目标与完成含义

Phase 11 沿用项目原定义：定稿通用 Command / Authority 框架，正式交付 `docs/07-final-architecture.md`。Core、Simulation、Telemetry 和宿主探针已经存在，本轮不再建一套重复框架。

本轮基本任务是文档与证据交付，不包含运行时代码修改、新游戏实验或 CombatBrain 集成。Phase 12 才开始 CombatBrain 案例分析和集成。用户本次要求允许推进 Phase 11 设计与基本交付，覆盖此前“本轮停止、不进入 Phase 11”的会话停止指令；不代表 Phase 10 所有实验通过，也不自动放行 Phase 12。

“Phase 11 基本交付通过”表示：已有证据能够支持的架构选择已落实，未验证能力有明确边界，交接资料可复核。它不表示三域接管、全场稳定、所有恢复路径或产品集成已验收。

## 2. 设计依据与证据范围

### 2.1 基线

- 当前读取到的分支为 `codex/local-phase10-verification-20261006`，HEAD 为 `e57e157e839e334f532d7f875224491af6bd140d`（Freeze measured Phase 10 probe, Windows runner, and driver snapshot）。工作树另有文档改动和未跟踪文件，不能当作干净 HEAD。
- 最终八场测量身份仍是各场记载的 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0 + git-diff.txt + 部署/实际加载 DLL 身份`；不得倒写成测量时运行 e57e157。
- 原始实测：`_audit/_local-phase10-20261006/run04-census-final` 至 `run11-rel-after-release`。它们位于本地忽略目录；跨机器交付必须另带可访问副本，只有路径不构成交接。
- 本次设计前实际核验：八场 `artifact-manifest.json` 所列共 491 个文件的长度、SHA-256 全部相符；各场 assessment 指定的 metrics/telemetry 哈希也相符。此检查认证归档一致性，不认证全部实验结论。
- 本次没有启动游戏，也**未重跑全量工程/宿主构建或离线套件**（首轮仅用 `dotnet run` 构建并执行了单个诊断工程）。102/102 C#、23/23 Python 是 2026-10-06 已有报告的结果，不冒充本次执行。

优先读取 `docs/local-phase10-verification-20261006.md`、逐场 assessment 和原始输入；旧 HANDOFF 与旧日期的推断不能覆盖新证据。如果开发前出现新的 Phase 10 收口报告，先核对其原始输入和口径，再登记对本设计的影响，不静默替换既有判据。

### 2.2 能支持的结论

| 依据 | 已知事实 | 可以支持 | 不能支持 |
|---|---|---|---|
| E3/E4/E6/E7/E9/E12，`docs/06`、`docs/07a`、对应离线产物 | 写入口 Reject、Lease/Fencing、替换与并列 Writer 的差异在既有模型中有量化证据 | 框架职责划分、强制点、默认 C 与受限 B 的设计选择 | 把 Ns 模型结果写成任意游戏版本的实测事实 |
| run05 H2 | G1=0、G2=100%，目标 T1F0 Movement 的 HOLD 窗口约 24.50 秒；49 次 Native 尝试被拒；G3=Oscillating FAIL | 此实体、此场景、此窗口取得并保持目标控制 | 全场稳定、所有 Movement 路径完全覆盖、多场景泛化 |
| run06 H2b | G1/G2 PASS、G3 FAIL；目标 Arrangement sampled=2、port=3、unobserved=0；脚本只 acquire Movement | 没有观察到该计数口径的缺口 | Arrangement Full 已验证；完整写入覆盖率=100% |
| run07 H3 | H3-a/H3-b/G2 通过；AI 控制关闭后目标窗口 Native Movement 尝试为 0；全场 grade 仍为 Oscillating | 当前实验配置下的关联，以及关闭 AI 控制的抑制效果观察 | 路线 C 的 Behavior/Weight 集成已通过；实际 AI.Tick/行为选择调用计数为 0 |
| 当前安装 DLL 的 `current-formation-tick-il.txt` | AI.Tick 条件为 HasTeamAi 且 `(IsAIControlled || IsPlayerSergeant)` | 普通非 PlayerSergeant 情况下关闭控制会跳过该路径 **`FormationAI.Tick`** 的静态路径代价（`Formation.Tick` 自身继续其余编队簿记，见 IL 原件第 38 行） | 任意编队均停 Tick；运行时调用次数已经观测 |
| run08–run11 REL | None/Reapply/Reapply+Reset/AfterRelease 的 REL 分别为 0/0/0/1；最后一项为预期失败 | 原始 Move 重发有效；先丢失所有权再以旧 Lab 身份恢复会被拒；自然恢复可出现 | Reapply 或 Reset 普遍必要；恢复全部内部状态；到期/异常恢复也已验收 |

H3 脚本名 `h3-route-b` 沿用历史名称。其中 `controlledByAiRoute` 是关闭编队 AI 控制的实验开关，不能与 A/B/C 比较中的 C 集成路线混用。

Phase 8 的两处强表述在最终文档中需要限定：C 不提供独占控制保证，而不是任意场景下 hold 都只能为 0%/50%；A 被当前实验否定的是保留竞争写入后靠频率、优先级和时序抢写的方案，不是所有外部决策源。

## 3. 架构选择

### 3.1 通道与三域

| 域 | 常态设计 | 需要确定控制时 | 当前宿主支持范围 | 尚未支持的能力 |
|---|---|---|---|---|
| Movement | C：在原生控制链中影响/替换决策产出，常态 Writer 净增 0 | B：Full + 有限 Lease + Token/Generation，写入口 Reject | H2 已支持 T1F0、HOLD、既有单场窗口；作为受限候选 | 完整覆盖率、泛化与全场稳定；不能宣布默认产品能力 |
| Facing | C 为设计选择，保留原生合法写入关系 | B 的概念契约与 Movement 相同，但待专门验证 | 当前探针可以观察/拦截相关入口；没有独立目标命令与接管实测 | Full 接管、独立 hold 与 Release 验收 |
| Arrangement | C 为设计选择，必须考虑原生自维护路径 | B 只有覆盖自维护/兜底写入后才能验收 | 当前仅有观测与计数对照 | Full 接管、完整写入口覆盖、恢复与跨域副作用 |

C 是本轮选择的常态架构方向，有 RBM 静态案例和 E9 Replace 仿真支持，不是本探针已实现或实测通过的一套 Behavior 集成。

默认拒绝 A 的抢写做法；不把增加频率、反复 SetOrder、无限加 Priority 作为控制权补救。C 意图落地不佳时，不自动关闭 AI，也不自动升级为 B。需要确定控制的请求只有在目标域、场景、指令和恢复条件均有证据支持时才能采用 B；否则明确报告能力未支持。它不能返回“确定控制成功”后默默退回建议模式。

H3 的关闭 AI 控制方式不选作常态默认。其开关作用于整个 FormationAI，可能同时影响多个域的行为选择；逻辑上按域授权不意味着宿主物理效果已按域隔离。PlayerSergeant 例外必须保留。

### 3.2 职责与现有实现

| 既有职责 | 现有位置 | 最终契约 | 本轮处理 |
|---|---|---|---|
| Intent / Task | `Core/Planning.cs` | 目标、终止条件、任务状态与 Resume/Restart/Cancel；不得代替写入口授权 | 复用与说明，不增加另一套任务框架 |
| Ownership / Lease | `Core/Authority.cs` | `(EntityId, Domain)` 授权；Acquire/Renew/Release/Expire；单调 Token 和变更 Generation | 复用，明确宿主恢复尚有缺口 |
| 写入许可与合法候选选择 | `Core/WritePort.cs` | Ownership 与 Fencing 在写入口；**最终契约要求**许可检查先于合法候选的 Arbitration；所有尝试留痕（**现有实现相反，见 §4.1 末项差距**） | 不搬到 Source 或决策器；不改实验开关 |
| 中性仿真 | `Simulation/` | 虚拟时钟、播种、确定性与 Ns 条件模型 | 保留，不作为游戏能力证明 |
| 宿主接入 | `BannerlordProbe/` | 类型翻译、入口拦截、宿主状态观察与恢复；Harmony 不承担战术决策 | 描述当前探针边界，不称产品执行框架 |
| 遥测与判读 | `Telemetry/`、离线工具 | 写入尝试、归属事件、任务事件、hold 与稳定性证据 | 统一文档口径，不改字段/算法/阈值 |
| Plan / Hierarchy | `docs/01` 的 L3 说明 | 跨目标约束/重规划负责 Intent/Plan Conflict | 保留设计职责；尚无完整实现，不本轮新增 |

依赖保持：Core 不反向引用 Telemetry；Simulation/Telemetry 可以依赖 Core；只有 BannerlordProbe 接触宿主类型。本轮不读取 CombatBrain 源码来迁就其旧结构。

### 3.3 Proposal / Executor 裁决

当前最小框架不新增 Proposal、独立 Executor 或新的 Command Bus。理由：E1–E12 与既有宿主控制实验没有逼出这些抽象；Intent/Task、WriteRequest、AuthorityService、WritePort 已能表达当前问题。

这是本轮范围内“不需要新增”的设计裁决，不是“永远不需要”的定理。仅当后续真实需求证明现有对象无法表达、给出最小失败例和验收标准时再讨论。宿主实际执行动作仍存在，由现有接入层承担；不因没有名为 Executor 的类而否认执行职责。

五类冲突沿用 `docs/01`：Command→合法候选 Arbitration；Authority→Ownership/Lease/Fencing；Intent/Plan→任务与上层约束/重规划；Native Pipeline→接入位置与覆盖范围。Content Transformation 保留为与 Authority 正交的值变换机制，不新增第六类冲突。

## 4. 控制权生命周期契约

以下是最终设计要求；是否已有实现必须逐项标注，不能把契约写成已验证事实。本轮只形成契约和差距表，不修改现有实验装置。

1. **进入 B**：先确认目标域的能力与恢复前提，再取得 Full Lease。Acquire 未成功不得宣称获得控制，也不得留下已变更的宿主 AI 状态。Lease 保持有限到期时间。
2. **控制中**：每条受控请求携带创建时的 Owner/LeaseId/Token/Generation，写入口以当前记录校验。旧请求不能在入口被重新盖上当前代次而变“新”。Renew 失败、到期或归属变化后，不再以旧权限提交。
3. **释放**：停止本方新命令；如选择重发，必须仍有有效 Lab 权限且使用缓存的原始命令，不能从 Obs 投影重建；恢复本方实际改变的 AI 状态；交还归属、使旧代次失效、清理本方缓存；观察 Native 后续合法流量及未接管域影响。
4. **恢复选择**：不默认 Reapply 或 ResetBehaviorWeights。run08 支持某场景可以自然恢复；run09/run10 支持动作可执行，不证明必需。H3 若修改过 AI 控制，恢复夺取前状态是交还契约；不能永久关闭原生 AI。
5. **失败和边界**：动作被拒、抛异常、没有有效缓存、租约已失效，均不得被计为该动作成功。显式 Release、Expire、实体退出和 Mission 结束应当能够终止本方控制；已到期不能复用旧 Lab Token 强行重发。恢复失败须留痕，并避免持续续约或重复强写把失败掩盖。具体宿主异常恢复实现待专门开发和验证。
6. **成功确认**：夺取与交还分别提供证据；原方法获准、原方法完成、宿主真值变化和后续自然恢复不能互相替代。恢复旧命令成功不等于所有 Behavior/Timer 内部状态回到基线。

### 4.1 必须公开的代码差距

| 源码观察 | 与最终契约的差距 | 本轮裁决 |
|---|---|---|
| `ProbeRuntime.Acquire` 在申请 Lease 前关闭 AI 控制 | Acquire 被拒时是否回滚宿主状态缺乏保证 | 记录阻塞该方式产品化；不本轮修补 |
| `TickCore` 的 SweepExpired + `DropDeadLeases` 只移除租约/关闭 hold；后者未调用 Restore | 自动到期不能等同显式 Release 的宿主清理 | 记录待验证/待实现，不称全部生命周期完成 |
| `WritePort.Evaluate` 不直接检查 ExpiryAt，租约到期回收依赖外部 Sweep 调用时序 | 写入口时刻的到期拒绝不能仅凭有 Sweep 方法认定成立 | 列出时序前提与待验证边界，不本轮改算法 |
| `GateCore` 以当前 `_leases` 和当前记录 Generation 构造 Lab 请求 | 当前同步探针不能证明在途旧请求身份完整保留 | 保留 E6 仿真结论；宿主延迟请求能力未验证 |
| `Release` 未检查 `_authority.Release` 返回值；`ReapplyLastNative` 在宿主原方法异常时的路径仍需核对 | 当前 REL 零计数不证明异常路径的完整成功认证 | 单列风险与后续验收，不自行修源码 |
| `_inLabWrite` 与相关集合的并发身份/访问 | 单场无崩溃不证明 worker 写入归因与并发安全 | 接入生产前补直接线程/归因验证，不凭临时变量名推断 |
| **（审查补充，原设计遗漏）`WritePort.FlushTick` 先 `Resolve` 后 `Evaluate`**：`foreach (var p in Resolve(...)) Evaluate(p);`（`src/CommandLab.Core/WritePort.cs:157-183`；`Resolve` 见 `185-209`） | **先仲裁后许可**：非法的高优先级候选可在权限检查前淘汰合法的低优先级候选，与本节上表"许可检查先于 Arbitration"的最终契约相反 | 由 Codex 审查指出（见 `_audit/2026-10-07-phase11-review.md` §3 第 3 项）。已在 `docs/07-final-architecture.md` §4.1/§4.3/§5.4 差距 7/§5.5 登记；**修 Core 属独立授权任务，本轮不改** |

这张表来源是代码阅读，除 REL 已有反例外，不表示这些故障已在本轮游戏中发生。差距阻塞对应能力产品化，不阻塞本轮如实完成设计文档。

## 5. 指标、证据与判定

### 5.1 统一口径

- 原 14 项指标继续保留；新增到正式文档的第 15 项是既有 `intent_hold_share`，不是新增算法。
- `intent_hold_share`：同一实体、同一域、**有效意图窗口**内，宿主真值与意图值相同的累计时长 / 该窗口累计时长。**有效意图窗口 = 从该 `(EntityId, Domain)` 首个被接受的 Lab 写入起，到该 Lab 归属结束**；`pre_acquire` 与 `acquire_to_first_accepted_lab_hold` 是**事件归因窗口，不是 hold 的分母**，不能从 Acquire 到首次被接受的 Lab 写入之间补算 hold。窗口起止与分母须逐 `(EntityId, Domain)` 记录。宿主采用 tick 采样加时间累计；匹配样本比例只作辅助，不等于普遍意义下的连续真值证明。
  - **口径更正（审查补充）**：原文"现有 HoldMeter 在有效命令提交后打开窗口"过于简化。`ProbeRuntime.Command`（`ProbeRuntime.cs:378-384`）实际条件是 `LastAttempt is not null && domain == CommandDomain.Movement`，**只检查该次写入尝试存在，未检查 `Rejected`**。故"仅在成功/有效命令提交后打开窗口"**不能无条件宣称**；接受写入打开窗口有本轮实测证据，**被拒命令是否同样打开/延长窗口未实测**，登记为待验证。**E12 历史仿真不使用窗口打开/延长语义**：`tests/CommandLab.Tests/Scenarios/ArchitectureComparison.cs:43-58` 在固定 `[2.0,22.0)` 区间按有效变更序列积分、沿用窗口前状态，**分母固定 20 s**（第 201 行 `from=2.0, to=22.0`），不调用 `HoldMeter.Open`；宿主 Probe 则是 `HoldMeter` tick 采样。**两者须分别标明**，引用 hold 数值须注明来源侧，不得把 Probe 的窗口规则倒写为 E12 历史行为。
- G2 仍为 hold≥95%。没有意图窗口、窗口为零、未采样或目标不存在，不能凭 0 次越权判成功。多目标时逐目标报告，不能拿 BestHold 代表所有目标；当前实验只对其指定目标作结论。
- G1 沿用既有 `unauthorized_writes` 的生效变更口径；写同值放行不计变化，故还需审查 rejected/OME 与目标 Native 尝试，不能仅凭 G1 认证所有不合法调用都被阻断。
- G3 沿用现有全场 MetricsCalculator，不改为目标局部分级，不裁掉 Acquire 前数据，不改阈值。目标 hold、全场 grade、事件窗口归因必须分别写明。
- X5/unobserved 是采样变化与入口计数缺口的下界。没有独立全量写入分母，不能计算或宣称完整覆盖率 100%。
- `lab_delivery_rate` 不作控制成功判据；它低估写同值且持续保持的情况。保留它作历史诊断，不重命名成 hold，也不改历史数值。
- 仿真 `release_recovery_failures` 的状态恢复窗口与探针 REL 的恢复动作失败计数分别列出；本轮不强行合并为一个数。探针 REL=0 不能认证仿真定义所要求的完整基线恢复。
- 宿主记录的 OME 目前主要表达 Prefix 放行意图；不能据此证明其他 Harmony Prefix 没有拦截，或原方法已成功返回。Probe crash/driver exception/身份不明必须登记，不能用零计数填补缺失证据。

### 5.2 证据表的必要列

每条重要结论包含：结论原文、证据类型（仿真/静态/宿主实测/设计选择/未验证）、实验或 run 编号、输入路径与 SHA-256、实体/域/时间窗口、实际配置、原判据及结果、支持范围、不能推导事项。

派生报告标 `runtime=NOT_RUN` 表示该次离线复核未启动游戏，不改原始实测 `runtime=RUN`。不同流同时间戳的事件不得凭打印顺序推断跨流严格先后；已打印时长的取整误差与重建时间须标明。

最新诊断工具若已经存在，只用于解释原 G3 失败来源并保留原判定。跨窗口 ABA 三元组要保留跨界信息，不切窗后假称全场通过；新工具的存在和输出本身不替代复核。

## 6. 最终七问的答案边界

| 问题 | 本轮定稿方向 | 证据边界 |
|---|---|---|
| Q1 谁控制 Formation | 多入口共享可变资源；Native 常态控制链 + 显式域授权，不能寻找一个万能最终 Writer | 原生静态取证与现有运行窗口 |
| Q2 如何取得控制 | C 只给影响力；确定控制用有证据范围的 B，Full/Lease/Token/入口 Reject | Movement 限定实测；三域全量待验证 |
| Q3 如何交还 | 显式结束与宿主状态恢复契约；重发必须合法，恢复动作必要性分别评估 | REL 动作与反例已支持；到期/异常/完整状态恢复尚不足 |
| Q4 三域独立 Authority | 逻辑以 `(EntityId, Domain)` 为原子单位；宿主接入副作用另验 | 仿真方向支持；H3 表明不能宣称物理效果完全分域 |
| Q5 多源如何协作 | 按五类冲突分别使用许可、合法候选仲裁、任务/计划与控制链接入 | 不以一个 Arbiter 代替所有职责 |
| Q6 CombatBrain 路线 | 本轮框架建议常态 C、必要时受限 B | 没有本轮 CombatBrain 案例分析，不给其具体迁移实现裁决 |
| Q7 冲突占比 | 沿用五类遥测签名的归因方法 | 没有 CombatBrain 异常样本，不给百分比 |

## 7. 开发组交付与 Codex 审查

开发组按派单完成 `docs/07-final-architecture.md`、指标文档最小补记、证据复核记录、`HANDOFF.phase11.md`，根 HANDOFF 只更新当前状态和链接，历史证据保持原样。

验收看：设计选择是否落地、每条结论能否回到输入、FAIL 是否保留、三域/生命周期是否夸大、文档口径是否一致、文件和证据能否实际访问、handoff 是否足够由另一审查者复核。文档改动不要求重跑全部构建，也不新增镜像实现的测试。

开发组交付状态应为“完成，待 Codex 审查”，不能自称已经通过最终审查。Codex 收到后对照本设计检查实际差异与证据，必要时独立复算；输出 `_audit/2026-10-07-phase11-review.md`（跨日按实际日期命名），结论写通过/部分通过/返工，并区分本轮基本交付和后续能力放行。

本轮没有新增架构模块、状态机、指标名称或专用分析术语；沿用项目已有 A/B/C、Authority、Lease、Token、Generation 和 handoff。
