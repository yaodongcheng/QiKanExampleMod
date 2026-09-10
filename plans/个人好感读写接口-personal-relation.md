# 全局 patch 个人好感（绕开族长归纳）（计划）

> **一句话**：**不改存储、不改 LWN 那 48/37 处调用**——在官方唯一的两个「换算关卡」上打 Harmony 补丁，让全游戏（原版 + LWN + 其他 mod）读到的都是 hero↔hero 的原始好感，不再自动归到族长头上。
> **状态**：**待审批**（施工前需批准）。
> **口径**：所有数量、行号、符号存在性、算法细节均为 grep / 反编译实测，不含估算。

> **✅ 三项裁定（2026-09-10 用户拍板）**
> 1. **生效范围 = 全局全改**：所有关系读写（含 NPC↔NPC、原版 AI 之间）一律按个人算，补丁内不加"是否涉及玩家"的白名单。
> 2. **保留特质修正**：`Hero.GetRelation` 继续叠加 Honor / Valor / Mercy 三项特质修正（算法按官方复刻，见 § 三）。
> 3. **派发官方关系事件**：写关卡补丁照常派发 `OnHeroRelationChanged`（传实际那一对英雄）→ 飘字 / LWN `[Sense] 关系动态` / IM 广播照常，只是名字从家族变成当事人。

---

## 一、要做的两件事

1. **补丁 1（读关卡）**：`Hero.GetRelation(Hero)` → 原始值 + 特质修正（不再换算到族长）。
2. **补丁 2（写关卡）**：`ChangeRelationAction.ApplyInternal(Hero, Hero, int, bool, ChangeRelationDetail)` → 把这一对英雄原样写入原始表，并派发关系事件。

**LWN 侧代码 0 处改动**：现有 48 处写入（`ChangeRelationAction.*`）与 37 处读取（`.GetRelation(`）走的正是这两个关卡，补丁生效后语义自动变成个人账。附录 A/B 保留作**抽查验证**用。

---

## 二、为什么是这两个关卡（实测依据）

| 结论 | 证据 |
|---|---|
| 官方**只有两处**调用换算函数 | 1.2.12 反编译：`GetHeroesForEffectiveRelation` 在全 `TaleWorlds.CampaignSystem.dll` 只有 2 个调用方——`DefaultDiplomacyModel.GetEffectiveRelation`（读路径）与 `ChangeRelationAction.ApplyInternal`（写路径） |
| 读路径唯一入口 | `Hero.GetRelation(other)` → `DiplomacyModel.GetEffectiveRelation`；`Hero.GetRelationWithPlayer()` 内部就是 `MainHero.GetRelation(this)`，一并覆盖 |
| 写路径唯一入口 | 三个公开写 API（`ApplyPlayerRelation` / `ApplyRelationChangeBetweenHeroes` / `ApplyEmissaryRelation`）**全部**转调 `ApplyInternal` |
| 补丁目标两版本都在 | 二进制核实 1.2.12 与 1.5.2 的 `TaleWorlds.CampaignSystem.dll`：`GetRelation` / `ApplyInternal` / `ApplyPlayerRelation` / `ApplyRelationChangeBetweenHeroes` / `GetEffectiveRelation` / `GetHeroesForEffectiveRelation` / `SetPersonalRelation` / `GetRelationWithPlayer` / `GetBaseHeroRelation` / `ChangeRelationDetail` **全部存在** |
| 存储不用动 | `CharacterRelationManager.HeroRelations._relations`（hero↔hero，`[SaveableField(1)]` 已在存档里）；`GetHashCodes` 按 `hero1.Id > hero2.Id` 归一 = **每对一个数、天生对称**（本方案下"有向好感"不成立） |

**本来就按个人算、不受影响也不需补丁的路径**

- `CharacterRelationManager.GetHeroRelation/SetHeroRelation`（底层原件）
- `Hero.IsFriend/IsEnemy`、`Hero.GetBaseHeroRelation`、`DiplomacyModel.GetBaseRelation`
- 实测：**百科全书的敌友判定与排序**（`TaleWorlds.CampaignSystem.ViewModelCollection` 反编译 54482 / 54487 / 54551）直接调 `CharacterRelationManager.GetHeroRelation` → 一直是原始值。

**原版关系表面实测**（1.2.12）

