# 已造轮子速查（索引）

> **总纲：加功能前先查本表。命中就复用，不要重写。**
> 本文件是**索引**：先按任务定位域 → 打开 `wheels.d/<域>.md` 读细则（每卷 20–670 行）。
> **只加载命中域的卷，不要整卷全读**——避免把几十万 token 全塞进上下文。
> 路径均相对 `ExampleModVS/ExampleMod/ExampleMod/`。签名为核实后的真实签名。

| 域 | 分卷 | 涵盖内容 |
|----|------|---------|
| 配置 / 双配置(MCM) / 版本兼容 | [wheels.d/config.md](wheels.d/config.md) | Settings 单例与世界观、MCMSettings（MCM 排序渲染坑）、DisabledInteractionMissionModes、设计数据 CSV、Emotion↔台词模板一致性、日志、控制台调试指令、Settings 新增开关、VersionCompat 三锚点（含 **InventoryManager→InventoryScreenHelper 改名表**） |
| Agent 行为 / 警戒 / 移动 | [wheels.d/agent.md](wheels.d/agent.md) | AgentControlHelper 动作走位、🔴 **引擎蹲姿（SetCrouchMode = 玩家 Z 键同机制，非 SetPose）**、🔴 **移动目标分派（找 agent → FollowAgentAction，坐标点 → MoveToPositionAction）**、AgentBrain 行为队列、儿童 human_child、SetPartyAiAction、SetScriptedPosition、战斗回调职责、警戒值系统与字段、AlertTypes、NpcSightSystem 清理 / **感知目标注册链（🔴 追加行为拿不到 OnBehaviorInitialize 的坑）** |
| NPC 互动意图 | [wheels.d/intents.md](wheels.d/intents.md) | 意图引擎（Evaluate/Goal/…）、IntentBase 新 API（NPC 平权）、BuildForNpc、IntentRegistry、Intent Tactic × ActionParam、NPC 主动意图、InteractionOptionType、SettlementHonorStore |
| 对话 / 叙事注入 | [wheels.d/dialogue.md](wheels.d/dialogue.md) | 对话注入铁律、v2 新模型（Transition 路由）、CrimeDialogueBuilder（辅助方法/自包含/拦截）、AckNode 与 Transition 检定纪律、KCD2 轮次对话、NpcSpeechResolver（XML 模板台词）、PlaceholderResolver、大世界对话接入、延迟操作 |
| 🔴 对话会话三件套 | [wheels.d/dialogue-session.md](wheels.d/dialogue-session.md) | 🔴 **双层边界与混合在场**（§0 必读：管道看渠道/认知共用/表现按在场裁剪）、**SpeechChannel 说话并联**（优先级/队列/闸门/线程安全/战斗喊话，全量收编 25 处直接 AgentSay）、**PersuadeSlot 说服会话**（agree 公式/兑现/打断/plan_decision 回流）、**SessionDialogueTemplates**（分类×职业×档位 key 回落链 + 无 LLM 完整模板会话）、🔴 **CampaignSession 已删除（2026-08-16：私聊劝说/群聊动议 AI 扩张产物——问句误判动议 + 表态回包必丢弃，用户裁决删除）**、ApplyPlan 合并修复、旁观插嘴 |
| 世界事件 / 犯罪 / 赔偿 | [wheels.d/worldevent.md](wheels.d/worldevent.md) | 事件架构（模拟器→数据库→导演）、出生点寻路验证、通知防刷屏、新婚事件姿势、事实派生 API、ActionRecord 记账、赔偿消费点清单、🔴 **GameMenu 多动态选项文本机制（实例级 SetTextVariable，全局表串名/args.Text 替换无效——赎回菜单两连 bug 实录）** |
| Story 命令引擎 | [wheels.d/story.md](wheels.d/story.md) | JSON 脚本剧情演出（CommandManager + StoryEngine） |
| 偷窃 / 动物 | [wheels.d/stealth.md](wheels.d/stealth.md) | 动物识别/偷动物/持久化/价格修正、扒窃盲盒、撬锁片数、储物道具（四件套/复合键/黑名单）、**战利品挑选共享管线（LootFlowSession）**、浮标五色条、双动体、子弹时间、玩家输入冻结 |
| Gauntlet UI / HUD | [wheels.d/ui.md](wheels.d/ui.md) | 双版本 XML 兼容、VM↔XML 同步铁律、HUD 五元素与显隐、AgentHudVM、性能距离分级、原生弹窗面板、富文本、NinjaNotification 书信流、**GauntletLayer 层序表（原生层序实测）**、**贴内容气泡（CoverChildren+MaxWidth）**、**贴底滚动语义（Bottom 对齐聊天流：val=MaxValue 才是贴底 + 锁定态防漂移）** |
| 记忆 / 叙事迁移 | [wheels.d/memory.md](wheels.d/memory.md) | 记忆系统三件套、QuestManager 硬编码字串清理 |
| 存档诊断 | [wheels.d/save.md](wheels.d/save.md) | 🔴 **存档三合一防线 `Debug/SaveGuard.cs`**（2026-08-18 合并）：①字符串超长防护（救档+监控+裁剪）②错误诊断（`SaveErrorReporter`/`SaveSerializeDiagPatch`，新增 Saveable 后遇存档问题的第一取证入口）③**只读属性防护（`SaveFileReadOnlyGuard`：写盘前清 ReadOnly，治 PlatformFileHelperFailure/Access denied）** |
| LLM | [wheels.d/llm.md](wheels.d/llm.md) | LLMService（重试/HttpClient 复用）、**连接失败五原因诊断 + 统一展示（ClassifyFailure/ShowConnectionMessage）**、PromptBuilder（静态 prompt 工厂）、**prompt 静态文本单一事实源（LWN_plan_* XML，py/C# 同源，改 prompt 只改 XML）**、🔴 **【目之所及】在场采样器（楼层聚类 + 分层配额 + 优先级采样 + 己方/主公标记 + 视角/人称纪律）**、🔴 **分兵近况段（BuildSplitPartyAwareness：分兵随从自己的队伍位置/AI 行为/兵力，J3 裁剪只裁主队信息；NearestSettlementName 参数化重载）** |
| IM 传讯 / 群聊 | [wheels.d/im.md](wheels.d/im.md) | 群聊回复管线（延迟调度 + 三层丢弃纪律）、**群聊记忆方案 B（参与度写入）**、回应模式人格化（trait/画像/hash 加权）、事件广播线程模型（🔴 async-over-sync 死锁教训 + 三段式）、选人增强（@提及候选/bigram 相似度/沉寂补偿/随机+保底）、🔴 **决策卡片统一结构**（ImButtonVM/CardButtons 数据驱动按钮行、UpdateCardAnchors 单锚点规则；计划卡片/闲聊动作卡片/NPC 提议/群聊动作同构，含双路径分离 + 防死循环纪律）、🔴 **闲聊动作空间模型（ActionSpace 三态：执行人×目标 Mission 内外裁决）**、🔴 **defender 场景优先解析 + 执行期目标解析同口径**、🔴 **多消息分时投递（SpeechPauseFor 字数挂钩间隔 + 延迟队列）**、🔴 **卡片按钮可见性时序（先 IsCardAnchor 后 AnchorCard + 构建后强制广播）**、🔴 **墙钟驱动（后台任务禁止依赖 CampaignEvents.TickEvent——暂停停发；挂 ImChatView.Tick 双端入口 + WorldBackgroundBehavior 生成任务范式：状态机/快速首检低频重试/指纹失效判定）** |
| 密谋命令系统 | [wheels.d/planner.md](wheels.d/planner.md) | LLM 计划生成 + PlanExecutor 确定性执行（四件套：语法/世界状态/执行器/ReactiveAgent）、Plot 玩法行、plan_debug、Replan、执行摘要 HUD、🔴 **检定成功率公式（d20 风格：掷点≥门槛，ratio 式 + 模板 Level 估算属性）**、执行期目标解析（快照五层匹配）、🔴 **击晕单管线（KnockoutFlow：玩家/NPC 平权范本，判定+结算共享、壳留节奏与播报）**、🔴 **免确认瞬时动作（RequiresConfirm=false 白名单 + crouch/stand 范本）**、扒窃绕背走位、🔴 **Agent.Index 目标唯一标记（[#N] prompt 标记 + TryResolveIndexedTarget 解析链 + 计划轮【目标指认】段）**、🔴 **Party 动作 IsValid/Execute attacker 侧 dual-check（IM 语境 defender 恒为玩家——执行者是队伍成员自己的动作必须补 attacker 侧，范本 gather_to_player/party_patrol 归队巡逻 + move_to 玩家目标跟随兜底）** |
| 按键映射 / 输入 | [wheels.d/input.md](wheels.d/input.md) | 输入三件套、当前映射表（改键唯一入口）、设备检测原理、UI 按键提示接入范式、🔴 模态门控（IM 面板/弹窗打开期间玩法行暂停） |
| 🔴 废弃系统（别碰） | [wheels.d/deprecated.md](wheels.d/deprecated.md) | **旧对话 UI（StoryDialogVM/DialogChoice）+ 旧切磋 UI（DuelMissionView）**——已废弃勿加功能；IM 弹窗确认回调禁调 `_vm.Close()`（触发旧链 OnDialogClosed → GenerateEventAsync 必崩，实机 11:13:37）；现行对话 = 原版对话流 + IM/AgentSay，切磋 = CombatManager |

**登记新轮子**：进 `wheels.d/` 对应域文件追加条目（解决什么问题 + 关键签名 + 调用范例 + 文件路径，与现有格式一致）。域归属拿不准时先问，或就近放入最相关的卷。
