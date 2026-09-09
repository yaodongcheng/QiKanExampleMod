# 🔴 场景 Tag 与 StandingPoint 系统分析（场景内的"位置"如何驱动人物）

> 分析对象：v1.5.2 引擎（`TaleWorlds.MountAndBlade.dll` / `SandBox.dll` 反编译）+ 织丰场景（`Modules/Shokuho/SceneObj/*/scene.xscene` 原文）
> 配套生成物：`Knowledge/场景Tag清单.md`（逐场景 tag 全表，脚本 `tools/gen_scene_tag_list.py`）

**一句话结论**：场景 tag 不只是出生点——引擎把场景里**预放置的实体**当"可用位置"消费，分三级（出生位 / StandingPoint 行为位 / UsableMissionObject 机械位）；**"两个点位永远有人面对面交谈、人却动态换"就是 `AnimationPoint`（StandingPoint 子类）+ `PairEntity` 配对的工作方式**，织丰场景里同款机制在跑。

---

## 0. Tag 挂在哪、格式如何（xscene 实证）

`SceneObj/<场景>/scene.xscene`（XML，1.5.x 场景格式）里：每个 `game_entity`（预放置模型/布景）可带 0~N 个 `<tag name>`、一个 `<transform position="x,y,z">` 与可选 `<scripts>`：

```xml
<game_entity prefab="sp_npc_argue_set">      <!-- 情景组：面对面交谈布景 -->
  <tags><tag name="sp_npc_argument_trio"/></tags>
  <transform position="62.553, 52.979, 4.367" .../>
  <children>                                  <!-- 组内每人一个子实体 -->
    <game_entity _index_="0">
      <game_entity _index_="0">
        <scripts>
          <script name="AnimationPoint">
            <variables><variable name="PairEntity" value="{95EBD191-...}"/></variables>
          </script>
        </scripts>
      </game_entity>
    </game_entity>
  </children>
</game_entity>
```

- tag = 位置标记集合；**坐标可解析**（`transform.position`，米）
- 纯 tag（无 scripts）实体 = 一级出生位；带 scripts = 二级/三级（见下）
- 🔴 一个实体可挂**多个 tag**（如 `sp_notable`+`npc_drinker`+`npc_common`+`npc_wait` 同一把椅子）——引擎按当前人口/身份往同一个位置"分配"不同的人

## 1. 三级消费模型（tag 被谁用）

| 级别 | 机制 | 场景形态 | 引擎类 | 触发 |
|---|---|---|---|---|
| ① 出生位 | 纯位置标记，spawn Agent | tag 实体、无 scripts（如织丰 `sho_sitting_pose5` 无脚本造型座位）| `MissionLocationLogic`（见跳转文档 §3.12）| 进场景时按 Location 数据 spawn |
| ② 行为位 | **StandingPoint 系统**：占位后播动画，离场换人 | `<script name="AnimationPoint/ChairUsePoint/PlayMusicPoint/DynamicObjectAnimationPoint">` | `StandingPoint : UsableMissionObject`（SandBox.dll 的 `SandBox.Objects.AnimationPoints.*`）| **AI 空闲自动 claim + 播动画；占用者离开 → 空出 → 换人** |
| ③ 机械位 | 可操作物（门/梯/马/攻城器）| `UsableMissionObject`/`ClimbingMachine`/`EventTriggeringUsableMachine` | 引擎自带 | 玩家点击 / AI 任务 |

### ①与②的区别（容易混）
- ①**只给位置**：角色 spawn 到点位，坐姿是 `Agent.AiBehavior` 的 `Sit=42 / SitOnTheFloor=43 / SitOnAThrone=44` 行为（**纯造型座位不建交互绑定**）
- ②**给位置+交互+动画**：Agent 通过 `UseGameObject` 占位，**绑定 `CurrentlyUsedGameObject`**，起身走 `StopUsingGameObjectMT`（`AutoAttachAfterStoppingUsingGameObject` 标志可自动续接下一个可用物）

## 2. StandingPoint 引擎类（反向实证）

