# Phase 10 本地构建、部署与实测交付（2026-10-06）

> **2026-10-07 后续分析**：本文保留10月6日的场次与原始结果；事后源码冻结与细化归因见 [Phase 10收口报告](phase10-closure-20261007.md)。全场G3 FAIL不变；目标也有ABA贡献。H3新增同场前/关闭/交还流量证据，但不声称实际Tick调用数或孤立开关因果；H2b完整覆盖、Arrangement独占及REL必要性/完整恢复仍未验证。

本轮在 `E:\MBmod\TacticalCommandLab` 接续云端提交，完成 census、H2/H2b/H3 和四组 REL 的本地采集。**有失败和证据不足项，不能汇总为全部实验 PASS。Phase 11/12 未推进；本轮交付后停止，等待用户决定后续工作。**

## 1. 源码和环境身份

- 初始分支 `master`，HEAD `c86167a887cb3c1691dd12fa2d19385d937e8364`，跟踪工作树干净。云端提交已在本地对象库，且是该 HEAD 的后继；没有发现后续本地提交。
- `git ls-remote` 核验云端分支及 PR #1 head 均为 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0`，远端 master 仍为 `c86167a`。实际 origin 为 `https://github.com/Lzq061122/TacticalCommandLab.git`。
- 创建本地分支 `codex/local-phase10-verification-20261006` 接续 `adab4cf`，未合并、重置、改写 master，未提交或推送。最终测量源码是 **adab4cf + 本轮未提交修补**，每场保存 `source-head.txt`、`git-diff.txt`、当前驱动及 DLL 哈希，不能仅用 HEAD 指代最终二进制。
- 主 Agent 派发、审查、构建部署和验收；代码修补由 GPT-6 Luna 子 Agent 完成。未改 Core、Telemetry、Simulation、C# 测试、实验时间线或阈值。

| 项目 | 本机事实 |
|---|---|
| 终端 | PowerShell 7.6.5；最终驱动也通过 Windows PowerShell 5.1.26100.9444 解析 |
| .NET SDK | `C:\Program Files\dotnet\dotnet.exe`，10.0.400 |
| Python | `C:\Python314\python.exe`，3.14.7 |
| Bannerlord | `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`；运行时 `v1.4.8.119303`，Steam buildid 24573425 |
| Harmony | `Modules\Bannerlord.Harmony`，模块 v2.4.2.248；0Harmony.dll 文件版本 2.4.2.0 |
| 游戏 CLR | `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\clr.dll`，文件版本 4.8.9345.0 |
| 驱动 | 复用 `_audit\_tcl-step5.ps1`、CBATH 的 `tools\mcp-http.ps1` 及现有 GABS；配置入口 `bannerlord148-nocb` |

每场真实 `active_modules` 均为：

```text
Bannerlord.Harmony,Bannerlord.ButterLib,Bannerlord.UIExtenderEx,Bannerlord.MBOptionScreen,Native,SandBoxCore,Sandbox,StoryMode,CustomBattle,CombatBrainAutonomousTestHarness,CommandLabProbe
```

没有 CombatBrain、CAI、RBM 或其他列入隔离清单的 AI Mod。CBATH 是驱动，框架模块保留原先已跑通的配置。场景为现有 `battle_terrain_biome_096`，双方混合兵种；**被控目标是非玩家步兵 T1F0（初始 20 人）**，不称整场纯步兵。目标由本次 census 的 projection、HOST 流量及实际 formation 快照选定，不沿用历史 T1F3。最终 census 的 T1F0 Movement `sampled_changes=1 / port_changes=1 / unobserved=0`，真实存在且有 Native Movement 流量。

## 2. 先验收装置，再测实验

修补前分别保存了验收条件：`offline-fix-acceptance.md`、`driver-acceptance.md`、`runtime-identity-acceptance.md`、`rel-raw-order-acceptance.md`，均在 `_audit/_local-phase10-20261006/`。

1. **Windows 离线入口**：首次六项构建通过，但输出中断后清理临时 DLL 报 WinError 5；UTF-8 环境重跑区分出入口测试的 Windows 路径问题。修补控制台转发与子进程回收，并以规范化路径识别 fake-dotnet。增加一项 GBK 控制台/UTF-8 日志回归。锁文件复现支持清理故障机制，但旧次具体触发点仍是推断，未冒充已证实的完整根因。
2. **驱动证据归档**：拒绝已运行游戏和非空结果目录；精确校验 11 个活动模块；启动前播种，记录原脚本与实体映射；每场结束 mission、停止本轮游戏，再启动下一场；保存实际状态、原始响应和本次 PID 的引擎日志。旧驱动是本地忽略、未跟踪文件，修改前漏做原件字节备份；收尾从本会话初始完整读出恢复了 179 行旧版文本，保存为 `driver-before-session-recovered.ps1`，解析通过。它是文本恢复副本，不声称与原件逐字节相同；当前修订版逐场保存为 `driver-script.ps1`。不将旧驱动误称为 Git 中的既有版本。
3. **部署身份**：`Get-Process.Modules` 实际未枚举出这三份托管程序集。现有 boot-marker 增加加载路径、MVID、磁盘 SHA-256，空枚举明确记为 unknown。新版 marker 的三份路径及哈希均与部署清单匹配。boot 时 patchedCount=0 是延迟 patch 的既定时序；随后 patch-marker 的 count=7、failure=(none)、missionCurrentNull=no。
4. **REL 原始命令缺陷**：本次 telemetry 的 Native `raw=Move` 被中性投影为 Obs，旧缓存却经 `ToMovement(Obs)` 变成 Stop，不能验收“原样重发”。修补为仅在 Native Movement 写入被接受时缓存 Prefix 的完整 `MovementOrder input`，恢复直接重发该 struct；Prefix 参数名和签名未变，已用本机 sigscan 核对。增加现有 NOTE 的请求/即时实际类型、位置和结果，诊断不参与判据。

