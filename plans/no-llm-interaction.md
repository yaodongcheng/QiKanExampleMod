# 无 LLM 可玩化 + 意图引擎 —— 计划与实现状态

> 本文是 NPC 互动「无 LLM 版」改造的**权威说明 + 实现进度**，供后续 session 参考。
> （会话期的临时计划文件在 harness 目录 `~/.claude/plans/`，不入库；本文才是仓库内的正式记录。）
>
> 状态图例：✅ 已实现并编译通过 ｜ ⏳ 已留口子未做 ｜ ❌ 不做

最后更新：2026-06-11，编译状态 **0 错误**，产物 `bin/Win64_Shipping_Client/LivingWorldNpcs.dll`。

---

## 1. 目标与背景

让玩家**不配 LLM** 也能跟 NPC 求婚/招募/策反/要军资/仕官/送礼/聊天——每个选项「能不能选、成不成」都由纯 C# 规则算，不问大模型。架构上保证「选项很多也优雅」：加一个新选项 = 写一个小类 + 注册一行。

关键事实：mod 早已写好整套纯 C# 的「数值裁判层」（难度/性格抗性/技能胜率），无 LLM 版**不是重写，是把现成零件串起来**。

**两条玩法决策（已定并落地）**
- 单次检定**有随机性**，但**失败掉好感 + 进冷却**（逼玩家先提高胜率再来，而非读档刷）。
- 菜单**混合过滤**：彻底不可能的隐藏；条件不够但原则可行的**置灰+写明原因**。

---

## 2. 架构主干（核心资产，加玩法优先挂这里）

「意图 = 一个类 + 注册表」，与 Story 命令引擎、AgentBrain 并列的第三套可扩展引擎。
通用机制（成功率公式、掷骰、冷却、台词、置灰）在共享引擎，**每个意图类只声明三件事：资格 / 目标 / 成败后果**。

目录 `ExampleModVS/ExampleMod/ExampleMod/Interaction/Intents/`：

| 文件 | 职责 | 状态 |
|---|---|---|
| `IntentContext.cs` | 上下文：开对话时一次性算好身份/关系（IsHero/Relation/SameFaction/IsLiege/IsWanderer/OppositeSex/IsMySoldier/IsEnemyAgent…），含 `OnCooldown/CooldownDaysLeft` | ✅ |
| `IntentBase.cs` | 意图基类：`Type/Category/DisplayName/Goal/Tactic/DialogueKey/FailRelationPenalty/CooldownDays/GetOfferValue` + `Evaluate/OnInstant/OnSuccess/OnFail`（OnFail 基类默认掉好感+冷却） | ✅ |
| `IntentRegistry.cs` | 注册表 `Register/EnsureInitialized/RegisterDefaults/GetVisible` + **SingleRollResolver**（单次检定公式 `Compute` / 掷骰 `Roll`） | ✅ |
| `IntentCooldownStore.cs` | per-(NPC,目标) 冷却，运行时 `Dictionary<string,double>`（到期游戏天数），`Serialize/Deserialize` 供存档 | ✅ |
| `DialogueTemplateHelper.cs` | 查 CSV 台词，占位符替换，空表兜底 | ✅ |
| `SocialIntents.cs` | 求婚/送礼/茶席/切磋 | ✅ |
| `DiplomacyIntents.cs` | 登庸/劝诱倒戈/策反/要军资/仕官 | ✅ |
| `GeneralIntents.cs` | 情报/命令士兵/跟随/寒暄/离开 | ✅ |
| `RecruitSoldierIntent.cs` | **普通平民应募入伍**（花钱招文化基础兵 + 魅力砍价） | ✅ |

**加新意图的姿势**：新建 `XxxIntent : IntentBase`，在 `IntentRegistry.RegisterDefaults()` 加 `Register(new XxxIntent());`。内容包也能 `IntentRegistry.Register(...)` 注册自己的意图。

---

## 3. 单次检定公式（SingleRollResolver.Compute）

```
状态 = new NegotiationState(对方, 目标, 描述)          // 只读 阈值/开局优势/性格Trait，不跑回合
技能胜率 = SkillCheckSystem.CalculateSkillCheck(...).WinChance   // 0~1
献礼占比 = 出价 / 难度阈值                                // 0~1
性格倍率 = NegotiationRegistry.CalculateMultiplier(虚拟卡, 状态) // 0.1~5（虚拟卡 CostAmount=0 → 不建Chip，避坑P1）
覆盖比例 = (0.30×技能胜率 + 0.70×献礼占比) × 性格倍率      // 权重 SkillWeight/OfferWeight 可调
最终进度 = 开局优势 + 覆盖比例 × 阈值
成功率   = clamp(最终进度/阈值, 0.02, 0.95)               // 点击前显示在选项上
成败     = MBRandom.RandomFloat < 成功率
```
归一化关键（P5）：阈值可达上万、技能点才几百，**全换成「占阈值百分比」**，绝不直接相加。效果：纯嘴炮求婚大贵族近乎不可能，得真送钱送地。
**可调参数**：`SingleRollResolver.SkillWeight=0.30 / OfferWeight=0.70`；各意图 `FailRelationPenalty / CooldownDays / Tactic`。

