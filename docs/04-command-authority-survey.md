# 04 · 通用 Command Authority 架构调研

> Phase 4 交付物 · 2026-09-19
> 状态：**已完成（纯文献/资料调研 + 归纳，零代码）**
> 用途：为 Phase 5（实验设计）、Phase 6（抽象建模）、Phase 8（A/B/C 对比）提供**机制取舍依据**与**判定规则**。
> 核心命题：**成熟系统如何决定"谁拥有控制权"**——不是名词解释。

---

## 0. 核心判定结论（先给，后文逐条取证）

| # | 结论 | 证据等级 |
|---|---|---|
| C1 | **成熟系统确定控制权用的是 Ownership，不是 Arbitration。Arbitration 只在 Ownership 已确定后的"合法候选集内部"使用。** | `资料明确证明` |
| C2 | **Ownership 必须带时间边界（Lease）**，因为持有者可能崩溃/暂停/卡死而永不 Release。Lease 的本质是**把 liveness 判定责任反转**：从"服务方证明持有者已死"变为"持有者证明自己还活着"。 | `资料明确证明` |
| C3 | **Ownership 的强制点必须在被保护资源上（写入口），不在锁服务、也不在持有者自身。** 否则持有者暂停后仍会写入脏数据。 | `资料明确证明` |
| C4 | **Preemption 的恢复语义不是一种而是三种**，且必须由**任务性质**决定：可丢弃→Cancel / 进度可重放→Restart / 进度不可重放→Resume。实时系统模式切换协议给出严格对应。 | `资料明确证明` |
| C5 | **通用文献中不存在"Peer 仲裁"这种东西**——所有 peer 结构（Blackboard / Contract Net）都配一个集中控制器（scheduler / announcer）。这与 Phase 3 从 RBM 归纳的"仲裁权不下沉"独立吻合。 | `资料明确证明` + `工程推断`（跨域外推） |
| C6 | **本项目的结构性问题（Phase 1 第 5 类 Native Pipeline）在通用文献中没有对应机制。** 最接近的两条是 Actor 模型（外部只能通过消息进入）与 CQRS（单一权威写入口）。**Phase 8 的 A/B/C 无法从文献获得直接背书，只能靠本项目实验。** | `工程推断` |
| C7 | **冲突的深层语义错配**：Blackboard（多源共享可变状态，无 ownership）vs Command Bus（单一权威写入口）。Phase 2 已证明宿主把 Formation 当 Blackboard 用；而 Mod 需要的是 Command Bus 语义。**这一错配可能是第 5 类冲突的根因。** | `工程推断`（可检验，Phase 7/8） |
| C8 | 除 Arbitration / Ownership 外，还有两种第三方范式：**Conflict Detection（事后检测，MVCC/乐观并发）** 与 **Transformation（值变换层）**。后者已有 BT Decorator 与 RBM `OverrideSetMovementOrder` 两处独立证据，且与 Authority 正交。 | `资料明确证明`（BT Decorator）+ `源码分析`（本项目 Phase 3） |

---

## 1. 证据等级口径（本文档全程遵守）

| 标记 | 含义 | 本文档中的使用纪律 |
|---|---|---|
| `资料明确证明` | 有一手或权威二手资料直接陈述该事实 | 必须给出来源（§8） |
| `源码分析` | 本项目亲自读源码/IL 得到的结论 | 仅用于引用 Phase 2 / Phase 3 已产出结论 |
| `工程推断` | 从上述两类推导、跨域类比、或由本项目的既有结论外推 | **必须写明推断链条**；不得升格为"资料证明" |
| `假设` | 尚未有任何证据支撑，仅作为待验证命题登记 | 必须写明验证 Phase |

**纪律**：任何`工程推断`都不得写成"业界通常做法是……"。反之，凡标`资料明确证明`的条目，§8 必有其来源条目。

---

## 2. 控制权的四要素模型（本文档的组织骨架）

控制权问题不是一个问题，是四个正交维度。任何"Authority 方案"都必须对四者都给出答案，缺一维就会出现 Phase 1 §3.2 那种"看起来有了 Authority，实际压不住"的情形。

| 维度 | 问题 | 通用系统的成熟答案 | 证据等级 |
|---|---|---|---|
| **Subject**（谁） | 谁被允许写？ | 唯一 Owner，非 Owner 的写入被**在资源侧拒绝** | `资料明确证明` |
| **Object**（域） | 对什么拥有？ | 按**资源粒度**划分（文件/键/聚合根/actor 私有状态），而非按"整个实体" | `资料明确证明` |
| **Duration**（时长） | 拥有多久？ | **Lease**：Acquire / Renew / Expire / Release，附单调递增 token | `资料明确证明` |
| **Transition**（转移） | 被顶掉的那个怎么办？ | 由任务性质决定：Cancel / Restart / Resume；并有 graceful window | `资料明确证明` |

> **本项目对照**：Phase 1 的术语表只定义了 Owner 与 Lease（缺 Transition 语义）；Phase 8 的 A/B/C 三路线在 Subject/Duration 上差异明显，但**都没有显式定义 Transition**——这是当前方案设计的最大缺口（`工程推断`，基于与 §6.4 实时系统协议的对照）。

**粒度维度的关键提示**：通用系统几乎都按**细粒度资源**授权（Chubby 的每个文件/目录是一把锁；Orleans 每个 Grain 是独立激活单元；CQRS 每个 Aggregate 是独立写入单元），而不是"整个对象归某人"。这直接支撑 Phase 1 Q4 与假设 H-C 的方向（Movement / Facing / Arrangement 独立 Authority），但**不是证明**——因为授权粒度还必须满足"不变量不被破坏"这一前提，而这一点只能在具体宿主上实测（Phase 10）。证据等级：`工程推断`。

---

## 3. 机制取证

### 3.1 Game AI 侧

#### 3.1.1 Subsumption Architecture（Brooks 1986）

- **它回答什么**：多个行为层如何共存于一个机器人。
- **谁拥有控制权**：**由层号固定决定**。高层可以抑制（suppress）低层的输出；低层永远不知道高层的存在。
- **机制性质**：**结构性 Hierarchy**，不是仲裁。没有评分、没有投票。
- **失效模式**：层间只能做抑制/覆盖，无法协商，也无法让低层"完成收尾"；新增一层需要重新论证与所有下层的关系。
- **证据等级**：`资料明确证明`（Brooks, *A Robust Layered Control System for a Mobile Robot*, IEEE J. Robotics and Automation, 1986；另见 Colledanchise & Ögren 2018 §2.3 对其的形式化比较）。
- **对本项目**：这是"**固定优先级 + 抑制**"的最早工程形态，等价于"每 Tick 让高层赢"的静态版本。它的稳定性来自**结构固定**而非**评分更优**——这提示"抖动"可能来自"赢家会变"，而不是"赢家优先级不够高"。`工程推断`。

