# AIInfluenceProject 技术实现分析

## 一、核心架构概览

```
Bannerlord 引擎
    ↓
CampaignBehaviorBase (官方基类)
    ↓
AIInfluenceBehavior (Mod 主入口)
    ├── NPCInitiativeSystem (NPC主动性)
    ├── DialogManager (对话管理)
    ├── DiplomacyManager (外交管理)
    ├── AIActionManager (行为管理)
    ├── Information (信息传播)
    └── ... (数十个子系统)
```

---

## 二、十大亮点及核心接口

### 1. NPC 主动性系统 (NPCInitiativeSystem)

**亮点：**
- NPC 会主动接近玩家发起对话
- 基于关系、情绪、目标决定行为
- 定时检查机制，非事件触发

**核心实现接口：**

```csharp
// 继承 Bannerlord 官方行为基类
public class AIInfluenceBehavior : CampaignBehaviorBase
{
    // 每游戏小时触发一次
    public override void RegisterEvents()
    {
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
    }
    
    private void OnHourlyTick()
    {
        // 检查哪些 NPC 应该主动找玩家
        _npcInitiativeSystem.CheckInitiatives();
    }
}

// NPC 主动性检查
public class NPCInitiativeSystem
{
    public void CheckInitiatives()
    {
        foreach (var npc in GetNearbyNPCs())
        {
            if (ShouldInitiate(npc))
            {
                // 创建主动交互
                CreateInitiative(npc);
            }
        }
    }
    
    // 决策算法：关系 + 情绪 + 目标
    private bool ShouldInitiate(Hero npc)
    {
        float score = 0;
        score += GetRelationScore(npc);      // 关系分数
        score += GetEmotionScore(npc);       // 情绪分数
        score += GetGoalScore(npc);          // 目标分数
        return score > INITIATIVE_THRESHOLD;
    }
}
```

**关键 Bannerlord 接口：**
- `CampaignBehaviorBase` - 行为基类
- `CampaignEvents.HourlyTickEvent` - 定时事件
- `Hero` - NPC 英雄对象
- `Agent` - 场景中的角色实体

---

### 2. 智能对话系统 (DialogManager)

**亮点：**
- LLM 实时生成对话内容
- 对话影响游戏状态
- 上下文记忆

**核心实现接口：**

```csharp
public class DialogManager
{
    // 对话启动接口
    public void StartDialog(Hero npc, Hero player, DialogContext context)
    {
        // 构建提示词
        string prompt = BuildPrompt(npc, player, context);
        
        // 调用 LLM
        string response = CallLLM(prompt);
        
        // 解析响应
        var dialogData = ParseResponse(response);
        
        // 显示对话 UI
        ShowDialogUI(npc, dialogData);
    }
    
    // 玩家输入处理
    public void OnPlayerInput(string input)
    {
        // 发送到 LLM
        // 获取 NPC 回应
        // 更新游戏状态
        UpdateGameState(response);
    }
}

// 对话上下文
public class DialogContext
{
    public Hero NPC { get; set; }
    public Hero Player { get; set; }
    public string CurrentTopic { get; set; }
    public List<string> History { get; set; }  // 对话历史
    public Dictionary<string, object> Memory { get; set; }  // NPC 记忆
}
```

**关键 Bannerlord 接口：**
- `CampaignGameStarter` - 注册对话
- `ConversationSentence` - 对话句子
- `MissionMode` - 任务模式切换
- `InformationManager` - 显示消息

---

### 3. 信息传播系统 (Information)

**亮点：**
- 消息在 NPC 间传播
- 有延迟和失真
- 影响 NPC 对玩家的态度

**核心实现接口：**

```csharp
public class Information
{
    // 信息节点
    public class InfoNode
    {
        public string Topic { get; set; }           // 主题
        public Hero Source { get; set; }            // 信息源
        public List<Hero> KnownBy { get; set; }     // 谁知道
        public float Credibility { get; set; }      // 可信度
        public CampaignTime Time { get; set; }      // 时间戳
    }
    
    // 传播信息
    public void SpreadInformation(InfoNode info, Hero from, Hero to)
    {
        // 计算传播概率
        float chance = CalculateSpreadChance(from, to);
        
        if (Random.Next() < chance)
        {
            // 信息可能失真
            var newInfo = DistortInformation(info);
            to.LearnInformation(newInfo);
        }
    }
    
    // 每游戏日传播
    public void DailySpread()
    {
        foreach (var info in _activeInfos)
        {
            foreach (var knower in info.KnownBy)
            {
                // 找到社交圈
                var contacts = GetSocialContacts(knower);
                
                foreach (var contact in contacts)
                {
                    SpreadInformation(info, knower, contact);
                }
            }
        }
    }
}
```

