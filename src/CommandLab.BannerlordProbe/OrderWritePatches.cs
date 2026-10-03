// BannerlordProbe / Harmony 写入口 patch
//
// 定位（红线）：Harmony 只做"控制权集成"——把宿主的写入口接到 Core 的 WritePort 上。
// 这里没有任何战术判断，也没有"每 Tick 重复 SetOrder"，更没有靠 return false 硬压 Vanilla 之外的逻辑。
//
// 路径归类依据 02 §5 门控矩阵：
//   Movement / Facing  -> Gated   （Behavior 侧写入，IsAIControlled 覆盖得到）
//   Arrangement        -> NonGated（Formation 自维护双 Timer 不受门控，ArrangementPath 可配置以便实测）
//
// Phase 10 新增两类 patch：
//   A. 观测型（SetControlledByAI / SetFormOrder / SetFiringOrder / SetRidingOrder）——只计数，永不拦截。
//      用途：写入口覆盖率普查。02 §2 记的是 6 个写入口，本 Phase 必须实测"哪几个口真的有流量"。
//   B. 生效性取证：PatchAll 后用 GetPatchedMethods() 导出真正被 patch 的方法列表。
//      没有它就无法区分"这个口没有流量"与"patch 静默不匹配"——后者会产出"看起来没写入"的假数据。

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using CommandLab.Core;
using TaleWorlds.MountAndBlade;

namespace CommandLab.BannerlordProbe;

internal static class Patches
{
    /// <summary>
    /// 真正被 patch 的方法（Harmony 自己的账本，与运行时流量无关）。
    /// Phase 10 的 P0 门禁：这里为空则所有"没有原生写入"的结论一律作废。
    /// </summary>
    public static List<string> Applied { get; } = new List<string>();

    /// <summary>
    /// PatchAll 失败时的异常摘要（null = 全部成功）。
    ///
    /// 为什么必须暴露它：Harmony 的 `Prefix` 除 `__instance` / `__0` 等特殊名外，
    /// **一律按参数名匹配原方法参数**；名字对不上会抛异常并使 `PatchAll()` **整体中止**。
    /// 2026-09-22 实测踩过：`SetMovementOrder` 的形参叫 `input`（不是 `order`）、
    /// `SetControlledByAI` 的形参叫 `isControlledByAI`（不是 `isAIControlled`），
    /// 两处笔误导致整轮 P0 FAIL。只写一个 `HARMONY_PATCH_FAILED` 常量会把根因藏起来。
    /// </summary>
    public static string? Failure { get; private set; }

    /// <summary>由 SubModule 调用。自身不抛异常：失败信息进 <see cref="Failure"/>，由调用方落盘。</summary>
    public static void Apply(string harmonyId)
    {
        var harmony = new Harmony(harmonyId);
        Applied.Clear();
        Failure = null;

        try
        {
            harmony.PatchAll();
        }
        catch (Exception ex)
        {
            Failure = Describe(ex);
        }

        // 无论成败都刷新账本：PatchAll 在中途抛异常时，**此前已成功的 patch 仍然生效**，
        // 因此"失败了 ⇒ 一个都没打上"是错的。这里读的是 Harmony 的事实来源，不是我们的意图。
        try
        {
            foreach (var m in harmony.GetPatchedMethods())
            {
                var ps = m.GetParameters().Select(p => p.ParameterType.Name);
                Applied.Add($"{m.DeclaringType?.FullName}.{m.Name}({string.Join(", ", ps)})");
            }
            Applied.Sort(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Applied.Add("(GetPatchedMethods failed: " + ex.GetType().Name + ")");
        }
    }

    private static string Describe(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var line = e.GetType().Name + ": " + e.Message;
            if (!parts.Contains(line)) parts.Add(line);
        }
        return string.Join(" <- ", parts);
    }

    // ---------------- 兜底层级（2026-09-22 修正，重要）----------------
    //
    // ⚠ 曾经的缺陷：兜底只加在 ProbeRuntime.Gate() / Observe() **内部**，而 Prefix 方法体里
    //   **在调用它们之前**的那些表达式（`runtime.Bind(...)` / `Project(...)` / `input.OrderType.ToString()`）
    //   全部落在兜底之外。这些表达式在宿主调用写入口时**同步**执行，一旦抛异常就会穿透到宿主
    //   调用栈并让游戏崩溃 —— 而 ProbeRuntime 里的 try/catch 根本来不及覆盖。
    //
    // ⇒ 兜底必须加在 **Prefix 方法体这一层**，覆盖从参数解引用到返回值构造的全部代码。
    //   这里是唯一能保证"探针不弄崩宿主"的位置。
    //
    // 同时：关于 `MovementOrder.OrderType` 是否可空 —— **它不可空**。
    //   曾据 sigscan 的 "Public, Sealed 且无 SequentialLayout" 推断它是引用类型，进而猜测
    //   `input.OrderType.ToString()` 在 null 时抛 NRE 是崩溃根因。**该推断已被编译器证伪**：
    //   对 `OrderType` 用 `?.` 报 CS0023「运算符"?"无法应用于"OrderType"类型的操作数」
    //   ⇒ `OrderType` 是**值类型**，永远非 null。**该 NRE 假设作废，不作为崩溃解释。**
    //   修复后这里保留 try/catch，是为了覆盖 struct 的 ToString() 实现万一在其内部抛异常的情况；
    //   真正的修复价值在于**兜底层级**，不在于 null。

