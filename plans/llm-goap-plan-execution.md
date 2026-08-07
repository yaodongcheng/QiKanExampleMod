# 密谋命令系统 v2 — 意图分类 + LLM 计划生成 + 确定性执行

> **状态**：✅ 已实施（2026-08-07，M1-M4 核心全部落地；LLM 链路已实测打通）
> - 代码落地：`Planner/`（PlanGrammar/SceneSnapshot/RuntimeWorldState/PlanExecutor/InlineSteps/ReactiveAgent/PlanReplan + GoalTemplates）、`Interaction/PlanCommandFlow.cs`、`Debug/PlanDebugCommands.cs` + `Debug/PlanExamples/`（17 份示例 JSON）
> - 接线：`AgentBrain` order_execute_plan/plan_decision/ReactiveAgent 触发词分支、`NpcIntent.ExecutingCommand`（CommandDetail）、`AgentAIController.OnMissionTick → PlanExecutor.TickAll`、Plot(G长按)/StopPlan(X短按) 玩法行、AgentHudVM 执行摘要
> - LLM：`LLMService.ChatAsync` 增加 temperature 参数 + **懒初始化修复**（原 Initialize 无调用点，LLM 功能从未工作）+ **URL/model 参数化**（读 MCM 配置）+ `reasoning_effort: none` 关思考模式（实测 25s→3.5s）；`PromptBuilder.BuildPlanPrompt`（意图词表+few-shot+封闭词表+JSON模板+跳转纪律）
> - 验证：`Scripts/validate_plan_json.py` 支持 .json 直验（17 示例全 PASS）；**`Scripts/test_llm_plan.py` LLM 链路回归（已固化进 §12 流程）**；`custom.plan_debug snapshot/list/run/status/stop/role/step/replan`
> - 已知实现偏差：contingency 的 `was` 修饰 + `one_shot: false` 组合下，触发后清除 wasEver 记录以恢复"掉线重触发"语义（§5.2）；signal_player 用非模态 DisplayMessage（NinjaNotification 模态锁鼠标会挡玩家操作）；执行期零 LLM 的铁律不变
> ⚠️ **代码库中不存在 v1 实现**（`RivalBrain`/`SceneSnapshot`/`PlanExecutor` 均未落地，grep 全库无命中）——文中的"v1 教训/v1 设计保留"指上一版设计文档，实施时无旧代码可继承、无需找旧实现，以本文为准。

---

## 📋 实施完成度总表（2026-08-07 复盘）

> ✅ = 实现完成（代码就绪，部分待实机验证）｜🟡 = 部分完成（见说明）｜❌ = 未实现
> **待实机项统一标注「🔧实机」**：代码链路已就绪，需进游戏验证行为（动态执行/动画/模态冲突等）。

| 章节 | 完成度 | 说明 |
|------|--------|------|
| §0.1 玩法 case（16） | 🟡 | 17 份示例 JSON 全部提取 + 静态校验 PASS；执行器支持全部示例语法（move_to/say_to/wait/steal_attempt/knockout/lead/make_noise/signal_player/give/loop/query/fallbacks/contingencies）；**🔧实机**：动态执行验证（`plan_debug run`） |
| §0.2 意外演练（Y1-Y4） | ✅ | Y1 折返（ReactiveFollowAction + was 检测）、Y2 拒绝（refuse 反应 + 当面报告）、Y3 战斗（R5 + Replan）、Y4 玩家被打（R1 Paused + 护主恢复）代码全就绪；**🔧实机**验证 |
| §1 四层架构 | ✅ | 意图分类→计划生成→批准→确定性执行管线完整 |
| §2.1 意图全表（29） | 🟡 | 枚举 29 值 + few-shot 判定基准 ✓；v2 梯队部分原子未实现：FETCH/PURCHASE/TALK_TO/FIND/SHADOW/COLLECT/DUEL/DISCREET/INTERACT（→ CUSTOM 诚实拒绝 ✓） |
| §2.2 CommandIntent | ✅ | PlanIntent 模型 + target 解析 + 分工（who_does）+ 一次调用 |
| §2.3 GoalTemplate | 🟡 | GoalTemplate 类 + 静态判定表 ✓；Success/Maintain Func 模板未实例化——**替代实现**：goal 缺失回落 = 主链走完即成功 |
| §3 SceneSnapshot | ✅ | 角色自动打标（StringId 关键词）/物件枚举/可见性矩阵/ToPromptText；铁律 5 语义版 |
| §4 原子行为库 | 🟡 | 17/21 实现：move_to/follow(极坐标)/stop_following/order_attack/knockout(完整)/lead/face/look_at/say_to(+ask广播)/wait/emote(降级no-op)/make_noise/signal_player/steal_attempt(绕背+目击问责)/give_item/give_gold/end_plan；**❌ 未实现**：deliver_item/shadow/negotiate/duel（stub 明确失败路径，§5.3 步骤级降级）；emote 动画表 **🔧实机**验证 |
| §4.1 随从犯罪责权 | ✅ | 目击问责（RegisterTheftWitnesses→嫌犯=玩家）/暗账（RegisterUnwitnessedTheft）/守恒转移（扒窃源 Hero→玩家，箱子记账=Grant） |
| §5.0 能力矩阵 | 🟡 | 线性/跳转/until/循环段/动态引用(lure_spot等6查询)/actor寻址/actor并行/相对站位/步骤级跳转 ✓；台本 script 播放 ❌（§5.5） |
| §5.1 Plan JSON 铁律 | ✅ | S1-S4 双向校验（validate_plan_json.py + PlanValidator）+ 运行期跳转缺失 Abort |
| §5.2 谓词词表 | ✅ | 13 谓词 + sustained/was 修饰（was 记录基础谓词，语义已修正） |
| §5.3 PlanValidator | ✅ | 步骤级降级（未知动作丢弃该步，>50% 拒收）/参数钳制/别名容错（attack→order_attack 等 6 个别名） |
| §5.4 PlanExecutor | ✅ | 多 actor 游标/双通道（事件+轮询）/收尾三路（当面/密信报告）/Paused 保留 Inline 状态/R1-R7 安全网（§7）/执行摘要 HUD |
| §5.5 台本演出 | 🟡 | script 模型 + validator 分支完整性校验 ✓；**执行器播放未实现**（negotiate/duel 依赖，v2 排期） |
| §6 ReactiveAgent | 🟡 | 触发词/反应词/人格演算/决策结果广播（refused/followed）/职业默认模板/深拷贝 ✓；**❌ 未实现**：恐慌传播链（see_ally_killed→flee/call_guards 链式）、relay_message（间接 BRING） |
| §7 Guardrail R1-R7 | ✅ | 全部实现（R1 战斗 Paused/R2 死亡/R3 停止键/R4 追回+独行豁免/R5 敌对+Replan/R6 总时长+事件驱动豁免/R7 模态）；Replan 节流 ≤2（成功才计数） |
| §8 密谋对话壳 | 🟡 | Plot(G长按)/StopPlan(X短按) 玩法行、自由输入→LLM→澄清轮(≤2)→批准轮(同意/再想想/算了)、密谋中 Talk 互斥移除 ✓；**❌ 未实现**：TextInputWidget + 手柄预设 chips（用既有 ShowTextInquiry 替代，功能等价） |
| §9 LLM 升级点 | ✅ | 超额完成：temperature 参数 + max_tokens 4000 + BuildPlanPrompt + **懒初始化修复**（原单例从未初始化，LLM 功能实际从未工作）+ **URL/model 参数化** + **reasoning_effort:none 关思考**（实测 25s→3.5s） |
| §10 复用清单 | ✅ | 全部接线完成（NpcIntent.ExecutingCommand/order_execute_plan/执行摘要 HUD/护主/警戒/WitnessCrime 围观） |
| §11 M1 骨架+case A | ✅ | 交互四接线/SceneSnapshot/PlanGrammar/PlanExecutor/order_execute_plan/17 示例/多 actor 游标/Guardrail 框架 |
| §11 M2 LLM 计划生成 | ✅ | LLMService 升级/BuildPlanPrompt/PlanResponse/PlanValidator/GoalTemplates/澄清+批准循环/降级路径；case B（BRING）执行器支持 |
| §11 M3 ReactiveAgent | ✅ | 触发词表/反应词表/人格演算/默认模板兜底/决策广播；传播作用域部分（§6 见上） |
| §11 M4 Replan+STEAL | ✅ | Replan 循环（事件日志→重入→新计划→summary 播报）+ steal_attempt 完整版（绕背/目击问责/守恒）+ knockout 完整版 |
| §11 M5 打磨 | 🟡 | 本地化（中英 60+ 键）/冒泡/HUD 执行摘要/emote 点缀/边界（随从死亡/并发 Plot 互斥）✓；**❌ 待做**：SPAR 意图、双版本编译（1.2.12）、实机体验 |
| §12 验证方案 | 🟡 | plan_debug 8 子命令 ✓（snapshot/list/run/status/stop/role/step/replan）；17 示例静态校验 ✓；**LLM 回归流程已固化（见下）**；**🔧实机**：测试矩阵（多疑守卫/尽责守卫/村长拒绝/意外三连/停止键/模态/R4 豁免） |
| §13 风险对策 | ✅ | 全部有对策且落地（LLM 分类错→few-shot+澄清；自说自话→人格演算+默认模板；竞争→统一接管+收尾三路；Replan 死循环→节流；冒泡无听者→face 前置；零成本→§4.1 问责） |

---

## 🔬 LLM 链路回归测试流程（已固化，2026-08-07）

> 每次修改 prompt / 意图表 / 换模型 / 调温度后，**必须**跑一次回归，退出码 0 才算合格。

```bash
# 完整回归（11 个预设命令：8 意图 + 歧义 + 词表外 + 清剿）
python Scripts/test_llm_plan.py

# 单命令 / 场景规模压力 / 示例 JSON 校验（不发请求）
python Scripts/test_llm_plan.py --cmd "干掉他"
python Scripts/test_llm_plan.py --scene 30
python Scripts/test_llm_plan.py --json Debug/PlanExamples/A_DISTRACT.json
```

**脚本职责**（`Scripts/test_llm_plan.py`）：读 MCM 配置（key 掩码）→ 构建与 C# `BuildPlanPrompt` 逐段同构的 prompt（意图表 + few-shot + 纪律 + 词表 + JSON 模板）→ `reasoning_effort: none` 发真实请求 → 模拟 `PlanValidator`（S1 跳转存在/S4 id 唯一/fallbacks 双层/动作词表+别名/say_to 字段名）→ 汇总分类正确率。

**回归基线（2026-08-07 实测，deepseek-v4-flash 关思考）**：
- 11 命令通过 **10/11**（唯一失败 = "杀全村人"被模型自主道德拒绝 → CUSTOM，属预期行为，见 §0.1 case N 注）
- 分类正确率 91%；schema 合规 100%；0 解析失败；耗时 1.5-5.4s（input ~1000-1700 tok，output ~480-550 tok）
- 已修复的不稳定项：①动作别名漂移（attack→order_attack，C# ActionAliases + prompt 强调）②相近意图漂移（"干掉他"在 ATTACK/DISTRACT 间漂移 → few-shot 意图判定基准）
- **同步维护纪律**：C#（`PlanCommandFlow.BuildIntentTable`/`BuildGrammar` + `PlanGrammar.ActionAliases`）与 py（`INTENT_TABLE`/`ACTION_ALIASES`）双份，改一边必须同步另一边

**实机验证入口**（游戏内，取真实环境/agent 数据）：
```
custom.plan_debug snapshot        # 真实场景快照（LLM 将看到的内容）
custom.plan_debug run A_DISTRACT  # 注入示例计划动态执行
custom.plan_debug step            # 当前步骤详情
custom.plan_debug status          # 执行器状态
custom.plan_debug replan          # 强制触发 replan 链路
```
或 Plot 流程：面向随从长按 G → 输入命令 → 澄清/批准 → 观察随从执行（日志 `Debug/StoryEngine_RuntimeLog.txt` 的 `[PlanExecutor]`/`[PlanCommandFlow]` 前缀）。


