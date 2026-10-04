# TacticalCommandLab 开发进度验收（2026-10-04）

验收范围：当前工作区、提交 `19c7f82`、未提交的 docs/10 §14 和 H1 实验脚本、已保存的宿主实测产物。
验收方式：完整重建、隔离目录复跑现有测试、原始 telemetry 复算、游戏启动日志核对、部署程序集代码比对。本轮没有启动新的游戏实验，也没有修改产品代码、共享启动配置或既有实验产物。

## 结论

**部分通过。Phase 1–9 的已有交付和离线实现通过本轮工程验收；Phase 10 有新增实测进展，但尚不能正式验收为原生控制链实验成功。Phase 11/12 未开始。**

相比上一轮：实体编号冲突修复获得实测支持，基线四项门禁全绿，H1 已实跑两轮。当前需要优先解决的是实验环境和 H1 判据，而不是继续扩展架构。

| 部分 | 本轮判断 | 证据及边界 |
|---|---|---|
| Phase 1–5 研究与实验设计 | 交付物齐备 | docs/01–05 存在；本轮不重新验证历史文献和 IL 研究的全部结论 |
| Phase 6–8 Core、Simulation、Telemetry、仿真实验 | 通过 | 本轮复跑 D0 15/15、E1–E11 51/51、E12 10/10；重放哈希逐字一致 |
| Phase 9 BannerlordProbe 工程 | 通过构建验收 | 全 solution Rebuild：0 warning、0 error；不得把构建通过等同于全部宿主行为正确 |
| Phase 10 实体绑定和采集装置修复 | 有实测支持 | 29 个绑定对应 29 个唯一 ID；孤儿跳过 NOTE 12 行；基线和 H1 的 X5 独立复算均为 0 |
| Phase 10 纯 Vanilla 基线资格 | 不通过 | 基线和两轮 H1 的实际启动日志均显示 CombatBrain.dll 加载成功，违反 docs/10 §7.0 |
| Phase 10 H1 | 已实跑，未正式通过 | r2 同实体、同域的 HOLD 被覆盖，延迟 0.1588s / 0.1603s；H1-a 仍 FAIL，且环境未隔离 |
| Phase 10 H2/H2b/H3/REL | 未找到完成证据 | 已有脚本副本，但未找到对应实测产物 |
| Phase 11 最终架构、Phase 12 CombatBrain 集成 | 未开始 | 对应交付文件不存在；也不满足当前实验验收前提 |

## 1. 本轮重新执行的工程验证

`dotnet build CommandLab.slnx -t:Rebuild -v minimal --nologo`：退出码 0，0 个警告、0 个错误。

测试在复制出的运行目录执行，避免覆盖 `tests/artifacts` 的历史结果：

```text
=== 汇总：15/15 通过，失败 0 项 ===
=== 判据汇总：51/51 通过，失败 0 项 ===
=== 判据汇总：10/10 通过，失败 0 项 ===
D0-S1 replay_hash = d9853e282f033d16b7b67a958797e0c345be1d7e16d39b22a30dc9ca833d8199
TEST_EXIT=0
```

Core / Simulation / Telemetry / tests/CommandLab.Tests 相对基线 `3b49861` 的源码 diff 为空。TaleWorlds 的 using 均在 BannerlordProbe，源码无 CombatBrain 命名空间引用。现有测试不引用 BannerlordProbe，因此不能用于证明探针本次修复的宿主行为正确。

完整输出：`_audit/_acceptance-20261004/build.txt`、`tests.txt`。

## 2. 原始实测数据复算

| 项目 | baseline 01:33 | H1 r1 12:11 | H1 r2 12:24 |
|---|---:|---:|---:|
| P0 | 7 | 7 | 7 |
| P0b（报告值） | 1957 | 2113 | 2078 |
| projection 行数 / 独立复算唯一 ID 数 | 29 / 29 | 29 / 29 | 29 / 29 |
| 孤儿跳过 NOTE | 12 | 12 | 12 |
| SHADOW 行数 | 53 | 54 | 66 |
| 相同时间、实体、域的重复 SHADOW key | 0 | 0 | 0 |
| X5（按 SHADOW 和获准的有效写入独立复算） | 0 | 0 | 0 |
| Lab 写入 / 被拒绝 | 0 / 0 | 2 / 2 | 2 / 0 |
| native_overrides（独立复算） | 0 | 0 | 0 |
| lab_overrides（独立复算） | 0 | 0 | 2 |

H1 r2 的两对同实体、同域原始写入：

```text
T1F3 / Movement: t=3.0013 Lab HOLD -> t=3.1601 非 Lab 写入 Obs0；间隔 0.1588s
T1F3 / Movement: t=8.0041 Lab HOLD -> t=8.1644 非 Lab 写入 Obs0；间隔 0.1603s
```

这证明该进程环境中目标 HOLD 确实被覆盖。它不直接证明纯 Vanilla 的覆盖时机或真正调用者身份。

`NativeOverrideLatency` 的代码当前使用一个全局 `_lastLabChangeAt`，没有约束后续改动必须属于同一实体、同一域。本轮两对数据复算恰好匹配，0.159s 这一具体值有原始数据支持；该实现仍需要按实体和域关联，才能可靠支持其他场景。

X5 是 `max(0, sampled_changes - accepted_port_changes)` 的合计下限估计。X5=0 表示这一估计未检出缺口，不能据此证明所有路径、所有战斗中均没有绕过写入口的变化。`T-1F0..T-1F9` 仍存在，本场未撞名，不代表任意 Teams 变化下都能保持全局唯一。