#### 3.1.2 Behavior Tree（Isla / Bungie, GDC 2005；Colledanchise & Ögren 2018）

- **它回答什么**：如何组织大量行为而不产生状态爆炸。
- **谁拥有控制权**：**由树的语法结构在每次 tick 时唯一决定**——tick 从根向下传播，Selector 依次尝试子节点，任一时刻"正在 Running 的节点"是唯一的。Isla 原文把 Halo 2 的系统描述为"HFSM / behavior tree / 更准确地说是 behavior DAG"，约 50 个行为。
- **两个关键事实**（与本项目直接相关）：
  1. **Isla 明确把"dithering（在两个或多个动作间快速来回切换）"列为必须不惜代价避免的复杂性代价**（原文：*"we must avoid at all costs the problem of dithering"*）。→ **抖动是被业界点名的架构级风险，不是实现细节。** `资料明确证明`
  2. **Memory Node（有记忆的控制流节点）的作用是"牺牲反应性换取不回退"**（Colledanchise & Ögren 2018 §3.6 *Creating Un-Reactive BTs using Memory Nodes*）。→ **"抗抖动的标准手段是主动降低反应性"，这是一个显式的结构性权衡：Reactivity ↔ Stability。** `资料明确证明`
- **Decorator Node**：*"have only one child and can be used to implement custom policies modifying the returned status of the child"*。→ **这是"值/状态变换层"的通用形态：不改控制权，只改流经它的内容。** `资料明确证明`
- **失效模式**：优先级是**静态写在树里**的；下级无法否决上级。Colledanchise & Ögren §3.4（*Improving Safety using Sequences*）给出的安全做法是**在高层加安全分支**，而不是让下层上报。→ **BT 天生没有"自下而上的 veto 通道"**，这正是 Phase 1 §3.4 Plan Conflict 需要的那条通道的反例。`工程推断`
- **证据等级**：`资料明确证明`（GDC 2005 Proceedings；arXiv:1709.00084）。

#### 3.1.3 Utility AI（Mark / Dill, GDC 2010 & 2012）

- **它回答什么**：在**多个都合理的候选**里选一个，且避免 if/else 的硬跳变。
- **谁拥有控制权**：**无人拥有**——每次评估的赢家可能不同；控制权是"当次评分最高者"，是**逐帧租约**（ lifetime = 1 次决策）。
- **机制性质**：典型的 **Arbitration**。
- **它解决什么**：偏好冲突（Phase 1 第 1 类）。
- **它不解决什么**：权限冲突。Utility 没有"拒绝某个源参与"的概念——它的输入假设是"所有候选都合法"。
- **失效模式**：评分函数连续而输入带噪 → 阈值附近反复翻转 → 抖动。业界标准补丁就是迟滞（hysteresis）与最小驻留时间，与 §3.1.2 的 Memory Node 是同一思想的不同实现。
- **证据等级**：`资料明确证明`（GDC 2010 *Improving AI Decision Modeling Through Utility Theory*；作者 Dave Mark 著有 *Behavioral Mathematics for Game AI*）。
- **交叉印证**：Phase 2 已取证宿主原生仲裁器使用了迟滞折扣（`源码分析`，Phase 2）——说明迟滞是这一层的共识解法，但**它只压制评估噪声，不消除第二个写入者**（`工程推断`，待 Phase 7 E7 验证）。

#### 3.1.4 GOAP / HTN（Orkin, F.E.A.R. 2004；STRIPS 系 planner）

- **它回答什么**：**为了达到目标，动作序列怎么排**——是规划问题，不是控制权问题。
- **谁拥有控制权**：**规划期由 planner 独占，执行期交给 FSM**。Orkin 明确写：F.E.A.R. 的 FSM 只有 3 个状态（GoTo / Animate / UseSmartObject），A\* 负责挑选状态和状态迁移时机。
- **一个重要事实**：GOAP 里**目标本身有固定优先级**（Monolith 后续 GDC 2015 复盘中，Goals 一页明确标注 *Fixed priority* + `IsValid()`）。→ **连"目标选择"这一层也是优先级 + 合法性过滤，而不是自由仲裁。** `资料明确证明`（GDC 2015, Higley, *GOAP: Ten Years Old and No Fear*）
- **Plan 失效的处理**：*"If a situation changes… the steps planned to accomplish his goal are no longer valid. When a plan becomes invalid, the NPC reassesses the situation and either finds alternate means… or activates a different goal."* → **plan 失效是显式触发的重规划事件，不是靠压制。** 这与 Phase 1 §3.3 的"压制 ≠ 取消"完全同构。`资料明确证明`
- ** Squad 语义**：Orkin 原话 *"AI have goals to respond to orders, and it is up to the AI to prioritize following those orders versus satisfying other goals"* → **命令被建模为"目标"，由 agent 自己权衡**。即：命令不是强制写入，而是**进入决策系统的一个候选**。这其实就是路线 C（挂进链内）的语义。`工程推断`（把 orders-as-goals 外推为 integration 路线）
- **证据等级**：`资料明确证明`（Orkin 系列论文；GDC 2015 Monolith 复盘）。

#### 3.1.5 Blackboard（Hearsay-II, Erman et al. 1980）

- **它回答什么**：多个异构专家模块如何协作解一个没有单一算法的问题。
- **谁拥有控制权**：**没有 ownership**。所有 Knowledge Source 都读写同一块共享黑板；**KS 之间从不直接通信**。
- **关键纠正（重要）**：所谓"peer 自治"是误读——Hearsay-II 有**集中的 scheduler/controller**，"*A managerial specialist watched the overall flow of activity and regulated resource allocations*"；BB1 进一步引入独立的 control blackboard。→ **peer 结构必须配集中控制器，否则无解。** `资料明确证明`（Erman et al. 1980；RAND P-6573 综述）
- **属性**：机会主义推理、增量求解、**优雅降级**（去掉部分 KS 仍能产出部分解）。
- **失效模式**：KS 之间无互斥 → 同解空间内互相推翻；收敛依赖调度启发式；**没有"谁在写、谁该停"的语义**。
- **对本项目（关键）**：Phase 2 已证明宿主把 Formation 建模为**多入口共享可变资源**（`源码分析`）。这在架构风格上就是 Blackboard——**多 Writer 共享一块状态、无 owner、无互斥**。而 Mod 需要的是"我写了就得保持住"的语义，那是 Command Bus 语义。**两者是不同架构风格，冲突是语义错配而非实现缺陷**（`工程推断`，结论 C7，待 Phase 7/8 检验）。
- **证据等级**：`资料明确证明`。

