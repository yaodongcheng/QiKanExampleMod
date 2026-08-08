# 大世界遇敌进自定义对话（inquiry 分流 + 开真对话 Mission，方案 A）

> 状态：已规划，待实现。
> 触发场景：玩家在大世界沙盘遇到（中立/未开战）部队时，弹窗选择「原版对话 / 新版对话」，
> 选新版则进入一个真对话 Mission，走本 mod 的 `InteractionMissionView` 自定义对话，而非骑砍原版对话。

## Context（为什么要做）

玩家在大世界沙盘遇到部队时，引擎走 **map conversation**（地图覆盖层），不是真正的 `Mission`：
`PlayerEncounter.DoMeeting()` → `CampaignMapConversation.OpenConversation(player, partner)`
→ `ConversationManager.OpenMapConversation(...)`，对话双方是虚拟 `MapConversationAgent`（空壳），
**没有 `MissionScreen`、没有真实 `Agent`**。而本 mod 的自定义对话管线（`InteractionMissionView` /
`StoryDialogVM` / `InteractionController.StartInteraction(Agent)`）全部挂在 `MissionScreen` 上、以真实
`Agent` 为索引，所以大世界遇敌时它根本不会被实例化 → 只能进原版对话。

**已定方案**：在唯一咽喉 `CampaignMapConversation.OpenConversation` 处拦截，弹 inquiry 让玩家选
「原版对话 / 新版对话」。选新版 → 用 `CampaignMission.OpenConversationMission(...)` **开一个真正的对话
Mission**（真实 `Agent` + `MissionScreen` 生成 → 现有 `InteractionMissionView` 管线**原样复用，无需
Hero-only 重构**），并抑制该 mission 自动拉起的原版对话、改为自动触发自定义对话流；选原版 →
原样放行。对话结束后结束 mission 并收尾 `PlayerEncounter`。**覆盖所有地图对话**（对方是 Hero 的场景）。

代价：开 mission 有一次对话场景加载（短暂淡入），换取零重构 + 完整复用现有 Agent 演出/镜头管线。

### 世界状态保持（mission 结束回沙盘后，军队位置/时间不变）

- **引擎机制**：campaign 模拟（含部队移动）只在 `MapState` 推进；切进任何 `Mission` 后 `GameState`
  离开 `MapState`，campaign 时间冻结、部队不动，回来在原 `MapState` 同位置恢复。遭遇期间
  `PlayerEncounter` active 还把双方锁在一起。
- **原版同款先例**：`PlayerEncounter.DoCaptureHeroes()` 在 hideout 内就是用 `OpenConversationMission`
  （真 mission）从遭遇态开对话、结束回地图，世界完好——我们对世界地图部队遭遇用同一手法，等价。
- 与原版世界地图遭遇（不切状态的覆盖层）相比唯一差别是**多一次对话场景淡入**；模拟状态本身一致。

### 进 mission 后只走 InteractionMissionView，绝不闪原版对话

抑制补丁从**第一帧**就把 `ConversationMissionLogic.OnMissionTick` 整段 return false（标志在
`OpenConversationMission` 之前已置位），拉起原版对话的 `InitializeAfterCreation` 永不执行，连一帧原版
对话都不会出现；`InteractionMissionView`（挂在每个 mission 上）检测标志后自动拉起自定义流程。

### 关键技术发现（反编译核实）

`CampaignMission.OpenConversationMission(player, partner, specialScene="", sceneLevels="")`
（SandBox `SandBoxMissions.OpenConversationMission`）：
- 场景：`specialScene` 为空时自动 `PlayerEncounter.GetConversationSceneForMapPosition(MainParty.Position2D)`，
  传 `""` 即可自动按地形选对话场景。
