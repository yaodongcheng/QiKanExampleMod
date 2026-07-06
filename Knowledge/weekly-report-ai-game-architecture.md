# AI 游戏的真正战场不是内容生成，是系统架构

> 2026-07-05 | 基于 LivingWorldNpcs 犯罪后果系统的架构讨论  
> 对话人：yaodongcheng & Claude

---

## 一、核心命题：AI 原生游戏是不是伪命题？

**分两种情况。**

把 LLM 当"万能内容生成器"（AI 写对话、AI 生任务、AI 做地图）——**是伪命题**。生成的内容没有设计意图，没有节奏感，没有"什么时候该给压力、什么时候该给奖励"的判断。生成得多不等于好玩。

把 LLM 当"玩家自由输入 → 系统理解的翻译层"——**不是伪命题，但极难做对**。难点不在于 LLM 能不能理解玩家——而在于理解之后，系统有没有对应的能力去执行。LLM 只能映射到已有系统能力，不能无中生有。且自由文本输入本身有 UX 代价：选择空间过大导致玩家迷茫，从社会推理退化为 prompt engineering。

**真正有价值的方向：AI 作为输出层的叙事渲染器，不是输入层的意图解释器。** 物理引擎替代了手写动画——策划不设计每一帧，而是定义质量、摩擦、重力。LLM 在叙事层可以扮演类似的角色：策划不设计每一句台词怎么措辞，而是定义信息量、态度、约束，让 LLM 去措辞。

---

## 二、涌现感的来源：不是单个事件的深度，是事件之间的连线

旷野之息的元素交互（火+可燃物=燃烧，不区分火把/火箭/火杖）、暗影魔多的复仇系统（兽人之间的权力关系自动推演）、边缘世界的需求驱动行为（"饥饿-30 + 厨房有食物"自动产生吃饭行为）——**它们的共同点不是内容多，是系统之间互相消费对方的数据。**

反观我们的框架：

```
目前的数据流：
  WorldEvent → AttitudeSystem → ResponseGenerator → CommissionQuest
  TheftLedger → 背包UI（仅此而已）
  TrustSystem → 对话门槛
  HeroNemesisTracker → 独立运转

问题：
  每个系统都在消费自己的数据，不消费其他系统的数据。
  偷羊的报复队和暗杀的复仇队打照面了——
  它们不知道彼此是"为同一件事来的"。
```

**涌现感的本质不是"一个事件里有多少种预设的选项"——是"我的行为在一个系统里产生了数据，在另一个系统里我看到了后果"。**

需要的不是新系统，是连线：

```
TheftLedger → AttitudeSystem（被偷过的NPC对任何犯罪更愤怒）
WorldEventStore → CommissionGenerator（跨定居点案底影响委托信任度）
Resolved WorldEvent → NewsSpreadSystem（传播的不只是"出事了"，还有"是谁干的，怎么解决的"）
HeroNemesisTracker → WorldEventStore（被栽赃的NPC出狱后自创调查事件）
```

每一条连线都不需要新系统——已有的系统已经有数据了，只是还没开始互相读。

---

## 三、LLM 在这个框架里的正确位置

| 层级 | LLM 角色 | 是否影响游戏性 | 脱离 LLM 能跑吗 |
|------|---------|--------------|---------------|
| 文本润色 | 把"羊被偷了，赔 900"翻译成更自然的 NPC 台词 | ❌ 纯表现层 | ✅ 模板回退 |
| 叙事多样性 | 同一条语义骨架，每次措辞不同，50 小时后不重复 | ❌ 影响沉浸，不影响玩法 | ✅ 模板回退 |
| 环境叙事 | 从日志中提取策划没预设的细节，让世界"读起来"更鲜活 | ❌ 不影响因果，只影响感知 | ✅ 静态描述回退 |

**LLM 不碰的东西：**
- 玩家选项的可见性和后果（IntentBase 确定性守卫）
- NPC 态度和行为选择（AttitudeSystem + ResponseGenerator 公式）
- 调查进度和嫌犯锁定（InvestigationEngine 公式）
- 资源操作（AgentControlHelper 守恒校验）

**为什么 LLM 不能替代确定性引擎？** 游戏乐趣来自"在约束下做选择"——18 个 Intent 之所以好玩，不是因为少，是因为每个都有代价、有门槛、有后果。变成自由文本输入 → 约束消失 → 游戏性塌陷为 prompt engineering。可见选项本身是对玩家的教育——告诉玩家这个世界怎么运作。

