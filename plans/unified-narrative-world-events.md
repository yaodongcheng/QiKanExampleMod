# 统一叙事架构 + 世界事件 + 复仇女神

> **状态**：计划（未开始实施）
> **前提**：新 session 完整阅读本文件 + `plans/rules/wheels.md`（已造轮子速查）。
> **关联文件**：`plans/npc-commissions.md`（已完成）, `plans/npc-commissions-optimization.md`（8 点优化已完成 7/8）

---

## 一、问题诊断

三个现象指向同一根因：**"先有委托模板，再编造理由"，而不是"先有世界事件，再自然产生委托"。**

| 现象 | 表现 | 影响 |
|------|------|------|
| **旅途/变故事件不可见** | `JourneyEvents.cs` 和 `ComplicationTable.cs` 仅用 `quest.AddLog()` 写任务日志 | 玩家几乎不感知。无弹窗、无选择、无大地图 party |
| **对话文本分裂** | ~460 条硬编码中文串分散在 25 个 `.cs` 文件；`Dialogue.csv`（27行，维度在 ID 里）和 `CommissionNarrative.csv`（56行，维度在列里）schema 不同、查询逻辑不同 | 加一条新对话要改 C#；两张表各自一套代码 |
| **委托无涌现因果** | `FillTargetHero` 从 `Hero.AllAliveHeroes` 随机选目标，不检查真实冲突关系；威胁（匪帮 party）在玩家**接受委托后**才生成 | 委托像"刷出来的任务"，不像"世界正在发生的事" |

---

## 二、新增三层架构（不改动现有 CommissionQuest 核心循环）

```
                     ┌──────────────────────────────┐
                     │   NarrativeResolver +         │  ← 数据层：统一文本查询
                     │   Narrative.csv (单表)         │     替代两张旧表 + 全部硬编码
                     └──────────────┬───────────────┘
                                    │ 所有面向玩家的文本都走这里
        ┌───────────────────────────┼───────────────────────────┐
        │                           │                           │
        ▼                           ▼                           ▼
┌───────────────┐    ┌─────────────────────────┐    ┌──────────────────────┐
│ WorldEvent    │    │ Soft Director           │    │ HeroNemesisTracker   │
│ Simulator     │    │ (控制事件可见性)          │    │ (私人恩怨)            │
│               │    │                         │    │                       │
│ 生成真实危机:  │    │ 五种机制:                │    │ 追踪:                 │
│ 匪患/粮荒/    │    │ 1.就近发现(NPC ! )       │    │ 与每个NPC的交手次数    │
│ 绑架/情感冲突… │    │ 2.路途拦截(求救者追玩家)  │    │ 胜败/伤疤/宿敌等级     │
│               │    │ 3.酒馆传闻(ambient)      │    │                       │
│ 物理后果:     │    │ 4.闲置助推(>5天无委托)   │    │ 宿敌复活 → 新WorldEvent│
│ 生成大地图party│    │ 5.叙事线程(同NPC多次交互) │    │ 主动猎杀玩家           │
│ 影响原版AI    │    │                         │    │                       │
│ 到期自动演化   │    │ 不创造事件 —              │    │ "那道疤还在疼——"       │
└──────┬───────┘    │ 只控制玩家怎样发现          │    └───────────┬──────────┘
       │            └────────────┬────────────┘                │
       │  存入                   │                              │
       ▼                         ▼                              ▼
┌──────────────────┐    ┌──────────────────┐    ┌──────────────────────┐
│ WorldEventDatabase│   │ 导演发现附近事件   │    │ NemesisRevenge       │
│ (存档持久,JSON)   │   │ 决定推送强度      │    │ WorldEvent (新类型)   │
│ Active/Resolved/  │   │ NinjaNotification │    └──────────────────────┘
│ Expired/Escalated │   └──────────────────┘
└────────┬─────────┘
         │ CommissionGenerator 生成委托时优先查这里 → 取真实事件目标/背景
         │ 兜底：没有匹配事件时回退旧的随机生成逻辑
         ▼
┌─────────────────────────────────────────────────────┐
│              现有 CommissionQuest (核心循环不改)      │
│  新增字段: CommissionData.WorldEventId (因果链)      │
└─────────────────────────────────────────────────────┘
```

---

## 三、世界事件类型 — 覆盖江湖的爱恨情仇

> **设计原则**：每个事件类型有独特的情感基调和玩法定位，不堆量、不重叠。
> 灵感来源：天国拯救2（失踪、背叛、假指控、情仇、保护费）+ 荒野大镖客2（逃犯、失助者、文物追回、传奇追猎）。

### 3.1 事件类型总览