> **v1 教训**：v1 只解决了"随从引开守卫"一个 case——RivalBrain 是特化的守卫模型，命令处理没有分类层。用户指出：①命令可以是"请村长到我面前"、"我引开守卫你去偷"（角色互换）；②守卫模型不能特化；③老滚5/KCD/GTA 里的随从命令大部分要接得住；④计划提前写好，执行中意外（玩家被打、目标开战）怎么办。
> **v2 核心转变**：
> - 命令的本质 = **目标状态** + **角色分配**，不是动作序列 → 新增 **意图分类层（CommandIntent）+ GoalTemplate 表**
> - 守卫模型泛化为 **ReactiveAgent**（任何 NPC 可当"对手/被叫者"，同一套框架）
> - 意外处理 = 运行时级 **GuardrailEngine 安全网** + 执行器状态机（Paused/Aborted/**Replan 低频重入**）
> **对标原则**：[design-philosophy.md](rules/design-philosophy.md) 四原则 + [narrative-design.md](rules/narrative-design.md)（计划只基于场景可见事实）。

---

## 0. 玩法目标 — 通用性验证矩阵

> 三个 case 不够——它们都只覆盖"引开型对抗"。真正的通用性判据：**把所有 case 摊开，每个 case 都能映射到四件套（意图分类/场景快照/计划执行/ReactiveAgent）+ 薄场景层上，且没有哪个 case 需要新框架部件**。case 是验证手段，不是需求清单。

### 0.1 玩法 case（16 个，成败树状）

> case 格式 = **玩家原始输入（标题带意图标注：`case A（DISTRACT｜引开=随从，偷=玩家）`）→ 并联执行人（每个 actor 一块）→ 块内串联树 → 附 Plan JSON 执行视图**（✓ 成功 / ✗ 失败分支；框架标注 GATE/GOAL/MAINTAIN/TRIGGER、原子行为、§引用挂在节点上）。镜像 §5.4 并行执行模型：actor 间并行、actor 内串行。**三层呈现，无中间层**：意图标注 = 一行索引（链接 §2.1 意图词表 / §2.3 GoalTemplate / §7.1 R5 白名单）；JSON = 权威视图（信息全量——语义解析的正式载体是 `intent` 字段，§2.2）；树 = 人读逻辑视图。
> **书写纪律①接近步必须显式**：凡 say_to/近距离互动的步骤，树里必须写 move_to（§5.1 s1→s2 范本）；**face 已内置 say_to（§4 先 face），不单独成步**，与"接近步已内置于原子行为的（lead 节奏 / steal_attempt 绕背 / FightEnemyAction 追击 / say_to 的 face）不展开"同类。**🔴 树让步原则**：JSON 是执行器实际读取的权威视图，树是给人读的逻辑视图——树节点与 JSON 步骤 id 一一对应，凡树与 JSON 不一致，一律树让步。**书写纪律②成败分支齐全**：每个有成败的节点必须写 ✓/✗ 两个出口（含空情报、摸空、拒绝、打不过等失败态），玩家体验列完整呈现两种结局。**书写纪律③驱动模式必须标注**：每块块头标注驱动模式——① 执行者（随从）= **计划驱动**：PlanExecutor 按步骤+预案+安全网确定性驱动（actor 内串行推进）；② 玩家 = **自主驱动**：真人自由行动，计划只给窗口/信号提示（何时动手），**绝不驱动玩家**——玩家块永远"玩家侧自理"；③ 对抗方/被叫方（ReactiveAgent）= **事件驱动**：触发词→人格演算→反应动作（§6），计划不驱动其行动。读者看块头即知该块靠什么推进。**书写纪律④报告方式必须标注**：每个报告/信号节点必须指明报告方式（§5.4）——**当面报告**（随从可脱身：走回玩家身边 3m 内冒泡转述，用"回来报告/返回当面"字样）或**密信报告**（随从脱不开身或紧急中断：望风/盯梢/折返警报，用"秘密消息/即时信号"字样）。两种方式并存的计划必须逐分支分别标注，禁止笼统写"报告"。

**A. "我想偷那箱子，有人盯着怎么办？"（DISTRACT｜引开=随从，偷=玩家）**
**执行人① 随从（actor=self · 计划驱动）· 串联**
```text
走到守卫面前（move_to）→ 诱骗跟走（say_to，引开手段）
├─ [守卫跟走]（following(guard, self) 成立）→ 走向引开点（move_to lure_spot——动态选点（§5.0）：距 watch_point > 10m（窗口前提）+ 距 player > 阈值（不把守卫引到玩家埋伏点）+ 半径内无其他目击者 + navmesh 可达，**不硬编码 door**；守卫 follow_for_a_bit 跟随随从移动，随从不动守卫就不走；GATE：distance(guard, lure_spot) < 4 提前推进）→ 途中安抚（say_to s5，"别让人等急了"）→ 窗口达成（GATE：distance(guard, watch_point) > 10 sustained 5s）→ 跳 s7"动手"（密信报告玩家——行动窗口提示，玩家趁窗口偷）✓
│   ├─ [窗口迟迟不成立]（守卫跟到引开点但没离岗 10m，s6 超时）→ s13 密信"他没走远，拖不住了，先收手" ✗
│   └─ [守卫折返]（GATE：following(guard, self) 由成立变不成立——跟走有时限，left_post_seconds 到点折返，区分"从未跟随"见 §5.1 折返检测语义）→ s8"快收手！"（密信报告玩家）✗
└─ [守卫拒绝]（following(guard, self) 从未成立）→ 再哄一次 → 再哄成功（s11 on_success）→ 回主链继续引 ／ 再哄失败 → 放弃（当面报告玩家"他不上当"——守卫拒绝后随从可脱身，走回玩家身边 3m 内冒泡转述）✗
```
**执行人② 玩家（actor=player · 自主驱动）· 玩家侧自理（计划不驱动）**
```text
纠缠守卫（对话/战斗）
└─ 收到"动手！"密信 → 偷箱子 ✓ ／ 收到"快收手！"密信 → 收手 ✗
```
**目标方 守卫（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：spoken_to / asked_to_follow]（人格：duty/gullibility 演算）
├─ 跟走（follow_for_a_bit——跟随随从走向引开点，跟随时长/折返时机按 duty 运行时定，§6.4）→ 离岗超时（left_post_seconds）→ 折返回岗（return_post）
└─ 拒绝（duty 高）→ 不动 + 拒绝台词
```

**示例计划（§5.1 语法的 case A 完整实例——上面的树是逻辑视图，JSON 是执行视图；M1 开发期测试数据，不交付玩家。每份 = PlanResponse 截取：intent + plan 顶层字段，省略 reply/emotion/options/questions 对话壳字段）**
> 🔴 **示例定位：开发期测试数据，不随 Mod 交付、不上运行线**——**开发期**：全部 17 份示例（case A–P + §5.5 COLLECT 模板）逐一跑通执行器（§11 M1 / §12：`custom.plan_debug run <json>`，静态校验 + 动态执行**双通过**才算合格）。**M2 接入 LLM 后**：真实游玩每次下命令都由 LLM **现场生成**计划（测试矩阵以示例行为为期望基准），示例退役为三类参考：①**prompt 格式模板**（§5.5 COLLECT 完整模板进 BuildPlanPrompt）；②**LLM 输出对照基准**（§12 测试矩阵期望值）；③**语法权威示范**（词表/谓词/预案链/事件挂载的唯一完整用法）。v1 无 LLM 不可用时的示例兜底（§9 明示"v1 不做预设脚本兜底"）。示例质量 = LLM 输出质量的下限，但示例本身不上运行线。

```json
{
  "intent": {"intent_type": "DISTRACT", "subjects": ["companion"], "target": "guard", "who_does": "companion", "watch_point": "chest", "destination": "lure_spot"},
  "summary": "我把守卫引到远处清净处拖住，你趁机动。",
  "goal": {"type": "distance", "a": "guard", "b": "watch_point", "op": ">", "value": 10, "sustained_s": 5},
  "steps": [
    {"id": "s1", "action": "move_to", "target": "guard", "within": 2.0, "timeout_s": 15},
    {"id": "s2", "action": "say_to",  "target": "guard", "ask": "follow", "text": "那边有人找你，说是有急事，让我来叫你", "timeout_s": 8},
    {"id": "s3", "action": "wait",
        "until": {"type": "following", "a": "guard", "b": "self", "op": "true"},
        "on_event": [{"type": "refused", "then": "s10"}],
        "timeout_s": 10, "on_timeout": "s10"},
    {"id": "s4", "action": "move_to", "target": {"query": "lure_spot(watch_point, 12)"}, "within": 1.0,
        "until": {"type": "distance", "a": "guard", "b": "lure_spot", "op": "<", "value": 4},
        "timeout_s": 25, "on_timeout": "s6"},
    {"id": "s5", "action": "say_to",  "target": "guard", "text": "就在那边等着呢，别让人等急了", "timeout_s": 6},
    {"id": "s6", "action": "wait",
        "until": {"type": "distance", "a": "guard", "b": "watch_point", "op": ">", "value": 10, "sustained_s": 5},
        "timeout_s": 25, "on_timeout": "s13"},
    {"id": "s7", "action": "signal_player", "text": "守卫被我引开了，快动手！", "timeout_s": 3}
  ],
  "fallbacks": [
    [
      {"id": "s10", "action": "say_to",  "target": "guard", "ask": "follow", "text": "就说几句话的事，劳驾跟我走一趟吧", "timeout_s": 6},
      {"id": "s11", "action": "wait",
          "until": {"type": "following", "a": "guard", "b": "self", "op": "true"},
          "on_event": [{"type": "refused", "then": "s12"}],
          "timeout_s": 6, "on_timeout": "s12", "on_success": "s4"}
    ],
    [
      {"id": "s12", "action": "end_plan", "result": "fail", "report": "他不上当，不肯跟我走", "timeout_s": 3}
    ],
    [
      {"id": "s8", "action": "signal_player", "text": "守卫回岗了，快收手！", "timeout_s": 3},
      {"id": "s9", "action": "end_plan", "result": "fail", "timeout_s": 3}
    ],
    [
      {"id": "s13", "action": "signal_player", "text": "他没走远，拖不住了，先收手", "timeout_s": 3},
      {"id": "s14", "action": "end_plan", "result": "fail", "timeout_s": 3}
    ]
  ],
  "contingencies": [
    {"when": {"type": "alert_phase", "entity": "guard", "phase": "Alarmed"}, "then": "@abort_gracefully", "one_shot": true},
    {"when": {"type": "following", "a": "guard", "b": "self", "op": "false", "was": "true"}, "then": "s8", "one_shot": true},
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

> **条件角色标注**（对应 §5.3 条件角色表；角色由 JSON 字段直接命名，不需要槽位→角色映射表）：
> - s2 `ask: "follow"`（§4 say_to 参数）：播完广播 `asked_to_follow(guard)` 而非仅 `spoken_to`——守卫"跟不跟"的演算挂在 `asked_to_follow` 触发词上（§6.1），没有这个桥 s3 的 `following` 永远等不到
> - s3 `until` + `on_event` + `on_timeout`（wait 退出条件）：守卫跟走（`following(guard, self)` 成立）→ 本步完成推进 s4；**守卫拒绝** = ReactiveAgent 决策结果事件 `refused` 即时到达（§6.3 广播）→ `on_event` 跳 s10"再哄"（树上"再哄一次"✗ 分支，**不等超时**）；`on_timeout` 10s 仅为兜底（say_to 未被听到/事件未达，`was` 从未置真）
> - s4 `until` + `on_timeout`（动作提前完成条件）：守卫到引开点（< 4m，`lure_spot` = s4 query 求值后的具名落点）→ 提前截断 move_to，本步完成，拖住守卫；`on_timeout`（仍在跟随但走得慢/路径长，25s 没到位）→ 跳 s6 直接等窗口——引开点不是目的，窗口才是
> - s6 `until`（wait 退出条件）：守卫离岗（> 10m sustained 5s）→ 窗口成立，推进到 s7 发动手信号；s6 `on_timeout`（窗口 25s 不成立 = 守卫没离岗）→ 跳 s13 密信"他没走远，拖不住了，先收手"（树"窗口迟迟不成立"✗ 分支）
> - s11 `on_success` / `on_timeout` / `on_event`：再哄成功（第二次跟走，`following` 首置真 → `was` 记录）→ `on_success` 跳回主链 s4 继续引；再哄失败（超时）→ `on_timeout` 跳 s12 放弃；**再拒绝** = `refused` 事件即时到达 → `on_event` 跳 s12 放弃（不等超时，预案可跳回主链）
> - s12 `end_plan report`（放弃收尾）：`report` 触发**当面报告**——恢复默认跟随走回玩家 ~3m 内冒泡转述"他不上当"（§5.4 当面报告），对应树上"放弃（当面报告）"✗ 分支
> - `fallbacks` 分区（**四个预案**，只被跳转进入、不参与游标推进）：fb1 再哄（s10/s11）、fb2 放弃（s12）、fb3 折返警报（s8/s9）、fb4 窗口超时（s13/s14）
> - contingencies `when`（异常/跳转条件）：警戒 Alarmed → @abort（异常收尾）；折返 `following==false && was==true`（曾成立变不成立）→ 跳 s8（密信警报 + s9 失败收尾）；combat → @abort（安全网中止）
> - `goal`（GOAL）：守卫离岗 10m sustained = 计划成功（失败无独立字段——意外全走 contingencies，计划性失败走超时/fallbacks 的 end_plan）
> - s1→s2（接近步）：s1 仅 move_to——face 由 s2 say_to 执行期内置（§4），树不写"+ face"（内置不展开，纪律①）；s5（途中安抚）："别让人等急了"的 say_to，挂在树"走向引开点"与"窗口达成"之间

**B. "请村长到我面前来"（BRING）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
找到村长（move_to）→ 开口邀请（say_to）
├─ 跟来（被叫方 ReactiveAgent 决策通过，见目标方块）→ 到达（GOAL：distance(chief, player) < 3 && !moving(chief)）
│   ├─ 村长开口后玩家搭话 → 对话结束 → 随从回位 ✓
│   └─ 玩家不理 → 村长到点回岗 → 随从回位 ✓
└─ 拒绝（Y2）→ 返回当面报告"村长说忙，不来"（转述原话）✗
```
**目标方 村长（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：asked_to_follow(随从)]（人格：duty/social 演算）
├─ 跟来（social 高/duty 低）→ follow 随从 → 到达 meet_point → 逗留窗口（~10s 人格修正，duty 高呆得短）→ 开口"找我什么事？"
│   ├─ 玩家对话 → 回岗取消（对话结束才走）
│   └─ 玩家不理 → 到点自行回岗
└─ 拒绝（duty 高走不开 / social 低不给面子）→ 拒绝台词 → 回原岗位
```

**示例计划（B–P 与 case A 同构：树 = 逻辑视图，JSON = 执行视图；M1 以 A 为测试数据，B–P 为设计验证）**

```json
{
  "intent": {"intent_type": "BRING", "subjects": ["companion"], "target": "chief", "who_does": "companion", "meet_point": "player"},
  "summary": "我去请村长过来见你。",
  "goal": {"type": "and", "conditions": [
    {"type": "distance", "a": "chief", "b": "player", "op": "<", "value": 3},
    {"type": "moving", "a": "chief", "op": "false"}
  ]},
  "steps": [
    {"id": "b1", "action": "move_to", "target": "chief", "within": 2.0, "timeout_s": 30},
    {"id": "b2", "action": "say_to", "target": "chief", "ask": "follow", "text": "村长，我家主人请您过去一趟，有事相商", "timeout_s": 8},
    {"id": "b3", "action": "wait", "until": {"type": "following", "a": "chief", "b": "self", "op": "true"}, "timeout_s": 10, "on_timeout": "b6"},
    {"id": "b4", "action": "move_to", "target": "player", "within": 3.0, "until": {"type": "distance", "a": "chief", "b": "player", "op": "<", "value": 3}, "timeout_s": 40, "on_timeout": "b5"},
    {"id": "b5", "action": "wait", "until": {"type": "distance", "a": "chief", "b": "player", "op": "<", "value": 3, "sustained_s": 5}, "timeout_s": 20, "on_timeout": "b6"}
  ],
  "fallbacks": [
    [{"id": "b6", "action": "end_plan", "result": "fail", "report": "村长说忙，不肯来", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**C. "我引开守卫，你去偷那箱子"（STEAL｜偷=随从，引开=玩家）**
**执行人① 随从（actor=self · 计划驱动）· 串联**
```text
等窗口（GATE：!seeing(any, self) sustained 3s——唯一安全条件，判定对象=随从本人）
├─ 窗口成立 → steal_attempt（蹲下+Intent 显示 → 公式判定）
│   ├─ 物品到手 → 密信报告玩家"得手了，撤！"（即时信号）→ 撤出 → 当面移交玩家（铁律 4 守恒）✓
│   ├─ 摸空（空箱子）→ 当面报告玩家"没摸到" ✗
│   └─ 守卫脱身回看 → 窗口自动翻转 → steal_attempt 中断 → 密信报告玩家"守卫回看了，我停一下"（即时信号）
│       ├─ 玩家重新缠住 → 窗口恢复 → 回到等窗口重试
│       └─ 窗口迟迟不恢复 → 放弃 → 当面报告玩家"没机会了" ✗
└─ 窗口超时（一直有人看）→ 放弃 → 当面报告玩家"一直有人盯着，没机会" ✗
```
**执行人② 玩家（actor=player · 自主驱动）· 玩家侧自理（计划不驱动）**
```text
纠缠守卫（对话/战斗）—— 守卫被缠住 = 窗口成立的前提（玩家侧）
├─ 收到"得手了，撤！"密信 → 脱身撤离 ✓
└─ 收到"守卫回看了"密信 → 重新缠住（拉回守卫注意力）→ 随从继续 ✓ ／ 不理会 → 随从放弃 ✗
```
**目标方 守卫（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：被玩家纠缠（对话/战斗，玩家侧行为）]（人格：duty/gullibility 演算）
├─ 缠住期间 → 注意力在玩家身上（随从视角 seeing(guard, self)=false——窗口成立的前提）
└─ 对话/战斗结束 → 脱身回看箱子（investigate——duty 高挣脱更快；= 窗口翻转的机制，§5.2）
    └─ 再被缠住（玩家重新对话/开战）→ 注意力回到玩家（窗口恢复）
```

```json
{
  "intent": {"intent_type": "STEAL", "subjects": ["companion"], "target": "chest", "who_does": "companion"},
  "summary": "你引开守卫，我趁他背身去偷那箱子。",
  "steps": [
    {"id": "c1", "action": "steal_attempt", "target": "chest", "variant": "item",
        "when": {"type": "seeing", "a": "any", "b": "self", "op": "false", "sustained_s": 3},
        "result": {"success": "c2", "empty": "c6", "interrupted": "c8"},
        "timeout_s": 40, "on_timeout": "c7"},
    {"id": "c2", "action": "signal_player", "text": "得手了，撤！", "timeout_s": 3},
    {"id": "c3", "action": "move_to", "target": "player", "within": 3.0, "timeout_s": 20},
    {"id": "c4", "action": "give_item", "target": "player", "item": "stolen", "timeout_s": 5},
    {"id": "c5", "action": "end_plan", "result": "success", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "c6", "action": "end_plan", "result": "fail", "report": "没摸到，他太精了", "timeout_s": 3}],
    [{"id": "c7", "action": "end_plan", "result": "fail", "report": "一直有人盯着，没机会", "timeout_s": 3}],
    [{"id": "c8", "action": "signal_player", "text": "守卫回看了，我停一下", "timeout_s": 3},
     {"id": "c9", "action": "wait", "until": {"type": "seeing", "a": "any", "b": "self", "op": "false", "sustained_s": 3}, "timeout_s": 60, "on_timeout": "c10", "on_success": "c1"}],
    [{"id": "c10", "action": "end_plan", "result": "fail", "report": "没机会了", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true},
    {"when": {"type": "alert_phase", "entity": "any", "phase": "Alarmed"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**D. "去把那个守卫的钱袋摸来"（STEAL·人）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
绕背定位（steal_attempt 人变体：盲区几何搜索 + 无目击者视野；站位不可行 → 诚实报告）
├─ 窗口成立（GATE：!seeing(any, self) sustained 3s）→ 扒窃结算（公式）
│   ├─ 到手 → 当面移交玩家（走回玩家旁，铁律 4 守恒）✓
│   └─ 摸空/目标察觉 → 空手收尾 + 当面报告"没摸到，他太精了" ✗
├─ 窗口超时（一直有人盯，d1 超时）→ 当面报告"一直有人盯着，没机会" ✗
└─ 站位不可行 → 当面报告"没地方下手" ✗
```

```json
{
  "intent": {"intent_type": "STEAL", "subjects": ["companion"], "target": "guard", "what": "purse", "who_does": "companion"},
  "summary": "我去把那守卫的钱袋摸来。",
  "steps": [
    {"id": "d1", "action": "steal_attempt", "target": "guard", "variant": "pickpocket",
        "when": {"type": "seeing", "a": "any", "b": "self", "op": "false", "sustained_s": 3},
        "result": {"success": "d2", "empty": "d5", "impossible": "d6"},
        "timeout_s": 30, "on_timeout": "d7"},
    {"id": "d2", "action": "move_to", "target": "player", "within": 3.0, "timeout_s": 20},
    {"id": "d3", "action": "give_gold", "target": "player", "amount": "stolen", "timeout_s": 5},
    {"id": "d4", "action": "end_plan", "result": "success", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "d5", "action": "end_plan", "result": "fail", "report": "没摸到，他太精了", "timeout_s": 3}],
    [{"id": "d6", "action": "end_plan", "result": "fail", "report": "没地方下手", "timeout_s": 3}],
    [{"id": "d7", "action": "end_plan", "result": "fail", "report": "一直有人盯着，没机会", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true},
    {"when": {"type": "alert_phase", "entity": "any", "phase": "Alarmed"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**E. "缠住掌柜，我去翻那保管箱"（ENGAGE｜缠=随从，翻=玩家）**
**执行人① 随从（actor=self · 计划驱动）· 串联**
```text
站位（保管箱对侧，掌柜背对箱子）→ 循环说话（face 纪律自动面向随从）
├─ 达成（GOAL + MAINTAIN）→ 保持缠住
│   ├─ 玩家翻完 → 按停止键收尾（R3：当面喊停）✓
│   └─ 掉线（掌柜转头/走开）→ 预案：再缠（say_to 拉回视线）→ 拉不回 → 密信报告玩家"掌柜转头了，快收手！"（即时信号）✗
└─ 站位失败 → 当面报告"没位置" ✗
```
**执行人② 玩家（actor=player · 自主驱动）· 玩家侧自理（计划不驱动）**
```text
翻保管箱 —— 掌柜维度由执行人①保证；其他目击者走既有犯罪系统
├─ 翻完 → 按停止键收尾（R3）✓
└─ 被其他目击者发现 → 既有犯罪流程接管 ✗
```
**目标方 掌柜（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：被循环说话（spoken_to 持续）]（人格：temper/social 演算）
├─ 搭话/回应 → 保持原地对话（随从视角 seeing(掌柜, player)=false——GOAL 维持条件）
└─ 不耐烦（temper 高/缠太久）→ 转头/走开（= 掉线，窗口翻转的机制，§5.2）
```

```json
{
  "intent": {"intent_type": "ENGAGE", "subjects": ["companion"], "target": "tavernkeeper", "watch_point": "chest", "who_does": "companion"},
  "summary": "我缠住掌柜，你翻那保管箱。",
  "goal": {"type": "and", "conditions": [
    {"type": "seeing", "a": "tavernkeeper", "b": "player", "op": "false", "sustained_s": 3},
    {"type": "moving", "a": "tavernkeeper", "op": "false"}
  ]},
  "steps": [
    {"id": "e1", "action": "move_to", "target": {"query": "stand_spot(tavernkeeper, chest)"}, "within": 1.0, "timeout_s": 15, "on_timeout": "e9"},
    {"id": "e2", "action": "say_to", "target": "tavernkeeper", "text": "掌柜的，听说店里进了批好酒？", "timeout_s": 6},
    {"id": "e3", "action": "say_to", "target": "tavernkeeper", "text": "跟我讲讲呗，我主人想打听", "timeout_s": 6, "on_success": "e2"}
  ],
  "fallbacks": [
    [{"id": "e5", "action": "say_to", "target": "tavernkeeper", "text": "掌柜的您别走啊，我还没说完", "timeout_s": 6},
     {"id": "e6", "action": "wait", "until": {"type": "seeing", "a": "tavernkeeper", "b": "player", "op": "false", "sustained_s": 3}, "timeout_s": 15, "on_timeout": "e7", "on_success": "e2"}],
    [{"id": "e7", "action": "signal_player", "text": "掌柜转头了，快收手！", "timeout_s": 3},
     {"id": "e8", "action": "end_plan", "result": "fail", "timeout_s": 3}],
    [{"id": "e9", "action": "end_plan", "result": "fail", "report": "没位置", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "seeing", "a": "tavernkeeper", "b": "player", "op": "true", "was": "true"}, "then": "e5", "one_shot": false},
    {"when": {"type": "moving", "a": "tavernkeeper", "op": "true"}, "then": "e5", "one_shot": false},
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**F. "闹出点动静，把人都引过来"（COMMOTION｜闹=随从，下手=玩家）**
**执行人① 随从（actor=self · 计划驱动）· 串联**
```text
闹动作（砸酒桶/喊叫）→ 复用 WitnessCrime 围观聚集（§10，criminal=随从纯围观无犯罪副作用）
├─ 达成 → 保持 + 密信报告玩家"都看过来了，快动手！"（即时信号——人群里脱不开身）
│   ├─ 玩家完事 → 按停止键收尾（R3：当面喊停）✓
│   └─ 视线散开 → 预案：再闹（拉回视线）→ 拉不回 → 密信报告玩家"人散了，收手！"（即时信号）✗
└─ 引不过来 → 当面报告"没人看热闹" ✗
```
**执行人② 玩家（actor=player · 自主驱动）· 玩家侧自理（计划不驱动）**
```text
收"动手"信号 → 下手 ✓ ／ 收"人散了"信号 → 收手 ✗
```

```json
{
  "intent": {"intent_type": "COMMOTION", "subjects": ["companion"], "target": "all", "who_does": "companion"},
  "summary": "我闹出点动静把人都引过来，你趁乱动手。",
  "goal": {"type": "seeing", "a": "all", "b": "self", "op": "true", "sustained_s": 5},
  "steps": [
    {"id": "f1", "action": "make_noise", "timeout_s": 10},
    {"id": "f2", "action": "wait", "until": {"type": "seeing", "a": "all", "b": "self", "op": "true", "sustained_s": 5}, "timeout_s": 30, "on_timeout": "f9"},
    {"id": "f3", "action": "signal_player", "text": "都看过来了，快动手！", "timeout_s": 3},
    {"id": "f4", "action": "wait", "seconds": 600}
  ],
  "fallbacks": [
    [{"id": "f5", "action": "make_noise", "timeout_s": 10},
     {"id": "f6", "action": "wait", "until": {"type": "seeing", "a": "all", "b": "self", "op": "true", "sustained_s": 5}, "timeout_s": 20, "on_timeout": "f7", "on_success": "f4"}],
    [{"id": "f7", "action": "signal_player", "text": "人散了，收手！", "timeout_s": 3},
     {"id": "f8", "action": "end_plan", "result": "fail", "timeout_s": 3}],
    [{"id": "f9", "action": "end_plan", "result": "fail", "report": "没人看热闹", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "seeing", "a": "all", "b": "self", "op": "false", "was": "true"}, "then": "f5", "one_shot": false},
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**G. "把那个守卫引到巷子里打晕"（KNOCKOUT）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
走到守卫面前（move_to）→ 诱骗跟走（say_to，同 case A）→ 引到隐蔽点（move_to hidden_spot——动态选点（§5.0）：周围无人 + 无视野 + navmesh 可达）
├─ 窗口成立（GATE：!seeing(any, self) sustained 3s）→ 绕背 knockout（复用击晕轮子）→ 目标击晕（GOAL）→ 收手 → 回来报告"放倒了"（当面）✓
│   ├─ 窗口迟迟不成立（一直有人看着，g5 超时）→ 密信"没机会下手，先撤了" ✗
│   └─ 中途折返（同 case A 预案：密信报告玩家"他折回去了" → 中止）✗
└─ 隐蔽点不可达 → 当面报告"没地方下手" ✗
```
**目标方 守卫（ReactiveAgent · 事件驱动）**——同 case A 完整复用：跟走/拒绝/折返行为与人格演算逐字一致（§6.4），不再重复展开
```text
[触发：spoken_to / asked_to_follow]（人格：duty/gullibility 演算，同 case A）
```

```json
{
  "intent": {"intent_type": "KNOCKOUT", "subjects": ["companion"], "target": "guard", "who_does": "companion"},
  "summary": "我把他引到巷子里放倒。",
  "goal": {"type": "knocked_out", "entity": "guard"},
  "steps": [
    {"id": "g1", "action": "move_to", "target": "guard", "within": 2.0, "timeout_s": 15},
    {"id": "g2", "action": "say_to", "target": "guard", "ask": "follow", "text": "那边有人找你，说是有急事", "timeout_s": 8},
    {"id": "g3", "action": "wait", "until": {"type": "following", "a": "guard", "b": "self", "op": "true"},
        "on_event": [{"type": "refused", "then": "g10"}],
        "timeout_s": 10, "on_timeout": "g10"},
    {"id": "g4", "action": "move_to", "target": {"query": "hidden_spot(self, 15)"}, "within": 1.0, "until": {"type": "distance", "a": "guard", "b": "hidden_spot", "op": "<", "value": 4}, "timeout_s": 30, "on_timeout": "g13"},
    {"id": "g5", "action": "wait", "until": {"type": "seeing", "a": "any", "b": "self", "op": "false", "sustained_s": 3}, "timeout_s": 20, "on_timeout": "g14"},
    {"id": "g6", "action": "knockout", "target": "guard", "timeout_s": 10},
    {"id": "g7", "action": "end_plan", "result": "success", "report": "放倒了", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "g8", "action": "signal_player", "text": "他折回去了，先算了", "timeout_s": 3},
     {"id": "g9", "action": "end_plan", "result": "fail", "timeout_s": 3}],
    [{"id": "g10", "action": "say_to", "target": "guard", "ask": "follow", "text": "劳驾跟我走一趟，就问几句话", "timeout_s": 6},
     {"id": "g11", "action": "wait", "until": {"type": "following", "a": "guard", "b": "self", "op": "true"},
          "on_event": [{"type": "refused", "then": "g12"}],
          "timeout_s": 6, "on_timeout": "g12", "on_success": "g4"}],
    [{"id": "g12", "action": "end_plan", "result": "fail", "report": "他不上当，不肯跟我走", "timeout_s": 3}],
    [{"id": "g13", "action": "end_plan", "result": "fail", "report": "没地方下手", "timeout_s": 3}],
    [{"id": "g14", "action": "signal_player", "text": "没机会下手，先撤了", "timeout_s": 3},
     {"id": "g15", "action": "end_plan", "result": "fail", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "following", "a": "guard", "b": "self", "op": "false", "was": "true"}, "then": "g8", "one_shot": true},
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true},
    {"when": {"type": "alert_phase", "entity": "guard", "phase": "Alarmed"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**H. "帮我望风，来人了叫我"（LOOKOUT）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
走到望风点（move_to）→ 望风（MAINTAIN：保持望风位置——望风是无限期待命，不套 5 分钟总时长上限（R6 豁免）；结束方式 = 玩家按停止键（仅对执行中的随从）：随从在身边 → 当面喊停；离远了 → 密信通知中止；**无 GOAL：达成点 = 下令时刻 t0，没有"等待达成"阶段**）
└─ 有人进望风区（从"没人"变"有人"的那一瞬间，TRIGGER）→ "有人来了！"（密信报告玩家——望风留守脱不开身）→ 继续望风（可重复）
```

```json
{
  "intent": {"intent_type": "LOOKOUT", "subjects": ["companion"], "target": "watch_zone", "who_does": "companion"},
  "summary": "我去望风，来人了叫你。",
  "steps": [
    {"id": "h1", "action": "move_to", "target": "watch_zone", "within": 1.5, "timeout_s": 30},
    {"id": "h2", "action": "wait"}
  ],
  "triggers": [
    {"when": {"type": "in_zone", "a": "any", "b": "watch_zone", "op": "true"}, "then": {"action": "signal_player", "text": "有人来了！"}}
  ]
}
```
> h2 无限保持：`wait` 省略 seconds/until/timeout = 无限期待命（§4 wait 行），结束 = 玩家 R3 停止键 / R4 收尾——与树"无限期待命"一致（R6 豁免同源，不套 5 分钟上限）。

**I. "告诉他，我在老地方等他"（DELIVER）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
走到目标面前（move_to）→ 传话（say_to）
├─ 目标听到（GOAL）→ 回来报告"说好了"（当面报告玩家）✓
└─ 目标不理/赶走 → 回来如实转述"他让我滚开"（当面，台词与原话一致）✗
```
**目标方 传话对象（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：spoken_to(随从)]（人格：social/temper 演算）
├─ 听到（social 高）→ 原地回应"知道了" → 随从 GOAL 达成
└─ 不理/赶走（temper 高/social 低）→ 拒绝台词"滚开" → 随从如实转述
```

```json
{
  "intent": {"intent_type": "DELIVER", "subjects": ["companion"], "target": "contact", "message": "我在老地方等他", "who_does": "companion"},
  "summary": "我去把你的话带到。",
  "steps": [
    {"id": "i1", "action": "move_to", "target": "contact", "within": 2.0, "timeout_s": 30},
    {"id": "i2", "action": "say_to", "target": "contact", "text": "我家主人说，他在老地方等你", "timeout_s": 8},
    {"id": "i3", "action": "wait", "until": {"type": "time_since", "step_id": "i2", "op": ">", "value": 2},
        "on_event": [{"type": "refused", "then": "i5"}],
        "timeout_s": 6, "on_timeout": "i6"},
    {"id": "i4", "action": "end_plan", "result": "success", "report": "说好了", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "i5", "action": "end_plan", "result": "fail", "report": "他让我滚开", "timeout_s": 3}],
    [{"id": "i6", "action": "end_plan", "result": "fail", "report": "他没应我", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**J. "带我去河边"（GUIDE）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
[计划期预检①：语义锚点解析] "河边" → 预定义 Zone 或动态空间查询（§5.0，水网格/语义标记，能力待验证）
├─ 场景无此地 → 当面报告"我不知道河边在哪"（知识诚实：情报只能来自场景可见事实，不瞎带路）✗
└─ 解析成功 → [预检②：navmesh 可达性]
    ├─ 不可达 → 当面报告"去不了那边" ✗
    └─ 可达 → lead（§4：节奏同步在 `lead` 原子行为内部——前进 + 定期回望，不自顾自走）
        ├─ 玩家跟上 → 到达目的地（GOAL）✓
        └─ 玩家跟丢 → 停下等 → 跟上继续；等待超时 → 当面报告"你走不走啊" → 中止 ✗
```

```json
{
  "intent": {"intent_type": "GUIDE", "subjects": ["companion"], "destination": {"query": "zone(河边)"}, "who_does": "companion"},
  "summary": "我带你去河边。",
  "goal": {"type": "distance", "a": "player", "b": "destination", "op": "<", "value": 3},
  "steps": [
    {"id": "j1", "action": "lead", "target": "destination", "timeout_s": 300}
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "@abort_gracefully", "one_shot": true}
  ]
}
```

**K. "把那个醉鬼从我身边赶走"（DRIVE_AWAY）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
走到醉鬼面前（move_to）→ say_to 恐吓（剧本骨架，§6.4）
├─ 被吓走（目标方反应，见目标方块）→ GOAL 达成 → 收手回位 ✓
├─ 恐吓无效（不理不睬）→ 再恐吓（k5/k6）→ 仍无效 → 当面报告"他不理我，赶不走他" ✗
└─ 反抗（目标方反应，见目标方块）→ 开战 → 密信报告玩家"他急眼了"（k7 密信 + 中止）✗（铁律 12：恐吓失败有代价）
```
**目标方 醉鬼（ReactiveAgent · 事件驱动）**——非执行人：计划不驱动其行动，反应由人格演算决定（§6）
```text
[触发：spoken_to(随从) 恐吓语气]（人格：gullibility/temper 演算）
├─ 被吓走（gullibility 高/temper 低）→ 离开 → 随从 GOAL 达成
└─ 反抗（temper 高）→ 骂回/挥拳 → 进入战斗（combat 事件 → 随从 k7 密信"他急眼了" + 中止）
```

```json
{
  "intent": {"intent_type": "DRIVE_AWAY", "subjects": ["companion"], "target": "drunkard", "who_does": "companion"},
  "summary": "我去把那醉鬼赶走。",
  "goal": {"type": "distance", "a": "drunkard", "b": "player", "op": ">", "value": 10},
  "steps": [
    {"id": "k1", "action": "move_to", "target": "drunkard", "within": 2.0, "timeout_s": 15},
    {"id": "k2", "action": "say_to", "target": "drunkard", "text": "滚远点，别在这碍事！", "timeout_s": 6},
    {"id": "k3", "action": "wait", "until": {"type": "distance", "a": "drunkard", "b": "player", "op": ">", "value": 10, "sustained_s": 3}, "timeout_s": 20, "on_timeout": "k5"},
    {"id": "k4", "action": "end_plan", "result": "success", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "k5", "action": "say_to", "target": "drunkard", "text": "再不滚，对你不客气！", "timeout_s": 6},
     {"id": "k6", "action": "wait", "until": {"type": "distance", "a": "drunkard", "b": "player", "op": ">", "value": 10, "sustained_s": 3}, "timeout_s": 15, "on_timeout": "k9", "on_success": "k4"}],
    [{"id": "k7", "action": "signal_player", "text": "他急眼了，我先撤", "timeout_s": 3},
     {"id": "k8", "action": "end_plan", "result": "fail", "timeout_s": 3}],
    [{"id": "k9", "action": "end_plan", "result": "fail", "report": "他不理我，赶不走他", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "k7", "one_shot": true}
  ]
}
```

**L. "你在这等我，去那边看看有什么"（SCOUT）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
侦察往返（move_to 目标点 → 观察 → 返回）
├─ 有发现 → 回来报告"巷子里有伙人"（GOAL：报告送达）✓
└─ 空情报 → 回来报告"那边没什么"（GOAL：报告送达）✓（信息如实，空情报也是情报）
```

```json
{
  "intent": {"intent_type": "SCOUT", "subjects": ["companion"], "destination": {"query": "point(那边)"}, "who_does": "companion"},
  "summary": "我去那边看看有什么。",
  "steps": [
    {"id": "l1", "action": "move_to", "target": {"query": "point(那边)"}, "within": 2.0, "timeout_s": 60},
    {"id": "l2", "action": "wait", "seconds": 5},
    {"id": "l3", "action": "end_plan", "result": "success", "report": "{OBSERVATION}", "timeout_s": 3}
  ]
}
```
> l3 报告内容运行时填充（PlaceholderResolver 先例 §5.5 `{AMOUNT}`）：l2 观察期后按所见填充——有发现 → "巷子里有伙人"／空情报 → "那边没什么"，与树两条 ✓ 分支对应（缺口 10 的 report 占位符支持）。

**M. "干掉他"（ATTACK）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
走到目标面前（move_to——接近到交战距离是前置步骤，玩家可尾随验证；FightEnemyAction 的追击只在开战后生效）→ order_attack（战斗风格：不留恋）
├─ 目标被击倒（GOAL）→ 收手 → 返回当面报告"办完了" ✓
├─ 打不过/目标逃跑 → 脱战 → 密信报告"他太硬，我先撤了"（紧急中断，随从离玩家可能远）✗
└─ 缠斗过久（120s 无结果，m2 超时）→ 密信"拖太久了，我先撤" ✗
```

```json
{
  "intent": {"intent_type": "ATTACK", "subjects": ["companion"], "target": "foe", "who_does": "companion"},
  "summary": "我去办了他。",
  "goal": {"type": "dead", "entity": "foe"},
  "steps": [
    {"id": "m1", "action": "move_to", "target": "foe", "within": 1.5, "timeout_s": 20},
    {"id": "m2", "action": "order_attack", "target": "foe", "until": {"type": "dead", "entity": "foe"}, "timeout_s": 120, "on_timeout": "m6"},
    {"id": "m3", "action": "end_plan", "result": "success", "report": "办完了", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "m4", "action": "signal_player", "text": "他太硬，我先撤了", "timeout_s": 3},
     {"id": "m5", "action": "end_plan", "result": "fail", "timeout_s": 3}],
    [{"id": "m6", "action": "signal_player", "text": "拖太久了，我先撤", "timeout_s": 3},
     {"id": "m7", "action": "end_plan", "result": "fail", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "and", "conditions": [
        {"type": "combat", "entity": "self", "op": "false", "was": "true"},
        {"type": "dead", "entity": "foe", "op": "false"}]}, "then": "m4", "one_shot": true}
  ]
}
```

**N. "把全村人都杀了"（ANNIHILATE）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
[计划期澄清（questions，≤2 轮）：目标集合边界] "全村人" 是否含己方/同伴？
├─ 玩家明确"连他也杀" → 过滤规则 = 无差别（含己方阵营 + 同伴，后果自负）
└─ 默认 / 玩家确认不含 → 过滤规则 = 排除己方阵营 + 同伴（**不攻击自己人 = 硬默认**）
↓
循环段（until 清空）：query 下一个敌人 → 击杀
├─ 清空 zone（GOAL）→ 收手 → 返回当面报告战果 ✓
└─ 目标逃出 zone（区内已无敌人 = loop.until 达成，部分达成）→ 循环自然结束 → 返回当面报告战果（"跑了几个"由收尾动态统计）✓ ／ 随从死亡/离场（R2 安全网）→ 中止 ✗（战斗意图不写 combat 兜底——combat 是正常进展；无独立"受伤"谓词，损失保护走 R2）
（恐慌传播链：see_ally_killed → flee/attack/call_guards 链式传播）
```

```json
{
  "intent": {"intent_type": "ANNIHILATE", "subjects": ["companion"], "zone": "village", "filter": "exclude_allies", "who_does": "companion"},
  "questions": [{"q": "全村人——连自己人也算进去吗？", "options": ["只杀外人（默认）", "连自己人也杀"]}],
  "summary": "我把这村子里的人清了（不含自己人）。",
  "goal": {"type": "count", "of": {"query": "all_in(zone)"}, "op": "=", "value": 0},
  "loop": {
    "steps": [
      {"id": "n1", "action": "move_to", "target": {"query": "nearest_enemy(self)"}, "within": 1.5, "timeout_s": 15},
      {"id": "n2", "action": "order_attack", "target": {"query": "nearest_enemy(self)"}, "timeout_s": 30}
    ],
    "until": {"type": "count", "of": {"query": "all_in(zone)"}, "op": "=", "value": 0}
  },
  "steps": [
    {"id": "n3", "action": "end_plan", "result": "success", "report": "{BATTLE_REPORT}", "timeout_s": 3}
  ]
}
```

**O. "他要是敢还手，你就上"（GUARD·条件参战）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
压阵（MAINTAIN：戒备跟随——无 GOAL：达成点 = 下令时刻 t0，下令即开始保持）
├─ 对手不动手（只挨威胁/对峙）→ 持续压阵 ✓
└─ 对手还手（TRIGGER：对手攻击护卫对象——combat(对手, 护卫对象) 上升沿）→ attack 对手 → 战斗收手 → 恢复压阵 ✓
```
**执行人 玩家（actor=player · 自主驱动）· 玩家侧自理（计划不驱动）**
```text
与对手冲突（威胁/对峙/动手）——先手在玩家："还手"以玩家出手为前提
├─ 对手还手 → 随从参战 ✓
└─ 对手认怂/冲突结束 → 按停止键收尾（R3）✓
```

```json
{
  "intent": {"intent_type": "GUARD", "subjects": ["companion"], "target": "player", "opponent": "rival", "who_does": "companion"},
  "summary": "我就在你旁边压阵，谁还手我上。",
  "steps": [
    {"id": "o1", "action": "follow", "target": "player", "rel_pos": "behind"}
  ],
  "fallbacks": [
    [{"id": "o2", "action": "order_attack", "target": "rival", "timeout_s": 30},
     {"id": "o3", "action": "wait", "until": {"type": "combat", "a": "rival", "b": "player", "op": "false"}, "timeout_s": 90, "on_success": "o1"}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "a": "rival", "b": "player", "op": "true"}, "then": "o2", "one_shot": false},
    {"when": {"type": "and", "conditions": [
        {"type": "combat", "entity": "self", "op": "true"},
        {"type": "combat", "a": "rival", "b": "player", "op": "false"}]},
      "then": "@abort_gracefully", "one_shot": true}
  ]
}
```
> o1 无限压阵：follow 省略 timeout_s = 无限保持（R3 停止键收尾）。abort 兜底 = and 组合（缺口 7 已裁决）：**随从被打 且 对手未在打玩家** 才中止——参战期间（对手在打玩家）豁免，不误杀计划。

**P. "打晕门口那两个守卫"（KNOCKOUT·批量）**
**执行人 随从（actor=self · 计划驱动）· 串联**
```text
循环段：query 下一个守卫 → 绕背击晕
├─ 全部击晕（GOAL）→ 收手 → 返回当面报告"全放倒了" ✓
└─ 惊动增援 / 目标醒转 → 中止 + 密信报告"来了增援，先撤"（紧急中断）✗
   惊动判定（两级信号，取早者）：①**警戒上升**——附近任一 ReactiveAgent 进入 Alarmed（`alert_phase` 谓词，WitnessCrime 目击脉冲源，case A 警戒 @abort 同款）→ 惊动了，立刻中止跑路；②**开打兜底**——增援已敌对化（`combat(self)` = 有人把随从设为 enemy）→ 晚信号，此时已被围，脱战即跑
```

```json
{
  "intent": {"intent_type": "KNOCKOUT", "subjects": ["companion"], "target": {"query": "all_in(gate)"}, "who_does": "companion"},
  "summary": "我把门口那俩守卫全放倒。",
  "goal": {"type": "count", "of": {"query": "all_in(gate)"}, "op": "=", "value": 0},
  "loop": {
    "steps": [
      {"id": "p1", "action": "move_to", "target": {"query": "nearest_enemy(self)"}, "within": 1.5, "timeout_s": 15},
      {"id": "p2", "action": "knockout", "target": {"query": "nearest_enemy(self)"}, "timeout_s": 10}
    ],
    "until": {"type": "count", "of": {"query": "all_in(gate)"}, "op": "=", "value": 0}
  },
  "steps": [
    {"id": "p5", "action": "end_plan", "result": "success", "report": "全放倒了", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "p3", "action": "signal_player", "text": "来了增援，先撤", "timeout_s": 3},
     {"id": "p4", "action": "end_plan", "result": "fail", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "alert_phase", "entity": "any", "phase": "Alarmed"}, "then": "p3", "one_shot": true},
    {"when": {"type": "combat", "entity": "self"}, "then": "p3", "one_shot": true}
  ]
}
```

### 0.2 意外演练 case（4 个，执行期验证）

| # | 意外 | 机制 | 期望 |
|---|------|------|------|
| Y1 | 守卫中途折返 | ReactiveAgent 职责记忆（duty 参数） | 玩家窗口内偷完 / 失败收尾 |
| Y2 | 村长拒绝来 | 拒绝型反应（duty 高走不开 / social 低不给面子，§6.4 人格演算） | 拒绝台词 → 随从返回玩家旁 → 当面冒泡转述"村长说忙，不来"（当面报告，§5.4）→ 计划收尾 |
| Y3 | 守卫和玩家打起来 | Guardrail R5 + Replan | 中止报告 → 战斗结束可 replan |
| Y4 | 偷到一半玩家被打 | Guardrail R1 Paused + 护主 | 随从护主 → 打完恢复计划 |

### 0.3 维度覆盖分析（证明通用性的核心）

| 维度 | case 覆盖 |
|------|----------|
| 意图类别 | 引开(A)/请人(B)/偷物(C)/偷人(D)/缠住(E)/闹事(F)/打晕(G)/望风(H)/传话(I)/带路(J)/驱逐(K)/侦察(L)/单杀(M)/清剿(N)/条件参战(O)/批量击晕(P) = 16 种，涵盖对抗、合作、事件、信息、**战斗**五族 |
| 角色分配 | 随从独行(I/K/L)、玩家偷+随从引(A/F/G)、随从偷+玩家引(C/D)、随从请人玩家等(B/E/H/J)、随从独立作战(M-P) |
| 对抗形态 | 无对抗(B/I/J)、温和对抗(A/C/E)、强对抗(F/G/K/M/N/P)、事件对抗(H/Y1/Y2/Y3/Y4) |
| 目标类型 | 物(A/C/F)、人(D/G/K/M/O)、位置(B/E/J)、视野(E/H)、信息(I/L)、**批量目标**(N/P) |
| 参与者数量 | 1v1（多数）、1v多(F/N 群体)、多配合(A/C/F 玩家+随从双角色) |
| 意外类型 | 折返(Y1)/拒绝(Y2)/开战(Y3)/玩家被打(Y4)/目标死亡(R2) |

**判据结论**：16+4 个 case 全部落在"意图词表一行 + GoalTemplate 一个 + prompt 描述一段"的薄场景层上——**没有任何 case 需要新框架部件**。新增 case 的成本恒定（≈一个枚举行 + 一个谓词组合 + 一段 prompt），不随 case 数量增长——这就是框架通用性的定义。（战斗批量 case N/P 需要**计划语法扩展**：循环段 + 动态目标引用，见 §0.5 缺口⑤——这是语法能力成长，不是框架重构。）

**真正的能力边界**（诚实列举，不是设计缺陷）：词表外的命令 → CUSTOM 诚实拒绝；需要游戏引擎不存在的实体交互（INTERACT）→ 待验证后进词表；超出 Mission 层的能力（大地图指令）→ 不属于本系统。

**性能铁律不变**：计划阶段可容忍 LLM 数秒；**执行阶段零 LLM**——所有参与 NPC（随从 + 对手 + 相关人）全部确定性运行。

### 0.4 压力测试：武侠 / 中世纪村镇（第二谱系）

> 用另一套玩法谱系压框架：金庸味命令的核心不是"偷/引开"，而是**情报、找人、跟踪、社交结算、传递链**。

| # | 玩家命令 | 意图解析 | 映射结论 |
|---|---------|---------|---------|
| W1 | "去打听镇上最近来了什么高手" | SCOUT 社交版（问人式情报：多目标 say_to + 汇总回报） | 词表组合，v2 |
| W2 | "找到卖药的郎中，请来给我看伤" | FIND + BRING | **缺口①：目标特征搜索**（目标不在角色表） |
| W3 | "悄悄跟着那黑衣人，看他去哪" | SHADOW（动态目标隐蔽跟踪 + 事件回报） | **缺口②：shadow 原子行为** |
| W4 | "去张员外家讨回那笔债" | COLLECT（目标交钱 → 移交玩家） | **缺口③：社交结算** + TransferGold（铁律 4） |
| W5 | "把这封信送到李秀才家" | DELIVER 送物版（交付物品给目标） | **缺口④：deliver_item** |
| W6 | "假扮贩马商人，去套镖师的话" | TALK_TO（伪装 = 台词内容 + gullibility 判定） | **验证：欺骗是反应层问题，无需新框架** |
| W7 | "护送王姑娘回城" | GUIDE 泛化（引导第三方 NPC）+ GUARD | 词表组合——BRING 机制复用：随从走到目的地，第三方 follow |
| W8 | "拖住管家，别让他去报信" | ENGAGE | 已有意图 |
| W9 | "去和那剑客切磋，试他深浅" | DUEL（非致死比武 + 水平评估回报） | **缺口③**：战斗结算化（可复用切磋虚拟血量轮子） |
| W10 | "看到他们出城就放烟火" | LOOKOUT 变体（事件条件=出城）+ 信号执行 | 词表组合；烟火特效 v3 待验证（v1 降级为秘密消息） |
| W11 | "去买些金创药" | PURCHASE | 已有意图（v2） |
| W12 | "去客栈订两间上房" | TALK_TO 结算版（安排事务 + 房号回报） | **缺口③**：社交结算 |

**信息传递链**（W8 的"报信"另一侧）："通报门卫，就说XX帮主来访" → 门卫 `relay_message` 给主人 → 主人来见 = **间接 BRING**（`relay_message` 已在 §6.3 反应词表）。

### 0.5 压力测试结论：真缺口只有 4 个，且全是"库要长"不是"框架要改"

| 缺口 | 扩展内容 | 性质 |
|------|---------|------|
| ① 目标特征搜索 | SceneSnapshot 支持按 occupation/外观检索 + 歧义澄清；找不到 → 诚实报告"镇上没见到这人" | 快照检索小扩展 |
| ② shadow 隐蔽跟踪 | 新原子行为：距离保持 + 反被发现判定 + 事件回报（目标停下/会面/离场） | 原子行为词表扩展 |
| ③ 社交结算化 | 新原子行为族：`negotiate`（讲价/讨债/订房——随从技能 vs 目标参数确定性结算）、`duel`（切磋，复用既有切磋虚拟血量轮子）——**社交互动不必逐句对话，可结算为原子行为**。**已拍板：结算化 + 计划期台本生成**（§5.5：台本随计划生成，分支与结果枚举一一对应，执行期播台本零 LLM，场面顺且结果与台词一致） | 原子行为词表扩展 + 台本机制 |
| ④ 交付物 | `deliver_item(target, item)`：DELIVER 从传话泛化到送物 | 原子行为词表扩展 |
| ⑤ 批量目标 / 循环结构 | **计划语法扩展**：循环段（`loop { steps, until }`）+ 动态目标引用（query refs：`nearest_enemy(self)` / `all_in(zone)`——运行时求值，不只快照实体）。"杀全村人"、"偷光箱子"、"挨家挨户搜刮"都是同一表达能力（见 §5.0） | 计划语法扩展（v2） |
| ⑥ 群体恐慌传播链 | ReactiveAgent 触发词 `see_ally_killed` → flee/attack/call_guards，经 `BroadcastEventInRange` 链式传播（看到逃的人也跟着逃）——既有犯罪围观流程的恐慌版 | 反应词 + 传播机制复用 |

**结论**：24 个 case 无一动框架架构（四件套不变），缺口全是"词表与原子行为库的增长点"——玩法多样性的成本 = 词表行数，与框架复杂度解耦。两个额外收获：欺骗 = 台词 + 反应层判定（gullibility，无新框架）；引导机制可复用给任何人（W7）。

### 0.6 群组谱系：一带多（多个随从——单个随从是特例，v1 就要支持）

> 核心原则：**计划属于小队，不属于个人**——步骤级 `actor` 寻址（§5.0）。单随从 = 所有步骤 actor 缺省 self，特例零成本。

| # | 玩家命令 | 意图解析 | 验证点 |
|---|---------|---------|--------|
| Q1 | "你们都跟着我" | 群体 FOLLOW（actor: all） | actor 广播 + 单随从特例 |
| Q2 | "你们几个排成一列，站我身后" | FORMATION（各随从相对站位） | **相对站位复用 `FollowAgentAction` 极坐标参数**（radius+angleOffset，站身后=angleOffset≈π，一字排开=每人不同角度）；GoalTemplate = all 到达站位 |
| Q3 | "你们三个一起上" | 群体 ATTACK（actor: all + query 目标） | 群战 = actor 广播 + 循环段组合 |
| Q4 | "你俩去请村长，你望风" | 分工（多 actor 步骤） | **分工 = 同计划不同 actor 步骤**，无需新结构 |
| Q5 | "和我切磋一下" | SPAR（player 是互动对象） | **响应型计划**：随从备战（face+拔刀）→ 等玩家动手（wait until player_action）→ 既有切磋结算 → 收手台词 |
| Q6 | "站我身后" | FORMATION（单随从） | 单随从特例 = 同一意图零特化 |

**一带多三条规则**：
1. **寻址在命令文本**："你们/你俩/你"由分类层解析成 `subjects`（§2.2 CommandIntent 新字段）；交互入口不变（对任一随从按 G），subjects 默认 = 该随从单人，说"你们" = 附近 10m 内全部随从
2. **计划步骤带 actor**：`actor` = 随从 id 或 `"all"`（广播）；单随从 = 全部缺省 self
3. **玩家被寻址时玩家侧自理**：执行器不驱动玩家；SPAR 只表达随从侧备战 + 结算衔接

### 0.7 优雅性审计（全谱系通盘检查结论）

> 28 个玩法 case（0.1×16 + 0.4×12）+ 4 意外 + 6 群组逐一遍查：不优雅点 4 处，设计层面已消除（待实现验证）。

| 不优雅点 | 修复 | 位置 |
|---------|------|------|
| 计划属于个人（一带多要复制 N 份） | **计划属于小队**：步骤级 actor 寻址，执行器多 actor 游标（状态机结构不变） | §5.0 |
| 寻址要靠 UI（对每个随从单独下命令） | 寻址在命令文本（"你们/你俩"），分类层解析 subjects | §2.2 |
| "站身后/排一列"要写坐标 | 相对站位复用 `FollowAgentAction` 极坐标参数（radius+angleOffset），零新运动系统 | §4 |
| "切磋"要计划驱动玩家 | 玩家被寻址时玩家侧自理 + 响应型计划（备战→等触发→结算） | §0.6 规则 3 |

**优雅性总原则**：所有 actor（随从/对手/被叫方）共享同一套原子行为与谓词世界状态，计划与反应计划**通过世界状态隐式协调，无显式握手**——随从计划等 `distance(主人, 玩家) < 3` 成立，主人的 ReactiveAgent 自己走过来。双方都不知道对方完整计划，靠共享世界状态收敛。这就是"兜得住所有 case 且不堆特化"的根本原因。

---

## 1. 核心架构：一条管线，四层

```
玩家自然语言命令
  │
  ▼ ① 意图分类（LLM，封闭意图词表，一次调用内完成）
CommandIntent { type, target, who_does, params }
  │  查 GoalTemplate 表（C# 薄层）→ 执行器可验证的目标状态定义
  ▼ ② 计划生成（LLM，同一次调用）
  │  - 随从主动计划（steps + contingencies + success/fail）
  │  - 全部相关 NPC 的 ReactiveAgent 反应计划（trigger→reaction）
  │  - 玩家行动窗口提示（何时动手）
  ▼ ③ 玩家批准（同意/修改/算了）
  ▼ ④ 确定性执行（零 LLM）
  PlanExecutor × N（每个参与 NPC 一个，驱动各自 AgentBrain）
  GuardrailEngine 安全网（运行时级硬规则，所有计划共享）
    意外 → Paused（可恢复）→ Aborted（报告）→ Replan（低频 LLM 重入）
```

**GOAP 关系**：GOAP = 目标状态 + Action 集 + 规划器 + 执行器。本项目 = **LLM 替代规划器**（输出意图分类 + 全 cast 计划），**执行层做执行器**（确定性解释 + 安全网）。LLM 只写"程序"，不跑"程序"。

**泛化原则**：四件套通用框架（意图分类、场景快照、计划语法+执行器、ReactiveAgent）+ 一层薄场景定义（GoalTemplate 表 + 角色表 + 意图词表描述）。未来命令 = 意图词表加一行 + GoalTemplate 加一个 + prompt 描述，框架零改动。

---

## 2. ① 意图分类层（接住全部命令类别的入口）

### 2.1 意图全表（按老滚5 / KCD / GTA 随从命令整理）

| 意图 | 玩家话术 | 目标状态（GoalTemplate，执行器可验证） | 能力 | 阶段 |
|------|---------|--------------------------------------|------|------|
| FOLLOW | 跟我走 | 随从在玩家 5m 内 | `order_follow` 已有 | v0 |
| WAIT | 在这等我 | 随从原地不动 | `StayAction` 已有 | v1 |
| STOP | 住手/别打了 | 停止当前行为链 | `ClearAllActions` 已有 | v0 |
| ATTACK | 干掉他 | 目标被击倒 | `order_attack` 已有 | v0 |
| GUARD/PROTECT | 护住他 | 目标不受伤害 | 护主逻辑已有 | v0 |
| **BRING** | 请村长到我面前 | `distance(target, 玩家) < 3 && !moving(target)` | 随从 move_to+say_to（邀请）→ 目标 follow 随从返回（ReactiveAgent 被叫方：duty/social 决定是否跟，拒绝 → Y2） | **v1** |
| **DISTRACT** | 引开那守卫 | `distance(target, watch_point) > 阈值 sustained` 或 `target.following(随从)` | 随从 move_to+say_to+follow + ReactiveAgent(target) | **v1** |
| **LOOKOUT** | 帮我望风 | 异常发生时 `signal_player`（事件驱动目标） | 感知谓词 + signal | **v1** |
| **DELIVER** | 告诉他我在老地方见 | 目标听到消息（say_to 完成） | 随从 move_to+say_to | **v1** |
| **ENGAGE** | 缠住掌柜/拖住他 | `!seeing(目标, player) sustained && !moving(目标)`（滞留 + 调视野——**目击判定对象是小偷（玩家/随从），不是箱子**，谓词见 §5.2） | 随从站位（保管箱对侧，目标背对箱子，复用 §4 相对站位）→ say_to 循环说话 + ReactiveAgent(target) | **v1.5** |
| **DRIVE_AWAY** | 把那醉鬼赶走 | `distance(target, 玩家) > 10`（威胁型中间态） | 随从 say_to（恐吓）+ ReactiveAgent(target) | **v2** |
| **STEAL** | 去偷那箱子/他的钱袋 | 物品（或钱）获得并移交玩家 | NPC 侧偷窃原子行为 + `TransferItems`/`TransferGold`（铁律 4） | **v1.5** |
| **FORMATION** | 站我身后/你们排成一列 | 各随从到达相对站位 | `FollowAgentAction` 极坐标参数（radius+angleOffset）+ 状态维持 | **v1.5** |
| **SPAR** | 和我切磋一下 | 切磋分出胜负（player 是互动对象） | 响应型计划（备战→等玩家触发→结算）+ 既有切磋虚拟血量轮子 | **v1.5** |
| FETCH | 去把我的剑拿来 | 物品获得并移交玩家 | move_to + 取物 | v2 |
| PURCHASE | 去买两桶酒 | 物品获得（花随从自己的钱）并移交 | 交易 API | v2 |
| KNOCKOUT | 打晕他 | 目标击晕 | 背后击晕轮子（已有）→ 原子行为封装 | v2 |
| GUIDE | 带我去河边 | `distance(player, destination) < 3`（GOAL：玩家到达目标点；Impossible = 目的地不可达预检） | `lead` 原子行为（§4：节奏同步在 `lead` 原子行为内部，不自顾自走；玩家跟丢/等待超时报告在 lead 内部） | v2 |
| SCOUT | 去那边看看有什么 | 随从返回并报告所见 | move_to + 感知快照 + report（`{OBSERVATION}` 运行时填充，缺口 10） | v2 |
| TALK_TO | 去和掌柜谈酒钱 | 目标被交涉（状态变化） | say_to（v1 简化为传话）+ 交涉系统 | v2 |
| **FIND** | 找到卖药的郎中 | 报告目标当前位置（目标不在角色表，特征搜索后定位） | 快照特征检索 + move_to + signal | v2 |
| **SHADOW** | 悄悄跟着那黑衣人 | 事件回报：目标停下/会面/离场时 `signal_player` | `shadow` 原子行为（隐蔽跟踪） | v2 |
| **COLLECT** | 去张员外家讨回那笔债 | `gold_transferred(目标→玩家) > 金额` | `negotiate` 结算 + `TransferGold`（铁律 4） | v2 |
| **DUEL** | 去和那剑客切磋 | 非致死比武分出胜负 + 水平评估回报 | `duel` 结算（复用切磋虚拟血量轮子） | v2 |
| **ANNIHILATE** | 把全村人都杀了 | `all_in(zone)` 敌对清空（多目标批量目标） | 循环段 + 动态目标引用 + `order_attack`（非致死版 = 批量 KNOCKOUT） | v2 |
| COMMOTION | 闹出点动静 | 周围 NPC 视线聚拢到随从身上（`seeing(all, companion)`）——**脱身/掩护场景：被看到是目的** | 喊叫/攻击动作（行为参数） | v3 |
| INTERACT | 把门打开/把灯吹灭 | 实体状态改变 | 实体交互 API（游戏引擎能力待验证） | v3 |
| DISCREET | 低调点/别惹事 | 行为参数（不跑不叫不打架） | 行为参数化 | v2 |
| CUSTOM | 词表外的请求 | 执行器无法验证 → **诚实拒绝**："这我做不到"，提示改述 | — | v1 |

**"接得住"= 架构上接得住**：每个意图都走同一条管线（分类 → GoalTemplate → 计划 → 执行 → 安全网）。意图表可增长，新增意图 = 三处小改动（词表行 + GoalTemplate + prompt 描述），**框架零改动**。v1 只实现第一梯队（BRING/DISTRACT/LOOKOUT/DELIVER + 已有 v0），其余是"词表里已注册、执行深度按里程碑补"——玩家说了"去买酒"，v1 的答案是"这我还做不了"（诚实拒绝），v2 就能跑。这与意图分类层的存在不矛盾：**分类永远接得住，执行深度逐版本加深**。

### 2.2 CommandIntent 结构

**与既有意图体系的关系（统一架构，无桥接）**：`IntentBase`（玩家交互选项）／`CommandIntent`（玩家命令的分类，计划层输入）／`NpcIntent`（NPC 运行时行为，互斥状态机，`NpcIntent.cs`）——**运行时意图只有一套 = `NpcIntent`**：既有状态值 + 新增 `ExecutingCommand`（detail = CommandIntentType，复用既有 Confronting + ConfrontationType 的 detail 模式）。`CommandIntent` 是计划期输入数据（LLM 分类 → GoalTemplate 查表），执行期被 NpcIntent **持有**（包含关系，非映射，无翻译函数）。细节见 §10。

```json
{
  "intent_type": "DISTRACT",
  "subjects": ["companion"],    // 执行者列表（一带多）："你们/你俩"解析为多个随从；默认 = 该随从单人
  "target": "guard",            // 角色表引用（歧义 → questions 澄清）
  "who_does": "companion",      // 谁执行主动作：companion / player
  "watch_point": "chest",       // 意图相关锚点（DISTRACT 的看守点 = 目标物"箱子"的位置，快照 ObjectInfo.Position 解析；BRING 的集合点 = 玩家位置）
  "destination": "lure_spot",   // 可选：引开点/去向（DISTRACT 默认 = lure_spot 动态搜索（§5.0）：距 watch_point 远 + 距玩家远 + 人少 + 可达，**不硬编码语义锚点**；LLM 可给叙事方向，实际落点仍由运行时按条件搜）
  "params": { "distance": 10 }
}
```

- **subjects 寻址在命令文本**："你们/你俩/你"由 LLM 解析（§0.6 规则 1）；交互入口不变，subjects 默认 = 按 G 键的那个随从单人；"你们" = 附近 10m 内全部随从（不在附近的排除 + 提示）

- **target 解析**：玩家命令里的"村长/守卫/掌柜"→ 快照实体按 occupation/名字匹配 + 歧义澄清轮（"是那边那个戴帽子的村长吗？"）。角色表由场景入口构建，target 必须是快照里存在的引用（Validator 校验）。**同名模板 NPC 消歧**（如多个"帝国农民"）：外观不可靠（共用模型），位置优先（快照 `PositionDesc`）——澄清轮选项 = 候选位置描述 + **"随便一个就行"是合法选项**（玩家不关心 → 默认就近匹配）。target 可以是场景里任意人，含模板 NPC（铁律 8：所有 Agent 平等互动，ReactiveAgent 默认人格模板兜底）。**对抗意图（DISTRACT/ENGAGE/KNOCKOUT 等）的目标选择集合 = 目标物潜在目击者集合——与 `seeing(any, …)` 的 eligible 同源（清醒 + 非队友 + 看得见目标物）**："guard" 只是快照角色标签，可以是任意非己方目击者（守卫/掌柜/路人/村民），按"谁看得见目标物"用谓词解析（`CanAgentSeeTarget` 反查），不用硬编码 ID（铁律 5）。
- **分工（who_does，通用机制）**：命令里每一件事都有明确执行人——底层语法是 §5.0 的**步骤级 actor 寻址**（每个步骤声明 `actor`，单随从 = 全部缺省 self，一带多 = 指定 actor 或 "all"），**所有 case 通用，角色互换只是分工的一个实例**（玩家和随从互换常规角色）。命令可声明"谁做哪件事"：case C = 偷（随从）+ 引开（玩家）——计划生成时按分工输出**两段**：随从的主动 plan（等窗口→偷→移交）+ 玩家的行动提示（"你只管缠住守卫，我听见动静就动手"）。玩家侧的执行不归计划管（玩家是真人），计划只负责：随从的 plan 里声明"窗口条件"（如守卫离开 watch_point），执行器检测到才推进。
- **意图与计划一次调用**：LLM 同一次调用输出 `{intent, plan, questions}`——questions 非空则只问不计划（歧义优先澄清，复用澄清循环，最多 2 轮）。**失败降级**：JSON 解析失败或 validator 拒收步骤 > 50% → 一次重试（temperature 0.3 重申）→ 仍失败 → signal_player "我没听明白，你再跟我说一遍" + 释放控制（对话壳可重新下令，复用 LLMService 既有重试）。

### 2.3 GoalTemplate 表（C# 薄层，`Planner/GoalTemplates.cs`）

```csharp
public enum CommandIntentType { /* §2.1 意图全表一一对应：Follow/Bring/Distract/Lookout/Steal/…/Custom */ }

