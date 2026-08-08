# 密谋命令系统 — badcase 台账 + prompt 纪律注册表（迭代循环）

> **定位**：与 [llm-goap-plan-execution.md](llm-goap-plan-execution.md)（设计文档）分工——**本文只管迭代**：badcase 台账 + 纪律注册表 + validator 清单。设计文档写"系统长什么样"，本文写"系统踩过哪些坑、怎么防"。
> **新 session 从这里继续**：先读「待办」→ 实机找 badcase → 按格式补录 → 改 prompt（XML）/validator → 跑 `test_llm_plan.py` 回归 → 实机验证。

## 迭代循环（每个 badcase 走完整圈）

```
实机测试 ──发现 badcase──▶ 记台账（现象/日志证据/根因）
   ▲                          │
   │                          ▼
实机验证 ◀── 回归通过 ◀── test_llm_plan.py 回归 ◀── 改 validator（C#+py 双份）/ prompt（XML）
```

**核心原则（改 prompt 前必读）**：
1. **validator 是安全网，prompt 是教材**——结构性违规（词表外/跳转悬空/防抖缺失）→ 进 validator 自动拦截；只有"模型需要理解语义才能做对"的（方向语义/意图判定/保持型概念）→ 留 prompt。
2. **纪律不无限追加**——prompt 越长 = token 成本 + 注意力稀释。能进 validator 的纪律，prompt 侧压缩成一句话即可（教材越长学生越记不住）。
3. **prompt 单一事实源**：静态块只改 `ModuleData/Languages/std_LivingWorldNpcs_prompts.xml`（EN）+ `CNs/` 版（py/C# 双端自动读取）；词表动态拼接段在代码（`check_vocab_sync.py` 校验）。
4. **validator 双份同步**：C# `PlanGrammar.cs`（warning 级）↔ py `test_llm_plan.py` `validate_plan`（issue 级 = 回归失败）。改一边必须改另一边。

---

## 📋 待办（新 session 从这里继续）

**A. 实机验证（先做）**
- [ ] 验证 tickDt 修复 + 纪律 19/20（重启游戏 → "找酒馆老板聊聊" → 随从应 2s 内起步、走到老板面前搭话；心跳日志距目标应动态变化而非恒定）
- [ ] 验证通过 → 把 BC-004 回归状态改为「实机通过」

**B. 验证后收尾**
- [ ] 心跳日志收敛：TickAll + 执行器双心跳（各 1s 一条）是诊断用的——验证完决定挂 `ShowDebugMessages` 开关或删除，别让正式日志每秒刷屏
- [ ] `MoveToPositionAction.OnEnd` 的 `TeleportToPosition` 瞬移：8s 强收尾时当面闪现，对齐 `FollowAgentAction`（不瞬移）
- [ ] wheels.d/planner.md 登记本次产出：tickDt 时间基准纪律、纪律 19/20、`plans/plan-badcases.md` 引用（CLAUDE.md 工作流约定）

**C. 回归遗留 ISSUE（既有问题，非本次引入）**
- [ ] 「赶走 DRIVE_AWAY」on_timeout 谎报 success——**连续两轮稳定复现**，疑似 LLM 对失败路径的系统性缺陷，查 prompt 纪律 12/13 表述
- [ ] 「偷箱子」on_timeout 谎报 success + 分类漂移（CLARIFY/STEAL/LOOKOUT 不稳定）
- [ ] 「传话 DELIVER」分类漂移（一次 WAIT 一次 DELIVER）
- [ ] 「清剿 ANNIHILATE」模型自主拒绝 = 预期内，不修

**D. 通用防线**
- [ ] validator 通用规则：contingency 条件无 sustained 且启动瞬间为真 → 警告（BC-001 seeing / BC-002 following 之后的第三种"开局必真"形态，或直接做）

**E. 后续架构（badcase 循环跑稳后再开，单独 plan）**
- [ ] 执行器收编 AgentBrain 重构——闩锁 5 职责迁移表 + 多 actor 方案 + 接线改动清单，开 `plans/plan-executor-into-brain.md`

**F. 发现新 badcase**
- [x] **BC-006 已补录**（2026-08-08）：目标对 spoken_to 只转头不开口（反应词表缺 respond）

