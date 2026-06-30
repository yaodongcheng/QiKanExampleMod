# 村庄动物偷窃后续反应系统 — 完整设计

> **状态**：设计阶段（对齐中）
> **关联文件**：`plans/rules/wheels.md`（已造轮子）、`plans/rules/narrative-design.md`（叙事铁律）、`Knowledge/原版骑砍2任务系统分析.md`（Issue-Quest 架构）
> **配套系统**：`Stealth/VillageAnimalTracker.cs`（偷窃追踪）、`Quests/Commissions/CommissionHubIssue.cs`（信号 Issue 范本）、`Quests/WorldEvents/HeroNemesisTracker.cs`（宿敌追踪）、`Quests/WorldEvents/WorldEventConfig.cs`（世界事件配置）

---

# 设计篇

---

## 零、设计目标

**玩家偷了村庄动物后，村庄不会"无事发生"。** 村民会经历发现→调查→锁定→报复四个阶段，每个阶段都是一个独立的 **Issue-Quest 对**（对标原版双层架构），玩家**随时可以介入**——无论他自己是不是贼。
由于我们是单机游戏，因此有如下几种情况
-贼是玩家自己：对玩家来说，又分为已经被发现和还没被发现两个分支。如果还没被发现，可以自首，也可以误导、嫁祸别人。
-贼不是玩家自己：对玩家来说，就是一个单纯的偷窃事件，去抓真贼就行

核心原则：
1. **每阶段都是可见的 Issue（`!`）+ 可接的 Quest** — 玩家可以观察、接取、或误导
2. **AI 自动推进不依赖玩家** — 玩家不接 Quest，调查也会进行
3. **调查结果有不确定性** — 可能查错人、不是必然指向玩家，小概率不了了之
4. **叙事遵守铁律** — 从玩家视角出发，文本并非冷冰冰的文字，而是要从每个npc个体的视角出发睡哦怎么看来某件事。对于玩家收到的传闻通知，也要有传闻的感觉，比如 "村民怀疑你"
5. **以 KCD2/RDR2 水准要求** — 开放世界该有的反应都要有

---

阶段说明：

整个系统是一条 **「事实 → 认知 → 行动」** 的单向推进链：偷窃**事实**在动作发生当下就被记录（谁偷的、偷了多少、有没有人看见），但村民的**认知**（嫌犯是谁）和**行动**（悬赏 / 报复）是随时间逐步浮现的——这中间的落差，正是玩家可以介入操纵的空间。

设计目标里说的「发现→调查→锁定→报复」是**叙事上的四拍**，落到实现上压缩成 **三个 Issue-Quest 对**（调查并入发现阶段，见 `TheftCaseStage` 枚举）：

| 叙事拍子 | 实现阶段 | 案件状态 (`TheftCaseStage`) | 村民态度 | 玩家可做什么 |
|---------|---------|---------------------------|---------|------------|
| 发现 + 调查 | **阶段 1** | `Discovery` | "牲口少了，但不知道是谁" | 接调查 Quest（认真查 / 误导 / 嫁祸）、私下封口、或放任 AI 自动掷骰推进 |
| 客气谈判 | **阶段 2** | `SuspectIdentified` | "是 {嫌犯} 干的，把他找出来" — 客客气气要债 | 嫌犯≠己 → 接追捕 Quest，单人赏金猎人模式；嫌犯=己 → 村长还愿意谈：赔钱 / Charm 辩护 / 威胁 |
| 忍不住动手了 | **阶段 3** | `Retaliation` | "自己动手" — 怒气爆棚开打 | 嫌犯≠己 → 带队（一群愤怒村民去打人）；嫌犯=己 → 对话窗口关闭，只剩打/逃/投降/被追上和解 |
| —（终局） | — | `Resolved` | 案件了结 | 抓到真贼 / 赔钱了事 / 报复完成 / 冷案超时，任一路径都收束到此 |

阶段 2 → 3 的本质升级：**从"好好谈"到"没得谈"。** 阶段 2 村长把你当欠债的——还钱/说清楚就行；阶段 3 村长把你当仇人——话已经说完了，现在只动手，或者说如果还想谈就得付出更大的代价。

两条铁规贯穿全部三个阶段：

1. **AI 不等玩家**——玩家完全不接 Quest，调查也会每日掷骰自动推进、自动锁定嫌犯、自动 spawn 报复部队。Quest 只是玩家"插手改写结局"的入口，不是剧情的发动机。
2. **嫌犯不必然是玩家**——调查结果带不确定性（受目击人数、玩家 Roguery、是否主动误导影响），可能锁定真凶、嫁祸对象、无辜路人，甚至 7 天查不出变成冷案。玩家是不是贼，只是众多分支里的一支。

阶段之间如何迁移，第一道分水岭是**偷窃当下有没有被目击**——这决定了案件从阶段 1（完整调查）还是直接跳到阶段 2/3 起步：

### 目击后果分流

```
偷窃动作执行
    │
    ├─ 有人目击 (WasWitnessed = true)
    │   → ThiefHeroId = Hero.MainHero（当场确定）
    │   → 目击者可能当场大喊，然后周围其他Npc过来围观
    │   │
    │   ├─ 被当场抓住 → 当场对峙（mission 内即时事件，不进阶段机）:
    │   │   ├─ 认错赔钱 → Resolved（当场了结）
    │   │   ├─ 打翻村民逃跑 → 直接进阶段3（已经动手了，跳过"好好谈"的阶段2）
    │   │   └─ 被村民制服 → 惩罚 cutscene → Resolved
    │   │
    │   └─ 没被当场抓住（跑掉了）
    │       → 直接进阶段2（嫌犯=玩家，跳过阶段1调查）
    │       → 村长没抓到现行，虽然认定是你，但仍愿意悬赏/谈——"客客气气要债"
    │
    └─ 没人目击 (WasWitnessed = false)
        → ThiefHeroId = null（未知嫌犯）
        → 玩家安全离开
        → 下次 DailyTick 村民发现东西少了 → 进阶段1（完整调查流程）
```



## 一、偷窃没被发现后的玩家体验总览：三条典型路径

### 路径 A：栽赃嫁祸（Roguery 流）—— 找替罪羊脱身，可顺手坑仇人

```
偷羊 → 第二天回村 → 看到 `!` → 找村长接 Quest（调查任务）
→ [若有 notable 证人看见你] 先 Roguery 收买/吓唬，否则汇报会被戳穿
→ 向村长汇报，选嫌犯（候选由 PlayerTheftLedger 生成）：

   ├ "是附近藏身处的强盗干的！"          ← 目标①：没脸的"强盗"替罪羊（干净脱身）
   │   纯 Roguery 检定，不需证物——"强盗偷牲口"天经地义，村长天然愿意信
   │   → 嫌犯 = 强盗头子 → 接追捕 Quest → 清藏身处 → 双份报酬
   │   → 零后果：强盗没有出狱复仇这回事
   │
   └ "是 {某 Hero} 干的！"               ← 目标②：嫁祸具体仇人（一石二鸟）
       前提: 账本有他记录 且 背包仍持有从他身上偷来的随身物
       [出示证物]：拿出他的随身物（不消耗，给村长看一眼）→ 检定 + 固定加成
       → 嫌犯 = 该 Hero → 接追捕 Quest → 活捉带回 → 报酬 + Trust +10
       → 他出狱 → HeroNemesisTracker 记宿敌（嫁祸无辜的道德重量，以后复仇）

→ 村长感激不尽，Trust +10 → 偷羊完全脱身 ✓ 完美犯罪

两个目标的博弈不在检定，在【选谁】:
  目标① 强盗 = 最干净，DC 最低，纯 Roguery 裸过，无后续
  目标② 仇人 = 脱身 + 一石二鸟，但 DC 因目标身份而异（流浪汉易、商人难、领主极难），
             道具只是拉高成功率的手段；最重的代价不在检定——而在出狱后的复仇
  目标② 栽赃大人物（商人/领主）= 过了 belief 检定还没完，村长认出是大佬会想缩——
       玩家必须二次检定（Charm 激将 / Roguery 恐吓）推村长一把，失败则案子被压下

注：栽赃不需要"去现场丢东西再发现"。玩家自己就是调查者，
    汇报对话里的 [出示证物] 就是栽赃落地的那一下——一个动作
    同时完成"声明嫌犯 + 提交证据"。

注：被"戳穿"靠村长【转述并点名引述】证人，不把目击者拉到现场。
    村长冷脸:"别装了，西头的老猎户克拉昨儿就来找我，说亲眼瞧见是你。"
    - 为什么转述：notable 证人按 heroId 存，但他可能是外乡人/住别村，
      第二天不保证在场；转述无依赖、永远能演，也符合叙事铁律(情报来自渠道)。
    - heroId 的意义就是让村长能叫出名字、引述原话(具体 > "有人看见你")。
    - 条件化：证人被你收买/吓唬封口后，村长无人可引述 → 戳不穿(反馈明确)。
    - 目击者当面对质(AddDialogLineMultiAgent 切 speaker) = Step4+ 可选演出，
      仅当他本就是在场 Agent 时触发，失败回落转述。
```

### 路径 B：被查出来了，想办法摆平（Charm 流）

```
【阶段1 Discovery】偷羊 → 没管 → AI 自动调查（每日掷骰推进）
   → 有人目击了你 + Roguery 不足 → 调查锁定 嫌犯 = 玩家
        ↓ 嫌犯确定，迁移
【阶段2 SuspectIdentified】
   → NinjaNotification: "急报——{village}村民认定是你偷的……"
     （通知只是情报，不强制回村。无视 = 合法的被动选择：
       案子在悬赏期内继续，期满未了结 → 自动迁移【阶段3】，见路径 C）
   → 玩家回村找村长 → 村长冷脸："你还敢来？"（IsSuspectPlayer 分支）
   → 对话选项（嫌犯=玩家专属，接不了追捕 Quest）:
       [Charm 辩护]（检定: Charm，每案仅一次，防无限循环）"你们搞错了"
           → 成功率随 Charm 升（玩家没做过 = 给隐藏加成？必过？待定）
           → 成功 = 嫌犯【降级】"待定"(进度→~0.5)回【阶段1】，CharmReprieveUsed=true
                    AI 很快从 0.5 重新确认 → 只买短暂喘息；二次被锁则无此选项
           → 失败 = Trust-10，进【阶段3】
       [付钱赔偿]（无技能检定，但查钱是否够）"这是赔偿够不够？"
           → 钱不足 → 选项灰掉/不可选（提示"你凑不出这笔钱"）
           → 钱够 → 走 AgentControlHelper 转移（玩家→村长，守恒），×3 动物价值
                    （可选: Trade 技能影响赔偿额/讨价还价）
       [威胁]（检定: Roguery，黑道威慑；可加成于队伍规模/恶名）"你再说一遍？"
           → 成功 = 村长忍气吞声压下案子（恶名+1，Trust 暴跌，Resolved）
           → 失败 = 激怒村民，跳过阶段2 → 直接进【阶段3】
   ├ Charm/付钱 摆平 → Stage = Resolved，Issue 关闭，Trust -5（心里还有疙瘩）
   └ 威胁/不理会 → 进【阶段3 Retaliation】（报复部队，见路径 C）

（跨案件）下次再偷此村 → 村庄处于警觉 → 直接锁定玩家（跳过阶段1调查）
```