public class GoalTemplate {
    public CommandIntentType IntentType;        // 命令层意图枚举——不是 NpcIntentType（见下）
    public Func<WorldState, bool> Success;      // GOAL：成立 = 意图达成（一次性成功）；保持型意图 = 达成锚点（先达成）
    public Func<WorldState, bool> Maintain;     // MAINTAIN（保持型意图）：达成之后保持条件持续成立；翻转 = 掉线预案（后保持）
    public Func<WorldState, bool> Impossible;   // 提前判定不可行（目标已死/不在场景）
}
// BRING:    Success(GOAL) = distance(target, meetPoint) < 3 && !moving(target)
//            机制链：随从找目标 → 走过去邀请（say_to）→ 目标被说服则 follow 随从回来
//            （目标是场景内真实存在的 ReactiveAgent，duty/social 决定跟不跟，可能拒绝）
// DISTRACT: Success(GOAL) = distance(target, watchPoint) > 10 sustained 5s
//            (alternate) = following(target, companion)
// ENGAGE:   Success(GOAL) = !seeing(目标, player) sustained 3s && !moving(目标)
//            Maintain(MAINTAIN) = 达成后保持到玩家 R3 叫停；掉线（转头/走开）→ 预案
// COMMOTION:Success(GOAL) = seeing(all, companion) sustained；Maintain(MAINTAIN) 同 ENGAGE
// GUIDE:     Success(GOAL) = distance(player, destination) < 3（目标参与者 = 玩家）；Impossible = ①语义锚点解析失败（场景无"河边"类地点 → 诚实报告"不知道"，知识来自场景可见事实，不瞎带路）②navmesh 不可达（→ "去不了那边"）
// LOOKOUT:  Success = null（事件驱动：纯 MAINTAIN——异常即触发 signal_player，无限期待命，R6 豁免）
```

**`CommandIntentType` 与 `NpcIntentType` 的关系（统一架构，无桥接）**：运行时意图只有一套 = `NpcIntent`（既有状态机）。`CommandIntentType` 是**计划层键**（LLM 分类输出 → GoalTemplate 查表，29 个命令类别），执行期作为 `NpcIntent.ExecutingCommand` 的 detail 被持有（复用既有 Confronting + ConfrontationType 的 detail 模式）——**包含关系，不是映射**：不需要把命令类别"翻译"成状态，NpcIntent 直接带着它。**值优先**：战斗中 → Fighting（既有值自然表达，不因计划内而变）、被击晕 → KnockedOut、跟随 → Following；计划特有行为（引开/望风/传话/带路…）→ ExecutingCommand(detail)。**LLM JSON 层 `intent_type` 是 string（LLM 只能输出字符串），C# 层 Parse 成 `CommandIntentType`，未知值 → validator 拒收/降级 CUSTOM**（封闭词表纪律，§5.3）。

目标状态必须**执行器可验证**，所以不能是 LLM 自由文本——LLM 只能从词表选意图，目标状态由 C# 模板定义。这是"封闭语法"纪律的延伸（对齐现有 `tactic` 纪律）。

**与 plan 的 goal 的关系**：GoalTemplate.Success 是权威，plan 里的 `goal` 是**可选的具体化**（可省略，缺省回落模板）。并存时 validator 校验语义兼容（同意图同谓词族，如 DISTRACT 的模板 `distance > 10 sustained` 允许计划改写为 `following(companion)` 变体），**禁止与模板矛盾**（如 BRING 计划写"守卫离开"）。执行器只认一套判定：有 plan 条件用 plan 条件，没有用模板。**失败无独立字段**：意外（警戒/combat）走 contingencies @abort，计划性失败走超时 / fallbacks 的 end_plan。

---

## 3. ② 场景快照 SceneSnapshot — 计划期的世界模型

**文件**：`Planner/SceneSnapshot.cs`（v1 设计保留，微调）

```csharp
public class SceneSnapshot
{
    // 采集源分两路（引擎两个遍历入口，互不混淆）：
    public List<AgentInfo> Agents;         // ① ← Mission.Agents（人形活体）：角色表（Role 语义标签）+ 路人（Role=null）；几十~几百，超限近玩家优先采样
    public List<ObjectInfo> Objects;       // ② ← Mission.Scene 上**可交互 GameEntity**（带 UsableMissionObject：门/箱/灯/桶/床…，StealBar 目标枚举同源）；几个~几十
    // 纯装饰 GameEntity（无交互组件：桌椅/植被/网格，数量级几千）→ **不进快照**（全量 = prompt 爆炸）；
    //    命令引用时按需语义查询（INTERACT"把灯吹灭"→ query 匹配"灯"），查不到 → 诚实报告
    public List<ZoneInfo> Zones;           // 预定义场景锚点：door / meet_point…（watch_point/destination 不是预定义 Zone，按意图语义解析：预定义锚点 → 动态空间查询（§5.0）→ 都查不到 → 诚实报告"不知道"，narrative 铁律）
    public Dictionary<(int,int), bool> Visibility;  // 复用 NpcSightSystem.CanAgentSeeTarget
    public class AgentInfo
    {
        public string Role;        // "player" "self" "guard" "chief" "tavernkeeper"…
        public string DisplayName; public string FacingDesc; public string PositionDesc;
        public string State;       public string Occupation; public string PersonalityHint; // 人设 trait 摘要
    }
    public class ObjectInfo
    {
        public string Id; public string Kind; public string DisplayName; public string PositionDesc;
    }
}
```

- 角色表按命令构建（不是固定守卫模板）：DISTRACT → target=正在看目标物的人（= 目标物潜在目击者集合成员，与 seeing(any, ·) eligible 同源，§2.2）；BRING → target=命令里指定的职业/名字匹配者；watch_point/meet_point/destination 由命令与场景解析。
- 目标物（无生命对象）："那个箱子/保管箱" → ObjectInfo 快照（复用 StealBar 既有目标枚举）；"所指"解析 = 玩家视角最近的可偷物，模糊由澄清轮兜底。**Zone 只管位置锚点（door/meet_point），容器/家具一律走 ObjectInfo**——case A/C/F 的目标物不在 Zones 里。
- **采集源分离（两路引擎遍历，互不混淆）**：`Agents` ← `Mission.Agents`（活体 Agent，含角色表 + 路人）；`Objects` ← `Mission.Scene` 上**可交互 GameEntity**（带 `UsableMissionObject` 组件：门/箱/灯/桶/床…，StealBar 目标枚举与它同源）——**骑砍2 里 GameEntity 不等于装饰**：可交互的那部分正是 Objects 的来源。**纯装饰 GameEntity**（无交互组件：桌椅/植被/网格，数量级几千）才是不进快照的部分——全量 = prompt 爆炸，只在命令引用时**按需语义查询**（INTERACT"把灯吹灭"），查不到 → 诚实报告"这我做不到"（§0.5：INTERACT 实体交互能力待验证后进词表）。
- 视野矩阵复用 `NpcSightSystem.CanAgentSeeTarget`（`NpcSightSystem.cs:36-60`）。
- `ToPromptText()` 纯相对语义描述；执行期读实时更新的同一模型（100ms 节流）。

---

## 4. ③ 原子行为库 — 封闭动作词表

**执行期零 LLM 的关键**：LLM 只能从封闭词表选动作，每个词条映射到既有轮子。**动作词表与意图无关、与角色无关**——随从、守卫、村长用的都是同一张表。

| 动作 | 参数 | 底层实现（全部已有或 v1 内新增） |
|------|------|---------------------|
| `move_to` | target/zone, within | `AgentControlHelper.MoveTo` / `MoveToPositionAction`（`AtomicAction.cs:403`） |
| `follow` | target, rel_pos?（behind/line/left/right）, max_dist?, until? | `FollowAgentAction`（`AtomicAction.cs:564`）：①不带 `rel_pos` = 普通跟随（跟随目标，`max_dist` 跟丢距离）；②带 `rel_pos` = **跟随式相对站位**（v1 新增，M1 即需 Q2/Q6 硬编码验证——极坐标参数 radius+angleOffset：站身后 = angleOffset≈π、一字排开 = 每人不同角度，保持偏移持续跟随目标，零新运动系统）——同一引擎行为，参数化区分，不占两个词表位 |
| `stop_following` | — | brain 队列清理 |
| `order_attack` | target, until?, timeout_s | `FightEnemyAction`（既有轮子，`AgentBrain` 战斗接管）——战斗型意图主原子（M/O/N 用；旧示例的 `fight` 已统一为此名） |
| `knockout` | target, timeout_s | 背后击晕轮子封装（击晕轮子见 Knowledge/击晕机制_引擎能力与实现踩坑.md）——KNOCKOUT 意图主原子（G/P 用） |
| `lead` | destination | **v2 新增**：带路（GUIDE 用）——朝目的地前进 + **定期回望**（distance(player, self) > 跟丢阈值 ~8m → 停下 + face 玩家 + 等）；玩家跟上（< 3m）继续；等待超时（玩家不走）→ 中止 + 报告（"你走不走啊"）。**节奏同步在原子行为内部，不自顾自走**——与 `follow` 镜像（follow = 跟随者，lead = 领路人） |
| `face` / `look_at` | target, seconds | `TurnToDirectionAction` / `LookAtAction`（`AtomicAction.cs:327,765`） |
| `say_to` | target, text, ask? | `AgentHudMissionView.AgentSay`（`AgentHUD\AgentHudMissionView.cs:302`）+ 先 `face`：执行期队列 = `face`（TurnToDirectionAction 面向目标保持站立），AgentSay 是 HUD 层冒泡**不占队列**；完成 = 冒泡播完，`timeout_s` = 播完兜底上限（"N 秒内必须播完"，非"说 N 秒"）——**不是 StayAction**（StayAction = 原地不动占位，语义不同）。**`ask` 可选**：`"follow"` = 邀请跟随——播完广播 `asked_to_follow(target)`（对方"跟不跟"演算的触发词，§6.1）；缺省仅广播 `spoken_to` |
| `wait` | seconds **或** until（互斥；**保持型可两者皆省 = 无限等待**），timeout_s? | 执行器计时：完成 = 所写的那个条件到点（`seconds` 到点 或 `until` 成立）——**纯等待用 `seconds`（等 N 秒），等条件用 `until`（等世界状态），两者互斥使用，禁止同写**；`timeout_s` 兜底冗余可省略（完成点自己定的步骤，兜底只在计时器卡死时起作用；timeout 真正有意义的是完成取决于外部条件的步骤：move_to/say_to）；**两者皆省 = 无限保持**（LOOKOUT 望风 / GUARD 压阵等事件驱动 keep 步，结束 = 玩家 R3 停止键 / R4，R6 豁免同源，§5.3 的 30s 默认豁免） |
| `emote` | anim_id（**语义标签**，见下动画表） | `PlayAnimAction`（`AtomicAction.cs:807`）：演出点缀（说话配动作/结果配情绪/台本配动作，M5 打磨）——可选装饰步骤，不改世界状态、不影响成败；LLM 只写语义标签，运行时查映射表出引擎动画 ID |
| `make_noise` | — | 喊叫/砸物等行为参数（COMMOTION 引众用；criminal=随从 → 复用围观聚集，纯围观无犯罪副作用，`AgentBrain.cs:395,499`） |
| `signal_player` | text | `NinjaNotificationManager.Show`（`Notify\NinjaNotificationMissionView.cs:25`） |
| `steal_attempt` | target_item / target_agent | **v1.5 新增**：NPC 侧偷窃原子行为，**两个 target 变体**：①**物**（箱子）：接近→蹲下 + **Intent 显示**（玩家靠视觉感知"他在偷"，**不复用玩家 StealBar UI/子弹时间**）→ **成功率公式判定**（随从技能 vs 目标参数）→ Transfer 移交/空手 → 复用 WitnessCrime 目击/警戒脉冲；②**人**（扒窃）：**绕背定位**（内部几何搜索——目标正背后盲区 + 可达 + 不被任何 eligible 目击者看见，复用相对站位轮子 + `CanAgentSeeTarget` 校验；盲区不可达/被盯 → 尝试侧面替代盲区，仍不行 → 判定不可行，诚实报告"没地方下手"）→ 蹲下 + Intent 显示 → 公式判定（复用扒窃盲盒参数）→ 移交/空手 → WitnessCrime。**分工：计划层管时机（`!seeing(any, self)` 窗口 GATE），原子行为管站位几何** |
| `give_item` / `give_gold` | amount/item | `AgentControlHelper.TransferItems/TransferGold`（铁律 4 统一归口） |
| `deliver_item` | target, item | **v2 新增**：送物（move_to 目标 → 转交物品——DELIVER 从传话泛化到送物） |
| `shadow` | target | **v2 新增**：隐蔽跟踪（距离保持 8~15m + 反被发现判定 + 事件回报——W3 盯梢） |
| `negotiate` | target, topic, desired | **v2 新增**：社交结算（讲价/讨债/订房——随从技能 vs 目标参数确定性结算，非逐句对话） |
| `duel` | target | **v2 新增**：切磋结算（非致死比武，复用既有切磋虚拟血量轮子 + 水平评估回报） |
| `end_plan` | result, report? | 执行器收尾；**`report` 可选** = 当面报告文本（§5.4 当面报告：恢复默认跟随走回玩家 ~3m 内冒泡转述后再彻底收尾；缺省 = 仅收尾不冒泡） |

**emote 动画表（语义标签 → 引擎动画）**：LLM 词表里 `anim_id` 只接受以下语义标签（封闭小表，validator 校验），运行时查映射表出引擎动画 ID。**动画校验两层，均与 action_set 相关**：①**XML 定义校验（构建期/启动期）**——解析 actions.xml（Native + 各模块），用 `MBAnimationManager.GetActionCodeWithID` 确认动画 ID 真实存在（候选表"待 M5 验证"即走这步，失败的标签从表里剔除）；②**运行时 action set 校验（播放前）**——按执行者 agent 的动作集（`Agent.Monster` 的 ActionSet，human/human_child 骨骼动作集不同）查该 ActionCode 可用性，**不可用 → 该 emote 降级为无动作**，不崩、不穿模（参击晕轮子 action_set 继承链坑）。两层校验都过才播放：

| 语义标签 | 候选引擎动画（待 M5 验证） | 用途 |
|----------|--------------------------|------|
| `nod` | `act_agree_1` | 点头（确认/答应） |
| `shake` | `act_disagree_1` | 摇头（拒绝） |
| `wave` | `act_wave_1` | 招手（望风报告/叫人来） |
| `cheer` | `act_cheer_1` | 欢呼（任务成功） |
| `bow` | `act_bow_1` | 鞠躬（请人/敬意） |
| `shrug` | `act_shrug_1` | 耸肩（无能为力） |
| `point` | `act_point_1` | 指方向（带路/指引） |
| `threaten` | `act_threaten_1` | 威胁（恐吓语气） |
| `disappointed` | `act_defeat_1` | 泄气（失败收尾） |

**执行通道**：`AgentAIController.SendEventToAgent(npc, "order_execute_plan", plan)`（`AgentAIController.cs:431`），`AgentBrain.ReceiveEvent` 新增事件分支：暂停护卫跟随 → 启动执行器 → 逐条入队。收尾统一：`ClearAllActions` → `ForceUnlockAgent` → 恢复默认行为（随从回 `FollowAgentAction(Leader)`，`AgentBrain.cs:859-871`）。

### 4.1 随从犯罪的责权归属（case C/D — 防止零成本最优解）

**原则：随从偷窃 = 玩家偷窃的代理版**——结算/目击/警戒复用既有玩家偷窃流程，唯一区别是**事后问责对象是玩家**：

- **结算**：`steal_attempt` **不复用玩家偷窃 UI**——StealBar 子弹时间是玩家专属界面，NPC 偷只有"蹲下 + Intent 显示"两个感知通道（玩家通过视觉看出随从在偷），判定走**成功率公式**（随从技能 vs 目标参数：警觉度/财物等，复用既有扒窃/偷窃判定参数），一次出手定成败；成功 → 物品/金钱 `Transfer` 给玩家（铁律 4 守恒：从目标处转移，非凭空生成）
- **目击/警戒**：复用 WitnessCrime 的目击/警戒脉冲机制——目击者认出随从是玩家同伴（`IsPlayerTeammate` 反向使用），告发对象指向玩家
- **事后问责**：玩家真实犯罪后的质问/赔偿流程照走，NPC 措辞归因"你的手下"（对话层走 `LWNTextHelper` 本地化）；玩家出口对齐赔偿纪律：赔钱 / 抵赖（关系恶化）/ 处罚随从
- **为什么不构成零成本**：随从偷与玩家偷同风险结构——无目击 = 成功无后果（与玩家自己偷一致），有目击 = 玩家面对问责。**随从技能只影响结算成功率，不影响问责归属——随从不会成为免罪替身**
- v1 简化：只做"无目击成功 / 有目击问责"两态；处罚随从的团队关系影响 v2

---

## 5. ④ 计划语法 + 解释器（v1 设计保留，加状态机）

### 5.0 计划语言能力矩阵

> 计划语言是框架的表达力核心，按 case 需求逐版本成长。批量目标（case N/P"杀全村人/打晕两个守卫"）暴露了循环与动态引用的需求。

| 能力 | 语法 | 版本 | 覆盖的 case |
|------|------|------|------------|
| 线性序列 | `steps[]` | v1 | 全部 |
| 条件跳转 | `contingencies`（when → then） | v1 | A（折返预案）/O（条件参战） |
| 提前推进 | 步骤内 `until` | v1 | A（守卫到位即走） |
| **循环段** | `loop { steps, until }`——段内步骤循环执行，每轮求值 until；**loop 走完（until 达成）→ 顺序继续 `steps` 主链**（loop 后接报告步骤：case N 战果 `{BATTLE_REPORT}` / P"全放倒了"） | **v2** | N（清剿）/P（批量击晕） |
| **动态目标引用** | query refs：`nearest_enemy(self)` / `all_in(zone)` / **`hidden_spot(near, min_dist)`** / **`lure_spot(watch_point, min_dist)`**（运行时空间搜索——case G"引到巷子"的落地：**mission 没有"巷子"标记**，自然语言位置描述 → 空间条件，由运行时找点。`hidden_spot` = 隐蔽点：navmesh 可达 + 半径内无 agent + 不被 any eligible 目击者看见；**`lure_spot` = 引开点**：hidden_spot 条件 + 距 watch_point > min_dist（窗口前提，杜绝"引开点不够远→窗口永不成立"）+ 距 player > 阈值（不把守卫引到玩家埋伏点）——case A 引开点动态搜索，**不硬编码 door**（无 door 锚点时可用；玩家恰在 door 旁时选点绕开））。**query refs 求值一次后注册为具名引用，后续步骤/谓词可复用**（如 until 引用本步的 lure_spot 落点）。**有效目标语义**：`nearest_enemy(self)` / `all_in(zone)` 只返回**清醒敌对活体**（排除 knocked_out / dead / 昏迷）——击晕/击杀的目标自动移出候选，`count(...) = 0` 循环才能终止（case P 全部击晕 = count 归零的前提）；**query 求值失败**（找不到满足条件的落点/目标）→ 该步失败，走 `on_timeout` / 缺省 abort，不静默。`stand_spot(target, anchor)`（站位：目标对侧 + 可达 + 不被目击者看见，E 用）、`zone(名称)` / `point(描述)`（语义锚点：预定义 Zone 或动态空间查询，J/L 用）同属 query 词表 | **v2** | N/P、G、A、E/J/L |
| **步骤级 actor 寻址** | `actor: c1 / "all"`——**计划属于小队**：步骤可分配给任何执行者；单随从 = 全部缺省 self | **v1** | Q1-Q6（一带多） |
| **actor 维度并行** | 步骤按 actor 分组，各 actor **并行推进**（actor 内串行）；跨 actor 同步用步骤前置条件 `when`（等世界状态），**不提供步骤间 wait_for 依赖** | **v1** | Q2/Q4/C |
| 相对站位 | 复用 `FollowAgentAction` 极坐标参数（radius+angleOffset） | v1 | Q2/Q6 |
| 台本 | 结算型步骤的 `script`（§5.5） | v1.5 | W4/W9（讨债/切磋） |
| 步骤级跳转 | `on_timeout` / `on_success` / **`on_event`**（超时/完成/**事件到达** → 跳转指定步骤；`on_timeout` 缺省 @abort_gracefully、`on_success` 缺省顺序下一歩；`on_event` = 本步执行期间收到决策结果事件（§6.3 广播的 `refused`/`followed` 等）→ **即时跳转，不等超时**，跳转目标随步骤而异（s3 拒绝 → s10 再哄；s11 再拒绝 → s12 放弃）；预案经此跳回主链） | v1 | A（拒绝再哄 / 再哄成功回主链 / 引开超时跳窗口） |

```json
// 循环段示例（case N 清剿：逐敌作战直到 zone 无敌人）
"loop": {
  "steps": [
    {"action": "move_to", "target": {"query": "nearest_enemy(self)"}, "within": 2.0, "timeout_s": 15},
    {"action": "order_attack", "target": {"query": "nearest_enemy(self)"}, "timeout_s": 30}
  ],
  "until": {"type": "count", "of": "all_in(zone)", "op": "=", "value": 0}
}
```

**实现要点**：循环段 = 执行器 step pointer 段内回跳 + until 谓词求值；query refs 由引用解析层运行时解析（查快照 + 谓词过滤），与静态 refs 共用同一解析入口。

**循环内步骤的退出路径（四层，不需要 break 语法）**：①步骤 `until`（条件成立提前完成本步——如目标失效 → 回循环顶重 query）；②步骤 `timeout_s`（超时 = 本步失败 → 回循环顶重新求值 `loop.until`，清空则退出、否则继续）；③`loop.until`（**正常退出**：zone 清空）；④Guardrail（**异常退出**：R1 玩家战斗 Paused / R2 随从死亡 / R5 敌对 → 循环中止，计划不用写 break）。

**语法缺口清单（§0.1 的 16 份 JSON 示范暴露，v2 补齐）**

| # | 缺口 | 说明 | 涉及 case |
|---|------|------|-----------|
| 1 | 逻辑组合 `and` | 复合条件（`distance<3 && !moving`）缺组合节点——B/E/M 的 goal 与脱战判定需要 | B/E/M |
| 2 | 结果路由 `result{}` | 判定型原子（steal_attempt/knockout）有内部结果（success/empty/impossible/interrupted），需按结果跳转；`on_timeout` 只覆盖超时 | C/D/G |
| 3 | 瞬间事件谓词 | ✅ 已裁决：步骤级 `on_event`（§5.0 步骤级跳转行）——决策结果事件（`refused`/`followed` 等）进计划侧，执行器本步期间即时跳转，timeout 降级为兜底；case A s3/s11、G g3/g11、I i3 已用 | A/G/I |
| 4 | `knocked_out` 谓词 / `knockout` 原子 | ✅ 已落地：`knockout` 原子（§4）+ `knocked_out` 谓词（§5.2）已注册（G/P 用）；**残余**：P 的"目标醒转"检测（knocked_out 翻转）语义待定 | G/P |
| 5 | 新查询/动作落语法 | ✅ 已落地：`make_noise` 原子（§4）、`stand_spot`/`zone`/`point` 查询（§5.0 动态目标引用行）、`lead`（§4 已有）；**残余**：语义锚点解析（"河边"→zone）的运行时实现能力待验证（case J 预检①） | F/E/J/L |
| 6 | 保持位时长豁免 | ✅ 已解决：`wait` 省略 seconds/until = 无限保持（§4 wait 行）+ §5.3 的 30s 默认豁免 + R6 事件驱动豁免；H h2 / O o1 已用 | H/O/E/F |
| 7 | 预期战斗声明与 abort 兜底边界 | ✅ 已裁决：O 的 abort 兜底改为 and 组合（`combat(self) && !combat(rival, player)` 才 abort——压阵时被第三方攻击 → 中止；参战期间豁免），见 O 示例 | O |
| 8 | R2 对战斗意图豁免 | ✅ 已解决：§7.1 R2 行补战斗意图豁免注释（战斗意图的目标死亡 = GOAL 达成，不触发 Aborted；随从死亡/非战斗意图目标离场仍触发） | M |
| 9 | 掉线预案只保一次 | ✅ 已解决：E/F 掉线 contingency 改 `one_shot: false`（EDGE 上升沿触发，恢复后再次掉线可反复触发），见 E/F 示例 | E/F |
| 10 | 收尾报告机制 | ✅ 已落地：loop 后接 steps（报告步骤）已定义（§5.0 循环段行，N/P 示例已用）；L `{OBSERVATION}` / N `{BATTLE_REPORT}` 占位符运行时填充（PlaceholderResolver 先例 §5.5 `{AMOUNT}`）；**残余**：PlaceholderResolver 的报告占位符扩展实现 | L/N/P |

### 5.1 Plan JSON（LLM 输出，封闭语法）

> **示例计划**（case A 完整实例：含再哄/放弃预案、`ask` 邀请桥、`on_timeout`/`on_success` 步骤级跳转）已搬到 **§0.1 case A**——树是逻辑视图、JSON 是执行视图，并排阅读；条件角色标注见 case A 示例下方。本节只讲语法规则。

> **折返检测语义**：示例用 `following(guard, self) == false && was == true` 表达"守卫回岗"——`was` 修饰符记录谓词**曾成立**（v1 曾用 `time_since(s7)` 组合判定，`was` 是同义的显式语法），由此区分**"从未跟随"**（多疑守卫不跟走，`was` 从未置真 → s3 超时 → 走"再哄/放弃"预案，测试矩阵 case A 多疑行）与**"停止跟随"**（真折返，`was` 曾真后回落 → 跳 s8），防止误报"守卫回岗了"。ReactiveAgent `return_post` 状态（§6.5 暴露）可与 `was` 互为校验。

> **线性 JSON ↔ 树状 case 的关系**：case 树状图是**逻辑视图**（给人读：分叉/成败一目了然）；执行的是**线性 `steps[]` + `contingencies`**（执行视图：主链顺序推进，分叉 = `when` 条件成立跳转对应步骤）。同一逻辑两种表示，**JSON 不需要写成树状**——执行器是线性游标 + 跳转，没有树遍历。
> **contingencies 装"一切意外"**：异常收尾（警戒 Alarmed → @abort）、安全网（combat → @abort）、预案跳转（折返 → 跳 s8 入口）都放这里，跳转目标 = 主链步骤或 `fallbacks` 预案区入口；**顺利路径的推进全部由步骤 `until` 承担**——until 成立 = 步骤完成 = 主链自然推进到下一步（示例 v1 曾把"离岗 10m sustained → 跳 s7"写成 contingency，与 s6 until 冗余，已删除）。**树上的 ✓ 分支 = 主链 + until 达成**（守卫跟走由 s3 until 显式判定）；**树上的每个 ✗ 分支必须落到 contingencies 或超时路径**（case A 示例已含"守卫拒绝 → 再哄 → 放弃"完整预案 s10-s12；超时跳转走步骤级 `on_timeout`——§5.1 折返检测语义已要求区分"从未跟随/停止跟随"）。
> **主链与预案分区（fallbacks）**：`steps` = 主链（游标顺序推进，走完越界即收尾）；`fallbacks` = **预案数组**（数组的数组：每个元素 = 一个预案的步骤序列；**不参与游标推进**，仅 contingencies 或步骤级跳转（`on_timeout`/`on_success`/`on_event`）进入预案内入口步骤，顺流限在预案内，预案尾完成 → 回收尾判定，不溢出到下一个预案）——失败预案（s8 折返警报 → s9 fail 收尾）必须放 fallbacks，躺在主链上会被线性游标顺流执行（s7 发完"快动手"后假发"快收手"）。**步骤级跳转**：`on_timeout`（超时 → 跳转，缺省 @abort_gracefully）、`on_success`（完成 → 跳转，缺省顺序下一歩）、`on_event`（事件到达 → 即时跳转，§5.4 事件通道）指定跳转目标，预案经此可**跳回主链**（case A：s11 再哄成功 → 跳回 s4 继续引；s4 引开超时 → 跳 s6 直接等窗口）；跳转目标引用不存在的 step id → 忽略跳转按默认处理。**goal 收尾时序**：goal 达成 = 计划成功，但收尾检查放在**"步骤完成时"**而非每 tick 抢断——s6 完成瞬间 goal（同一条件）即达成，须等 s7 信号发完（3s）再收尾，否则"快动手"发不出去。
> 🔴 **计划语法铁律：跳转与预案入口双向校验（prompt 设计纪律 + 静态脚本检查）**：
> ① **每个跳转必须有明确目标 id**——`on_timeout` / `on_success` / `on_event[].then` / `contingencies[].then` / `result` 路由 / `time_since.step_id` 引用的 id 必须真实存在（`steps` / `fallbacks` / `loop.steps`），或为 @-保留指令（`@abort_gracefully` 等）；
> ② **每个 fallback 的第一条 id 必须有注入源头**——`fallbacks[i][0].id` 必须被至少一个跳转引用，禁止写"没人能跳进去"的死预案；跳进 fallback 只能跳**入口步**（预案内顺流），禁止跳中间步（会跳过开头的信号/动作）；
> ③ **JSON 生成后必须过静态脚本检查**——`Scripts/validate_plan_json.py`：提取文档/文件中的全部 plan JSON，校验 S1 目标存在 / S2 入口可达 / S3 不跳预案中间步 / S4 id 唯一，退出码非 0 = 不通过。手写示例与 LLM 输出（进 `custom.plan_debug run` 前）都过此脚本；运行时 PlanValidator（§5.3）执行同一规则（缺失 → 忽略跳转 + 日志警告）。
> **分叉 = 双通道（事件即时跳转 + 状态轮询推进）**：对方**决策结果**（拒绝/接受）由 ReactiveAgent 事件广播（§6.3：`refused`/`followed`），执行器步骤级 `on_event` **即时跳转**——守卫 2 秒拒绝，随从 2 秒去再哄，不干等超时；**持续事实**（`following(guard, self)` 是否成立、`sustained` 防抖、距离/组合条件）由每 tick（100ms）**轮询谓词**看到；`spoken_to` 等输入事件只在 ReactiveAgent 内部触发演算。`timeout_s` 是兜底不是主信号（say_to 未被听到/事件未达时兜底推进）。

**端到端执行画面（case A，M1 开发期测试数据跑通的画面）**：

```
玩家密谋："我想偷那箱子，有人盯着怎么办？" → LLM 一次调用（意图 DISTRACT + 计划 JSON + 守卫反应计划）→ 玩家批准"同意，去办" → 执行开始（全程零 LLM）：