| # | 事件类型 | 感情基调 | 受谁影响 | 有加害方？ | 典型委托映射 |
|---|---------|---------|---------|----------|-------------|
| 1 | **BanditRaid** 匪患 | 恐惧、愤怒 | 全村 | 有（匪首） | BountyHunt, VillageDefense |
| 2 | **Famine** 饥荒 | 绝望、无力 | 全城/村 | 无（天灾） | SupplyEmergency |
| 3 | **Kidnapping** 绑架 | 焦急、恐惧 | 一人+其亲友 | 有（绑匪） | BountyHunt（追踪解救）, DecoyMission（引开绑匪） |
| 4 | **Betrayal** 背叛 | 愤怒、被辜负 | 一人/一组织 | 有（背叛者） | LostItem（追回被卷走的财物）, BountyHunt（追捕叛徒） |
| 5 | **DebtTrap** 债务陷阱 | 绝望、屈辱 | 一人/一家 | 有（债主/勒索者） | BountyHunt（摆平债主）, ProcurementAgent（筹款赎身） |
| 6 | **RomanticConflict** 情仇 | 嫉妒、心碎 | 二~三人 | 无（双方各执一词） | ArenaSpecial（为爱决斗）, DecoyMission（协助私奔） |
| 7 | **FalseAccusation** 冤案 | 冤屈、愤怒 | 一人 | 有（真凶） | LostItem（找回证据）, PrisonBreak（劫狱救人） |
| 8 | **InheritanceDispute** 继承争端 | 贪婪、不甘 | 一家族/一派系 | 无（多方争端） | ProcurementAgent（找遗物证明身份）, ArenaSpecial |
| 9 | **Fugitive** 逃犯/隐士 | 怜悯、矛盾 | 一人 | 有（追捕方） | Escort（护送逃脱）, BountyHunt（选择放过还是抓捕） |
| 10 | **TradeDispute** 贸易争端 | 贪婪、不公 | 一城/一商人 | 有（竞争方） | SupplyEmergency, ProcurementAgent |
| 11 | **NobleConflict** 贵族冲突 | 傲慢、尊严 | 两方领地 | 有（敌对领主） | SupplyIntercept, DecoyMission |
| 12 | **SacredTheft** 圣物失窃 | 亵渎、身份危机 | 一门派/家族/文化群体 | 有（窃贼/敌对势力） | LostItem（追回圣物）, BountyHunt（追捕窃贼） |
| 13 | **Assassination** 行刺 | 震惊、权力真空 | 被刺者+其追随者/弟子/族人 | 有（刺客） | BountyHunt（追凶）, PrisonBreak（救被牵连者） |
| 14 | **NemesisRevenge** 宿敌复仇 | 执着、宿命 | **玩家本人** | 有（宿敌） | BountyHunt（特殊：目标是玩家） |

### 3.2 各类型定位详解

#### 1. BanditRaid 匪患 — 集体恐惧
> 最基础的危机。匪帮已经在村外扎营，村民日夜提心吊胆。
- **感情基调**：底层百姓对暴力的无力抵抗
- **与其他类型的区别**：集体威胁 vs Kidnapping（个体威胁）、外敌 vs Betrayal（内鬼）
- **玩法偏好**：Combat

#### 2. Famine 饥荒 — 集体绝望
> 粮食耗尽，粮商囤积居奇。这不是谁造成的，但有人从中牟利。
- **感情基调**：面对天灾的渺小与无力
- **与其他类型的区别**：无加害方 → 玩家面对的不是"打败谁"，而是"救多少人"
- **玩法偏好**：Trade / Scout（找货源、运粮）

#### 3. Kidnapping 绑架 — 个体焦急
> 一个孩子的母亲来到酒馆，她的儿子被绑匪带走了。指定了赎金和地点。
- **感情基调**：亲情的撕裂、时间紧迫的焦虑
- **与其他类型的区别**：个体人身安全、时间压力大、赎金 vs 强攻的道德选择
- **玩法偏好**：Scout（追踪）/ Combat（突袭救出）/ Wealth（付赎金）
- **KCD2 原型**：The Jaunt（贵族之子失踪）

#### 4. Betrayal 背叛 — 被自己人捅刀（含手足相残）
> 商会的账房先生卷款跑了。佣兵队长在战斗前夜投敌。**师兄为了掌门之位杀了师弟。**
- **感情基调**：被信任的人背叛后的愤怒与耻辱。如果是同门/同族相残，情感强度翻倍——"你是我的兄弟/师兄，你怎么下得了手"
- **与其他类型的区别**：内在威胁 vs BanditRaid（外敌）。关键维度是**背叛者和受害者的关系越近，情感冲击越大**（同族 > 同门 > 同商会 > 陌生上下级）
- **玩法偏好**：Scout（追踪）/ Social（劝其回头）/ Combat（清理门户）
- **原型**：KCD2 Mice；江湖任务行：赵寒杀师兄冲冥（"武当之耻"弑兄）

#### 5. DebtTrap 债务陷阱 — 被制度压榨
> 一个自耕农欠了帮派的高利贷，现在帮派要把他的地收了。或者一个村庄向匪帮交了保护费，现在交不起了。
- **感情基调**：屈辱、无力抗争体系
- **与其他类型的区别**：加害方不是暴力匪帮而是"合法"的债权人/勒索者。玩家可以付钱解决、也可以打破这个体系
- **玩法偏好**：Wealth（代付）/ Social（谈判减免）/ Combat（消灭债主势力）
- **RDR2 原型**：收债任务；KCD2 原型：Dancing with the Devil（保护费）