#### 3.1.6 Contract Net（Smith 1980）

- **它回答什么**：任务该交给谁做。
- **谁拥有控制权**：**任务所有权通过显式合同转移**——announcer 发布任务、候选者投标、announcer 授标。**授予者是固定的**（manager），变化的是执行者。
- **性质**：Peer 竞价 + 集中裁决（与 Blackboard 同构：peer 贡献 + 集中控制）。
- **对本项目**：这是"**控制权转移需要显式协议 + 明确授予者**"的早期工程证据，支撑"Acquire/Release 必须是对称的显式协议，不能靠抢占实现"。`工程推断`
- **证据等级**：`资料明确证明`（Reid Smith, *The Contract Net Protocol*, IEEE Trans. Computers, 1980）。

#### 3.1.7 RTS 分层指挥链（Squad / CommandAI）

- **它回答什么**：上百个单位如何不做上百套决策。
- **结构**：Strategy → Squad/Combat Manager → 单位，命令逐级下传；单位层只执行不规划（Safadi & Ernst 对 StarCraft 的三层分解：strategic / mid-term / short-term；"*orders generated by the strategy manager are not directly transmitted to individual units*"）。
- **准入校验**：RecoilEngine（Beyond All Reason，开源）的 `CCommandAI` 结构显示命令先过 `AllowedCommand()` 校验再入 `commandQue`。→ **"写入口 = 校验 + 队列"，校验失败即拒绝，是工程常态。** `工程推断`（依据 RecoilEngine 开源结构的二手转述，未亲自核源码）
- **对本项目**：这给出了"Reject 应该长什么样"的成熟样板：**在写入口做准入判定，而不是在决策器里做**。`工程推断`
- **证据等级**：`工程推断`（依据二手文档转述）。

---

### 3.2 通用系统侧

#### 3.2.1 Actor Model（Hewitt et al. 1973；Orleans / Akka / Erlang）

- **谁拥有控制权**：**Actor 独占自己的私有状态**。*"Each actor encapsulates its own state and behavior, never sharing mutable data with another actor"*；*"no two messages are processed concurrently within a single actor"*。Orleans 的 Grain "*在任何时刻只在一个线程上执行*"。
- **核心机制**：**用"不共享"消灭"谁有权写"这个问题**。外部只能通过不可变消息交互。
- **失效模式**：无法表达"两个 actor 对同一物理资源的竞争"——因为模型里根本没有共享资源。跨 actor 一致性需要额外的 Saga/事务机制。
- **对本项目（关键）**：这是唯一一个**从模型层面消除 Authority Conflict** 的范式。但它**不可直接套用**：宿主的 Formation 是共享可变资源，不是 actor。可以借鉴的是其**边界语义**：若 Mod 想获得 Authority，唯一干净的方式是**让所有写入都经由一个入口进入**——即把宿主那块共享状态"包"成一个 actor 式的单一入口。这正是路线 B/C 的共同内核。`工程推断`
- **证据等级**：`资料明确证明`（Hewitt/Bishop/Steiger 1973；Orleans 官方语义）。

#### 3.2.2 Lease / 分布式锁（Chubby, Burrows OSDI 2006）

- **机制**：客户端与 Chubby cell 建立 session，靠周期 KeepAlive 续约；**session 有 lease，超时未续约则会话过期、所持有的全部锁自动释放**。Chubby 明确设计为 **coarse-grained**（锁持有分钟~小时，不是毫秒级）。
- **关键数字**：KeepAlive 占其 RPC 流量的 **93%**（即"续约"是主要运行时成本）；锁持有时间长、锁数量少。→ **Lease 的成本在续约流量，不在获取。**
- **为什么必须过期**：持有者会崩溃。*"if a client crashes, its locks are automatically cleaned up after the session timeout"*。若 Release 是唯一回收路径，持有者一死资源就永久锁死。
- **证据等级**：`资料明确证明`（Burrows, OSDI'06；课程/综述二手一致）。

#### 3.2.3 Fencing Token / Sequencer（Kleppmann 2016；Chubby sequencer）

- **机制**：锁服务每次授予锁时下发**单调递增**的 token；**被保护资源**记住见过的最大 token，**拒绝任何更旧的写入**。Chubby 的 sequencer 就是这个概念的工业原型（lock name + mode + lock generation number）。
- **不变量**：`reject write if token < max_seen`。
- **它解决的正是"持有者暂停"问题**：GC pause 15 秒 → lease 过期 → 别人拿到 token 34 → 原持有者醒来仍写 token 33 → **在资源侧被拒**。
- **三条硬性要求**（直接可用于本项目）：
  1. **token 必须由授予方生成且严格单调**（不能用时钟、不能用 UUID）；
  2. **校验必须在资源侧**（*"A token nobody validates is decoration"*；*"If your storage layer cannot check fencing tokens, you do not have a safe distributed lock"*）；
  3. **读通常不需要 token，写需要**（陈旧读可容忍，陈旧写不可容忍）。
- **对本项目（极强）**：第 2 条直接给出架构约束——**Authority 的 Reject 必须发生在写入口（`Set*Order` 那一级），而不是在 Lab 的决策器里**。写在决策器里的"我不是 Owner 所以我不写"只管得住自己，管不住已发出的在途命令。这对应 Phase 8 指标 `stale_command_execution`。`工程推断`（跨域迁移，待 Phase 7 E6/E8 验证）
- **证据等级**：`资料明确证明`（Kleppmann, *How to do distributed locking*, 2016；Chubby sequencer；etcd/ZooKeeper 的 revision / czxid 用法）。

#### 3.2.4 Preemption（Kubernetes Pod Priority & Preemption）

