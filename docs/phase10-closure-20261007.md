# Phase 10 证据收口与版本冻结（2026-10-07）

本轮只重放已有数据、审查补丁并冻结版本，没有启动游戏、重新部署或推进 Phase 11/12。全场 G3 仍然失败；H2b 完整覆盖率和 Arrangement 独占仍证据不足；H3 的实际 AI.Tick/Behavior 选择调用次数仍未验证。本报告是后来新增的分析，不能当成 10 月 6 日测量时的源码或记录。

## 版本与原始证据

真实仓库为 `E:\MBmod\TacticalCommandLab`，本轮分支 `codex/local-phase10-verification-20261006`，基线 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0`。未重置、合并 master 或推送历史。

- **测量快照**：run04–run11 记录的 HEAD 是上述基线，另有四文件未提交 diff：`OrderWritePatches.cs`、`ProbeRuntime.cs`、`run_offline.py`、`test_run_offline.py`。每场的 `git-diff.txt` 原样保留。
- **A 源码冻结**：`e57e157e839e334f532d7f875224491af6bd140d` 保存匹配的四个源码/测试 blob、实测原版驱动以及证据索引。这是事后录入提交，不是当时测量 HEAD。重新构建时 Git 元数据可能改变产物，不能把新 DLL 冒充旧 DLL。
- **B 可复用驱动修补版**：提交及 SHA-256 见 `docs/evidence/phase10-closure-20261007/closure-index.json`。只修正清理失败的报告/退出码及固定验收文档路径；没有游戏实测 B。
- **C 分析与文档冻结**：本报告所在提交保存离线诊断入口、派生表、结论修订和完整差异清单；不是新增运行证据。实际最终提交由交付消息给出。

原始索引为 [phase10-measurement-index.json](phase10-measurement-index.json)，新分析索引为 [closure-index.json](evidence/phase10-closure-20261007/closure-index.json)。原始数据根 `_audit/_local-phase10-20261006/` 是本机保留的忽略目录，未将原始日志放进 Git；仅取得仓库不足以重新分析，须一并保存该目录和 `tests/artifacts/offline-20261006T135059.876430Z-5638d296/`。原始总清单 724 项逐项核对，初次核对零差异；最终核对记录见新索引。

实测部署探针 SHA-256 为 `6c5fed1b9b0964f2392ea4dc144678aee7d67d2fc4b846e4c48077fbd7be970a`，Core 为 `a45e234dae7c9234a7458f102bdfd7952372d5dac789e4c92bd0d93d4c2fcbb5`，Telemetry 为 `0aadd6234903bd55de5fec152f955f2e103b3a0b64cb989dcbc6ede89c1da316`。Probe MVID 为 `ec585f8a-0c3a-4fc0-a8e4-61c5987adbf9`。游戏运行版本 `v1.4.8.119303`，11 模块名单、源码 HEAD、脚本、部署清单、boot marker、遥测、metrics、引擎日志、CBATH 时间序列均按场保留。模块为 Harmony、ButterLib、UIExtenderEx、MBOptionScreen、Native、SandBoxCore、Sandbox、StoryMode、CustomBattle、CBATH、CommandLabProbe；实际规范 ID 以各场 `runtime-identity.json` 为准，没有 CombatBrain/其他 AI Mod。

## 补丁审查与验收

| 修改 | 原因及行为变化 | 验收证据及边界 |
|---|---|---|
| Windows runner / Python 测试 | GBK 控制台不能打印字符时仅控制台替换字符，UTF-8 日志和返回文本保留；异常时回收子进程；fake dotnet 按规范路径比较；碰撞错误断言消除系统语言依赖 | 10 月 6 日最后离线结果：六目标构建、C# 102/102、Python 14+9=23/23、零跳过。此结果 `runtime=NOT_RUN`。仅 GBK 回归验证日志保真，不宣称所有异常/终止平台均已测试 |
| REL 原始 struct 缓存与重发 | 旧投影 Obs 值反转为 Stop 会丢失 Move；现在从被接受的非 Lab Movement 写入缓存原始 `MovementOrder`，含采集时刻，重发同一 struct；记录请求/即时实际类型、位置、目标 | net472 实际宿主构建零警告错误；run09/10 请求 Move 的即时类型和位置匹配，run11 交还后的 Lab 重发被拒。未证明所有 MovementOrder 类型或引用目标长期有效 |
| 加载身份 marker | 用 CLR `Assembly.Location`、MVID 配合部署清单排除装错 DLL | 八场 boot marker 与三处产物哈希吻合；哈希取 Location 路径的磁盘文件，**不是进程内存字节哈希**。OS 模块枚举 unknown 的历史结果保留 |
| 原版驱动 A | 固定实际跑通的播种、模块核验、启动/结束及证据归档流程 | run04–11 原版驱动字节相同，SHA-256 `541c4ce45c46718ec0633e77a8ea69a8196bb3ef72750357672f29377eb6cdc3`，八场 clean/COMPLETE 且无异常。另有恢复的旧 179 行文本，字节身份未验证，不替代实测原版 |
| 驱动 B | 明确缺陷：清理工具异常/错误 envelope、强制退出失败或残留本场 PID 仍可能报 COMPLETE/0；现在报 FAIL/1，保留原始响应/异常 | **修改前验收条件**保存在 `_audit/_phase10-closure-20261007/driver-fix-acceptance.md`。Luna 修改，主 Agent 审查和独立回归：PS 5.1/7 各八例，执行实际 finally 和退出 footer，采集真实子进程退出码；外部 PID 不受影响。只使用模拟游戏/进程控制/MCP，实际启动隔离 PowerShell 子进程；没有重跑游戏 |
| 新离线诊断 | 引用原生产 MetricsCalculator，解释已有全场失败，不另建 verdict | 主 Agent 独立重放 run05/06，全场仍 Oscillating；变更、ABA、寿命和窗口合计守恒。未改生产测量代码、阈值或原 assessment |

详细驱动审查见 [phase10-patch-review-20261007.md](phase10-patch-review-20261007.md)。B 的错误 envelope 检查只识别 JSON-RPC `error` 和 MCP `result.isError=true`，不能宣称验证未知响应语义或任意中断场景。本轮唯一运行行为修补是驱动 B；诊断工具只读日志。

## G3：保持全场裁决，解释失败来源

复现入口：`dotnet run --project tools/phase10_evidence/Phase10Evidence.csproj`。它引用未修改的生产 `CommandLab.Telemetry` 和 Core，使用原 `MetricsCalculator.Compute`，`T_eval=0.5s`。Stable 要同时满足越权生效写入=0、oscillation_frequency≤0.05/s、order_changes_per_second≤0.2/s；否则频率≥0.5/s，或有变更且平均寿命<1s，判为 Oscillating。

| 全场指标 | run05 | run06 | 含义 |
|---|---:|---:|---|
| attempts / rejected | 1649 / 50 | 1642 / 50 | 被拒记录不进入有效变更序列 |
| 有效变更 / 实体域键 | 122 / 63 | 120 / 63 | 接受且 chg=1 |
| A→B→A 三元组 | 37 | 35 | 每键 NewValue 全序列，允许重叠三元组 |
| 原报告 osc/s | 1.1211 | 1.0605 | **超过 0.5/s，是 Oscillating 的触发项** |
| 原报告 changes/s | 3.6965 | 3.6359 | 超过 Stable 的 0.2/s，但单独不是 Oscillating 触发项 |
| 原报告平均寿命 / 样本数 | 15.2366s / 102 | 15.2138s / 100 | 未触发 <1s 分支 |
| unauthorized_writes | 0 | 0 | G1 通过不足以推导 G3 |
| 原全场裁决 | Oscillating / G3 FAIL | Oscillating / G3 FAIL | 原样保留 |

原始 t 只打印 F4、时长 F2，不能声称打印的 33.00 是精确 dump 时刻。以 33.00 重放，频率为 1.1212121/1.0606061，变更率为 3.6969697/3.6363636，寿命为 15.2348382/15.2120040（完整输出为准），全场裁决相同。工具另用原打印的时长和三项频率构造可容区间：run05 `[33.0041260161,33.0041920728]`，run06 `[33.0042622130,33.0043285519]`，中点只作重建辅助。用中点重放率值按 F4 与报告吻合；run06 寿命可能在末位舍入不同，原始端点四位小数误差使平均误差上界约 0.0000785s，未篡改端点来强求一致。详见 [g3-summary.json](evidence/phase10-closure-20261007/g3-summary.json)。

### 按实体、域归因

下表给出所有产生 ABA 的键与目标所有域。**63 键完整变更/ABA/寿命计数**见 [run05-keys.csv](evidence/phase10-closure-20261007/run05-keys.csv) 和 [run06-keys.csv](evidence/phase10-closure-20261007/run06-keys.csv)，无 ABA 的键也保留。三个数字顺序是有效变更 / ABA / 寿命样本。

| 实体与域 | run05 | run06 |
|---|---:|---:|
| T0F0 Movement | 9 / 7 / 8 | 9 / 7 / 8 |
| T0F1 Movement | 9 / 7 / 8 | 9 / 7 / 8 |
| T0F2 Movement | 9 / 7 / 8 | 9 / 7 / 8 |
| T0F3 Movement | 11 / 7 / 10 | 11 / 7 / 10 |
| T0F4、F5、F6、F7 Movement（每个） | 3 / 1 / 2 | 3 / 1 / 2 |
| **目标 T1F0 Movement** | **3 / 1 / 2** | **3 / 1 / 2** |
| 目标 T1F0 Facing | 2 / 0 / 1 | 2 / 0 / 1 |
| **目标 T1F0 Arrangement** | **4 / 2 / 3** | **3 / 1 / 2** |
| T1F3 Movement | 3 / 1 / 2 | 2 / 0 / 1 |
| T1F4 Arrangement | 3 / 1 / 2 | 3 / 1 / 2 |

T0F0–3 Movement 贡献 28/37、28/35；两场各有 **24 个 ABA 的第三条记录已在 t=0.0050**，每个上述键六个，每组三条均为 tick=1、t=0.0050，是初始化同 ProbeClock 时刻的序列。两场各 34 个寿命样本为打印的零间隔；不能解释为真实墙钟下零寿命，也不能解释成 HOLD 窗口持续振荡。余下变更序列和行号已完整保留。目标分别贡献 3/37、2/35；其他键贡献 34/33，按原全场分母已足以超阈值。但“G3 全部由其他编队造成”不成立，应改为：**主要贡献在其他键和接管前初始化，目标的正常接管/交还及未接管的 Arrangement 也被原公式计入**。

### 窗口、边界与寿命

窗口基于本场 AUTH Acquire、首条被接受的 Lab HOLD、AUTH Release，不用脚本标称 3/3.5/28 秒：run05 `3.0046 / 3.5045 / 28.0060`，run06 `3.0057 / 3.5020 / 28.0060`。半开窗口；恰在首 HOLD 的有效变更计入 HOLD。Acquire、Release 记录时刻没有有效变更，首 HOLD 只有目标 Movement 的第 170 行，因此当前边界分桶不引入额外变更歧义。

| 窗口（第三事件归属） | run05 变更 / ABA | run06 变更 / ABA |
|---|---:|---:|
| 接管前 `[0,Acquire)` | 112 / 34 | 112 / 34 |
| Acquire 至首次 HOLD | 0 / 0 | 0 / 0 |
| 首次 HOLD 至 Release | 8 / 1 | 6 / 0 |
| Release 至 dump | 2 / 2 | 2 / 1 |

口径是**先建完整每键序列，再给完整三元组按第三事件归属并标记跨窗口**，不是截日志套全场公式。run05 HOLD 桶的一次回归来自 T1F3 Movement（原行 39→658→1386，`.0050→13.1611→27.6707`），跨接管前/HOLD；不能说三条都在 HOLD。目标 Movement 两场都是初始化 Obs0→Lab HOLD→交还后 Obs0，分别原行 `27→170→1398`、`27→170→1395`，跨三个窗口，按第三事件计入释放后。目标 Arrangement run05 有接管前一次及跨接管前/释放后一次，run06 只有接管前一次；此域没有取得租约。

起始状态逐键延续边界前最后一个被接受且变化的 NewValue，不把 PrevValue 人工插成第一条，不重置窗口。无先前记录标为 UNOBSERVED；完整携带状态表见两个 `runXX-window-start-states.csv`。复现方法：对 changes.csv 每键按原行号排序，对每个上述边界选 `t<边界` 的最后一条；边界自身记录另列，未观测初态不自行补全。该表是诊断派生物，未进入 MetricsCalculator。

寿命沿用原规则：每键 N≥2 时只取 N−1 个相邻变更间隔，**最后一条到 dump 的尾段不加入均值**；N=1 时取 dump−首次变更的右删失样本；N=0 无样本。run05 有 59 个完成间隔+43 个单变更右删失，run06 为 57+43。目标 Movement 样本为 run05 `3.4995s、24.5216s`，run06 `3.4970s、24.5228s`；目标 Arrangement 为 run05 `2.4652、0.0430、26.0111s`，run06 `2.4952、0.0428s`。寿命跨窗口按几何重叠拆分，既不新增样本也不重新计算局部 grade；多个键的重叠秒数之和不是墙钟时长。CSV 中 G17 双精度表示展开不增加原 F4 时间精度。

每条变更、ABA 三元组、寿命样本都有原文件行号和窗口信息，详见同目录的 `runXX-changes.csv / triples.csv / lifetimes.csv`。全场有效变更、ABA、寿命计数及跨窗重叠秒数守恒均通过。目标局部稳定性只能辅助诊断，不能替换 G3。

## H3：run07 同实体、同场分段

目标为本次 census 的 T1F0，非玩家步兵 20 人。ProbeClock 上 setter 输入记录：初始 True `t=0.0050`（行 5134）、False `3.0039`（5476）、True `28.0049`（6647），与 AUTH Acquire/Release 同打印时刻。该 hook 记录调用输入，不是独立的字段赋值插桩；CENSUS/AUTH/写入是分段输出，不能按跨流文件位置推断同刻先后。

| Native Movement 时段 | attempts | accepted / rejected | effective / OME=true |
|---|---:|---:|---:|
| t<3.0039 | 4 | 4 / 0 | 1 / 4 |
| t=3.0039 边界 | 0 | 0 / 0 | 0 / 0 |
| 3.0039<t<28.0049 | 0 | 0 / 0 | 0 / 0 |
| t=28.0049 边界 | 4 | 1 / 3 | 1 / 1 |
| t>28.0049 至 dump | 10 | 10 / 0 | 0 / 10 |

释放同刻第 1298/1300/1303 行 owner=Lab，NotOwner 拒绝且 OME=0；第 1304 行 owner=Native，接受且 OME=1。边界单列，不能把四条统统归为“关闭期间”或“释放后”。之后第一条在 28.0256，后续是同值写入而非十次有效变更。逐条索引和原始 SHA 见 [h3-same-run.json](evidence/phase10-closure-20261007/h3-same-run.json)。

独立 CBATH 1Hz 真实字段采样支持旗标状态发生切换：MissionClock True/Move 的上一样本 2.036，首 False/Move 3.041；最后 False/StandYourGround 27.127，首 True/Move 28.127。首次样本 1.033 是 True/Move。MissionClock 与 ProbeClock 的精确偏移没有记录，这些是各自时钟上的切换区间，不是精确赋值时刻。

同场支持这套 **Acquire+HOLD+AI 旗标操作组合下**，目标已观测 Native Movement 在关闭区间消失，交还边界及之后恢复。没有隔离其他干预因素，不能单独归因于一个开关。H2 `[3.5,28)` 的 49 次与 H3 的 0 次保留为跨场描述，不能单独作为因果证明。已装 DLL 的 Formation.Tick IL 门控是静态证据（原 `current-formation-tick-il.txt`），实际 AI.Tick/Behavior 选择调用次数仍 `NOT_VERIFIED`，本轮未新增插桩。

## H2b 与 REL 的边界

| 命题 | 当前结论 |
|---|---|
| H2b 本场未观察到 OrderEnum 采样计数缺口 | 成立：目标 Arrangement sampled=2、port=3、unobserved=0，X5=0 |
| 全部写入路径覆盖完整 | 证据不足：采样的 enum 变化不是独立完整写入分母，零缺口不能排除未采到的路径/同值写入 |
| Arrangement 取得并保持独占 | 证据不足：此场只取得 Movement 租约，未建立 Arrangement 独占意图窗口及保持判据 |
| REL 重发动作有效 | run09/10 成立：原 Move struct 重发被接受，请求/即时实际类型及位置匹配；恢复后 20 次 Native Movement 允许写入 |
| 不重发也自然恢复 | run08 成立：None 组交还后约 23ms 出现 Native Move，随后 20 次允许写入；不把 None 的零计数本身当恢复证据 |
| 错误顺序被拒 | run11 成立：t=28.0049 Lab 在 owner=Native 时重发 NotOwner/OME=0，实际保持 StandYourGround；t=28.0251 Native Move 被接受。REL=1 为预期失败反例，不能改成普通 PASS |

run09 t=28.0016 重发采集于 2.5104 的 Move，位置 `919.2695,718.495544`；run10 t=28.0019 重发采集于 2.5272 的 Move，位置 `1040.26379,658.7628`。run10 Reset 的 BehaviorCautiousAdvance 权重 1→0 只证明观察到重置效果，没有证明带来额外恢复收益。上述三类结果不能推出重发/Reset 普遍必要，也不能宣布原生 AI 完整内部状态恢复。原 assessment 和 REL=0/0/0/1 不变。

## 留给下一阶段的条件式前提

可引用的已验证前提仅包括：此部署/版本/模块集上装置门禁通过；此目标 Movement 的 G1=0、G2=100%；原始 struct 的两个 Move 重发动作有效；同场 AI 旗标组合期间观察到写入消失并在交还恢复；错误顺序反例能被识别。必须随附宿主版本、模块集、census 身份、实际写入口覆盖和具体窗口；不能外推任意版本/模组/域或内部状态。

必须继续附带：G3 全场失败及初始化贡献；H2b 完整覆盖与 Arrangement 独占未验证；H3 没有实际 Tick/Behavior 调用计数或孤立因果实验；REL 必要性/完整状态恢复未验证；B 仅通过隔离回归、没有新增游戏验收；H1 0.15s 是历史场次。本报告只整理这些前提，不进行架构定型或下一阶段开发。

本轮停止并交回验收。并行出现的三个 Phase 11 文件列为工作区外来改动，原样保留，不纳入本轮提交、结论或授权范围；本轮的冻结清单只包含明确列出的 Phase 10 文件。
