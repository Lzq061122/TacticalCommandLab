<!-- PROMPT META
Name: Tactical Command Lab Phase 11 Basic Delivery
Version: 1.0
Author Role: Architecture Designer and Acceptance Reviewer
Model: GPT-6
Created: 2026-10-07
Updated: 2026-10-07
Target: Tactical Command Lab Development Team
Scope: Phase 11 Architecture Documentation and Evidence Handoff
-->

<!-- CHANGE LOG (newest first, keep last 3)
v1.0 2026-10-07 01:11 | role=Architecture Designer and Acceptance Reviewer | model=GPT-6 | editor=codex-phase11-design-20261007 | note=依据已核对的 Phase 10 证据限定 Phase 11 设计、交付、验收与 handoff 责任
-->

# Phase 11 基本任务派单

你是 TacticalCommandLab 开发组。本轮设计由 Codex 完成，你负责把设计落实为可审查的正式架构交付，并提交反馈文档，由 Codex 审查。不要重新自由选型，也不要为了让实验全 PASS 修改既有判据。

## 1. 目标与授权范围

工作区：`E:\MBmod\TacticalCommandLab`。

Phase 11 原任务是定稿框架。已有 Core/Simulation/Telemetry/Probe，不要求你新建一套框架。本轮基本开发包括：证据复核、`docs/07-final-architecture.md`、指标口径最小同步、项目当前状态更新、专门的 handoff。

本轮不修改运行时代码，不新增游戏实验，不部署 DLL，不读取或修改 CombatBrain 项目，不进入 Phase 12。若发现实现缺口，完成不依赖它的文档交付，将缺口、证据和最小后续任务交回 Codex；不要擅自把 Phase 11 扩成重做 Phase 10 或产品集成。

用户已授权推进本轮 Phase 11 设计与基本任务，历史“本轮停止/不进入 Phase 11”只记录旧会话停止状态，不阻止此派单；Phase 10 的 FAIL 和未验证项仍保持原结论。

## 2. 开工前阅读与保护

按以下顺序读取：

1. 用户当前指令、`E:\MBmod\AGENTS.md`、工作区实际存在的下级 AGENTS；Prompt 资产按 `E:\MBmod\manager\Prompt 题头元数据规范.md`。
2. `docs/phase11-design.md`：本轮已经完成的设计与验收约定，是任务实现依据。
3. 最新根 `HANDOFF.md` 的当前状态段与 Phase 表；`docs/local-phase10-verification-20261006.md`；若已有更新的 Phase 10 收口报告，核对原始证据后一起读取。
4. `docs/01-problem-definition.md`、`docs/06-experiment-results.md`、`docs/07a-architecture-abc-comparison.md`、`docs/05-experiment-plan.md` 的指标与判别力定义。
5. 最终 run04–run11 的 assessment、manifest、实际脚本、原始 metrics/telemetry、加载身份；只读查看本设计列出的现有实现位置。

截至派单创建，本项目未发现 Prompt registry；不要从题头推断项目生命周期，也不要为本轮自行创建 registry 或注册进其他项目。本文件用于用户本次明确下发的任务。

先记录当前 branch/HEAD/status、设计文件 SHA-256、已有修改清单和本轮预期修改文件。设计时观察到 HEAD=e57e157，但实测身份仍为 adab4cf+逐场 diff+DLL；现场如有新提交，以实际记录为准。

当前工作区已有他方未提交文档和诊断工具。逐文件读取最新版本；同一文件只保留一个写入方，写前复核内容，发生并发变化时重新读取。禁止 reset/clean/stash 覆盖、强推、整体重排 HANDOFF、顺手格式化或打包提交他方文件。沿用用户指定分支；确需新分支时默认 `codex/` 前缀，不丢失现有工作树。

## 3. 任务一：形成可复核证据记录

交付 `_audit/2026-10-07-phase11-evidence-review.md`；跨日用实际日期。至少包括：

