# NPC委托任务系统 — 设计方案 (Gameplay-Driven)

## Context

当前 `QuestManager.cs` 只有"大名主命"系统，且硬编码了大量日本战国内
容，未与 Intent 系统集成。用户希望增加一套 **NPC 委托任务**（NPC Commissions）。

核心目标：**玩法驱动、有多样性、过程不无聊、能赚钱**。
**核心约束：少对话/打字，重 gameplay 机制——乐趣来自战术选择、资源管理、技能运用，不是测试最优 prompt。**

LLM 的角色仅限：
- 生成委托描述/变故通知的**风味文本**（有 LLM 时增强，无 LLM 时模板兜底）
- 不参与玩家决策，不要求玩家打字

---

## ⚠️ 引擎可行性审计（经 ilspycmd 反编译验证）

### 引擎真实存在的系统

| 系统 | API 证据 |
|------|---------|
| **越狱营救** | `SandBoxMissions.OpenPrisonBreakMission()` + `PrisonBreakMissionController` |
| **潜行** | `isStealth` 参数 + `GetStealthEquipmentForPlayer()` + `sp_player_stealth` 生成点 |
| **帮派暗巷** | `Alley` 类 + `MissionCrimeHandler` + 占领/清理机制 |
| **Party 投降** | `DoesSurrenderIsLogicalForParty()` + `PlayerSurrender`/`EnemySurrender` |
| **昼夜差异** | `IsDay`/`IsNight` + `DisableAtNightTag`/`EnableAtNightTag` + `PartyVisibilityChangedEvent` |
| **匪穴** | `Hideout` + `GetDefenderParties` + `MapEvent.BattleTypes.Hideout` |
| **地图箭头** | `IMapTracksCampaignBehavior.AddMapArrow()` |
| **囚犯/逃亡** | `PrisonRoster` + `IsPrisoner` + `IsFugitive` |
| **感叹号** | `IssueManager` + `PotentialIssueData` + `OnCheckForIssueEvent` |
| **护送** | `SetMoveEscortParty()` |
| **载重减速** | `NumberOfPackAnimals` + `MobileParty.Speed` |
| **市场/贸易** | `TownMarketData` + `GetAveragePriceOfItemInTheWorld` |

### 引擎**不存在** → 已修正

| 臆想 | 修正 |
|------|------|
| ~~猎杀"巨兽"~~ | → 猎杀**传奇匪首**（有名 Hero 的超强 bandit boss） |
| ~~捕猎"野生名马"~~ | → **寻购名马**：去市场搜或从拥有者手里买/抢 |
| ~~酒馆"椅子打架"~~ | → **竞技场特别赛**或空地决斗 |
| ~~Scout"痕迹追踪"~~ | → Scout **视野侦测**+`AddMapArrow`标记目标位置 |
| ~~"下药"~~ | Poison 只是 NPC 绰号，无 gameplay → 删除此路径 |
| ~~"收买内应改道"~~ | → 删除，保留"花钱买情报" |
| ~~"破坏桥梁"~~ | → 删除 |
| ~~"拍卖/竞拍"~~ | → **跨城比价代购** |
| ~~"修路障/组织民兵"~~ | → 简化为"村内迎击 vs 主动出击" |
| ~~"地形地标匹配"~~ | → `AddTrackedObject` 标记坐标搜索链 |
| ~~"替身假扮"~~ | → **引开追兵**（吸引敌方 Party 追击，不涉及 disguise） |

---

## 一、委托分类（16 类，分六大玩法方向）

### A. 狩猎/追踪类 — Scout/Roguery 技能运用，不是无脑刷怪

#### 1. 悬赏缉拿 (Bounty Hunt)
- **核心玩法**：目标部队在大地图上游走，玩家用 **Scout 技能追踪痕迹**找到并击败
- **策略乐趣**：
  - 目标会移动（不是固定刷怪点），Scout 越高痕迹越清晰（方向/距离/停留时间）
  - 可选白天突袭（敌人警觉但视野好）或夜间偷袭（敌人松懈但玩家视野差）
  - 可设伏（Engineering 检定在目标路径上设陷阱→减少敌军数量）
- **引擎支持**：`MobileParty` 自由移动, `AddTrackedObject`, Scout 技能判定, `MapEventEnded`
- **多解法**（都是游戏操作，不需要对话）：
  - 正面战斗 / 夜间偷袭(Roguery) / 设伏(Engineering) / 花钱买情报精确位置
- **随机变故**：目标可能被第三方先干掉、目标途中壮大（招募了更多兵）、目标发现被追→反伏击

#### 2. 猎杀传奇匪首 (Legendary Outlaw Hunt)
- **核心玩法**：一个**有名有姓的超强匪首**（有 HeroId 的 bandit leader）在特定区域活动，玩家追踪+击败
- **策略乐趣**：
  - 战前准备：带克制装备、医疗物资、足够兵力
  - 击杀后获得该匪首的**唯一掉落装备**（他身上穿的，不是商店货）
  - 匪徒营火痕迹/路人目击情报→Scout 越高越早发现目标位置
- **引擎支持**：`MobileParty` 生成特殊敌人（强兵力+好装备）, `MapEventEnded`, `AddMapArrow` 标记区域, 战利品系统
- **多解法**：正面围剿 / 夜间偷袭(利用 `IsNight` + 潜行 `isStealth`) / 花钱买目击情报精确位置

#### 3. 清剿匪穴 (Hideout Clear)
- **核心玩法**：清理一个藏身处，但可以选择进攻方式
- **策略乐趣**：
  - 白天进攻（敌人全在但视野好）vs 夜间潜入（敌人少但黑暗）
  - 可先派斥候侦察→得知内部敌人数量和类型→针对性配兵
- **引擎支持**：`Hideout.IsSpotted`, `Hideout.GetDefenderParties`, `MapEvent.BattleTypes.Hideout`
- **多解法**：正面强攻 / 夜袭(Roguery) / 策反内部线人(先完成一个前置小任务)