LLM 被锁死在确定性框架的上限里。Intent 框架越丰富，LLM 能理解的策略空间越大；系统连线越多，LLM 能渲染出的世界越真实。**LLM 是放大器，不是发动机。**

---

## 四、策划到底该设计什么

**答案是：设计"世界的物理法则"，而非"玩家会经历什么"。**

四层设计金字塔：

```
④ 叙事翻译层 ← LLM 把状态变成自然语言（策划控制：tone、信息量、必须说/不能说）
③ 行为空间层 ← Intent / ResponsePattern（策划控制：玩家/NPC 能做什么、什么条件解锁）
② 物理规则层 ← EventConfig / AttitudeSystem 公式（策划控制：参数权重和边界）
① 事实记录层 ← WorldEvent 数据模型（策划控制：什么行为记录什么字段）
```

**策划的工作从"设计剧情分支"变成"设计本体论 + 调参数 + 定义 MustConvey/MustNotSay"。**

质量控制的抓手：
- ①层：哪些信息必须记录（不给 LLM 遗漏关键信息的可能）
- ②层：公式的权重校准（severity=30 vs 50 的体验差异是策划调的）
- ③层：什么条件下解锁什么选项（防止 LLM 生成不合理的行为空间）
- ④层：MustConvey 清单（锁死 NPC 必须传达的核心信息），LLM 只在措辞层面发挥

---

## 五、核心议题：本体论 vs 日志流 —— 上下文管理的两条路线

> 这是本次讨论最深的一层。游戏里 NPC 需要"知道发生了什么"才能做出合理的行为。
> 目前我们的做法是策划设计 WorldEvent 的结构化字段（`SuspectHeroId`、`InvestigationProgress`、`StolenItems`……），
> 本质上是**替 NPC 预先定义好"什么信息是重要的"**。
> 那能不能不做这个抽象？直接把原始游戏日志扔给 LLM，让 AI 自己判断什么重要？

这两个选项——**结构化本体论 vs 原始日志流**——不是我们发明的二分法。它在 AI 和计算机科学里已经吵了五十年以上。以下是它的思想谱系。

### 5.1 传统 AI：符号主义 vs 连接主义（1970s–1990s）

**本体论路线 → 符号主义（Symbolic AI）**

Allen Newell 和 Herbert Simon 在 1976 年提出 **"物理符号系统假说"（Physical Symbol System Hypothesis）**：智能的本质是对符号的操作。要实现智能，必须先有一组明确的符号（本体论）和操作这些符号的规则。

工程实践是 **Cyc 项目**（Douglas Lenat, 1984–至今）——人类历史上最雄心勃勃的本体论工程。试图把"人类常识"全部手工编码为逻辑断言：`(#$isa #$Sheep #$LivestockAnimal)`、`(#$hasProperty #$Theft #$requiresVictim)`……三十多年、数百万条断言、数亿美元投入。它确实能做一些推理——但从未真正理解"为什么偷一只羊和偷三只羊，村民的反应不是线性翻三倍"。因为**本体论的脆弱在于：现实中的相关性是无穷无尽的，你不可能把"什么重要"全部预定义。**

**日志流路线 → 连接主义（Connectionism）**

Rumelhart 和 McClelland 的 **PDP（Parallel Distributed Processing, 1986）** 从根本上反对符号主义的预设——智能不需要显式的符号和规则，它可以从原始数据的统计规律中涌现。这一派奠定了今天深度学习的基础。

这两派的张力至今未解。GPT 本身就是一个"日志流"式的胜利——不需要任何人告诉它什么是主语、什么是动词，它从海量原始文本中自己学到了语法、语义、甚至一些推理。

### 5.2 机器人学：表征 vs 反表征（1990s）

**Rodney Brooks, "Intelligence without Representation"（1991）** — MIT 机器人学家，后来创办了 iRobot（Roomba）。他提出了一个激进的论点：

> "世界就是它自己最好的模型。"
> "The world is its own best model."

传统机器人学的做法（本体论路线）：机器人先建立世界的内部 3D 模型，然后在这个模型上规划路径。Brooks 说这太慢了、太脆了——**机器不需要内部世界模型，只需要直接对环境做出反应。** 他的"包容架构"（Subsumption Architecture）没有全局规划、没有内部地图——只有分层的"反射"：躲障碍、沿墙走、漫游。简单的局部规则组合产生了看似有目的的整体行为。

他的论文标题"没有表征的智能"就是对符号主义本体论路线的直接宣战。

**这场辩论的结果不是谁赢了——而是两种路线各有适用场景。** 今天最先进的机器人系统（波士顿动力等）用的是混合方案：底层用反应式控制（日志流思维），高层用模型预测规划（本体论思维）。

