# 原版 SceneNotification 过场动画系统 — 完整参考

> **织丰嵌入研究（如何复用织丰的过场场景/事件类/触发）见 [织丰过场嵌入研究.md](织丰过场嵌入研究.md)**。
>
> 引擎机理（v1.2.12 / v1.3.15 / v1.4.x / v1.5.x **全版本同构**，2026-08-30 四版本反编译实测）：
> `MBInformationManager.ShowSceneNotification(SceneNotificationData)` 触发 → 全局 UI 层
> `GauntletSceneNotification`（层序 19000）内部创建**独立渲染的 3D 场景**（非 Mission！）播放。
> ⚠️ 早期文档所述「以 `MissionMode.CutScene` 加载场景」的旧制式已作废——1.2.12 起即为下述 Gauntlet 制式。

## 架构总览（实测）

```
MBInformationManager.ShowSceneNotification(data)      [TaleWorlds.Core.dll，唯一入口]
  → 触发事件 OnShowSceneNotification                  [Core.dll]
    → GauntletSceneNotification.OnShowSceneNotification   [Native GauntletUI.dll]
        data 入队（一次只播一个；LoadingWindow 激活时暂停出队）
      → 队列取头 → 上下文门控 IsGivenContextApplicableToCurrentContext(RelevantContext)
        ├─ 不适用 → 排队等待（如 Map 型在 mission 中需等切回大地图）
        └─ 适用 → CreateSceneNotification(data) → 真正开播
            → 自建 Scene.CreateNewScene + Scene.Read(SceneID)（独立 3D 场景，不走 Mission 加载）
            → PopupSceneCameraPath 相机脚本（场景 tag）播放运镜
            → PopupSceneSpawnPoint（tag "spawnpoint_player_N"）摆放 AgentVisuals 角色
            → Banner 旗帜渲染（tag "banner_N"，BannerVisual 到绘到网格）
            → 1.3.15+：VisualShipFactory 船（tag "spawnpoint_ship_N"）
            → 叠加 UI：标题 / 描述 / 两互动按钮 / 「点击继续」
```

## 核心类型

| 类型 | 位置 | 说明 |
|------|------|------|
| `SceneNotificationData` | `TaleWorlds.Core.dll` | 基类（非抽象）。定义 SceneID / TitleText / RelevantContext / GetSceneNotificationCharacters() 等全部虚属性 |
| `SceneNotificationData.SceneNotificationCharacter` | 同上嵌套 struct | 封装 CharacterObject + Equipment + BodyProperties + 颜色 + 骑马 |
| `SceneNotificationData.SceneNotificationShip` | 同上（1.3.15+） | 船：prefab / 升级件 / 血比 / 帆色 / 种子 |
| `MBInformationManager` | `TaleWorlds.Core.dll` | **唯一请求入口** `ShowSceneNotification`；另有 `HideSceneNotification` / `GetIsAnySceneNotificationActive` / `GetActiveSceneNotificationData` |
| `GauntletSceneNotification : GlobalLayer` | Native `TaleWorlds.MountAndBlade.GauntletUI.dll` | 播放管理器（单例 `Current`，`Initialize()` 时 `ScreenManager.AddGlobalLayer`，层序 19000） |
| `SceneNotificationVM` | `TaleWorlds.Core.ViewModelCollection.dll` | 数据源 VM（文本/按钮/场景/EndProgress 绑定） |
| `SandboxSceneNotificationContextProvider` | `SandBox.GauntletUI.dll` | 沙盒版上下文门控：仅 `Map` 需 `ActiveState is MapState` |
| `CampaignSceneNotificationHelper` | `TaleWorlds.CampaignSystem.dll` | 静态工具类：CreateNotificationCharacterFromHero / GetBodyguardOfCulture / RemoveWeaponsFromEquipment 等 |

## 是否可播放：上下文门控（RelevantContext）

`SceneNotificationData.RelevantContext` 枚举：`Any / MPLobby / CustomBattle / Mission / Map`（**全版本有**）。