**G. BC-006 修复（已实施 v2+v3，待实机验证）**
- [x] ① 反应词表加 `respond`（`ReactiveAgent.ReactionActionsInPromptOrder`，prompt 自动读到；py `REACTIVE_ACTIONS` 同步）——**v2 方案：respond = 实时 LLM 开口回应**（用户拍板：不用计划期预写 reply，每次说话发 LLM 请求更符合情景）
- [x] ② `LLMService.ChatOnceAsync`（单次请求、2s 超时、失败静默 null；**429 → 10s 全局冷却**）——区别于 ChatAsync（3 次重试 + 弹连接提示），回应请求禁用重试
- [x] ③ **v3 记忆整合（2026-08-08，用户要求整体考虑）**：`StartRespond` 上下文与写入统一走 `AllNpcMemoryManager.GetMemoryForAgent(目标)` 三层记忆（`RecentHistory`/`DynamicMemories`/`PermanentMemory`）——简易 `DialogueHistory` 退役；随从对话写入 `AddHistory(role, "名字: 台词", speakerId)`；上下文 = `PromptBuilder.GetPrompt_RespondContext`（按 SpeakerId 过滤 + 永久截断 200 字 + 动态 2 条）；**演算意图进 prompt**（score → 热情/正常/敷衍态度段）；`ChatMessage.SpeakerId` + `BuildPromptForSummary` 归因泛化（NPC-NPC 总结不再写成"和玩家聊过"）；`SuppressFailureAlerts`（随从对话触发记忆维护失败静默）
- [x] ④ 默认模板补 tavernkeeper/merchant/chief（spoken_to → respond 高权重）
- [x] ⑤ 本地化（EN/CN）：occupation 补 key、trait 描述、respond 模板台词、回应 prompt 骨架（含态度/记忆段）
- [x] ⑥ prompt：`LWN_plan_exec` 执行要求注明 respond；BuildGrammar reactions 说明加 respond
- [x] ⑦ 单句延迟实测（`Scripts/test_respond_latency.py`）：**5/5 达标（avg 1.34s / max 1.42s，2026-08-08 复验）**——2s 预算可行；首次请求偶发 3s+（降级模板兜住）
- [x] ⑨ **v4 对话模式（2026-08-08，用户拍板：计划期只定话题走向、执行期双方实时生成）**：`say_to` 扩展——省略 text、带 `topic` + `outline`（2-5 段走向）= 多轮对话；`SayInlineState` 对话状态机（开场[LLM 生成]→广播[带走向段]→轮询目标记忆等回应→随从续话[LLM 生成]→…→走向完收尾）；随从侧 `GenerateCompanionLine`（2s 预算 + 走向模板 `LWN_plan_chat_fallback` 兜底）；`PlanGrammar.OutlineSegments` JToken 容错 + validator 2-5 段校验（非法退化单句）；prompt 纪律 11 + quality + `LWN_plan_example_chat` 示范（EN/CN）；回归实测 LLM 已生成 `text:null + topic + 4 段 outline` ✓；py validator 同步
- [ ] ⑧ 实机验证：TALK_TO"找酒馆老板聊聊天" → 老板开口回应（LLM 实时台词，态度与演算一致）；老板能接住玩家/随从先前对话（RecentHistory 生效）；**对话模式 4-7 句来回（随从/老板每句 LLM 实时，走向推进自然结束）**；断网/429 → 降级模板无红字；回归后把 BC-006 状态改为「实机通过」
- [x] ⚠️ **429 限流风险（实测发现，已对策）**：连续请求触发网关限流 → `ChatOnceAsync` 429 分支设 10s 全局冷却（冷却期内直接降级模板）+ 记忆维护静默参数（D4）

---

## 纪律注册表（prompt LWN_plan_rules 1-20）