t=0    执行器启动；随从 NpcIntent = ExecutingCommand(引开)
t=0~3  随从走向守卫（move_to）——玩家看到随从真走过去
t=3    随从冒泡"那边有人找你…"           ← 台词来源①：plan step.text
       执行器广播 asked_to_follow(guard)（s2 带 ask: follow）→ 守卫反应表演算（duty 高 → 跟走权重下调）← 来源②：ReactiveAgent
t=5    守卫跟走 → 随从带路走向 lure_spot；随从状态行"正在把守卫引开"（执行摘要）
t=20   GATE 达成：distance(guard, watch_point) > 10 sustained 5s
t=20   密信"我把他引开了，快动手！"        ← 来源①
       玩家去偷（玩家侧自理）
t=20+  守卫折返（following 变 false）→ 密信"守卫回岗了，快收手！" ← 来源①
t=X    玩家按停止键（R3）→ 收尾三路 → 随从回默认跟随（NpcIntent = Following）
```

**三种台词来源的选用规则（何时用台本）**：

| 场景 | 用哪个 | 为什么 |
|------|--------|--------|
| 只说一句话（诱骗/邀请/恐吓） | plan step.text（LLM 计划期写死） | 单句台词，执行期零 LLM |
| 对方"要不要配合"（跟不跟/拒不拒绝） | ReactiveAgent 反应表 | 人格决定，运行时演算，LLM 只写骨架 |
| 结算型步骤（谈价/讨债/比武） | 预写台本（script，§5.5） | 结果由公式结算，台词必须与结果一致——多轮对话预写 + 结算结果选分支 |

**判据一句话**：这一步的结果由**公式结算**还是**人格反应**决定？公式 → 台本；人格 → 反应表；只是说一句话 → 计划文本。例：case B 请村长 = say_to 文本 + 村长反应表（**不需要台本**——村长回不回应是人格决定的）；W4 讨债 = 台本（让不让是公式算的）。

### 5.2 封闭谓词词表（条件即数据，不执行任意代码）

**实体引用语义**：`self` = 该步骤的执行 actor（单随从 = 随从本人；一带多 = 该步的 actor，§0.6）；`player` = 玩家（永远显式引用，不会指代不明）。例：`following(guard, self)` = 守卫跟随随从；`!seeing(掌柜, player)` = 掌柜看不到玩家。

| 谓词 | 参数 | 语义 |
|------|------|------|
| `distance` | a, b, op, value | 实体↔实体/区域距离 |
| `seeing` | watcher, subject | `CanAgentSeeTarget`。**watcher 三值域**：①具体实体（`guard`/`player`/`self`）→ 单对单可见性（case E：`!seeing(掌柜, player)`）；②`"all"` → ∀ 全部（case F 引众：`seeing(all, companion)`）；③`"any"` → ∃ **任意一个会告发的目击者**——= 犯罪裁决同款判定（清醒 + 非队友 + 看得见 subject，`WitnessCrime.GetWitnesses` 同源；**"小偷被看到"判定，subject 是偷窃者本人而非箱子**；case C 窗口 = `!seeing(any, self)`）。**看到 = 关注到——唯一的感知谓词，无独立 attention 概念**：被缠住/被盯防/对话中/战斗中在感知层面都是 seeing 的组合（combat/对话/following 仍作为行为谓词单独存在）；"看到后怎么办"归反应层（ReactiveAgent）。**安全门控谓词 = 犯罪裁决谓词**：放行与判罪同一函数，零偏差 |
| `alert_phase` | entity, phase | ReactiveAgent 警戒阶段（复用 `AlarmPhase` 枚举，`AgentBrain.AlertPhase` 属性，`AgentBrain.cs:148`） |
| `following` | a, b | a 跟随 b（ReactiveAgent 状态或 brain 动作） |
| `facing` / `moving` / `in_zone` / `combat` | … | 朝向/移动中/入区域/战斗中 |
| `player_action` | action | 玩家蹲下/偷窃等（`PlayerActionType`） |
| `time_since` | step_id, seconds | 步骤完成计时 |
| `dead` | entity | 死亡/离场（安全网共用） |
| `knocked_out` | entity | 目标处于击晕状态（KNOCKOUT 意图的 GOAL/推进判定，G/P 用；"目标醒转" = 此谓词翻转，检测语义见缺口 4 残余） |

**通用时间修饰符 `sustained`**——不属于任何谓词，**所有布尔条件**（distance/seeing/following/…）都可挂载的顶层修饰：
- 语法：`<条件> sustained <N>s`（JSON：条件对象顶层字段 `"sustained_s": N`，case A 示例即此写法）
- 语义：条件**连续成立 N 秒**才对该条件角色生效（防抖确认）——"成立一次不算数，连续成立才算数"；缺省 = 0s（瞬时生效）
- 钳制：N ≤ 30s（§5.3 参数范围钳制，超限钳制 + 日志警告）
- **TRIGGER 禁用**：上升沿 = 瞬间时刻（"有人来了"），与持续确认对立——边缘触发不防抖
- **何时必须加**：条件有抖动源（路过/转头一瞥/离岗折返）或成立瞬间直接生效会误判——安全门控（`!seeing(any, self)` 窗口）必加、离岗确认（`distance > 10`）必加；**何时不加（缺省 0s）**：运动学到达（`distance < 4` 到位即停）、状态翻转（`following` 由真变假）、行为态（`moving`/`combat`/`dead`）
- 经验值：窗口/目击防抖 **3s**、离岗/折返确认 **5s**

**通用状态修饰符 `was`**——记录谓词**曾成立**（与 sustained 互补：sustained 盯"现在连续成立 N 秒"，was 盯"过去成立过"）：
- 语法：条件对象顶层字段 `"was": true`（case A 示例折返检测即此写法）
- 语义：条件**曾成立过**即置真、此后保持——用于状态翻转检测：`following == false && was == true` = "曾跟随、已停止"（真折返），与"从未成立"（`was` 始终 false = 守卫拒绝）区分，防止把拒绝误报成折返
- 用途：折返检测（§5.1 折返检测语义）；v1 曾用 `time_since` 组合判定，`was` 是同义的显式语法

**完美犯罪安全原则**：想不被发现，**只有"小偷不被看到"**（`!seeing(any, 小偷)` sustained 是唯一安全条件，any = 任意会告发的目击者）——对抗意图的本质都是把小偷从目击者视线里剔除：DISTRACT 移人（引开守卫）、ENGAGE 转人（缠住 + 背对偷窃点）、KNOCKOUT 放倒、COMMOTION 反其道（**故意被看到**，用于脱身/掩护——被看到是目的，不是失败）。

### 条件角色（Condition Role）——每个条件的用途必须显式声明

**角色是条件的"用法"，不是谓词固有属性**（同一个 `seeing` 既可以是门控也可以是保持）。LLM 生成计划时必须清楚每个条件写在哪、干什么用；执行器按位置解释角色：

| 位置 | 角色 | 触发性 × 激活 | 语义 | 例 | 防抖建议 |
|------|------|--------------|------|-----|---------|
| 步骤 `until` | **GATE** | 成立触发 · LEVEL | 条件成立 → 本步提前完成，推进下一步（wait 步骤 = 退出条件；动作步骤 = 提前完成条件） | case A s4：守卫到引开点即走 | 推进类可不加；窗口类必加 |
| 步骤 `when` | **GATE** | 成立触发 · LEVEL | 条件成立 → 本步才放行（前置门控） | case C 偷窃步骤：`!seeing(any, self)` 才动手 | 安全门控必加（3s） |
| contingencies `when` | **GATE** | 成立触发 · EDGE | 条件成立 → 跳转一次（异常收尾 @abort / 跳主链步骤或 `fallbacks` 预案入口；`one_shot` 可配防重入） | case A：警戒 Alarmed → @abort；折返 `following==false && was==true` → 跳 s8 警报 | 掉线防抖按需（折返可挂 sustained） |
| `goal`（顶层） | **GOAL** | 成立触发 · LEVEL | 条件成立 = 计划成功（收尾三路·成功） | case A：守卫离岗 10m sustained 5s | 有抖动源必加（5s） |
| GoalTemplate.Success + Maintain（保持型意图） | **GOAL + MAINTAIN** | GOAL：成立触发 · LEVEL ／ MAINTAIN：**翻转触发** · LEVEL | **执行时序：先达成、后保持**——GOAL 盯**达成的那一刻**（条件首次成立 → 进入保持期）；MAINTAIN 盯**达成之后**（保持期条件持续成立，**翻转 = 掉线 → 预案**）；玩家 R3 叫停 = 收尾 | ENGAGE / COMMOTION / LOOKOUT | 达成判定按语义；掉线防抖按需 |
| 计划 `triggers[]`（事件驱动意图专用） | **TRIGGER** | 成立触发（上升沿）· EDGE | 条件成立（上升沿：false→true）→ signal_player 报告，**计划不结束**（报告后继续等待，可重复触发）；玩家 R3 叫停 = 收尾 | LOOKOUT 望风"有人来了！"（`in_zone(any, watch_zone)` 上升沿）/ SHADOW 盯梢目标停下/会面/离场 | **禁用 sustained**（边缘触发不防抖） |

**执行器规则**：
- **GATE**：不成立 = 等待（步骤不推进）；超时 = `on_timeout` 跳转（步骤配置时）或该步失败收尾（缺省）
- **GOAL**：成立 = 收尾三路·成功
- **MAINTAIN**：达成后保持——条件翻转（掉线）→ 预案（再执行 / 报告玩家）；玩家干预（停止键 / 新命令，R3）→ 收尾
- **TRIGGER**：上升沿成立 → signal_player（文本来自计划）→ 回到等待（可重复触发，非 one_shot）
- **FAIL**：成立 = 收尾三路·失败（**JSON 无独立字段**——意外触发走 contingencies @abort，计划性失败走 fallbacks 的 end_plan / 步骤超时→`on_timeout` 跳转）

### 5.3 PlanValidator（铁律 2：LLM 输出不可信任）

未知动作/谓词/实体/phase → 该步丢弃 + 日志警告；缺 `timeout_s` 补默认 30s（**保持型/无限等待步骤除外**：wait 省略 seconds/until、follow 省略 timeout = 无限保持，不套 30s 与总时长上限，§4 wait 行 + R6）；跳转目标引用不存在的 step id（contingencies `then` / `on_timeout` / `on_success` / `on_event[].then` / `result` 路由 / `time_since.step_id`）→ 忽略跳转按默认处理（@abort_gracefully / 顺序下一歩）；整体失败 → `signal_player` 告知 + 释放控制。**跳转一致性双向校验**（与 §5.1 铁律同规）：①正向——所有跳转目标必须存在，缺失 → 忽略跳转 + 日志警告；②反向——`fallbacks[i][0].id` 必须被至少一个跳转引用（死预案 → 日志警告），且跳进 fallback 只能跳入口步。生成后静态检查走 `Scripts/validate_plan_json.py`（S1 目标存在 / S2 入口可达 / S3 不跳预案中间步 / S4 id 唯一，退出码非 0 = 不通过）。**参数范围钳制**：数值型参数钳到合理上限（`within` = move_to/follow 的**到达判定半径**（走到目标 within 米内 = 本步完成），≤ 5m；距离 ≤ 50m、时长 ≤ 60s、sustained ≤ 30s 等，超限 = 钳制 + 日志警告而非拒收——计划大体可用，只修不合理的量）。校验在 `Planner/PlanGrammar.cs`。

### 5.4 PlanExecutor — 解释器 + 状态机（新增意外处理）

**文件**：`Planner/PlanExecutor.cs`。挂各参与 NPC 的 `AgentBrain.Tick`（100ms 节流）。

**并行执行模型（明确定义）**：
```
计划 = steps[]，每步带 actor
执行器 = 按 actor 分组 → 每个 actor 一个独立游标 + 动作队列
  ├─ actor 内：串行（自己的步骤按序推进）
  ├─ actor 间：并行（互不等待，各推进各的）
  └─ 跨 actor 同步：步骤前置条件 when 谓词（等世界状态成立，如"守卫离开 watch_point"）