---

## 4. 逐项实现状态

### 4.1 两处必要重构（地基）
| 项 | 状态 | 落点 |
|---|---|---|
| 抽 `SkillCheckSystem.MapTacticToSkill`，`SkillCheckOption.CalculateChance` 复用，消除两套 switch | ✅ | `Negotiation/NegotiationSystem.cs`（SkillCheckSystem 顶部 + CalculateChance） |
| 把被旁路的 `CalculateMultiplier` 接回结算（`性格倍率 × (有LLM?clamp(delta,0.5,2):1)`） | ✅ | `Interaction/InteractionController.cs` `ProcessNegotiationResponse` |
| P9：未动 setter、未碰 LLM 开场路径（新 resolver 拿着 NPC 现算，不经 setter） | ✅ | —— |

### 4.2 意图与结算
| 意图 | 类 | Goal | 成功后果 API | 状态 |
|---|---|---|---|---|
| 求婚 | ProposeMarriageIntent | ProposeMarriage | `MarriageAction.Apply` | ✅ |
| 登庸（招浪人） | RecruitWandererIntent | RecruitHero | `AddCompanionAction.Apply` | ✅ |
| 劝诱敌将倒戈 | DefectEnemyIntent | DefectFaction | `ChangeKingdomAction.ApplyByJoinToKingdomByDefection` | ✅ |
| 策反同阵营 | BetrayalIntent | DefectFaction | `ChangeKingdomAction.ApplyByLeaveKingdom` | ✅ |
| 请求军资 | RequestFundsIntent | Exaction | `AgentControlHelper.TransferGold(对方→玩家)` | ✅ |
| 仕官 | RequestWorkIntent | JoinInFaction | `ChangeKingdomAction.ApplyByJoinToKingdom` | ✅ |
| 送礼（即时） | GiftIntent | — | 物品菜单 + `物价/100×喜好` 好感 + 转物品 | ✅ |
| 茶席（即时） | TeaCeremonyIntent | — | 按性格 +好感（稳重+3/重情+2/酒鬼-1） | ✅ |
| 切磋（即时） | SparIntent | — | `order_attack` 事件 | ✅ |
| 情报（即时） | InfoIntent | — | 百科 | ✅ |
| 命令士兵（即时） | OrderSoldierIntent | — | 无LLM：模板台词；有LLM：SendIntent | ✅ |
| 跟随（即时） | FollowIntent | — | brain.SetLeader + order_follow | ✅ |
| 寒暄（即时） | ChatIntent | — | 有LLM自由输入；无LLM话题菜单 | ✅ |
| 离开（即时） | LeaveIntent | — | EndInteraction 广播 + 关闭 | ✅ |

> **普通模板 NPC 的核心价值 = 当兵**（RecruitSoldierIntent）：点村民/镇民 → 招其文化 `BasicTroop` 入队。
> 价格用骑砍2 原版 `Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(troop, MainHero)`。
> **「特殊」机制 = 魅力砍价**：一次魅力检定（成功率 `0.30+charm*0.003`），成功打折（`折扣=0.25+charm/400`，最高 75%，甚至免费），失败原价；钱不够则拒招。
> 资格 = `IntentContext.IsRecruitableCivilian`（非 Hero、非士兵、非敌对、其文化有 BasicTroop）。
> **性别处理（已决策）**：忽略村民性别，一律招 `culture.BasicTroop`（默认男兵）。骑砍2 兵=兵种模板的计数、性别写死在模板上、原版无女兵线，故同一兵种不能男女混编；这与原版从 notable 募兵不分性别一致，零内容成本。若日后要女兵：加 `Settings.AllowFemaleSoldiers` + 用铁律5 predicate 动态查该文化女兵模板、找不到回退基础兵（本轮未做）。

### 4.3 资格层（混合过滤三态）
| 项 | 状态 | 落点 |
|---|---|---|
| `Eligibility` 三态（Hidden/Disabled+原因/Enabled） | ✅ | `IntentContext.cs` |
| 各意图 `Evaluate` 资格规则（非Hero/关系/阵营/冷却） | ✅ | 各意图类 |
| `StoryOptionVM` 加 `IsEnabled/DisableReason`，点禁用项弹原因不执行 | ✅ | `Interaction/StoryDialogVM.cs` |
| 置灰视觉：当前用「🔒」前缀 + 点击弹原因 | ✅ | `InteractionOptionManager.cs` |
| Gauntlet XML 真·灰色样式绑定 | ⏳ | 延后（不易验证） |

