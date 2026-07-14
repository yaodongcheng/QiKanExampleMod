# 村庄财富分配系统 — 实施计划

## 背景

玩家搜刮/偷 NPC 时，NPC 身上的钱是随机的（`RandomInt(1,20)` 或 `100+等级*50`），跟定居点实际经济无关。富人村和穷村没区别。

## 目标

进村时，把定居点金库的一部分钱分配到 NPC 身上 + 一个公共箱子里。偷 NPC 就是真的偷了村里的钱。按 H 看 NPC 面板也能看到身上带了多少钱。

---

## 修改文件 1：`Stealth/StealManager.cs` — 追加财富分配模块

### 新增字段

```csharp
// 每个 Agent 身上带的钱，key = Agent.Index
private static Dictionary<int, int> _agentGold = new Dictionary<int, int>();

// 公共箱子里的钱（还没被偷走的 20%）
private static int _stashGold = 0;

// 防重复分配
private static string _lastDistributedSettlementId = null;
```

### 公开 API

```csharp
public static void DistributeSettlementWealth(Settlement settlement)
public static int GetAgentGold(Agent agent)
public static int ConsumeAgentGold(Agent agent, int requested, Settlement settlement)
public static int StashGold => _stashGold;
public static int LootStash(int requested, Settlement settlement)
public static void ClearWealthDistribution()
```

### 可配置比例

```csharp
public static float CirculatingRatio { get; set; } = 0.20f;  // 金库取多少出来流通
public static float NpcShareRatio { get; set; } = 0.80f;      // 流通池里多少给 NPC，剩下进箱子
```

### 财富计算（极简）

```csharp
int treasury = settlement.Town?.Gold ?? settlement.Village?.Gold ?? 0;
int pool = (int)(treasury * CirculatingRatio);
int npcPool = (int)(pool * NpcShareRatio);
_stashGold = pool - npcPool;
```

### NPC 分配：五档身份权重

| 档位 | 身份 | 判定条件 | 权重 |
|------|------|---------|:----:|
| 1 | 村长 | `IsHero && Occ == Headman` | 10 |
| 2 | 乡绅 | `IsHero && Occ == RuralNotable` | 7 |
| 3 | 有身份者 | `IsHero && Occ != Headman && Occ != RuralNotable` | 4 |
| 4 | 模板大人 | `!IsHero && Occ != NotAssigned`（有职业的模板 NPC） | 2 |
| 5 | 小孩/路人 | `!IsHero && Occ == NotAssigned`（无业模板 NPC） | 1 |

分配公式：`个人金额 = (个人权重 / 总权重) * npcPool`，存入 `_agentGold[agent.Index]`。

### 扣款逻辑（懒扣除）

分配时不扣金库。等玩家真偷了才扣：

```csharp
int ConsumeAgentGold(Agent agent, int requested, Settlement settlement)
{
    if (!_agentGold.TryGetValue(agent.Index, out int have)) return 0;
    actual = Math.Min(requested, have);
    _agentGold[agent.Index] = have - actual;
    settlement.Town?.ChangeGold(-actual);
    AgentControlHelper.TransferGold(null, Hero.MainHero, actual);
    return actual;
}

int LootStash(int requested, Settlement settlement)
{
    actual = Math.Min(requested, _stashGold);
    _stashGold -= actual;
    settlement.Town?.ChangeGold(-actual);
    AgentControlHelper.TransferGold(null, Hero.MainHero, actual);
    return actual;
}
```

---

## 修改文件 2：`Interaction/InteractionMissionView.cs`

### 新增字段（`_animalSyncDone` 旁边，line 68）
```csharp
private bool _wealthDistributed = false;
```

### OnMissionTick 首帧（动物同步块之后）
```csharp
if (!_wealthDistributed)
{
    _wealthDistributed = true;
    var settlement = Settlement.CurrentSettlement;
    if (settlement != null)
        StealManager.DistributeSettlementWealth(settlement);
}
```

### LootAgent 金币计算改造

**原：** 金币随机，inquiry 也不显示预期金额
```csharp
if (isStealing) lootedGold = MBRandom.RandomInt(1, 20);
else lootedGold = character.IsHero ? (100 + character.Level * 50) : (character.Level * 5);
// ...
string contentText = $"你在 {targetAgent.Name} 身上发现了些东西:{itemsName} \n{partyItems}";
```