```
- **为什么不用 wait_for 步骤依赖**：①违背"通过世界状态隐式协调，无显式握手"总原则——c1 预案跳转后步骤 id 可能消失，wait_for 悬空；`when` 与 c1 怎么做到的无关（引开/打晕/说服都行）；②步骤依赖是剧本内耦合（脆），世界状态是世界级事实（稳）。
- **when 永不成立**（如 c1 失败放弃）→ 步骤 timeout → 该 actor 中止 + 报告，**不拖累其他 actor**（只作废该 actor 的步骤，同 R2 的"按参与方作废"语义，但触发者是 timeout 而非死亡）。
- **单 actor 计划天然线性**：A/B/I 等 case 全部单 actor 串行，不受并行模型影响。
- **prompt 约束**：LLM 表达跨 actor 同步必须用 when 谓词（few-shot："等守卫离开 watch_point"而非"等 s3"）。

**等待机制：双通道（事件即时跳转 + 状态轮询推进）**：
```
事件通道：决策结果广播（§6.3 refused/followed 等）→ 执行器步骤 on_event 匹配 → 即时跳转
状态通道：感知事件（NpcSightSystem / WitnessCrime / 动作完成）→ 世界状态快照（每 tick 刷新）
        → 谓词求值（100ms 节流，O(1)）→ until/when 推进（sustained 由条件计时器积分）