---

### B. 护送/运输类 — 路线规划 + 风险管理

#### 4. 护卫商队 (Caravan Escort)
- **核心玩法**：护送商队从 A 城到 B 城，玩家必须全程跟随
- **策略乐趣**：
  - **路线选择**：大路（安全但远）、小路（近但危险）、山路（绕开战场但慢）
  - Scout 技能影响"提前发现伏击"的概率——高 Scout 能在敌人进攻前发现并绕开
  - 可考虑货物品类（奢侈品吸引更多盗贼但报酬高）
- **引擎支持**：`SetMoveEscortParty`, `SettlementEntered`, Scout 感知检定
- **多解法**：
  - 跟随护送(标准) / 重金贿赂已知盗贼团买路 / 分兵引诱(派一队兵走岔路引开敌人)

#### 5. 限时运粮 (Emergency Delivery)
- **核心玩法**：某地急缺物资，**限时内**送达。载重影响速度——带越多赚越多，但容易超时
- **策略乐趣**：
  - **载重 vs 速度**的经典取舍：每单位货物 = 额外报酬，但移动速度 -X%
  - 可选雇佣临时护卫（花钱）或轻装独行（快但危险）
  - 超时报酬递减（不是瞬间失败，而是每分钟扣 X%）
- **引擎支持**：`MobileParty.Speed`, 物品重量系统, `SettlementEntered` + 时限检测
- **多解法**：满载冒险 / 轻装速递 / 分批运输(安全但效率低)

---

### C. 寻回/探索类 — Scout/Tracking 技能驱动

#### 6. 失物追寻 (Lost Item)
- **核心玩法**：NPC 的贵重物品被盗/丢失，玩家需要通过一系列坐标点逐步追踪
- **策略乐趣**：
  - 委托人给第一个线索（"最后出现在XX方向"）→`AddTrackedObject` 标记区域
  - 到达该区域后 Scout 检定→发现下一阶段线索（生成下一个追踪标记）
  - 最终找到赃物/小偷→选择处理方式
- **引擎支持**：`AddTrackedObject` 标记线索地点, Scout 检定, `SettlementEntered` 检测踩点, `AgentControlHelper.TransferItems`
- **多解法**：追踪找到 / 花钱从黑市赎回 / 抓小偷换回(Roguery)

#### 7. 寻宝 (Treasure Hunt)
- **核心玩法**：获得藏宝信息→按多个标记坐标逐步缩小搜索范围→找到宝藏
- **策略乐趣**：
  - 藏宝信息分 2-3 个坐标碎片（分别从不同渠道：买/偷/任务奖励）
  - 收集全碎片后标记最终挖掘地点→到达后 Scout 检定发现具体位置
  - 宝藏地点可能有少量守护者（随机）
- **引擎支持**：`AddTrackedObject` 标记坐标, `SettlementEntered` 检测到达, 物品生成
- **多解法**：独自寻宝 / 雇向导(花钱减少搜索范围) / 卖藏宝信息(不寻了直接变现)

#### 8. 寻购名马 (Horse Acquisition)
- **核心玩法**：委托人想要一匹特定品种/属性的马，玩家去各大城镇市场搜索或从拥有者手里买/抢
- **策略乐趣**：
  - 多个城镇马市价格不同→**比价寻优**
  - 如果市场上没有→去找拥有该马的 NPC 交涉（买或抢）
  - Trade 技能砍价→预算内买到好马，差价归自己
- **引擎支持**：`ItemObject.IsAnimal` + `ItemObject.HorseComponent`, 市场价格系统, Trade 检定
- **多解法**：市场购买 / 从拥有者手里高价买 / 拦路劫马(最高风险)

---

### D. 竞技/战斗类 — 自身战力考验

#### 9. 地下拳赛 (Underground Fight)
- **核心玩法**：委托人办了非法竞技，玩家代表他出战，有特殊规则
- **策略乐趣**：
  - 规则变化：禁用盾牌 / 只能用单手武器 / 1v3 车轮战 / 限时击败
  - **下注机制**：可在自己身上押注→赢了双倍报酬
  - 输了不只丢脸：委托人可能被庄家追债→迁怒玩家
- **引擎支持**：`TournamentFinished`, Mission 内战斗, `AgentControlHelper`(装备限制)
- **多解法**：正面战胜 / 赛前练级提升技能 / 雇高手代打(花钱)

#### 10. 村防应援 (Village Defense)
- **核心玩法**：村庄即将被劫掠，赶在敌人到达前布置防御
- **策略乐趣**：
  - 到达后**准备时间有限**（游戏内小时），需要分配时间：
    - 修筑路障(Engineering)→减少敌人数
    - 组织民兵(Leadership)→增加友军数量
    - 侦查敌情(Scout)→提前知道敌人兵种构成
  - 选择主动出击拦截 vs 在村里等敌人来
- **引擎支持**：`OnVillageBeingRaided`, Engineering/Leadership 检定, `MapEventEnded`
- **多解法**：正面守村 / 主动迎击 / 给劫匪送钱请他们走

#### 11. 竞技场特别赛 (Arena Special)
- **核心玩法**：委托人安排了一场特殊规则的竞技，玩家代表他出战
- **策略乐趣**：
  - 规则变化：限用单手武器 / 禁用盾牌 / 限时击败多个对手
  - **下注机制**：可在自己身上押注→赢了双倍报酬
  - 输了不只丢脸：委托人可能被庄家追债→迁怒玩家
- **引擎支持**：`TournamentFinished` + `JoinTournament`, 可限制玩家装备, Mission 内战斗
- **多解法**：正面战胜 / 赛前练级提升技能 / 雇高手代打(花钱)

---

### E. 隐秘/行动类 — Roguery 技能考验

