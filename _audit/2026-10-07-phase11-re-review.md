# Phase 11 文档整改复审

日期：2026-10-07（北京时间）。审查责任：Codex。结论：**仍为部分通过，尚不能认定六项全部关闭**。

本轮仅复审整改文档及对应源码/已有诊断证据，没有重跑游戏、全量测试或诊断实验，没有修改开发组交回文件。主架构、原始失败保留、PID/算术/REL 状态与延迟等多数整改已落实；剩余问题集中于文档事实一致性，不要求扩展开发范围。

## 1. 交付身份核对

分支与 HEAD 仍为 `codex/local-phase10-verification-20261006` @ `f8b1f173f05bf054b42eb2e31527deb1a83aa613`。src/tests/deploy/tools、冻结证据和 docs/10 的 tracked diff 为空。前次独立审查的证据清单中所有文件 SHA-256 未变，故沿用前次已验证结果，不重复测试。

| 文件 | 交回 SHA-256（实测匹配） | 物理行数 | 非空行数 |
|---|---|---:|---:|
| `docs/07-final-architecture.md` | `37f44214a01dca200db65a81bbe15e865a5fd316eded109aa6f286ce68a7b486` | 477 | 336 |
| `_audit/2026-10-07-phase11-evidence-review.md` | `b2e32d8df990e4dd09cef16b969d06beb0190cdb05d2808556204d645632fec2` | 452 | 335 |
| `HANDOFF.phase11.md` | `ab4232991e6e6ba86c33a89e6c81bc48224affe11deb58c3b76ef1f09cb8ca1f` | 140 | 113 |
| `docs/05-experiment-plan.md` | `038c606dd8091bc2adaa0690f5a2fef0b19274769412772e65fae2f0ab09d511` | 396 | 305 |
| `docs/phase11-design.md` | `dd13fbaf97d1642ca14639a9f36320ef7ef096793b7c85a8de465f0aecae54f5` | 146 | 100 |

用户给出的“行数”实际是非空行数，五份均吻合。这是统计口径区别，不是文件身份不一致。定位审查意见时使用物理行号或章节标题。输入校验记录见 `_audit/2026-10-07-phase11-re-review/input-verification.json`。

## 2. 首轮六项的关闭情况

| 首轮问题 | 本次判断 |
|---|---|
| ① Arrangement 覆盖率推论 | 关闭：已经区分覆盖率可检验性与 Arrangement Full 验收，无 Lab 命令不再被用作否定预测可检验性的理由 |
| ② hold 窗口与 Open 差距 | 主修订可接受；**新增 E12 窗口描述错误，仍需修改** |
| ③ 批量仲裁/许可差距 | 差距已登记；**复现表的两个拒绝原因写反，仍需修改** |
| ④ 事实身份/时间/范围 | PID、算术、主要时间与 pairs 表已更正；**摘要和报告仍有重复旧表述** |
| ⑤ REL 证据状态与恢复限制 | 主修订可接受：run09/10 的 INSUFFICIENT 不再误作 REL 失败；0.0265/0.0269s 已写明，后续同值投影的限制已登记 |
| ⑥ 覆盖事故与交接 | 三层损失评估可接受；**工具实际写出范围仍误列 h3-same-run.json，交接记录另有矛盾** |

## 3. 剩余的精确整改

### ① [P2] 最小复现表把拒绝原因写反

位置：`docs/07-final-architecture.md` §5.5，第 **245** 行。

当前写“Native 以 LostArbitration 被拒（Evaluate 阶段），Lab 以 NotOwner 被拒”。与前次实际诊断输出相反，也与紧邻的源码顺序说明不符。

**应改为：Lab 在 Resolve 阶段以 LostArbitration 被拒；Native 在 Evaluate 阶段以 NotOwner 被拒；最终值 Unset。**

直接证据：`_audit/2026-10-07-phase11-codex-review/arbitration-reproduction.txt`、同目录 `Program.cs`；`WritePort.Resolve` 淘汰的是低优先级 Lab，随后 Evaluate 检查高优先级 Native 的 Owner。已有源码与证据未改变，不需要再次运行复现。

### ② [P2] E12 采用固定区间积分，不是逐写入打开/延长窗口

位置：`docs/05-experiment-plan.md` 第 **113** 行；`docs/07-final-architecture.md` 第 **260** 行；`docs/phase11-design.md` 第 **109** 行的补记。

新增“E12 在每次生效写入后打开/延长 hold 窗口”没有源码依据，且 `docs/05` §1.4 不是 E12 窗口实现。