#### 6. RomanticConflict 情仇 — 爱恨交织
> 贵族小姐爱上了平民，家族要她另嫁。或者两个骑士同时爱上一个女人，要决斗。
- **感情基调**：心碎、嫉妒、浪漫
- **与其他类型的区别**：没有纯粹的"坏人"。双方各执一词。感情驱动而非利益驱动
- **玩法偏好**：Social（调解）/ Combat（决斗代表一方）/ Wealth（资助私奔）
- **KCD2 原型**：Hans Capon 的爱情线

#### 7. FalseAccusation 冤案 — 正义感驱动
> 一个老实人被指控为盗贼，真凶另有其人。证据不足但城主要杀鸡儆猴。
- **感情基调**：对不公的愤怒、追寻真相的动力
- **与其他类型的区别**：核心玩法不是战斗而是调查（收集证据、找人证）。目标可能是保护无辜者而非击杀目标
- **玩法偏好**：Scout/Investigation（找证据）/ Social（说服城主）
- **KCD2 原型**：For Whom the Bell Tolls（牧师被控异端）

#### 8. InheritanceDispute 继承争端 — 合法性之争
> 老领主死了，两个儿子都声称自己是合法继承人。遗嘱不见了，或者有两份矛盾的遗嘱。
- **感情基调**：贪婪 vs 对父辈遗愿的尊重
- **与其他类型的区别**：冲突不靠武力解决要靠证据/法理。两边都有合理之处
- **玩法偏好**：Scout（找遗物/遗嘱）/ Social（调解仲裁）
- **RDR2 原型**：The Iniquities of History（追回文物/遗物证明归属）

#### 9. Fugitive 逃犯/隐士 — 道德的灰色地带
> 一个逃兵躲在山里，追捕他的人说他是叛徒，他自己说是被长官陷害。或者一个流浪的女人其实是逃亡的贵族。
- **感情基调**：怜悯、矛盾——这个人可能罪有应得，也可能被冤枉
- **与其他类型的区别**：目标不是"坏人"。玩家的选择是帮助逃亡还是交给法律。道德灰色带最重的类型
- **玩法偏好**：Scout（找到藏身处）/ Social（对话了解真相）/ Combat（帮一方打另一方）
- **RDR2 原型**：The Stranger in the Mirror（多重人格）；KCD2 原型：The Hermit

#### 10. TradeDispute 贸易争端 — 商人的战争
> 城镇里的粮价突然翻了三倍。调查发现不是一个商人囤积，而是一个垄断联盟。或者一个外地商人被本地行会排挤。
- **感情基调**：贪婪与不公，但无血光
- **与其他类型的区别**：没有暴力冲突（初期），解决方式是市场手段或谈判
- **玩法偏好**：Trade / Social
- 保留已有类型，补充 Famine 的天灾定位

#### 11. NobleConflict 贵族冲突 — 领主之间的尊严游戏
> 一个领主在另一个领主的领地边上建了哨站。双方都不退让，小规模摩擦已经开始。
- **感情基调**：尊严、傲慢、政治算计
- **与其他类型的区别**：发生在"高层"（领主 vs 领主），其他类型都是平民受害者
- **玩法偏好**：Social / Combat（代表一方出战）
- 保留已有类型，与 InheritanceDispute 的区别：前者是领主之间的积极冲突，后者是已故领主遗产的被动争端

#### 12. SacredTheft 圣物失窃 — 被盗的不是财物，是身份
> 一个家族代代相传的祖传宝剑被人从祠堂里偷走了。这把剑不只是值钱——它是族长身份的信物，没有它新族长甚至无法召开族会。或者一个领主家族的纹章旗帜被敌对势力盗走，这在战场上等于剥夺了指挥权。
- **感情基调**：传承被玷污——"这是祖宗传下来的东西，怎么能落在别人手里"
- **与其他类型的区别**：不是 LostItem（个人财物——"我的戒指被偷了"），失窃物代表**一个群体/家族/门派的文化身份**。委托方通常是群体的集体意志（族老会、行会、教团），而非孤立的个人。追回不是"找回我的东西"而是"洗刷我们的耻辱"
- **玩法偏好**：Scout（追踪去向）/ Combat（夺回）/ Social（交涉赎回）
- **原型**：RDR2 The Wisdom of the Elders（帮原住民追回圣物）；江湖任务行中盗剑引发全篇

#### 13. Assassination 行刺 — 关键人物被清除
> 城镇总督在自己的房间里被暗杀。下属们陷入互相猜忌——谁是主使？下一个轮到谁？是不是有外部势力在幕后操纵？或者村长在巡查田地的路上被人埋伏杀害。
- **感情基调**：震惊、人人自危——"连他都会被刺杀，这地方还有安全可言吗？"
- **与其他类型的区别**：
  - vs Kidnapping：Kidnapping 是"付赎金可以换人回来"，Assassination 是"人已经没了，只能追凶+处理后事"
  - vs BanditRaid：匪患是针对全村的暴力，行刺是针对**一个人**的精准清除
  - vs NobleConflict：贵族冲突是双方都在明处互斗，行刺是一方先手清除关键人物 → 后续冲突是**连锁反应**（下属互相猜忌、外人趁虚而入）