### 路径 C：放任不报 → 被报复部队找上门（被迫摊牌）

```
前提：阶段1-2 全程不报案 / 不赔 / 不栽赃 → 案子升级到阶段3
      → 村庄出钱委托报复部队，在大地图追猎玩家

报复部队追上你 → 遭遇，必须选（人都到门口了，"站着不管"不成立）：

① 打
   赢 → 击溃这一波，但【不结案】：村庄继续掏自己的钱委托下一波
        （更多更强、花费更高），直到村庄金库山穷水尽才停止派人。
        即便没钱了 → 仍永久视玩家为仇敌（关系敌对、拒绝交易/委托）。
        每赢一波：恶名+2，HeroNemesisTracker 持续记仇。
   输 → 见 ② 投降的同一结局（被俘带回村庄）

② 投降 / 战败 → 被俘，部队把你带回村庄
   → 播放【惩罚 cutscene】（示众/鞭笞/罚没，非处决——骑砍2 无"坐牢"）
   → 归还动物 + 罚金 + 关系暴跌 + 恶名 → 放人，案子 Resolved

③ 不打·和解（让部队解散，案子 Resolved）
   劝说检定（Charm/Roguery，愤怒中成功率低）→ Trust-15
   赔钱赔赃（×5 + 罚金 + 安抚费，钱够才可选；主动归还动物可降价码）→ Trust 归零

④ 不打·逃避（"不管"的正确形态 = 跑赢倒计时）
   靠队伍速度甩开 / 进城躲 → 拖到报复部队 15 天超时自散
   代价：期间持续被追、Trust 持续流失、"该文化圈传开"
```

> **报复经费是有限的**：村庄的委托资金来自 Headman notable 金库（+ 可选村庄繁荣度折算），
> 每派一波走 `AgentControlHelper.TransferGold(Headman → 打手/null)` 扣减（铁律4）。
> 金库 < 下一波花费 → 停止 spawn，但**关系永久敌对不归零**。这把"打赢"从
> "一劳永逸"变成"经济消耗战"——你能赢，但赢不光，且永远多了个仇村。

> **被俘惩罚走 cutscene**：骑砍2 无监狱坐牢概念，用过场表现惩罚。
> 参考 [Knowledge/vanilla_cutscenes/README.md](../Knowledge/vanilla_cutscenes/README.md)
> 复用/改造 SceneNotification（处决场景是模板，但此处是**非致死的示众/羞辱**，
> 可能需自定义场景或替换角色槽位）。结束后释放玩家，不是永久关押。

---

## 二、三阶段 Issue-Quest 链

```
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 1: 发现 — "村里的牲口被偷了！"                                    │
│                                                                       │
│ Issue_1: VillageTheftDiscovery                                        │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 蓝色（普通）                                                │
│  Effect: Security -1, 牲畜产量 -10%                                    │
│  持续时间: 1~7 天（调查期间）                                          │
│                                                                       │
│ Quest_1: InvestigateVillageTheft  ← 玩家可接                          │
│  发布者: Headman                                                      │
│  目标: 查清楚是谁偷了牲口                                              │
│  任务链（MVP，纯对话）:                                                 │
│    ① [若有 notable 证人] 先处理证人（Roguery 收买/吓唬）               │
│    ② 向村长汇报 → 选嫌犯（账本名单 + "强盗"）                          │
│    ③ 村长不信 → [出示证物]（出示赃物，不消耗）→ 检定 + 固定加成        │
│    ※ 物理"搜查现场/发现赃物 GameEntity" = Step3+ 增强，MVP 不做        │
│                                                                       │
│  玩家介入点（按顶层选择分 3 类）:                                       │
│    ▸ 接 Quest 调查:                                                    │
│        · 认真查 → 发现真凶                                             │
│        · 故意误导 → 栽赃强盗 / 嫁祸具体人（见路径 A）                   │
│        · 没头绪 → 主动认栽(Trust 小降) 或 静默超时(Trust 略大降+不靠谱) │
│             两者都【转 AI 自动继续查】，不结案、有后续(AI 仍可能锁你)   │
│    ▸ 不接 → AI 自动调查（每日掷骰）→ 锁定你 / 他人 / 自身窗口超时→冷案   │
│        （被动，无主动代价，但 AI 可能反过来锁定你）                     │
│    ▸ 直接找村长 → 私下赔钱封口（不进 Quest，跳过调查）                  │
│    ※ 真正"冷案(Resolved 终态)"只发生在 AI 自己也查不出时；玩家放弃/超时 │
│       只是把调查交还 AI，≠ 洗白                                        │
│                                                                       │
│  自动调查公式（不接 Quest 且玩家就是真凶时）:                            │
│    目击修正: witnessBonus = witnessCount × 0.15                        │
│    （witnessCount 见 五.1：notable + 未封口的没脸村民）                  │
│    反侦察: rogueDefense = min(0.5, playerRoguery / 300 × 0.5)         │
│    每日推进: 0.25 + witnessBonus - rogueDefense                        │
│    7日内，进度满 1.0 → 确定是玩家干的，转下一阶段。                      │
│    否则调查不出结果，草草结案。                                          │
└──────────────────────────────────────────────────────────────────────┘
    ↓ 调查结束，嫌疑人确定
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 2: 锁定 — "是 {嫌犯} 干的！"                                      │
│                                                                       │
│ Issue_2: VillageTheftSuspectIdentified                                │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 黄色/橙色（更急迫）                                         │
│  Effect: Security -2, Prosperity -1                                    │
│  持续时间: 5~15 天（悬赏有效期间）                                      │
│  特殊: 如果嫌犯=玩家 → 村长对玩家对话 cold                              │
│                                                                       │
│ Quest_2: ApprehendVillageThief  ← 玩家可接（如果嫌犯≠自己）            │
│  发布者: Headman                                                      │
│  目标: 把 {嫌犯} 抓回来（对标 BountyHunt CommissionQuest）             │
│  任务链（嫌犯不一定在大地图！按其位置分流）:                          │
│    ① 定位嫌犯（查 hero.CurrentSettlement / PartyBelongedTo）:          │
│       · 在定居点内(wanderer/notable，常蹲城镇/酒馆) → 进点找人         │
│       · 是大地图部队(强盗头子/带队 hero) → 大地图拦截                  │
│       ※ 目标类型≈位置：框强盗→藏身处(hideout)；框具体人→城镇/酒馆      │
│    ② 制服 → 必须【击晕】(Unconscious)，打死只能搜刮、交不了差:          │
│       · 定居点内：背后击晕(隐蔽)；公开动手=犯罪，卫兵/镇民反应          │
│       · 大地图：战斗打到对方昏迷                                       │
│    ③ 俘虏键带走(见 三.3 活捉机制) → 嫌犯入你俘虏栏 → 回村交付 → 领报酬 │
│                                                                       │
│  ── 活捉机制（普通 mission 内俘虏，新轮子）──                          │
│    问题: 骑砍2 只有【大地图战斗结算】才自动转俘虏；mission 内打晕不会。 │
│    方案: 在倒地交互的 else 分支(现仅"搜刮")加一个【俘虏】键(和搜刮平级) │
│      显示条件: AgentState.Unconscious(非Killed) + 是Hero + 俘虏栏没满  │
│      执行 TryCaptureAgent: TakePrisonerAction.Apply(MainParty, hero)   │
│      → 移除场景 Agent(表现为绑走) → mission 结束后人在你俘虏栏         │
│    要点: 【击晕=活捉正路】(TryKnockoutAgent 保证 Unconscious 不致死)；  │
│          死人只显示"搜刮"、昏迷Hero显示"搜刮+俘虏"，按 AgentState 区分  │
│                                                                       │
│  ── 核心分支体验 ──                                                    │
│                                                                       │
│  ★ 嫌犯 = 玩家:                                                       │
│    玩家接不了 Quest（不能抓自己）                                       │
│    替代选项（对话中）:                                                  │
│      💰 赔钱消灾: 钱够才可选 → TransferGold(玩家→村长, ×3)            │
│         → Issue 关闭, Trust 归零（钱不足则选项灰掉）                    │
│      🗣 Charm 辩护（每案仅一次！防无限循环）:                         │
│         成功 → 嫌犯【降级】为"待定"(进度 1.0→~0.5) → 回阶段1，         │
│              但 CharmReprieveUsed=true；AI 很快从 0.5 重新确认         │
│              → 只买到短暂喘息，不是免罪                                 │
│         二次被锁 → Charm 选项消失/必败（村长记仇:"上次你就这么说"）     │
│         失败 → Trust -10, 直接进阶段3                                  │
│      🤐 威胁（检定: Roguery，黑道威慑）:                              │
│         成功 → 村长忍气吞声压下案子（恶名+1, Trust 暴跌, Resolved）    │
│         失败 → 激怒村民, 直接进阶段3                                   │
│      🏃 直接走人（拒绝交涉，无检定）: 不回应 → 进阶段3                  │
│         （这是"我不伺候了"的自愿放弃，升级是它的自然代价，非陷阱）      │
│                                                                       │
│  ★ 嫌犯 = 嫁祸的 NPC（玩家故意指错的）:                                 │
│    玩家可以接 Quest → 抓那个无辜 NPC                                   │
│    抓到后: NPC 入狱, 玩家拿报酬, Trust +5~10                           │
│    深层后果: NPC 出狱 → HeroNemesisTracker 记录冤情                    │
│    "他没有偷！但他知道你陷害了他。"                                      │
│                                                                       │
│  ★ 嫌犯 = 真实强盗/NPC（自然调查结果或玩家指认正确）:                    │
│    玩家可以接 Quest → 正常 BountyHunt                                  │
│    嫌犯 = 强盗头子 → 清了藏身处还能顺带完成                              │
│    Trust +10~15（抓到了真凶！）                                         │
│                                                                       │
│  （注：冷案=没锁定任何人，【不进本阶段】。阶段1 AI 窗口耗尽无嫌犯 →     │
│    Stage=Resolved，Issue_1 直接关闭(`!`消失)，Trust 小降；             │
│    "村庄警觉(再偷×3)"是跨案件的村庄 flag，非 Issue 效果，案子关了仍存） │
└──────────────────────────────────────────────────────────────────────┘
    ↓ 嫌犯逍遥法外 —— 进入报复阶段
┌──────────────────────────────────────────────────────────────────────┐
│ 阶段 3: 报复 — "客气说话不管用，只能动手了！"                │
│                                                                       │
                                         │
│ Issue_3: VillageRetaliation                                           │
│  Owner: 村庄 Headman                                                  │
│  `!` 标记: 红色（危机）                                                │
│  Effect: Security -3, Prosperity -2                                    │
│  持续时间: 15~20 天（报复部队活跃期间）                                 │
│  同时: WorldEvent 自动 spawn 报复部队                                  │
│                                                                       │
│ Quest_3: LeadRetaliationParty  ← 玩家可接（如果嫌犯≠自己）             │
│  发布者: Headman                                                      │
│  目标: 带领村民报复队找到 {嫌犯} → 教训/活捉                           │
│  任务: 玩家指挥报复部队 → 寻找嫌犯 → 战斗                              │
│  其实对标 CommissionQuest 而非新类型                                    │
│                                                                       │
│  同时: WorldEvent 自动 spawn 报复部队（对标 NemesisRevenge 模式）       │
│    部队命名: "{village}的复仇队" / "{嫌犯名}讨伐队"                     │
│    部队规模: 村庄民兵 5~8 人 + 雇佣打手 3~5 人                         │
│    部队 AI: SetPartyAiAction → EngageParty(嫌犯 party)                 │
│    部队持续: 15 天 → 如果没找到目标自动解散                             │
│                                                                       │
│  ── 分支体验 ──                                                        │
│                                                                       │
│  ★ 嫌犯 = 玩家（详见路径 C 摊牌四选项）:                              │
│    报复部队在大地图追猎玩家。村庄经费有限——掏 Headman 金库委托，       │
│    每波走 TransferGold 扣减，金库见底则停派，但关系永久敌对不归零。     │
│    玩家选项:                                                          │
│      💰 回村找村长赔钱: 钱够才可选, ×5 + 罚金 + 安抚费                 │
│         → 报复部队解散, Trust 归零（钱不足则选项灰掉/只能打或逃）       │
│      ⚔ 击败报复部队: 打赢【不结案】→ 恶名+2, 宿敌追踪                  │
│        村庄继续掏钱派更强的下一波，直到山穷水尽才停（仍视你为仇敌）     │
│      🗣 Charm/Roguery 说服: 成功率更低（愤怒中）→ Trust -15            │
│      🏃 逃避: 跑赢/进城躲 → 部队 15 天超时自散 → Trust-30, 恶名+3       │
│      🏳 投降/战败: 被俘带回村庄 → 惩罚 cutscene（非坐牢）→ 归还+罚金   │
│                                                                       │
│  ★ 嫌犯 = NPC（嫁祸或真实）:                                           │
│    玩家可以接 Quest → 带报复部队去打那个 NPC                            │
│    打完 → 村民感激涕零, Trust +10~20                                    │
│    那个 NPC → NemesisRecord（恨死玩家）                                 │
│                                                                       │
│  （注：冷案=无嫌犯，【不进本报复阶段】——没对象可报复。                 │
│    "村民找不到贼→迁怒打错人"改作【冷案尾巴 mini-event】，见 三.4）      │
└──────────────────────────────────────────────────────────────────────┘
```