```
- **执行器双通道（事件 + 状态/超时，由语义分工，不是二选一）**：
  ① **事件通道——瞬间决策与瞬间事实**：拒绝/接受/折返/目击等**决策结果必须事件广播**给相关方（§6.3 决策结果广播；复用 `AgentBrain.ReceiveEvent` / `BroadcastEventInRange` 既有通道——`AgentBrain.cs` 全代码库事件驱动先例）。执行器以**步骤级 `on_event`** 消费：本步执行期间收到指定事件 → 即时跳转，不等超时。单机单线程同步投递，可靠性是工程问题（事件入队 + tick 消费），不是架构理由。
  ② **状态/超时通道——持续事实与时间积分**：`sustained` 是时间积分（首次成立 → 计时 → 满时推进），只有状态+计时器能算；组合条件（`distance>10 && sustained 5s`）无法事件化，状态快照上天然成立——这些照旧轮询。
  **分工原则**：决策/瞬间事实 → 事件（即时）；持续事实 → 状态轮询；两者皆可观察的（折返 = following 翻转状态可查，也 = return_post 事件）→ 任选，事件更即时。**每步保留 `timeout_s` 兜底**——事件未达/未发生（say_to 没人听、目标无反应）时按超时推进，双保险。

```
Executing ──(安全网/预案命中)──▶ Paused（等待条件解除）
   │ 每 tick：预案 → 成功 → 失败 → 安全网（运行时级硬规则同帧求值；R5 对计划预期状态放行，见 §7.1——不是"先于计划条件"，是"与计划条件共享同一豁免判定"）
   │
   ├─ 可恢复（战斗结束/玩家回来）→ 恢复 Executing（若目标仍有效）
   ├─ 不可行（目标死亡/离场/敌对）→ Aborted + signal_player(原因)
   └─ 需要新方案 → Replan（低频 LLM 重入，见 §7）