- **玩法偏好**：Scout/Investigation（追查真凶）/ Combat（缉拿刺客）/ Social（稳定乱局、阻止内部分裂）
- **关键机制**：被刺者死后 → 其追随者/下属/族人进入混乱状态 → 可能连锁触发 Betrayal（内部人趁机篡位）、NobleConflict（外部势力趁虚而入）、FalseAccusation（内部互相指责）
- **原型**：江湖任务行核心事件（掌门被刺→弟子相继死亡→幕后黑手浮出）

#### 14. NemesisRevenge 宿敌复仇 — 你和 NPC 之间的私人恩怨
> **唯一以玩家本人为目标的事件类型**。不通过 NPC 中介，直接 NinjaNotification 弹窗。
- **感情基调**：执着、宿命感——"又是你"
- **与其他类型的区别**：不是 NPC 向玩家求助，是 NPC 来找玩家麻烦
- **玩法偏好**：Combat
- 由 HeroNemesisTracker 生成，不由 WorldEventSimulator 随机生成

### 3.3 感情色彩地图

```
          有加害方（人祸）                    无加害方（天灾/结构性）
              │                                    │
    BanditRaid ●                                  ● Famine
    Kidnapping ●                                  
    Betrayal ●                                    ● InheritanceDispute
    DebtTrap ●                                    ● RomanticConflict
    TradeDispute ●                                
    NobleConflict ●                               
    FalseAccusation ●                             
    Fugitive ●                                    
    SacredTheft ●                                 
    Assassination ●                               
    NemesisRevenge ●                              
              │                                    │
    集体威胁 ←──────────────────────────→ 个体危机
              │                                    │
    BanditRaid            Kidnapping              Famine
    NobleConflict         Betrayal                
    SacredTheft           DebtTrap                
                          FalseAccusation          
                          Fugitive                
                          InheritanceDispute       
                          RomanticConflict         
                          Assassination            
                          NemesisRevenge           
```

### 3.4 跨事件机制：幕后黑手（Conspiracy）

不是独立的事件类型，而是附着在 WorldEvent 上的一个可选隐藏层。灵感来源：江湖任务行中，表面上的盗剑、掌门被刺、弟子互杀——全是紫炎真人一个人在幕后操纵。

```
WorldEvent 新增字段:
  bool HasHiddenMastermind;          // 背后是否有人操纵
  Hero HiddenMastermind;             // 幕后黑手（真实 Hero，可选）
  string ConspiracyId;               // 同一阴谋的多个事件共享此 ID

运作方式:
  1. WorldEventSimulator 生成事件时，低概率（~5%）设 HasHiddenMastermind=true
  2. 幕后黑手是一个真实 Hero（通常是敌对 faction 的领主/有野心的 clan leader）
  3. 同一 ConspiracyId 下的 2-4 个事件共享一条暗线——
     表面上是各自独立的 BanditRaid、Assassination、Betrayal……
     但玩家完成 2-3 个后会开始发现线索指向同一个人
  4. 发现幕后黑手的途径：
     - 击败事件 party 头目 → 审问俘虏（Social 检定）
     - CommissionQuest 完成后玩家 Scout 检定 → 发现事件之间有关联
     - 酒馆闲聊 → 偶然听到同一个名字反复出现
  5. 集齐足够线索 → 解锁特殊委托：直接面对幕后黑手（BountyHunt 传奇级）

设计价值:
  - 将孤立事件串联成"暗线故事"，增加世界层次感
  - 玩家体验从"接任务完成"升级为"我发现了一个更大的阴谋"
```

### 3.5 迭代顺序

阶段 2 MVP 只做 **BanditRaid**（最简单、最基础）。
阶段 5 按以下顺序扩展：

1. **Kidnapping** — 覆盖"个体人身安全"缺口（当前没有）
2. **DebtTrap** — 覆盖"非暴力的制度压迫"缺口
3. **Betrayal** — 覆盖"内部信任+手足相残"缺口（关系越近情感冲击越大）
4. **Fugitive** — 覆盖"道德灰色带"缺口
5. **FalseAccusation** — 覆盖"调查玩法"缺口
6. **SacredTheft** — 覆盖"身份/传承"缺口（不是个人财物，是集体身份象征）
7. **Assassination** — 覆盖"精准清除+连锁反应"缺口（关键人物死亡→多事件并发）
8. **RomanticConflict** — 覆盖"情感驱动"缺口
9. **InheritanceDispute** — 覆盖"法理争端"缺口
10. **Famine** / **TradeDispute** / **NobleConflict** — 完善已有的集体/经济/贵族维度
11. **Conspiracy（幕后黑手）** — 跨事件机制，在 Betrayal/Assassination/NobleConflict 中埋暗线，不占独立迭代

每个类型 ~2-3 天实现，全部 13 种类型 + 跨事件机制约 25-35 天。

---

## 四、第一层：WorldEvent — 核心机制