**关键 Bannerlord 接口：**
- `CampaignTime` - 游戏时间
- `Hero.GetRelation` - 获取关系值
- `Settlement` - 定居点（传播场所）
- `MobileParty` - 移动队伍（传播载体）

---

### 4. 动态外交系统 (DiplomacyManager)

**亮点：**
- AI 自主外交决策
- 考虑多方因素
- 玩家可以影响

**核心实现接口：**

```csharp
public class DiplomacyManager
{
    // AI 外交决策
    public void MakeDiplomaticDecision(Kingdom kingdom)
    {
        var options = GetDiplomaticOptions(kingdom);
        
        foreach (var option in options)
        {
            float score = EvaluateOption(kingdom, option);
            option.Score = score;
        }
        
        // 选择最高分
        var best = options.OrderByDescending(o => o.Score).First();
        ExecuteDecision(kingdom, best);
    }
    
    // 评估选项
    private float EvaluateOption(Kingdom kingdom, DiplomaticOption option)
    {
        float score = 0;
        
        // 军事实力
        score += GetMilitaryStrength(kingdom) * 0.3f;
        
        // 经济利益
        score += GetEconomicBenefit(option) * 0.25f;
        
        // 历史关系
        score += GetHistoricalRelation(option) * 0.2f;
        
        // 国内稳定
        score += GetDomesticStability(kingdom) * 0.15f;
        
        // 随机因素
        score += Random.Next(-10, 10) * 0.1f;
        
        return score;
    }
}

// 外交行为
public enum DiplomaticAction
{
    DeclareWar,      // 宣战
    MakePeace,       // 议和
    FormAlliance,    // 结盟
    BreakAlliance,   // 背盟
    ProposeMarriage  // 提亲
}
```

**关键 Bannerlord 接口：**
- `Kingdom` - 王国对象
- `Diplomacy` - 外交系统
- `DeclareWarAction` - 宣战行为
- `MakePeaceAction` - 议和行为

---

### 5. 婚姻系统 (MarriageSystem)

**亮点：**
- AI 自动撮合
- 政治考量
- 玩家可以干预

**核心实现接口：**

```csharp
public class MarriageSystem
{
    // 寻找配偶
    public Hero FindSpouse(Hero hero)
    {
        var candidates = GetMarriageCandidates(hero);
        
        Hero bestMatch = null;
        float bestScore = 0;
        
        foreach (var candidate in candidates)
        {
            float score = CalculateMarriageScore(hero, candidate);
            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = candidate;
            }
        }
        
        return bestMatch;
    }
    
    // 计算婚姻匹配度
    private float CalculateMarriageScore(Hero a, Hero b)
    {
        float score = 0;
        
        // 政治价值
        score += GetPoliticalValue(b) * 0.4f;
        
        // 年龄匹配
        score += GetAgeCompatibility(a, b) * 0.2f;
        
        // 关系好感
        score += GetRelationScore(a, b) * 0.2f;
        
        // 财富地位
        score += GetWealthScore(b) * 0.2f;
        
        return score;
    }
    
    // 执行婚姻
    public void ExecuteMarriage(Hero a, Hero b)
    {
        MarriageAction.Apply(a, b);
        
        // 更新外交关系
        UpdateDiplomaticRelations(a.Clan, b.Clan);
        
        // 触发事件
        CampaignEventDispatcher.Instance.DispatchEvent(new MarriageEvent(a, b));
    }
}
```

**关键 Bannerlord 接口：**
- `MarriageAction` - 婚姻行为
- `Hero.Spouse` - 配偶属性
- `Clan` - 家族对象

---

### 6. 动态事件系统 (DynamicEventsManager)

**亮点：**
- 随机生成世界事件
- 连锁反应
- 玩家可干预

**核心实现接口：**

```csharp
public class DynamicEventsManager
{
    // 事件定义
    public class WorldEvent
    {
        public string EventId { get; set; }
        public EventType Type { get; set; }
        public Settlement Location { get; set; }
        public float Severity { get; set; }
        public CampaignTime StartTime { get; set; }
        public List<WorldEvent> Consequences { get; set; }
    }
    
    // 每日检查
    public void DailyEventCheck()
    {
        // 随机触发事件
        if (Random.Next() < EVENT_CHANCE)
        {
            var newEvent = GenerateRandomEvent();
            TriggerEvent(newEvent);
        }
        
        // 更新进行中的事件
        foreach (var evt in _activeEvents)
        {
            UpdateEvent(evt);
        }
    }
    
    // 生成事件
    private WorldEvent GenerateRandomEvent()
    {
        var type = GetRandomEventType();
        
        switch (type)
        {
            case EventType.Plague:
                return CreatePlagueEvent();
            case EventType.Famine:
                return CreateFamineEvent();
            case EventType.Rebellion:
                return CreateRebellionEvent();
            default:
                return null;
        }
    }
    
    // 触发连锁反应
    private void TriggerConsequences(WorldEvent evt)
    {
        foreach (var consequence in evt.Consequences)
        {
            DelayedTaskManager.Instance.AddTask(() => {
                TriggerEvent(consequence);
            }, consequence.DelayDays);
        }
    }
}
```