| 值 | 行为（Sandbox provider 实测） | 典型用途 |
|---|---|---|
| `Any`（基类默认） | 任何时刻都允许（含 mission 战斗内） | 处决等随时可播的过场（`HeroExecutionSceneNotificationData` 工厂默认 `Any`） |
| `Mission` | 恒 true（provider 不拦）——**mission 内可播**，过场盖在战斗 UI 之上并暂停游戏（`PauseActiveState` 时 `MBCommon.PauseGameEngine`） | 战斗中插播剧情 |
| `Map` | 仅 `GameStateManager.Current.ActiveState is MapState`——mission 内会一直排队等切回大地图 | 大地图剧情（婚礼/建国等多数过场） |
| `MPLobby` / `CustomBattle` | 恒 true | MP / 自定义战斗 |

更多细节：一次只能播一个；播放期间新请求入队；队列项出队前有 2 帧延迟；`LoadingWindow.IsLoadingWindowActive`（读档/场景切换）期间不出队。

## SceneNotificationCharacter 构造

```csharp
new SceneNotificationCharacter(
    BasicCharacterObject character,     // 可为 null（空位）
    Equipment overriddenEquipment,      // 覆盖默认装备
    BodyProperties overriddenBodyProps, // 身体属性（年龄/身高/体重等）
    bool useCivilianEquipment,
    uint customColor1, uint customColor2, // 服装颜色
    bool useHorse                        // 是否骑马
)
```

注：1.2.12 基础参数为 `CharacterObject`（`BasicCharacterObject` 是 1.3.15+ 泛化），mod 以当前版本反编译为准。

## 文本系统

`TitleText` / `AffirmativeText` / `NegativeText` 等全部是 `TextObject` 虚属性——mod 自定义子类直接 override，用 `TextObject("{=LWN_key}...")` 走标准本地化（项目铁律 13 合规）。原生场景用 `GameTexts.FindText("str_xxx")` + `SetVariable` 注入变量；所有时间类场景使用 `CampaignSceneNotificationHelper.GetFormalDayAndSeasonText()` 生成"X年X季第X天"格式。

## 互动按钮（2026-08-30 实测）

| 控件 | 触发方式 | 行为 |
|---|---|---|
| 肯定按钮（Affirmative） | 显示条件 `IsAffirmativeOptionShown` | 调 `data.OnAffirmativeAction()` + `OnPositiveAction()`（相机/角色切「同意」动作帧）——**不关窗**，需再点「点击继续」关闭 |
| 否定按钮（Negative/取消） | `IsNegativeOptionShown` | 关窗 + `data.OnNegativeAction()` |
| 点击继续 / 关闭 | `Command.Close`（点击场景任意处/提示文本） | 关窗 + `data.OnCloseAction()` |
| 自动确认 | 仅 1.5.x 有 `ShouldAutoConfirm` | 开播约 1 秒后自动执行 `OnAffirmativeAction`（「通知型」过场用，如处决信息） |
| 按钮文案 | `AffirmativeText` / `NegativeText` + Description 变体 | TextObject 虚属性，mod 可 override |

注：1.2.12 的队列项额外记录 `PauseActiveState`（tuple），暂停逻辑与 1.5.x 相同但取数位置不同——不需要 mod 关心。

## 版本差异（SceneNotificationData 基类成员，四版本反编译实测）

| 成员 | 1.2.12 | 1.3.15 | 1.4.x | 1.5.x |
|---|---|---|---|---|
| SceneID / TitleText / SoundEventPath / Affirmative+Negative 全部文本与按钮开关 / PauseActiveState / RelevantContext / 三个回调 | ✅ | ✅ | ✅ | ✅ |
| `GetBanners()` / `GetSceneNotificationCharacters()` 返回 | `IEnumerable<T>` | `T[]` | `T[]` | `T[]` |
| `GetShips()` / `SceneProperties`（物理/阴影/水） | ❌ | ✅ | ✅ | ✅ |
| `DescriptionText`（独立正文描述） | ❌ | ❌ | ❌ | ✅ |
| `ShouldAutoConfirm` | ❌ | ❌ | ❌ | ✅ |