- **机制**：高优先级 Pod 无法调度时，scheduler 选择**优先级最低、能腾出足够资源的最小受害集合**驱逐；受害者收到 SIGTERM 并享有 graceful termination period（默认 30 s），超时则 SIGKILL。
- **两个关键设计**：
  1. **受害者选择最小化破坏**（"*Pick the candidate with the smallest set of victims*"），不是"谁最弱就杀谁"；
  2. **`PreemptionPolicy: Never`** —— 关键工作负载可被标为**不可被抢占**（sticky / hard reservation）。
- **语义本质**：**先尝试 Bounded（给优雅期），超期降级为 Immediate（强制杀）**。这是 §6.4 中 Bounded→Immediate 降级的现成工程实现。
- **对本项目**：
  - 提供"**可抢占性是资源的一项属性**"这一概念（对应：某个 Domain 是否允许被 Lab 抢占，应是可声明的，而不是硬编码）；
  - 提供"graceful window + 强制回收"的 Release 实现模板。
  - `工程推断`
- **证据等级**：`资料明确证明`（Kubernetes 官方文档 *Pod Priority and Preemption*）。

#### 3.2.5 Cancellation（Structured Concurrency；.NET CancellationToken；Go context）

- **机制**：任务形成树；**取消向下传播、错误向上传播**；子任务生命周期不得超出父作用域（Sústrik 2016 提出，N. J. Smith / Trio 推广，现已被 Kotlin / Swift / Java JEP 462+ / Python TaskGroup 采纳）。
- **关键不变量**：*"a task with no owner is a task whose failure you will find out about from a symptom, not a stack trace"*。→ **生命周期必须有归属**。
- **与 Cancellation 的关系**：结构化并发把"取消"做成**作用域退出时的确定性行为**，而不是可选的协作约定（对比 JS 的 `AbortSignal`：*"It's a convention, not a guarantee"*）。
- **对本项目**：Phase 1 §3.3 要求"Cancellation 并回执"（防止 zombie intent）。结构化并发给出的实现要点是：**取消必须是作用域级别的确定性动作，且失败必须向上传播**，而不是"发个 flag 希望对方检查"。`工程推断`
- **证据等级**：`资料明确证明`（JEP 462/543；Trio / TaskGroup 文档；Sústrik 2016）。

#### 3.2.6 Mode Change Protocol（实时系统）—— **Preemption 恢复语义的严格来源**

- **问题**：多模式实时系统切换模式时，旧模式任务怎么办、新模式任务何时启动。
- **协议的三种语义**（文献一致的分类）：

| 协议 | 语义 | 旧模式任务 | 新模式任务 | 系统状态 |
|---|---|---|---|---|
| **Immediate** | 立即中止并切换 | 立即 abort / suspend | 立即启动 | 任一时刻只在一种模式 |
| **Bounded**（= synchronous） | 有界时间内等到"旧任务全部完成"再干净切换 | 完成当前 job（rem-job） | 等 rem-job 完成后同时启用 | 任一时刻只在一种模式 |
| **Phased**（= asynchronous） | 新旧重叠一段过渡期 | 允许完成 | 在过渡期内逐步启动 | **存在新旧并存的区间** |

- **为什么必须区分**：*"an old-mode task may be immediately aborted, or it may require to complete the execution of its current instance **in order to preserve data consistency**"*。→ **选择依据不是"快不快"，而是"中断会不会破坏数据一致性"。** `资料明确证明`
- **文献明指的风险**：Phased（重叠）"*is the most difficult to analyse as the load on the system is typically higher during the change than it is in either mode*"。→ **过渡期是最危险的窗口。** `资料明确证明`
- **对本项目**：这是"Release / Preempt 之后旧任务怎么办"的唯一严格来源。映射见 §4.4。`工程推断`（映射本身是推断，协议分类是资料证明）
- **证据等级**：`资料明确证明`（Nélis & Goossens, *Mode Change Protocol for Multi-Mode Real-Time Systems upon Identical Multiprocessors*, arXiv:0809.5238；相关综述 arXiv:1102.2094；Burns & Wellings 系统模式切换分类）。

#### 3.2.7 CQRS / Command Bus / Event Sourcing（Fowler / Young / Microsoft）

- **机制**：写模型（command side）与读模型分离；**写模型是唯一权威源**（*"the store of events is the write model, and this is the authoritative source of information"*）。
- **关键收益陈述**：*"Using the stream of events as the write store, rather than the actual data at a point in time, **avoids update conflicts on a single aggregate**"*。→ **单一权威写入口 + 追加式日志 = 从结构上避免同一聚合上的更新冲突。** `资料明确证明`
- **命令语义**：*"CQRS commands are not CRUD operations. They represent business tasks"*；命令先经校验再由聚合根产出事件。→ **Command 是意图 + 校验，不是裸写入值。** 与 Phase 1 的 Intent(L2) / Command(L1) 分层同构。`工程推断`
- **代价**：最终一致（*"eventual consistency between the event store and the projections is inherent"*）。→ **单一写入口不是免费的：它要求所有消费方接受"读到的可能滞后"。**
- **对本项目**：Command Bus 是 Blackboard 的对偶。宿主用 Blackboard 语义，Mod 需要 Command Bus 语义（结论 C7）。同时提醒：**即使取得单一写入口，观测侧（Telemetry / 上层决策看到的）仍可能滞后，不能把"读到的 Order"当作"当前的 Authority 状态"**。`工程推断`
- **证据等级**：`资料明确证明`（Fowler, *CQRS* bliki；Microsoft Azure 架构指南 *CQRS pattern*）。

#### 3.2.8 Authority Levels（Sheridan & Verplank 1978 → Parasuraman 2000 → Playbook Delegation）

这是**唯一一个直接以"授权等级"为研究对象**的成熟谱系，可视为本项目的 Authority 分级蓝本。

**Playbook 的 7 级授权**（Miller & Parasuraman 系工作，DTIC ADA443116）：

| Level | 名称 | 含义 |
|---|---|---|
| Full | 全权委派 | 下级自行决策执行，无需再协调 |
| Inform | 事后告知 | 全权但须报告；上级**无否决权** |
| Override | 可介入覆盖 | 上级保留随时介入覆盖的权利 |
| Approval | 事前批准 | 下级出计划，批准后才可执行 |
| Recommend | 建议 | 下级只出建议，上级保有执行权 |
| Monitor | 监督 | 上级保留全部决策执行权，下级只做监视与批评 |
| None | 未委派 | 无授权 |