**关键 Bannerlord 接口：**
- `Settlement` - 定居点
- `ChangeRelationAction` - 改变关系
- `DestroyPartyAction` - 摧毁队伍

---

### 7. 死亡与继承系统 (DeathHistoryBehavior)

**亮点：**
- 记录所有死亡
- 影响继承
- 引发政治变动

**核心实现接口：**

```csharp
public class DeathHistoryBehavior
{
    // 死亡记录
    public class DeathRecord
    {
        public Hero Deceased { get; set; }
        public CampaignTime Time { get; set; }
        public DeathCause Cause { get; set; }
        public Hero Killer { get; set; }
        public bool IsNatural { get; set; }
    }
    
    // 记录死亡
    public void RecordDeath(Hero hero, DeathCause cause, Hero killer = null)
    {
        var record = new DeathRecord
        {
            Deceased = hero,
            Time = CampaignTime.Now,
            Cause = cause,
            Killer = killer
        };
        
        _deathRecords.Add(record);
        
        // 处理继承
        HandleInheritance(hero);
        
        // 触发政治变动
        TriggerPoliticalChanges(hero);
    }
    
    // 处理继承
    private void HandleInheritance(Hero deceased)
    {
        var heir = FindHeir(deceased);
        
        if (heir != null)
        {
            // 转移领地
            TransferFiefs(deceased, heir);
            
            // 转移财富
            TransferWealth(deceased, heir);
            
            // 更新家族领导
            if (deceased == deceased.Clan.Leader)
            {
                ChangeClanLeader(deceased.Clan, heir);
            }
        }
    }
}
```

**关键 Bannerlord 接口：**
- `Hero.IsDead` - 死亡状态
- `KillCharacterAction` - 杀死角色
- `ChangeClanLeaderAction` - 更换家族领袖

---

### 8. 任务系统 (TaskSystem)

**亮点：**
- AI 任务规划
- 延迟执行
- 优先级管理

**核心实现接口：**

```csharp
public class TaskManager
{
    // 任务定义
    public class AITask
    {
        public string TaskId { get; set; }
        public TaskType Type { get; set; }
        public Hero Assignee { get; set; }
        public CampaignTime StartTime { get; set; }
        public CampaignTime Deadline { get; set; }
        public TaskPriority Priority { get; set; }
        public Dictionary<string, object> Parameters { get; set; }
    }
    
    // 添加任务
    public void AddTask(AITask task)
    {
        _taskQueue.Enqueue(task, task.Priority);
    }
    
    // 每日处理
    public void ProcessTasks()
    {
        while (_taskQueue.Count > 0)
        {
            var task = _taskQueue.Dequeue();
            
            if (IsTaskValid(task))
            {
                ExecuteTask(task);
            }
        }
    }
    
    // 执行任务
    private void ExecuteTask(AITask task)
    {
        switch (task.Type)
        {
            case TaskType.AttackSettlement:
                ExecuteAttackTask(task);
                break;
            case TaskType.DefendSettlement:
                ExecuteDefendTask(task);
                break;
            case TaskType.RecruitTroops:
                ExecuteRecruitTask(task);
                break;
        }
    }
}

// 延迟任务管理器
public class DelayedTaskManager
{
    public void AddTask(Action action, float delayDays)
    {
        var executeTime = CampaignTime.Now + CampaignTime.Days(delayDays);
        _delayedTasks.Add(new DelayedTask(action, executeTime));
    }
    
    public void Tick()
    {
        foreach (var task in _delayedTasks.Where(t => t.ExecuteTime <= CampaignTime.Now))
        {
            task.Action();
        }
    }
}
```

**关键 Bannerlord 接口：**
- `MobileParty` - 移动队伍
- `SetPartyAiAction` - 设置 AI 行为
- `CampaignTime` - 游戏时间

---

### 9. 世界事件广播 (WorldEventsUILayer)

**亮点：**
- 实时显示世界新闻
- 玩家成为焦点
- 可过滤和搜索

**核心实现接口：**