### 5.3 软件架构：状态存储 vs 事件溯源（2000s–至今）

**事件溯源（Event Sourcing）** 由 Greg Young 和 Martin Fowler 在 2000 年代推广。传统做法存储当前状态（"账户余额 $500"）——这就是你的 `WorldEvent` 字段。事件溯源存储导致状态的每个事件序列（"存入$1000, 取出$300, 存入$200, 取出$400"）——这就是日志流。

```
状态存储（本体论）：
  Account { Balance: 500, LastModified: "2026-07-05" }
  
事件溯源（日志流）：
  [Deposit($1000), Withdraw($300), Deposit($200), Withdraw($400)]
```

**事件溯源的核心主张：日志是真相的唯一来源（Single Source of Truth）。** 当前状态只是日志的一个投影（projection）。你可以从同一个日志推导出任意视角：账户余额、月开销趋势、异常交易检测……每个视角都不需要修改日志本身。

CQRS（Command Query Responsibility Segregation）进一步区分了**写入模型**（只追加事件，不假设未来查询需求）和**读取模型**（针对具体查询场景优化的视图）。

**对应到我们的游戏框架：**
- WorldEvent 字段 = 一个针对"NPC 对话需要什么信息"场景优化的读取模型
- 原始游戏日志 = 写入模型（事件流，可以从中派生出策划从未预见的读取模型）
- CrimeDialogueBuilder = 把 WorldEvent 投影到对话脚本的投影器

### 5.4 认知科学：表征主义 vs 生成主义（1990s–至今）

**表征主义（Representationalism）** ——经典认知科学的主流范式。大脑是信息处理器，从感官输入中构建外部世界的内部模型（表征），然后在这些表征上进行推理和决策。你的 WorldEvent 字段就是 NPC 对游戏世界的内部表征。

**生成主义（Enactivism）** ——Varela、Thompson 和 Rosch 在 *The Embodied Mind*（1991）中提出的替代范式。认知不是"先在内部构建世界模型，再行动"——认知是**行动过程中涌现的**。蜘蛛不需要"网"的内部模型来织网——它的身体结构、运动模式和环境之间的持续互动自然产生了网。认知不在大脑里——认知在"身体-环境"的耦合中。

**对应到游戏上下文管理：** 你的 `SuspectHeroId`、`InvestigationProgress` 这些字段，就是 NPC 的"内部表征"。你在替 NPC 构建世界的心智模型。生成主义会说：NPC 不需要全局心智模型——NPC 只需要知道"当下这个对话中需要什么信息"，而这个信息应该从最近的互动日志中动态提取，而不是事先存在结构化字段里。

**Andy Clark 的"预测处理"（Surfing Uncertainty, 2015）** 提供了一个折中框架：大脑确实有内部模型（本体论），但这个模型**不是精确的模拟而是预测**。感知实际上就是预测误差——大脑先根据内部模型预测"我应该看到什么"，然后把预测和实际感官输入比较，只处理差异（预测误差）。**大多数信息在预测中被"解释掉了"，只有意外才进入意识。**

这对游戏 NPC 的启示：NPC 不需要知道一切——NPC 只需要一个粗糙的内部模型（WorldEvent 字段），加上注意"模型没预测到的意外"（日志摘要中跟预期不符的部分）。

### 5.5 游戏 AI：从有限状态机到目标导向规划（2000s–至今）

游戏 NPC 的"上下文管理"也有自己的进化史：

| 范式 | 上下文来源 | 代表作 |
|------|-----------|--------|
| **有限状态机（FSM）** | 策划手写的"当前阶段"枚举 | 大多数 2000 年代的 NPC |
| **行为树（Behavior Tree）** | 黑板共享数据 + 条件装饰器 | Halo 2/3, 大多数 3A 游戏 |
| **效用 AI（Utility AI）** | 所有可能行为的效用打分，取最高 | The Sims 3/4, RimWorld |
| **GOAP（Goal-Oriented Action Planning）** | 世界状态（`hasWeapon=true`）+ 目标（`killPlayer`），AI 自动规划动作序列 | F.E.A.R. (2005), Shadow of Mordor |

**GOAP 特别值得关注。** Jeff Orkin 在开发 F.E.A.R.（2005）时面临的问题跟我们一模一样：怎么让 NPC 的行为看起来像是有意图的，而不是按剧本走的？他的方案是：