#### 12. 越狱营救 (Prison Break)
- **核心玩法**：某人的朋友/家人被关在敌对城镇的监狱里
- **策略乐趣**：
  - **两种潜入方式**：
    - 贿赂守卫(花费金币，BribePaid 机制)→正大光明进去
    - 潜入(Roguery 检定)→高风险但省钱
  - 救出后**护送脱逃**阶段：可能有追兵
  - 可选择"要不要顺便放走其他囚犯"制造混乱掩护逃跑（道德抉择）
- **引擎支持**：`PrisonersChangeInSettlement`, `BribePaid`, Roguery 检定, `MobileParty`
- **多解法**：贿赂守卫 / 潜入地牢 / 外交施压(有领主身份)

#### 13. 物资截获 (Supply Intercept)
- **核心玩法**：在敌方补给队到达目的地之前拦截并夺取物资
- **策略乐趣**：
  - **时间窗口**有限——补给队有预定路线，必须在到达前拦截
  - 可 Scout 侦查提前找到最佳伏击点
  - 截获的物资可以：交给委托人(报酬) / 自己留着(物资价值可能更高)
- **引擎支持**：`MobileParty.Ai.SetMoveGoToSettlement`, Scout 追踪, `MapEventEnded`
- **多解法**：正面截击 / 收买内应让车队改道(花钱) / 破坏桥梁延迟车队(Engineering)

#### 14. 引开追兵 (Decoy Mission)
- **核心玩法**：委托人正在被追杀，需要玩家**吸引追兵注意**，让委托人趁机逃跑
- **策略乐趣**：
  - 玩家带少量兵故意暴露行踪吸引追兵→不能硬刚，需要**边打边跑**
  - 坚持的时间越长→委托人逃得越远→报酬越高
  - 如果被追上→被迫战斗，但委托人那边可能还没跑远
- **引擎支持**：`MobileParty.Ai.SetMoveEngageParty`(追兵), 计时机制, 存活判定, `EnemySurrender`
- **多解法**：诱敌深入(拉到友军附近) / 花钱请其他佣兵队帮忙挡 / 设伏反击(Engineering)

---

### F. 经济/贸易类 — 市场判断 + Trade 技能

#### 15. 紧急供货 (Supply Emergency)
- **核心玩法**：某城急缺某物资，委托人出价收购，玩家需要从其他城镇**低价进货**
- **策略乐趣**：
  - 多个候选进货城镇，价格不同、距离不同→**需要判断性价比最优路线**
  - Trade 技能影响能看到的"价格情报"范围
  - 限时内送达，超时报酬递减
- **引擎支持**：`TownMarketData`, 价格系统, Trade 检定, `SettlementEntered`
- **多解法**：市场采购 / 从自己库存直接给 / 去产地村庄挖原材料

#### 16. 跨城代购 (Procurement Agent)
- **核心玩法**：委托人想买一件稀有装备但身份不便出面，给预算让玩家去各大城镇**比价代购**
- **策略乐趣**：
  - 多个城镇搜索目标物品→**比价寻最低**
  - **预算管理**：委托人给了上限，花钱越少自己分成越多
  - Trade 技能影响砍价幅度
  - 如果市场上买不到→需要找拥有该物品的 NPC 交涉
- **引擎支持**：`MBObjectManager.GetObject<ItemObject>`, Trade 检定, 城镇物品搜索, 市场价格

---

## 二、多解法设计（Gameplay 驱动，非对话驱动）

每个委托的解决路径基于**游戏技能和资源**，不需要玩家打字：

| 路径类型 | 需要的资源/技能 | 风险 | 典型报酬 |
|----------|----------------|------|---------|
| **战力突破** | 部队数量/质量, 玩家战斗技能 | 兵员损失 | 标准报酬 |
| **潜行智取** | Roguery/Scout 技能 | 被发现→恶化局面 | 最高报酬 |
| **财力解决** | 金币 | 无风险但成本高 | 净收益最低 |
| **技术破局** | Engineering/Trade/Medicine 等专精技能 | 检定失败→浪费时间 | 中等报酬 |
| **借力打力** | 关系网(找其他NPC帮忙) | 欠人情/对方要分账 | 报酬分流 |

**切换规则**：玩家在委托进行中随时可以切换路径。比如追踪悬赏目标发现打不过→改为花钱买情报再偷袭（潜行路径）。

---

## 三、委托场所与风味（TK5 多职业任务借鉴 + RDO 赏金猎人机制）

### 3.1 灵感来源

**太阁立志传5** 的多样化任务体系：
- 武士有主命（筹粮/征兵/外交/攻城），商人有交易任务（低买高卖/投资），忍者有隐秘任务（侦查/破坏/暗杀），剑豪有道场挑战和踢馆，浪人在酒馆接五花八门的零活
- **核心借鉴**：不同"职业"下有不同**风味**的任务类型——这些任务本身是好玩的，不应该被身份锁死
- 在骑砍2里，**玩家是自由的**——既是剑客也是商人也可以当佣兵。所有委托类型全部开放，按**场所**自然分类

**荒野大镖客OL** 的赏金猎人：
- 赏金分 $$$ / $$ / $ 三级 + 传奇悬赏（唯一命名目标，打过不再刷新）
- 活捉报酬 >> 击杀报酬，且活捉后要经历押送阶段（目标同伙可能半路劫囚）
- 追踪阶段（线索→追踪→发现）→ 战斗阶段 → 押送阶段，三阶段各有玩法

### 3.2 委托场所（对应 TK5 的酒馆/座/道场/忍者里——但不锁身份）

不同场所的委托**风味**不同，但**所有玩家都能进**：

