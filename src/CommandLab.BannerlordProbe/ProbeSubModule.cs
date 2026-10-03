// BannerlordProbe / 模块入口与 Mission 行为
//
// 这是本工程唯一出现 TaleWorlds.* 类型之外还触碰宿生命周期的地方，职责仅限：
//   1) 装载 Harmony patch
//   2) 每 tick 驱动 ProbeRuntime（时钟 / 续约 / hold 采样 / 脚本）
//   3) 战斗结束时落盘 Telemetry
// 没有任何战术逻辑，也不实现 Commander。

using CommandLab.Core;
using TaleWorlds.MountAndBlade;

namespace CommandLab.BannerlordProbe;

public sealed class ProbeMissionBehavior : MissionBehavior
{
    private bool _started;
    private bool _ended;
    private bool _patched;
    private float _sinceDump;

    public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

    public override void OnMissionTick(float dt)
    {
        var runtime = ProbeRuntime.Current;
        if (runtime is null) return;

        // 本方法在宿主的 Mission 更新循环里，异常会崩游戏。runtime.Tick 内部已有兜底，
        // 这里再包一层覆盖本方法自己的几行（Mission.Current 访问 + 周期 dump）。
        try
        {
            TickCore(runtime, dt);
        }
        catch (Exception ex)
        {
            runtime.RecordFatal("ProbeMissionBehavior.OnMissionTick", ex);
        }
    }

    private void TickCore(ProbeRuntime runtime, float dt)
    {
        if (!_patched)
        {
            _patched = true;
            ApplyPatchesNow(runtime);
        }

        if (!_started)
        {
            _started = true;
            runtime.Dump("mission-start");
        }

        runtime.Tick(dt);

        _sinceDump += dt;
        if (_sinceDump >= 10.0f)
        {
            _sinceDump = 0f;
            runtime.Dump("tick");
        }

        if (!_ended && Mission.Current is not null && Mission.Current.IsMissionEnding)
        {
            _ended = true;
            runtime.Dump("mission-end");
        }
    }

    /// <summary>
    /// 装载 Harmony patch。**必须在第一次 Mission tick 执行 —— 不能提前到 OnSubModuleLoad。**
    ///
    /// 原因（2026-09-22 崩溃根因，docs/10 §12.9）：`Harmony.PatchAll()` 处理
    /// `[HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]` 时需要解析参数类型
    /// `MovementOrder`，这会**触发 `MovementOrder` 的静态构造函数**。而
    /// `MovementOrder..ctor` 的 IL 里有 `Mission::get_Current` → `get_CurrentTime`
    /// ⇒ **`Mission.Current == null` 时抛 NullReferenceException**。
    /// 静态构造失败后该类型被**永久标记为不可用** ⇒ 之后 `Team.Initialize → Formation.ResetAux`
    /// 一访问它就抛 `TypeInitializationException` ⇒ 进战斗必崩。
    ///
    /// `OnSubModuleLoad` 时 `Mission.Current` 必然是 null（还没进战斗），所以那时 patch 必炸；
    /// 而第一次 `OnMissionTick` 时 Mission 已在运行，`Mission.Current` 必定非 null —— 且游戏自己
    /// 早已成功初始化过 `MovementOrder`，此时再 patch 是安全的。
    ///
    /// 代价：Mission `AfterStart` 阶段的少量初始写入（`Formation.ResetAux` 等）落在 patch 生效之前，
    /// 不会被观测。对"稳态写入口流量普查"无影响，已在 docs/10 §12.9 登记。
    /// </summary>
    private static void ApplyPatchesNow(ProbeRuntime runtime)
    {
        try
        {
            Patches.Apply(ProbeSubModule.HarmonyId);
        }
        catch (Exception ex)
        {
            // 兜底：Apply 自身已吞异常并写 Failure，这里防 Apply 之外的意外（如 Harmony 程序集缺失）。
            runtime.Note("HARMONY_PATCH_FAILED(unexpected) " + ex.GetType().Name + ": " + ex.Message);
        }

        // patch 失败必须留痕，且必须留下根因：后续所有"没有原生写入"的结论都依赖 patch 真的生效。
        runtime.Note("HARMONY_PATCHED count=" + Patches.Applied.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Patches.Failure is not null)
        {
            runtime.Note("HARMONY_PATCH_FAILED");
            runtime.Note("HARMONY_PATCH_CAUSE " + Patches.Failure);
        }

        runtime.WritePatchMarker();
    }
}

public class ProbeSubModule : MBSubModuleBase
{
    public const string HarmonyId = "com.commandlab.probe";

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();

        var config = new ProbeConfig();
        new ProbeRuntime(config);

        // 崩溃可能发生在宿主代码里（不在 Gate/Tick 的兜底范围内），因此全局也挂一个。
        // 处理器里直接落盘（不是只 Note）—— 进程随即终止，内存里的 _hostLines 会全部丢失。
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception ?? new Exception(Convert.ToString(e.ExceptionObject));
                ProbeRuntime.Current?.RecordFatal("AppDomain", ex);
            };
        }
        catch
        {
            // 挂不上也要继续，不能因为诊断能力缺失而阻止加载。
        }

        // ⚠ 这里**故意不 patch**。patch 被推迟到第一次 Mission tick，原因见
        // ProbeMissionBehavior.ApplyPatchesNow 的注释与 docs/10 §12.9 的崩溃根因。
        var runtime = ProbeRuntime.Current;
        if (runtime is null) return;

        runtime.Note("HARMONY_PATCH_DEFERRED to first mission tick");
        runtime.WriteBootMarker(HarmonyId);
    }

    public override void OnMissionBehaviorInitialize(Mission mission)
    {
        base.OnMissionBehaviorInitialize(mission);
        mission.AddMissionBehavior(new ProbeMissionBehavior());
    }
}