- `TaleWorlds.CampaignSystem.dll`：**57 处**关系读取调用点 —— 外交与决策投票、AI 背叛动机、任务与委托条件、买卖估价、家族关系、对话条件等。
- `TaleWorlds.CampaignSystem.ViewModelCollection.dll`（百科 / 英雄面板 / 招募 / 队伍面板）：一类走 `GetRelationWithPlayer()`（= 被补丁覆盖），一类直接读 manager（本就个人值）。
- `Clan.GetRelationWithClan(x)` 实现为 `Leader.GetRelation(x.Leader)` → 两边都是族长，原始对 = 换算对 → **家族关系这个维度不受补丁影响**（保持族长对族长）。

---

## 三、补丁规格

**文件**：`ExampleModVS/ExampleMod/ExampleMod/Interaction/PersonalRelationPatches.cs`（新文件；按项目纪律登记进 `.csproj` 显式编译清单）。

### 补丁 1 —— 读关卡

- **目标**：`Hero.GetRelation(Hero otherHero)`（非虚实例方法）。
- **实现**（= 官方 `DefaultDiplomacyModel.GetEffectiveRelation` 去掉换算那一步，其余照抄）：
  1. `otherHero == null || this == otherHero` → 0。
  2. `int rel = CharacterRelationManager.GetHeroRelation(this, otherHero);`
  3. 叠加特质修正（官方算法，复刻）：
     - Honor 权重 2、Valor 权重 1、Mercy 权重 1；
     - 每项：`t1 = 己方该特质等级`，`t2 = 对方该特质等级`；`t1 * t2 > 0` → `rel += 权重`；`t1 * t2 < 0` → `rel -= 权重`；相等或为零 → 不变（最大 ±4）。
  4. `return MBMath.ClampInt(rel, DiplomacyModel.MinRelationLimit, Model.MaxRelationLimit);`（取当前模型的上下限，兼容改过上下限的 mod）。
- 🔴 **热路径纪律**：O(1)、**无日志、无分配**（AI 排序里高频调用）。

### 补丁 2 —— 写关卡

- **目标**：`ChangeRelationAction.ApplyInternal`（private static，签名 `(Hero originalHero, Hero originalGainedRelationWith, int relationChange, bool showQuickNotification, ChangeRelationDetail detail)`）。
- **实现**：
  1. `originalHero == null || originalGainedRelationWith == null || originalHero == originalGainedRelationWith` → 直接 return（官方对自对写入是 `Debug.FailedAssert`；我们代码里出过自对写入 bug，见 `Planner/ActionRegistry.cs:537` 注释）。
  2. `int cur = CharacterRelationManager.GetHeroRelation(originalHero, originalGainedRelationWith);`
  3. 钳制后 `originalHero.SetPersonalRelation(originalGainedRelationWith, cur + relationChange)`（`SetPersonalRelation` 自带上下限钳制）。
  4. **派发事件**：`CampaignEventDispatcher.Instance.OnHeroRelationChanged(originalHero, originalGainedRelationWith, relationChange, showQuickNotification, detail, originalHero, originalGainedRelationWith);`
     —— 前两参（effective）与后两参（original）都传**实际那一对**，于是 `Core/MyBehavior.cs:332` 的 `[Sense] 关系动态`、IM 广播、以及任何监听者看到的都是当事人本人。

### 新增（给新代码用的统一入口）

`PersonalRelationHelper`：`Add / Set / Get / GetAllFor / event OnChanged / [PRel] 日志`——补丁内部复用它的核心逻辑，LWN 以后写关系只认这一个入口。**老代码不改。**

**纪律**：玩家可见文本走 `LWNTextHelper`（铁律 13）；玩家侧与 NPC 侧共用同一套（铁律 18）；不新建存档键、不改 `ResetAllCampaignState`、老档不需要迁移。

---

## 四、已知代价（裁定的必然结果，非建议）

1. **原版平衡会变（全改的必然）**：家族级 → 个人级。得罪一个领主不再连坐全家；招降、外交、决策投票、AI 背叛动机的读数全部变成"这个人对你"。
2. **NPC↔NPC 也一并变**：原版 AI 之间的关系（`ApplyRelationChangeBetweenHeroes`）补丁后记到具体两人头上，不再聚到族长。
3. **老档的旧账留在族长身上**：老档里已聚合在族长对上的数值**不会自动拆到个人**；补丁生效后新的变化才记个人。
4. **原版里"故意按族长算"的写入会改记到当事人**：例如 `ApplyRelationChangeBetweenHeroes(settlement.OwnerClan.Leader, besiegerParty.LeaderHero, -5)` 原来记围城方族长的账，补丁后记这个领主本人。
5. **会绕过第三方 DiplomacyModel 的自定义**：若某 mod 替换了 `DiplomacyModel` 并自定义 `GetEffectiveRelation`（关系修正类 mod），补丁在调用侧直接返回，等于屏蔽它的修正——这是"以我为准"的必然。
6. **一条实测备注**：飘字文案 `{CLAN_LEADER}` / `{HERO.NAME}` 两套 id（`alEG3BOa` / `TxeKpK3Y` / `YbB2IpTR` / `YevUiidU`）在 1.2.12 与 1.5.2 的**所有 DLL 里都搜不到引用**（语言包里有、代码里没引用）——玩家日志里那条「你与吴三桂部的关系-10」很可能是第三方 mod 发的，LWN 的 `[AddQuickInformation]` 只是记录了别人的调用。补丁后由监听者自己决定用哪套文案。