**改：** 金币来自财富分配，inquiry 显示预期金额
```csharp
int allocatedGold = StealManager.GetAgentGold(targetAgent);
if (allocatedGold > 0)
{
    lootedGold = isStealing
        ? Math.Min(allocatedGold, MBRandom.RandomInt(5, 30))
        : allocatedGold;
}
if (lootedGold == 0)  // 回落原逻辑
{
    if (isStealing) lootedGold = MBRandom.RandomInt(1, 20);
    else lootedGold = character.IsHero ? (100 + character.Level * 50) : (character.Level * 5);
}

// inquiry 内容里显示预期金币
string goldPreview = lootedGold > 0 ? $"\n金币: {lootedGold} 第纳尔" : "";
string contentText = $"你在 {targetAgent.Name} 身上发现了些东西:{itemsName}{goldPreview}\n{partyItems}";
```

然后在"全部拿走"和"自己挑选"的回调里，实际调用 `ConsumeAgentGold`（懒扣除才发生）：
```csharp
if (lootedGold > 0)
{
    int actual = StealManager.ConsumeAgentGold(targetAgent, lootedGold, Settlement.CurrentSettlement);
    // actual 可能小于 lootedGold（被其他人先偷了），但 inquiry 展示的是预期值
    if (actual > 0)
        InformationManager.DisplayMessage(new InformationMessage($"获得了 {actual} 第纳尔。", Colors.Yellow));
}
```

### StealSpecificItem 追加偷钱
```csharp
int agentGold = StealManager.GetAgentGold(agent);
if (agentGold > 0)
{
    int goldToSteal = Math.Min(agentGold, MBRandom.RandomInt(1, 15));
    if (goldToSteal > 0)
    {
        int actual = StealManager.ConsumeAgentGold(agent, goldToSteal, Settlement.CurrentSettlement);
        if (actual > 0) StealManager.RecordStolenGold(agent, actual);
    }
}
```

### OpenNPCInfoBoard 改造（~766 行）— 模板 NPC 也能开面板

**原：** `memory == null` 直接 return，模板 NPC 开不了面板
```csharp
var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
if (memory == null) return;
_npcInfoVM = new NPCInfoVM(memory, CloseNPCInfoBoard);
```

**改：** 传入 Agent，模板 NPC 也能看基本信息和身上的钱
```csharp
var memory = AllNpcMemoryManager.GetMemoryForAgent(agent);
_npcInfoVM = new NPCInfoVM(memory, agent, CloseNPCInfoBoard);
```

### OnMissionScreenFinalize 清理
```csharp
StealManager.ClearWealthDistribution();
_wealthDistributed = false;
```

---

## 修改文件 3：`Interaction/NpcInfoVM.cs` — 显示 NPC 身上带的钱

### 构造改为接收 Agent
```csharp
private readonly Agent _agent;

public NPCInfoVM(SingNpcMemorySystem memory, Agent agent, System.Action onClose)
{
    _agent = agent;
    _memory = memory;
    _profile = memory?._profile;  // 模板 NPC 无 profile，null 安全
    _onClose = onClose;
    ExecuteSelectPersonal();
    RefreshValues();
}
```

### 新增金钱属性
```csharp
private string _goldInfoText;
[DataSourceProperty]
public string GoldInfoText
{
    get => _goldInfoText;
    set { if (value != _goldInfoText) { _goldInfoText = value; OnPropertyChangedWithValue(value, "GoldInfoText"); } }
}
```

### RefreshValues 中追加金钱显示

所有 NPC（包括模板）都能看到身上带的钱：
```csharp
// 身上携带的金钱（从村庄财富分配来的）
int gold = StealManager.GetAgentGold(_agent);
if (gold > 0)
    GoldInfoText = $"身上携带: {gold} 第纳尔";
else if (_hero != null)
    GoldInfoText = $"总资产: {_hero.Gold} 第纳尔";
else
    GoldInfoText = "身上没有钱";
```

### 模板 NPC 兼容

当 `_hero == null` 时，背包/部队信息给兜底文本而不是空白：
```csharp
InventoryInfoText = _hero != null ? AgentControlHelper.GetBagInfo(_hero) : "（非英雄单位，无辎重信息）";
PartyInfoText = _hero != null ? AgentControlHelper.GetPartyInfo(_hero) : "（非英雄单位，无部队信息）";
```

---

## 修改文件 4：`GUI/Prefabs/NPCInfoBoard.xml` — UI 加金钱行

在个人属性 Tab（`IsPersonalSelected` 的 ListPanel）中，加到 `AgentStateText` 下面：

```xml
<!-- 身上带的钱（金色 #FFD700） -->
<TextWidget WidthSizePolicy="CoverChildren" HeightSizePolicy="CoverChildren" 
            SuggestedWidth="500" SuggestedHeight="40"
            Text="@GoldInfoText" Brush="MyBrush_22_Left"  
            TextColor="#FFD700FF" WordWrapping="Wrap"
            HorizontalAlignment="Left" MarginLeft="20"/>
```

---

## 修改文件 5（新增）：`Stealth/StealManager.cs` — 场景箱子