### 本机最终回归

`tools/run_offline.py` 在**未预设 Python UTF-8 环境变量**的终端通过。最终报告：

`tests/artifacts/offline-20261006T135059.876430Z-5638d296/`

- 六项构建 PASS；C# D0 15/15、E1–E11 51/51、E12 10/10、MEASURE 26/26，合计 **102/102**。
- Python offline_runner **14/14**、offline_telemetry **9/9**，合计 **23/23，零跳过**；比云端 22 项多上述编码回归一项。
- D0-S1 replay_hash 仍为 `d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199`。
- 报告源码摘要 `313469e1b6519d2a88ae12eed08fe9db61d6a227ef7f178e1704af9c526a666b`；HEAD 为 adab4cf，working_tree=dirty。
- **离线报告 runtime=NOT_RUN**；它不代表下文的游戏实测未运行。下文各场 assessment 的 runtime=RUN。

依赖本机游戏 DLL 的 BannerlordProbe 单独构建为 net472，最终 **0 warnings / 0 errors**。构建后确认游戏已退出，逐 DLL 独占打开检查无占用，备份旧游戏部署，再复制 Probe/Core/Telemetry 和 SubModule.xml 到游戏 Modules。每场的 `deployment-manifest.json` 保存构建、deploy 和游戏目录的 SHA-256、长度、时间戳；三处相等。

| 最终游戏 DLL | SHA-256 |
|---|---|
| CommandLab.BannerlordProbe.dll | `6c5fed1b9b0964f2392ea4dc144678aee7d67d2fc4b846e4c48077fbd7be970a` |
| CommandLab.Core.dll | `a45e234dae7c9234a7458f102bdfd7952372d5dac789e4c92bd0d93d4c2fcbb5` |
| CommandLab.Telemetry.dll | `0aadd6234903bd55de5fec152f955f2e103b3a0b64cb989dcbc6ede89c1da316` |

最终 Probe MVID 为 `ec585f8a-0c3a-4fc0-a8e4-61c5987adbf9`；最终八场的 boot-marker 均为这一身份。

## 3. 最终部署版实测结果

各场完全退出后重启，实际播种脚本归档；只将动作实体 N0 替换为本次 T1F0，未改 policy、时刻或阈值。最终八场 P0 均为 **7 PASS**、P0c 均为 **29/29 PASS**、X5 均为 **0 PASS**。下表 P0b 也全部通过。前期排障场 run01–run03 单列保留，不混进最终同一部署版对照。

| 归档目录 | P0b | 声明判据 / 记录值 | 分类 |
|---|---:|---|---|
| run04-census-final | 2040 | 四项门禁全通过 | **通过** |
| run05-h2-suppression | 2086 | G1=0 PASS；G2=100% PASS；G3=Oscillating FAIL | **失败（G3）；核心夺取 G1∧G2 通过** |
| run06-h2-coverage | 2077 | G1=0 PASS；G2=100% PASS；G3 FAIL；目标 Arrangement sampled=2、port=3、unobserved=0 | **失败（G3）；完整覆盖率证据不足** |
| run07-h3-route-b | 1992 | H3-a=60 PASS；H3-b=0 PASS；G2=100% PASS | **通过现有 H3 判据** |
| run08-rel-none | 2318 | REL=0 PASS；没有恢复动作 | **通过对照判据，0 本身是平凡值** |
| run09-rel-reapply | 2308 | REL=0 PASS；Move 重发 accepted，即时实际类型和位置相同 | **通过** |
| run10-rel-reapply-resetweights | 2283 | REL=0 PASS；Move 重发 accepted，即时实际类型和位置相同 | **通过** |
| run11-rel-after-release | 2301 | REL=1 FAIL；恢复写入 NotOwner / rej=1 / OME=0 | **预期失败；反例辨识验收通过** |

### H2 / H2b 的结论范围

H2 的目标 HOLD 窗口约 24.50 秒，3503 次采样全部匹配；同实体 Movement 在 `[3.5,28)` 的 **49 次 Native 尝试全部被拒**，越权生效写入为 0。支持该目标、该窗口的 G1∧G2 成立。

G3 沿用**全场** MetricsCalculator：其他编队和域也进入 grade。不能把 target hold=100% 写成全场稳定，也不能为了 PASS 改成目标局部 grade。H2/H2b 的 G3 FAIL 原样保留。