---

## 五、验收清单

1. **读关卡生效**：对一个**族内非族长**英雄写入后，`Hero.GetRelation` 返回个人值（改前返回族长的值）；百科全书英雄面板同步显示该个人值。
2. **不变式**：对**族长本人**写入，`GetRelation` 读数与补丁前一致（原对 = 换算对）。
3. **特质修正保留**：构造一对"双方 Honor 都为正"的英雄，确认读数比纯原始值高 2（其余两项为零时）。
4. **家族维度未被破坏**：取两个不同家族的英雄，`Clan.GetRelationWithClan` 仍返回族长对族长的值。
5. **写路径全覆盖**：三个公开写入口各测一次都落到个人账；**NPC↔NPC 写入也落到两人头上**（不再进族长账）。
6. **事件派发**：飘字 / `[Sense] 关系动态` / IM 广播照常出现，且 `[Sense]` 行里的人名是**当事人**（不再出现家族名）。
7. **存档**：写 → 存档 → 读档，数值不变（同表自带持久化）。
8. **性能**：进一场大会战 + 打开百科，`[Perf]` 面板确认无新增卡顿。
9. **回归**：铁律 12 的代价仍在（罪行走人照样掉关系）；贿赂 / 招降 / 谈判 / 接委托手测一遍。
10. **两版本各编译验证一次**（1.2.12 与 1.5.x）；补丁目标按项目纪律用二进制 grep 核对签名。

---

## 附录 A：LWN 现有关系写入调用点（48 处实测，本方案下**不用改**，仅作抽查）

| 文件 | 行号 | 调用 |
|---|---|---|
| `AI/Actions/AtomicAction.cs` | 262 / 298 / 314 | `ApplyPlayerRelation(victimHeroObj, -3 / -5 / -8)` |
| `Core/AgentControlHelper.cs` | 1322 | `ApplyRelationChangeBetweenHeroes(targetHero, targetHeroSpouse, -30)` |
| `Core/KingdomPoliticsFlow.cs` | 110 | `ApplyPlayerRelation(lord, -10, true, true)` |
| `WorldEvent/WorldEventSimulator.cs` | 598 / 599 | `ApplyPlayerRelation(evt.InstigatorHero, -10)` / `(targetHero, -10)` |
| `Interaction/InteractionController.cs` | 383 / 457 | `ApplyPlayerRelation(ctx.Speaker / target, delta)` |
| `Interaction/Dialogue/CrimeDialogueBuilder.cs` | 854 | `ApplyPlayerRelation(authority, -10)`（随从包庇拒认） |
| `Interaction/Intents/AccountabilityIntents.cs` | 233 / 259 / 381 / 562 / 592 / 804 / 819 / 1028 / 1070 / 1436 / 1504 / 1551 / 1562 | 罪行走人 -10/-20、索赔 -3/-5/-15、改口 +5 等 |
| `Interaction/Intents/CombatSurrenderIntents.cs` | 218 / 227 | `ApplyPlayerRelation(ctx.Speaker, +2 / -10)` |
| `Interaction/Intents/IntentBase.cs` | 104 | `ApplyPlayerRelation(ctx.Speaker, -FailRelationPenalty)` |
| `Interaction/Intents/SocialIntents.cs` | 90 | `ApplyPlayerRelation(ctx.Speaker, delta)` |
| `Interaction/Intents/SystemIntents.cs` | 39 / 63 | `ApplyPlayerRelation(ctx.Speaker, ±amount)` |
| `Planner/ActionRegistry.cs` | 542 / 547 / 567 / 569 / 588 / 589 / 606 / 607 | `ApplyRelationChangeBetweenHeroes(a, d, delta, …)`（relation_up / relation_down） |
| `Quests/QuestManager.cs` | 712 / 793 / 873 | `ApplyPlayerRelation(QuestGiver, +5 / penalty / -10)` |
| `Quests/Commissions/CommissionQuest.cs` | 371 / 438 / 450 / 2178 / 2195 / 2205 / 2300 | `ApplyPlayerRelation(QuestGiver, +5 / penalty / -20 / -5 / -8 / -15 / +5)` |
| `Scenario/ScenarioStores.cs` | 472 | `ApplyPlayerRelation(hero, targetRelation, true, true)`（剧本 DSL 写关系） |

