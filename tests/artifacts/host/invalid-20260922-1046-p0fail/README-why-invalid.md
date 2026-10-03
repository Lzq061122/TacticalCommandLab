# 作废轮归档：2026-09-22 10:46 首次进游戏实测

## 作废原因（独立、充分）

**P0 门禁 FAIL** —— `harmony patched methods > 0` 实测 `actual=0`。

根因：Harmony `Prefix` 形参名笔误 2 处（详见 `docs/10` §11.2）

| patch | 我们的 Prefix 声明 | 宿主真实形参 | |
|---|---|---|---|
| `Formation.SetMovementOrder` | `MovementOrder order` | `MovementOrder input` | ✗ |
| `Formation.SetControlledByAI` | `bool isAIControlled` | `bool isControlledByAI` | ✗ |

Harmony 除 `__instance` / `__0` / `___field` 外按**参数名**匹配宿主形参，
不符即抛异常并使 `PatchAll()` **整体中止** ⇒ 7 个 patch 生效 0 个。

`PatchAll` 只扫描**自身程序集**内的 patch 类，因此这两个笔误
**在编译期就决定了失败**，与运行时加载了哪些 mod 无关。

## 现场摘要

```text
[verdict]
P0     harmony patched methods > 0    actual=0    FAIL
P0b    write-port traffic > 0         actual=0    FAIL
[census] [coverage] [patches]  三段全空

telemetry-…-mission-start.txt  唯一内容：
NOTE|t=0.0000|HARMONY_PATCH_FAILED
```

- 战斗时长 164s（10:46:30 → 10:49:14）。脚本自身的 `t=30.0 dump` 按时触发（10:47:00），
  时间线部分正常 —— 失败发生在**更早的 SubModule 加载阶段**。
- 38 个文件，含每 10s 一次的 tick 快照。

## 环境记录（该轮未受控）

| 项 | 值 |
|---|---|
| **CombatBrain** | ⚠️ **处于开启状态** |
| 宿主版本 | v1.3.4 |
| Bannerlord.Harmony | v2.4.2.248 |
| 探针 | `CommandLabProbe`，但**patch 全部未生效** |

**CombatBrain 不构成本轮失败的原因**（根因见上，是编译期事实），但这一环境事实
**独立地会让本轮数据即使 P0 PASS 也不可用**：字符串级取证显示 CombatBrain.dll 引用了
本实验观测的全部 4 个写入口（`SetMovementOrder` / `SetFacingOrder` / `SetArrangementOrder` /
`SetControlledByAI`）且引用 `Harmony`。若它同样 patch 这些方法，两个 Prefix 叠加在同一方法上
⇒ 取决于 Harmony 执行顺序，写入可能被它提前拦截、根本到不了我们的 `Gate()`。

⇒ **后续所有有效采集必须关闭 CombatBrain**（见 `docs/10` §7.0「环境前提」）。

## 保留价值

1. `docs/10` §11 的全部证据溯源。
2. 「装置故障伪装成架构事实」的实例 —— 若当初只有 `P0b` 而没有 `P0`，
   这个"零流量"会被读成"宿主原生不写 MovementOrder"这一硬结论。
3. `[patches]` 段为空但实际可能部分生效的误导性（已修为输出 `failure` / `count` 两行）。

**请勿删除本目录。**