```

- **until 提前推进**、**超时**（`timeout_s`，步骤可配 `on_timeout` 跳转预案/主链步骤，缺省 → `@abort_gracefully`）、**收尾统一三路一函数**（成功/失败/中断一个收口，发信号 → 清队列 → 释放控制 → 恢复默认行为）——沿用 `StealBarVM.CloseReason` 单一收口纪律。
- **报告方式：当面 / 密信，二选一**（收尾时如何告知玩家）：①**当面报告**（默认）——随从 abort 后恢复默认跟随（`FollowAgentAction(Leader)`，AgentBrain.cs:859-871）物理走回玩家身边，到达 ~3m 内触发一句冒泡报告（say_to + face，**转述实情原话**，情报有来源）再彻底收尾。适用：BRING 拒绝（Y2）/ DELIVER 传话 / SCOUT 侦察回报 / FETCH 取物失败等"随从本就该回到玩家身边"的收尾；②**密信报告**（signal_player）——随从脱不开身的场景：LOOKOUT 望风 / SHADOW 盯梢（随从回不来，须留守）+ 紧急中断（R2 目标死 / R5 开战，随从可能在战斗或离玩家远）。**选型规则：收尾时随从按默认行为本来就回得来 → 当面报告；回不来或紧急 → 密信报告。**
- **玩家干预（两档）**：①**停止键**（轻量专用，仅对执行中的随从：交互距离内当面喊停（say_to 冒泡）；随从离远 > 交互距离 → 密信中止——当面/密信双通道对称覆盖玩家→随从方向，§8.1）；②**新命令**：再次 Plot 下新命令 → 旧计划作废（玩家最高优先级，自由感铁律）。
- **与既有警戒系统交接**：玩家真实犯罪 → 既有 `WitnessCrime` 流程接管（`StealManager.cs:474` 等），执行器监听 `combat`/`alarm` 谓词自动收尾
- **执行状态反馈（KCD2 反馈明确原则，两层显示）**：①**NpcIntent 文本**（既有通道）：`ExecutingCommand` 拼接 = **"执行计划中·引开→守卫"**（状态 + 命令类别 detail + 目标 target，与既有 Confronting 拼接模式同构，本地化 `LWN_ui_npcintent_*` + `LWN_ui_commandintent_*`）；②**执行摘要**（AgentHudVM 随从状态行）：执行器每步一句动态细节（"正在把守卫往巷子引"/"正在前往村长家"）——粗状态、命令类别、步骤细节三层可见，玩家随时知道随从在忙什么
- **执行物理可见（玩家可尾随）**：计划的每一步都是玩家可见的真实物理行为——move_to 真走、say_to 真冒泡、steal 真蹲下，**无瞬移、无抽象结算**。玩家发令后可全程尾随随从观察"是不是真听话在做了"（R4 允许随从独行走远，玩家跟得上就跟；跟丢了有执行摘要兜底）。执行摘要 + 物理行为 = 反馈明确原则在执行的完整落地
- **快照惰性求值**：距离矩阵只对计划引用的实体对计算（O(N) 而非 O(N²)），缓存引用相关的中间结果

### 5.5 结算型步骤的台本化演出（script 字段）— 已拍板

**问题**：结算化（negotiate/duel）是"技能 vs 参数"确定性出结果，但干巴巴弹结果会出戏；逐句对话又违反执行期零 LLM。

**方案**：台本在**计划阶段**与计划一起生成——LLM 为每个结算型步骤预写多轮对话台本，台词分支与结算结果枚举一一对应。执行期：播开场 → 运行时结算（瞬间）→ 播对应结果分支 → announce 汇报。**零 LLM，场面顺，结果与台词一致**。

```json
{
  "id": "n1", "action": "negotiate", "target": "merchant", "topic": "酒钱", "desired": "打八折",
  "script": {
    "opening": [
      {"self": "掌柜的，我家主人常来光顾，这酒钱能否让两成？"},
      {"target": "让两成？你倒会说话……"}
    ],
    "outcomes": {
      "success": [{"self": "就按这个数，回头我带人来拿。"}, {"target": "行吧，当交个朋友。"}],
      "partial": [{"self": "要不各退一步？"}, {"target": "最多让一成，再低我就亏本了。"}],
      "fail":    [{"target": "本店小本经营，爱买不买。"}, {"self": "……那算了。"}]
    },
    "announce": "谈成了，{AMOUNT} 到手。"   // {AMOUNT} 由运行时按结算结果填充（PlaceholderResolver 模式）
  },
  "timeout_s": 15
}
```

**四条铁律**：

1. **结果由运行时结算，台词只是包装**——LLM 写台本不能决定结果。目标的"让不让"由技能 vs 参数算出；台本分支必须覆盖运行时全部结果枚举（negotiate: success/partial/fail；duel: win/draw/lose），validator 校验缺分支 → 拒收该步骤。
2. **台词跟随结算**——播的分支 = 结算结果，保证"谈成了"绝不配"爱买不买"的台词。
3. **目标台词是台本发言**——self 台词走执行器（AgentSay + face 纪律）；target 台词由执行器直接 `AgentHudMissionView.AgentSay(targetAgent, line)` 按序播放。**ReactiveAgent 只参与结果侧参数（是否松口），不碰台词文本**——防止"写对手台词 = 自说自话"。
4. **台本播放期间执行器独占目标**——结算步骤播放时，目标的 ReactiveAgent 反应表**挂起**（其 `spoken_to/approach_by` 等触发词不响应），播完本步才恢复。否则目标一边播台本一边被自己的反应计划驱动（又拒绝又走开/重复发言=双重人格）。实现：结算步骤启动时对该目标 `ForceUnlockAgent` + 挂起反应表，结束（含分支播完）后恢复 + 重启反应表。

**通用能力**：台本播放 = 执行器的通用"多句台词序列"能力（每句间隔 ~1.5s + 面向对方，逐句间隔走缩放 dt 纪律），任何结算型/演出型步骤复用——`duel`（开场叫阵 → 比武结算 → 胜负台词 + 水平评估回报）与 `negotiate` 同构。**序列推进者是执行器**（非对话双方自主对答）：lines = [{speaker, text}] 队列，播放循环 = 取行 → speaker face 对方 → AgentSay → 间隔 → 下一行；说话者可含第三方（不止 self/target）。播放期间执行器独占说话双方（反应表挂起，铁律 4）——**"来回"要么预写（台本，确定性播片），要么实时（ReactiveAgent 反应表，人格驱动），不存在两套同时抢说话**。

**结算步骤可配 `result` 路由**（success/partial/fail → 跳转，与判定型原子同规 §5.0 缺口 2）——fail 可跳失败收尾，缺省 = 播完顺序下一歩。

**完整 PlanResponse 模板示范（prompt few-shot 用，自身也过 §5.1 铁律校验）**：`script` 字段只出现在结算型动作（negotiate/duel）上，分支枚举与运行时结算结果一一对应（缺分支 validator 拒收），目标台词写进 `script.outcomes.<branch>.target`（铁律 3），`announce` 走 PlaceholderResolver（`{AMOUNT}` 先例）：

```json
{
  "intent": {"intent_type": "COLLECT", "subjects": ["companion"], "target": "merchant", "amount": "debt", "who_does": "companion"},
  "summary": "我去把酒钱讨回来。",
  "steps": [
    {"id": "w1", "action": "move_to", "target": "merchant", "within": 2.0, "timeout_s": 20},
    {"id": "w2", "action": "negotiate", "target": "merchant", "topic": "酒钱", "desired": "全额",
        "script": {
            "opening": [
                {"self": "掌柜的，上回的酒钱该结了吧？"},
                {"target": "催什么催，这不生意还没周转开么。"}
            ],
            "outcomes": {
                "success": [{"target": "行行行，拿去吧，别再来烦我。"}, {"self": "痛快，告辞。"}],
                "partial": [{"self": "要不先还一半，剩下的下月再说？"}, {"target": "……就一半，别再催了。"}],
                "fail":    [{"target": "没钱，你看着办。"}, {"self": "那我回去禀报主人。"}]
            },
            "announce": "要到 {AMOUNT}。"
        },
        "result": {"success": "w3", "partial": "w3", "fail": "w4"},
        "timeout_s": 15},
    {"id": "w3", "action": "end_plan", "result": "success", "timeout_s": 3}
  ],
  "fallbacks": [
    [{"id": "w4", "action": "end_plan", "result": "fail", "report": "没要到钱，那掌柜说没有", "timeout_s": 3}]
  ],
  "contingencies": [
    {"when": {"type": "combat", "entity": "self"}, "then": "w4", "one_shot": true}
  ]
}
```

---

## 6. ⑤ ReactiveAgent — 通用对抗方/被叫者模型（取代 v1 的 RivalBrain）

**任何 NPC（守卫/村长/商人/路人）都可能是计划的相关方**。ReactiveAgent 是同一套框架的两种用法：

- **对手方**（DISTRACT 的守卫）：有"职责记忆"（离开岗位到点折返）→ 制造对抗
- **被叫方**（BRING 的村长）：人格决定是否跟来 → 制造"请得动请不动"的差异

### 6.1 反应计划（LLM 在计划阶段为整场戏生成，每 NPC 一份）

> **驱动方式**：事件驱动（与计划侧双通道配合：计划侧事件通道消费本表广播的决策结果，状态通道轮询持续事实，见 §5.4）。触发词 = "瞬间时刻"，由动作完成广播触发（执行器在 say_to/接近/攻击完成时广播；既有感知系统广播观察/犯罪事件）。

```json
{
  "role": "guard",
  "personality": {"gullibility": 0.3, "duty": 0.9, "temper": 0.6, "social": 0.4, "greed": 0.2},
  "responses": [
    {"event": "approach_by(companion)",      "reactions": [{"action": "listen", "weight": 0.9}, {"action": "warn_away", "weight": 0.1}]},
    {"event": "spoken_to(companion)",        "reactions": [{"action": "consider", "weight": 0.7}, {"action": "refuse", "weight": 0.3}]},
    {"event": "asked_to_follow(companion)",  "reactions": [{"action": "follow_for_a_bit", "weight": 0.5}, {"action": "refuse", "weight": 0.5}]},
    {"event": "player_suspicious_near(watch_point)", "reactions": [{"action": "investigate", "weight": 0.8}]},
    {"event": "left_post_seconds(20)",       "reactions": [{"action": "return_post", "weight": 1.0}]}
  ]
}
```

### 6.2 触发词表（通用，不特化守卫）

`approach_by(x)` / `spoken_to(x)` / `asked_to_follow(x)` / `asked_to_stay(x)` / `player_suspicious_near(zone)` / `see_crime(agent)`（WitnessCrime 已有）/ `combat_nearby(x)` / `left_post_seconds(t)` / `alone_with(player, zone)` / `seen_speaking(x, y)` / **`see_ally_killed(ally)`**（→ 恐慌传播：flee/attack/call_guards，经 `BroadcastEventInRange` 链式——看到逃的人也跟着逃）…

**传播作用域**：恐慌链（`see_ally_killed` → flee/call_guards）是**唯一允许链式传播**的反应——经 `BroadcastEventInRange` 按半径衰减、单链 ≤ 3 跳；其余反应（investigate/warn_away/stare 等）**只作用于触发者本人**，不触发他人同类反应（守卫 investigate 不会引发路人连环 investigate）。

### 6.3 反应词表（全部映射到 §4 原子行为）

`listen` / `consider`（短暂犹豫）/ `refuse`（说句拒绝的话 + 不动）/ `follow_for_a_bit`（→ `follow` 动作）/ `investigate`（→ `move_to` zone + `look_at`）/ `return_post` / `stare` / `alert_raise`（→ `brain.AddAlert` 脉冲，复用 `AgentBrain.cs:1060`）/ `attack`（→ `FightEnemyAction`）/ `call_guards`（→ `BroadcastEventInRange`）/ `ignore` / `relay_message`（→ 转告他人，信息经 NPC 链传递——通报门卫让主人来见的间接 BRING）/ `pay`（→ `TransferGold` 守恒转移，铁律 4）/ `hand_over_item`（→ `TransferItems`）…

**后置状态**：每个反应词带"执行完回哪"的通用语义（investigate → 回岗位或继续盯；follow_for_a_bit → 到点自动回位或折返；listen/refuse → 回 Idle）——§6.1 的守卫状态机是这套通用语义的一个实例，不是特例。

**决策结果广播**：涉及"跟不跟/答不答应"的**决策型反应执行后必须把结果广播给请求方**——`follow_for_a_bit` → 广播 `followed(请求方)`；`refuse` → 广播 `refused(请求方)`（**统一事件名**，拒绝任何请求：拒绝跟随/拒绝传话——case A/G 的 ask:follow 与 case I 传话共用，执行器按发送者 + 当前步骤的 `on_event` 匹配）。广播走既有 AIEvent 通道（`AgentAIController.SendEventToAgent` / `BroadcastEventInRange`，`AgentBrain.ReceiveEvent` 模式），执行器步骤级 `on_event` 消费（§5.4 事件通道）。`followed` 与 `following` 状态并存：事件给即时性（拒绝零延迟可见），状态给可查性（轮询/防抖兜底）。**BRING 的逗留窗口**：被叫方到达后进入逗留（下限 ~10s，人格修正：duty 高呆得短），**开口问事**（`NpcSpeechResolver` 模板台词"找我什么事？"走 `LWN_speech_*` key，人格变体可选；LLM 反应表可覆盖为性格化台词）——逗留既是被请到位的反馈，也是"快搭话"的行动暗示；玩家对话/交互 → 回岗取消（对话结束才走），玩家不理 → 到点自行回岗。

### 6.4 运行时演算（对抗性的保障）

**LLM 写剧本骨架，运行时按人格"演"强度**——防止"LLM 自说自话"（LLM 想让守卫蠢到跟走，但运行时参数不让）：

- 反应选择的确定性：weight 初值由 LLM 写（剧本感），运行时按人格修正（`duty` 高的 NPC，`follow_for_a_bit` 的 weight 下调、`refuse` 上调），取最高者为本次反应
- 反应强度/时长由人格决定：`follow_for_a_bit` 的跟随时长、`return_post` 的折返时机（`left_post_seconds(t)` 里的 t = 运行时按 duty 计算，LLM 给的 t 只是上限）
- **默认人格模板兜底**：LLM 没给某 NPC 写 responses → 按职业默认模板（守卫 duty 高/酒客 gullibility 高/**未知职业 → 中性值兜底 duty=social=temper=0.5**），LLM 的 responses 只是覆盖默认——**对抗性不依赖 LLM 写得好不好**

### 6.5 驱动与状态暴露

- 通过该 NPC 自己的 AgentBrain 驱动（move_to/look_at/follow…全部是同一套原子行为）
- 状态（following / alert_phase / left_post）暴露给随从计划的谓词——**随从的预案就是基于对对手状态的预判写的**
- 计划结束 → `ForceUnlockAgent` 恢复原版 AI；检测到既有 `StartL3Confrontation`（`AgentBrain.cs` L3 质问）让位

---

## 7. GuardrailEngine 安全网 + Replan — 意外处理（新增）

### 7.1 运行时级硬规则（所有计划共享，不写进任何计划，不特化任何场景）

| # | 规则 | 触发 → 行为 |
|---|------|------------|
| R1 | 玩家进入战斗（`Agent.Main` 被攻击/开战） | 计划 **Paused** → 随从转护卫玩家（既有护主逻辑 `AgentBrain.cs:304` 复用）→ 战斗结束且目标仍有效 → 恢复 |
| R2 | 计划参与者死亡/离场 | 相关步骤失效 → **Aborted** + `signal_player(原因)`。⚠️ **战斗意图豁免**（缺口 8 已裁决）：ATTACK/ANNIHILATE 的**目标死亡 = GOAL 达成**（§2.1 目标状态），不触发本规则——本规则只管**执行者（随从）**死亡/离场，与非战斗意图的目标离场 |
| R3 | 玩家干预：按停止键（仅对执行中的随从：近距离当面喊停 / 远距离密信中止，§8.1）或下达新命令（再次 Plot / `order_*`） | 旧计划作废（玩家最高优先级） |
| R4 | 玩家与随从距离 > 30m | **Paused** → 随从追回玩家身边 → 恢复。⚠️ **豁免随从独行任务**：当前步骤的 target/zone 远离玩家 > 30m（DELIVER/FETCH/SCOUT/BRING 等"离开玩家去办事"）→ R4 不触发——玩家走开不能把正在远处办事的随从叫回来（按世界状态判定，不用意图白名单） |
| R5 | 计划目标变为敌对（target 与人开战，**包括守卫和玩家打起来**） | **Aborted** + 报告；若玩家主动结束战斗 → 可选 **Replan**。⚠️ **计划预期的状态不触发**：①战斗型意图（ATTACK/ANNIHILATE/批量 KNOCKOUT）**自动豁免**（combat 是其正常进展，不依赖 LLM 声明）；②非战斗意图下 LLM 把 `combat` 写进计划**任何条件**（success / fail / **contingencies 的 when**）也可豁免——**必须含 contingencies**：case O（条件参战）的 `combat(对手, 护卫对象) → attack` 只在预案里，漏了预案 → 预期战斗被误杀。Validator 按「意图 → 允许声明的战斗谓词」白名单校验（ATTACK/ANNIHILATE/批量 KNOCKOUT/GUARD-参战 可声明 combat；纯 DISTRACT/BRING 声明 combat → 拒收），防 LLM 借豁免钻空子 |
| R6 | 计划总时长 > 5 分钟 | **Aborted** + 报告（防僵局）。⚠️ **豁免事件驱动计划**（LOOKOUT/SHADOW 等 success 为 null 的意图）：守望/盯梢是"无限期待命"，套总时长上限会在正常守望 5 分钟时误报"计划超时"——这类计划由玩家手动叫停（对随从再 Plot 说"行了，回来吧"→ R3 作废）或 R4 收尾 |
| R7 | **玩家进入模态 UI**（偷窃条子弹时间/原版对话/剧情演出） | 计划 **Paused**（随从原地待命）→ 模态结束恢复——防止随从在 0.35x 子弹时间里说话走路的出戏场面，以及随从与玩家对话对象抢互动 |

安全网命中后判定：可恢复（R1/R4/R7）→ Paused；不可恢复 → Aborted；或可 replan（R5 战斗结束）→ Replan。

**多计划并发**：玩家可对不同随从分别下令（多个计划并行，actor 集合互斥）；同一随从再下令 → R3 作废旧计划。⚠️ **相关方互斥**：同一 NPC 同时只能是一个计划的相关方（执行者/目标/ReactiveAgent）——两个计划都想引开同一个守卫 = 守卫只能持一份反应计划。第二个计划引用同 NPC → 执行前拒绝并提示"他和上一个活儿有关，我顾不过来"，或由玩家选择作废旧计划（ReactiveAgent 反应表按 NPC 单例，无合并机制）。

**Mission 生命周期**：计划是 Mission 级瞬态——`OnMissionScreenFinalize` 全部执行器统一收尾（沿用 `StealBarVM` Finalize 兜底纪律）；玩家离开场景 = 计划作废，回来需重新下达（v2 可让随从提一句"上次那事没办成"，走既有记忆系统）。

**系统消息本地化**：Guardrail 的 abort 原因等**运行时生成**的消息走 `LWNTextHelper` + `std_*.xml`（铁律 13）；LLM 生成的 signal 文本是运行时内容直接显示。

### 7.2 Replan — 低频 LLM 重入（唯一允许执行期之外再调 LLM 的路径）

**为什么合法**：replan 是"意外 → 重新进入计划阶段"，频率极低（一次意外一次），符合"计划阶段可容忍 LLM 时间"的约束。**执行期的每步动作仍然零 LLM**。

```
意外 → 执行器记录事件日志（"守卫与玩家发生战斗，s3 未能完成"）
     → 暂停随从（原地等待/护卫玩家）
     → LLM 调用：原命令 + 原计划 + 意外事件日志 + 新场景快照
     → 输出：新计划 / "不可行"（→ 报告玩家，结束）
     → 玩家无需重新批准（原命令仍在），但新计划的 summary 会再次播报