实际 `tests/CommandLab.Tests/Scenarios/ArchitectureComparison.cs:43-58`：筛选该域被接受且 OrderChanged 的事件，延续此前的 NewValue 状态，在指定区间内累计状态等于 HOLD 的时长；第 **201** 行调用参数明确为 **from=2.0、to=22.0**，分母为 **20 秒**。没有调用 HoldMeter.Open，也没有每次变更延长窗口。

应同步三处为：“E12 历史仿真在固定 `[2.0,22.0)` 区间按有效变更序列积分、沿用窗口前状态，分母20s；Probe 使用 HoldMeter tick 采样，现有 Open 条件只查 LastAttempt 存在。两者的实验窗口与实现须分别标明。”不要将 Probe 首次接受写入的窗口规则倒写为 E12 历史行为。

本项只纠正文档，不改变历史 hold 数值、G2 门限或源码。

### ③ [P2] 正文局部更正后，摘要仍保留旧事实

需要在所有重复处同步，具体已确认的位置：

- 最终文档第 **37** 行仍断言关闭控制“跳过整个 Formation::Tick”。改为“跳过该路径的 FormationAI.Tick；Formation.Tick 继续其他处理”。IL 原件第38行即有此限定。设计证据表第33行的“整个 Tick”也宜明确为 FormationAI.Tick，避免再传播歧义。
- 最终文档第 **34** 行仍将“24.50s HOLD 窗口”和“50次 Native 拒绝”同列且不分计数范围。明确：接管/全记录50次；`[3.5,28)` 意图窗口49次。前次独立 `independent-checks.json` 已给出两种计数。
- 证据报告第 **141** 行，pairs 明细已更正，但后半句仍写“八场脚本一律 acquire+cmd”。改为“其余七场；run04 只有 policy+dump”。实际 census 脚本没有 acquire/cmd。
- 最终文档第 **21** 行仍写“没有新跑构建”，与后文 dotnet run 默认构建冲突。改为“未重跑全量工程/宿主构建及测试套件；首轮使用 dotnet run 构建并执行了诊断工程”。

这些是首轮已经指出问题的残留位置，不新增实验要求。

### ④ [P2] 覆盖清单应当来自工具实际写文件代码

位置：证据报告 §3.2 第 **170–175** 行；`HANDOFF.phase11.md` §7 的覆盖事故行。

当前仍把 `h3-same-run.json` 列为 Phase10Evidence 本次生成/覆盖的文件。实际 `tools/phase10_evidence/Program.cs` 第17、143–151行只生成：

```text
results.json
run05-diagnostics.json / run06-diagnostics.json
run05-keys.csv / run06-keys.csv
run05-changes.csv / run06-changes.csv
run05-triples.csv / run06-triples.csv
run05-lifetimes.csv / run06-lifetimes.csv
```

共11个文件。**代码没有生成 h3-same-run.json**；前次审查独立输出目录也没有该文件。h3 副本相同可以独立记录，但不能算该工具重生成或用于认证被它覆盖的范围。

应列出上述准确清单，注明 h3 文件不属于该次工具输出。保留“已比对内容/可重生成内容/缺少覆盖前哈希而不能认证”三层结论，不再泛称所有 run05/run06 系列文件均被覆盖。

### ⑤ [P3] 清理交接中的自指哈希与设计修订矛盾

- `HANDOFF.phase11.md` 已公开承认整改阶段修改设计文件，但 §8“是否改变派单、设计、实验阈值或原始证据”仍回答“否”。拆开写：设计按审查修订；派单、实验阈值和原始输入未修改。
- 删除手工滚动写入本文件自身哈希的中间值（§8、§7bis），保留“本文件最终哈希由外部交回消息或独立清单给出”。当前已存在以交回消息认证的有效哈希，不需自指循环。
- 最终架构题头及证据报告的设计依据仍仅写旧 `042ef0fb…`。注明它是开工基线，同时补整改设计的 `dd13fbaf…`，不能让旧值看起来是当前文件哈希。
- 行数栏注明“非空行数”，定位用物理行号。无需重新排版。

本项不改变架构选择，但应在交回前顺手完成，避免下个接手者读到互相矛盾的身份声明。

## 4. 下一次复审范围

只修改以上五条涉及的文字。最终文档、05、设计、证据报告和 handoff 中相同事实逐处同步；交回新哈希及精确位置。没有源码变化、输入变化或新失败时，**不重跑游戏、全量套件或仲裁实验**。

运行时未完成能力仍是公开遗留项，不要求本轮修 Core。基本交付能否通过取决于文档是否准确记录现有证据和设计边界，不取决于把这些后续能力全部实现。本次仍不放行 Phase 12。