| 编号 | 纪律一句话 | 来源 badcase | validator 拦截 | 回归用例 |
|------|-----------|-------------|---------------|---------|
| 1 | 只允许已定义 action/谓词 | 原始 | ✅ 动作词表 | 全部 |
| 2 | 每步唯一 id | 原始 | ✅ 重复 id | 全部 |
| 3 | 跳转指向真实 id / @abort_gracefully | 原始 | ✅ 悬空跳转 | 全部 |
| 4 | fallbacks 双层、入口被引用 | 原始 | ✅ fallbacks 单层 | 全部 |
| 5 | 顺利路径 steps，失败/意外走 fallbacks/contingencies | 原始 | — | 全部 |
| 6 | 失败出口落到跳转/超时路径 | 原始 | — | 全部 |
| 7 | 目标引用只用场景角色/物件或 query | 原始 | — | 全部 |
| 8 | say_to 前必须有 move_to | 原始 | — | 全部 |
| 9 | 安全窗口加 sustained_s（3s/5s，上限 30s） | BC-001 同源 | ⚠️ 仅 BC-001 形态 | — |
| 10 | 只基于场景可见事实 | 原始 | — | 全部 |
| 11 | say_to text / until 对象 / ask 只允许 follow | 原始 | ✅ | 全部 |
| 12 | 成败收尾两个节点，失败路径指向 fail | 原始 | ✅ 谎报 success | 全部 |
| 13 | wait on_timeout = 条件没等到 = 失败路径 | 原始 | ✅（并入 12） | 全部 |
| 14 | 地点诚实（场景外地点不编造） | 原始 | ✅ zone/point 锚点 | 带路 |
| 15 | 保持型 vs 任务型（无限 wait / goal+success） | 实验①（v15） | — | 望风 |
| 16 | 条件只能写谓词词表（事件词只进 reactions） | 原始 | ✅ 谓词词表 | 全部 |
| 17 | ask:follow 只用于邀请 | 原始 | ✅ ask 校验 | 全部 |
| 18 | contingencies[].then 必须是字符串 | 原始 | ✅ | 全部 |
| 19 | **找目标不靠视野**：seeing 只表达真实视觉语义；掉线检测 = seeing(self,target)+sustained_s | **BC-001** | ✅ seeing-false 无 sustained | 找酒馆老板 |
| 20 | **禁止 following-false contingency**（恒成立必触发） | **BC-002** | ✅ following-false | 找酒馆老板 |

---

## badcase 台账

### BC-001 seeing-false 无防抖开局必杀（2026-08-08 实机）

- **【命令/场景】**「和酒馆店主聊天」（TALK_TO）；酒馆老板 12m 外**背对玩家**站着
- **【日志证据】**（Debug/StoryEngine_RuntimeLog.txt 16:28 会话）
  ```
  16:28:10.792  开始执行计划（TalkTo）
  16:28:10.871  contingency 触发: t8          ← 79ms，第一帧
  16:28:10.873  ▶ 步骤 t8 开始（end_plan）
  16:28:10.875  计划结束（Failed）: 没能找到店主   ← 老板其实在场
  ```
- **【根因】** ① LLM 写 `seeing(tavernkeeper, self, op:false)`——**方向反了**（seeing(A,B)=A 看见 B，应写 seeing(self,target)）② **语义错**：找人不是视觉（执行器走场景查询）③ **无 sustained_s 防抖**：老板背对 → 第一帧成立 → 计划开局即毁，连 move_to 都没执行
- **【修复】** prompt 纪律 19（找目标不靠视野）+ validator：seeing-false 无 sustained_s → 违规（C# warning / py issue）
- **【回归】** `test_llm_plan.py`「去找酒馆老板聊聊，探探口风」→ TALK_TO ✅（2026-08-08 通过）

### BC-002 following-false 恒成立开局必杀（2026-08-08 实机）

- **【命令/场景】**「去找酒馆店主聊聊天」（TALK_TO）；同酒馆场景
- **【日志证据】**（16:45 会话）
  ```
  16:45:51.909  开始执行计划（TalkTo）
  16:45:51.988  contingency 触发: t8          ← 79ms，第一帧
  16:45:51.990  ▶ 步骤 t8 开始（move_to → player）
  16:45:52~08  心跳 17s | 步骤=t8 | 距目标=2.0m 恒定   ← t8 永不完成（叠加 BC-004）
  ```