### 4.4 入口与接线
| 项 | 状态 | 落点 |
|---|---|---|
| `InteractionOptionManager` 退化为薄壳 `BuildOptionVMs`（含成功率预览/置灰） | ✅ | `InteractionOptionManager.cs` |
| `DispatchIntent`（即时→OnInstant；对抗→有LLM谈判盘/无LLM单次检定） | ✅ | `InteractionController.cs` |
| `ResolveAdversarialIntent`（主线程同步掷骰+结算+台词+收尾选项，避坑P2） | ✅ | `InteractionController.cs` |
| `StartLLMNegotiation`（有LLM时用已知目标直接开谈判） | ✅ | `InteractionController.cs` |
| `OpenGiftMenu / OpenChatTopicMenu / OpenFreeChatInput / ShowNpcLineKeepMenu` | ✅ | `InteractionController.cs` |
| G 键门禁放开（无LLM也进菜单） | ✅ | `InteractionMissionView.cs` |
| P8：开场空数组回退本地意图菜单 | ✅ | `InteractionController.cs` `ProcessOpeningResponse` |

### 4.5 冷却存档
| 项 | 状态 | 落点 |
|---|---|---|
| `IntentCooldownStore` 运行时字典 + JSON 序列化 | ✅ | `Intents/IntentCooldownStore.cs` |
| `MyBehavior.SyncData` 以 JSON 串持久化（记忆系统不入档，故走这里） | ✅ | `Core/MyBehavior.cs` |

### 4.6 台词 CSV（内容包可注入）
| 项 | 状态 | 落点 |
|---|---|---|
| `GameDatabase.Dialogue` 表注册（Initialize 加载默认 + LoadTablesFromPath 守卫覆盖） | ✅ | `Data/DesignDataLoad.cs` |
| `DialogueTemplateHelper`（ID=`{Goal}_{Success/Fail}`/`Chat_xxx`/`Order`，占位符 {PLAYER}{NPC}{WORLD}{TERM_LORD}，空表兜底） | ✅ | `Intents/DialogueTemplateHelper.cs` |
| Mod A 默认卡拉迪亚台词 | ✅ | `ModuleData/DesignData/Dialogue.csv` |
| TaikouContent 战国版 Dialogue.csv | ⏳ | 内容包侧未做（放一份同名 CSV 即覆盖，无需改 Mod A） |
| **CSV 限制**：逗号分隔列、`|` 分隔多条台词、台词内禁英文逗号（P12） | ✅ | —— |

---

## 5. 已留口子 / 未做（后续可补）

- ⏳ **汇报/请辞/结交/拉拢/拜师** 等意图：原本多是 `DisplayMessage` 占位，本轮未做成意图类。补它们 = 各写一个 `XxxIntent` + 注册一行（这正是本架构的意义）。
- ⏳ **置灰的 Gauntlet XML 灰色样式**：现用「🔒」前缀+点击弹原因替代。
- ⏳ **TaikouContent 注入战国台词 CSV**。
- ❌ **打死模板兵 → 俘虏**：考虑过（俘其本兵种 / 平民俘文化基础兵，加入 PrisonRoster），**本轮决定不做**。
- ❌ **女兵线**：当前忽略性别统一基础兵（见 RecruitSoldierIntent 性别决策）；要做需 `Settings.AllowFemaleSoldiers` + 动态查女兵模板。
- ❌（本轮明确不做，留待后续重构）：三套 LLM Response schema 抽公共父类；`NegotiationCard`/`SkillCheckOption` 两个选项卡类合并；`ActionHandler` 字符串动作码与意图 `OnSuccess` 统一。

---

## 6. 关键坑位（改这块时务必对照）

- **P1** 单次检定**不能 new Chip**（Chip 构造有 `DisplayMessage` 副作用），用 `float offerValue`。
- **P2** 结算走**主线程同步**，不塞 `Task.Run`（要触发结婚/改阵营/刷 UI）。
- **P3** 对抗结算前判 `Target!=null && Profile!=null`（守卫小兵无人设）。
- **P5** 公式归一化（见 §3），技能点绝不直接加进阈值。
- **P8** 无 LLM 开场是空数组（非 null），需回退本地菜单。
- **P11** 台词表空时走兜底句。**P12** 台词禁英文逗号。

---

## 7. 验证清单（需在游戏内做，代码侧已编译通过）

1. 清空 config.json 的 LLM 三项 → 按 G → 菜单按身份/关系正确显示（小兵无求婚、敌国君主无仕官、关系太低的策反置灰带原因）。
2. 5 个对抗意图各点一次 → 看到成功率 → 成功真结婚/入伙/改阵营/到账；**失败掉好感+进冷却**，再点变置灰「过几天再来」。
3. 送礼/茶席按公式涨好感；寒暄出话题菜单；全程不卡三秒、不弹「走神」、不崩；对小兵聊也不崩。
4. 失败进冷却 → 存档读档后冷却仍在。
5. 新建一个意图类 + 注册一行 → 出现在菜单（验证架构优雅）。
6. 配上 LLM → 菜单仍走谈判盘；`ProcessNegotiationResponse` 用「性格×LLM倍率」，进度条行为同改前（回归）。

---

## 8. 待办：wheels.md 登记

这套「意图注册引擎」应作为第三套可扩展引擎登记进 [rules/wheels.md](rules/wheels.md)（与 Story 命令引擎、AgentBrain 并列）。**尚未登记**，下个 session 或本 session 结尾补。
