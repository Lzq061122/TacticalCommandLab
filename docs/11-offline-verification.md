# 跨平台离线验证

在仓库根目录运行：

```bash
python3 tools/run_offline.py
```

如果 .NET 10 SDK 不在 `PATH`，可显式传入 `dotnet` 可执行文件；也可先设置 `DOTNET` 环境变量：

```bash
python3 tools/run_offline.py --dotnet /path/to/dotnet
```

入口只构建不依赖 Bannerlord 的项目：Core 与 Telemetry 的 `net10.0`、`netstandard2.0` 目标，Simulation 的 `net10.0` 目标，以及 Tests 的 `net10.0` 目标。它随后运行集成测试，并分别核对 D0 `15/15`、E1–E11 `51/51`、E12 `10/10` 和离线测量阶段 `26/26`。任何构建错误、进程失败、缺少阶段或检查数不符都会以非零状态退出。入口也必须发现并通过 `tests/offline_telemetry` 与 `tests/offline_runner` 下的 Python `unittest`；若任一测试套件缺失或未实际运行检查，就会失败。

测试在临时影子副本运行，既不需要 Bannerlord 游戏文件，也不会覆盖仓库中已有的 `tests/artifacts`。每次运行的 `result.json`、`summary.md`、日志、D0/E1–E11/E12 报告及测量报告存放在唯一的 `tests/artifacts/offline-<UTC时间>-<随机后缀>/` 目录。本地手动运行也会生成这些文件。目录中的 `run-info.txt` 和 `run-info.json` 记录本地 Git HEAD、工作树状态、源码和 Python 工具/测试/输入夹具的 SHA-256、SDK 版本，以及初始远端源码引用。

输出中的 `Runtime=NOT_RUN` 专指 Bannerlord 游戏运行时未启动。本入口验证的是跨平台构建、离线模拟和判据；它不会加载游戏，也不会伪称完成游戏内实测。

## GitHub Actions

`.github/workflows/offline-verification.yml` 在指向 `master` 的 PR 创建、更新、重开和 ready-for-review 时运行。`opened` 事件也涵盖 draft PR。向 `master` push 和手动 `workflow_dispatch` 也会触发验证。Actions job 使用 Ubuntu 24.04、.NET 10 和 Python 3.12，并调用同一 `tools/run_offline.py` 入口；它不吞掉失败。runner 在每次唯一的 `offline-*` 报告目录写 `result.json`、`summary.md` 和日志。workflow从JSON读取六项目标构建、四组C#检查、`tests/offline_telemetry` 与 `tests/offline_runner` 的真实Python结果，不解析易变的控制台文本；它还核对当前checkout的Git HEAD、结果字段和检查数量。PR中使用GitHub默认的合并提交检出方式，因此来源HEAD是本次workflow checkout的合并提交SHA，而非单独PR head。summary会报告运行时状态，并在成功或失败后都尝试上传该次报告目录。若SDK setup在入口启动前失败，summary会标记 `NO_RESULT`，而不是报告通过。

`workflow_dispatch` 只有在工作流文件已存在于默认分支后，才能从 Actions 页面手动启动。workflow 对 Actions 使用 `contents: read`、关闭 checkout 凭据持久化，并按 PR 或 ref 取消过期的并发 run。

工作流使用完整提交 SHA 固定官方 Actions；注释保留对应发布版本：

| Action | Release | Pinned commit |
|---|---|---|
| [actions/checkout](https://github.com/actions/checkout/releases/tag/v7.0.1) | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
| [actions/setup-dotnet](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0) | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
| [actions/setup-python](https://github.com/actions/setup-python/releases/tag/v7.0.0) | v7.0.0 | `5fda3b95a4ea91299a34e894583c3862153e4b97` |
| [actions/upload-artifact](https://github.com/actions/upload-artifact/releases/tag/v7.0.1) | v7.0.1 | `043fb46d1a93c77aae656e7c1c64a875d1fc6a0a` |

## 2026-10-06 Windows 本机复核

本机 PowerShell 7.6.5、.NET SDK 10.0.400、Python 3.14.7 已实际执行入口。修补了控制台不能编码输出时的安全转发/子进程回收，以及 fake-dotnet 临时路径短名/长名比较问题；增加 GBK 控制台/UTF-8 日志回归。未跳过测试，也未修改验收阈值。入口可在未设置 `PYTHONIOENCODING` 或 `PYTHONUTF8` 的终端完成。

最终本机报告 `tests/artifacts/offline-20261006T135059.876430Z-5638d296/`：六项构建 PASS，C# **102/102**；Python offline_runner **14/14**、offline_telemetry **9/9**，合计 **23/23，零跳过**，多出的一项为上述编码回归。HEAD 为 `adab4cf008a8dd6e83f8e12c03f1423483cb98f0`，working_tree=dirty；源码摘要 `313469e1b6519d2a88ae12eed08fe9db61d6a227ef7f178e1704af9c526a666b`。具体本轮修改由 Git diff 和逐场源码记录描述。

离线 result.json 的 `runtime=NOT_RUN` 仍只指离线入口没有启动游戏。随后已单独实际构建并部署 BannerlordProbe net472，完成本地游戏采集；这部分记录在 [Phase 10 本地交付](local-phase10-verification-20261006.md)，不把宿主构建/实测结果混入离线入口结果。

## 2026-10-07 版本冻结补记

Windows runner/测试匹配测量快照的blob事后冻结于e57e157；原离线结果仍为adab4cf+dirty，不能改写成新提交上的运行。全场诊断引用原生产MetricsCalculator，原102项结果保留；本轮新增验收仅为只读重放和驱动cleanup修补的PS5.1/7各8例隔离回归，没有重跑游戏或宿主构建。版本和证据见 [Phase10收口报告](phase10-closure-20261007.md)。