```
不设计："敌人看到玩家 → 进入掩体 → 射击 → 玩家靠近 → 扔手雷"
而是：
  世界状态：{ playerVisible: true, hasGrenade: true, playerDistance: 15m }
  目标：killPlayer
  可用动作：[Shoot, ThrowGrenade, TakeCover, Flank, Retreat…]
  
  AI 规划器自动搜索从当前世界状态到目标的动作序列。
  每种动作有前提条件（ThrowGrenade 需要 hasGrenade && playerDistance < 20m）
  和效果（ThrowGrenade → playerUnderThreat += 1.0）。
  
  不是策划手写"If玩家靠近就扔手雷"——
  而是 AI 自己在运行时刻算出来"现在扔手雷是实现目标的最优路径"。
```

**GOAP 和我们的 IntentBase + ResponseGenerator 是同一种思想。** F.E.A.R. 的每种动作 = 我们的一个 Intent。F.E.A.R. 的世界状态 = 我们的 WorldEvent + NpcStance。F.E.A.R. 的目标 = 我们的 ResponsePattern 背后的动机（NPC 想"恢复正义"/"保护村庄"/"索要赔偿"）。

区别在于：**GOAP 显式执行搜索规划，我们的系统用"过门坎解锁"的简化版本。** 这两种设计选择有各自的取舍：GOAP 的行为组合爆发力更强（AI 可能组合出设计者没预料到的动作序列），但调参更难、更可能产出"看起来聪明但演出来很蠢"的结果；"过门坎即解锁"更可预测、更易调试，但行为空间的上限更低。

### 5.6 NLP/RAG：知识图谱 vs 稠密检索（2020s）

这是当前最热的本体论 vs 日志流的战场。当你把一份文档扔进 LLM 的上下文窗口时，你怎么选择"哪些片段相关"？

```
知识图谱方案（GraphRAG, 微软 2024）：
  ① 先用 LLM 从文档中抽取实体和关系，构建知识图谱
  ② 查询时，按图结构检索相关子图
  ③ 将子图转换为自然语言摘要，和原始片段一起喂给 LLM

稠密检索方案：
  ① 把文档切成固定大小的 chunk，用 embedding 模型编码为向量
  ② 查询时，用向量相似度检索 top-k 相关 chunk
  ③ 直接把这些 chunk 喂给 LLM
  
中间方案：
  用知识图谱引导检索（KGs 决定"检索哪些维度"），
  但实际喂给 LLM 的是检索到的原始文本片段，不是图谱本身。
```

**这跟游戏里的 NPC 上下文管理是同一个问题：**
- 知识图谱 = 你设计的 WorldEvent 字段（策划决定什么信息是重要的，预先抽取）
- 稠密检索 = 把原始日志全部喂给 AI（让 AI 自己判断什么相关）
- GraphRAG = 我们用结构化骨架锁住"必须传达的信息"，再从日志中补充策划没预设的细节

### 5.7 哲学根源：这个问题比计算机科学更古老

"什么算是相关的信息？"——这本质上是 **Frame Problem（框架问题）**。

Daniel Dennett 在 1984 年的经典论文 *"Cognitive Wheels: The Frame Problem of AI"* 中把它讲透了。框架问题简单说就是：

> 一个智能体要做出合理决策，它需要知道哪些信息跟当前决策相关、哪些不相关。如果它不知道什么相关，它就会永远困在"我是不是漏掉了什么重要的东西？"的无限回溯中。如果要显式地跟这个智能体说清楚"下列情景中只有 A/B/C 是相关的，D/E/F 不用管"——那你实际上已经替它做了所有思考。

**这就回到了你的问题："能不能不设计字段，直接把日志扔给 LLM？"** 不能——不是因为 LLM 不聪明——是因为连人类都解决不了没有预设本体论的"什么信息是相关的"这个问题。每一次有效的思考都依赖一个隐含的本体论——一个关于"什么重要"的先验判断。你的 WorldEvent 字段就是你对这个先验判断的显式化。

但反过来，本体论永远是错的。你的 WorldEvent 捕获了 `WitnessesSilenced = true`，但没有捕获"被威胁后，张三整个人都躲起来了，连日常工作都不做了"。日志里可能有这个模式——但你设计字段时根本没想过要记录它。

**所以不是"本体论 vs 日志流"选一边——而是"本体论负责因果正确性，日志流负责本体论之外的意外丰富性"。**

### 5.8 补充：从其他领域能学到什么具体的

上述谱系不只是学术史——每个传统都对"NPC 上下文管理怎么做"有可操作的启示。