在 `StealManager` 中追加箱子数据（因为箱子是村庄财富的一部分，和 NPC 分配同源）。

### 箱子数据

```csharp
// 箱子里的物品（非动物：原材料、装备、交易品、食物等）
public static ItemRoster ChestItemRoster { get; private set; } = new ItemRoster();

// 箱子对应的实体 GameEntity（InteractionMissionView 创建后回填）
public static GameEntity ChestEntity { get; set; } = null;
```

### 填充箱子物品

在 `DistributeSettlementWealth` 里，**不搬动物资**——只记录"箱子代表哪些物资"，实际扣定居点 ItemRoster 发生在玩家真偷时（懒扣除）：

```csharp
// 记录箱子可展示的物品（仅元数据，不动 ItemRoster）
ChestItemRoster = new ItemRoster();
var settlementRoster = settlement.ItemRoster;
for (int i = 0; i < settlementRoster.Count; i++)
{
    var item = settlementRoster.GetItemAtIndex(i);
    if (item == null) continue;
    if (item.Type == ItemObject.ItemTypeEnum.Animal) continue; // 动物场景里直接偷
    int have = settlementRoster.GetElementNumber(i);
    ChestItemRoster.AddToCounts(item, have);  // 只是展示，没扣定居点
}
```

### 偷箱子 API（懒扣除——真偷才扣）

```csharp
/// <summary>偷箱子物品，实际扣定居点 ItemRoster</summary>
public static int LootChestItem(ItemObject item, int count, Settlement settlement)
{
    int actual = Math.Min(count, settlement.ItemRoster.GetItemNumber(item));
    if (actual <= 0) return 0;
    
    // 1. 从定居点 ItemRoster 真实扣除
    settlement.ItemRoster.AddToCounts(item, -actual);
    // 2. 箱子显示同步减少
    ChestItemRoster.AddToCounts(item, -actual);
    // 3. 给玩家
    AgentControlHelper.TransferItems(null, Hero.MainHero, item, actual);
    // 4. 犯罪记账
    TheftLedger.Record(initiatorId: Hero.MainHero.StringId,
        victimHeroId: null, settlementId: settlement.StringId,
        itemId: item.StringId, count: actual,
        locationName: $"在{settlement.Name}的保管箱");
    return actual;
}
```

---

## 修改文件 6（新增）：`Interaction/InteractionMissionView.cs` — 箱子实体生成 + 互动

### 新增字段

```csharp
private GameEntity _chestEntity = null;
private bool _chestSpawned = false;
```

### 首帧生成箱子（财富分配之后）

```csharp
if (!_chestSpawned && StealManager.StashGold > 0)
{
    _chestSpawned = true;
    SpawnSettlementChest();
}
```

### SpawnSettlementChest 方法

```csharp
private void SpawnSettlementChest()
{
    var scene = Mission.Current.Scene;
    if (scene == null) return;

    // 1. 找到 Headman Agent 的位置
    Vec3 headmanPos = Vec3.Invalid;
    foreach (Agent agent in Mission.Current.Agents)
    {
        if (!agent.IsHuman || !agent.IsActive()) continue;
        var co = agent.Character as CharacterObject;
        if (co?.HeroObject?.Occupation == Occupation.Headman)
        {
            headmanPos = agent.Position;
            break;
        }
    }
    // 没 Headman → 找 RuralNotable → 兜底用场景中心
    if (!headmanPos.IsValid)
    {
        foreach (Agent agent in Mission.Current.Agents)
        {
            if (!agent.IsHuman || !agent.IsActive()) continue;
            var co = agent.Character as CharacterObject;
            if (co?.HeroObject?.Occupation == Occupation.RuralNotable)
            {
                headmanPos = agent.Position;
                break;
            }
        }
    }
    if (!headmanPos.IsValid)
        headmanPos = Agent.Main?.Position ?? scene.GetScenePosition();

    // 2. 在 Headman 附近找寻路可达的位置（3-5 米范围内）
    Vec3 chestPos = headmanPos;
    scene.GetAccessiblePointNearPosition(headmanPos, 3f, out Vec3 accessiblePoint);
    if (accessiblePoint.IsValid)
        chestPos = accessiblePoint;
    else
        chestPos = headmanPos + new Vec3(2f, 0f, 0f);  // 兜底偏移

    // 3. 创建箱子实体
    MatrixFrame frame = new MatrixFrame(Mat3.Identity, chestPos);
    _chestEntity = GameEntity.Instantiate(scene, "chest_wooden", frame);
    if (_chestEntity == null)
        _chestEntity = GameEntity.Instantiate(scene, "chest_a", frame);
    if (_chestEntity == null)
    {
        // 兜底：空实体（看不见但能交互，后续可替换为其他 prefab）
        _chestEntity = GameEntity.CreateEmpty(scene);
        _chestEntity.SetGlobalFrame(frame);
    }
    
    // 回填给 StealManager
    StealManager.ChestEntity = _chestEntity;
    DebugLogger.Log($"[Chest] Spawned at {chestPos}, gold={StealManager.StashGold}, items={StealManager.ChestItemRoster?.Count ?? 0}");
}
```

