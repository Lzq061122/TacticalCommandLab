CommandLabProbe 实验脚本集（Phase 10 / H1·H2·H3）
=================================================

【战前设置（一次性）】
  0. ⚠ 环境前提：本实验测的是 Vanilla 原生写入口流量基线，**必须关闭所有修改 Formation AI 的 mod**。
     必须关：CombatBrain / CAI / RBM / FrontlineMod / ImmersiveBattlefields / ChaosBattle /
             PolicyExpansion / CommandWindow / ClanControl
     推荐最小 load order（正好是本模块 SubModule.xml 声明的依赖集）：
       Native + SandBoxCore + Sandbox + StoryMode + CustomBattle + Bannerlord.Harmony + CommandLabProbe
     理由：CombatBrain.dll 字符串级取证显示它引用了本实验观测的全部 4 个写入口
           （SetMovementOrder / SetFacingOrder / SetArrangementOrder / SetControlledByAI）且用了 Harmony。
           两个 Prefix 叠加在同一方法上时，取决于 Harmony 执行顺序，写入可能被它提前拦截、
           根本到不了我们的 Gate() ⇒ [census] 计数失真。详见 docs/10 §7.0。
  1. 启动器勾选 CommandLab Probe，置于 Bannerlord.Harmony 之后。
  2. 模式 = Custom Battle / 自定义战斗；地图 = 平坦野战（不要攻城）。
  3. 玩家 = 小兵/参战者（不要当将军）。
  4. 全程不暂停、不手动下令。

【兵力与兵种：没有硬性要求】
  * 所有指标都是按 tick 累加的比例量或计数，不含绝对兵力项。
    兵力唯一的作用是"别让战斗太快结束"（采样窗口需要 ≥35s）。
  * 兵种完全不做要求，但推荐纯步兵 —— 纯骑兵/纯弓兵会污染 behavior= 归因。
  * 具体建议：
      - 双方各 3~4 个步兵编队（保证 N0/N1/N2/P0 真实存在）
      - 每队 ≥ 20 人
      - 双方兵力接近（各 100~150 人），总兵力 ≤ 800
      - 不要用双方各 1 队 20 人打，那样 N1 不存在且 Arrangement 几乎无流量

【每场流程（约 35~40 秒）】⚠ 一脚本一重启游戏，不是连续跑八把
  ① 复制脚本（必须在启动游戏之前！见下"硬约束"）：
       copy "E:\MBmod\TacticalCommandLab\deploy\CommandLabProbe\scripts\<名>.txt" "%USERPROFILE%\Documents\CommandLabProbe\script.txt"
  ② 启动游戏 → 自定义战斗 → 配好兵力 → 开打
  ③ 载入后全程挂机，脚本到时间自动 dump（每 10s 还有 tick 快照）
  ④ 退出战斗并**退回主菜单/退出游戏**（见下"硬约束"）
  ⑤ 取 %USERPROFILE%\Documents\CommandLabProbe\metrics-<时间戳>-<tag>.txt
     重命名带上脚本名，拷到 E:\MBmod\TacticalCommandLab\tests\artifacts\host\
  ⑥ 换下一个脚本，回 ①（要重新启动游戏）

【硬约束：一脚本一重启游戏】
  ScriptRunner 只在 ProbeSubModule.OnSubModuleLoad（游戏启动那一刻）构造一次，
  script.txt 之后改成什么都不生效 —— 整个进程内只有一份脚本。
  正确循环：改 script.txt → 启动游戏 → 进战斗 → 挂机 35s → 退出游戏 → 改 script.txt → 再启动
  ✗ 错误做法："启动一次游戏、连续进出八场战斗" —— 八场跑的都是同一个脚本。

  为什么不能把八个脚本拼成一个大脚本（省启动次数）：
    1. 时间轴不重置。t 从第一次 OnMissionTick 累加，一场内线性推进；第二场进来时时钟
       继续往前走，t=33.0 dump 早在第一场就触发过了，第二场不会再触发。
    2. ProbeRuntime 是单例（静态 Current，_bindings/_leases/_census/_sampledChanges 全部
       跨战斗存活）。第一场的 lease、hold 累计、写入口计数会被带进第二场，
       unobserved / unauthorized_writes / intent_hold_share 全部不可信。

  同理，"同脚本重放比对"（docs/10 §7.7 第 3 条）也必须重启游戏。

  合计启动次数：9 个脚本 → 9 次；建议挑 census-baseline 与 h2-suppression 各重放一次 → 11 次。

【产物】
  telemetry-<时间戳>-<tag>.txt   原始行：
       Core WriteAttempt 行 + HOST|（hook/behavior/weight 归因）
                            + CENSUS|（观测型 patch 原始值）
                            + SHADOW|（绕过写入口的写入轨迹）
  metrics-<时间戳>-<tag>.txt     [config] [metrics] [verdict] [hold]
                                 [census] [coverage] [patches] [projection]