### 4.1 演员身份：必须用游戏内真实持久 Hero

- **受害者（TargetHero）**：受影响定居点的真实名人/头人/领主。从 `Settlement.Notables` / `Settlement.Owner` 选取。**找不到 → 不生成此事件**。
- **加害方（InstigatorHero）**：两轮策略。①附近 Hideout 的 bandit Hero / 敌对 lord / 真实囚犯 ②全局搜索 ③null。降级规则见下表。
- 匪首在事件中被杀 → **那个 Hero 真的从游戏世界消失**。

| 事件类型 | InstigatorHero 来源 | 找不到真人处理 |
|----------|-------------------|--------------|
| BanditRaid | 附近 Hideout 的 Bandit Hero | 生成模板 party（`IsGenericInstigator=true`），叙事只说"一伙匪徒" |
| Kidnapping | 附近 Hideout 的 Bandit Hero 或敌对 faction 的 Lord | 同上 |
| Betrayal | 与 TargetHero 有关系的真人（同 clan/同城 名人） | **不生成事件** |
| DebtTrap | 附近 GangLeader 名人 或 城镇富商 | **不生成事件** |
| NobleConflict | 敌对 faction 真实领主 | **不生成事件** |
| FalseAccusation | 真凶（随机但有动机的真人） | 生成模板 party |
| InheritanceDispute | 另一方 claimant（同一家族的真人） | **不生成事件** |
| Fugitive | 追捕方（领主/赏金猎人） | 生成模板 party |
| TradeDispute | 竞争商人（城镇名人） | **不生成事件** |
| SacredTheft | 窃贼（附近 Hideout 的 Bandit Hero 或 rogue notable） | 生成模板 party |
| Assassination | 刺客（雇佣的杀手或敌对 faction 的 Lord） | 生成模板 party，叙事只说"不知名的刺客" |
| NemesisRevenge | HeroNemesisTracker 中的宿敌 | **只能是真人**（Nemesis 的定义） |
| Famine | null | 天灾无加害方 |
| Famine | null | 天灾无加害方 |

### 4.2 AI 行为影响

| 行为 | 来源 | 机制 |
|------|------|------|
| 事件 party 向目标移动 | **我们** | `MobileParty.Ai.SetMoveGoToSettlement()` / `SetMoveEngageParty()` |
| 事件 party 到达后与村庄/目标交战 | **原版** | `MapEvent` 自动触发 → `VillageBeingRaided` 状态 |
| 领主巡逻遇敌对 party → 交战 | **原版** | 原生 AI `DefaultPartyAiBehavior` |
| 村庄被劫掠后冒烟/prosperity↓ | **原版** | `VillageBeingRaided` 状态 |
| 商队/村民绕开危险区域 | **原版** | 原生寻路代价地图 |
| 事件 party 在事件解决后移除 | **我们** | `WorldEvent.Resolved` → `RemoveParty()` |
| 事件到期无人解决 → 自动演化 | **我们** | 见 4.3 |

### 4.3 事件生命周期

```
WorldEventSimulator.DailyTick:  (每游戏日执行一次)
  基础概率 ~10% × 区域状态加权 (有无 Hideout、是否战争、prosperity)
    → 选 Settlement (按 prosperity + 距玩家距离加权)
    → 选 TargetHero (从定居点名人)
    → 选 InstigatorHero (两轮策略，按 4.1 表降级)
    → 设 Severity(1-10)、DayLimit(3-15天)
    → ApplyEventConsequences: 生成 MobileParty
    → 存入 WorldEventDatabase
    → 原版 AI 立即开始反应

每日检查:
  - AI 领主消灭了事件 party？→ 自动 Resolved
  - 到期未解决？
    有人祸的类型 → 加害方达成目标 → 受害者受损
    BanditRaid → prosperity -30%, RefugeeCrisis
    Kidnapping → 人质死亡, TargetHero 从世界移除
    Betrayal → 背叛者卷走财物消失
  - 持续 7 天未解决 → escalate (severity+1, 扩 party, 导演推送升级)

事件解决:
  - 玩家通过委托完成 → Resolved + Trust/Infamy 变化
  - AI 自然解决 → Resolved
  - 到期无人解决 → Expired + 受害者受损
```

### 4.4 与 CommissionGenerator 集成

`CommissionGenerator.GenerateCommissionData` 新增第一优先路径：
1. 查 `WorldEventDatabase.GetActiveEvents()` 在委托人附近 (距离 < 80 单位)
2. 按委托类别匹配事件类型（见 3.1 表"典型委托映射"列）
3. 匹配成功 → `data.WorldEventId = event.EventId`；目标用真实 InstigatorHero / TargetHero / Settlement
4. 匹配失败 → 回退旧的随机 `FillTargetHero` / `FillTargetSettlement`

`CommissionQuest` 改动：
- 有 `WorldEventId` → `OnStartQuest` 不新生成 party（用已有的），`OnCompleteWithSuccess` 调 `WorldEventDatabase.ResolveEvent()`

---

## 五、第二层：Nemesis — 让 NPC 记住你