- 八场输入与实际身份；核对 manifest 的路径、长度和哈希，并核对 assessment 指定的 metrics/telemetry 哈希。缺失或不匹配时，标明受影响结论，不能将它们认证为本轮复核通过。
- 保留 H2/H2b 的 G3 FAIL、H3 原判据 PASS 与独立 global grade、REL=0/0/0/1。最后一项同时记“实验预期失败、反例辨识成功”，不得写 REL 四组全部 PASS。
- 对关键结论使用设计 §5.2 的证据列，至少覆盖：目标 Movement 接管、G3 范围、完整覆盖率不足、H3 性质、REL 恢复顺序与自然恢复、Proposal 裁决。
- 复用 `tools/recompute_telemetry.py` 对既有八份 telemetry 做只读复核；新输出存到本轮独立目录，不能覆盖原始报告。其资格输出和解析成功不能替代装置门禁，也不能认证全部覆盖率。
- 若已有 `tools/phase10_evidence` 或后续正式诊断工具，复核其源码与输入，必要时用于解释 G3 失败。输出必须写到新的目录并区分原判据、事件归因和重建时间，不能让默认输出覆盖他方的审计目录。本轮不再新建另一套诊断框架。

可用的既有离线重算调用示例（每份输入分别执行，捕获真实退出码）：

```powershell
& 'C:\Python314\python.exe' 'tools\recompute_telemetry.py' '<实际存在的一份 telemetry 绝对路径>'
```

不要用每场最后一个 tick 文件替代 assessment 指定的实验 dump；不同输入时长会改变判定范围。hold 不能从只包含写入事件的文件凭空重建连续宿主真值，应核对现有 metrics、采样实现和原始观测能支持的范围。

跨机器开发拿不到本地忽略证据时，明确列出缺失文件和未复核结论，先完成其余任务。handoff 需提供可实际打开的证据包/本地副本及哈希，不把“路径已写”当作“对方已收到”。

## 4. 任务二：正式架构文档

新增 `docs/07-final-architecture.md`，完整落实 `docs/phase11-design.md`。至少覆盖：

1. 本轮范围、设计定稿与运行时能力验收的区别。
2. A/B/C 选择与证据：常态 C，确定控制使用受限 B，禁止靠频率/优先级抢写；限定历史强断言适用范围。
3. Movement/Facing/Arrangement 的逐域表：默认通道、夺取条件、现有实测范围、恢复要求、未验证能力。
4. 既有 Intent/Task/Authority/WritePort/Telemetry/宿主接入的职责与依赖；Plan/Hierarchy 未实现的部分如实说明。
5. 不新增 Proposal/独立 Executor 的裁决、证据和重新讨论的触发条件。
6. Acquire/Renew/控制/显式 Release/Expire/实体退出/Mission 结束/异常的设计契约与现有差距；不得以正常 Release 等同全部恢复路径。
7. 第 15 项 hold、G1/G2/G3、覆盖率、OME、两种 release_recovery_failures 口径的边界。
8. 本设计 §6 的最终七问，逐问区分当前答案、设计建议、Phase 12 才能回答的部分。
9. 结论到证据的映射、遗留项及后续能力放行条件。

图示只使用现有职责，可用简短流程图；不创建新模块名、新状态机或新术语代替清晰说明。架构文档中的宿主方法名可作为证据引用，但不把宿主依赖引入 Core。

必须特别保留：

- 当前宿主 Full 控制证据仅限 T1F0 Movement 和既有单场 HOLD 窗口。
- H3 是关闭 AI 控制的实验，并不是 C 的 Behavior/Weight 集成验收；普通非 PlayerSergeant 情况下会影响整个 FormationAI.Tick。
- 目标 hold=100% 不能覆盖全场 G3 FAIL；X5=0 不能推完整覆盖率=100%。
- 原始 Move 重发按完整缓存执行；AfterRelease 被拒之后的 Native 自行恢复不能算那次恢复写入成功。
- 不把代码阅读中的到期/回滚/请求身份/并发风险写成已经发生的故障；不把未实现契约写成现有功能。