`TaleWorlds.MountAndBlade.StandingPoint : UsableMissionObject`——即**行为位本身是可交互对象**：
- 字段：`AutoSheathWeapons`（占位自动收武器）/`AutoEquipWeaponsOnUseStopped`/`TranslateUser`（强制摆位）/`_needsSingleThreadTickOnce`（tick 扫描附近 Agent 分配）
- 子类（SandBox.dll `SandBox.Objects.AnimationPoints`）：`AnimationPoint`、`ChairUsePoint`、`DynamicObjectAnimationPoint`、`PlayMusicPoint`（乐师位）
- `AnimationPoint` 配置字段：`ArriveAction / LoopStartAction / PairLoopStartAction / LeaveAction`（到位/循环/成对/离开动画）+ `GroupId`（一组多人）+ `RightHandItem`（手持道具）+ 问候动画族 `_greetingFront/Right/LeftActions`（4 组）+ `PairState.StartPairAnimation`（**pair 双方到位后同步播面对面交谈**）
- **换人闭环**：占用者离开（对话结束/巡逻/task 完成）→ `OnUseStopped` → 点空出 → 空闲小兵 AI 寻位重新 claim —— 这就是"点位永远有人、人在换"
- `PairEntity` GUID = 场景编辑器里两个 AnimationPoint 拉线配对；`"alternative"` 常量 tag = 备用位（原版 `alternative×7` 等）

## 3. 场景"人群布景"prefab 组（可直接复用）

| prefab 组名 | 内容 | tag（引擎消费）|
|---|---|---|
| `sp_npc_argue_set` / `sp_npc_argument_trio` | 2~3 人争吵/争论（AnimationPoint 配对）| `sp_npc_argument_trio` |
| `sp_notable_instructing_with_listeners` | 贵族训话（1 讲 2 听）| — |
| `sp_notable_giving_order` | 下命令（将领对部下）| — |
| `sp_notable_hangout_set` | 贵族闲聊组 | `sp_notable_hangout_set` |

## 4. 织丰场景覆盖情况（清单文档 §1 数据）

| 场景 | tag 情况 | 评价 |
|---|---|---|
| `sho_keep_scene` | 9 类：`npc_wait×30/npc_common×29/sp_notable×29/npc_drinker×28/npc_idle/sp_throne/gambler_npc/gambler_player/reserved` + 情景组实体（argue/notable 组）| ✅ 合格（比原版 keep 多了酒客/王座位）|
| `sho_prison_b` / `sho_tavern_a` | 5 / 8 类（`sp_prison_guard`、`npc_drinker×8`、`musician×3` 等）| ✅ 合格 |
| `sho_town_a/b/c` | 33 类**全是攻城元素**（`defender/sho_archer_enforcer/gate/plank/strategycamera*`）| ⚠️ 缺少街道生活位/AI 行为位（原版 town 676 tag）|
| **`sho_castle_map_*` / `sho_village_*` / `sho_duel_map_*`** | **0 个 tag**（433/733 实体摆了无 tag）| 🔴 城堡庭院/村庄无出生位与行为位——**待实机验证**（守卫/平民可能缺失或走兜底）|

## 5. 对 Taikou / LWN 的实操结论

1. **做"人群生活感"不用写代码**：场景里摆 prefab 组（子实体 AnimationPoint + PairEntity + GroupId）→ 引擎自动填人/换人；织丰与原版的这些 prefab 组**可直接拖进自己场景复用**。
2. **造场景 tag 清单**（抄原版约定）：室外至少 `npc_common×N`/`sp_guard×N`/`sp_notable×N` + `sp_throne`；室内领主殿加 `sp_guard_unarmed` + `sp_defender_infantry/archer_lords_hall`（攻城守军位）；地牢 `sp_prison_guard`+`sp_prisoner`。
3. **玩家出生不用 tag**：走 `OpenTownCenterMission(scene, ..., playerSpawnTag)` 参数 / 场景默认出生点。
4. 出生位（①）一个实体多 tag 复用是原版正常设计；行为位（②）需实体挂 `AnimationPoint` 脚本才有 AI 动态。
5. `scene.xscene` 是 XML——**脚本可批量解析 tag 与坐标**（`tools/gen_scene_tag_list.py` 范本；坐标提取见文档 §0 的解析示例）。

## 6. 反编译锚点

| 内容 | 位置 |
|---|---|
| `StandingPoint : UsableMissionObject` 全字段 | `TaleWorlds.MountAndBlade.dll` 该类型 |
| `AnimationPoint : StandingPoint`（配置/Pair/问候动画） | `SandBox.dll` `SandBox.Objects.AnimationPoints.AnimationPoint` |
| `Agent.StopUsingGameObjectMT` 链路 | `TaleWorlds.MountAndBlade.dll` `Agent`（`StopUsingGameObjectAux` → `CurrentlyUsedGameObject.OnUseStopped`）|
| 行为枚举 Sit/SitOnTheFloor/SitOnAThrone | 同上 `Agent`（42/43/44）|
| 出生分配（一级） | 跳转文档 §3.12（`MissionLocationLogic` + `HeroAgentLocationModel`）|
| 织丰场景 tag 全表 | `Knowledge/场景Tag清单.md`（生成物）|
