# Phase 11 开发反馈文档

> 本文件是**交接事实记录**，不代表开发完成或审查通过。按 `docs/phase11-handoff-template.md` 填写。
>
> **2026-10-07 整改后状态**：Codex 已给出审查结论 **部分通过，需文档整改后复审**（`_audit/2026-10-07-phase11-review.md`）。本文件已按该报告的六项整改要求修订，**当前状态为"待复审"**，最终验收结论由 Codex 给出。

## 1. 交付结论

- 开发状态：**文档整改完成，待 Codex 复审**（首轮审查结论为"部分通过"）
- 本轮范围：与派单一致。三项任务全部交付——①证据复核报告 ②`docs/07-final-architecture.md` ③最小同步 + 本文件。
- 未完成验收条件：**指标、代码差距两项首轮未通过，已完成整改待复审；"事实"一项亦因正文事实错误被要求整改。** 逐项现状见 §4。**最终是否通过由 Codex 判定，开发组不填通过。**
- 正式架构文档：`E:\MBmod\TacticalCommandLab\docs\07-final-architecture.md`
- 证据复核报告：`E:\MBmod\TacticalCommandLab\_audit\2026-10-07-phase11-evidence-review.md`
- 本轮游戏运行：**NOT_RUN**。未启动游戏、未部署 DLL、未修改运行时代码、未新增实验。
- 引用的原始游戏实测：八场均为 **Phase 10 既有实测**，与本轮执行分开记录——`run04-census-final`、`run05-h2-suppression`、`run06-h2-coverage`、`run07-h3-route-b`、`run08-rel-none`、`run09-rel-reapply`、`run10-rel-reapply-resetweights`、`run11-rel-after-release`；八场 `assessment.json` 的 `runtime` 均为 `RUN`，`source_head` 均为 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0`。本轮仅**只读**读取其归档产物。
- 最终审查：**首轮已由 Codex 执行，结论"部分通过"；整改后待复审。开发组不填通过。**

## 2. 开发身份与工作树

| 项 | 真实记录 |
|---|---|
| 工作区 / 开发环境 | `E:\MBmod\TacticalCommandLab`（Windows 本地）。原始八场证据在 `_audit\_local-phase10-20261006\`，**该目录未被 git 跟踪**；跟踪副本在 `docs\evidence\phase10-closure-20261007\`。两处本轮均可访问，已逐项核对。 |
| 开工分支 / HEAD | 分支 `codex/local-phase10-verification-20261006`；HEAD `f8b1f173f05bf054b42eb2e31527deb1a83aa613` |
| 交付分支 / HEAD / 提交范围 | **未提交、未推送**。分支与 HEAD 同开工值 `codex/local-phase10-verification-20261006` @ `f8b1f17`；改动包含**四份 tracked 文档修改**（`HANDOFF.md`、`docs/05`、`docs/06`、`docs/07a`）与若干未跟踪新增文件。 |
| 设计依据 | `docs/phase11-design.md`；**开工基线** SHA-256 `042ef0fbc5ed3eaa452cba67bd0b442ed442c33f00c531fe391519b6a3759a74`（blob `f84440e6e06f143ef97ee1996e5918ac6c5b3507`）；**整改后当前版本** `dd13fbaf97d1642ca14639a9f36320ef7ef096793b7c85a8de465f0aecae54f5`（旧值**不是**当前文件哈希） |
| Prompt 依据 | `_dispatch/2026-10-07-phase11-development-prompt.md`；SHA-256 `aadc20923744f7dd0ce603c107391cedcdc18919429b7be2029f871589707cfd`（blob `a018052472f98e5b781c611484f65d34e3967e8a`），126 行 |
| 开工已有他方改动 | 三项未跟踪文件（均在本轮之前存在）：`_dispatch/2026-10-07-phase11-development-prompt.md`、`docs/phase11-design.md`、`docs/phase11-handoff-template.md`（开工基线 SHA-256 分别为 `aadc2092…`、`042ef0fb…`、`ba79c8d3574f7c39f16f99f9e853f7d6d66633fc84ccb8b5673f98a9676231bf`；其中设计文档在整改阶段被更新为 `dd13fbaf…`）。开工时 `git status --porcelain` **无 tracked 修改**。 |
| 本轮自己的未提交改动 | 新增：`docs/07-final-architecture.md`、`HANDOFF.phase11.md`、`_audit/2026-10-07-phase11-evidence-review.md`、`_audit/verify_phase11_evidence.ps1` 及 `_audit/2026-10-07-phase11-evidence/`。修改：**四份 tracked 文档**——`HANDOFF.md`（3 处状态行）、`docs/05-experiment-plan.md`（§1.5）、`docs/06-experiment-results.md`（§5）、`docs/07a-architecture-abc-comparison.md`（§2.2a/§2.2b）。**唯一写入方 = 本轮开发组**（根 `HANDOFF.md` 顶部审查块由 Codex 写入）。 |
| 实测源码身份 | **`adab4cf008a8dd6e83f8e12c03f1423483cb98f0` + 四文件 diff**（八场 `assessment.json.source_head` 实录），**不得替换为交付 HEAD `f8b1f17`**。部署 DLL 身份在 `runtime-identity.json` / `deployment-manifest.json` 中复核，八场 `dll_hashes_match=true`。 |
| 八场 `game_pid`（更正） | run04=`19180`、run05=**36348**、run06=**39760**、run07=`38156`、run08=**40564**、run09=**38548**、run10=**18632**、run11=`19252`。早期版本误把 run05/06/08/09/10 记为 `19180`，**已按逐场 `assessment.json` 更正**。 |

> **设计文档偏差登记**：`docs/phase11-design.md` 记载的匹配源码冻结版本为 `e57e157`，而本轮实测开工 HEAD 为 `f8b1f17`（`e57e157` → `0a0a594` → `f8b1f17`）。**以实际记录为准**；八场实测身份 `adab4cf` 不受影响，且**不得**用交付 HEAD 倒写实测身份。

## 3. 逐文件改动

| 文件 | 改动与原因 | 对应设计位置 | 是否改变运行时行为 |
|---|---|---|---|
| `docs/07-final-architecture.md`（新增） | 完整落实设计文档，含 9 项覆盖要求与 §6 七问逐问区分 | 全篇 | 否（文档） |
| `_audit/2026-10-07-phase11-evidence-review.md`（新增） | 八场输入与实际身份核对、只读重算复核、失败保留、设计 §5.2 六项证据列 | §5.2 | 否（文档） |
| `HANDOFF.phase11.md`（新增） | 本文件，按模板填 | 派单 §5 交回要求 | 否 |
| `HANDOFF.md` | 仅 3 处：顶部加 Phase 11 状态引用块；"当前交接"段改为 Phase 11 状态并**原样保留** Phase 10 段为历史记录；状态表 Phase 11 行 `⬜ 未开始` → 🟨 待审查。**未整体重排** | 派单 §5 任务三 | 否 |
| `docs/05-experiment-plan.md` | §1.5 指标表补入 `intent_hold_share` 精确定义；稳定性分级之后新增 G1/G2 硬门限表；链接 Phase 11 与 E12 证据 | 派单 §5 任务三 | 否 |
| `docs/06-experiment-results.md` | §5 新增第 7、8 条：hold 的角色（本轮非判据 → Phase 8/11 成判据）、`lab_delivery_rate` 不得用作控制判据 | 派单 §5 任务三 | 否 |
| `docs/07a-architecture-abc-comparison.md` | 仅新增 §2.2a（C 的保证边界）与 §2.2b（A 的实验适用范围）两节。**未改动任何既有结论、矩阵数值或评分** | 派单 §4 限定 3 | 否 |
| `docs/phase11-design.md`（**整改阶段**） | 按审查报告补充：§4.1 差距表新增"先仲裁后许可"一项（原设计遗漏，审查方指出）；§5.1 `intent_hold_share` 口径更正（`ProbeRuntime.cs:378-384` 实际只查 `LastAttempt is not null`，未查 `Rejected`）；§3.2 职责行注明该顺序是最终契约、现有实现相反 | 审查报告 §3 第 2、3 项 | 否（文档） |
| `_audit/verify_phase11_evidence.ps1`（新增） | 本轮核对脚本，读 manifest/assessment 并复算哈希 | 派单 §5 任务一 | 否 |

**本轮未触碰**：`src/`、`tests/`、`deploy/`、`tools/`、`docs/10-harmony-runtime-experiments.md`、`docs/evidence/phase10-closure-20261007/` 下任何 tracked 文件。

## 4. 逐项验收（整改后状态，非通过声明）

> 下表"结果"列为**开发组自评**。首轮 Codex 审查（`_audit/2026-10-07-phase11-review.md` §4）判定：范围、架构（主方向）、失败保留、同步四项**可接受**；**事实、指标（部分）、代码差距需整改**。下表已标注整改对应项。

| 条件 | 首轮审查 | 整改后自评 | 可核查证据与整改位置 |
|---|---|---|---|
| 范围 | 可接受（含 1 项输出目录违规需限定损失评估） | **通过** | 本轮仅产出文档与只读核对产物，`src/`/`tests/`/`deploy/`/`tools/` 无改动；未启动游戏。**输出目录违规的损失评估已按审查要求限定**：报告 §3.2、本文 §7。 |
| 架构 | 主方向可接受 | **通过** | `docs/07-final-architecture.md` 覆盖设计 §1–§7。**整改**：§3.2/§10.1 第 9 项撤销"伪预测/不可判伪"理由（整改 ①）；§4.1/§4.3/§5.4 差距 7/§5.5 补"先仲裁后许可"（整改 ③）；§6.1 补有效意图窗口与 HoldMeter 实际条件（整改 ②）。 |
| 事实 | **不通过** | **通过**（8 处已逐项更正） | manifest 491/491（**更正为 61×5+62×3**）；16/16 哈希匹配。**整改 ④ 八处**：① 八场 `game_pid` 逐场更正；② run04/09/10 `pairs=0`；③ 计数窗口 `rej=49/50` 区分；④ IL 措辞改为"跳过 `FormationAI.Tick`，`Formation.Tick` 仍继续簿记"；⑤ 窗口时长改为 `simulation_seconds=33.00`；⑥ 恢复缓存是 Native 原始 `Move` 而非 Lab HOLD；⑦ 算术更正；⑧ 本文件身份与 G3 窗口更正。 |
| 失败保留 | 可接受 | **通过** | H2/H2b G3 FAIL 原样保留；H3 原判据 PASS + 独立全场 grade；REL=0/0/0/1 并记"预期失败、反例辨识成功"。**整改 ⑤ 额外**：明确 run09/run10 的 `EVIDENCE_INSUFFICIENT` **不是 REL 判据失败**，只有 run11 是反例。 |
| 指标 | **部分通过** | **通过** | `docs/05` §1.5 已把 `pre_acquire` / `acquire_to_first_accepted_lab_hold` 明确划为**事件归因窗口**并声明"不参与 hold 分母"；有效意图窗口定义为"首个被接受的 Lab 写入 → 归属结束"；补 E12 仿真窗口与 Probe 采样两套实现的口径差异（整改 ②）。 |
| 代码差距 | **不通过**（漏列、需补两项） | **通过** | 原五项保留；**新增差距 7（先仲裁后许可）与 §5.5 最小复现**（整改 ③）；`intent_hold_share` 被拒命令 Open 未检验登记为待验证（整改 ②）。修 Core 未做，属独立授权任务。 |
| 同步 | 可接受 | **通过** | 仅改 `HANDOFF.md`（3 处）、`docs/05`（§1.5）、`docs/06`（§5）、`docs/07a`（新增 2 节）；整改阶段另按审查要求修订 `docs/phase11-design.md`（首轮遗漏项）。未重排 HANDOFF、未 reset/clean/stash、未强推。 |
| 交接 | 需更新为待复审并列出逐项对应 | **通过** | 本文件已更新为"部分通过，文档整改待复审"，并列出六项整改的逐项对应位置（§4 本表、§7）。标注**待复审**，未自行宣称通过。 |

## 5. 实际执行的检查

| 执行内容/准确命令 | 环境与时间 | 退出码/结果 | 输出路径与 SHA-256 | 检查边界 |
|---|---|---|---|---|
| `pwsh _audit\verify_phase11_evidence.ps1`（读 `artifact-manifest.json` 逐条复算 `length`+`sha256`） | Windows / 2026-10-07 本地 | 0；**491/491 ok，0 missing，0 mismatch** | `_audit\2026-10-07-phase11-evidence\manifest-verification.json` | 只证明归档产物自洽；**不证明**游戏内行为正确 |
| 同上脚本核对八场 `assessment.json` 与 `metrics-*.txt`/`telemetry-*.txt` 实算哈希 | 同上 | **16/16 匹配**；`runtime=RUN`、`instrument_gates_pass=true`、`dll_hashes_match=true` | `_audit\2026-10-07-phase11-evidence\assessment-verification.json` | 同上；`script_sha256` 指向**播种后**的 `script-seeded.txt`，非 `script-source.txt` |
| `python tools\recompute_telemetry.py <单场 telemetry 绝对路径>`（逐场各调用一次，stdout 重定向到文件） | 同上 | 八场 **exit=0** | `_audit\2026-10-07-phase11-evidence\recompute\run04-census-final…run11-rel-after-release.recompute.json` | **该脚本只接受一个位置参数**（argparse `telemetry_file`），**没有输出路径参数**；本行写法是"stdout 重定向"，不是 CLI 参数。另：该脚本 `EVIDENCE_INSUFFICIENT` **也返回 0**；必须读 JSON 的 `assessment_status`，退出码不足以判断结论一致 |
| `dotnet run --project tools\phase10_evidence\Phase10Evidence.csproj -- <run05 telemetry> <run06 telemetry> _audit\2026-10-07-phase11-evidence\g3-rebuild` | 同上 | **exit=0**；两份 `ReportedGrade="Oscillating"` | `_audit\2026-10-07-phase11-evidence\g3-rebuild\`（8 CSV + 2 diagnostics + `results.json` + `console.txt`）；`run05-changes.csv` = `d5ae2ae90b81ca3484b4e07b07e4d520baafa42d05fda23c6911dd5bf6b61028`、`run06-changes.csv` = `4ce9e13fa63c17951f5eb2810a8270f3cd8d472de0a8631e2fedf988c9321666`；`results.json` = `a29bd216c78df6b8a74fedb481f414a9fe17f9e7b0df37f539c92502adda87e6` | 复现的是**计算侧**结论；**不重新证明**游戏内采集正确。`dotnet run` **默认包含构建**，故本行只构建了该单个工程 |
| 逐字节比对 `g3-rebuild` 与 tracked 冻结副本 `docs\evidence\phase10-closure-20261007\` | 同上 | **8/8 CSV + `h3-same-run.json` 全部 IDENTICAL** | 冻结 `g3-summary.json` = `27fda22bea7bd685a3017a199671362bf9a80f55976b36b900808b174df79b78`（len 30937） | 只支持"**本次输入与本次工具版本下输出确定**"；**不泛化为任意输入下的工具认证**，也不单独证明覆盖事故无损失（见 §7） |
| **未执行**：**全量工程** `dotnet build` / C# 套件 / Python 套件 / 游戏启动 / DLL 部署 | — | **未运行** | — | 本轮无运行时代码改动，故未跑套件；**不得**用 Phase 10 的历史 PASS 冒充本轮结果 |

> §2 与 §4 中引用的构建/套件数字（六项构建、C# 102/102、Python 23/23、D0 15/15、E1–E11 51/51、E12 10/10）**全部来自 Phase 10 历史报告**，本轮未重跑，仅作历史引用。

## 6. 结论与证据映射

| 重要结论 | 类型 | 实验/run、输入路径、哈希、行号 | 实体/域/窗口/配置 | 原判据与结果 | 支持范围与限制 |
|---|---|---|---|---|---|
| 宿主 Full 控制确实能在 Movement 域落地 | 宿主实测 | `run05`/`run06` `[hold]` 段：`T1F0\|Movement CLOSED intent=HOLD hold=100.0% window=24.50s samples=3503/3669`；telemetry `9242c657…`/`c2f41b3f…` | `T1F0` / Movement / 归属窗口 24.50 s / `level=Full ttl=30s` | 原判据 G2 `intent_hold_share ≥ 95%` → **100.0% PASS** | **仅限 T1F0 Movement 与既有单场 HOLD 窗口**；不是全场、不是全域，也不是 C 的 Behavior/Weight 集成验收 |
| G2=100% **不能**覆盖全场 G3 FAIL | 宿主实测 | `run05`/`run06` 主矩阵 `OrderChangesPerSecond=3.696969696969697`/`3.6363636363636362`，`OscillationPairs=37`/`35`，阈值 `stableOrderChangesPerSecondMax=0.2`；独立重建 console 第 191、382 行 | 全局 63 键 / 判读时刻原时长 **`simulation_seconds=33.00`**（非"全程 30 s"） | G3 `m.Grade != Oscillating` → **FAIL** | 目标以 Level 5 接管 Movement，**但其他编队与接管前初始化仍在抖动**；接管前初始化占多数、目标本身亦贡献 3/37 与 2/35，**不得称全部由其他编队造成** |
| 完整写入口覆盖率**未**证明 | 宿主实测 / 尚未验证 | `[coverage]`：`T1F0\|Arrangement sampled_changes=2 port_changes=3 unobserved=0`；`run06` 切 `enforceNonGated=true arrangementPath=Gated` 后 G3 **仍 FAIL** | `T1F0` / Arrangement（non-gated 自维护 Timer） | X5 `unobserved_writes == 0` → **0 PASS** | 该计数是**缺口下限**（影子采样分辨率 = Mission tick），且 `recompute` 输出自带 `interpretation: "zero means no observed positive gap; it does not prove complete coverage"` ⇒ **X5=0 不能推完整覆盖率=100%** |
| H3（关闭 AI 控制）**性质**：它是关闭实验，不是 C 的集成验收 | 宿主实测 | `run07`（`57205e643e42418a67ffaf1039a1acd5d05141df7045af104b4c88ca4d0d252b`）`[verdict]`：`H3-a=60 PASS`/`H3-b=0 PASS`/`G2=100.0% PASS`；`assessment.scope_note` 原文 | `T1F0` / Movement / `controlledByAiRoute=true restore=RestoreControlledByAi` | H3-a `CensusOf("Formation.SetControlledByAI") > 0`；H3-b `unauthorized_writes == 0` | `h3` 分支**不输出 G3**，全场 grade 需从 `[metrics]` 单读（该场仍 `Oscillating` 级别）；`scope_note` 明写 **AI tick 计数未插桩**；该路径跳过 **`FormationAI.Tick`**（`Formation.Tick` 自身仍继续其余簿记）—— **普通非 PlayerSergeant 会影响整个 `FormationAI.Tick`**，不构成 Behavior/Weight 层集成验收 |
| REL：恢复顺序与自然恢复 | 宿主实测 | `run11`（`0c121a76be088647650d424cf3649c8779da5aa8e8f8f02563f7d698975041da`）逐行：`t=28.0049 …W=Lab O=Native rej=1 why=NotOwner` → `AUTH\|Release … why=released to Native` → `t=28.0251 …W=Native chg=1`；对照 `run08` `t=28.0234 …W=Native chg=1`（无任何恢复动作）。恢复缓存实测：`REL_ORDER stage=reapply-request … rawType=Move position=794.721558,707.077637` | `T1F0` / Movement / `restore=ReapplyLastNativeOrder restoreBeforeRelease=false`（run11）vs 无（run08） | REL `release_recovery_failures == 0` → run11 **1 FAIL**（设计出来的反例）；**run09/run10 均为 PASS** | **原始 Move 重发按完整缓存执行（缓存是 Native 的原始 `Move`，不是 Lab HOLD）；AfterRelease 被拒之后的 Native 自行恢复不算那次恢复写入成功。** run09/run10 的 reapply 写入在 t≈28.00 被接受，但约 0.0265/0.0269 s 后下一 tick Native 即 `rgen=2` 回到 `Obs0`（该行 `chg=0` 为同值投影），与 run08 无动作结果相同 ⇒ **未证明 Reset 的恢复收益或必要性**；两场 `EVIDENCE_INSUFFICIENT` 是证据充分性状态，**不是 REL 判据失败** |
| Proposal 裁决 | 设计选择 | `docs/07a` §7 移交项②；`docs/06` §7 红线"预置 Proposal/Arbiter/Executor"行（全程零 `Proposal`/`Executor` 实现，"Proposal 不必要"的弱结论） | — | 无实验强制需要 | 判为**当前不实现**；属**弱结论**（无任何实验需要 ⇒ 不必要性成立，但未被正面对照实验检验），非"已证伪" |
| 八场身份与输入自洽 | 静态核对 | `artifact-manifest.json` **491/491**；八场 `assessment.json` 16/16 哈希；`recompute` 八场 `input.sha256` 一致 | 八场 run04–run11 | — | 只证明**归档产物互洽**；不证明游戏内采集正确，也不能替代本轮未做的任何运行 |

> **证据副本说明**：原始八场产物在 `_audit\_local-phase10-20261006\`（**本地忽略目录，未随 git 交付**）；随仓库跟踪的副本在 `docs\evidence\phase10-closure-20261007\`（`g3-summary.json` = `27fda22b…`、8 份 CSV、`full-diff-files.csv`、`closure-index.json` = 各含 SHA-256 记录）。本轮 `_audit\2026-10-07-phase11-evidence\` 同样**未被 git 跟踪**，若需审查请从工作区直接读取。

## 7. 设计偏差、实现差距与风险

| 条目 | 已知事实/源码观察/尚未验证 | 与设计差异 | 影响哪些结论或后续能力 | 建议处理 |
|---|---|---|---|---|
| Acquire 回滚 | **尚未验证**：设计 §4.1 要求的"Acquire 失败/中止后的回滚"没有对应实验；八场中无一场触发该路径 | 设计有契约、实验未覆盖 | 回滚正确性无法断言；不进入结论 | 后续最小任务：新增一场触发 Acquire 中止的 run，验收 = 回滚后 `unauthorized_writes == 0` 且恢复窗口内回到基线 |
| 到期恢复（Lease TTL 到期而非显式 Release） | **尚未验证**：八场均以显式 `release` 结束；`run04` `ttl=8s` 但 census 场零干预算 | 设计有契约、实验未覆盖 | "到期即恢复"能力未知 | 后续最小任务：一场 `ttl` 短于脚本时长的 run，验收 = 到期后 `release_recovery_failures == 0` |
| 请求原始身份（request identity） | **尚未验证**：`gen`/`rgen`/`tok` 已在 telemetry 中，但无实验构造同实体的并发请求来验证身份判定 | 设计有契约、实验未覆盖 | 并发下的归属判定未知 | 后续最小任务：同实体/同域构造两个请求，验收 = 后到者被 `NotOwner`/fencing 拒绝且计数可读 |
| Release / 异常路径的成功认证 | **部分验证**：`run08`–`run11` 覆盖了显式 Release；异常路径（写入口抛异常后的归属状态）**尚未验证** | 设计有契约、异常路径未覆盖 | 异常后是否残留 Lab 归属未知 | 后续最小任务：注入一次写入口异常，验收 = `AUTH` 日志出现完整 Release 且 `unauthorized_writes == 0` |
| 并发风险 | **源码观察**：设计 §4.1 点名的并发写入竞态在 `ProbeRuntime.cs` 中可见，但**八场无一次并发触发的事故证据** | 无差异（设计本身即为待验证项） | 无 | **不写成已发生故障**；后续以最小并发场景验证 |
| `tools/phase10_evidence` 覆盖他方目录（**本轮已发生**） | **已发生**：首次运行未传 `args[2]`，`Program.cs:13` 默认写 `_audit/_phase10-closure-20261007`，覆盖了该工具**实际会写出的 11 个文件**（`Program.cs:17` 的 `results.json` + `143-151` 行的 `run05|run06-{keys,changes,triples,lifetimes}.csv` 与 `run05|run06-diagnostics.json`）。**`h3-same-run.json` 不是该工具输出**（代码不生成它）；非本工具产物（`final-freeze-manifest.json`、`final-vs-adab4cf.patch`）未受影响 | **违反派单"不得覆盖他方审计目录"的操作要求** | **分三层**：①**已认证**——重新生成的 **8 份 CSV** 与 tracked 冻结副本 `docs\evidence\phase10-closure-20261007\` 逐字节相同（**范围仅限这 8 份**）；`results.json`、`run05\|run06-diagnostics.json` **未被逐字节认证**；`h3-same-run.json` 与冻结副本相同可独立记录，但**不属该工具输出、也不能用于认证被覆盖范围**；②**可重生成**——`results.json` 与两份 diagnostics 已在 `g3-rebuild\` 独立产出完整副本；③**不能认证**——**覆盖前未记录各文件哈希/副本**，无法对被覆盖的 11 个文件作逐字节前后一致性认证，时间戳等元信息确定已变更且不可恢复。**"未被 git 跟踪"不等于"覆盖无害"**；本轮一致性结果只支持"本次输入与本次工具版本下输出确定"，**不泛化为任意输入** | 已改用显式输出目录；后续调用一律显式传 `args[2]`。完整登记见报告 §3.2 |
| `docs/05` §1.5 的 G1/G2 为**补记**而非本轮新判据 | 已知事实：判据本身在 Phase 8 引入、Phase 10 落实，本轮只补正式定义 | 无差异，属派单要求的同步 | 若被读成"Phase 5 即有此门限"则为误读 | 文中已明写"Phase 8 引入，Phase 11 正式补记" |
| 设计文档 HEAD 记载偏差 | 已知事实：设计文档记 `e57e157`，实际 HEAD `f8b1f17` | 实际值领先两次提交 | 影响"以哪个版本为基线"的引用 | 已按实际记录；八场实测身份 `adab4cf` 不受影响，**不得倒写** |
| H3 的适用范围风险 | 源码观察：`Formation::Tick` 门控为 `if (IsAIControlled) AI.Tick(); else if (Team.IsPlayerSergeant) AI.Tick(); else IsAITickedAfterSplit=true;` ⇒ Route B 连 Behavior 选择一起停掉 | 与设计 §2.2 限定一致 | 若把 H3 PASS 当作"C 的 Behavior/Weight 集成验收"会**超出证据** | 已在 `docs/07-final-architecture.md` 与报告 §4/§5 明写；后续以真正的 Behavior/Weight 层实验验收 |

## 8. 交给 Codex 的审查事项

> **本文件自身的交付哈希**：**不在此记录**——本文件任何位置的哈希值都可能因该次写入而失效（自指循环）。**交付哈希由外部交回消息或独立清单给出**；如需自行核对，请在工作区执行 `Get-FileHash HANDOFF.phase11.md -Algorithm SHA256`。

- **请求审查的实际文件 / 未提交 diff**：
  - `docs/07-final-architecture.md`（新增，架构主交付）
  - `_audit/2026-10-07-phase11-evidence-review.md`（新增，证据复核）
  - `HANDOFF.phase11.md`（新增，本文件）
  - `HANDOFF.md`、`docs/05-experiment-plan.md`、`docs/06-experiment-results.md`、`docs/07a-architecture-abc-comparison.md`（修改，最小同步）
  - `_audit/verify_phase11_evidence.ps1`、`_audit/2026-10-07-phase11-evidence/`（新增，核对产物）
  - 获取方式：`git status --porcelain` + 上述路径；**本轮无提交**，请直接读工作区。
- **最需要独立复核的结论**：
  1. G3 FAIL 的**范围归属**（接管前初始化 vs 目标自身贡献 3/37、2/35）——输入 `run05`/`run06` telemetry，`g3-rebuild\console.txt` 第 191、382 行。
  2. run11 的 **REL=1 反例成立性**（`rej=1 why=NotOwner` 后的 `chg=1` 属 Native 自行恢复）——输入 `run11` telemetry `0c121a76…`。
  3. **X5=0 ≠ 完整覆盖**的口径——输入 `recompute\*.recompute.json` 的 `x5_coverage_gap_lower_bound`。
  4. H3 的**判据边界**（`h3` 分支无 G3 行、AI tick 未插桩）——输入 `run07` `[verdict]` 与 `assessment.json.scope_note`。
- **证据缺失与受影响验收**：八场原始产物位于**未跟踪**目录 `_audit\_local-phase10-20261006\`，若审查环境只有 git 内容，须以 `docs\evidence\phase10-closure-20261007\` 的冻结副本为准（该副本含 8 份 CSV + `g3-summary.json` + `closure-index.json`，但**不含** `metrics-*.txt` / `telemetry-*.txt` 原文）。因此**"事实"一项中的 16/16 哈希核对在仅有 git 内容时无法复现**——这是本轮唯一受环境影响的验收项，请审查方确认是否需要把 telemetry 原文纳入交付。
- **建议审查顺序**：① 先验 `docs/07-final-architecture.md` 与设计文档的覆盖一致性（9 项 + §6 七问）→ ② 再验报告 §2/§3 的数字（491、16/16、八场 recompute 表、G3 重现）→ ③ 再验 §4 失败保留的四条原判据与八场 `[verdict]` 原文 → ④ 再验 §5 六项证据列的限定是否被引文忠实保留 → ⑤ 最后验任务三四处同步是否越界（应仅限允许范围）。
- **后续建议**（按优先级，**不自动实施**）：① 补齐 §7 中"尚未验证"的四项契约（Acquire 回滚 / 到期恢复 / 请求原始身份 / 异常 Release）各一场最小实验；② 用真正的 Behavior/Weight 层实验验收 C，而不是复用 H3；③ 决定是否将八场 telemetry 原文纳入版本交付。
- **是否改变派单、设计、实验阈值或原始证据**：**分项回答**——①**派单**：未修改；②**实验阈值**：未修改；③**原始输入与原始证据**：未修改、未重跑；④**设计文档**：**已修改**——按审查报告在 `docs/phase11-design.md` §3.2/§4.1/§5.1 补记差距与口径更正（整改阶段，见 §3 逐文件表），故不再笼统回答"否"。`docs/07a` 的两节新增为**范围限定性说明**，不改动既有结论与数值。

### 7bis. 整改后交付物 SHA-256（实测）

**行数栏为非空行数**（审查方统计口径）；定位审查意见请用**物理行号或章节标题**。下表为**首轮整改后**（本次复审前）的值；**本次复审整改后的最新值由外部交回清单给出**。

| 文件 | 非空行数 | 首轮整改后 SHA-256 |
|---|---|---|
| `docs/07-final-architecture.md` | 336 | `37f44214a01dca200db65a81bbe15e865a5fd316eded109aa6f286ce68a7b486` |
| `_audit/2026-10-07-phase11-evidence-review.md` | 335 | `b2e32d8df990e4dd09cef16b969d06beb0190cdb05d2808556204d645632fec2` |
| `HANDOFF.phase11.md`（本文件） | — | **本文件哈希由外部交回消息或独立清单给出**，不在此自指记录（写入即改变其值，任何表内数值都不成立） |
| `docs/05-experiment-plan.md` | 305 | `038c606dd8091bc2adaa0690f5a2fef0b19274769412772e65fae2f0ab09d511` |
| `docs/phase11-design.md` | 100 | `dd13fbaf97d1642ca14639a9f36320ef7ef096793b7c85a8de465f0aecae54f5` |

**首轮审查时（整改前）对照**：`docs/07-final-architecture.md` = `a55b902c0919367fe93eba7314800b03784f9e7779b05b5c91e390bfe2fa6b22`（457 行）；`HANDOFF.phase11.md` = `d3664bdcb93012fc93e70c924f440803096b60252365fb4878d666a7d21cd1a5`（118 行）；`_audit/2026-10-07-phase11-evidence-review.md` = `e706875036ef8fb4b8f32418bb4b70dd054656193bb78776a6b23c9ca0ac9a96`（432 行）。三个数字与 Codex 审查报告 §1 记录一致。

**整改后工作树**（`git status --porcelain`）：tracked 修改 4 个（`HANDOFF.md` 10+/3−、`docs/05` 14+/0−、`docs/06` 2+/0−、`docs/07a` 16+/0−，合计 39+/3−）；`src/`、`tests/`、`deploy/`、`tools/`、`docs/evidence/`、`docs/10-harmony-runtime-experiments.md` **零改动**（`git diff --name-only` 对这些路径为空）。分支 `codex/local-phase10-verification-20261006`，HEAD `f8b1f173f05bf054b42eb2e31527deb1a83aa613`，**未提交、未推送**。**注**：`docs/06-experiment-results.md` 的 2 行新增**不在** Codex 审查报告的六条整改点内，属首轮已交付、复审时一并核验。
- **是否进入 Phase 12**：**否**。