- 建 5 个 behavior，其中关键是 **`ConversationMissionLogic(player, partner)`**（namespace
  `SandBox.Conversation.MissionLogics`，`InteractionMissionView.cs:3` 已 import 该 ns）：
  - `AfterStart()`：生成玩家 + 对方 `Agent`（对方存私有字段 `_curConversationPartnerAgent`）。
  - `OnMissionTick()`：①agents 就绪 → `InitializeAfterCreation` → `ConversationManager.SetupAndStartMapConversation`
    **自动拉起原版对话**；②`if (!ConversationManager.IsConversationInProgress) Mission.EndMission()`。
  - **陷阱**：只拦 ① 会触发 ② 把 mission 立刻关掉。**解法：整个 `OnMissionTick` Prefix return false**
    （仅对我们的 mission），agents 仍在 `AfterStart` 生成，原版对话与自动结束一并掐掉，由我们接管。
- 收尾：`MapEventHelper.OnConversationEnd()`（不交战则置 `PlayerEncounter.LeaveEncounter=true`）+
  `Mission.Current.EndMission()` 回大地图。

### 边界澄清

- **settlement 里点 NPC 说话 = 真 Mission**（`MissionConversationLogic`），本补丁不碰，现有 F 交互已覆盖。
- **settlement「请求会面」也用 `OpenConversationMission`** → 因此抑制补丁**必须只对我们的 mission 生效**
  （用静态标志位 gate），否则会误伤城镇会面。
- **至战敌方部队不进对话**，直接进战斗/遭遇菜单；不触发本补丁。
- **本队伍 companion 在大地图对话**：核心代码未见走 `OpenConversation`，入口待运行时确认，暂不纳入。

### 约束（CLAUDE.md 铁律）

LLM 路径走 `Settings.Instance.IsLLMConfigured` 总闸；C# 单次检定路径无 LLM 也要能跑。世界观字串不硬编码。
资源进出走 `AgentControlHelper`。完成后登记 wheels.md。

---

## 实施步骤

### 1. 静态协调标志 `MapEncounterDialogState`

新文件 `Interaction/MapEncounterDialogState.cs`——抑制补丁与 `InteractionMissionView` 靠它识别
「这是我们的遭遇对话 mission」：

```csharp
public static class MapEncounterDialogState
{
    public static bool Active;                 // 我们的遭遇 mission 生命周期内为 true
    public static CharacterObject Partner;     // 对方角色，用于在 mission 里精确定位 partner Agent
    public static void Clear() { Active = false; Partner = null; }
}
```

### 2. Harmony 拦截 + inquiry 分流（咽喉，覆盖所有地图对话）

新文件 `Interaction/MapEncounterConversationPatch.cs`，跟随现有补丁风格（参看
`InteractionMissionView.cs:886-950`，`PatchAll()` 在 `MySubModule.cs:43` 自动注册）：

```csharp
[HarmonyPatch(typeof(CampaignMapConversation), nameof(CampaignMapConversation.OpenConversation))]
public static class MapEncounterConversationPatch
{
    private static bool _reentry = false;
    [HarmonyPrefix]
    public static bool Prefix(ConversationCharacterData playerCharacterData,
                              ConversationCharacterData conversationPartnerData)
    {
        try
        {
            if (_reentry) return true;                                  // 放行重入，别再拦
            Hero partnerHero = conversationPartnerData.Character?.HeroObject;
            if (partnerHero == null) return true;                       // 新对话需 Hero，无则放行原版

            var p = playerCharacterData; var q = conversationPartnerData; // 结构体按值入闭包
            InformationManager.ShowInquiry(new InquiryData(
                "交涉方式", "如何与对方交涉？", true, true, "新版对话", "原版对话",
                affirmativeAction: () => {
                    MapEncounterDialogState.Active = true;
                    MapEncounterDialogState.Partner = q.Character;
                    CampaignMission.OpenConversationMission(p, q);      // 开真对话 mission，场景自动选
                },
                negativeAction: () => {                                  // 原版分支：直调底层绕开本补丁
                    _reentry = true;
                    try { Campaign.Current.ConversationManager.OpenMapConversation(p, q); }
                    finally { _reentry = false; }
                }));
            return false;                                               // inquiry 异步，统一同步拦掉
        }
        catch (Exception ex) { DebugLogger.Log($"[MapConvPatch] {ex}"); return true; } // 出错放行原版
    }
}
```