**两个必须引用的原文事实**：

1. **文献明确承认 Override 级会引入不稳定**：*"There might be an agreed upon lag period which the subordinate must wait for an override… and **there is no guarantee here that the supervisor's intervention might not produce instabilities in the system**"*。→ **"可覆盖"这个能力本身就是不稳定源**，这与 Phase 1 §3.2（压制 ≠ 取消，会产生每 Tick 重仲裁）独立吻合。`资料明确证明`
2. **授权被明确定位为可量化维度**：Sheridan 1978 十级量表；Parasuraman et al. 2000 把自动化程度映射到信息处理的四个阶段（获取/分析/决策/执行），并指出**同一系统在不同阶段可以有不同自主度**。→ **授权不是"一个布尔值"，而是按阶段/按动作分级的。** `资料明确证明`

**适配本项目的 Authority Level 谱系**（`工程推断`，供 Phase 6/8 使用）：

| Level | 名称 | 语义 | 对应路线 |
|---|---|---|---|
| 0 | None | 只读，仅观测 | Telemetry Observer |
| 1 | Monitor | 可上报建议，不写不否决 | Observer + Suggestion |
| 2 | Recommend | 出 Intent，由宿主决定是否执行 | **C · Native Pipeline Integration** |
| 3 | Approval | 宿主出计划，Lab 否决/批准 | 反向 veto（文献无成熟样板） |
| 4 | Override | Lab 可打断宿主，宿主仍在产出候选 | ⚠️ **文献明确警告的不稳定级** |
| 5 | Full | Lab 独占，宿主冲突写被抑制 | **B · Full Authority Takeover** |

> **判定建议**：Level 4 是"看起来有权、实际最不稳"的档位。若 Phase 7/8 实测显示 L4 的 `oscillation_frequency` 显著高于 L5，则支持"要么 2 要么 5，不要停在 4"这一推论。`假设`（登记为 **H-I**，Phase 7/8 验证）

#### 3.2.9 Conflict Detection（MVCC / 乐观并发）—— **第三种范式**

- **机制**：不阻止并发写，而是**允许都写、事后检测冲突**（版本号 / CAS / 事务冲突回滚）。
- **适用前提**：写入是可版本化的、冲突是可检测的、冲突后是可回滚或可合并的。
- **为什么不适用于本项目**（`工程推断`）：宿主的 Order 写入是 last-write-wins 的裸状态赋值，**既无版本号也无回滚路径**。要套用此范式，Mod 必须自建版本层与回滚能力——成本高于直接做 Ownership。
- **保留价值**：作为 Telemetry 手段有价值（记录"这次写入是否被覆盖"），但不作为 Authority 机制。`工程推断`
- **证据等级**：`资料明确证明`（CQRS/ES 文献中关于 update conflict 的讨论；etcd mod_revision 的 CAS 用法）。

---

## 4. 四组对照（硬性要求）

### 4.1 Arbitration vs Ownership

| 维度 | Arbitration | Ownership |
|---|---|---|
| 回答的问题 | 多个候选里**选哪个** | 哪些源**根本不该成为候选** |
| 输入 | 候选集 + 评分/优先级 | (资源, 域, 请求者, token) |
| 输出 | 一个胜者 | 允许 / 拒绝（二元） |
| 时间性质 | 逐次决策，可每 Tick 重跑 | 跨越多次决策，有时长 |
| 失败后果 | 选错值（质量下降） | 双写 / 抖动 / 不可归因 |
| 典型实现 | Utility、BT Selector、优先级、投票 | 锁 + Lease + Fencing、Actor 私有状态 |
| 证据等级 | `资料明确证明` | `资料明确证明` |

**判定规则（可操作）**：

> **问一句："我能不能让输的那一方停止产出候选？"**
> - **能** → 这是偏好问题，用 Arbitration。
> - **不能**（对方不在我的治理范围内，或对方不知道我的存在）→ 这是权限问题，必须 Ownership + **在写入口拦截**。此时继续做 Arbitration 等于每 Tick 重复一次注定无意义的投票。

**推论**：两者是**串联而非互斥**的——Ownership 先筛掉非法源，Arbitration 在剩下的合法候选里选值。Phase 1 §3.2 的"仲裁的前提是参与者合法"在此得到通用文献支持。`工程推断`

### 4.2 Hierarchy vs Peer

| 维度 | Hierarchy（上下级） | Peer（同级） |
|---|---|---|
| 控制权归属 | 上级决定，下级执行 | 无天然归属，需额外机制 |
| 成熟形态 | BT（静态优先级）、RTS 指挥链、Subsumption、K8s priority | Blackboard + scheduler、Contract Net + announcer |
| 是否真的"无中心" | — | **否。所有可用的 peer 结构都配集中控制器**（结论 C5） |
| 失效模式 A | **下级无 veto 上报通道** → 局部最优、全局失败（Phase 1 §3.4） | **收敛慢 / 循环推翻 / 无仲裁权** |
| 失效模式 B | 上级成瓶颈、单点 | 控制器启发式一旦失准即失控 |
| 证据等级 | `资料明确证明` | `资料明确证明` |

**判定规则**：

- 若**决策目标可静态分层且下层不需要否决上层** → Hierarchy（成本最低，BT/RTS 已验证）。
- 若**下层必须能上报冲突**（存在全局一致性约束）→ 纯 Hierarchy 不够，必须补一条 **veto/上报通道**；**该通道在通用文献里没有成熟样板**（BT 的做法是"在高层加安全分支"，即让上层自己看见，而不是下层上报）。
- **Peer 不能省掉控制器**：想用 peer 就必须先设计那个 scheduler/announcer，否则是"无仲裁的共享写"，即 Phase 1 §3.2 的 Authority Conflict。

**与 Phase 3 的交叉印证**：RBM 的三条可迁移原则之一"**仲裁权不下沉**（不动 FormationAI）"在此获得通用文献独立支持——Blackboard 与 Contract Net 都保留集中控制。`工程推断`（跨域一致性外推，非 RBM 证明）

### 4.3 Lease vs Permanent Ownership