> 备注：`ApplyPlayerRelation` 官方签名第三参 `affectRelatives` 在 1.2.12 的 `ApplyInternal` 里**没有被使用**（死参），不要指望它。

## 附录 B：LWN 现有关系读取调用点（37 处实测，本方案下**不用改**，仅作抽查）

| 文件 | 行号 | 读法 |
|---|---|---|
| `Core/FriendlinessHelper.cs` | 96 / 97 / 157 | `a.GetRelation(b)` 双向 / `hero.GetRelation(MainHero)` |
| `Core/KingdomPoliticsFlow.cs` | 54 / 55 / 91 / 188 | `king.GetRelation(lord)` / `lord.GetRelation(MainHero)` |
| `Core/MyBehavior.cs` | 359 | 跨档位判定 `a.GetRelation(b)` |
| `WorldEvent/WorldEventSimulator.cs` | 739 / 743 / 815 / 816 / 852 | 背叛动机：`instigator.GetRelation(h)`（NPC↔NPC） |
| `WorldEvent/InvestigationEngine.cs` | 51 | `authority.GetRelation(lead)` |
| `WorldEvent/AttitudeSystem.cs` | 149 / 259 | `npc.GetRelation(suspect)` / `speaker.GetRelation(MainHero)` |
| `Negotiation/NegotiationSystem.cs` | 462 / 1617 / 1745 | `BaseHero.GetRelation(MainHero)` / `TargetHero.GetRelation(player)` |
| `LLM/WorldFactProvider.cs` | 1421 / 3101 / 3168 / 3223 | 事实段：`MainHero.GetRelation(hero)`、`a.GetRelation(b)` |
| `LLM/PromptBuilder.cs` | 695 / 1638 | 与主公关系段 / 受害者关系段 |
| `Memory/NPCProfile.cs` | 849 / 1695 / 1707 | 与国王关系 / 友人仇人列表 |
| `ImChat/ImChatManager.cs` | 433 / 467 | `self.GetRelation(other/peer)`（选人加权） |
| `ImChat/ImChatView.cs` | 2224 | `hero.GetRelation(MainHero)`（标题栏显示） |
| `Interaction/NpcInfoVM.cs` | 138 | 面板显示 `_hero.GetRelation(MainHero)` |
| `Interaction/Intents/IntentContext.cs` | 179 | `Relation = hero.GetRelation(MainHero)` |
| `Planner/ActionRegistry.cs` | 1047 / 1069 | `defender.GetRelation(MainHero) >= 0` |
| `Scenario/ScenarioStores.cs` | 405 | 剧本谓词「親密度」 |
| `Scenario/AttributeResolver.cs` | 67 | 剧本条件 `h1.GetRelation(h2) >= want` |

## 附录 C：用到的官方 API（两版本均已二进制核实存在）

- `Hero.GetRelation(Hero)`（补丁 1 目标） / `Hero.SetPersonalRelation(Hero, int)`（按当前 `DiplomacyModel` 上下限钳制） / `Hero.GetBaseHeroRelation(Hero)` / `Hero.GetRelationWithPlayer()`。
- `ChangeRelationAction.ApplyInternal`（补丁 2 目标，private static） / `ApplyPlayerRelation` / `ApplyRelationChangeBetweenHeroes` / `ApplyEmissaryRelation`。
- `CharacterRelationManager.GetHeroRelation(Hero, Hero)`（原始读，补丁内部使用）。
- `CampaignEventDispatcher.Instance.OnHeroRelationChanged(...)`（派发用）。
- `MBMath.ClampInt` / `DiplomacyModel.MinRelationLimit` / `MaxRelationLimit`（钳制用）。
- 现有广播入口：`Core/MyBehavior.cs:332`（`[Sense] 关系动态` + IM 广播）。
- 特质权重（官方 `DefaultDiplomacyModel.GetPersonalityEffects`，复刻依据）：Honor 2 / Valor 1 / Mercy 1。