| 来源 | 核心洞察 | 对我们框架的具体启示 |
|------|---------|-------------------|
| **Newell & Simon 符号系统假说** | 智能需要符号，但符号必须是启发式（heuristic）而非穷举 | WorldEvent 字段不需要覆盖所有信息——覆盖"对 NPC 决策必要"的信息即可。分类本身就是在做启发式剪枝 |
| **Brooks 反表征** | 复杂行为可以从简单局部规则中涌现，不需要全局模型 | NPC 不需要知道"全世界正在发生什么"——只需要知道"这个定居点 + 跟我有关的事"。用数据局部性降低本体论的设计压力 |
| **事件溯源 / CQRS** | 写入模型和读取模型应该分离。日志负责写入，投影负责查询 | WorldEvent 字段 = 为"对话场景"优化的读取投影。原始日志 = 真相来源。未来如果有新查询需求（如"玩家过去一个月偷过谁"），不需要改 WorldEvent，从日志重新投影即可 |
| **Simon 有限理性** | 理性受限于信息获取成本、认知能力和时间。最优决策通常不可行，满意决策（satisficing）才是现实 | NPC 做决策时不需要消费所有可用数据。AttitudeSystem 的公式就是对"满意决策"的建模——不是绝对最优的态度，而是"足够好"的启发式计算 |
| **Schema 理论** | 记忆不是存储原始事件，而是存储经过图式（schema）过滤和解译后的版本 | WorldEvent 字段本质上就是 NPC 的"犯罪事件图式"——它抽象掉了具体的时间坐标、动作序列，只保留"谁对谁做了什么、严重吗、有人看见吗"这些图式槽位。新生事件通过匹配这个图式被理解 |
| **Gibson 示能性（Affordances）** | 感知不是读取内部表征，而是直接感知环境"提供什么行动可能"。门把手"示能"转动，楼梯"示能"攀爬 | NPC 看到玩家时，不是在查"此人的 WorldEvent 表"——而是直接"感知"到交互的可能：这个人在我的村庄有案底 → 我可以指控他（Accuse intent 解锁）。Intent 的 Evaluate 本质上就是"情景↔示能"的映射 |
| **Minsky 心智社会** | 智能是大量简单 agent 互动的产物，没有中央控制器 | 你的 9 个系统（WorldEvent / Attitude / Response / CommissionQuest / Trust / Infamy / Nemesis / TheftLedger / NewsSpread）每个都可以是一个独立 agent。它们之间的连线 = agent 之间的消息传递。不需要一个"中央调度器"来协调它们——让它们各自消费彼此的输出 |
| **NoSQL 运动** | Schema-later 在数据摄入时有优势，Schema-first 在查询时有优势；生产系统最终都是混合 | 游戏日志可以"无 schema"写入（不管将来怎么用，先记下来）。WorldEvent 字段是 schema-on-read（为对话场景设计的读取视图）。未来新场景可以从同一份日志派生出不同 schema 的视图 |
| **GraphRAG** | 知识图谱决定"在什么维度上检索"，稠密检索提供"检索到的具体内容" | 结构化骨架（MustConvey）= 决定"NPC 必须聊到哪些维度"。日志摘要 = 在每个维度上 LLM 能找到的具体叙事细节。Ontology 是检索的导航结构，不是检索的内容本身 |
| **预测处理（Clark）** | 大脑不存储世界的完整模型——只存储预测，感知 = 处理预测误差 | NPC 只需要 WorldEvent 的粗糙骨架作为"预测"。跟玩家对话时，如果玩家的行为符合预测（赔钱了），NPC 不需要动用日志。如果玩家的行为偏离预测（威胁目击者、说了一个意料之外的谎），NPC 才需要查日志来处理"意外"。日志不是每次对话都消费——是在"预测误差"触发时才消费 |
| **GOAP** | 分离"能做什么"和"该做什么"。前者是动作库，后者是规划器 | IntentBase = 动作库（"赔钱/威胁/栽赃/辩护"）。AttitudeSystem + ResponseGenerator = 规划器（"当前情境下该做什么"）。加新犯罪类型不需要动规划器——只需要注册新 Intent |

### 5.9 总结：三条设计原则

从这五十年的讨论中，可以提炼出三条对游戏 NPC 上下文管理有效的设计原则：

**原则 1：本体论不可缺，但应该是启发式而非穷举式。**
你不需要预设 NPC 可能需要的所有信息。你只需要预设"对 NPC 的决策和对话有结构性影响"的少数关键维度（谁是犯人、有多严重、有人看见吗、查出是谁了吗）。其余的留在日志里，需要时再取。