- **【根因】** LLM 写 `following(player, self, op:false)` = 「玩家跟着随从」——**恒为 false**（玩家从不跟随随从）；且计划启动必然停止跟随。写进 contingency = 第一帧必触发。LLM 连续两轮发明"开局必真"条件（BC-001 seeing → BC-002 following），**通用防线待建**（见下）
- **【修复】** prompt 纪律 20 + validator：following-false → 违规
- **【回归】** 同上用例 ✅（2026-08-08 通过）
- **【后续防线】** 通用规则：contingency 条件无 sustained 且启动瞬间为真 → 警告。等第三种形态出现后实施（避免为猜模式过度设计）

### BC-003 end_plan 内联步骤重入 NRE 崩溃（2026-08-08 实机）

- **【命令/场景】** 任何计划走到 end_plan 即崩溃
- **【日志证据】** 崩溃堆栈：`PlanExecutor.TickCursor` 547 行 `cursor.Inline.Finished`（NullReferenceException）
- **【根因】** `EndPlanInlineState.OnTick` → `ApplyEndPlan` → `Finish` → `ClearSubAction` **把 cursor.Inline 置 null** → 返回后 `cursor.Inline.Finished` 二次解引用。**任何计划到达 end_plan 必崩**（执行器 bug，非 LLM）
- **【修复】** TickCursor 内联驱动：`OnTick` 后判 `cursor.Inline == null → return`（由 IsFinished 收尾）
- **【回归】** 执行器层修复，实机验证（无独立回归用例）

### BC-006 反应词表缺"回应台词"——目标收到 spoken_to 只转头不开口（2026-08-08 实机）

- **【命令/场景】** 「去找酒馆老板聊聊天」（TALK_TO）；随从走到酒馆老板面前冒泡搭话，老板全程无台词
- **【日志证据】**（Debug/StoryEngine_RuntimeLog.txt 17:12 会话；随从 t2/t4 连说两句，老板两次同样表现）
  ```
  17:12:16.387 [Brain-Receive] 酒馆店主(Idx=3) 收到事件 'spoken_to'   ← 事件送达（不是"没收到"）
  17:12:16.389 [Brain-Enqueue] 酒馆店主(Idx=3) 入队 LookAtAction     ← 反应 = listen（LLM 注入 0.8/ignore 0.2）
  17:12:18.387 [Brain-Tick] 酒馆店主(Idx=3) 完成 LookAtAction        ← 看完 2 秒，全程无台词
  ```
- **【根因】** 三层：
  - ① **反应动作词表缺"开口回应"动作（主根因）**——`ReactiveAgent.ReactionActionsInPromptOrder` 15 个动作里，`listen`/`consider` 实现 = `LookAtAction`（只看不说）、`ignore` = 不动，只有 `refuse`/`warn_away` 会开口。LLM 想表达"回应"也无词可用，只能写 listen/ignore → **目标永远不开口**。设计文档明确要求目标对 spoken_to **开口回应**（case B 村长"开口'找我什么事？'"、case I"原地回应'知道了'"、case E"搭话/回应"）——**实现缺口，非设计缺口**（TALK_TO 不是结算型，走反应表不走 §5.5 台本，判据见 §5.1 三种台词来源）
  - ② **LLM 反应计划写 listen**：对 TALK_TO 场景给 tavernkeeper 写 `spoken_to → listen(0.8)/ignore(0.2)`——prompt 无"被搭话要回应"的示范，listen 是词表里最接近回应的动作
  - ③ **默认模板职业缺口**：`DefaultResponses` 只有 guard/villager/drunkard 三职业；tavernkeeper/merchant/chief 无兜底模板——LLM 没写 responses 的 NPC 对 spoken_to 走 `TryHandleEvent` response==null 分支直接 return（仅 asked_to_follow 有 "…" 默认）
- **【修复】**（v2，用户拍板：实时 LLM 而非计划期预写）① 反应词表加 `respond`（`ReactiveAgent` respond 分支 → `StartRespond` 发 LLM 请求：身份/人格 + 主题 + 对话历史 + 对方刚说 → `ChatOnceAsync` 单次 2s 预算 → 结果入队 → `TickAll` 主线程 `FaceToActor` + `AgentSay` 播放）② 降级链：LLM 未配置/超时/失败/回合超限 → 职业模板台词（`LWN_reactive_respond_*`，铁律 1）③ 默认模板补 tavernkeeper/merchant/chief ④ prompt `LWN_plan_exec` 注明"被搭话目标写 respond"；py `REACTIVE_ACTIONS` 同步 ⑤ 单句延迟实测脚本 `Scripts/test_respond_latency.py`（**429 限流中待跑**）
- **【回归】** `test_llm_plan.py` 反应词表校验（respond ∈ REACTIVE_ACTIONS）+ `Scripts/test_respond_latency.py` 2s 预算实测 + 实机 TALK_TO 验证酒馆老板开口回应（LLM 实时台词；超时/限流降级模板）
- **【附注】** 同会话 `[PlanQuality] 预案 t6 仅 1 步（要求 ≥2）` = LLM 质量不达标但未被拦截（质量要求是提示不是硬校验），另案跟进