---

## 三、对话与交互设计

### 三.1 对话集成

**村长对话选项**（结构示意）：

```
阶段1: "听说村里丢了牲口？"  →  介绍案情 → "我可以帮忙查"
        [接 Quest_1]
        ---------------------------
        "我听说你们在找偷牲口的……"（玩家是贼，主动来误导）
        → [接 Quest_1] → 故意指错人

阶段2: "关于那个偷牲口的贼……"
        → (嫌犯≠玩家) "我可以去抓他" → [接 Quest_2]
        → (嫌犯=玩家) "你们搞错了——"
            → [Charm 辩护]
            → [赔钱]
        → (嫌犯=NPC，玩家是贼) "我会把他带回来" → [接 Quest_2]

阶段3: "牲口的事还没解决？"
        → (嫌犯≠玩家) "我带你们的人去" → [接 Quest_3]
        → (嫌犯=玩家) "我们可以商量……" → [赔钱/Charm]
```

**对话注册方式：走 `DialogueInjector.cs` 的原始注入模式，不走 IntentRegistry。**
参看 [DialogueInjector.cs](../ExampleModVS/ExampleMod/ExampleMod/Interaction/DialogueInjector.cs)
+ [原版对话流引擎逆向分析](../Knowledge/原版对话流引擎逆向分析.md)：

- 直接用 `ConversationManager.AddPlayerLine` / `AddDialogLineMultiAgent` 把对话节点注册到引擎，
  turn→token 串联（NPC 一句 → 玩家多选项 → 各自 NPC 回应）。
- 用 **owner 哨兵** 标记本案注入的所有行，案件关闭/读档时 `RemoveRelatedLines(owner)` 一次清干净。
- ⚠ 与 DialogueInjector 现状的差距（需扩展，不是照搬）：
  - 它的条件目前硬编码 `() => true`——本系统的选项要**真实条件委托**：
    阶段判定（`case.Stage`）、嫌犯是否=玩家、账本是否有可栽赃目标、Gold 是否够、技能检定。
  - 它的 `ExecuteAction` 只有 RELATION/GOLD 几种——要扩出本系统的动作：
    设 `Evidence.TargetId`、`TakePrisonerAction`、阶段迁移、`CharmReprieveUsed` 等。
  - 金钱动作必须改走 `AgentControlHelper.TransferGold`（铁律4），不用裸 `GiveGoldAction`。

### 三.2 汇报 = 栽赃落地（MVP 核心交互）

栽赃**不是**"去现场丢东西再发现"——玩家自己就是调查者，汇报对话里的 [出示证物]
就是栽赃落地的那一下，一个动作同时完成"声明嫌犯 + 提交证据"。

**检定模型：确定性，无骰子。**

```
belief 门槛 = 目标基础 DC
           （村长的先验可信度——"说这个人偷牲口"有多难信，不受玩家影响）

playerPower = Roguery 折算值 + 道具加成(若有) + 封口加成(若有)
            （玩家的说服力——你有多能让人信你指的那个人是贼）

playerPower ≥ 门槛 → 村长信了 → 嫌犯锁定，进阶段 2
playerPower < 门槛 → 村长不信 → 进 fail forward 分支（见下）
```

**目标基础 DC（村长"先验可信度"）**：

| 目标类型 | DC | 村长心理 |
|---------|-----|---------|
| 强盗头子 | 40 | "强盗偷牲口，天经地义" — 不需要说服，门槛最低 |
| 流浪汉 / wanderer | 35 | 没人在乎他，没人替他说话 — 最容易栽赃的具体人 |
| 本地村民 / 猎人 | 55 | 在村里有信誉，村长会犹豫 |
| 外国商人 | 70 | 有钱有势，村长不敢随便指认 |
| 领主 / 贵族 | 85 | "你说领主大人偷羊？你是不是疯了？" |

DC 从目标属性自动计算：名声、荣誉 trait、与村庄关系、是否有犯罪记录等。

**道具加成**：

- **道具是什么**：从栽赃目标身上偷窃（Pickpocket）得来的随身物品——匕首、戒指、护身符、钱袋等。
  玩家向村长出示此物，声称"在牲口棚附近捡到的"或"贼落下的"，作为栽赃的实物证据。
- **来源**：偷窃目标（与偷牲口是两次独立的偷窃行为——先偷牲口、再偷目标的随身物做证据）
- **[出示证物] 效果**：playerPower +20（固定值），代表"有实物比空口无凭强"
- **消耗**：**不消耗**——村长只是看一眼，不会收走实物。同一件赃物可在不同场合反复利用
- 没有道具 = 纯嘴炮，只有 Roguery 折算值，对高 DC 目标基本过不去

**检定失败 ≠ 结束，fail forward**：