**原则 2：写入和读取分离。**
日志负责"记录发生了什么"（写入模型，不预设查询需求）。WorldEvent 负责"NPC 需要知道什么"（读取模型，针对对话场景优化）。未来新场景（如商人信誉评估、跨村通缉）可以从同一份日志派生出新的读取视图，不需要改动写入模型。

**原则 3：NPC 的认知是"满意决策"而非"最优推理"。**
AttitudeSystem 的公式不需要消费所有数据——它只需要消费"足够让行为看起来合理"的数据。当 NPC 的行为被玩家挑战（"你为什么这样对我？"）时，系统才需要深入日志寻找更具体的理由——这是"预测误差驱动的深度查询"，不是常态。

---

### 5.10 深度学习/LLM 的爆发：连接主义的胜利意味着什么

回到一个必然被追问的问题：深度学习和大语言模型的爆发，是不是意味着连接主义赢了、符号主义失败了？对 NPC 设计来说，路线是不是明确了？

**表面答案是"连接主义赢了"——但细看并非如此。**

先承认事实：深度学习在感知任务上碾过了所有符号主义方法。LLM 在语言生成上的表现，任何手写语法规则的系统都追不上。Cyc 项目 40 年、几亿美元、几百万条手工逻辑断言——现在 GPT-4 写一段常识推理，比 Cyc 更像人话。

**但这不等于"符号主义失败了"。等于"纯粹符号主义在感知和语言生成上失败了"——这两个任务的特征空间太大，手工定义特征不现实。**

在需要**可靠性**的领域——形式验证、数据库查询优化、编译器设计、航空控制系统——符号主义仍然在岗。没有任何人用神经网络替代 SQL 查询引擎。为什么？因为查询引擎的错误是不可接受的——1000 条查询里错 1 条，数据就脏了。

**游戏 NPC 的"核心逻辑"更接近谁？** 更接近 SQL 引擎，不是更接近聊天机器人。因为 NPC 的话不是最终产品——NPC 的话是确定性游戏系统的输入。ChatGPT 说错话用户刷新一下就行了——NPC 说错赔偿金额，世界状态就被污染了，后续所有依赖这个值的计算都出错。

**这意味着：连接主义的胜利缩小了本体论的地盘，但没有取消它。**

```
符号主义时代（1970s）：
  要做 NPC 对话，需要手工设计：
  语法规则 + 语义范畴 + 对话策略 + 语用规则 + 事实推理 + 世界状态管理
  └───────────────全部手工本体论───────────────┘

现在（2020s）：
  语法规则 + 语义范畴 + 语用规则 + 对话策略     ← LLM 自动习得（连接主义的实际胜利区）
  事实推理 + 世界状态管理                        ← 仍需手工本体论（符号主义的保留地盘）
```

**边界清晰：LLM 管"怎么说"，本体论管"什么是对的"。**

---

但再深一层：**LLM 的胜利其实不是纯粹连接主义的胜利。** 这个论点反直觉，但值得说。

GPT 不是纯粹从统计模式匹配中产生语言——它是在连接主义架构上，通过自监督学习，**间接习得了符号操作能力**。论文证据：Othello-GPT 实验（Li et al., 2023）——训练一个 GPT 预测奥赛罗棋的走法。模型从未被告诉棋盘状态——但它内部自动形成了棋盘的线性表征。"世界模型"从纯序列预测中涌现了。

这意味着：**连接主义是实现媒介，但不是认知终点。** 一个足够大的连接主义系统，会自己在内部构建出近似符号系统的结构。符号不是被设计进去的——是被"发现"出来的。

这对 NPC 设计的启示是双面的：
- **乐观面**：足够强大的 LLM 理论上可以从原始日志中自己构建出等价于 WorldEvent 的内部本体论——不需要手工定义字段
- **现实面**：这个"自己构建"的过程不稳定、不可审计、有幻觉——在需要因果正确性的场景（NPC 告诉你赔偿金额）无法信任

---

**连接主义的真正遗产，不是"让我们放弃本体论"——是"让我们重新理解本体论从哪来"。**

旧范式：策划坐在桌前，思考"一个 NPC 在面对犯罪时需要知道什么"，然后设计 WorldEvent 字段。这是**设计时的本体论**——在设计阶段手工定义。

新可能：策划仍然定义 WorldEvent 字段（设计时的本体论），但**日志流可以在运行时为 LLM 提供"本体论之外的意外发现"**——一个策划在设计时没想到的叙事细节。两者不替代，是互补。

更重要的是：LLM 可以在设计阶段**辅助策划完善本体论**。把 100 小时的游戏日志喂给 LLM，问它"玩家行为中有哪些反复出现的模式是当前 WorldEvent 字段没覆盖到的？"——LLM 可能发现"玩家在偷窃后经常会先去找目击者说话，然后再去找村长"这个模式，建议增加 `PreConfrontationIntentAttempted` 字段。