---

## 全部 25 个过场场景

> 清单基于 v1.2.12 反编译（角色槽位 / 文本 ID 在 1.5.x 仍有效）。
> ⚠️ 版本补充（1.3.15+）：新增海战死亡过场 `NavalDeathSceneNotificationItem`（1.3.15+ 的 `GetShips()` 生效）。

### 1. 加冕为王 — `scn_become_king_notification`

| 项目 | 内容 |
|------|------|
| **类** | `BecomeKingSceneNotificationItem` |
| **触发** | 玩家自立为王 |
| **总人数** | 1 + 14 观众 + 2 卫士 + 4 同伴 = **21** |
| **文本 ID** | `str_become_king_empire` (帝国) / `str_become_king_nonempire` (非帝国) |
| **文本变量** | `DAY_OF_YEAR`, `YEAR`, `KING_NAME`, `IS_KING_MALE`, `TITLE_NAME` |

**角色槽位（按列表顺序）**：

| 序号 | 角色 | 可替换? |
|------|------|---------|
| 0 | **新国王/女王** — `NewLeaderHero.CharacterObject` + 无武器战斗装备 | ✅ Hero.CharacterObject |
| 1–14 | **观众** — 按 `IsAudienceFemale(i)` 在 `Culture.Townswoman` / `Culture.Townsman` 间切换，随机颜色 | ⚠️ 按文化的 Townsman/Townswoman |
| 15–16 | **卫士** — `GetBodyguardOfCulture(NewLeaderHero.Clan.Kingdom.Culture)` | ✅ CharacterObject ID |
| 17–20 | **同伴** — `GetMilitaryAudienceForHero(NewLeaderHero)` 选 4 人，平民装 | ✅ Hero |

---

### 2. 家族成员和平死亡 — `scn_cutscene_family_member_death`

| 项目 | 内容 |
|------|------|
| **类** | `ClanMemberPeaceDeathSceneNotificationItem` |
| **触发** | 家族成员非战斗死亡 |
| **总人数** | 1 + 5 观众 = **6** |
| **文本 ID** | `str_family_member_death` |
| **文本变量** | `DAY_OF_YEAR`, `YEAR`, `NAME` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **逝者** — `DeadHero` + 平民装无武器 |
| 1–5 | **哀悼者** — `GetMilitaryAudienceForHero(DeadHero)`，亲属/族人按等级排序 |

---

### 3. 家族成员战死 — `scn_cutscene_family_member_death_war`

| 项目 | 内容 |
|------|------|
| **类** | `ClanMemberWarDeathSceneNotificationItem` |
| **触发** | 家族成员战斗死亡 |
| **总人数** | 1 + 5 = **6** |
| **文本 ID** | `str_family_member_death_war` |

布局同 #2，区别仅在 SceneID 和文本 key。

---

### 4. 寿终正寝 — `scn_cutscene_death_old_age`

| 项目 | 内容 |
|------|------|
| **类** | `DeathOldAgeSceneNotificationItem` |
| **触发** | 英雄老死 |
| **总人数** | 1 + 5 = **6** |
| **文本 ID** | `str_died_of_old_age` |

布局同 #2/#3。

---

### 5. 宣告龙旗 — `scn_cutscene_declare_dragon_banner`

| 项目 | 内容 |
|------|------|
| **类** | `DeclareDragonBannerSceneNotificationItem` |
| **触发** | 主线任务：宣告龙旗归属 |
| **总人数** | **17**（硬编码兵种 + 玩家 + 族人） |
| **文本 ID** | `str_declare_dragon_banner` |
| **文本变量** | `PLAYER_WANTS_RESTORE` (0/1), `DAY_OF_YEAR`, `YEAR` |

**角色槽位**（每个 index 对应固定场景站位）：