```csharp
public class WorldEventsUILayer : GauntletLayer
{
    // 事件条目
    public class WorldEventEntry
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public CampaignTime Time { get; set; }
        public EventImportance Importance { get; set; }
        public Sprite EventIcon { get; set; }
    }
    
    // 添加事件
    public void AddEvent(WorldEventEntry entry)
    {
        _events.Add(entry);
        
        // 按重要性排序
        SortEvents();
        
        // 刷新 UI
        RefreshUI();
        
        // 显示通知
        if (entry.Importance >= EventImportance.High)
        {
            ShowNotification(entry);
        }
    }
    
    // 广播事件
    public void BroadcastEvent(string title, string desc, EventImportance importance)
    {
        var entry = new WorldEventEntry
        {
            Title = title,
            Description = desc,
            Time = CampaignTime.Now,
            Importance = importance
        };
        
        AddEvent(entry);
    }
}
```

**关键 Bannerlord 接口：**
- `GauntletLayer` - UI 层基类
- `ScreenBase` - 屏幕基类
- `InformationManager` - 信息管理器

---

### 10. 设置系统 (ModSettings)

**亮点：**
- 可配置所有参数
- 实时生效
- 保存加载

**核心实现接口：**

```csharp
public class ModSettings : AttributeGlobalSettings<ModSettings>
{
    // AI 频率
    [SettingPropertyInteger("AI 思考频率(小时)", 1, 24)]
    public int AITickInterval { get; set; } = 6;
    
    // LLM 设置
    [SettingPropertyString("API Key")]
    public string LLMApiKey { get; set; } = "";
    
    [SettingPropertyString("API URL")]
    public string LLMApiUrl { get; set; } = "https://api.openai.com/v1/chat/completions";
    
    // 游戏性设置
    [SettingPropertyBool("启用 NPC 主动性")]
    public bool EnableNPCInitiative { get; set; } = true;
    
    [SettingPropertyBool("启用信息传播")]
    public bool EnableInformationSpread { get; set; } = true;
    
    [SettingPropertyFloatingInteger("事件频率", 0f, 1f)]
    public float EventFrequency { get; set; } = 0.1f;
    
    // 设置变更回调
    public override void OnPropertyChanged(string propertyName)
    {
        base.OnPropertyChanged(propertyName);
        
        // 实时应用设置
        ApplySetting(propertyName);
    }
}
```

**关键 Bannerlord 接口：**
- `AttributeGlobalSettings` - MCM 设置基类
- `SettingPropertyInteger` - 整数设置
- `SettingPropertyBool` - 布尔设置
- `SettingPropertyFloatingInteger` - 浮点设置

---

## 三、关键技术总结

### Bannerlord 引擎核心接口

| 类别 | 接口 | 用途 |
|------|------|------|
| **行为基类** | `CampaignBehaviorBase` | Mod 主入口 |
| **定时事件** | `CampaignEvents.HourlyTickEvent` | 定时检查 |
| **游戏时间** | `CampaignTime` | 时间管理 |
| **角色** | `Hero` / `Agent` | NPC/玩家 |
| **势力** | `Kingdom` / `Clan` | 王国/家族 |
| **定居点** | `Settlement` | 城镇/村庄 |
| **队伍** | `MobileParty` | 军队/商队 |
| **行为** | `XXXAction` | 游戏行为 |
| **UI** | `GauntletLayer` | 界面系统 |
| **设置** | `AttributeGlobalSettings` | MCM 配置 |

### 数据流架构

```
游戏事件
    ↓
AIInfluenceBehavior (捕获事件)
    ↓
各子系统处理 (决策)
    ↓
LLM 生成内容 (可选)
    ↓
执行游戏行为 (Action)
    ↓
更新游戏状态
    ↓
广播世界事件
```

### 关键设计模式

1. **单例模式** - `AIInfluenceBehavior.Instance`
2. **观察者模式** - `CampaignEvents` 事件监听
3. **策略模式** - 不同的 `AITask` 执行策略
4. **工厂模式** - `GenerateRandomEvent()`
5. **责任链模式** - 信息传播链

---

## 四、实现建议

### 优先级 1：必须实现

1. **主行为类**
```csharp
public class MyModBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        // 注册定时事件
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
    }
}
```

2. **初始化入口**
```csharp
public class MySubModule : MBSubModuleBase
{
    protected override void OnSubModuleLoad()
    {
        // 注册行为
        CampaignGameStarter starter = ...;
        starter.AddBehavior(new MyModBehavior());
    }
}
```

### 优先级 2：推荐实现

- NPC 主动性检查
- 基础信息传播
- 设置面板 (MCM)

### 优先级 3：可选实现

- 完整外交系统
- 复杂事件链
- 世界广播 UI

---

*报告生成时间：2026年3月11日*