### BC-004 tickDt 时间基准饿死（2026-08-08 实机，执行器 bug，非 LLM）

- **【命令/场景】** 所有计划通用；实机表现 = **NPC 原地发呆**
- **【日志证据】**（16:45 会话）t8 心跳 17s 距目标=2.0m 恒定；最早三轮会话（15:59/16:19/16:23）日志全部停在"步骤 t1 开始"后零输出
- **【根因】** `TickInner` 100ms 节流通过后把**帧 dt（~16ms）**传给 `_world.Tick`/`TickGuardrails`/`TickCursor` → 子动作内部计时、`StepElapsed`、`sustained_s` 全部 **~6 倍饿死**：
  - 起身延迟 2s → **12.5s**；`_maxTime` 8s → **50s**；步骤 timeout 20s → **125s**；sustained_s 5s → **31s**
  - 观感 = 随从站着不动十几秒（玩家以为是 bug，其实是计时饿死）；t8 卡 2.0m = 到达后浮点边缘 + 50s maxTime 未到
- **【修复】** 节流通过时计算 `tickDt = _tickAccum`（真实经过时间 ≈0.1s）传给下游
- **【回归】** 待实机验证（心跳日志距目标应变动态）

### BC-005 移动时不解除注视锁 → 倒着走路（2026-08-08 实机）

- **【命令/场景】** 计划 move_to 接管随从；此前随从处于跟随停驻态
- **【现象】** 随从身体走向酒馆老板、头却锁着玩家——观感「倒着走路找老板」
- **【根因】** `FollowAgentAction` 停驻态 `SetLookAgent(玩家)`（"停下来看着目标更自然"）不随移动自动解除；`ScriptedMoveToPoint` 从不清理注视 → 计划接管后头锁玩家、身体走目标
- **【修复】** `AgentControlHelper.ScriptedMoveToPoint` 加 `agent.SetLookAgent(null)`（移动命令统一清注视，幂等；停驻态需要看目标的动作自行重设）
- **【回归】** 实机验证（move_to 应正面走向目标）

---

## validator 检查清单（C# ↔ py 双份同步表）

| 检查 | C#（PlanGrammar.cs） | py（test_llm_plan.py validate_plan） | 对应纪律 |
|------|---------------------|--------------------------------------|---------|
| 跳转目标存在（S1）/ id 唯一（S4） | 跳转双向校验块 | 悬空跳转 / 重复 id | 2/3 |
| fallbacks 双层 | fallbacks 校验 | fallbacks 单层 | 4 |
| 动作词表 + 别名 | ValidateAction | ALLOWED_ACTIONS + ACTION_ALIASES | 1 |
| say_to text 字段 / ask 校验 | 步骤校验 | say_to 缺 text / ask 非法 | 11/17 |
| until 必须是对象 | 步骤校验 | until 类型检查 | 11 |
| 失败路径谎报 success | 条件等待 on_timeout/on_event → success 收尾 | is_condition_wait 检查 | 12/13 |
| zone/point 锚点引用 | 锚点校验 | SCENE_ANCHORS | 14 |
| 谓词词表（条件内事件词） | ValidateCondition | check_condition | 16 |
| reactions 词表 | ReactiveAgent 词表 | REACTIVE_EVENTS/ACTIONS | reactions |
| **seeing-false 无 sustained_s** | contingency 校验块（BC-001） | 纪律 19 检查 | 19 |
| **following-false** | contingency 校验块（BC-002） | 纪律 20 检查 | 20 |

> 改任何一行：C# 编译 + `python Scripts/test_llm_plan.py` 回归双过才算完。