| 序号 | 原版角色 | 类型 |
|------|----------|------|
| 0 | `battanian_picked_warrior` → 或族人[0] | 兵种/Hero |
| 1 | `imperial_infantryman` | 固定兵种 |
| 2 | `imperial_veteran_infantryman` | 固定兵种 |
| 3 | `sturgian_warrior` → 或族人[1] | 兵种/Hero |
| 4 | `imperial_menavliaton` | 固定兵种 |
| 5 | `sturgian_ulfhednar` → 或族人[2] | 兵种/Hero |
| 6 | `aserai_recruit` | 固定兵种 |
| 7 | `aserai_skirmisher` | 固定兵种 |
| 8 | `aserai_veteran_faris` | 固定兵种 |
| 9 | `imperial_legionary` → 或族人[3] | 兵种/Hero |
| 10 | `mountain_bandits_bandit` | 固定兵种 |
| 11 | `mountain_bandits_chief` | 固定兵种 |
| 12 | `forest_people_tier_3` → 或族人[4] | 兵种/Hero |
| 13 | `mountain_bandits_raider` | 固定兵种 |
| 14 | **玩家** (`CharacterObject.PlayerCharacter`) | ✅ 玩家 |
| 15 | `vlandian_pikeman` | 固定兵种 |
| 16 | `vlandian_voulgier` | 固定兵种 |

> 族人替换逻辑：按 `Hero.MainHero.Clan.Heroes` 等级排序取前 5 个非儿童存活族人，
> 分别在 index 0/3/5/9/12 替换对应兵种（如果族人存在）。

---

### 6. 帝国阴谋开始 — `scn_empire_conspiracy_start_notification`

| 项目 | 内容 |
|------|------|
| **类** | `EmpireConspiracyBeginsSceneNotificationItem` |
| **触发** | 主线：帝国阴谋任务开始 |
| **总人数** | **8**（密谋者） |
| **文本 ID** | `str_empire_conspiracy_begins_antiempire` / `str_empire_conspiracy_begins_proempire` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0–7 | **密谋者** — `villager_empire` CharacterObject + `conspirator_cutscene_template` 装备，随机颜色 |

---

### 7. 反帝国阴谋支持 — `scn_empire_conspiracy_supports_notification`

| 项目 | 内容 |
|------|------|
| **基类** | `EmpireConspiracySupportsSceneNotificationItemBase` |
| **子类** | `AntiEmpireConspiracyBeginsSceneNotificationItem` / `ProEmpireConspiracyBeginsSceneNotificationItem` |
| **总人数** | **6** |
| **文本 ID** | `str_empire_conspiracy_supports_antiempire` / `str_empire_conspiracy_supports_proempire` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **国王** — `King` Hero + 平民装 |
| 1–3 | **密谋者** — `villager_battania` + `conspirator_cutscene_template` 装备 |
| 4–5 | **卫士** — `GetBodyguardOfCulture(King.MapFaction.Culture)` |

---

### 8–10. 龙旗碎片发现 — 无角色场景

| # | 类 | SceneID | 文本 ID |
|---|-----|---------|---------|
| 8 | `FindingFirstBannerPieceSceneNotificationItem` | `scn_first_banner_piece_notification` | `str_first_banner_piece_found` |
| 9 | `FindingSecondBannerPieceSceneNotificationItem` | `scn_second_banner_piece_notification` | `str_second_banner_piece_found` |
| 10 | `FindingThirdBannerPieceSceneNotificationItem` | `scn_third_banner_piece_notification` | `str_third_banner_piece_found` |

> #10 有确认按钮："Assemble"（组装龙旗），标题变为 `str_third_banner_piece_found_assembled`。

---

### 11. 继承人成年（男性） — `scn_cutscene_heir_coming_of_age`