```
村长不信 → 每次失败计入本回合失败计数:

  [出示证物]（有道具时）→ 再检定，道具加成生效
      → 成功 → 进阶段2
      → 又失败 → 计入失败次数

  [换个人指] → 换目标重来（调查进度 +0.05，时间成本）

  [嘴硬坚持] → 二次裸检定（无额外加成，DC 不变）
      → 成功 → 进阶段2
      → 失败 → 计入失败次数

  [算了] → 退回，不结案，可下次回村再来
            （但 AI 调查继续推进，下次来可能已经锁定你了）

本回合累计 2 次失败 → 村长耐心耗尽。后果按玩家身份分叉：

    ┌─ 玩家是贼 ──────────────────────────────────────────
    │ "你越说越不对劲。一会指这个一会指那个……
    │  该不会就是你干的？"
    │ → 嫌疑转回玩家 → 进阶段2(嫌犯=玩家)
    │
    │ 叙事逻辑：贼心虚，越描越黑。村长不是靠检定没过推
    │ 理出来的——是你自己语无伦次暴露了。
    │
    ├─ 玩家无辜（没偷过这个村的牲口）───────────────────
    │ "你指了两个人都说不通。算了，这事我们自己查吧。"
    │ → 调查 Quest 失败/解除，村长不再信任你的判断
    │ → 降关系（Honor 扣分——你差点冤枉好人）
    │ → AI 调查继续推进，但村长不会再找你汇报
    │ → 玩家不会被当成贼——没动机、没证据，村长的逻辑是
    │   "这人侦探水平不行"，不是"这人就是贼"
    │
    └─ 玩家没偷牲口、但你指的那个人是你偷过随身物的对象 ──
        → 同上（Quest 失败 + 降关系）
        → 额外风险：被指者若在村中有声望，可能反向指控
          你偷窃（→ 阶段2 嫌犯=玩家的子分支）
```
```

**设计要点**：
- 失败不结束对话，但有时机成本和信任成本——每次失败村长对你的**判断力**信任下降，而非直接怀疑你偷了东西
- 两次失败 = 封顶，防止无限重试
- 村长最终的结论取决于**你实际做了什么**（是否偷过牲口），不靠检定没过就魔幻地"推理"你是贼
- [出示证物] 的固定加成让道具路线明显更稳——但不是必选，低 DC 目标不需要也能过

**策略深度在【选谁】，不在检定本身**：

检定是确定性的，真正的玩家决策是"我选哪个替罪羊"：

| 目标 | DC | 需要道具？ | 后续形态 | 出狱后果 |
|------|-----|----------|---------|---------|
| 强盗 | 40 | 否 | 阶段2 单人追捕 → 清藏身处 | 零——强盗没有出狱复仇 |
| 流浪汉 | 35 | 基本不需要 | 阶段2 单人追捕 | 轻——他没资源报复 |
| 本地猎人 | 55 | 建议有 | 阶段2 单人追捕 | 中——出狱后村里信任崩塌，以后你来这村有人翻旧账 |
| 商人 | 70 | 必须有 | **跳过阶段2 → 进阶段3**（村民组队） | 重——出狱 = NemesisRecord，有钱雇人找你麻烦 |
| 领主 | 85 | 必须有 | **跳过阶段2 → 进阶段3**（村民组队） | 极重——整个家族跟你敌对，出狱那天就是你的死期 |

**为什么商人/领主跳过阶段 2**：村长不傻。给流浪汉悬赏 500 第纳尔指望你自己搞定，给领主悬赏 5000 第纳尔没人敢接——他自然知道这种量级只有全村一起上才有机会。玩家栽赃大人物，就是绑全村上战车。

**大人物第二道坎：村长想缩**

第一道坎（belief 检定）是"让村长相信是他干的"。对商人/领主，过了第一道还有第二道——**村长认出是大佬，本能想缩**。你得再推他一把。

```
belief 检定过 → 村长:"领主大人？！你确定？！"
    → 村长本能退缩:
        "这事儿……要不咱还是算了吧。丢几只羊，犯不着得罪他。"

    玩家必须推他（三选一，每个都要过二次检定）：

    [Charm 激将] "你是村长，村民看着你呢。你不替他们出头，谁出？"
        → 成功 → 村长咬牙:"好……干了。" → 进阶段3(村民组队报复)
        → 失败 → 村长:"你站着说话不腰疼……"
            → 村长压下案子，Trust -10，从此对你冷眼

    [Roguery 恐吓] "你不抓他，回头他知道了你在查他——你猜他先找谁？"
        → 成功 → 村长:"……你说得对，先下手为强。" → 进阶段3
        → 失败 → 村长识破:"你在拿我当枪使。" → 嫌疑转回玩家

    [算了]  → 村长压下案子，Trust -10，"以后少给我惹事"
                → 案件 Cold（不 Resolved，像个定时炸弹——以后可能被翻出来）
```

**设计要点**：
- 第二道坎是"大人物"专属——强盗/流浪汉/村民不触发
- 两道坎的逻辑不同：DC 是"让他信是你"，二次检定是"让他敢对你动手"
- 栽赃领主的真实难度 = 一个高 DC + 一个中高 DC + 全村陪你赌命——不是难在某个数字，是难在事态不断超出预期
- 二次检定失败也有 fail forward：不是"请重试"，是案子被压下或嫌疑转回你

**候选名单注脚由账本数据驱动**，玩家一眼明白选项从哪来、为什么只有这几个，不出戏。
候选 ≤3 用对话行；更多用 `MultiSelectionInquiryData`（需确认当前版本 API 可用）。
建议在确认嫁祸具体人时给一句让玩家犹豫的旁白（对标铁律6 的 KCD2 水准）。

**不确定性不在骰子，在信息不对称**：AI 调查进度到哪了、有没有 notable 证人见过你、证人被你封口了没——这些玩家不查不知道。玩家可以做更多侦察/收买来消除不对称，而不是靠运气。

### 三.2 附：追捕 Quest 分支结果

栽赃过了、Quest 接了——但抓人不一定顺利。以下六个分支覆盖 `ApprehendVillageThiefQuest` 的全部结局：

```
接追捕 Quest → 定位嫌犯 → 动手
    │
    ├─ ★ 成功活捉 — 击晕 → 俘虏 → 回村交付
    │     → 报酬全款 + Trust +10~15 → Resolved ✓
    │     → 嫌犯入狱 → 出狱后按身份触发 NemesisRecord
    │
    ├─ ★ 嫌犯被杀（战斗中失手打死）
    │     → 无法交付活人，Quest 失败
    │     → 回村汇报:
    │         [出示尸体信物] "他拒捕，我只能杀了。"
    │           村长勉强接受（死人也算交代）→ 半额报酬，Trust 不变
    │         [老实说] "下手重了……"
    │           村长不满:"我要活的！"→ Trust -5
    │     → 嫌犯死亡 = 不出狱、不复仇（NemesisRecord 不触发）
    │        策略性后果：杀掉 = 死无对证，没人知道被陷害
    │        —但这是谋杀，比嫁祸重得多。旁边若有人看见……(目击系统复用)
    │
    ├─ ★ 嫌犯逃脱（追到但跑了 / 追丢了）
    │     → Quest 失败
    │     → 嫌犯知道有人在抓他 → 隐藏/出逃（短期内不可再定位）
    │     → 村长失望，Trust -5
    │     → 阶段2 Issue 关闭，AI 可能重新评估案情或进阶段3
    │     → 如果玩家是栽赃者 + 嫌犯是无辜 NPC:
    │        他逃命之余可能自己调查"谁在害我"→ 未来反噬
    │
    ├─ ★ 超时（期限内未抓到）
    │     → Quest 自动失败
    │     → 嫌犯仍自由 → 阶段2 Issue 关闭
    │     → 村长:"看来是指望不上你了。"→ Trust -5
    │     → AI 可能另找人抓、进阶段3、或冷案
    │
    ├─ ★ 玩家背叛（找到嫌犯后主动放走）
    │     对话选项: "快跑。村里人在抓你。"
    │     → 嫌犯反应因身份而异:
    │        · 真贼 → 感激 / 逃走，后续可能报恩
    │        · 无辜 NPC（被玩家栽赃的）→ 困惑 / "你为什么帮我？"
    │          → 他不一定立刻识破，但日后可能拼出真相
    │        · 无辜 NPC + 玩家透露"是我陷害的你"→ NemesisRecord 当场生成
    │     → Quest 失败，村长雷霆大怒: Trust -15
    │     → 若村长怀疑是故意放人 → 嫌疑转回玩家 → 进阶段2(嫌犯=玩家)
    │
    └─ ★ 玩家取消（回村找村长说"我不干了"）
          → Quest 取消，Trust -5（"你答应了又不做"）
          → AI 接管: 可能另找人抓，或直接进阶段3
          → 不触发嫌疑转回（正常取消，不像背叛）
```

**设计要点**：
- 六种结局梯度分明：从最好（活捉）到最糟（背叛被识破），每档后果不同
- 杀掉看似"干净"（死人不会复仇），但谋杀罪的目击风险是另一层博弈
- 背叛是唯一玩家主动选择"不做坏人"的出口——但代价最大
- 所有失败分支都不让案件"消失"——Quest 失败了，案子还在，AI 继续推进

### 三.3 活捉机制：mission 内俘虏（新轮子）

**问题**：骑砍2 的俘虏只在【大地图战斗结算】时自动产生；普通 mission（村庄/城镇/酒馆等 location 场景）里把人打晕，引擎**不会**转俘虏。而嫌犯（尤其框的 wanderer/notable）**多半就蹲在城镇/酒馆里**——这正是 location 场景，是活捉的主战场，大地图战斗反而只是次要情况。所以 `ApprehendVillageThiefQuest` 把嫌犯"带回村庄"这一步缺一块底层能力。

**现有拼图**（[InteractionMissionView.cs](../ExampleModVS/ExampleMod/ExampleMod/Interaction/InteractionMissionView.cs)）：
- 击晕已有：`TryKnockoutAgent`（`Immortal + Health=0 → AgentState.Unconscious`，倒地不死）
- 倒地交互的 `else` 分支已有（line ~487），目前只挂一个 `("搜刮","F")`
- 加俘虏的先例：[GeneralIntents.cs:173](../ExampleModVS/ExampleMod/ExampleMod/Interaction/Intents/GeneralIntents.cs#L173) 用 `PrisonRoster.AddToCounts` 收俘虏

**方案**：倒地 `else` 分支再加一个【俘虏】键，与搜刮平级。

```
显示条件（同一具倒地的身体）:
  · 已击杀(AgentState.Killed)        → 只显示 ("搜刮","F")
  · 昏迷(Unconscious) 且 是 Hero     → 显示 ("搜刮","F") + ("俘虏","R")
  · 俘虏栏已满                        → "俘虏"灰掉并提示

TryCaptureAgent(agent):
  ① agent.Character → HeroObject（守卫：非 Hero 用 PrisonRoster.AddToCounts 兜底）
  ② 守卫：Unconscious + 容量够，否则 return
  ③ TakePrisonerAction.Apply(MobileParty.MainParty.Party, hero)  // 确认签名
  ④ 移除场景 Agent（表现为"绑走扔上马背"）
  ⑤ 反馈 "你绑住了 {name}。"
  → mission 结束 → 嫌犯已在你队伍俘虏栏 → 回村交付
```

**两种抓法（互斥，殊途同归到 `TakePrisonerAction` → PrisonRoster）**：

> ⚠ 两条路线终点【必须相同】：都把人塞进 **PrisonRoster（俘虏队列）**，
> **绝不**经过 MemberRoster（部队成员）。成员能脱队、还没法当贼交付，是错误状态。
> 也别图省事"先加进队伍再转俘虏"——会有一帧他是你队友触发别的逻辑。Agent → 俘虏一步到位。

```
方法A 背后击晕（无对话）:
   潜行(蹲伏+背后判定) → 一闷棍(Unconscious) → 俘虏键 → TryCaptureAgent
   · 一开口搭话就破功(他转身面对你)，所以这条路【不对话】
   · 道德重量靠聚焦时的气泡/旁白("就是他了……他还什么都不知道")

方法B 诱捕（对话，且对话本身=抓捕手段）:
   [跟我走一趟，村长找你有事] —— 选项文案要点明【这是把他骗去关起来】
   → 需骗过他: Charm/Roguery 检定(让他相信这套说辞)
       成功 → 他信了，"行，我跟你去" → 直接结算：NPC 进玩家队伍俘虏栏
              （无物理出手——对话就是抓捕，他心甘情愿走进囚笼）
       失败 → 他起疑/翻脸 → 惊动(犯罪)或逃 → 回落方法A 或开打
   · 在场景内就地结算，不让他真的在大地图跟随(避免 escort 复杂度)
   · 对真贼可改为"劝降/亮通缉": 检定成功他束手就擒进俘虏栏，失败翻脸

