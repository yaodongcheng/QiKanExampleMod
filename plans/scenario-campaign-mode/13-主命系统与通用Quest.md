# 13 — 主命系统与通用 Quest 框架

> 阶段：Phase 1 ｜ 依赖：01（剧本引擎）、现有 Quest 代码 ｜ 完成后：五套并存的任务机制整合成一套"数据驱动的通用任务"，主命在其上运行
>
> **审核状态：⏳ 未审核（禁止实施）** ｜ 审核人：用户 ｜ 审核日期：— ｜ 审核意见：—
> 通过后改为 ✅ 已审核（日期）+ 记录意见。

## 这一步做什么（一句话）

项目里现在有**五套任务机制并存**，互相不知道对方：原版 40 种委托、我们的委托系统（CommissionQuest）、**旧主命系统（GenericQuest，已废弃但数据模型很好）**、因果链引擎（QuestConsequenceResolver，JSON 驱动）、剧本引擎要用的剧情阶段任务。本 plan 把它们**整合成一个"数据驱动的通用任务框架"**，主命作为其中一种来源跑在上面。

## 现状盘点（已读代码确认）

| 机制 | 现状 | 能复用什么 |
|---|---|---|
| 原版 40 种 Issue/Quest | 正常运行，直接复用 | 完整任务管线 |
| CommissionQuest（委托） | 16 种裁到 3（越狱/偷盗/搜刮） | 委托语义 |
| **GenericQuest（旧主命）** | 🔴 已废弃（Obsolete）但代码完整：`QuestType` 30 种主命类型（筹粮/军马/铁炮/资金/讨伐/征兵/训练/掠夺/占领/开发/外交/侦查/破坏/人才/劝诱/修业/竞技场/护送/承诺等）+ `QuestData` 通用目标字段（Type/TargetId/TargetCount/TargetHero/TargetSettlementId/本金/物资）+ 完整的事件监听/进度/奖惩实现 | 🔴 **主命数据模型和实现范本现成**，注释写明"等主命系统正式重构（Phase C）时迁移"——就是本 plan |
| QuestConsequenceResolver（因果链） | ✅ 已实现，JSON 因果表驱动（完成 → 查表 → 生成后续事件/任务） | 因果链条 |
| ScenarioQuest（剧本阶段） | 本工程新增 | 剧本阶段载体 |

## 通用 Quest 设计（统一模型）

**一个任务 = 一段数据 + 多阶段链**（QuestDef JSON），不写死在代码里：

```jsonc
{
  "id": "master_order_theft",            // 任务模板 ID
  "type": "theft",                       // 任务类型（复用 QuestType 30 种 + 扩展）
  "giver": { "relation": "lord" },       // 派发者（主公/委托NPC/剧情角色/因果）（顶层 = 派发者描述对象）
  "source": "master_order",              // 来源：主命 / 委托 / 剧情阶段 / 因果链
  "durationDays": 30,                    // 总期限
  "given": { "gold": 500 },              // 主公给的本金/物资
  "stages": [                            // 🔴 多阶段链（04 阶段化任务链的落地）：
    { "name": "接受委托",                //   任务面板显示的名字
      "kind": "dialog",                  //   表现方式：dialog/travel/operation/battle/cutscene...
      "target": { "heroId": "target_npc" }, "next": 1 },
    { "name": "前往目标地点",
      "kind": "travel",
      "target": { "settlementId": "town_xxx" }, "next": 2 },
    { "name": "偷取物品",
      "kind": "operation",               //   操作型阶段（偷窃检定）
      "target": { "itemId": "xx", "count": 1 },
      "progress": { "listen": ["inventory_exchange"], "daily": false }, "next": 3 },
    { "name": "返回提交",
      "kind": "dialog",
      "giver": "lord",                            // （阶段内 = 上交对象标识字符串）
      "reward": { "gold": 1000, "relation": 5 }, "next": null }
  ],
  "onTimeout": { "relation": -15 },      // 超时惩罚（有本金贪污更重）
  "next": [ "quest_grain_2" ]            // 完成后走因果链表
}
```

**核心原则**：
- **QuestDef 数据驱动**（像因果链那样 JSON 定义），代码侧只有一个统一的任务类（UnifiedQuest : QuestBase）负责执行
- 🔴 **任务 = 阶段链**：stages 数组（每阶段：名字/表现方式 kind/目标/进度监听/下一阶段），完成当前解锁下一个——任务面板随时显示"当前阶段"，玩家每一步都知道去哪、干什么（对应 04 的阶段化任务链）
- **目标/进度/奖惩通用化**：QuestData 的通用字段设计保留（TargetId/Count/Hero/Settlement/StartValue/Given*），补上"阶段链 + 进度监听声明 + 完成后后续"
- **五套并入一套**：主命（GenericQuest 复活重构）→ UnifiedQuest；委托（CommissionQuest）→ 委托能力迁入统一框架；剧情阶段（ScenarioQuest）→ 也是 UnifiedQuest 的一种 source；原版 40 种**不动**（引擎原生，复用即可）；因果链（QuestConsequenceResolver）→ 保留，作为"完成后后续"的执行器