| 项目 | 内容 |
|------|------|
| **类** | `HeirComingOfAgeSceneNotificationItem` |
| **总人数** | **4** |
| **文本 ID** | `str_hero_came_of_age` |
| **文本变量** | `HERO_NAME`, `DAY_OF_YEAR`, `YEAR` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **导师** — `MentorHero` + 平民装无头盔 |
| 1 | **6 岁时的继承人** — `HeroCameOfAge` + 文化对应的 `comingofage_kid_*_cutscene_template` 装备，DynamicBodyProperties 年龄=6 |
| 2 | **14 岁时的继承人** — 同上，年龄=14 |
| 3 | **成年后的继承人** — `HeroCameOfAge` + 战斗装无头盔 |

---

### 12. 继承人成年（女性） — `scn_hero_come_of_age_female`

| 项目 | 内容 |
|------|------|
| **类** | `HeirComingOfAgeFemaleSceneNotificationItem` |

布局同 #11（4 个槽位：导师 + 6岁 + 14岁 + 成年），场景不同。

---

### 13. 处决 — `scn_execution_notification`

| 项目 | 内容 |
|------|------|
| **类** | `HeroExecutionSceneNotificationData` |
| **总人数** | **2** |
| **文本** | 由工厂方法动态生成 |

**角色槽位**：

| 序号 | 角色 | 特殊处理 |
|------|------|----------|
| 0 | **受刑者** — `Victim` + 战斗装全部武器槽清空 | 无武器 |
| 1 | **行刑者** — `Executer` + `execution_axe`（唯一武器） | 硬编码 `execution_axe` ID |

**工厂方法**：

| 方法 | 用途 |
|------|------|
| `CreateForPlayerExecutingHero(dyingHero, onAffirmative)` | 玩家处决别人 |
| `CreateForInformingPlayer(executingHero, dyingHero)` | 通知玩家有人被处决 |

---

### 14. 加入王国 — `scn_cutscene_factionjoin`

| 项目 | 内容 |
|------|------|
| **类** | `JoinKingdomSceneNotificationItem` |
| **总人数** | 1 + 5 = **6** |
| **文本 ID** | `str_new_faction_member` |
| **文本变量** | `CLAN_NAME`, `DAY_OF_YEAR`, `YEAR`, `KINGDOM_FORMALNAME` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **新家族族长** — `NewMemberClan.Leader` + 平民装无头盔 |
| 1–5 | **王国代表** — `GetMilitaryAudienceForKingdom(KingdomToUse)` 取 5 人 |

---

### 15. 王国建立 — `scn_kingdom_made`

| 项目 | 内容 |
|------|------|
| **类** | `KingdomCreatedSceneNotificationItem` |
| **总人数** | 1 + 5 = **6** |
| **文本 ID** | `str_kingdom_created` |
| **文本变量** | `KINGDOM_NAME`, `DAY_OF_YEAR`, `YEAR`, `LEADER_NAME` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **国王** — `NewKingdom.Leader` + 战斗装无头盔 |
| 1–5 | **王国成员** — `GetMilitaryAudienceForKingdom(NewKingdom, includeKingdomLeader: false)` 取 5 人 |

---

### 16. 王国覆灭 — `scn_cutscene_enemykingdom_destroyed`

| 项目 | 内容 |
|------|------|
| **类** | `KingdomDestroyedSceneNotificationItem` |
| **总人数** | **2**（尸体） |
| **文本 ID** | `str_kingdom_destroyed_scene_notification` |
| **文本变量** | `DAY_OF_YEAR`, `YEAR`, `FORMAL_NAME` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0–1 | **阵亡士兵** — `GetRandomTroopForCulture(DestroyedKingdom.Culture)` 随机兵种 |

---

### 17. 主角战死（败方） — `scn_cutscene_main_hero_battle_death`

| 项目 | 内容 |
|------|------|
| **类** | `MainHeroBattleDeathNotificationItem` |
| **总人数** | 1 + **23** = **24** |
| **文本 ID** | `str_main_hero_battle_death` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **逝者** — `DeadHero` + 战斗装无头盔 |
| 1–23 | **尸体** — 前 12 个来自 `DeadHero.MapFaction.Culture` 随机兵种，后 11 个来自 `KillerCulture` |

---

### 18. 主角战死（胜方） — `scn_cutscene_main_hero_battle_victory_death`