### 3. Harmony 抑制对话 mission 的原版自动流程（仅对我们的 mission）

同文件追加：

```csharp
[HarmonyPatch(typeof(ConversationMissionLogic), "OnMissionTick")]
public static class SuppressVanillaConversationMissionPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        // 仅抑制「我们的遭遇 mission」；城镇会面等其它 OpenConversationMission 不受影响
        return !MapEncounterDialogState.Active;   // Active → return false 跳过原版 tick（自动对话+自动结束都掐掉）
    }
}
```

- agents 在 `ConversationMissionLogic.AfterStart()` 照常生成（不 patch AfterStart）；只掐 OnMissionTick。
- gate 在静态 `Active`：仅在 inquiry「新版」→ 我们的 mission 结束之间为 true，期间玩家不可能同时开城镇
  会面，故不误伤。

### 4. `InteractionMissionView` 自动触发自定义对话 + mission 收尾

改 `Interaction/InteractionMissionView.cs`：

- **自动触发**：在 `OnMissionTick`（已有，line 373）里加一段——若 `MapEncounterDialogState.Active`
  且本 mission 尚未触发：按 `MapEncounterDialogState.Partner` 在 `Mission.Current.Agents` 里找到对方
  `Agent`（`a.Character == Partner`），等其 `IsActive()` 后调用现有的 `StartFreeConversationFlow(partnerAgent)`
  （line 464；对话场景里双方已面对面预摆位，走其 `isStandingNaturally` 快速路径即可，无需额外走位），
  置实例标志 `_encounterDialogStarted = true` 防重入。
  - 复用现有 `_interactionController` / `StoryDialogVM` / `SetupCameraForDialogue`，零新增 UI。
- **收尾**：扩展现有 `OnDialogueEnded`（line 686）——末尾加：若 `MapEncounterDialogState.Active`，执行
  ```csharp
  try {
      MapEventHelper.OnConversationEnd();                 // 不交战 → LeaveEncounter=true
      DebugLogger.Log($"[MapConv] end: enc={PlayerEncounter.Current!=null} leave={PlayerEncounter.LeaveEncounter}");
  } catch (Exception ex) { DebugLogger.Log($"[MapConv] teardown {ex}"); }
  finally { MapEncounterDialogState.Clear(); }
  Mission.Current?.EndMission();                          // 关 mission 回大地图
  ```
  回到大地图后 `PlayerEncounter.Update` 据 `LeaveEncounter` 收尾（和平→脱离；仍敌对→遭遇菜单）。
- **安全网**：在 `OnMissionScreenFinalize`（line 654）里 `MapEncounterDialogState.Clear()`，防玩家 ESC 直接
  退 mission 时标志泄漏到下一个对话 mission。

### 5. 登记 wheels.md

增条目：「大世界地图对话→真对话 Mission 接入 —— 咽喉补丁 `CampaignMapConversation.OpenConversation`
+ inquiry 分流 + `OpenConversationMission` + `ConversationMissionLogic.OnMissionTick` 抑制（静态标志 gate）
+ `InteractionMissionView` 自动触发/收尾 + `MapEventHelper.OnConversationEnd`/`EndMission` 序列」。

---

## 关键文件

| 文件 | 改动 |
|------|------|
| `Interaction/MapEncounterDialogState.cs` | **新增**：静态协调标志（Active / Partner） |
| `Interaction/MapEncounterConversationPatch.cs` | **新增**：①咽喉 Prefix + inquiry 分流；②`ConversationMissionLogic.OnMissionTick` 抑制补丁 |
| `Interaction/InteractionMissionView.cs` | 自动触发自定义对话（OnMissionTick 加分支）+ 收尾（OnDialogueEnded 加 EndMission/encounter 收尾）+ Finalize 清标志 |
| `plans/rules/wheels.md` | 登记新轮子 |