| 场所 | 对应 TK5 原型 | Bannerlord 入口 | 委托风味 | 典型委托 |
|------|-------------|----------------|---------|---------|
| **酒馆** | 酒馆（浪人零活）+ 座（商人情报） | 城镇→去酒馆 | 三教九流、小道消息、赏金告示、灰色委托 | 悬赏缉拿、护卫商队、酒馆乱斗、寻宝 |
| **要人宅** | 座（商人行会）+ 寺（文化人） | 城镇→找要人 | 贸易、供货、采购、投资、制作类 | 紧急供货、拍卖代理、失物追寻 |
| **领主大厅** | 大名家（武士主命） | 城镇→去城堡 | 军事、外交、领地管理（主命类，已有） | 物资截获、越狱营救、村防应援 |
| **村庄** | 农村（地侍/村长） | 进村→找头人 | 庶民疾苦、防御、寻物、护送 | 村防应援、限时运粮、捕猎名马、替身护送 |
| **竞技场** | 道场（剑豪挑战） | 城镇→竞技场 | 战斗竞技、实力证明、名声类 | 地下拳赛、猎杀巨兽、清剿匪穴 |
| **帮派暗巷** | 忍者里（隐秘任务） | 城镇→Alley/帮派头目 | 高风险灰色地带 | 越狱营救、替身护送、酒馆乱斗 |

**关键规则**：
- **酒馆是万能入口**——任何时候都能在这里找到至少 1-2 个基础委托（保障玩家总有活干）
- 其他场所需要玩家**自己发现**（走进要人宅、村庄、竞技场），不需要任何身份门槛
- 同一委托类型可能出现在多个场所（比如"悬赏缉拿"既可以在酒馆告示看到、也可以从村庄头人那里接）
- 场所影响委托的**难度和报酬区间**，但不影响玩家能否接

### 3.3 委托难度进阶（RDO 赏金分级 + TK5 功勋递进）

每种委托类型内部有**难度分级**，完成了低阶才会刷出高阶：

```
悬赏缉拿的难度进阶:

$ 级 (新手——酒馆/村庄可接):
  目标: 1 个弱匪首, 几乎无护卫, 固定位置或小范围移动
  报酬: 100-300g, 活捉额外+50g
  典型描述: "村口的张三欠了酒钱跑了"

$$ 级 (熟练——酒馆/要人宅可接):
  目标: 1 个中级目标 + 3-5 护卫, 在地图上移动
  报酬: 500-1000g, 活捉+200g, 有限时
  典型描述: "绿林帮的账房卷款逃了"

$$$ 级 (高手——酒馆/帮派暗巷可接):
  目标: 1 个强目标 + 10+ 护卫, 快速移动, 可能伏击玩家
  报酬: 1500-3000g, 活捉+500g, 限时, 有竞争者也在追
  典型描述: "沙漠马匪二当家最近在这一带出没"

传奇悬赏 (唯一——酒馆传闻触发):
  目标: 命名 Boss（有唯一 HeroId）, 超强护卫, 多阶段(追踪→清理据点→决战→押送)
  报酬: 5000-10000g + 唯一掉落装备, 活捉双倍
  每个传奇目标只会出现一次，杀死/捕获后不再刷新
  典型描述: "红袍雷德——横行卡拉迪亚二十年的匪王，至今无人能捉"
```

**进阶条件**：
- 完成 3 个 $ 级悬赏 → 开始刷出 $$ 级
- 完成 5 个 $$ 级 + 至少一次 ⭐⭐ 评级 → 开始刷出 $$$ 级
- 完成所有 $$$ 级 + Scout/Roguery 技能 ≥ 150 → 传奇悬赏传闻出现

### 3.4 活捉 vs 击杀

对于悬赏/缉拿类委托，利用原生 **Party 投降系统**：

| 结算方式 | 报酬倍率 | 实现 |
|----------|---------|------|
| 击杀目标 | ×0.5 | 正常战斗消灭 → 目标部队全灭 |
| 活捉（逼降） | ×1.0 | 战力碾压触发 `EnemySurrender` → 目标 Party 投降，目标 Hero 成为囚犯 |
| 活捉（完美） | ×1.5 | 夜间潜行(`isStealth`) + Roguery 检定 → 偷袭目标营地，无伤捕获 |

活捉后的**押送阶段**：
- 目标成为你 Party 的囚犯（走原生 `PrisonRoster`）
- 押送期间可能随机触发目标同伙 `MobileParty` 追击劫囚
- 可雇额外护卫（花钱）降低劫囚概率

### 3.5 完成质量评级（不止成功/失败）

| 评级 | 条件 | 报酬倍率 | Trust |
|------|------|---------|-------|
| ⭐⭐⭐ 完美 | 限时内完成 + 无伤亡 + 指名目标活捉 + 附加目标全部达成 | ×1.5 | +15 |
| ⭐⭐ 优良 | 限时内完成 + 轻度伤亡 | ×1.0 | +10 |
| ⭐ 完成 | 超时但仍完成 / 有较重伤亡 / 目标死亡(悬赏类) | ×0.7 | +5 |
| ✗ 失败 | 未完成 | 追讨定金 | -10~40 |

---

## 四、反无聊机制

### 4.1 动态变故表

| 变故类型 | 触发条件 | 对玩家的影响 |
|----------|---------|-------------|
| **竞争者出现** | 委托开始后随机天数 | 另一个队伍也在追同一个目标→比速度 |
| **目标移动/升级** | 玩家距离目标 > 一定距离 | 目标换了位置/招募了更多兵→需要重新侦查 |
| **环境变化** | 特定 Settlement 事件 | 目标城镇被围城/村庄被烧→进入方式改变 |
| **委托人追加条件** | 半路收到信 | 追加要求(+额外报酬)或改变目标 |
| **天气/季节** | 随机 | 大雪/暴雨→移动速度-20%，但敌人视野也降低 |

### 4.2 旅途事件

旅途中按时间和距离触发，不影响主线但增加趣味：