```

- **节流**：同一计划的 replan 上限 2 次，超限 → Aborted（防死循环烧钱）
- **玩家被打的细化**（用户问题）：R1 暂停后随从参战（既有护主）→ 打完后若原目标（守卫）仍活着且未敌对 → 恢复；若守卫成了敌人 → R5 → Aborted/Replan

---

## 8. 密谋对话壳（计划阶段的 UX）

**文件**：`Interaction/PlanCommandFlow.cs`

### 8.1 交互入口（新玩法行，四处改动，全有轮子）

| 改动点 | 位置 | 内容 |
|--------|------|------|
| 玩法 ID | `Input/ModInput.cs:13-26` | `InteractionIds.Plot` |
| 键位行 | `Core/Settings.cs:104-114` | G 键长按（AltInteract 空闲，无冲突） |
| available | `InteractionMissionView.cs` `BuildAgentContext`(:564-650) | 条件：`brain.Leader == Agent.Main`（随从关系由 `FollowIntent` 建立，`GeneralIntents.cs:78-86`）。**执行器可驱动任意 NPC 当执行者，入口限随从是叙事选择**——v2 可放开到雇佣/临时伙伴/酒馆帮手，框架零改动（这是入口与执行器不对称，属特性非缺陷） |
| 分发 | `ExecuteInteraction`(:389-422) | case → `PlanCommandFlow.Start(companionAgent)` |
| **停止键**（新玩法行，键位待定——选空闲短键，不与 G 长按冲突） | `InteractionIds.StopPlan` | available：仅对**正在执行计划的随从**（`PlanExecutor` 活动）；执行：交互距离内 → 当面喊停（say_to 冒泡，复用 §5.4 当面通道）；超出交互距离 → 密信中止（signal_player，玩家→随从方向的对称通道）→ 收尾三路·中断 → 随从回默认跟随 |

### 8.2 计划对话流（复用 StoryDialogVM）

```
密谋（本地化标题）
随从：小声——"你说吧，我听着。"
[自由输入框] 我想偷那个箱子，有人盯着，怎么办？ [发送]   ← 文本输入（新小 UI，TextInputWidget + 手柄预设 chips）
── 快照构建 + 角色表 → LLM 调用（意图分类 + 计划 + 可含 questions）──
随从：你是说箱子旁那个守卫？他站的位置不太好办。
[是他] [不是，是门口那个] [算了]                        ← 澄清轮（最多 2 轮，追加上下文再调）
随从：我去和守卫说门外有人找，把他引到门口，你趁机动。同意吗？
[同意，去办] [再想想] [算了]
```

- 多轮 = 追加上下文再次调用（现有 `RecentHistory` 模式）；**意图歧义优先澄清**（`questions` 非空则不生成计划）
- **选项语义**（对齐自由感）：澄清轮 `[是他]/[不是，是门口那个]/[算了]`——前两项把答案追加进上下文再调一次；批准轮 `[同意，去办]/[再想想]/[算了]`——"再想想" = 回到输入框**重说或追加修改意见**（如"别引到门口，引到巷子里"，上下文追加后重新生成计划），"算了" = 放弃本次密谋、随从回默认行为。两轮的"算了"都走同一收口（释放控制、恢复跟随）
- 响应类型 `PlanResponse`（[JsonProperty] + 全字段 null-guard，`defensive-coding.md`）：`{ reply, emotion, options[], intent, plan, questions, needs_clarification }`
- 密谋对话进行中该随从的 Talk/Pickpocket 行从 available 移除（互斥）
- UI chrome 全走 `LWNTextHelper` + `std_*.xml`（铁律 13）；LLM 台词/计划文本是运行时内容直接显示

---

## 9. LLM 升级点（在现有 `LLM/` 目录内做）

| 文件 | 改动 |
|------|------|
| `LLM/LLMService.cs` | `ChatAsync` 加 `float temperature = 0.7f` 可选参（计划调用传 0.4）；`max_tokens` 传 **4000**（计划 + 台本 + 多 NPC 反应表 + 多 actor 步骤可能超出 2000；DeepSeek 输出上限 8k） |
| `LLM/PromptBuilder.cs` | 新增 `BuildPlanPrompt(snapshotText, command, persona, history, intentTable, grammar)`：意图词表全表 + 动作/谓词封闭词表 + 角色表 + 人设 + 命令 + 澄清历史 + **完整 PlanResponse JSON 模板（§5.5 模板块，含 script 示范步骤）** + "严禁创造未定义 type" + **跳转双向校验纪律（§5.1 铁律）——指令 + few-shot 双重示例**：每个跳转写明确目标 id、每个 fallback 入口必须被引用、禁止跳预案中间步 |
| 新 `Planner/PlanGrammar.cs` | `PlanResponse` 模型 + `PlanValidator` |
| 新 `Planner/GoalTemplates.cs` | 意图→目标状态表（§2.3） |

**IsLLMReady 总闸（铁律 1）**：不可用 → Plot 行不出现/点开提示"随从想不出主意"。v2 可做预设脚本兜底，v1 不做。

---

## 10. 与现有系统的关系（复用清单）

| 现有部件 | 用途 | 改动 |
|---------|------|------|
| `AgentBrain` 动作队列 + `IAtomicAction` | 所有参与 NPC 的执行底层 | 加 `order_execute_plan` 事件分支；执行器挂 Tick |
| `NpcIntent` / `AgentBrain.SetNpcIntent`（`NpcIntent.cs`） | 执行期 NPC 状态显示（互斥状态机，**唯一运行时意图体系**） | **统一架构（无桥接）**：`NpcIntentType` 新增 `ExecutingCommand` + 字段 `CommandIntentType? CommandDetail`（复用既有 `InterceptDetail` 模式，两 detail 按 Type 互斥生效）。**显示**：`ToString` 拼接 = "执行计划中·引开→守卫"（typeName + detailStr + targetStr，与既有 Confronting 同构，key = `LWN_ui_npcintent_executingcommand` + `LWN_ui_commandintent_*`）；细粒度步骤细节 = 执行摘要（AgentHudVM 随从状态行，§5.4）。**值优先**：战斗中 → Fighting、被击晕 → KnockedOut、跟随 → Following（既有值自然表达，不因计划内而变）；计划特有行为（引开/望风/传话/带路…）→ ExecutingCommand(detail)。计划收尾 → 恢复 Following。ReactiveAgent 长时行为走既有值，短时反应（investigate/return_post/say）不设（None） |
| `AgentControlHelper.MoveTo/LookAtAgent/FaceToActor/ForceUnlockAgent` | 原子行为底层 | 无 |
| `AgentHudMissionView.AgentSay` | say_to 冒泡（非原版对话流） | 无 |
| `NpcSightSystem.CanAgentSeeTarget/GetObserversOf` | 视野矩阵 + 角色表"谁在盯着" | 无 |
| `NinjaNotificationManager.Show` | 秘密消息 | 无 |
| `SendEventToAgent("order_*")` | 下令通道（随从 + 对手共用） | 无 |
| `StoryDialogVM` + `InteractionController.StartInteraction` | 密谋对话壳 | 复用 |
| `StealBar`/`WitnessCrime`/`GetWitnesses` | 玩家偷窃 + 目击结算 + NPC 偷窃的目击/脉冲复用；**COMMOTION 引众围观复用**（`BroadcastEventInRange` → `WitnessCrime_GatherOnLook` 围观流，criminal=随从 = 纯围观无犯罪副作用，`AgentBrain.cs:395,499`） | 无（NPC 偷 = 新入口包装；围观流原样复用） |
| `FollowIntent`/`SetLeader`/`IsPlayerTeammate` | 随从关系 + 队友豁免 | 无 |
| `AlertValue`/`AlarmPhase`/护主逻辑 | ReactiveAgent 警戒/折返/参战 | 复用 |
| 谈判 `detected_nogotiation_goal` | 意图分类的先例（封闭词表 + LLM 选） | 无 |
| `LLMService`/`PromptBuilder` | §9 小改 | 参数 + 新方法 |
| 存档 | **不需要**——计划是 Mission 级瞬态数据 | 无 |

**版本兼容**：底层 API 全已封装，无新增跨版本签名；新 UI prefab 双版本 XML 兼容照 `ui.md` 纪律。

---

## 11. 实施步骤（里程碑）

**M1 — 骨架 + case A 执行链（无 LLM）**
- Plot 交互四处接线 + 密谋面板壳 + 文本输入
- `SceneSnapshot` + 角色表 + `PlanGrammar`（词表/验证器）+ `PlanExecutor`（含状态机骨架）+ `order_execute_plan` 事件
- 用 case A 示例 JSON（§0.1）作为**开发期测试数据**跑通**完整交互链路**（对话壳 → 调试占位计划 → 执行；调试占位非产品行为，M2 移除）：move_to→say_to→wait→move_to(lure_spot)→signal_player + 再哄/放弃预案（on_timeout 跳转）+ 折返警报
- **全部 17 份示例跑通 = 验收基线**：每份示例随其依赖原子落地（steal_attempt/negotiate 等按 M2-M5 排期）经 `custom.plan_debug run <json>` 完成执行验证（步骤推进/跳转/预案/收尾），M5 结束时全部跑通——**不允许"只跑 case A 就宣布执行器验证完成"**
- PlanExecutor **多 actor 游标** + 步骤 actor 寻址（一带多 v1 就位：Q1/Q2/Q6 以示例 JSON 经控制台注入验证）
- GuardrailEngine R1-R7 规则框架就位（先以调试触发点验证暂停/恢复/中止路径）

**M2 — 意图分类 + LLM 计划生成**
- `LLMService` 参数升级 + `BuildPlanPrompt` + `PlanResponse` + `PlanValidator` + `GoalTemplates`
- 澄清循环 + 批准循环；LLM 失败降级路径
- case B（BRING）跑通：村长被请来（ReactiveAgent 被叫方行为）

**M3 — ReactiveAgent + 对抗完整化**
- ReactiveAgent（触发词表/反应词表/反应路由/人格演算/默认模板兜底）+ case A 的守卫对抗完整化
- 传播作用域验证（investigate 不链式、恐慌链 ≤3 跳）

**M4 — Replan + case C（STEAL）**
- Replan 循环（事件日志 → 重入 → 新计划）；R5 战斗意外实战验证
- NPC 侧偷窃原子行为（`steal_attempt` 结算/目击复用/§4.1 问责）+ 物品移交 + 玩家引开的窗口检测

**M5 — 打磨 + 意图扩展**
- 本地化条目、冒泡样式、emote 点缀；边界（随从死亡/ESC/并发 Plot）
- 按意图表逐个补 **v1.5 梯队**（ENGAGE/FORMATION/SPAR）+ **v2 梯队**（FETCH/PURCHASE/KNOCKOUT/GUIDE/SCOUT…）——v1.5 在 M5 内（相对站位/台本/切磋的底层都已在 M1-M4 就位），v2 起向后排期
- 双版本编译 + 实机体验（KCD2 标准自查：节奏、信息传递、出戏点）

---

## 12. 验证方案

- **控制台指令**（`Debug/PlanDebugCommands.cs`，`custom` 前缀）：`custom.plan_debug snapshot` / `list` / `run <示例名> [agentId]` / `status [agentId]` / `stop [agentId]` / `role <角色名> [agentId]` / `step [agentId]` / `replan [agentId]`（step = 当前步骤详情；replan = 强制触发 R5 重入链路）
- **全部示例跑通**：17 份示例 JSON 逐一 `custom.plan_debug run <json>` **动态执行验证**（实体引用按快照角色匹配就近解析，§2.2 target 解析）；静态校验（`validate_plan_json.py`）+ 动态执行**双通过** = 该示例合格（§0.1 示例定位）
- **测试矩阵**：

| 场景 | 期望 |
|------|------|
| case A：多疑守卫 | 不跟走 → 预案切换（再哄/放弃） |
| case A：尽责守卫 | 跟走但到点折返 → 玩家窗口内偷完（或失败收尾） |
| case B：村长 social 高 | 跟来 → success 判定 `distance(chief, player) < 3` → 逗留窗口内开口问事（模板台词） → 玩家搭话取消回岗 / 不理他到点回岗 |
| case B：村长拒绝（duty 高） | 拒绝台词 → 随从返回玩家旁 → 当面冒泡转述（台词与原话一致）→ 收尾回默认 |
| case C：分工（随从偷 + 玩家引开） | 随从等窗口（守卫离开 watch_point）→ 偷 → 移交玩家 |
| **意外：玩家被打** | R1 Paused → 随从护主 → 战斗结束恢复 |
| **意外：守卫和玩家打起来** | R5 Aborted + 报告 → replan 或结束 |
| **意外：目标死亡/离场** | R2 Aborted + 报告 |
| **停止计划：停止键** | 执行中按停止键 → 近距离当面喊停 / 远距离密信中止 → 随从回默认跟随（§8.1 停止键行） |
| **意外：玩家下达新命令** | R3 旧计划作废，新命令接管 |
| **模态冲突：偷窃条/对话打开时** | R7 Paused（随从原地待命）→ 模态结束恢复 |
| **随从独行任务时玩家走远** | R4 豁免（当前步骤远离玩家，不叫回） |
| **玩家离开场景** | Mission 结束 → 执行器统一收尾，计划作废 |
| LLM 不可用 / 垃圾 JSON / 未知动作 | 降级/Validator 拒收/该步丢弃，游戏不崩 |
| 结算台本缺结果分支 | Validator 拒收该步骤（分支必须覆盖运行时结果枚举） |
| 结算结果与台本一致性 | 播的分支 = 结算结果；"谈成"不配拒绝台词（§5.5 铁律 2） |
| 词表外命令（v1 未实现意图） | 诚实拒绝："这我做不到" + 提示改述 |

- **四原则自查**：①反馈明确——计划/信号/失败/意外全部显式告知玩家；②自由感——同意/修改/算了 + 玩家随时新命令覆盖；③NPC 接得住——任何随从可密谋、任何 NPC 可当对手/被叫者（人格参数化 + 默认模板兜底）；④信息塑造目标——玩家知道计划但不知道守卫折返时刻与人格演算结果，悬念在执行

---

## 13. 风险与对策

| 风险 | 对策 |
|------|------|
| LLM 意图分类错（"叫村长来"分成 DELIVER） | 歧义 → questions 澄清轮；意图表 few-shot 示例；Validator 参数校验 |
| LLM 写对手剧本自说自话（守卫被写得必跟走） | 人格演算修正 weight + 默认模板兜底 + duty 折返由运行时定 |
| 执行器与既有行为竞争（护卫跟随/警戒质问） | `order_execute_plan` 统一接管；收尾三路一函数；L3 让位 |
| Replan 死循环 | 同计划 replan ≤ 2 次，超限 Aborted |
| AgentSay 冒泡无听者概念 | say_to 前强制 `face(target)` |
| 文本输入双版本兼容 | 最小 prefab + `ui.md` 纪律；手柄走 chips |
| 玩家把随从留在原地自己跑了 | R4 暂停 + 追回 |
| 随从偷窃变零成本最优解 | §4.1 责权归属：随从犯罪 = 玩家代理（同风险结构），目击即问责玩家；随从技能只影响成功率 |
| LLM 一次调用输出失败 | §2.2 降级：一次重试 → signal 告知 + 释放控制 |