| 维度 | Lease（有期） | Permanent（永久） |
|---|---|---|
| 回收路径 | Renew 失败 → 自动 Expire | 只有显式 Release |
| 持有者崩溃后 | 资源在 TTL 后可再分配 | **资源永久锁死** |
| 运行时成本 | 续约流量（Chubby：KeepAlive 占 93% RPC） | 无续约成本 |
| 需要额外机制 | **Fencing token**（否则旧持有者醒来仍写脏数据） | 需要外部 liveness 检测 |
| 适用 | 持有者可能死/卡住/暂停的环境 | 持有者与资源同生命周期 |
| 证据等级 | `资料明确证明` | `资料明确证明` |

**为什么必须有过期（Reasons，不是结论）**：

1. 持有者可能**崩溃**（Chubby：*"if a client crashes, its locks are automatically cleaned up after the session timeout"*）；
2. 持有者可能**暂停但仍活着**（GC pause、卡顿、Mod 热重载）——这是最危险的一类，因为它**自认为仍持有**；
3. Lease 把 liveness 判定责任反转：**从"服务方证明持有者已死"变为"持有者证明自己还活着"**。前者需要故障检测器，后者只需要一个计时器。

**Release 失败会发生什么（分层列出）**：

| 层级 | 后果 | 是否可被 Lease 兜住 |
|---|---|---|
| 未 Release 但仍在续约 | 资源被无谓占用，别人拿不到 | ❌ 需要独立的"任务完成检测" |
| 已停止续约 | Lease 到期自动回收 ✅ | ✅ |
| 释放后仍有在途写入 | **stale write**（最危险） | ❌ **需要 Fencing token** |
| 释放后宿主侧残留状态 | 恢复失败（Phase 10 必测项） | ❌ 需要显式的 Release 语义（§4.4） |

> **关键**：**Lease 解决"拿不回来"，Fencing 解决"回来的路上旧写入还在飞"。两者必须配套。** 单独用 Lease 的系统是"效率锁"而非"安全锁"（*"If your storage layer cannot check fencing tokens, you do not have a safe distributed lock. You have an efficiency lock at best."*）。`资料明确证明`

### 4.4 Preemption 的恢复语义：Resume / Restart / Cancel

**唯一严格来源**：实时系统 Mode Change 协议（§3.2.6）把"旧任务怎么办"分为三类，且明确选择依据是**数据一致性**而非性能。

| 实时系统语义 | 命令系统语义 | 适用任务性质 | 判定依据 |
|---|---|---|---|
| **Immediate**（立即中止） | **Cancel** | 任务**无中间状态**，或中断不产生不一致 | 执行体是幂等 sink；写入值可随时被下一个值完整覆盖（如"移动到 P 点"） |
| **Bounded / Synchronous**（等旧任务跑完再切） | **Resume**（或先 drain 再接管） | 任务持有**不可重放的进度 / 宿主内部状态** | 中断会留下需要清理的内部状态（如已激活的行为、已分配的资源） |
| **Phased / Asynchronous**（新旧重叠） | **Restart**（旧任务作废重来，允许过渡期） | 进度**可重放/可重建**，且接受过渡窗口的额外负载 | ⚠️ 文献明确：*"load… typically higher during the change than it is in either mode"* |

**三条判定规则（可操作）**：

1. **先问一致性，再问速度**。文献原话：旧任务"*may require to complete the execution of its current instance **in order to preserve data consistency***"。若中断会破坏一致性，Immediate/Cancel 直接出局。
2. **执行体是幂等 sink → Cancel 最简；执行体持有宿主内部状态 → 必须 Resume 或显式 Reset**。后者正是 HANDOFF §10.6 要求 Phase 10 必测的"Release 后是否有残留状态、是否需 Reset"的由来。`工程推断`
3. **能用 Bounded 就不要用 Phased**。Phased 是唯一会**同时存在新旧两个写入者**的语义，等于主动制造 Phase 1 §3.2 的双写窗口；若不得不用，必须给过渡期加上界（K8s graceful period 的做法：先 Bounded，超时降级为 Immediate）。

**工程实现模板**（来自 K8s）：`请求（SIGTERM）→ graceful window（默认 30s）→ 强制回收（SIGKILL）`。对应本项目：`Release 请求 → 等待宿主侧 drain → 超时则强制 reclaim 并记 Telemetry`。`工程推断`

---

## 5. 与 Phase 1 五类冲突的映射矩阵

| 冲突类（Phase 1 §3） | 层 / 性质 | 通用机制 | 证据等级 | 能否解决 | 备注 |
|---|---|---|---|---|---|
| **1 · Command Conflict** | L1 / preference | Arbitration：Utility、BT Selector、优先级、投票、Contract Net 竞价 | `资料明确证明` | ✅ | 硬要求：确定性（同输入同输出） |
| **2 · Authority Conflict** | L0 / permission | Ownership + 细粒度 Domain + Lease + **资源侧 Fencing** + 显式 Acquire/Release | `资料明确证明`（机制）/ `工程推断`（迁移到游戏 AI） | ✅ | Arbitration 不可替代它（§4.1 判定规则） |
| **3 · Intent Conflict** | L2 / commitment | Task 生命周期 + **Cancellation 与回执**（结构化并发的向下取消 + 向上报错）+ Contract 撤回 | `资料明确证明` | ✅ | 关键是"取消"，不是"压制"（GOAP 的 replan 触发是同构证据） |
| **4 · Plan Conflict** | L3 / consistency | HTN / GOAP 重规划、约束传播 | `资料明确证明`（planner 领域） | ⚠️ **部分** | **缺"局部决策者 veto 上报通道"的成熟样板**（§4.2）——BT 的做法是让上层自己看见，不是下层上报 |
| **5 · Native Pipeline Conflict** | L4 / structural | **通用文献无对应机制** | — | ❌ **无现成** | 最接近：Actor 的"外部只能经由消息进入" + CQRS 的单一权威写入口（均为 `工程推断` 的迁移） |

**覆盖率结论（重要）**：

1. **第 1–3 类有充分文献背书**，Phase 6 建模时可直接采用成熟语义，不需要自创。
2. **第 4 类有机制但缺一条通道**：需要自建"veto / 上报"路径，文献只给了反例（BT 把安全分支放在上层）。
3. **第 5 类通用文献完全没有对应条目**——它是 Modding / 插件生态的特有问题（宿主链不认识插件）。**因此 Phase 8 的 A/B/C 不存在"文献公认更优"的选项，必须由本项目 Phase 7/10 的 Telemetry 数据裁决。** 这是对本阶段"不得预设赢家"的正面理由，不是保守表态。
4. **第 2 类的解法在文献里是"标准工程实践"级别**（锁 + 租约 + fencing 是分布式系统的基石），而**当前 Mod 生态普遍在做的是 Arbitration 或频率压制**。这一落差暗示：本项目的收益可能主要来自"把成熟机制搬过来"，而不是"发明新机制"。`工程推断`