| 事件 | 触发 | 效果 |
|------|------|------|
| **受伤旅行者** | 随机遭遇 | 用 Medicine 救他→得情报/物品，不管→无影响 |
| **困住的车队** | 随机遭遇 | 帮忙修车(Engineering)→报酬/感谢，不帮→无影响 |
| **路遇同行** | 同方向旅行者 | 一起走一段，获得关于目标的额外情报(Scout bonus) |
| **天候突变** | 随机 | 强制改变移动速度/视野，考验路线规划 |

纯模板文本（无 LLM 降级），有 LLM 时增强 flavor。

---

## 五、经济与信任设计

### 5.1 委托发起人与目标约束

**硬性规则**：
- **委托发起人必须有 HeroId**（城镇要人/村庄头人/领主/浪人），**禁止**随机刷出的模板 NPC 作为发起人——因为玩家需要能找到他交任务
- **目标对象优先有 HeroId**，但部分泛用玩法（送货到某城、清剿区域匪徒）不需要特定人物

以下 NPC 类型**可作为**委托发起人：

| NPC 类型 | Occupations | 典型委托类别 |
|----------|------------|-------------|
| 城镇要人 (Notable) | Merchant, Artisan, GangLeader | 经济类/灰色类/寻回类 |
| 村庄头人 (Headman) | Headman, RuralNotable | 护送类/村防/狩猎类 |
| 领主 (Lord) | Lord | 战斗类/截获类/营救类 |
| 浪人 (Wanderer) | Wanderer | 竞技类/灰色类/寻回类 |

**禁止**作为发起人：
- 随机生成的商队护卫、村民、无 HeroId 模板 NPC
- 已死亡/已被囚禁超过 N 天的 NPC（无法交任务）

**委托目标分类**：

| 目标类型 | 需要特定 Hero? | 适用委托 | 示例 |
|----------|---------------|---------|------|
| **指名目标** | ✅ 必须有 HeroId | 悬赏缉拿、猎杀悍匪、越狱营救、酒馆乱斗、替身护送 | "追杀叛逃骑士 XXX" |
| **区域/地点目标** | ❌ 不需要 | 清剿匪穴、护卫商队、限时运粮、寻宝、村防应援、物资截获 | "清剿沃斯特罗姆附近的匪穴" |
| **物品目标** | ❌ 不需要 | 紧急供货、拍卖代理、失物追寻、捕猎名马 | "采购 50 单位谷物" |

### 5.2 报酬结构

```
总收益 = 定金(接委托时给) + 尾款(完成时给) + 表现奖金

表现奖金来源：
- 速度奖：比期限快 X 天→额外 +X%
- 完美奖：没有任何伤亡完成→额外 +10%
- 额外目标：完成委托人追加的附加条件
```

### 5.3 信任系统 (Trust)

| 等级 | 效果 |
|------|------|
| 陌生人(0-20) | 低难度委托，定金比例 30% |
| 熟人(21-50) | 中难度委托，定金 25%，可同时接 2 个 |
| 信赖(51-80) | 高难度委托，定金 20% |
| 心腹(81-100) | 专属任务线，定金 15%，报酬 ×1.5 |

### 5.4 定金与失败惩罚

```
委托失败/超时
    ↓
NPC 要求退还定金
    ↓
┌── 退还定金 → Trust -10，可继续接委托
├── 拒绝退还 → Trust -40 + 恶名+1 + 结仇 + 该地区声望-X
└── Charm检定→减半退还(失败=拒绝退还后果)
```

### 5.5 恶名 (Infamy)

- 拒还定金：恶名 +1
- 高恶名：诚实 NPC 不给委托，但灰色 NPC 更愿意给高风险高回报委托
- 完成高难度委托可逐步消除恶名

---

## 六、委托发现机制 — 利用骑砍2原生感叹号（!）系统

### 6.1 原生系统原理

骑砍2自带 Issue 系统，当 NPC 有可接任务时自动在头顶显示 **!**：

```
CampaignEvents.OnCheckForIssueEvent 触发（NPC 进入玩家视野时）
    ↓
各 IssueBehavior 检查条件→ IssueManager.AddPotentialIssueData(hero, data)
    ↓
IssueManager 为 hero 分配 Issue → hero.Issue = issue
    ↓
城镇菜单检查: hero.Issue != null && hero.Issue.IssueQuest == null
    ↓
菜单选项标记 AvailableIssue → NPC 头顶 ! 感叹号
```

关键数据结构：
- `IssueManager.Issues` — `Dictionary<Hero, IssueBase>`，有 entry 的 NPC 就有标记
- `GameMenuOption.IssueQuestFlags` — `AvailableIssue | ActiveIssue | TrackedIssue | ActiveStoryQuest | TrackedStoryQuest`
- `PotentialIssueData` — 轻量 struct，包含 `OnStartIssue` delegate + `IssueType`

### 6.2 我们的接入方式：信号 Issue + Intent 菜单

创建一个极简的 `CommissionHubIssue : IssueBase`，**只做信号用**（不生成 Quest，不走 Issue 对话流）：

```csharp
// 1. CampaignBehaviorBase 监听
CampaignEvents.OnCheckForIssueEvent.AddNonSerializedListener(this, OnCheckForIssue);

// 2. 当 NPC 有可接委托时，注册信号
private void OnCheckForIssue(Hero hero) {
    if (CommissionGenerator.HasCommissionsFor(hero, out int count)) {
        Campaign.Current.IssueManager.AddPotentialIssueData(hero,
            new PotentialIssueData(OnIssueSelected, typeof(CommissionHubIssue), 
                                   IssueBase.IssueFrequency.Common));
    }
}

// 3. 创建信号 Issue —— 极简实现，仅触发 !
private IssueBase OnIssueSelected(in PotentialIssueData pid, Hero issueOwner) {
    return new CommissionHubIssue(issueOwner);
}
```

**玩家视角的完整流程**：