## 主命系统设计（跑在通用框架上）

- **主命池**：剧本数据 `orders` 表（哪些任务模板可用、什么阶段解锁、谁派发）——太阁式：主公**每月**派 1-2 条
- **派发**：剧本节拍（05 的导演标注覆盖层/演出调度调起）→ 从主命池挑（按阶段/势力关系/玩家状态）→ 生成 UnifiedQuest（source=master_order，giver=主公）
- **形态**：任务面板可见（标题/目标/期限/报酬）；接了给本金/物资（主公的信任）；完成回报（对话上交）；超时/失败扣关系（贪污本金更重）
- **接/拒**：拒绝有代价（关系/剧本推进变慢）但**不强迫**（设计哲学：给压力不剥夺选择）
- **与剧本的关系**：主命是"可选推进器"——完成主命加速剧本走向（触发条件提前）；不接等时间窗自然成熟

## 迁移路径（分三步，每步可独立验证）

1. **UnifiedQuest 落地**：新任务类 + QuestDef 解析 + 通用目标/进度/奖惩执行（复用 GenericQuest 的事件监听实现——它的代码基本就是范本，只是把 QuestData 换成 QuestDef）
2. **主命复活**：GenericQuest → UnifiedQuest（source=master_order）；主命池数据接入剧本引擎 orders 表
3. **委托并入**：CommissionQuest 语义迁到 UnifiedQuest（source=commission）；原版 40 种保持不动

## 分步骤清单

| 步骤 | 做什么 | 完成标志 |
|---|---|---|
| 1 | QuestDef 格式（先冻结）+ 解析 | 样例任务数据能解析 |
| 2 | UnifiedQuest 执行器（🔴 阶段链执行：每阶段目标/表现方式/进度监听/解锁下一阶段 + 奖惩，复用 GenericQuest 实现） | 控制台能创建并走完一个多阶段任务（接受→前往→执行→提交，任务面板显示当前阶段；完成/超时） |
| 3 | 主命池数据（orders 表）+ 节拍派发（接 01 事件引擎（trigger 注册）） | 演示剧本：主公派发主命 → 任务面板可见 → 接/拒双路径 |
| 4 | 完成回报（对话上交）+ 惩罚（超时/失败/贪污） | 回报拿报酬；超时扣关系（有本金扣更多） |
| 5 | 因果链接入（完成后走 QuestConsequenceResolver） | 完成主命 → 按因果表生成后续事件/任务 |
| 6 | 剧本阶段任务并入（ScenarioQuest → UnifiedQuest source=scenario） | 剧情阶段在任务面板正常显示/推进 |
| 7 | 委托并入（CommissionQuest → UnifiedQuest source=commission） | 委托任务走统一管线 |

## 做完怎么验收

> 🔴 分拆验证（12 总纲 A8）：`custom.quest_spawn <questId>` → `custom.quest_complete` / `custom.quest_fail`（含超时、拒绝路径、minTitle 过滤（按最低身份限制过滤）、功勋报酬）；拼装验收 = 12 Phase 1。分拆全绿前不拼装。

1. 五套并存的现状 → 一套通用框架（原版 40 种除外，保持不动）
2. 主命全流程：派发 → 接 → 做（进度条）→ 回报（报酬）→ 超时/失败（惩罚）→ 完成后走因果链
3. 主命拒绝有代价但不强迫；完成加速剧本走向
4. 剧情阶段/委托都跑在统一框架上（任务面板/存档统一）
5. 存档：进行中的任务存读档保持

## 要注意的坑

- **GenericQuest 的旧存档兼容**：旧档里有进行中的 GenericQuest，读存档还原任务时需要旧类结构壳（只为读旧存档保留，不挂业务）——迁移时保留壳（注释已写明），别删类
- QuestType 30 种主命类型是日式语义（铁炮/军资金）——QuestDef 里保留为"任务模板库"，题材名走数据包，代码无日式字串
- 进度监听声明化（QuestDef 里声明监听哪些事件）——避免像 GenericQuest 那样按类型 switch 写死监听
- 任务上限：通用框架要防任务刷屏（主命 1-2 条 + 委托 + 剧情阶段，控制总量）