风格差异：背刺=冷酷利落，物理压制；诱捕=社交残忍（他信了你，自己走进囚笼，全程没有反抗——直到被关进俘虏栏才明白发生了什么）。
```

**背叛对话落在"事后"，不在抓前**：
- 抓前他不知情（背刺路线根本没对话；诱捕路线他以为你在帮忙）。
- 抓后他醒来当俘虏 / 交付时才懂："你明明知道不是我……" → 写入 `HeroNemesisTracker`。
- 交付村长时他会喊冤 → 若当初栽赃证据弱 → **反噬窗口**：嫌疑可能转回你。


**要点**：
- **击晕 = 活捉正路**：普通战斗易把人打死（`Killed`）→ 交不了差。`TryKnockoutAgent` 保证 `Unconscious`，所以"活捉无辜 NPC"要走背后击晕 / 战斗点到为止——也契合"抓无辜你下不去死手"的叙事。
- **死人 vs 昏迷必须按 `AgentState` 区分**，不能把昏迷者一律当尸体只给搜刮。
- **定居点内动手 = 犯罪**：在城镇/酒馆当众击晕一个 hero 会惊动卫兵/镇民（crime rating、敌对）。
  正路是**背后隐蔽击晕**（复用偷窃的蹲伏+背后判定 + 目击两档系统）；公开硬抓代价大。
  → 把"抓人"也接进偷窃的目击/被发现逻辑，而非另起一套。
- **副作用守卫**：若嫌犯是绑定村庄的 notable，抓走可能影响该 settlement → 嫁祸对象最好是流动 hero/wanderer（见 十（开放问题）#8）。
- **俘虏键位**：F 被搜刮占用，暂定 **R**（待定，可改）。
- 这是通用能力（绑架/抓逃犯/私设公堂都用得上），实现后登记 [wheels.md](rules/wheels.md)。


## 四、通知与叙事（遵守叙事铁律）

### 四.1 暗探情报（阶段变化时推送）

**阶段1 — 村民发现:**
> "暗探来报——{village}近日有村民私下议论，说圈里的牲口不知怎的少了好几只。村长正在挨家挨户问话……看样子是要查个水落石出。"

**阶段2 — 嫌犯锁定（玩家）:**
> "急报——{village}传来消息：村民认定是你偷了他们的牲口。村长已经向附近放话，要找人来'讨个公道'。"

**阶段2 — 嫌犯锁定（NPC）:**
> "暗探来报——{village}村民认定是 {suspect} 偷了他们的牲口，正在悬赏捉拿此人。据说赏金已有 {reward} 第纳尔。"

**阶段3 — 报复部队出发:**
> "前线急报——{village}村民自己凑了钱，雇了几个打手，正满世界找 {suspect}。这事儿怕是不能善了了。"


### 四.4 需要新增的 Narrative.csv 条目

| ID | 用途 |
|----|------|
| `VillageTheft_Discovery_Headman` | 村长介绍案情 |
| `VillageTheft_Suspect_Headman_Player` | 村长对玩家冷脸 |
| `VillageTheft_Suspect_Headman_NPC` | 村长悬赏 NPC |
| `VillageTheft_Retaliation_Headman` | 村长动员报复 |
| `VillageTheft_Resolved_Restitution` | 玩家赔钱后村长回应 |
| `VillageTheft_Resolved_Caught` | 抓到真贼后村长感谢 |

---

# 实现篇

---

## 五、数据结构设计

### 五.1 VillageTheftCase（单一案件状态机）

```csharp
[Serializable]
public class VillageTheftCase
{
    // 身份
    public string CaseId;              // $"theft_{settlementId}_{timestamp}"
    public string SettlementId;
    public string HeadmanHeroId;       // 村长（Issue Owner）

    // 偷窃事实（偷窃时写入，不可变）
    public int TotalAnimalsStolen;     // 共偷了几只
    public Dictionary<string, int> StolenByMonster; // "sheep"→2, "cow"→1
    public float TheftDay;             // 偷窃发生的游戏日
    public string ThiefHeroId;         // 实际贼（可 null！系统知道但村民不一定知道）

    // 目击者（偷窃当下从 StealManager.GetWitnesses 分两档记录）
    //  ── 为什么按"模板计数"而非"逐人"？普通村民的 CharacterObject 是【类型】不是【个人】，
    //     同场景常有多个 Agent 共享同一 StringId，根本分不出个体。第二天回村时这些 Agent
    //     已重新生成，只能"找个长得对得上的村民"代言。所以没脸村民只存 kvpair: 模板 → 数量。
    public List<string> WitnessHeroIds;                       // 有名有姓的目击者（HeroObject != null）→ 存 heroId，可点名收买/对质，关系永久后果
    public Dictionary<string, int> TemplateWitness; // 模版生成的村民：CharacterObject.StringId → 该模板目击了几人
    public bool WitnessesSilenced;                            // 没脸村民已被聚合收买/吓唬封口（一次 Roguery 检定，清空有效流言）
    // 有效目击数（喂调查公式 witnessBonus）= notable 数 + (未封口时) 没脸村民总数
    public int WitnessCount => (WitnessHeroIds?.Count ?? 0)
                             + (WitnessesSilenced ? 0 : (TemplateWitness?.Values.Sum() ?? 0));

    // 调查阶段 (阶段1)
    public float InvestigationProgress; // 0.0 → 1.0
    public string SuspectHeroId;       // 当前嫌犯调查进度 0.5 为"嫌疑人"1.0 为"确认"
    public List<string> ClueList;      // 玩家收集的线索（"footprint", "witness_saw_stranger"...）
    public bool IsColdCase;            // 冷案（7天未破）

    // 证据指针（决定调查"指向谁"。真凶=NPC 时系统按真相设置；玩家栽赃时玩家设置）
    //  MVP：只用 TargetId（汇报时出示证物即设置）。AtScene/GameEntity 物理表现 = Step3+
    public EvidencePointer Evidence;   // null = 暂无指向性证据

    // 锁定阶段 (阶段2)
    public string IdentifiedSuspectId; // 锁定嫌犯的 HeroId（null = 未锁定）
    public bool IsSuspectPlayer;       // 嫌犯是否 = 玩家
    public float SuspectIdentifiedDay; // 锁定日期
    public bool CharmReprieveUsed;     // Charm 辩护已用过（每案一次，防"辩护→回阶段1→又锁→再辩护"无限循环）

    // 报复阶段 (阶段3)
    public bool RetaliationSpawned;    // 当前是否有活跃报复部队
    public string RetaliationPartyId;  // 当前这一波报复部队的 MobileParty.StringId
    public float RetaliationSpawnDay;
    public bool RetaliationResolved;    // 报复已彻底了结（赔钱/说服/超时/经费耗尽且和解）
    // 经济消耗战：打赢一波不结案，村庄继续掏钱派下一波，直到金库见底
    public int RetaliationBudget;       // 剩余委托经费（开案时从 Headman 金库 + 村庄繁荣折算播种）
    public int RetaliationWaveCount;    // 已派出波数（每波更强更贵）
    public bool PermanentEnemy;         // 经费耗尽/多次冲突后 → 村庄永久视玩家为仇敌（即使不再派人）

    // 状态
    public TheftCaseStage Stage;       // Discovery / SuspectIdentified / Retaliation / Resolved
    public float LastUpdateDay;

    // 玩家介入
    public bool PlayerTookQuest1;      // 玩家接了调查 Quest
    public bool PlayerTookQuest2;      // 玩家接了追捕 Quest
    public bool PlayerTookQuest3;      // 玩家接了报复 Quest
    public bool PlayerPaidRestitution; // 玩家已赔钱
}

public enum TheftCaseStage
{
    Discovery,          // 阶段1: 发现+调查中
    SuspectIdentified,  // 阶段2: 已锁定嫌犯
    Retaliation,        // 阶段3: 报复部队活跃
    Resolved            // 已解决（赔钱/抓到贼/报复完成/冷案超时）
}
```

#### 五.1.1 阶段迁移条件（集中定义）

分散在文档各处的迁移条件汇总于此，实现时以此为唯一真源：

| 迁移 | 触发条件 | 触发位置 |
|------|---------|---------|
| `Discovery → SuspectIdentified` | `InvestigationProgress >= 1.0` 且 `SuspectHeroId != null` | `DailyTick` 推进调查 |
| `Discovery → SuspectIdentified`（目击直达） | 偷窃时 `WasWitnessed = true` 且贼未被当场抓住 → `SuspectHeroId = 玩家`, `InvestigationProgress = 1.0` | `RecordTheft` 内立即迁移 |
| `Discovery → Resolved`（冷案） | `TheftDay + 7 < CampaignTime.Days` 且 `InvestigationProgress < 1.0` | `DailyTick` 超时检查 |
| `SuspectIdentified → Retaliation` | 嫌犯=玩家：期限内未赔钱/未说服成功；嫌犯=NPC：悬赏期满未抓到 | `DailyTick` 期限检查 |
| `SuspectIdentified → Resolved` | `PlayerPaidRestitution == true`；或追捕 Quest 完成（嫌犯被交付）；或村长被威胁成功（恶名路径） | 对话/Quest 完成回调 |
| `SuspectIdentified → Discovery`（回退） | Charm 辩护成功 → `InvestigationProgress` 重置为 ~0.5，`CharmReprieveUsed = true`。仅一次，二次被锁不可回退 | 对话回调 |
| `Retaliation → Resolved` | 赔钱和解（阶段3 ×5+罚金）；被俘惩罚完成；报复部队超时自散（15天）；经费耗尽且 `PermanentEnemy = true`（无后续部队） | `DailyTick` 检查 / 对话回调 |

> ⚠ **"打赢报复部队"≠结案**（`PermanentEnemy` 也不等于 `Resolved`）。打赢一波后 `RetaliationBudget` 扣费 → 若经费仍够 → spawn 下一波（更强更贵），`Stage` 保持 `Retaliation`。只有当经费不足、或部队超时自散、或玩家主动和解时，才迁移到 `Resolved`。但经费耗尽后 `PermanentEnemy = true`，关系永久敌对不归零。

#### 五.1.2 同村后续偷窃规则

同一村庄可能被玩家多次偷窃。规则：

| 场景 | 行为 |
|------|------|
| 该村有**活跃案件**（`Stage != Resolved`） | **合并**：新偷窃叠加进现有案件（`TotalAnimalsStolen += count`，`StolenByMonster` 合并）。调查进度**不重置**——村民已经在查了，又丢牲口只会让他们更确定。如果嫌犯尚未锁定，新目击者合并进 `WitnessHeroIds` / `TemplateWitness` |
| 该村前一案件**已 Resolved** | **开新案**。但如果 `VillageAlertFlag = true`（见下），新案调查起始进度 +0.3、玩家嫌疑权重 +20% |
| 该村从未被偷过 | **开新案**，正常从 0 起步 |

**村庄警觉标记**（跨案件持久化，存村庄级 flag，非案件字段）：
```csharp
// 存于 VillageTheftCase 管理器（或 Settlement 扩展数据），独立于单个案件
Dictionary<string, bool> _villageAlertFlags;  // SettlementId → 是否警觉
```
- 任意案件 Resolved 且嫌犯=玩家（赔钱/威胁成功/被报复）→ `_villageAlertFlags[villageId] = true`
- 冷案 Resolved（没查到是谁）→ 不触发警觉
- 警觉效果：下次该村被偷 → 新案 `InvestigationProgress` 起始 +0.3、玩家嫌疑权重 +20%、村民对玩家初始态度 cold

#### 五.1.3 Issue-Case 关联查找

进入村庄时 `OnSettlementEntered` 需要找到"当前村庄有没有活跃案件"来决定注册哪个 Issue：

```csharp
// 一个村庄同时最多一个活跃案件（后续偷窃合并，见五.1.2）
var activeCase = _allCases.FirstOrDefault(c =>
    c.SettlementId == settlement.StringId &&
    c.Stage != TheftCaseStage.Resolved);