```
进入城镇 → 看到 NPC 头顶 !
    ↓
走过去对话 → Intent 菜单出现 "【找工作】（3个委托可接）"
    ↓
点击 → 展示委托列表（报酬/期限/难度）→ 选一个
    ↓
讨价还价(Charm/Trade检定) → 定金到账 → CommissionQuest 启动
    ↓
委托完成后回来交 → 尾款到账 → Trust 更新
```

### 6.3 视觉层级

| 标记 | 含义 | 触发条件 |
|------|------|---------|
| **! (蓝/黄色)** | 有新委托可接 | 该 NPC 有 ≥1 个新委托，玩家尚未查看 |
| **? (蓝色)** | 有进行中的委托要找此 NPC | 玩家接了此 NPC 的委托，可以交任务/汇报 |
| **灰色 !** | 有委托但玩家不满足条件 | 技能不够/关系不够/名额已满 |

### 6.4 委托来源 + 发现渠道

| 来源 | NPC 类型 | 发现方式 |
|------|---------|---------|
| 城镇要人 | 工匠/商人/帮派头目 | 进入城镇→看到 ! → 对话 |
| 村庄长老 | 头人 | 进入村庄→看到 ! → 对话 |
| 酒馆 NPC | 浪人/旅行商人 | 酒馆中看到 ! → 对话 |
| 大地图信使 | 随机 | `NinjaNotificationManager` 弹通知"XX城有人找" |
| **主动搭话** | 随机 NPC 主动找上门 | NPC 走向玩家触发对话（下面详述） |

### 6.5 主动搭话（RDR2 式随机遭遇）

NPC **主动接近玩家**请求帮忙，不依赖 ! 标记。两种场景：

**场景 A：村镇内 NPC 走过来**

```
玩家进入城镇/村庄场景
    ↓ (随机触发，概率受区域繁荣度/治安影响)
一个 NPC (有 HeroId 的要人/浪人) 走向玩家
    ↓ AgentBrain.EnqueueAction(new ForceTalkAction(npc, player))
NPC 主动发起对话："这位大人，能耽误您一点时间吗……"
    ↓
展示委托（1个，紧急/限时类为主）→ 玩家选择接受/拒绝
```

- **技术复用**：`AgentBrain`(已有) + `ForceTalkAction`(已有) + `MoveToActor`(已有) → NPC 走向玩家然后强制对话
- **触发条件**：玩家在城镇/村庄场景中，该 NPC 有委托 + 没有 !（更自然——不是所有 NPC 都会挂感叹号）
- **委托类型偏向**：紧急供货、越狱营救、替身护送、寻人——这些"急事"更符合主动搭话的情境

**场景 B：大地图旅行者追上玩家**

```
玩家在大地图上行进
    ↓ (随机触发，受途经区域类型影响)
一个临时生成的"求助者" MobileParty 追上玩家
    ↓ 弹 Inquiry 窗口
"壮士请留步！我有急事相求……" + 简短委托描述
    ↓
┌── 接受 → CommissionQuest 启动，求助者消失（或跟随）
└── 拒绝 → 求助者离开，不影响 Trust
```

- **技术实现**：`CampaignBehaviorBase.DailyTick` 中概率生成求助方，`SetMoveEngageParty(MobileParty.MainParty)` 追上玩家
- **求助者**：可以是已有 HeroId 的 NPC（浪人/商人），也可以是一个"信使"角色（委托完成后去对应城镇找真正的委托人）
- **委托类型偏向**：村防应援(紧急)、猎杀巨兽(刚发现)、限时运粮、悬赏缉拿(刚发生的案子)

---

## 七、实现架构（2024-06-13 更新：双路径 + 告示板 + 叙事层）

### 7.1 文件组织

```
Quests/
    Commissions/
        CommissionData.cs           # 运行时数据 [SaveableField] + CommissionDef 模板 + CommissionTierProgression
        CommissionQuest.cs          # QuestBase 子类（生命周期：叙事阶段 → 正式启动 → 事件驱动 → 结算）
        CommissionHubIssue.cs       # IssueBase 子类（! 信号）+ CommissionIssueBehavior（OnCheckForIssue 监听）
        CommissionGenerator.cs      # 双路径生成：直接委托人 / 告示板聚合
        CommissionIntent.cs         # RequestCommissionIntent（看告示板/直接接）+ ConfirmCommissionIntent（叙事确认）
        CommissionNarrative.cs      # 玩家沟通层：首次介绍、Trust升级通知、难度解锁庆祝、状态面板
        ComplicationTable.cs        # 动态变故表（每日15%概率）
        JourneyEvents.cs            # 旅途事件表（每日25%概率）
        TrustSystem.cs              # 信任值（JSON持久化，四级：陌生人/熟人/信赖/心腹）
        InfamySystem.cs             # 恶名值（JSON持久化，拒还定金+1，Expert+完成-1）
```

### 7.2 集成点

| 现有系统 | 集成方式 |
|----------|---------|
| **Issue 感叹号** | `CommissionIssueBehavior` 监听 `OnCheckForIssueEvent`，通过 `CommissionHubIssue` 触发原生 ! |
| **Intent 系统** | 两个 Intent：`RequestCommissionIntent`（看委托/接委托）+ `ConfirmCommissionIntent`（告示板路径的叙事确认），均注册到 IntentRegistry |
| **InteractionOptionType** | 新增 `FindWork`（不复用 RequestWork，语义独立） |
| **QuestBase** | `CommissionQuest : QuestBase`，复用事件注册/进度更新。新增 `BeginNarrativePhase` / `ConfirmQuest` 两阶段启动 |
| **AgentControlHelper** | 所有金钱进出 + 物品转移走统一管道。定金在 `AcceptCommission`（直接）或 `ConfirmQuest`（告示板）单次支付 |
| **LLM (可选)** | 仅 flavor text 增强。委托描述、变故通知、旅途事件均可 LLM 增强，无 LLM 时模板兜底 |
| **Occupation 原生枚举** | `Tavernkeeper`（酒馆老板）、`Headman`（村长）、`Wanderer`（浪人）作为告示板。其余为直接委托人 |

