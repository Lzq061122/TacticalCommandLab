# 跨平台离线验证

在仓库根目录运行：

```bash
python3 tools/run_offline.py
```

如果 .NET 10 SDK 不在 `PATH`，可显式传入 `dotnet` 可执行文件；也可先设置 `DOTNET` 环境变量：

```bash
python3 tools/run_offline.py --dotnet /path/to/dotnet
```

入口只构建不依赖 Bannerlord 的项目：Core 与 Telemetry 的 `net10.0`、`netstandard2.0` 目标，Simulation 的 `net10.0` 目标，以及 Tests 的 `net10.0` 目标。它随后运行集成测试，并分别核对 D0 `15/15`、E1–E11 `51/51`、E12 `10/10` 和离线测量阶段 `26/26`。任何构建错误、进程失败、缺少阶段或检查数不符都会以非零状态退出。入口也必须发现并通过 `tests/offline_telemetry` 下的 Python `unittest`；若测试文件缺失或未实际运行检查，就会失败。

测试在临时影子副本运行，既不需要 Bannerlord 游戏文件，也不会覆盖仓库中已有的 `tests/artifacts`。每次运行的日志、D0/E1–E11/E12 报告及测量报告存放在唯一的 `tests/artifacts/offline-<UTC时间>-<随机后缀>/` 目录。目录中的 `run-info.txt` 和 `run-info.json` 记录本地 Git HEAD、工作树状态、源码和 Python 工具/测试/输入夹具的 SHA-256、SDK 版本，以及初始远端源码引用。

输出中的 `Runtime=NOT_RUN` 专指 Bannerlord 游戏运行时未启动。本入口验证的是跨平台构建、离线模拟和判据；它不会加载游戏，也不会伪称完成游戏内实测。