复用而非新写：`InteractionMissionView` 整套（VM/控制器/镜头/选项/记忆）、`StartFreeConversationFlow`、
`InteractionController`、`StoryDialogVM`、意图引擎、`OpenConversationMission`、`MapEventHelper`。
**不动**：意图层、`IntentContext`、`InteractionController` 的 Agent 签名（真 mission 有真 Agent）。

---

## 风险点

1. **遭遇态下开 mission → EndMission 后的 `PlayerEncounter` 解算（需测，但有原版先例兜底）**：拦掉
   原版后 `PlayerEncounter` 停在 `DoMeetingInternal`（已设 `_meetingDone=true`、`EncounterState=Begin`），
   我们改开 mission；mission 结束回大地图后需 `PlayerEncounter` 正确收尾。已知 `DoCaptureHeroes`(hideout)
   是同款「遭遇态开 OpenConversationMission」的原版先例，风险可控。仍 `DebugLogger.Log` 打点
   `PlayerEncounter.Current` / `LeaveEncounter` / `EncounterState`，实测「和平脱离 / 仍敌对回遭遇菜单 /
   不卡死 / 不重复遭遇 / 回地图后双方位置不错位」。
2. **抑制补丁误伤范围**：`OnMissionTick` 抑制仅靠静态 `Active` gate；务必确认 `Active` 在我们 mission
   外恒为 false（inquiry「原版」分支、城镇会面、任务 mission 期间都不能为 true）。Finalize 兜底清标志。
3. **partner Agent 定位**：用 `a.Character == Partner` 精确匹配（避免误抓护卫）；若一帧未生成则等下一帧，
   设超时兜底（多帧未找到则记日志并 EndMission 防卡）。
4. **「覆盖所有地图对话」波及任务/俘获/释放上下文**：这些场景走「新版」会跳过原版对话里写死的剧情
   后果（俘获/释放/任务推进）。inquiry 给了安全出口（选原版）；v1 收尾只保证不卡死、回地图，**不复刻**
   这些后果。任务类建议运行时默认走原版更稳；按 `CurrentConversationContext` 分上下文特判列为后续增量。
5. **场景加载**：`OpenConversationMission` 有一次对话场景淡入（用户已接受）；`doNotUseLoadingScreen:true`
   已在引擎侧，过场较轻。

---

## 验证（端到端）

构建后进游戏实测：

1. **触发**：大世界靠近中立/未开战、英雄统领的部队 → 进遭遇 → 弹「交涉方式」inquiry。
2. **新版分支**：选「新版对话」→ 进入对话场景（真 3D），**不出现原版对话**，自动拉起自定义对话 UI，
   对方角色正确就位、镜头正常；选项/掷骰/消选项/记忆均工作（与城镇 F 对话体验一致）。
3. **收尾**：对话结束 → mission 关闭回大地图，玩家自由移动、不卡遭遇；和平对象脱离，仍敌对对象回原版
   遭遇菜单（攻击/脱离）；回地图后双方位置与进入前一致。
4. **原版分支**：选「原版对话」→ 完全走原生 map conversation，无重入、与未装 mod 一致。
5. **抑制隔离回归**：进城「请求会面」领主、城镇点 NPC 对话 → 仍走原版（确认 `OnMissionTick` 抑制未误伤、
   `Active` 未泄漏）。
6. **广覆盖风险（风险4）**：触发 俘获敌将 / 释放俘虏 / 任务相遇，走「新版」确认不卡死、不丢任务状态；
   走「原版」一切如常。
7. **ESC 退出**：在我们的对话 mission 里直接 ESC 退出 → 标志被 Finalize 清除，下一次对话不被误抑制。
8. **无 LLM**（`IsLLMConfigured=false`）再跑：对话走 C# 单次检定，不崩。

观察 `Debug/StoryEngine_RuntimeLog.txt` 打点确认收尾状态机走向。