### 7.3 核心数据结构（当前实际签名）

```csharp
public class CommissionData
{
    [SaveableField] public string DefId;
    [SaveableField] public CommissionCategory Category;
    [SaveableField] public Hero QuestGiver;       // 真正的委托人
    [SaveableField] public Hero BrokerHero;        // 告示板中转人（null = 直接委托）
    [SaveableField] public bool IsNarrativePhase;  // 是否还在"见委托人听故事"阶段
    [SaveableField] public Hero TargetHero;
    [SaveableField] public string TargetSettlementId;
    [SaveableField] public string TargetItemId;
    [SaveableField] public int TargetItemCount;
    [SaveableField] public int NegotiatedReward;
    [SaveableField] public int DepositAmount;
    [SaveableField] public bool DepositRepaid;
    [SaveableField] public float TimeRemainingHours;
    [SaveableField] public int CurrentPhase;
    [SaveableField] public int PhaseProgress;
    [SaveableField] public ResolutionPath ChosenPath;
    [SaveableField] public CommissionTier Tier;
}
```

### 7.4 双路径流程

```
玩家遇到 NPC（头顶 !）
  │
  ├── NPC 是告示板？（Tavernkeeper / Headman / Wanderer）
  │     │
  │     ├── 是 → 展示告示板，列出周边所有委托人的需求
  │     │        每条显示：委托类型 + 真正委托人 + 委托人所在位置
  │     │        玩家选中 → IsNarrativePhase=true → 不转定金，不启动
  │     │        日志："去 XX 找 张三 当面了解详情"
  │     │        玩家找到张三 → ConfirmCommissionIntent → 叙事对话
  │     │        → 玩家点"接下委托" → ConfirmQuest() → 定金到账 → 正式启动
  │     │        → 点"婉拒" → CompleteQuestWithFail()
  │     │
  │     └── 否 → NPC 就是委托人本人
  │             展示他自己的委托列表
  │             玩家选中 → 直接确认 → 定金到账 → 正式启动
  │
  └── 正式启动后，两种路径完全相同
        → 时间递减 → 变故/旅途 → 事件触发 → 完成/超时/失败 → 结算
```
}