| 项目 | 内容 |
|------|------|
| **类** | `MainHeroBattleVictoryDeathNotificationItem` |
| **总人数** | 1 + 2 尸体 + 最多 3 同伴 = **最多 6** |
| **文本 ID** | `str_main_hero_battle_death` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **逝者** — `DeadHero` + 战斗装无头盔 |
| 1–2 | **阵亡士兵** — 随机兵种 |
| 3–5 | **幸存同伴** — `EncounterAllyCharacters.Take(3)` |

---

### 19. 婚礼 — `scn_cutscene_wedding`

| 项目 | 内容 |
|------|------|
| **类** | `MarriageSceneNotificationItem` |
| **总人数** | 2 + 1 牧师 + 6 观众 = **最多 9** |
| **文本 ID** | `str_marriage_notification` |
| **文本变量** | `DAY_OF_YEAR`, `YEAR`, `FIRST_HERO`, `SECOND_HERO` |

**角色槽位**：

| 序号 | 角色 | 装备来源 |
|------|------|---------|
| 0 | **新郎** — `GroomHero` | 新郎的 `CivilianEquipment` |
| 1 | **新娘** — `BrideHero` | 文化对应的 `marriage_female_*_cutscene_template`（婚礼礼服） |
| 2 | **牧师** — `cutscene_monk` | 牧师自带装备 |
| 3–8 | **观众** (6人) — 双方父母、兄弟姐妹、朋友 | 各自平民装 |

> **新娘礼服 ID（按文化）**：
> `marriage_female_emp_cutscene_template` / `marriage_female_ase_cutscene_template` /
> `marriage_female_bat_cutscene_template` / `marriage_female_khu_cutscene_template` /
> `marriage_female_stu_cutscene_template` / `marriage_female_vla_cutscene_template`

---

### 20. 新生儿（男孩） — `scn_born_baby`

| 项目 | 内容 |
|------|------|
| **类** | `NewBornSceneNotificationItem` |
| **总人数** | **3** |
| **文本 ID** | `str_baby_born` |
| **文本变量** | `FATHER_NAME`, `MOTHER_NAME`, `DAY_OF_YEAR`, `YEAR` |

**角色槽位**：

| 序号 | 角色 |
|------|------|
| 0 | **父亲** — `MaleHero` + 平民装无头盔 |
| 1 | **母亲** — `FemaleHero` + 平民装无头盔无护肩 |
| 2 | **产婆** — `cutscene_midwife` |

---

### 21. 新生儿（女孩） — `scn_born_baby_female_hero`

| 项目 | 内容 |
|------|------|
| **类** | `NewBornFemaleHeroSceneNotificationItem` |
| **文本 ID** | `str_baby_born_only_mother` |
| **文本变量** | `MOTHER_NAME`, `DAY_OF_YEAR`, `YEAR` |

布局同 #20（3 个槽位）。

---

### 22. 新生儿（女孩，备选） — `scn_born_baby_female_hero2`

| 项目 | 内容 |
|------|------|
| **类** | `NewBornFemaleHeroSceneAlternateNotificationItem` |
| **文本 ID** | `str_baby_born_only_mother` |

**角色槽位**（与上面不同！）：

| 序号 | 角色 |
|------|------|
| 0 | **空位** — `new SceneNotificationCharacter(null)` |
| 1 | **母亲** — `FemaleHero` + 平民装 |
| 2 | **产婆** — `cutscene_midwife` |

> 这个场景没有父亲在场（第一个槽位是 null）。

---

### 23. 宣誓效忠 — `scn_pledge_allegiance_notification`

| 项目 | 内容 |
|------|------|
| **类** | `PledgeAllegianceSceneNotificationItem` |
| **总人数** | 2 + 24 = **26** |
| **文本 ID** | `str_pledge_notification_title` |
| **文本变量** | `RULER` (CharacterProperties), `PLAYER_WANTS_RESTORE`, `DAY_OF_YEAR`, `YEAR` |

