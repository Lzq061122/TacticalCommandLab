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