### 5.1 核心思想

击败一个匪首没杀死他 → 他会带伤疤回来复仇 → 变得更强 → 专门猎杀你。这不是新系统，而是 WorldEvent 框架的一个特殊分支（`NemesisRevenge` 事件类型）。

### 5.2 追踪数据 (`HeroNemesisTracker`)

按 `Hero.StringId` 索引，JSON 持久化：
- `TimesEncountered` / `TimesDefeatedPlayer` / `TimesDefeatedByPlayer`
- `IsNemesis`（击败过玩家 ≥1 次）
- `HasScar`（被玩家击败但逃脱）
- `NemesisLevel` 1-5（部队随等级变强）
- `GrudgeOriginEventId`（恩怨起源的 WorldEvent）

调用点：`CommissionQuest.OnMapEventEnded` 中检测有 HeroId 的敌对参战方 → `RecordBattleOutcome(hero, playerWon, heroKilled)`。

### 5.3 复仇因果链

```
WorldEvent 中玩家击败匪首但没杀死他 →
  40% 概率 HasScar=true → ScheduleRevengeEvent(hero, delay:3~13天)

3-10 天后 →
  新 WorldEvent: NemesisRevenge, InstigatorHero=宿敌, TargetHero=玩家
  宿敌 party 在玩家附近生成, AI: SetMoveEngageParty(MainParty) 主动猎杀
  导演用 NinjaNotification 弹窗: "那道疤还在疼——每次下雨都提醒我你是谁。"

  再胜再逃 → NemesisLevel+1 → 部队更强 → 下次间隔更短 → "第二次了……"
  终于杀死 → 记录封存为"已终结" → Hero 从世界移除
  宿敌击败了玩家 → NemesisLevel+1 → 附近 NPC 会提起"击败了你的那个XXX"
```

### 5.4 卧底叛变（轻量）

扩展现有 `DiplomacyIntents`：玩家与敌方 Hero relation > 60 + 曾帮他解决过 WorldEvent + 非 Nemesis → `StrategicInfiltrationIntent` → 在下次同场 MapEvent 中该 Hero 切换阵营。代价：金币或封地承诺。

---

## 六、第三层：Soft Director — 让玩家发现事件

### 6.1 核心哲学

导演**不创造事件**，只控制玩家**怎样发现**已经在进行中的事件。世界自己运转——离得近就大声通知，离得远只能从酒馆闲谈里听到一句。

### 6.2 五种推送机制

| 机制 | 触发条件 | 方式 | 强度 |
|------|---------|------|------|
| **就近发现** | 玩家进定居点，附近 <80 单位有 WorldEvent | 为受影响 NPC 注册 ! 标记（复用已有 `CommissionHubIssue.OnSettlementEntered`） | severity≥7 → NinjaNotification；severity<7 → ! 标记 |
| **路途拦截** | 玩家大地图位置距事件 <50 单位 | 生成"求救村民"MobileParty 追玩家 → `MapEncounterConversationPatch` 开 3D 对话 | 必弹 |
| **酒馆传闻** | 玩家在酒馆选 Chat_Gossip | 回复内容引用远处 Active WorldEvent："东边雷别莱特村遭了匪……" | ambient（不强推） |
| **闲置助推** | 玩家 ≥5 游戏日无活跃委托 | 推送门槛降低：更远的事件也推、更小的 severity 也弹窗 | 加大 |
| **叙事线程** | 同一委托人多次接委托 / 同一反派再出现 | `WorldEventDirector.GetNarrativeThreadContext()` 生成额外维度 → NarrativeResolver 匹配台词变体 | 内容增强 |

### 6.3 导演持久化数据

存入 `MyBehavior.SyncData`：`WorldEventDirectorState`（每个 NPC 经历了哪些事、玩家上次接委托天数）。

---

## 七、第四层：NarrativeResolver + Narrative.csv — 统一文本系统

### 7.1 现状

- `Dialogue.csv`（27 行）：维度编码在 ID 里（`Chat_Greeting_High_Any_Any`），用 `DialogueTemplateHelper.BuildFallbackIds` 逐维退避
- `CommissionNarrative.csv`（56 行）：维度在列里（`Category + Phase + PersonalityTrait + TrustMin/Max + Grade`），用 LINQ 筛选
- ~460 条硬编码中文串分散在 25 个 `.cs` 文件

### 7.2 统一方案

**一张 `Narrative.csv`** 替代全部：

```
Schema:
  ID, EventName, GoalType, Outcome, Category, Phase, PersonalityTrait,
  TrustMin, TrustMax, Grade, Honor, Gender, Identity, Relation, Severity,
  IsNemesis, NemesisLevel, HasScar, TimesEncountered, LastOutcome,
  Text, Emotion

所有维度列可选。空值 = 不筛选。Any = 匹配任意值。
新增维度可随时加列，NarrativeResolver 自动识别。
```

**查询逻辑 `NarrativeResolver.Get(eventName, filters...)`**：
1. 按 EventName 初筛 → 候选行集
2. 对每个提供的 filter 维度精确筛选
3. 无结果 → 逐维退为 Any → 最宽泛匹配
4. 最终兜底 → 代码级 fallback（目标 < 5 条硬编码）