    // ⚠ 参数名必须与宿主元数据逐字一致（Harmony 按名字匹配，见 Patches.Failure 的注释）。
    //   v1.3.4 实测：SetMovementOrder 的形参是 `input`，不是 `order`。复核工具：sigscan。
    [HarmonyPatch(typeof(Formation), nameof(Formation.SetMovementOrder))]
    internal static class SetMovementOrderPatch
    {
        private static bool Prefix(Formation __instance, MovementOrder input)
        {
            try
            {
                var runtime = ProbeRuntime.Current;
                if (runtime is null) return true;

                // OrderType 是值类型（编译器已证：不可用 ?.），直接 ToString 即可。
                var raw = input.OrderType.ToString();

                var value = NeutralOrders.TryDecode(input, out var decoded)
                    ? decoded
                    : runtime.Bind(__instance).MovementProjection.Project(raw);

                return runtime.Gate(__instance, CommandDomain.Movement, WritePath.Gated,
                                    "Formation.SetMovementOrder", value, raw);
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetMovementOrder", ex);
                return true;   // 放行：探针是实验装置，绝不能弄崩宿主的游戏
            }
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.SetFacingOrder))]
    internal static class SetFacingOrderPatch
    {
        private static bool Prefix(Formation __instance, FacingOrder order)
        {
            try
            {
                var runtime = ProbeRuntime.Current;
                if (runtime is null) return true;

                var raw = order.OrderEnum.ToString();
                var value = runtime.Bind(__instance).FacingProjection.Project(raw);
                return runtime.Gate(__instance, CommandDomain.Facing, WritePath.Gated,
                                    "Formation.SetFacingOrder", value, raw);
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetFacingOrder", ex);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.SetArrangementOrder))]
    internal static class SetArrangementOrderPatch
    {
        private static bool Prefix(Formation __instance, ArrangementOrder order)
        {
            try
            {
                var runtime = ProbeRuntime.Current;
                if (runtime is null) return true;

                var raw = order.OrderEnum.ToString();
                var binding = runtime.Bind(__instance);
                var value = binding.ArrangementProjection.Project(raw);
                return runtime.Gate(__instance, CommandDomain.Arrangement, runtime.Config.ArrangementPath,
                                    "Formation.SetArrangementOrder", value, raw);
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetArrangementOrder", ex);
                return true;
            }
        }
    }

    // ---------------- 观测型（不拦截）----------------

    // 取证（2026-09-20，v1.3.4 IL）：Formation.SetControlledByAI 在本版本只有一种形态
    //   SetControlledByAI(bool isControlledByAI, bool enforceNotSplittableByAI)
    // IL 里 Ldarg_2 写入 _enforceNotSplittableByAI，确认是两个实参而非可选参数。
    // 因此 nameof 可精确匹配，不存在 HANDOFF 旧登记的"1/2 参重载歧义"。
    // ⚠ 2026-09-22 更正：形参名是 `isControlledByAI`（AI 在后），不是 `isAIControlled`。
    //   此前写成 isAIControlled ⇒ 名字匹配失败 ⇒ PatchAll 抛出 ⇒ 整轮 P0 FAIL。
    [HarmonyPatch(typeof(Formation), nameof(Formation.SetControlledByAI))]
    internal static class SetControlledByAiPatch
    {
        private static void Prefix(Formation __instance, bool isControlledByAI, bool enforceNotSplittableByAI)
        {
            try
            {
                ProbeRuntime.Current?.Observe(__instance, "Formation.SetControlledByAI",
                    "isControlledByAI=" + isControlledByAI);
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetControlledByAI", ex);
            }
        }
    }

    // SetArrangementOrder 内部会级联调用 SetFormOrder（IL 实证：FormOrder != 3 且 Arrangement != 1 时
    // 以 FormOrderCustom(CalculateDesiredWidth()) 写入）。观测它才能证明"写入口之间存在级联"。
    [HarmonyPatch(typeof(Formation), nameof(Formation.SetFormOrder))]
    internal static class SetFormOrderPatch
    {
        private static void Prefix(Formation __instance, FormOrder order)
        {
            try
            {
                ProbeRuntime.Current?.Observe(__instance, "Formation.SetFormOrder", order.OrderEnum.ToString());
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetFormOrder", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.SetFiringOrder))]
    internal static class SetFiringOrderPatch
    {
        private static void Prefix(Formation __instance, FiringOrder order)
        {
            try
            {
                ProbeRuntime.Current?.Observe(__instance, "Formation.SetFiringOrder", order.OrderEnum.ToString());
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetFiringOrder", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Formation), nameof(Formation.SetRidingOrder))]
    internal static class SetRidingOrderPatch
    {
        private static void Prefix(Formation __instance, RidingOrder order)
        {
            try
            {
                ProbeRuntime.Current?.Observe(__instance, "Formation.SetRidingOrder", order.OrderEnum.ToString());
            }
            catch (Exception ex)
            {
                ProbeRuntime.Current?.RecordFatal("Prefix:Formation.SetRidingOrder", ex);
            }
        }
    }
}