### 箱子互动检测（在 HandleInput 里）

```csharp
// 检测是否对着箱子按 F
if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F) && _chestEntity != null)
{
    float dist = Agent.Main.Position.Distance(_chestEntity.GetGlobalFrame().origin);
    if (dist < 3f)
    {
        OpenChest();
        return;
    }
}
```

### OpenChest 方法

```csharp
private void OpenChest()
{
    int gold = StealManager.StashGold;
    var roster = StealManager.ChestItemRoster;
    
    if (gold == 0 && (roster == null || roster.IsEmpty()))
    {
        InformationManager.DisplayMessage(new InformationMessage("箱子是空的。", Colors.Gray));
        return;
    }

    // 目击检测：周围有没有人盯着
    var witnesses = StealManager.GetWitnesses(Agent.Main, null, maxDistance: 15f);
    string riskHint = witnesses.Count > 0 
        ? $"\n⚠ 有 {witnesses.Count} 双眼睛可能看到你！" 
        : "";

    string goldLine = gold > 0 ? $"\n金币: {gold} 第纳尔" : "";
    string itemsPreview = "";
    if (roster != null)
    {
        for (int i = 0; i < Math.Min(roster.Count, 5); i++)
        {
            var item = roster.GetItemAtIndex(i);
            itemsPreview += $"\n  {item.Name} x{roster.GetElementNumber(i)}";
        }
        if (roster.Count > 5) itemsPreview += $"\n  ...还有 {roster.Count - 5} 种物品";
    }

    string content = $"你找到了村庄的保管箱。{goldLine}\n物品:{itemsPreview}{riskHint}";
    
    var settlement = Settlement.CurrentSettlement;
    InformationManager.ShowInquiry(new InquiryData(
        "村庄保管箱", content,
        true, true,
        "全部拿走", "自己挑选",
        () =>
        {
            // 全部拿走
            if (gold > 0)
                StealManager.LootStash(gold, settlement);
            if (roster != null && !roster.IsEmpty())
            {
                var dict = new Dictionary<PartyBase, ItemRoster>();
                dict[PartyBase.MainParty] = roster;
                InventoryManager.OpenScreenAsLoot(dict);
                StealManager.ChestItemRoster = new ItemRoster(); // 清空
            }
            // 箱子空了就移除实体
            if (_chestEntity != null && StealManager.StashGold == 0 && StealManager.ChestItemRoster.IsEmpty())
            {
                _chestEntity.Remove(0);
                _chestEntity = null;
                StealManager.ChestEntity = null;
            }
        },
        () =>
        {
            // 自己挑选
            if (gold > 0)
                StealManager.LootStash(gold, settlement);
            if (roster != null && !roster.IsEmpty())
            {
                var dict = new Dictionary<PartyBase, ItemRoster>();
                dict[PartyBase.MainParty] = roster;
                InventoryManager.OpenScreenAsLoot(dict);
            }
        }));
}
```

### 清理（OnMissionScreenFinalize）

```csharp
if (_chestEntity != null)
{
    _chestEntity.Remove(0);
    _chestEntity = null;
}
StealManager.ChestEntity = null;
StealManager.ChestItemRoster = new ItemRoster();
_chestSpawned = false;
```

---

## 涉及文件（4 个）
1. **修改**：`Stealth/StealManager.cs` — 财富分配模块 + 箱子数据 + 摸包偷钱
2. **修改**：`Interaction/InteractionMissionView.cs` — 场景钩子 + LootAgent + 箱子生成 + 箱子互动 + 模板 NPC 开面板
3. **修改**：`Interaction/NpcInfoVM.cs` — 金钱显示 + 模板 NPC 兼容
4. **修改**：`GUI/Prefabs/NPCInfoBoard.xml` — UI 加金钱行

## 验证
1. 进繁荣城镇 → Headman 旁边出现箱子，里面有金币+物资
2. 按 F 开箱子 → 能看到预期金币和物品列表 + 目击风险提示
3. 偷箱子时有人看到 → NPC 质问（走现有 WitnessCrime 管线）
4. 偷完箱子变空 → 实体消失
5. 按 H 看 NPC → 面板显示身上带的钱
6. 摸包偷装备同时摸到零钱
7. 搜刮尸体出完整分配额
8. 金库/ItemRoster 在偷后真实减少