占位符：`{PLAYER}` `{NPC}` `{TARGET}` `{LOCATION}` `{ITEM}` `{REWARD}` `{DEPOSIT}` `{GIVER}` `{COUNT}` `{DAYS}` `{PAYER}` `{WORLD}`

### 7.3 向后兼容

- 旧 CSV 文件保留不动，`GameDatabase.Initialize()` 改为只加载 `Narrative.csv`
- `GameDatabase.Dialogue` / `GameDatabase.CommissionNarrative` 属性改为从 Narrative 筛选子集返回
- `DialogueTemplateHelper` 后端切到 NarrativeResolver，公共 API 不变
- `CommissionNarrative.BuildOpening/BuildClosure` 改调 NarrativeResolver

### 7.4 内容量目标

| 阶段 | 行数 | 来源 |
|------|------|------|
| 阶段 1 | ~83 行 | 合并现有两张表 |
| 阶段 5 | ~550 行 | 迁移 ~460 条硬编码 |
| 阶段 7 | ~600 行 | 补足 personality 变体 + Nemesis 台词 + 新事件类型 Opening/Closure |

---

## 八、涉及文件

### 8.1 新增文件

| 文件 | 路径 | 职责 |
|------|------|------|
| `Narrative.csv` | `ModuleData/DesignData/Narrative.csv` | 统一文本表 |
| `NarrativeResolver.cs` | `Interaction/Intents/NarrativeResolver.cs` | 维度查询 + 回退链 + 占位符替换 |
| `WorldEventDatabase.cs` | `Quests/WorldEvents/WorldEventDatabase.cs` | 数据模型 + JSON 序列化 |
| `WorldEventSimulator.cs` | `Quests/WorldEvents/WorldEventSimulator.cs` | CampaignBehaviorBase：DailyTick 事件生成 + party 生成 + 自动演化 |
| `WorldEventDirector.cs` | `Quests/WorldEvents/WorldEventDirector.cs` | 静态工具类：五种推送 + 叙事线程上下文 |
| `WorldEventNotificationController.cs` | `Quests/WorldEvents/WorldEventNotificationController.cs` | 事件 → NinjaNotification 桥接 |
| `NotificationPipeline.cs` | `Quests/Commissions/NotificationPipeline.cs` | 旅途/变故事件的 CK3 弹窗管道（替代 AddLog） |
| `HeroNemesisTracker.cs` | `Quests/WorldEvents/HeroNemesisTracker.cs` | 宿敌追踪 + JSON 持久化 |

### 8.2 修改文件

| 文件 | 改动要旨 |
|------|---------|
| `Data/DesignDataLoad.cs` | GameDatabase：加载 Narrative.csv → `DataTable.Narrative`；Dialogue/CommissionNarrative 改别名 |
| `Core/MyBehavior.cs` | SyncData 新增三个 JSON 持久化（WorldEventDatabase、HeroNemesisTracker、DirectorState） |
| `Core/MySubModule.cs` | 注册 WorldEventSimulator CampaignBehavior |
| `Interaction/Intents/DialogueTemplateHelper.cs` | 后端切 NarrativeResolver，公共 API 不动 |
| `Quests/Commissions/CommissionGenerator.cs` | FillTarget 第一优先查 WorldEventDatabase |
| `Quests/Commissions/CommissionData.cs` | 新增 SaveableField WorldEventId(60)、IsGenericInstigator(61) |
| `Quests/Commissions/CommissionQuest.cs` | ①有 WorldEventId → 关联已有 party ②OnMapEventEnded → 调 HeroNemesisTracker ③结算 → 解决 WorldEvent ④硬编码文本 → NarrativeResolver |
| `Quests/Commissions/CommissionNarrative.cs` | BuildOpening/BuildClosure → NarrativeResolver |
| `Quests/Commissions/CommissionIntent.cs` | 硬编码文本 → NarrativeResolver |
| `Quests/Commissions/CommissionHubIssue.cs` | 感叹号加入世界事件统计 |
| `Quests/Commissions/JourneyEvents.cs` | 重写：AddLog → NotificationPipeline（弹窗+选择） |
| `Quests/Commissions/ComplicationTable.cs` | 重写：AddLog → NotificationPipeline（弹窗+选择） |
| `Interaction/Intents/` (General/Social/Diplomacy/RecruitSoldier) | 硬编码 DisplayName/ToolTip/Message → NarrativeResolver |
| `Interaction/InteractionController.cs` | OpenChatTopicMenu 等硬编码 → NarrativeResolver |
| `AI/Actions/AtomicAction.cs` | ShowVanillaConfrontation 等 → NarrativeResolver |
| 其余 ~15 个含硬编码的文件 | 逐文件迁移（CommissionHubIssue.cs、StealVM.cs、QuestManager.cs 等），详见探索报告 |

---

## 九、实施阶段

