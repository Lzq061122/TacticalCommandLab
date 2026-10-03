// BannerlordProbe / 配置
//
// 设计原则（承接 05 §6 判别力矩阵）：所有"对照条件"必须是可切换的配置，而不是改代码重编译。
// Phase 10 的 H1/H2/H3 靠翻转这里的开关产生对照，与仿真侧的 WritePortPolicy 一一对应。

using CommandLab.Core;

namespace CommandLab.BannerlordProbe;

[Flags]
public enum ReleaseRestoreMode
{
    /// <summary>只交还归属，不做任何宿主侧恢复动作（用于观察"不恢复会怎样"）。</summary>
    None = 0,

    /// <summary>Release 前/后重新下发最后一次观测到的原生 MovementOrder。</summary>
    ReapplyLastNativeOrder = 1,

    /// <summary>调用 FormationAI.ResetBehaviorWeights()（验证"是否必须重置权重才能恢复"）。</summary>
    ResetBehaviorWeights = 2,

    /// <summary>把 SetControlledByAI 恢复到夺取前的值（仅在夺取时改过才生效）。</summary>
    RestoreControlledByAi = 4
}

public sealed class ProbeConfig
{
    public const string NativeWriterId = "Native";
    public const string LabWriterId = "Lab";

    // ---- 归属 ----
    public AuthorityLevel Level { get; set; } = AuthorityLevel.Full;
    public double TtlSeconds { get; set; } = 8.0;

    /// <summary>每次 Mission tick 自动续约（对应 04 C2：Lease 必须可续）。</summary>
    public bool AutoRenew { get; set; } = true;
    public double RenewExtendSeconds { get; set; } = 8.0;

    // ---- 写入口覆盖率开关（与仿真 WritePortPolicy 同名同义）----
    public bool EnforceOnGated { get; set; } = true;

    /// <summary>默认 false = 复现 02 门控矩阵：Arrangement 自维护路径不受门控。Phase 10 H2 要实测的就是这一条。</summary>
    public bool EnforceOnNonGated { get; set; } = false;

    public bool PatchMovement { get; set; } = true;
    public bool PatchFacing { get; set; } = true;
    public bool PatchArrangement { get; set; } = true;

    /// <summary>Arrangement 写入归类为哪条路径。默认 NonGated（02 §8.2）。改成 Gated 即等于宣称"该路径已被覆盖"。</summary>
    public WritePath ArrangementPath { get; set; } = WritePath.NonGated;

    /// <summary>是否走 Route B（SetControlledByAI(false)），用于比对 10.6 的既有 PoC。</summary>
    public bool UseControlledByAiRoute { get; set; } = false;

    // ---- 释放与恢复 ----
    public ReleaseRestoreMode Restore { get; set; } = ReleaseRestoreMode.None;

    /// <summary>true = 恢复动作在 Release 之前执行（仍是 owner，写入放行）；false = 之后执行（写入会被拒，可观测）。</summary>
    public bool RestoreBeforeRelease { get; set; } = true;

    // ---- 指标 ----
    public double TEval { get; set; } = 0.5;

    // ---- 输入输出 ----
    public string DumpDir { get; set; } =
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            "CommandLabProbe");

    public string ScriptPath { get; set; } =
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
            "CommandLabProbe", "script.txt");

    public WritePortPolicy ToPolicy() => new WritePortPolicy
    {
        EnforceAuthorityOnGated = EnforceOnGated,
        EnforceAuthorityOnNonGated = EnforceOnNonGated,
        EnforceFencing = true,
        Arbitration = ArbitrationMode.None,
        MinLifetimeSeconds = 0.0,
        HysteresisRatio = 0.0
    };
}