H2b 没观察到 OrderEnum 采样计数缺口；本轮不支持“必产生 unobserved>0”的预测。sampled/port 计数没有提供全部真实写入的独立分母，**不能据此报告 100% 完整写入口覆盖率**。脚本只 acquire Movement，Arrangement 保持 Native 所有，亦不能推出 Arrangement Full 接管或阻断已验证。

### H3 的证据与代价

同一目标及 `[3.5,28)` 窗口，Native Movement 尝试从 H2 的 49 次降为 H3 的 **0 次**。目标 CENSUS 记录 t=3.0039 `isControlledByAI=False`，t=28.0049 恢复 True；早期实际快照确认 T1F0 为 AI-controlled=false、StandYourGround。global metrics grade 仍为 Oscillating；现有 H3 verdict 不包含 G3，故与 H2 分开记录。

当前安装游戏 DLL 的静态 IL（`current-formation-tick-il.txt`，SHA-256 `19387f31557ff840d14f378f6bbdf1d58fcff406ab9ff2294dfbb3e49a50b87e`）显示 FormationAI.Tick 的条件为 HasTeamAi 且 `(IsAIControlled || IsPlayerSergeant)`。普通非 PlayerSergeant 路径关闭 AI 控制会跳过整个 Tick，包括 Behavior 选择；PlayerSergeant 是例外。这是**控制模型的代价**，不是仅增加一种写入拦截。实际 Tick/FindBestBehavior 调用计数未直接观测，静态路径和实际旗标不冒充调用计数证据。

### REL 原始命令与反例

run09 t=28.0016：缓存 capturedAt=2.5104、rawType=Move、position=`919.2695,718.495544`，请求 accepted；即时 actualType=Move、actualPosition 相同。run10 也逐字段匹配（`1040.26379,658.7628`）。这验证了本轮缓存修补和重发动作。源码保存完整原始 struct；诊断只打印关键字段，不声称日志串覆盖了 struct 全部字段。

run08 无恢复对照在 t=28.0234 已出现 Native Move 生效；run09 释放后也有 20 次 `[28,38)` Native Movement 全部放行。**本场不重发也会自然恢复，没有证据说明 Reapply 对持续恢复是必需的。** run10 Reset 后观测权重从 1 变为 0，但 Move/AI 控制继续；失败数 0 不证明重置改善恢复，亦不证明所有内部状态已恢复。

run11 t=28.0049 恢复请求被 NotOwner 拒绝，OME=0，即时 actualType 仍为 StandYourGround；REL=1 FAIL。之后 t=28.0251 Native Move 放行。反例确认装置能检出错误的恢复顺序，不能把随后原生自行恢复误称为那次被拒的恢复动作成功。

## 4. 归档、历史隔离与剩余未验证项

本轮根目录：`_audit/_local-phase10-20261006/`。每场保存 HEAD/diff、当前驱动、脚本原文/实际种子/映射、运行时版本/模块列表/PID、boot/patch marker、部署三处清单、原始 telemetry/metrics、CBATH 原始响应与秒级时间序列、当次 PID 的 rgl/error/watchdog 日志及 cleanup 状态。每场 `assessment.json` 和总 `experiment-results.json` 记录判定、输入哈希与范围限制；`artifact-manifest.json` 保存文件哈希。实际脚本均在游戏启动前播种。

复用 `tools/recompute_telemetry.py` 对最终八份 telemetry 重算，均成功解析，X5 计数缺口下界均为 0，与宿主记录相符。它自己的 runtime=NOT_RUN 表示**这次重算没有启动游戏**，不改变原始实测的 runtime=RUN，也不独立认证完整覆盖率。

- run01：门禁通过，但托管 DLL 精确加载身份不足，且收尾归档 rename 错误；保存为排障证据，不作最终放行。
- run02：身份/门禁有效，但为 REL 缓存修补前的部署版 census。
- run03：修补前 H2，G1/G2 通过、G3 FAIL，作为早期旁证；最终结果用 run05。
- 2026-10-04 H1 约 0.15s 的记录是历史实测，本轮未重跑 H1，未改写为本轮结果。云端离线 NOT_RUN 报告也与新游戏数据分开。
- 尚未验证：全部写入路径的完整覆盖率、多实体/多场景泛化、H3 的实际 AI.Tick/Behavior 选择调用次数、各恢复动作的普遍必要性和完整状态恢复。恢复原方法若抛异常的计数路径缺口目前仅有源码风险，没有本轮发生证据；本轮无 probe crash/driver exception，不以未来发生异常时的 0 计数判成功。

最终八场的 driver COMPLETE、cleanup=clean、部署身份一致。未发现本轮 probe crash 或 driver exception；保存的引擎 error 日志仅有启动头，watchdog 的 “Waiting for an exception event” 不当作发生异常。最终 Bannerlord/GABS 进程均已退出。

**下一步只由用户决定：处理 G3 的范围问题、补覆盖率/行为选择证据或选择后续阶段。本轮不继续开发，不进入 Phase 11/12，不做 CombatBrain 集成。**