### 阶段 1：统一数据层
- [ ] 创建 `Narrative.csv`（合并旧两张表 ~83 行）
- [ ] 创建 `NarrativeResolver.cs`（维度查询 + 回退链）
- [ ] 修改 `GameDatabase`（加载新表，旧属性向后兼容）
- [ ] 修改 `DialogueTemplateHelper`（后端切换）
- [ ] **验证**：编译通过、对话正常、委托叙事正常

### 阶段 2：世界事件 MVP（只做 BanditRaid）
- [ ] 创建 `WorldEventDatabase.cs`（数据模型 + JSON）
- [ ] 创建 `WorldEventSimulator.cs`（DailyTick → BanditRaid → MobileParty → `SetMoveGoToSettlement`）
- [ ] `MyBehavior.SyncData` 持久化 + `MySubModule` 注册
- [ ] **验证**：开档几天后出现"劫掠匪帮"party → 存档读档后仍在

### 阶段 3：一条完整因果链（BanditRaid → BountyHunt）
- [ ] `CommissionData` 加 `WorldEventId`
- [ ] `CommissionGenerator`：BountyHunt 优先查 BanditRaid
- [ ] `CommissionQuest`：有 WorldEventId → 关联已有 party / 结算 → ResolveEvent
- [ ] `CommissionNarrative`：有 WorldEventId → 叙事引用真实背景
- [ ] **验证**：匪帮 party 生成 → 玩家进附近城 → NPC ! → 目标就是匪首 → 打完 → WorldEvent Resolved → party 消失

### 阶段 4：通知管道 + 导演
- [ ] 创建 `NotificationPipeline.cs`（CK3 弹窗 + 选择）
- [ ] 创建 `WorldEventDirector.cs`（五种推送）
- [ ] 创建 `WorldEventNotificationController.cs`
- [ ] 重写 `JourneyEvents.cs` / `ComplicationTable.cs`（AddLog → 弹窗）
- [ ] **验证**：旅途事件弹窗出现 → 选择"救人"→ 获得金币+经验 → 日志有记录

### 阶段 5：扩展事件类型 + 硬编码文本迁移（可并行）
- [ ] 按 3.4 顺序扩展事件类型（Kidnapping → DebtTrap → Betrayal → ...）
- [ ] CommissionGenerator 每种事件 → 委托匹配
- [ ] 全部硬编码文本迁移到 Narrative.csv（按探索报告逐文件）
- [ ] **验证**：无硬编码中文串残留

### 阶段 6：复仇女神
- [ ] `HeroNemesisTracker.cs`（交手记录 + JSON）
- [ ] `CommissionQuest.OnMapEventEnded` → `RecordBattleOutcome`
- [ ] `WorldEventSimulator.ScheduleRevengeEvent` + `NemesisRevenge` 事件
- [ ] Narrative.csv Nemesis 维度 + 首轮 ~10 条宿敌台词
- [ ] Nemesis party AI：`SetMoveEngageParty(MainParty)` 猎杀
- [ ] `StrategicInfiltrationIntent`（卧底）
- [ ] **验证**：击败匪首 → 带伤疤回来 → 再击败升级 → 杀 →"宿敌已终结"

### 阶段 7：内容充实 + 收尾
- [ ] Narrative.csv 扩展到 ~600 行
- [ ] 叙事线程完善
- [ ] 大地图 party 类型完善（求救村民/商队/追兵/难民）
- [ ] 旧 CSV 标记废弃
- [ ] **全集测试**：新档跑 20 天 → 世界事件自然发生 → 委托来自真实背景 → 旅途中弹窗选择 → 宿敌复仇 → 闭环

---

## 十、关键决策

| 决策 | 方案 | 理由 |
|------|------|------|
| CSV 数量 | 一张 `Narrative.csv`，维度列扩展 | 表分裂 = 查询逻辑分裂 = 两套代码 |
| 事件演员 | 必须用真实 Hero，找不到降级或放弃 | 虚假 NPC 没有涌现感 |
| 委托兜底 | 无匹配事件 → 旧随机逻辑 | 委托永不枯竭 |
| 导演 | 不创造事件，只控制可见性 | 世界运转和玩家体验解耦 |
| 弹窗 | NinjaNotification（揭示）+ InquiryData（选择） | NinjaNotification 沉浸好，Inquiry 支持双按钮 |
| 持久化 | JSON 走 MyBehavior.SyncData | 与 TrustSystem 一致，已跑通 |
| AI 影响 | 借原版为主，自己只做启动/收尾 | 避免冲突原版 AI |
| 向后兼容 | 旧表保留、旧 API 不动、旧存档可加载 | 每个 session 都能回到可运行状态 |

## 十一、风险

- **存档兼容**：新字段全可 null，反序列化空串 → 空集合。旧档加载后 WorldEventDatabase 为空，事件后续慢慢生成。
- **性能**：DailyTick ~10 行逻辑 + NarrativeResolver O(1)+O(n) 小 n → 可忽略。
- **回滚**：旧 CSV 保留不删。切回只需改 `GameDatabase.Initialize`。
- **原版 AI**：不给原生 party 加自定义 component，只用原生 API 设 AI 目的地，其余交原版处理。