if (activeCase != null)
{
    // 按 activeCase.Stage 映射到对应 Issue 类注册：
    //   Discovery          → VillageTheftDiscoveryIssue
    //   SuspectIdentified  → VillageTheftSuspectIssue
    //   Retaliation        → VillageTheftRetaliationIssue
    RegisterIssueForStage(activeCase);
}
```

- **Issue 不持有案件数据**，只持有 `CaseId` 引用。所有状态读写走 `VillageTheftCase`。
- 案件 Resolved → Issue 关闭（`!` 消失）。同一村庄后续再被偷 → 开新案 → 新 Issue。

### 五.2 持久化

- 多个 `VillageTheftCase` 以 `List<VillageTheftCase>` JSON 序列化
- `PlayerTheftLedger` 以 `List<TheftRecord>` JSON 序列化
- 均存入 `MyBehavior.SyncData`（对标 `HeroNemesisTracker` 的序列化方式）
- `Dictionary<string,int>`（`TemplateWitness` 等）序列化走既有 JSON 方式（对标 `StolenByMonster`），
  **勿**用骑砍原生 `SaveableField` 直接存字典（参看 [存档机制深度解析](../Knowledge/存档机制深度解析.md)）
- 存档字段 ID: 待分配（需要查 `StoryContext.SaveDefiner` 确认下一个可用 ID）

### 五.3 与 VillageAnimalTracker 的关系

`VillageAnimalTracker` 只管 "被偷了多少 → 还剩多少 → 自然恢复" 的**数值层**。

`VillageTheftCase` 负责 "谁偷的 → 村民怎么反应 → 报复链" 的**叙事层**。

`RecordTheft` 触发时，两套数据同时更新：

```csharp
// 现状
VillageAnimalTracker.RecordTheft(settlementId, monsterId, count);

// 新增：同时打开/更新案件
// 偷窃当下把目击 Agent 分两档：notable → heroId 列表；模板村民 → 模板计数 kvpair
var witnesses = StealManager.GetWitnesses(playerAgent, victimAgent);
var witnessHeroIds = witnesses
    .Where(a => (a.Character as CharacterObject)?.HeroObject != null)
    .Select(a => (a.Character as CharacterObject).HeroObject.StringId)
    .ToList();
var templateWitness = witnesses
    .Where(a => (a.Character as CharacterObject)?.HeroObject == null && a.Character != null)
    .GroupBy(a => a.Character.StringId)
    .ToDictionary(g => g.Key, g => g.Count());   // "villager_empire" → 3

VillageTheftCase theftCase = VillageTheftCase.OpenOrUpdate(
    settlementId, headmanHero, monsterId, count,
    thiefHero: Hero.MainHero,
    witnessHeroIds: witnessHeroIds,
    templateWitness: templateWitness
);

// 偷动物：受害方是村庄不是 hero，PlayerTheftLedger 记 VictimSettlementId
PlayerTheftLedger.Record(victimHeroId: null, settlementId, monsterItemId, count);
```

> 第二天回村需要"指认目击村民"时，按 `TemplateWitness` 的模板 id 去
> `Mission.Current.Agents` 里找 `Character.StringId` 相同的活村民代言（**只是替身，非原人**）。
> Fallback：该模板没刷出来 → 随便抓个村民；再不行 → 退化成纯数字检定，不露面。

### 五.4 PlayerTheftLedger（玩家偷窃账本 — MVP）

全局玩家级账本，**独立于按村庄走的 `VillageTheftCase`**。记录"玩家从谁偷了什么"，
两个用途：① 玩家按 H 查自己时展示赃物来源做决策；② 栽赃时它就是**嫌犯候选名单的来源**
（你只偷过谁就只能栽赃谁，名单天然只有 0~3 人 + "强盗"，根治"hero 太多没法进选项"）。

```csharp
[Serializable]
public class TheftRecord
{
    public string VictimHeroId;       // 扒窃来源 hero；偷动物则为 null
    public string VictimSettlementId; // 偷动物时的村庄
    public string ItemId;             // ItemObject.StringId
    public int Count;
    public float StolenDay;
    public string LocationName;       // "在{村庄}" — UI 叙事用
    public bool IsCleared;            // 案件已 Resolved 且玩家已赔钱/被惩罚（true = 赃物仍在你手但道义上已清算）
                                      // 用法：按 H 查自己时，IsCleared=true 的条目标注"已赔偿"而非"⚠ 偷自"
                                      // 待拍板：是否需要此字段？不做 = "历史不可改，赔钱只是摆平村民，偷过就是偷过"
}
// 管理器：List<TheftRecord>，存进 MyBehavior.SyncData
```

- **写入点**：`StealManager.StealSpecificItem`（扒 hero）、`TryStealAnimal`（偷动物）各加一行记账。
- **栽赃判定**：要栽给 X → 账本有 X 的条目 **且** 背包当前仍持有 ≥1 件该 `ItemId`
  （骑砍2 物品实例不带主人，故靠"账本条目 + 仍持有"近似 provenance；卖了/用了就栽不成，反而更真实）。

### 五.5 EvidencePointer（证据指针）

真凶留下的赃物 与 玩家栽赃放下的赃物 **共用一套结构**，区别只是谁来设置 `TargetId`。

```csharp
[Serializable]
public class EvidencePointer
{
    public string TargetId;        // 指向谁："bandit" 或 hero id
    public EvidencePackaging Kind; // Letter(信，自证) / Belonging(随身物，需 NPC 认领)
    public string ItemId;          // Belonging 时是哪件物品
    public bool AtScene;           // 是否还在现场可被发现 —— Step3+ 才用
}
public enum EvidencePackaging { Letter, Belonging }
```

- **MVP**：只用 `TargetId`/`Kind`/`ItemId`。玩家汇报时 [出示证物] → 设置指针 → 锁定嫌犯。无场景实体。
- **Step3+ 增强**：`AtScene=true` 时进村庄场景**懒生成** GameEntity（只持久化指针，每次进场重生成），
  支持"请 NPC 带我到事发地 → 看到赃物 → 信直接读 / 随身物拿去给认识的人认领"的沉浸发现体验。

### 五.6 玩家自查 UI（复用 NpcInfoVM，零新 UI）

- `NpcInfoVM` 已有 **Tab 6 = 背包栏**（`ExecuteSelectInventory`），内容是纯文本 `AgentControlHelper.GetBagInfo(hero)`。
- **按 H 查自己**：`HandleInput` 加分支 → `OpenNPCInfoBoard(Agent.Main)`。需给 board 一条"自己"的轻量路径（玩家可能没有 `SingNpcMemorySystem`，至少背包/个人栏可用）。
- **来源注脚**：改 `GetBagInfo`（或包一层），遍历物品时拿 `ItemId` 查 `PlayerTheftLedger`，命中即标注 `⚠ 偷自 {来源}`。纯文本拼接，零 XML。

---

## 六、Issue-Quest 类设计

### 六.1 三个 Issue 类

| 类 | 阶段 | Owner | `!` 颜色 | IssueEffect | `GenerateIssueQuest` |
|---|------|-------|---------|-------------|---------------------|
| `VillageTheftDiscoveryIssue` | 1 | Headman | 蓝 | Security-1 | → `InvestigateVillageTheftQuest` |
| `VillageTheftSuspectIssue` | 2 | Headman | 黄/橙 | Security-2 | → `ApprehendVillageThiefQuest`（仅嫌犯≠玩家时） |
| `VillageTheftRetaliationIssue` | 3 | Headman | 红 | Security-3 | → `LeadRetaliationQuest`（仅嫌犯≠玩家时） |

三个 Issue 都继承 `IssueBase`，对标 `CommissionHubIssue` 的实现方式。

Issue 生命周期管理：`VillageTheftIssueBehavior : CampaignBehaviorBase` 负责：
- `OnCheckForIssue` / `OnSettlementEntered` → 检查村庄是否有活跃案件 → 注册对应 Issue
- `DailyTick` → 推进案件调查进度 → 阶段迁移 → 生成下一阶段 Issue

##### VillageTheftIssueBehavior 骨架（中枢神经）

```csharp
public class VillageTheftIssueBehavior : CampaignBehaviorBase
{
    // ═══ 数据持有 ═══
    private List<VillageTheftCase> _allCases;           // 所有案件（含 Resolved）
    private Dictionary<string, bool> _villageAlertFlags; // 村庄警觉标记（跨案件）
    private Dictionary<string, IssueBase> _activeIssues; // CaseId → 当前活跃 Issue（用于关闭）

    // ═══ 存档 ═══
    public override void SyncData(IDataStore dataStore)
    {
        // _allCases、_villageAlertFlags JSON 序列化（对标 HeroNemesisTracker）
    }