---

## 6. 对后续 Phase 的可执行输入

### 6.1 六种机制范式总表（Phase 6 建模的候选集）

| 范式 | 回答什么 | 典型实现 | 适用冲突类 | 不适用 |
|---|---|---|---|---|
| **Arbitration** | 选哪个候选 | Utility / BT Selector / 投票 | 1 | 2、5 |
| **Ownership + Lease** | 谁可以写、写多久 | 锁 + 租约 + fencing | 2 | 1（会取消多源能力） |
| **Conflict Detection** | 写完再查冲突 | MVCC / CAS / 版本 | 1（弱） | 2（宿主无版本/无回滚） |
| **Hierarchy** | 上级说了算 | BT / RTS 指挥链 / Subsumption | 1、3 | 4（缺上报通道） |
| **Commitment + Cancel** | 目标要不要了 | 结构化并发 / Contract 撤回 / replan | 3 | 1、2 |
| **Transformation** | 不改权限，只改内容 | BT Decorator / 中间件管道 / RBM `OverrideSetMovementOrder` | 正交（不属于任何一类） | — |

> **Transformation 的地位**：它**不解决任何一类冲突**，因为它不改变"谁在写"。它的价值是**在不触碰 Authority 的前提下改变写入内容**。Phase 3 把它作为"第六类冲突候选"提出是**过重**的表述——建议改登记为"**与 Authority 正交的第四种机制**"，而非第六类冲突。`工程推断`，建议 Phase 5 用 E10 验证后再定稿（与 Phase 3 结论一致：待 E10）。

### 6.2 抖动（Oscillation）解法分类（供 Phase 7 实验 ⑦）

| 解法 | 作用原理 | 能降低 `unauthorized_writes`？ | 代价 |
|---|---|---|---|
| Hysteresis（迟滞带） | 压制评估噪声引起的翻转 | ❌ 只压制，不消除写入者 | 反应变慢 |
| Min lifetime / Commitment | 时间维度封禁重选 | ❌ | 灵活性下降 |
| Memory Node（BT） | 结构上禁止回退 | ❌ | 明确牺牲反应性 |
| **Ownership + Lease** | **从源头消除第二个写入者** | ✅ | 需要 Acquire/Release 语义 |
| 提高写入频率 / CPS | 竞速 | ❌（**红线**） | 掩盖症状 |

**核心推论**：只有 Ownership 能把 `unauthorized_writes` 降到 0；其余手段只改变 `oscillation_frequency` 的表观值。这直接支撑 Phase 1 的核心论断，并给出 Phase 7 实验 ⑦ 的**判别指标**：看 `unauthorized_writes` 而不是看"看起来稳不稳"。`工程推断`（登记为 **H-F**，Phase 7 验证）

### 6.3 新增待验证假设（登记，Phase 1 的 H-A~H-E 之后的 H-F~H-J）

| ID | 假设 | 来源 | 验证 Phase |
|---|---|---|---|
| **H-F** | Lease + 资源侧 Reject 能把 `unauthorized_writes` 降到 0；Hysteresis / Commitment / 提高频率均不能 | §4.3 + §6.2 | Phase 7 E7 |
| **H-G** | Preemption 恢复语义的选择取决于"执行体是否持有宿主内部状态"：幂等 sink → Cancel 安全；持状态者 → Cancel 会产生残留 | §4.4 规则 2 | Phase 7 E5 / Phase 10 |
| **H-H** | 第 5 类冲突的深层成因是 Blackboard（宿主）与 Command Bus（Mod 期待）的架构风格错配 | §3.1.5 + §3.2.7（C7） | Phase 7 / Phase 8 |
| **H-I** | Authority Level 4（Override）的稳定性显著低于 Level 2 与 Level 5；即"要么集成、要么独占，不要停在可覆盖" | §3.2.8 + K8s/BT 反例 | Phase 7 / Phase 8 |
| **H-J** | Generation / Fencing 校验必须落在写入口才能消除 `stale_command_execution`；放在决策器侧无效 | §3.2.3 第 2 条（C3） | Phase 7 E6 / Phase 10 |

### 6.4 对 Phase 6 抽象建模的约束（来自本文档）

1. **必须显式建模 Transition 语义**（Resume / Restart / Cancel），不能只有 Acquire/Release。依据：§2 四要素模型 + §4.4。
2. **Reject 必须发生在写入口**（`CommandDomain` 的写入函数），不是在上层决策器。依据：§3.2.3。
3. **Lease 与 Token 必须配套**：Lease 管"能不能拿回来"，Token 管"旧命令还能不能写"。依据：§4.3 表。
4. **Authority 应按 Level 声明**（§3.2.8 谱系），而不是一个布尔的 `HasAuthority`。
5. **Domain 粒度授权**（而非整个 Entity），参照通用系统按资源粒度授权的惯例；但**粒度是否可细分到 Movement/Facing/Arrangement 必须由实测决定**（H-C，Phase 10）。

---

## 7. 遗留与不确定性（明确登记，不掩饰）

| # | 遗留项 | 影响 | 建议处理 |
|---|---|---|---|
| 1 | **未亲自核源码**：RecoilEngine `CCommandAI` 结构来自二手文档转述；RBM 结论引用 Phase 3 的静态取证 | 相关条目降级为 `工程推断` | 若 Phase 8 关键依赖，回读 RecoilEngine 源码复核 |
| 2 | **MRTA / 机器人任务仲裁（拍卖、市场机制）未深入** | 可能遗漏"多源协商"的一类机制 | 若 Phase 8 需要"peer 协商"论证，补调研 |
| 3 | **数据库隔离级别 / MVCC 只做了结论性对照**，未系统取证 | §3.2.9 的"不适用"判定是推断 | 若 Phase 8 考虑 Conflict Detection 路线，补调研 |
| 4 | **HTN 的运行时控制权语义弱**：HTN 文献主要讲任务分解，不讲"谁有权写" | §5 第 4 类的"部分解决"可能高估 | 标注为弱证据 |
| 5 | **Orkin 的 orders-as-goals 与路线 C 的等价性是外推**，不是 F.E.A.R. 作者的意图 | 结论 C8 / §3.1.4 的"对本项目"段 | 保持 `工程推断`，不作为背书 |
| 6 | **自动化等级谱系（Sheridan/Playbook）来自人因工程域**，迁移到 Mod↔宿主关系未经任何实测 | §3.2.8 的 Level 谱系 | 由 H-I 承担验证责任 |
| 7 | **本文档没有任何一行代码或实测数据** | 所有 `工程推断` 均未被本项目验证 | Phase 7 起逐条验证；验证前不得写入结论性文档 |