复算记录：`_audit/_acceptance-20261004/telemetry-recomputed.json`。

## 3. 阻塞正式实验验收的问题

### 3.1 实验环境违反既有硬要求

docs/10 §7.0 明确要求关闭 CombatBrain，目的是测量 Vanilla 原生控制链。不能只看 CommandLabProbe 自己报告的 7 个补丁就认定其他模组未参与。

三次实测对应游戏日志均包含 CombatBrain 启动参数、DLL 加载和 active_modules：

- baseline：`C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_22072.txt`，第 122 行启动参数，第 1266 行加载 CombatBrain.dll，第 1341 行 active_modules。
- H1 r1：同目录 `rgl_log_9580.txt`，对应第 122 / 1266 / 1341 行。
- H1 r2：同目录 `rgl_log_40072.txt`，对应第 122 / 1266 / 1341 行；随后 Assembly load result 为 SUCCESS。

当前 GABS `bannerlord148` 的 `_MODULES_` 参数也包含 CombatBrain。`ModMasterStarter.bat` 的另一份模块表没有它，但本轮驱动走的是 GABS，不能用该 bat 为本轮实验环境背书。

**判断：这三组可以支持装置修复和混合环境下覆盖现象的记录，不能验收为纯 Vanilla 基线及因果实验。** 本轮未证明 CombatBrain 实际改了哪一次写入，也不据此断言所有数据均为假数据。

HostContext.cs 的 `behavior` 字段读取的是 `formation.AI.ActiveBehavior`，不是实际调用栈。GateCore 把所有非 `_inLabWrite` 的调用标为 Native，因此存在 CombatBrain 时，`W=Native` 和当前 Behavior 名不足以证明实际覆盖者就是原生 BehaviorMountedSkirmish。docs/10 §14.6 的确定性归因超出了现有证据。

环境摘录已保存：`_audit/_acceptance-20261004/environment-evidence.txt`。

### 3.2 H1 的脚本和成功判据尚未闭合

- 默认 Owner 为 Native / Full，且 enforceGated=true。不 acquire 的 Lab 写入会被拒绝。本轮 r1 两次均为 NotOwner，不构成成功外部写入后的覆盖实验。
- r2 关闭 enforceGated 后，两次 Lab 写入获准并被覆盖。但 Metrics.cs 的 native_overrides 只统计 Native 以非 Owner 身份执行的有效改动。本场 Owner 仍为 Native，故该指标为 0，与覆盖现象并不矛盾。
- ProbeRuntime.cs:932 的 H1-a 仍要求 native_overrides>0，与本实验归属语义不符。
- ProbeRuntime.cs:937–939 的 H1-c 无条件输出 PASS。r1 的 `no-override PASS` 说明不能直接把当前 H1-c 的 PASS 当成功门禁。
- 覆盖延迟实现还存在前述跨实体、跨域关联问题。修正成功判据时应一并明确「同一目标实体、同一域、先有获准的 Lab 有效写入，随后非 Lab 写入改变该意图」的验收条件。

**判断：docs/10 §14 对判据冲突的分析有代码和实测支持，但“RQ 完整回答，成功”不足以作为正式 H1 验收结论。** 应保留两轮反例，修正探针判据和脚本策略后在隔离环境重跑；不建议改变 Core 的已有归属定义来迎合 H1。

## 4. 部署版本核查

当前游戏目录的三个 DLL 与本轮完整重建产物均同尺寸，但文件 SHA256 不同。本轮进一步使用 Mono.Cecil 只读比对类型、字段、方法签名、方法 IL 及异常处理区：

| DLL | 新构建定义数 | 游戏目录定义数 | 该比较范围内差异 |
|---|---:|---:|---:|
| CommandLab.BannerlordProbe | 343 | 343 | 0 |
| CommandLab.Core | 571 | 571 | 0 |
| CommandLab.Telemetry | 172 | 172 | 0 |

因此，本轮没有支持“游戏仍在跑上轮旧修复代码”的证据。文件级一致性仍未成立；未证明哈希差异的具体来源，也未比较全部属性、资源和构建元数据。后续实验应记录实际部署哈希，建立源码提交与运行产物的对应关系。

记录：`_audit/_acceptance-20261004/assembly-comparison.json`。

## 5. 交付及下一步

- 已提交的进度截止 `19c7f82`：装置修复和基线结果归档。
- H1 结果写在未提交的 docs/10 §14，9 个新增实验脚本未跟踪。H1 产物仅位于被 `.gitignore` 忽略的 `_audit/_runs`，尚未纳入正式证据归档。
- HANDOFF 当前进度仍写下一步跑 H1，未反映 H1 已实跑及其判据缺陷。

建议顺序：

1. 为 CommanderLab 实验明确不加载 CombatBrain 的 CBATH 启动配置，并保存每轮实际 active_modules；CBATH 模块自身的 SubModule.xml 仅声明 Native 依赖，但剥离 CombatBrain 后桥接及自定义战斗能力仍须运行验证。
2. 修正 H1 脚本策略、成功门禁、同实体同域的覆盖延迟关联。保留 r1 的拒绝反例，确保没有获准 Lab 写入或没有覆盖时不会误报成功。
3. 在隔离环境重跑 census-baseline 和 H1，按新验收条件判读。
4. 正式归档原始产物、运行版本和模块列表，更新 HANDOFF 与实验文档，并提交已有 H1 工作。
5. 再推进 H2/H2b/H3/REL；Phase 10 全部收口后才能进入 Phase 11/12。

本轮只新增验收报告和审计辅助文件，不代改产品实现或共享游戏配置。