如你发现设计与新证据有实质冲突，列出具体设计条目、输入、推导和建议修订。完成未受影响部分；冲突部分标未定，不静默更改 `phase11-design.md` 或扩展范围。

## 5. 任务三：最小同步与反馈文档

允许修改以下位置，并仅做相关补记：

- `docs/05-experiment-plan.md`：将既有 `intent_hold_share` 与 G2 写入正式指标定义，链接 Phase 11；说明原实验、全场 grade 与新设计的口径边界，保留历史阈值。
- `docs/06-experiment-results.md`：补记 hold 的角色、`lab_delivery_rate` 不作控制判据、后续设计结论的链接；保留实验原始数值与判定。
- `docs/07a-architecture-abc-comparison.md`：仅在当前补记中限定 C 的保证边界和 A 的实验适用范围，并链接最终文档，不重写历史实验结果。
- 根 `HANDOFF.md`：当前状态写“Phase 11 基本交付已完成，待 Codex 审查”；链接新文件，说明 Phase 10 FAIL/遗留项和 Phase 12 未启动，保留原历史日志。

新增 `HANDOFF.phase11.md`，参考 `docs/phase11-handoff-template.md` 填写。模板只用于交接事实，不是假报告；不得把待填项替换成未经执行的 PASS。

你必须交给 Codex：工作身份、逐文件改动和理由、逐项验收结果、复核命令/退出码/输出路径、关键证据行和哈希、实际未验证项、设计偏差、已知风险、后续建议与证据副本位置。若没有运行游戏，写“本轮游戏 runtime=NOT_RUN，引用的原始 run04–run11 runtime=RUN”；不得只写一个 NOT_RUN 造成历史实测被误读。

## 6. 验收条件

| 条件 | 合格要求 |
|---|---|
| 范围 | 无运行时代码/部署/新游戏实验/CombatBrain 改动；无重复 Proposal/Executor 框架 |
| 架构 | 正式文档逐项落实设计，三域表、职责、生命周期、Proposal、七问齐全 |
| 事实 | 重要断言均有类型、输入、哈希、窗口、配置、支持边界；历史/新执行明确区分 |
| 失败保留 | H2/H2b G3 FAIL、H3 判据范围、REL 预期失败完整保留，不调阈值制造 PASS |
| 指标 | hold/G2、G1 生效变更、全场 G3、X5 下界、OME 和两种 REL 口径一致，无偷换 |
| 代码差距 | 设计 §4.1 各项公开登记；不宣称到期、回滚、延迟请求、并发和异常恢复已完成 |
| 同步 | 仅最小改动允许文档；根 HANDOFF 指向当前交付且状态为待审查 |
| 交接 | HANDOFF.phase11.md 可独立理解，证据实际可访问，无未说明占位符，无自我最终验收 |

本轮是文档与证据复核，不要求为文档新写单测，也不机械重跑全部构建。引用 2026-10-06 的 102/102、23/23 时必须写“已有报告”。只读复核工具如果需要构建，报告该工具实际构建/执行结果，不能把它写成新增游戏验收。

## 7. 交回与停止条件

交付后向用户回复：`HANDOFF.phase11.md` 的绝对路径、最终架构文档路径、证据复核报告路径、是否有未完成验收条件；说明“待 Codex 审查”。不要自行宣称最终通过，不自动进入 Phase 12。

审查职责归 Codex：读取 handoff、核查实际 diff 和证据、独立复算必要项，产出日期对应的 `_audit/*-phase11-review.md`，给出通过/部分通过/返工及具体整改。开发组完成交接即可停止，不需要向其他聊天自动发送消息。

若提交本轮变更，只暂存自己明确负责的文件和改动；Prompt 内容如有实质修改，按题头规范更新版本/CHANGE LOG，并使用与 CHANGE LOG 一致的 Editor-Role / Editor-Model / Editor-Team trailer。是否提交、推送或创建 PR 沿用用户已有授权；本派单不要求发布或合并。