---

## 8. 来源清单

| # | 来源 | 类型 | 用于 |
|---|---|---|---|
| S1 | Damian Isla, *Handling Complexity in the Halo 2 AI*, GDC 2005 Proceedings（gamedeveloper.com） | 一手 | §3.1.2（BT / dithering / 50 行为 / 192K→25K 内存） |
| S2 | Michele Colledanchise & Petter Ögren, *Behavior Trees in Robotics and AI: An Introduction*, CRC Press 2018；arXiv:1709.00084 | 一手 | §3.1.2（Memory Node §3.6、Safety via Sequences §3.4、Decorator 定义、Subsumption 形式化比较 §2.3） |
| S3 | Jeff Orkin, GOAP 系列（*Applying Goal-Oriented Planning for Games* 2003 / *3 States and a Plan: The AI of F.E.A.R.* GDC 2006 / *Agent Architecture Considerations for Real-Time Planning in Games* AIIDE 2005） | 一手 | §3.1.4（3 状态 FSM、plan 失效→重规划、orders-as-goals） |
| S4 | Peter Higley, *Goal-Oriented Action Planning: Ten Years Old and No Fear!*, GDC 2015 | 一手 | §3.1.4（Goals 的 Fixed priority + IsValid） |
| S5 | Dave Mark & Kevin Dill, *Improving AI Decision Modeling Through Utility Theory*, GDC 2010/2012 | 一手 | §3.1.3（Utility 定义与边界） |
| S6 | Lee D. Erman, Frederick Hayes-Roth, Victor R. Lesser, D. Raj Reddy, *The Hearsay-II Speech-Understanding System*, ACM Computing Surveys 12(2), 1980 | 一手 | §3.1.5（Blackboard、KS 不直接通信、managerial specialist 调度） |
| S7 | RAND P-6573（语音理解系统综述，含 Hearsay-II 与 Harpy 对比） | 一手 | §3.1.5（opportunistic reasoning、集中资源分配） |
| S8 | Reid Smith, *The Contract Net Protocol*, IEEE Trans. Computers, 1980 | 一手 | §3.1.6 |
| S9 | Rodney Brooks, *A Robust Layered Control System for a Mobile Robot*, IEEE J. Robotics and Automation, 1986 | 一手 | §3.1.1（层间抑制） |
| S10 | Carl Hewitt, Peter Bishop, Richard Steiger, *A Universal Modular ACTOR Formalism for Artificial Intelligence*, IJCAI 1973 | 一手 | §3.2.1（actor 私有状态、不共享可变数据） |
| S11 | Microsoft Orleans 官方语义（Grain 单线程执行、透明激活） | 一手 | §3.2.1 |
| S12 | Mike Burrows, *The Chubby Lock Service for Loosely-Coupled Distributed Systems*, OSDI 2006 | 一手 | §3.2.2（session lease、KeepAlive、coarse-grained、崩溃后自动清理、93% KeepAlive 流量、sequencer） |
| S13 | Martin Kleppmann, *How to do distributed locking*, 2016 | 一手 | §3.2.3（fencing token、GC pause 场景、校验必须在资源侧、"efficiency lock at best"） |
| S14 | Kubernetes 官方文档, *Pod Priority and Preemption* | 一手 | §3.2.4（最小受害集、graceful termination 30s、PreemptionPolicy: Never） |
| S15 | JEP 462 / JEP 543, *Structured Concurrency*, OpenJDK | 一手 | §3.2.5（parent/child 生命周期、取消向下 / 错误向上；Sústrik 2016 提出、N. J. Smith 推广） |
| S16 | Vincent Nélis & Joël Goossens, *Mode Change Protocol for Multi-Mode Real-Time Systems upon Identical Multiprocessors*, arXiv:0809.5238；及 arXiv:1102.2094 | 一手 | §3.2.6 / §4.4（Immediate / Bounded / Phased；rem-job；数据一致性是选择依据；Phased 负载最高） |
| S17 | Martin Fowler, *CQRS*（bliki）；Microsoft Azure 架构中心 *CQRS pattern* | 一手 | §3.2.7（写模型为唯一权威源、避免单聚合更新冲突、最终一致性代价） |
| S18 | Sheridan & Verplank 1978 自动化十级量表；Parasuraman, Sheridan & Wickens 2000（四阶段自主度）；Miller & Parasuraman 系 *Using Delegation as an Architecture for Adaptive Automation*, DTIC ADA443116 | 一手 | §3.2.8（7 级授权谱系；Override 级不稳定的明确警告） |
| S19 | Safadi & Ernst, *Organization in AI design for real-time strategy games*（StarCraft 三层分解） | 一手 | §3.1.7（策略/中期/短期分层；策略命令不直达单位） |
| S20 | RecoilEngine（Beyond All Reason）`CCommandAI` 结构，经 deepwiki 二手转述 | 二手 | §3.1.7（`AllowedCommand` 校验 + `commandQue`，**未亲自核源码**） |
| S21 | 本项目 Phase 2 产出 `docs/02-bannerlord-control-pipeline.md` | 源码分析 | §3.1.3 迟滞交叉印证、§3.1.5 共享可变资源判定 |
| S22 | 本项目 Phase 3 产出 `docs/03-rbm-case-study.md` | 源码分析 | §6.1 Transformation 范式、§4.2 仲裁权不下沉 |

---

## 9. 本 Phase 明确的「不做」

- 不写代码（Phase 6）
- 不读 CombatBrain 源码（Phase 12）
- 不对 Architecture A/B/C 下结论（Phase 8）——并且本文档已给出**为什么文献也无法替我们下结论**的理由（§5 覆盖率结论第 3 条）
- 不引用 `CombatBrain.*` / `TaleWorlds.*`（红线 D1）
- 不把本 Phase 的任何 `工程推断` 当作已验证结论写入后续文档（§7 第 7 条）