**角色槽位**：

| 序号 | 角色 | 特殊处理 |
|------|------|---------|
| 0 | **玩家** — `PlayerHero` + 战斗装，**骑马** | 如果没有马自动给一匹 |
| 1 | **国王** — `PlayerHero.Clan.Kingdom.Leader` + 战斗装，**骑马** | 同上 |
| 2–25 | **士兵方阵** — `GetRandomTroopForCulture(culture)` × 24 | 随机兵种 |

> 这是唯一 `useHorse: true` 的场景。

---

### 24. 支持阵营覆灭 — `scn_supported_faction_defeated_notification`

| 项目 | 内容 |
|------|------|
| **类** | `SupportedFactionDefeatedSceneNotificationItem` |
| **文本 ID** | `str_supported_faction_defeated` |
| **文本变量** | `FORMAL_NAME`, `PLAYER_WANTS_RESTORE`, `DAY_OF_YEAR`, `YEAR` |

> 无角色槽位，不 override `GetSceneNotificationCharacters()`。

---

## 触发位置总览

所有触发在 `SandBox.dll` → `DefaultCutscenesCampaignBehavior`：

| 游戏事件 | 使用的 SceneNotification |
|----------|--------------------------|
| `MarriageEvent` | `MarriageSceneNotificationItem` |
| `HeroCameOfAgeEvent` | `HeirComingOfAgeSceneNotificationItem` / `HeirComingOfAgeFemaleSceneNotificationItem` |
| `ChildBirthEvent` | `NewBornSceneNotificationItem` / `NewBornFemaleHeroSceneNotificationItem` / `NewBornFemaleHeroSceneAlternateNotificationItem` |
| `HeroKilledEvent` (战斗) | `ClanMemberWarDeathSceneNotificationItem` |
| `HeroKilledEvent` (非战斗) | `DeathOldAgeSceneNotificationItem` / `ClanMemberPeaceDeathSceneNotificationItem` |
| `ClanJoinedKingdomEvent` | `JoinKingdomSceneNotificationItem` |
| `KingdomCreatedEvent` | `KingdomCreatedSceneNotificationItem` |
| `KingdomDestroyedEvent` | `KingdomDestroyedSceneNotificationItem` |
| 玩家自立为王 | `BecomeKingSceneNotificationItem` |
| 主线龙旗 | `FindingFirstBannerPieceSceneNotificationItem` / `FindingSecondBannerPieceSceneNotificationItem` / `FindingThirdBannerPieceSceneNotificationItem` / `DeclareDragonBannerSceneNotificationItem` / `PledgeAllegianceSceneNotificationItem` / `SupportedFactionDefeatedSceneNotificationItem` |
| 帝国阴谋任务链 | `EmpireConspiracyBeginsSceneNotificationItem` / `AntiEmpireConspiracyBeginsSceneNotificationItem` / `ProEmpireConspiracyBeginsSceneNotificationItem` |
| 主角战死判定 | `MainHeroBattleDeathNotificationItem` / `MainHeroBattleVictoryDeathNotificationItem` |
| 处决通知 | `HeroExecutionSceneNotificationData` |

---

## 如何自定义

### 替换已有场景的角色

不能直接替换——这些类都是游戏原生代码。但可以通过 **Harmony Patch** 拦截 `GetSceneNotificationCharacters()` 或 `ShowSceneNotification()` 来替换 `SceneNotificationCharacter` 里的 `CharacterObject`/`Equipment`。

### 新增自定义场景

继承 `SceneNotificationData`，override `SceneID`、`TitleText`、`GetSceneNotificationCharacters()`，然后调用 `MBInformationManager.ShowSceneNotification(yourData)`。

前提是你有对应的场景文件（`scn_xxx`）。`Scene.Read(SceneID)` 找不到会渲染失败——可以用游戏内已有的 SceneID 复用场景布局，只替换角色（README 上方 25 个场景 ID 皆可直接复用）。

### 🔴 自定义图片：原生无槽位（2026-08-30 实测）