---

**所以路线不是"连接主义赢了，放弃本体论"。路线是 Neuro-Symbolic Architecture（神经符号架构）。**

这是目前 AI 研究最活跃的前沿之一：AlphaGo = 神经网络（走子评估）+ 符号搜索（MCTS）；AlphaGeometry = 神经语言模型 + 符号推理引擎；GraphRAG = 知识图谱 + LLM。

**我们的游戏 NPC 框架，本质上就是一个 Neuro-Symbolic 架构：**

```
符号层（确定性，手工本体论）：
  ├─ WorldEvent 数据模型（"什么事实是确定的"）
  ├─ AttitudeSystem（"态度怎么计算"）
  ├─ ResponseGenerator（"NPC 能做什么"）
  ├─ IntentBase（"玩家能做什么"）
  └─ AgentControlHelper（"资源怎么流动"）

神经层（LLM，连接主义）：
  ├─ 叙事措辞（把骨架翻译成人话）
  ├─ 日志细节发现（从原始日志中发掘策划没预设的叙事线索）
  └─ 人设一致性（保持 NPC 的说话风格符合其人格）

接口：
  MustConvey / MustNotSay / Tone = 策划定义的"语义骨架"
  符号层保证正确性，神经层提供丰富性
```

**这不是"追随哪个主义"——这是恰好踩在 AI 研究目前最前沿的范式上。**

而且有一个有趣的对称：这个架构不是我们看了论文之后"设计"出来的——是从游戏开发的工程约束中自然长出来的。你需要 LLM 不可用时游戏不能崩（铁律 1）→ 符号层必须能独立运行。你需要 LLM 不能胡编犯罪信息（铁律 2）→ LLM 必须被 MustConvey/MustNotSay 约束。工程约束推出来的架构，恰好和 AI 研究最前沿的神经符号范式重合。



```
CrimeDialogueBuilder 的 prompt 构造（两段式）：

结构化骨架（策划控制）:
  MustConvey: ["牲口被偷了", "有目击者", "你要赔900第纳尔"]
  MustNotSay: ["目击者是张三"]  ← 因为 WitnessesSilenced = true
  Tone: "Disapproving"
  Stage: "Active"

日志摘要（LLM 消费，200 token 内）:
  "D5.3 张三报告了偷窃。D5.4 张三与玩家的关系从0降到-30。
   D5.6 张三取消了与村长的会面。"

→ LLM 不允许编造 MustNotSay 的信息，不允许遗漏 MustConvey 的信息。
→ LLM 可以从日志中发现"张三吓得连村长都不敢见了"作为叙事细节融入措辞。
→ LLM 不可用 → 回落 PlaceholderResolver 模板。
```

**这一方案的对应关系：**
- 结构化骨架 = 知识图谱（策划锁死"什么必须正确"）
- 日志摘要 = 稠密检索（LLM 从原始流中发现意外细节）
- 两者关系 = GraphRAG（本体论引导检索范围，日志提供血肉）
- 策划的角色 = 本体论设计师 + 先验过滤器
- LLM 的角色 = 措辞引擎，在本体论约束内发挥

---

## 七、当前项目状态

| 层级 | 进度 | 备注 |
|------|------|------|
| ① 统一事件模型（WorldEvent + Store + TheftLedger + EventConfig ×4） | 95% | 比计划更丰富（多物品追踪、18 个 Intent vs 计划 14 个） |
| ② 调查引擎（AdvanceInvestigation + TryLockSuspect） | 100% | 目击/证据/反侦察/熟人识别 四修正项 |
| ③ 态度系统（ComputeStance + ResponseGenerator ×10 模式） | 100% | 人格/关系/身份/严重度 全参数化 |
| ④ 行动生成（ProcessAuthorityAction + 报复/SendThugs/上报） | 85% | AI 不等玩家，自主推进 |
| ⑤ 玩家介入（18 Intent + CrimeDialogueBuilder 动态对话） | 90% | 栽赃完整流程（子选项+证物+fail forward+大人物第二道坎） |
| ⑥ 偷动物端到端 | 100% | 动画→库存→WorldEvent→DailyTick→对话注入 |
| ⑦ 其他犯罪类型端到端 | 15% | EventConfig 已有，Mission 层触发点未接入 |
| LLM 叙事翻译 | 0% | 架构预留，CrimeDialogueBuilder 已具备接 LLM 的条件 |
| 系统间连线 | 5% | 各系统独立运转，尚未互相消费数据 |