public class CommissionData
{
    [SaveableField] public string DefId;
    [SaveableField] public Hero QuestGiver;
    [SaveableField] public Hero TargetHero;
    [SaveableField] public string TargetSettlementId;
    [SaveableField] public int NegotiatedReward;
    [SaveableField] public int DepositAmount;
    [SaveableField] public bool DepositRepaid;
    [SaveableField] public ResolutionPath ChosenPath;
    [SaveableField] public int ActiveComplicationFlags;  // bitmask
    [SaveableField] public float TimeRemainingDays;
}
```

---

## 八、分阶段落地

### 阶段 1（4 个核心委托，验证循环）
1. 悬赏缉拿 — 验证追踪机制
2. 护卫商队 — 验证路线选择+旅途事件
3. 紧急供货 — 验证限时+市场经济
4. 地下拳赛 — 验证特殊战斗规则

### 阶段 2（5 个委托，扩展玩法维度）
5-9：猎杀巨兽、村防应援、失物追寻、越狱营救、物资截获

### 阶段 3（全部 16 类 + 完整 Trust/Infamy 系统）
10-16 + 专属任务线 + 旅途事件表完善

---

## 九、关键设计决策（2024-06-13 更新）

| 决策点 | 确认方案 | 当前实现 |
|--------|----------|---------|
| 委托获取 | NPC 对话菜单 (Intent) + 原生 ! 感叹号 + 告示板聚合 | ✅ 双路径：直接委托人 / 告示板（Tavernkeeper/Headman/Wanderer）|
| 告示板 | —（新增） | 酒馆老板/村长/浪人聚合周边 NPC 的委托，玩家选中后需先见真正委托人听故事再确认 |
| 叙事阶段 | —（新增） | `IsNarrativePhase` → `ConfirmCommissionIntent` → 委托人当面讲前因后果 → 玩家决定接/不接 |
| 委托发起人 | **必须有 HeroId 的持久 NPC**（要人/头人/领主/浪人），禁止模板 NPC | ✅ `Hero.AllAliveHeroes` + `IsAlive` 检查 |
| 委托目标 | 指名类必须有 HeroId；区域/地点/物品类不需要特定人物 | ✅ `CommissionTargetType` 枚举区分 |
| 场所系统 | 6 种场所（酒馆/要人宅/领主大厅/村庄/竞技场/帮派暗巷），酒馆万能入口 | ⚠️ 当前按 Occupation 近似，30%随机无视（模拟万能入口）。精确场景判断未做 |
| LLM 角色 | **仅风味文本增强**（委托描述/变故通知），不参与玩法决策 | ✅ `Settings.Instance.IsLLMReady` 总闸，无 LLM 模板兜底 |
| 委托并行 | 最多 3-5 个同时进行 | ✅ 按 Trust 等级：陌生人 1 → 心腹 4 |
| 多解法 | 战力/潜行/财力/技术/借力——全是游戏操作，不需要打字 | ⚠️ `ResolutionPath` 枚举 + `PickBestPath()` 推荐，但实际 gameplay 分支未实现 |
| 失败惩罚 | 定金追讨→退还/拒还→恶名+结仇 | ✅ `ShowDepositRepaymentInquiry` 三选一：全退 / Charm减半 / 拒还+恶名 |
| 难度分级 | $/$$/$$$/传奇 四级，完成低阶解锁高阶 | ✅ `CommissionTierProgression` 按 tier 计数精确解锁 |
| 质量评级 | ⭐⭐⭐/⭐⭐/⭐/✗ 四级，影响报酬和 Trust | ✅ `ComputeFinalGrade` 三维度（限时/伤亡/活捉）|
| 活捉机制 | 悬赏类活捉报酬 > 击杀，押送阶段有劫囚风险 | ✅ 击杀 ×0.5 惩罚，`TryPrisonerEscapeEvent` 每日 15% 劫囚 |
| 旅途内容 | 模板文本为主，LLM 可选增强，无 LLM 完全可玩 | ✅ `ComplicationTable` + `JourneyEvents`，LLM 异步增强 |
| 玩家沟通 | —（新增） | ✅ `CommissionNarrative`：首次介绍 / Trust升级 / 难度解锁 / 状态面板 |
| 资源查找 | 遵守铁律5：Hero/Item/Settlement 查找必须两轮策略 | ✅ `MBObjectManager.GetObjectTypeList<T>` 动态遍历 |
| Occupation 用法 | —（新增） | ✅ 使用原生枚举：`Tavernkeeper`=酒馆老板, `Headman`=村长, `GangLeader`=帮派头目等 |

---

## 十、实现审计（2026-06-15 更新）

### ✅ 已完成

| 系统 | 状态 | 文件 |
|------|------|------|
| 16/16 委托类型 | ✅ 全部有 `CommissionDef` + `RegisterEvents` + `OnStart` + 结算 | `CommissionData.cs` |
| 双路径接取 | ✅ 直接委托人 + 告示板（Tavernkeeper/Headman/Wanderer） | `CommissionIntent.cs`, `CommissionGenerator.cs` |
| 叙事阶段 | ✅ `IsNarrativePhase` → `ConfirmCommissionIntent` → 当面确认 | `CommissionQuest.cs`, `CommissionIntent.cs` |
| ! 感叹号 | ✅ `CommissionHubIssue` + 原生 `OnCheckForIssue` + `SettlementEntered` 强制激活 | `CommissionHubIssue.cs` |
| 告示板→信格式 | ✅ `ShowCommissionLetter` 逐封浏览 | `CommissionIntent.cs` |
| 直接委托人对话叙事 | ✅ NPC 在对话里自己说，不弹窗 | `CommissionIntent.cs` |
| 结账分离 | ✅ `RewardPayer` 字段 + `IsObjectivesComplete` + `CollectCommissionRewardIntent` | `CommissionData.cs`, `CommissionQuest.cs`, `CommissionIntent.cs` |
| NPC 第一人称叙事 | ✅ `BuildOpening` / `BuildClosure` + CSV 驱动，覆盖全部 16 类 | `CommissionNarrative.cs`, `ModuleData/DesignData/CommissionNarrative.csv` |
| 距离加权目标选取 | ✅ `FillTargetSettlement` / `FillTargetHero` 按 `distance * (0.5 + Random * 1.5)` 排序，60% 就近 | `CommissionGenerator.cs` |
| 村防贿赂 | ✅ 大地图遭遇→Inquiry 弹窗→Charm 砍价→贿赂/战斗 | `CommissionQuest.cs` |
| 完成质量评级 | ✅ `ComputeFinalGrade` ⭐⭐⭐/⭐⭐/⭐/✗ | `CommissionQuest.cs` |
| 活捉机制 | ✅ 击杀 ×0.5，`TryPrisonerEscapeEvent` 每日 15% 劫囚 | `CommissionQuest.cs` |
| 动态变故+旅途事件 | ✅ `ComplicationTable` (每日 15%) + `JourneyEvents` (每日 25%) | `ComplicationTable.cs`, `JourneyEvents.cs` |
| Trust 四级成长 | ✅ 陌生人/熟人/信赖/心腹 → 影响定金比例 + 并发数 | `TrustSystem.cs` |
| 难度递进 | ✅ `CommissionTierProgression` Basic→Skilled→Expert→Legendary | `CommissionData.cs` |
| 恶名系统 | ✅ 拒还定金 +1，Expert+ 完成 -1 | `InfamySystem.cs` |
| 定金追讨 | ✅ `ShowDepositRepaymentInquiry` 退还/Charm减半/拒还 | `CommissionQuest.cs` |
| 日志 | ✅ 全链路 ~22 条（启动/确认/完成/超时/失败/每日/胜利/结算/部队生成/变故） | `CommissionQuest.cs` |
| NpcSight 刷屏日志 | ✅ 已删 | `NpcSightSystem.cs`, `InteractionMissionView.cs` |
| 资源查找 | ✅ 遵守铁律 5：两轮策略 | 全局 |

### ⚠️ 部分完成

| 系统 | 现状 | 差距 |
|------|------|------|
| CSV 叙事模板 | 56 行 / ~112 行目标（50%），全部 16 类覆盖但多数只有一个 opening 变体 | 多数类别缺少 personality 变体，closure 极少有 ≥3 评级覆盖 |
| 村防贿赂→3D 对话 | 当前：Inquiry 弹窗 | 设计目标：`OpenConversationMission` → 3D 场景 → ForceTalkAction |
| 场所系统 | 按 Occupation 近似 + 30% 随机无视 | 精确 GameMenu/Location 判断未做 |

### ❌ 已知未实现（留待后续）

- 委托进行中切换解法路径
- 精确场所判断（按 GameMenu/Location 而非 Occupation）
- 主动搭话（NPC 走向玩家 / 大地图求助者）
- 心腹专属任务线、押注机制、传奇悬赏唯一性
- 路线选择 / 载重取舍 / 昼夜潜行等深度 gameplay 分支

### 文件清单（10个，0编译错误）

`CommissionData.cs` / `CommissionQuest.cs` / `CommissionHubIssue.cs` / `CommissionGenerator.cs` / `CommissionIntent.cs` / `CommissionNarrative.cs` / `ComplicationTable.cs` / `JourneyEvents.cs` / `TrustSystem.cs` / `InfamySystem.cs`