1.2.12 → 1.5.x 的 `SceneNotificationData` **全都没有纹理/图片属性**（无 TextureName/Sprite），UI 布局 `Modules/Native/GUI/Prefabs/Information/SceneNotification.xml` 也只有 `SceneWidget`（3D 场景）+ 文字 + 按钮，无 ImageWidget。**"背景图"的官方表达 = 3D 场景布景**。想要 2D 图覆盖层的替代方案：
1. 自定义 3D 场景（SceneID 指向自己的场景文件，把图做成布景/标语板）；
2. 自挂 GauntletLayer 叠图（随过场开关显隐）；
3. 播放前先切 2D 层过渡。

### 自定义音效 / 减速

- `SoundEventPath`（全版本有）——播放过场的音效。
- 1.3.15+：`SceneProperties` 控制 `InitializePhysics` / `DisableStaticShadows` / `OverriddenWaterStrength`。

### 关键辅助方法

```csharp
// Hero → SceneNotificationCharacter（最常用）
CampaignSceneNotificationHelper.CreateNotificationCharacterFromHero(
    Hero hero,
    Equipment overridenEquipment = null,   // 不给就用英雄的战斗装
    bool useCivilian = false,
    BodyProperties overriddenBodyProperties = default,
    uint overriddenColor1 = uint.MaxValue,  // 不给就用阵营颜色
    uint overriddenColor2 = uint.MaxValue,
    bool useHorse = false
);

// 去武器
CampaignSceneNotificationHelper.RemoveWeaponsFromEquipment(
    ref Equipment equipment,
    bool removeHelmet = false,
    bool removeShoulder = false
);

// 文化卫士
CampaignSceneNotificationHelper.GetBodyguardOfCulture(CultureObject culture);

// 观众池
CampaignSceneNotificationHelper.GetMilitaryAudienceForHero(Hero hero, ...);
CampaignSceneNotificationHelper.GetMilitaryAudienceForKingdom(Kingdom kingdom, ...);
```

## 硬编码资源 ID 汇总

| ID | 用途 | 场景 |
|----|------|------|
| `cutscene_monk` | 牧师 CharacterObject | 婚礼 |
| `cutscene_midwife` | 产婆 CharacterObject | 新生儿 |
| `execution_axe` | 处刑斧 ItemObject | 处决 |
| `conspirator_cutscene_template` | 密谋者装备 MBEquipmentRoster | 帝国阴谋 |
| `marriage_female_*_cutscene_template` | 新娘礼服（6 种文化） | 婚礼 |
| `comingofage_kid_*_cutscene_template` | 儿童装备（6 种文化） | 继承人成年 |
| `villager_empire` / `villager_battania` / ... | 村民 CharacterObject | 阴谋场景 |

## 本 mod 调试观察（2026-08-30 登记）

`Debug/SceneNotificationLoggerPatch.cs`（`[HarmonyPatch]`，PatchAll 自动注册）在两个站点打日志到 `Debug/StoryEngine_RuntimeLog.txt`：

| 标签 | 补丁点 | 含义 |
|---|---|---|
| `[SceneNotification:REQUEST]` | `MBInformationManager.ShowSceneNotification` | 过场**请求**（唯一入口，原生+mod 全部经过） |
| `[SceneNotification:PLAY]` | `GauntletSceneNotification.CreateSceneNotification` | 上下文门控通过后**真正开播** |

日志内容：`type=`（数据类名）/ `scene=` / `title=` / `context=`（Any/Mission/Map…）/ `pause=` / `sound=` / `affirmBtn=`/`negBtn=` / `chars=` / `banners=`（1.3.15+ 追加 `ships=`）。搜索标签两处同构：REQUEST 与 PLAY 成对出现即一次完整播放；只有 REQUEST 没有 PLAY = 被门控排队/未播。

版本适配：`GetShips()` 用 `#if MB2_GE_130` 裁剪；`data` 参数名在 1.2.12（双参）与 1.5.x（单参）一致，Harmony 按名注入全版本可用。