    // ═══ 每日推进 ═══
    public void DailyTick()
    {
        foreach (var c in _allCases.Where(c => c.Stage != TheftCaseStage.Resolved))
        {
            c.LastUpdateDay = CampaignTime.Days;

            switch (c.Stage)
            {
                case TheftCaseStage.Discovery:
                    AdvanceInvestigation(c);   // 掷骰推进 → 检查是否 ≥1.0
                    CheckColdCaseTimeout(c);   // 7 天窗口耗尽 → Resolved
                    break;

                case TheftCaseStage.SuspectIdentified:
                    CheckSuspectDeadline(c);   // 悬赏期/赔钱期限满 → Retaliation
                    break;

                case TheftCaseStage.Retaliation:
                    CheckRetaliationTimeout(c); // 部队 15 天超时 → Resolved
                    CheckBudgetAndRespawn(c);   // 打赢后经费仍够 → 下一波
                    break;
            }
        }
    }

    // ═══ 阶段迁移（唯一入口） ═══
    private void TransitionStage(VillageTheftCase c, TheftCaseStage newStage)
    {
        var oldStage = c.Stage;
        if (oldStage == newStage) return;

        // 1. 关闭旧 Issue
        if (_activeIssues.TryGetValue(c.CaseId, out var oldIssue))
        {
            oldIssue.Close();                    // `!` 消失
            _activeIssues.Remove(c.CaseId);
        }

        // 2. 更新案件阶段
        c.Stage = newStage;

        // 3. 新阶段 ≠ Resolved → 注册新 Issue
        if (newStage != TheftCaseStage.Resolved)
        {
            var newIssue = CreateIssueForStage(c);
            Campaign.Current.IssueManager.AddIssue(newIssue);
            _activeIssues[c.CaseId] = newIssue;
        }
        else
        {
            // 4. Resolved：设置村庄警觉标记（如果嫌犯曾是玩家）
            if (c.IsSuspectPlayer || c.ThiefHeroId == Hero.MainHero.StringId)
                _villageAlertFlags[c.SettlementId] = true;

            // Issue 全部关闭后清理对话哨兵
            DialogueInjector.RemoveRelatedLines($"theft_{c.CaseId}");
        }
    }

    // ═══ 调查推进（阶段 1） ═══
    private void AdvanceInvestigation(VillageTheftCase c)
    {
        if (c.IsColdCase) return;

        float witnessBonus = c.WitnessCount * 0.15f;
        float rogueDefense = (c.ThiefHeroId == Hero.MainHero.StringId)
            ? Math.Min(0.5f, Hero.MainHero.GetSkillValue(DefaultSkills.Roguery) / 300f * 0.5f)
            : 0f;
        float dailyAdvance = 0.25f + witnessBonus - rogueDefense;

        c.InvestigationProgress = Math.Min(1.0f, c.InvestigationProgress + dailyAdvance);

        if (c.InvestigationProgress >= 1.0f && c.SuspectHeroId != null)
            TransitionStage(c, TheftCaseStage.SuspectIdentified);
    }

    // ═══ 冷案超时 ═══
    private void CheckColdCaseTimeout(VillageTheftCase c)
    {
        if (CampaignTime.Days - c.TheftDay > 7f && c.InvestigationProgress < 1.0f)
        {
            c.IsColdCase = true;
            TransitionStage(c, TheftCaseStage.Resolved);
            // 冷案不触发村庄警觉（没查到是谁）
        }
    }

    // ═══ 悬赏/赔钱期限 ═══
    private void CheckSuspectDeadline(VillageTheftCase c)
    {
        float deadline = c.IsSuspectPlayer ? 10f : 15f; // 嫌犯=玩家给更短期限
        if (CampaignTime.Days - c.SuspectIdentifiedDay > deadline
            && !c.PlayerPaidRestitution
            && !c.RetaliationResolved)
        {
            TransitionStage(c, TheftCaseStage.Retaliation);
            SpawnRetaliationParty(c);  // 见八 VillageTheftRetaliation
        }
    }

    // ═══ 报复部队超时 ═══
    private void CheckRetaliationTimeout(VillageTheftCase c)
    {
        if (c.RetaliationSpawned
            && CampaignTime.Days - c.RetaliationSpawnDay > 15f
            && !c.PlayerPaidRestitution)
        {
            c.RetaliationResolved = true;
            if (c.RetaliationBudget <= 0)
                c.PermanentEnemy = true;
            TransitionStage(c, TheftCaseStage.Resolved);
        }
    }

    // ═══ 打赢后检查是否再派 ═══
    private void CheckBudgetAndRespawn(VillageTheftCase c)
    {
        if (!c.RetaliationSpawned && c.RetaliationBudget > 0 && !c.RetaliationResolved)
        {
            // 上一波被打败 → 扣费已在战斗结算回调完成 → 检查经费
            if (c.RetaliationBudget >= GetWaveCost(c.RetaliationWaveCount + 1))
            {
                c.RetaliationWaveCount++;
                SpawnRetaliationParty(c);
            }
            else
            {
                c.PermanentEnemy = true;  // 没钱了，但恨你入骨
                TransitionStage(c, TheftCaseStage.Resolved);
            }
        }
    }

    // ═══ 玩家进村 → 注册 Issue ═══
    public void OnSettlementEntered(Settlement settlement)
    {
        if (!settlement.IsVillage) return;

        var activeCase = _allCases.FirstOrDefault(c =>
            c.SettlementId == settlement.StringId &&
            c.Stage != TheftCaseStage.Resolved);

        if (activeCase != null && !_activeIssues.ContainsKey(activeCase.CaseId))
        {
            var issue = CreateIssueForStage(activeCase);
            Campaign.Current.IssueManager.AddIssue(issue);
            _activeIssues[activeCase.CaseId] = issue;
        }
    }

    // ═══ Issue 工厂 ═══
    private IssueBase CreateIssueForStage(VillageTheftCase c)
    {
        return c.Stage switch
        {
            TheftCaseStage.Discovery         => new VillageTheftDiscoveryIssue(c),
            TheftCaseStage.SuspectIdentified => new VillageTheftSuspectIssue(c),
            TheftCaseStage.Retaliation       => new VillageTheftRetaliationIssue(c),
            _ => null
        };
    }

    // ═══ 外部回调入口 ═══
    // 对话/Quest 完成后调用，不走 DailyTick
    public void OnPlayerPaidRestitution(VillageTheftCase c)
    {
        c.PlayerPaidRestitution = true;
        TransitionStage(c, TheftCaseStage.Resolved);
    }

    public void OnCharmReprieve(VillageTheftCase c)
    {
        c.CharmReprieveUsed = true;
        c.InvestigationProgress = 0.5f;
        c.SuspectHeroId = null;
        TransitionStage(c, TheftCaseStage.Discovery);
    }

    public void OnSuspectDelivered(VillageTheftCase c)
    {
        TransitionStage(c, TheftCaseStage.Resolved);
    }