**下一步优先级：**
1. **系统连线**（最少代码量，最大涌现收益）
2. **非 AnimalTheft 犯罪接入**（验证框架通用性）
3. **LLM 叙事翻译**（CrimeDialogueBuilder + 日志摘要器）

---

## 八、关键洞察

1. **AI 游戏的正确战场不在"内容生成"**——在"让已有的系统互相消费数据"。涌现来自连线，不来自枚举。

2. **LLM 的交互自由度上限被确定性系统的能力上限锁死。** 框架越扎实，接 LLM 时它越强大。

3. **策划的工作从"写剧本"变成"定义物理法则"。** 本体论设计 + 参数校准 + MustConvey 约束 = 策划的新技能栈。

4. **不要用 LLM 解释玩家输入。** 让 LLM 翻译系统输出。游戏性不受 LLM 不稳定性影响，沉浸感受益于 LLM 的多样性。

5. **纯日志流不可行。** 框架问题（Frame Problem）是根本性的——没有预设本体论，连"什么信息相关"都判断不了。但同时，纯本体论会遗漏策划设想不到的丰富细节。唯一正确的答案是**二者分工**：本体论锁死因果正确性，日志流提供意外叙事细节。

6. **"本体论 vs 日志流"不是我们发明的二分法。** 它是 AI 史上贯穿始终的一条主线——从符号主义 vs 连接主义（1976）、Brooks 的"没有表征的智能"（1991）、事件溯源 vs 状态存储（2000s）、到 GraphRAG vs 稠密检索（2024）。每一次迭代都让两种路线的边界更清晰：**本体论负责"必须不错"，日志流负责"可能更好"。**

---

## 参考资料

| 主题 | 文献 |
|------|------|
| 物理符号系统假说 | Newell & Simon, *Computer Science as Empirical Inquiry: Symbols and Search* (1976) |
| 有限理性 | Herbert Simon, *A Behavioral Model of Rational Choice* (1955); *The Sciences of the Artificial* (1969) |
| 反表征智能 | Rodney Brooks, *Intelligence without Representation* (1991) |
| 包容架构 | Rodney Brooks, *A Robust Layered Control System for a Mobile Robot* (1986) |
| 生成主义 | Varela, Thompson & Rosch, *The Embodied Mind: Cognitive Science and Human Experience* (1991) |
| 示能性 | J.J. Gibson, *The Ecological Approach to Visual Perception* (1979) |
| 心智社会 | Marvin Minsky, *The Society of Mind* (1986); *The Emotion Machine* (2006) |
| 预测处理 | Andy Clark, *Surfing Uncertainty: Prediction, Action, and the Embodied Mind* (2015) |
| 框架问题 | Daniel Dennett, *Cognitive Wheels: The Frame Problem of AI* (1984) |
| 图式理论 | F.C. Bartlett, *Remembering: A Study in Experimental and Social Psychology* (1932) |
| GOAP | Jeff Orkin, *Applying Goal-Oriented Action Planning to Games* (2004) — F.E.A.R. 的 AI 架构 |
| 事件溯源 | Martin Fowler, *Event Sourcing* (2005); Greg Young, *CQRS Documents* (2010) |
| GraphRAG | Microsoft Research, *From Local to Global: A Graph RAG Approach to Query-Focused Summarization* (2024) |
| Othello-GPT / 涌现世界模型 | Li et al., *Emergent World Representations: Exploring a Sequence Model Trained on a Synthetic Task* (2023) — 证明 LLM 在训练中自发形成内部符号表征 |
| 神经符号架构 | Garcez & Lamb, *Neurosymbolic AI: The 3rd Wave* (2023); AlphaGeometry, DeepMind (2024) |
| 涌现式游戏设计 | Tynan Sylvester, *Designing Games: A Guide to Engineering Experiences* (2013) — RimWorld 作者 |
| 元素交互涌现 | 任天堂, *The Legend of Zelda: Breath of the Wild* (2017) — GDC 演讲"Breaking Conventions with The Legend of Zelda" |
| 复仇系统 | Monolith Productions, *Middle-earth: Shadow of Mordor* (2014) — Nemesis System GDC 演讲 |
| 本体论工程 | Douglas Lenat, *CYC: A Large-Scale Investment in Knowledge Infrastructure* (1995) |
| NoSQL/Schema-later | Patrick Linskey, *NoSQL: Principles and Practices* (2009); Martin Kleppmann, *Designing Data-Intensive Applications* (2017) 第 4-5 章