【entity 命名】
  N0 = 非玩家队 Formation.Index=0（纯 AI 队，默认被测实体）
  P0 = 玩家队步兵
  P = 玩家队，N = 非玩家队，数字 = Formation.Index

【时间单位】
  任务时间（秒），从第一次 OnMissionTick 起累加，不是墙钟。

【判据读法】
  只看 metrics 文件的 [verdict] 段，PASS/FAIL 由 Telemetry 算出，不靠画面。
  P0  = patch 真的打上了（GetPatchedMethods 非空）
  P0b = 写入口真的有原生流量
  ⇒ 这两条 FAIL 时，后面所有"没有原生写入"的结论一律作废。

【必跑顺序】门禁逐级
  1. census-baseline   P0 + P0b 门禁 ← 不过就停，先修装置
  2. h1-external-write H1-a / H1-c
  3. h2-suppression    G1 ∧ G2 必须同表读（仅 G1 PASS 不算夺取成功）
  4. h2-coverage       [coverage] 段 Arrangement 域 unobserved
  5. h3-route-b        H3-a + 记录"Behavior 选择被一起停掉"的代价
  6. rel-none / rel-reapply / rel-reapply-resetweights / rel-after-release
       ⚠ rel-after-release 是刻意设计的反例，预期必须 FAIL。
         它若 PASS，说明装置检不出失败，"其他组 failures=0"就毫无意义。

【文件清单】
  census-baseline.txt    C0 写入口普查：证明 patch 打上了 + 有流量（零干预，纯观测）
  h1-external-write.txt  H1：只写 HOLD 不夺取，测"多久被原生覆盖、来自哪个 hook/behavior"
  h2-suppression.txt     H2：Level 5 + Lease 夺取 Movement，判 G1/G2/G3
  h2-coverage.txt        H2b：覆盖率对照（把 Arrangement 也纳入门控）
  h3-route-b.txt         H3：Route B（SetControlledByAI(false)）对照
  rel-none.txt           REL-1 释放后不恢复（对照组）
  rel-reapply.txt        REL-2 释放前重发最后原生 Order
  rel-reapply-resetweights.txt  REL-3 重发 + ResetBehaviorWeights
  rel-after-release.txt  REL-4 恢复动作放在 Release 之后（预期 FAIL 的反例）

【观测对象从哪来：全量镜像（2026-09-22 定稿）】
  [census] / [coverage] 的计数都发生在 Harmony Prefix → Gate() → Bind(formation) 之后，
  SampleShadow 也只遍历 _bindings。没有绑定 ⇒ 两段全空 ⇒ P0b 判 FAIL，
  但真实原因是"没有观测对象"，会被误读成"原生没有写入"这一架构事实。

  ⚠ 2026-09-21 曾用脚本里三行 `cmd P0|N0|N1 Movement HOLD` 试图"建立绑定" —— **该修法无效，已撤销**。
    原因：ScriptRunner.TryCommand 先 Resolve→FindBinding，找不到就静默 return；
    Command() 根本不被调用 ⇒ Bind() 也不被调用；而 Bind() 此前只在 patch 触发时发生
    ⇒ 鸡生蛋死锁。脚本动作本身也要求实体已绑定，不可能用来建立绑定。

  正解：ProbeRuntime.BindAll() 每 tick 遍历 Mission.Teams → Team.FormationsIncludingEmpty → Bind，
  置于 SampleShadow 与 _script.Tick 之前，不依赖任何脚本动作。
  Bind() 只做 Core 侧注册，不触碰宿主状态。census-baseline 因此回归零干预纯观测。

【首次实测教训（2026-09-22）】
  首跑 census-baseline → P0 FAIL（actual=0），[patches]/[census]/[coverage] 三段全空。
  根因：Harmony Prefix 形参名笔误 2 处 —— Formation.SetMovementOrder 的宿主形参是
  `input`（写成了 order）、Formation.SetControlledByAI 的是 `isControlledByAI`（写成了
  isAIControlled）。Harmony 除 __instance/__0/___field 外按**参数名**匹配，不符即抛异常
  并使 PatchAll() 整体中止 ⇒ 7 个 patch 生效 0 个。
  核对工具：E:\MBmod\CAI\.tools\sigscan（打印方法签名含参数名）。
  改任何 patch 后都应跑一次核对。

【判据读法（装置的三个自检口）】
  [verdict] 的 P0       须 PASS（actual=7）
  [patches] 的 failure  须 (none)
  [patches] 的 count    须 7
  注意：[patches] 列表为空 ≠ 一个都没打上（Harmony 中止前的成功项不回滚），
        必须读 count 与 failure 两行，不要只看列表。