    public void OnRetaliationPartyDefeated(VillageTheftCase c)
    {
        // 不迁移阶段！扣除经费，检查是否再 spawn
        c.RetaliationSpawned = false;
        c.RetaliationPartyId = null;
        // 经费扣减由战斗结算回调处理
        CheckBudgetAndRespawn(c);
    }
}
```

**要点**：
- `TransitionStage` 是**阶段迁移的唯一入口**——所有路径（DailyTick/对话/Quest 回调）都走它，保证 Issue 生灭一致
- Issue 只持 `CaseId` 引用，全部状态读写走 `VillageTheftCase`，Issue 不存业务数据
- `OnSettlementEntered` 只在 Issue 尚未注册时创建（防重复 `!`）
- 打赢报复部队不走 `TransitionStage`——`Stage` 保持 `Retaliation`，只更新 `RetaliationSpawned = false`，等待 `CheckBudgetAndRespawn`

### 六.2 三个 Quest 类

| 类 | 对标 | 独特玩法 |
|---|------|---------|
| `InvestigateVillageTheftQuest` | `CommissionQuest` 的调查类 | **MVP 纯对话**：处理 notable 证人 → 汇报选嫌犯（账本名单）→ [出示证物] 检定。不做物理搜证 |
| `ApprehendVillageThiefQuest` | `CommissionQuest.BountyHunt` | **活捉机制(新轮子)**：mission 内击晕→俘虏键→`TakePrisonerAction`→带回交付；抓无辜 NPC → 出狱后 `NPC冤情→NemesisRecord` |
| `LeadRetaliationQuest` | `CommissionQuest` + WorldEvent 联动 | 带领村民报复队出击，对标带领友军作战 |

所有 Quest 继承 `QuestBase`（对标 `CommissionQuest`），使用已有的三种委托步骤（大地图追踪 → 战斗 → 交付）。

---

## 七、金钱赔偿统一规则（铁律4）

所有"赔钱/赔偿/罚金/安抚费"选项（阶段2 ×3、阶段3 ×5+罚金+安抚费、私下封口…）一律遵守：

1. **先查后转，原子**：弹出选项前先判 `Hero.MainHero.Gold >= 所需总额`。不足 → 选项**灰掉/隐藏**并提示，**绝不**让玩家付半截。
2. **走 `AgentControlHelper.TransferGold(玩家→村长, amount)`**，禁止裸调 `ChangeHeroGold`。
   - ⚠ 注意 `TransferGold` 本身"**不足自动截断、返回实际值**"——这是机械安全网，**不能当业务判断**。
     若不先卡 affordability 就直接调，钱不够时会"扣光玩家的钱但没付够、案子却消了"，正是铁律4 禁止的半截操作。
   - 正确写法：`if (player.Gold < total) { 选项不可选; } else { TransferGold(...); 关闭案件; }`
3. **守恒**：钱进村长（Headman Hero）口袋，不是凭空消失。罚金/安抚费若设定为"上缴世界/村庄基金"而非具体 Hero，则用 `TransferGold(玩家, null, amount)` 显式标注虚空去向（Sink，合法）。

---

## 八、新增/改动文件清单

### 新增文件

| 文件 | 职责 | 阶段 |
|------|------|------|
| `Stealth/VillageTheftCase.cs` | 案件数据模型 + 状态机 + List\<VillageTheftCase\> 管理器 + JSON 序列化 | Step1 |
| `Stealth/PlayerTheftLedger.cs` | 玩家偷窃账本（List\<TheftRecord\> + 写入/查询 + JSON）。**通用轮子**，将来赎罪/声望/被认出都用 | Step1 |
| `Quests/Commissions/VillageTheftIssues.cs` | 3 个 Issue 类 + IssueBehavior（创建/阶段迁移/DailyTick） | Step2 |
| `Quests/Commissions/VillageTheftQuests.cs` | 3 个 Quest 类（调查/追捕/带队报复） | Step2 |
| `Quests/WorldEvents/VillageTheftRetaliation.cs` | 报复部队 PartyComponent（如需自定义 AI）+ 部队 spawn 逻辑 | Step3 |
| `Stealth/CrimeSceneEvidence.cs` | 赃物 GameEntity 懒生成 + 现场发现 + NPC 认领（**新轮子**，登记 wheels.md） | Step4+ 增强 |

### 改动文件

| 文件 | 改动点 | 复杂度 |
|------|--------|--------|
| `VillageAnimalTracker.cs` | `RecordTheft` 增加参数 → 触发 `VillageTheftCase.OpenOrUpdate` | 低 |
| `InteractionMissionView.cs` | `TryStealAnimal` 传 thief + 目击者分两档入 `RecordTheft`；`HandleInput` 加按 H 查自己 → `OpenNPCInfoBoard(Agent.Main)`；倒地 `else` 分支加【俘虏】键 + 新 `TryCaptureAgent`（**新轮子**，登记 wheels.md） | 中 |
| `StealManager.cs` | `GetWitnesses` 复用；`StealSpecificItem`/偷动物加 `PlayerTheftLedger.Record` 记账 | 低（复用） |
| `NpcInfoVM.cs` / `AgentControlHelper.GetBagInfo` | 背包栏文本加"赃物来源"注脚（查账本）；board 加"自己"轻量路径 | 低 |
| `WorldEventConfig.cs` | 新增 `WorldEventType.VillageRetaliation` 配置 | 低 |
| `WorldEventDatabase.cs` | 枚举增加 `VillageRetaliation` | 低 |
| `MyBehavior.cs` | `DailyTick` → `VillageTheftCase.ProcessDaily`；`SyncData` → 序列化案件列表 + 账本 | 中 |
| `StoryContext.cs`（SaveDefiner） | 新增存档字段 | 低 |

---

## 九、实施阶段（建议分 4 步，优先"玩家是贼·栽赃"）

> 优先级原则：**先把"玩家自己是贼 → 栽赃他人"跑通（纯对话，零场景实体）**，
> 把贵的"真凶=NPC → 玩家发现真赃物"的沉浸体验放到最后。

### Step 1：数据层 + 基础链路（DDD：数据驱动）
- `VillageTheftCase` + `PlayerTheftLedger` 数据模型 + JSON 持久化
- `VillageAnimalTracker.RecordTheft` → 触发案件创建；扒窃/偷动物 → 账本记账
- 按 H 查自己 → NpcInfoVM 背包栏显示赃物来源
- `MyBehavior.DailyTick` → 每日推进调查进度
- 验证：偷羊/扒人 → 按 H 看到来源 → 存档读档 → 案件+账本仍在

### Step 2：玩家是贼·栽赃链（MVP 核心，纯对话）
- 3 个 Issue + 3 个 Quest 实现 + 阶段迁移逻辑
- 汇报对话：选嫌犯（账本名单）→ belief 检定 → [出示证物] 升档（三.2）
- 分支：栽赃强盗 / 栽赃具体人 / 失败转回玩家 / 赔钱封口
- 追捕 Quest 的【活捉机制】：倒地俘虏键 + `TryCaptureAgent` + `TakePrisonerAction`（三.3 新轮子）
- **不做**任何场景实体、搜证、护送
- 验证：偷羊 → 明天回村 `!` → 汇报"是强盗/某人干的" → 出示证物 → 嫌犯锁定
- 验证：接追捕 Quest → 击晕嫌犯 → 按俘虏键 → mission 结束人在俘虏栏 → 回村交付

### Step 3：报复部队 + 通知（WorldEvent + Narrative）
- `WorldEventType.VillageRetaliation` 配置
- 报复部队 spawn + AI（对标 `HeroNemesisTracker.SpawnNemesisParty`）
- **经济消耗战**：打赢不结案 → 从 `RetaliationBudget` 扣费派下一波（更强更贵），
  金库见底停派但 `PermanentEnemy=true`（关系永久敌对）
- **被俘惩罚 cutscene**：战败/投降 → 被带回村庄 → 播放过场（非坐牢，骑砍2 无监狱）
- NinjaNotification + 酒馆传闻 + 路途拦截
- 验证：偷羊 → 被锁定 → 报复部队追玩家 → 打赢后下一波更强 → 直到村庄没钱 / 战败播 cutscene

### Step 4+：沉浸增强（最贵，可延后）
- **真凶发现体验**：`EvidencePointer.AtScene` → 进村庄场景懒生成赃物 GameEntity（新轮子）
  → 请 NPC 带玩家到事发地（护送 AI）→ 发现赃物 → 信直接读 / 随身物拿去给 NPC 认领
  → 逻辑层不改，只给同一个证据指针加物理表现
- **冷案尾巴泄愤事件**（三.4）：冷案结算掷骰 → 小概率"村庄迁怒打错人"冤案连锁
- **被俘惩罚 cutscene**（路径C）：战败被带回村庄 → 非致死过场
- 验证：真凶=NPC 的案件 → 接调查 → 被带到牲口圈 → 捡到赃物 → 认出主人

---

## 十、开放问题（待对齐）

1. **目击系统**：`StealManager.GetWitnesses` 目前用于偷 NPC 装备。偷动物时能否复用？动物 Agent 不是 Human，`NpcSightSystem.IsPlayerSeeing` 是否对动物生效？
   - 可能需要一个简化版的目击检测：偷动物时检查周围一定距离内有多少村民 Agent。
   - ✅ **目击者记录方式已定（两档模型，见 五.1）**：notable（`HeroObject != null`）存 heroId 列表，可点名收买/对质；模板村民（`HeroObject == null`）**不存个体身份**——因为同模板的 `CharacterObject.StringId` 被多个 Agent 共享、且第二天 Agent 重新生成，只按 `Dictionary<模板id, 数量>` 计数。需"指认目击村民"时按模板找个长得对得上的活村民代言（替身非原人），配 fallback（模板没刷出来→随便抓村民→退化纯数字检定）。
   - ✅ **收买/吓唬时机已定：两者都做**。① 当场（偷窃 mission 内，Agent 还活着）→ 精确处理眼前 notable 的对话收买/吓唬，当场生效；② 第二天回村（纯 campaign 层）→ 对没脸村民做聚合检定（基于 `TemplateWitness` 计数），一次 Roguery 检定封口全部没脸目击者。两条路径互补，notable 在 mission 内处理、模板村民在 campaign 层处理。

2. **如果玩家不被识别为贼**（Roguery 高 + 无目击），`ThiefHeroId` 应该为 null。但 `VillageTheftCase` 需要知道实际贼是谁（用于嫁祸逻辑）。
   - ✅ **已定**：当前模型已区分三层：`ThiefHeroId`（客观真实，偷窃时写入，不可变）+ `SuspectHeroId`（阶段1 调查当前指向）+ `IdentifiedSuspectId`（阶段2 锁定）。不需额外拆分。

3. **IssueEffect 的具体数值**（Security/Prosperity 降多少）需要对齐原版的平衡。是否直接复用原版的 `IssueEffect` 模板（如 `security` / `prosperity` effect）？

4. **村民报复部队的文化/装备**：直接复用村庄所属文化的民兵模板？

   - ✅ **报复经费来源已定**：`RetaliationBudget` 播种 = Headman notable 的 `Hero.Gold` + 村庄繁荣度折算（`Settlement.Village.Hearth * 折算系数`，待定）。两者之和作为总预算。每波 `TransferGold(Headman → null/world, waveCost)` 扣减（Sink，合法），金库见底停派但 `PermanentEnemy=true`。实现时需反编译确认 notable 的 Gold 获取路径和村庄 Hearth 经济量访问方式。
   - ⏳ **被俘惩罚 cutscene 待定**：骑砍2 无坐牢，战败/投降 → 被报复部队俘虏带回村庄 → 播过场表现惩罚（示众/鞭笞/罚没，**非致死**）。参考 [vanilla_cutscenes](../Knowledge/vanilla_cutscenes/README.md)：处决场景可作模板，但需改成非致死并替换角色槽位，可能要自定义场景。结束后释放玩家。

5. **嫌犯候选弹窗**：候选 ≤3 用对话行即可；超过时用 `MultiSelectionInquiryData`——需确认该 API 在当前两个版本（1.2.12 / Latest）都可用，否则统一退化为对话行 + 翻页。

6. **栽赃已定结论**：① ✅ 不做"现场放置"，栽赃坍缩进汇报对话的 [出示证物]；② ✅ 嫌犯候选由 `PlayerTheftLedger` 生成（根治"hero 太多"）；③ ✅ provenance 靠"账本条目 + 仍持有该 ItemId"近似，非物品实例标记。

7. **按 H 查自己**：`OpenNPCInfoBoard` 现依赖 `SingNpcMemorySystem`，玩家可能没有。需给一条"自己"的轻量路径（无 memory 也能开背包/个人栏）。

8. **活捉机制（三.3）待确认**：① `TakePrisonerAction.Apply` 的确切签名（Hero 重载 vs CharacterObject 重载）；② 俘虏键位（暂定 R）；③ 抓走绑定村庄的 notable 是否破坏该 settlement（→ 嫁祸/嫌犯尽量限定流动 hero/wanderer）；④ 击晕在战斗混乱中能否稳定产出 `Unconscious` 而非 `Killed`（参看 [击晕机制](../Knowledge/击晕机制_引擎能力与实现踩坑.md)）。



---

## 十一、参考资料

- 原版 Issue-Quest 架构：[Knowledge/原版骑砍2任务系统分析.md](../Knowledge/原版骑砍2任务系统分析.md)
- 可复用模式目录：[Knowledge/vanilla_quests/04_patterns_catalog.md](../Knowledge/vanilla_quests/04_patterns_catalog.md)
- 完整 API 参考：[Knowledge/vanilla_quests/05_interface_reference.md](../Knowledge/vanilla_quests/05_interface_reference.md)
- 叙事设计铁律：[plans/rules/narrative-design.md](rules/narrative-design.md)
- 已造轮子速查：[plans/rules/wheels.md](rules/wheels.md)
- 偷盗系统全链路：[Knowledge/偷盗系统分析与优化方案.md](../Knowledge/偷盗系统分析与优化方案.md)
- 击晕机制踩坑：[Knowledge/击晕机制_引擎能力与实现踩坑.md](../Knowledge/击晕机制_引擎能力与实现踩坑.md)
