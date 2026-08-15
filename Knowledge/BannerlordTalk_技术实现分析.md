# BannerlordTalk v1.0.0 技术实现分析（外部模组逆向）

> 分析对象：`Modules/OtherMods/BannerlordTalk-v1.0.0-BL1.4.8`（独立大地图闲聊模组，纯展示定位）
> 方法：README/功能说明/提示词文件 + `ilspycmd` 反编译 `BannerlordTalk.dll`（PromptBuilder / CampaignEventMemoryService / StandaloneKnowledgeRetriever / NativeHeroContextProvider / ChatterManagerDataSource / CampaignChatterBehavior / UserPromptBudget / ResponseParser / TTS 全家桶）
> 🔴 **反编译原始快照**：`Knowledge/BannerlordTalk_逆向/v1.0.0/`（模组包整体替换后，按该目录 README 的更新流程 diff 并更新本文档）
> 日期：2026-08-15（v1.0.0 / BL1.4.8 包）

---

## 1. 定位与功能

**独立大地图自动闲聊模组**：主队英雄同伴之间自动闲聊（独白/两人私聊/多人群聊），纯展示——台词只进聊天窗和记忆，**不执行任何游戏操作**。核心不依赖 AnimusForge；可选 Living Commanders 作为战后已验证事实来源。

| 功能 | 说明 |
|------|------|
| 自动闲聊 | 按合格人数自动选模式；**每次请求只生成一名发言者**，严禁代写他人 |
| 人格卡 | 玩家+同伴各一张：人格/说话风格/长期目标/价值/禁忌 5 字段；可手编或 LLM 生成草稿（草稿必须玩家采用，不自动覆盖） |
| 分层记忆 + 私密思绪 | Recent/Situational/EventLog/Archive 四层（各层容量+半衰期）；私密思绪 Mid/Long/Belief 三档按发言数异步生成，只进本人记忆 |
| 公共常识库 | 每战役一份，`[标签\|权重\|Any/All\|可提取\|可匹配]正文` 五段式；游戏外编辑 + 整库粘贴原子替换；附卡拉迪亚/战锤/权游三份库（336/280/382 条） |
| 可编辑提示词 | 主聊天/独立思绪/记忆总结三个模板；纯文本或 JSON 导入；**不可覆盖程序强制段** |
| 安全边界 | 战斗/任务场景硬门、请求快照（地点身份变化→取消）、进城结束旧会话、迟到结果不写入 |
| Fish TTS | 正文/动作/心声分别开关；10 音色槽按 `Hero.StringId` 精确绑定，缺省不按名字猜 |
| Probe | 默认 metadata-only，正文审计需显式开启；永不记录密钥 |

## 2. 核心架构（反编译结论）

### 2.1 双层 Prompt 组装（PromptBuilder + UserPromptBudget）

**System prompt**：身份纪律（只生成当前发言人/禁姓名前缀/禁 Markdown）→ 会话模式声明（群聊/私聊/独白）→ 发言人/参与者 → 正文字数区间（60~240，硬上限）→ 玩家代发言声明（可选）→ 禁游戏面板术语 → 禁 thought 字段 → 【原生稳定身份】（姓名/文化/家族/王国/身份/年龄，2200 字）→ 【人格卡】（2600 字）→ 用户可编辑模板（`{{speaker.name}}` 等变量）→ **不可覆盖段：Presentation 合同**。总预算 12000。

**User prompt**（每段独立上限，总预算 16000）：
```
[当前话题] 400 → [实时存档事实] 2800 → [已验证战役事件] 900(≤2条)
→ [常识检索] 3200 → [已召回普通记忆] 2000(≤10条) → [私密思绪] 1600
→ [主动联想记忆] 600 → [最近心声去重参考] 700 → [最近公开台词] 2400 + 结尾指令
```

**🔴 尾部保底（最值得抄的设计）**：`UserPromptBudget.Compose` 把「最近历史 + 结尾指令」放 prompt 尾部并**从尾部截断保护**——前面的上下文从段首截断，近历史与 JSON 输出指令永远不会被长记忆挤掉。每段 `AppendSection(title, content, limit)` 独立上限。

**Presentation 合同**（不可覆盖、优先于玩家可编辑提示词）：
```
先由主模型选择 presentation：dialogue / dialogue_action / dialogue_inner / full
dialogue 是日常默认，只返回 presentation+text——不要轮换格式、不要为了丰富而加演出字段
每种 presentation 只能带它规定的键，不得返回 thought/姓名前缀/标签/Markdown
严格返回一个 JSON 对象：{"presentation":"dialogue","text":"正文"}
```

**ResponseParser 严格合同**：返回 `speaker_id`/`lines`/`thought` 字段 → **整条拒绝**；正文带 `名字：` 前缀 → 拒绝；正文含换行 → 按多发言人拒绝；重复字段名 → 拒绝。宁可降级也不放行。

### 2.2 LiveFacts — 实时存档事实（全部「带化」而非面板数值）

`NativeHeroContextProvider.BuildLiveFacts`：日期（第 N 天 HH 时）/地点/地形/天气/补给/队伍规模/俘虏/发言人带伤/关系。**所有量纲分档自然语言**：

```
补给：几乎耗尽/较为紧张/尚可维持/较为充足     队伍：寥寥数人/一支小队/人数不少/规模庞大
关系：仇视/不和/一般/友好/非常亲近           天气：晴朗/下着小雨/大雨不断/正在飘雪/风暴正盛
地形：开阔平原/干燥荒漠/积雪地带/林地/草原/山地/沼泽/近海/外海
```

### 2.3 世界事件记忆（CampaignEventMemoryService — 最扎实的轮子）

- 三类事件：**城塞易主**（按变更原因 Siege/Barter/Gift/Rebellion/ClanDestruction 各生成不同句式）、**英雄被俘/获释**（同一捕获周期合并成一条）、**英雄死亡**
- **相关性分级**：Direct（亲历/相关英雄/相关城塞）/ Faction（家族/王国）/ World（世界新闻）——决定注入范围
- **召回评分**：`importance + 英雄相关+1.0 + 城塞相关+0.9 + 家族+0.65 + 王国+0.45 + 话题文字命中+0.55 + 亲历+0.35 − 天数×0.02`
- **防复读三重闸**：每事件曝光 ≤2 次 + 注入冷却（3 轮 / 1 天）+ 30 天保留、30 条上限
- **叙事聚合**：城塞数日内多次易主自动合并为「数日内两次易主；最近一次：…」

### 2.4 常识库检索（StandaloneKnowledgeRetriever）

- 打分：实体精确匹配 +100 ≫ 关键词/别名子串 +36 ≫ 标题 +24 ≫ BM25（CJK 二元/三元切词，K1=1.2/B=0.75，封顶 18 分）+ 权重×2
- **变体 + 条件**：同一规则可挂多个 Variant，按**说话人上下文**（文化/王国/家族/性别/身份/角色/技能门槛/是否领袖）选措辞——世界观差异化在数据层解决
- **链式检索**：round 0 用当前话题检索；命中规则若「可提取」，round 1+ 用其正文当新 query 继续（≤5 轮）——「说到临冬城 → 自动带出史塔克家族」
- 文本映射（占位文本按说话人实时替换）+ 置顶规则独立字符预算

### 2.5 记忆分层

- Recent(6)/Situational(20)/EventLog(50)/Archive(50) 各层容量 + 半衰期（3/10/45/180 天）
- 私密思绪 Mid(4天)/Long(16天)/Belief(60天)，晋升规则：Mid 3 次/5 天 → Long，Long 3 次/15 天 → Belief
- 记忆总结可关/沿用主模型/独立便宜模型；总结失败不删原始记忆
- 召回有注入预算（每层条数上限），主动旧记忆最多一条且台词提交后才更新召回状态

### 2.6 调度与会话状态机

- 会话模式按人数随机选 → 首轮**按 chattiness 加权随机**选发言人 → 之后轮转游标
- 会话 TurnsRemaining 上限；`AutoInitiate` 决定谁能在无人驾驶时主动开口
- **请求快照纪律**：请求接受时固定地点/会话身份，生成期间地点变化 → `location_context_changed` 取消，不提交迟到台词、不推进发言人、不累计网络失败

### 2.7 TTS 实现（Fish Audio + MCI，Q1 答案）

- **API**：`POST https://api.fish.audio/v1/tts`（可配置 Endpoint，仅 HTTPS；本机回环可 HTTP）
- 认证：Header `Authorization: Bearer <ApiKey>`；`model` 走自定义 Header（默认 `s2.1-pro`）
- 请求体：`{text, reference_id(音色克隆引用), temperature, top_p, prosody:{speed, volume, normalize_loudness}, chunk_length:300, normalize, format:"pcm", sample_rate:44100, latency:"normal", max_new_tokens:1024, repetition_penalty:1.2, min_chunk_length:50, condition_on_previous_chunks, early_stop_threshold}`
- 响应：`application/octet-stream` 裸 PCM（限 8MB）→ 程序加 WAV 头 → 写临时文件 → **Windows MCI**（`mciSendString`：`open ... type waveaudio alias` / `play ... from 0` / `status ... mode` 25ms 轮询）播放
- 节流 1250ms/请求，超时 60s；正文/动作/心声按 presentation 顺序合成一句话播报（TtsTextComposer）
- 10 音色槽按 `Hero.StringId` 精确绑定 `reference_id`，同名不串音；失败不影响文字聊天/记忆/调度

### 2.8 表情/动画结论（Q2 答案）

**没有任何角色表情/动画/姿态引擎 API**。二进制 grep `PlayAction/SetPose/ForcePlayAction/Emote/Animation/AgentVisuals/face/expression` 全部 0 命中。Presentation 合同里的 `action` 字段是**纯文本叙事**（≤160 字，「不得虚构游戏已执行的后果」），只作为旁白展示/可选项进 TTS 播报。人物表现力全靠文本。

## 3. 与我们系统的对照

| 维度 | BannerlordTalk | 我们（LivingWorldNpcs） |
|------|---------------|------------------------|
| 定位 | 纯展示闲聊，台词不执行任何操作 | 动作驱动：闲聊回复可带动作/计划/决策卡片 |
| 发言模式 | 无人驾驶自动闲聊（定时调度） | 玩家驱动 IM 群聊 + 事件广播（ImEventBroadcaster） |
| Prompt 组装 | 分节预算 + 尾部保底 + Presentation 合同 | 顺序拼段（BuildPrompt_ImReply），无每段上限 |
| 实时事实 | LiveFacts 全部带化分档 | WorldFactProvider 动态注入（数值粒度） |
| 世界事件 | 事件记忆 + 分级评分 + 曝光冷却 | WorldEventStore + 广播闸门（每 NPC 180s/同类型 300s/日 10 条） |
| 世界知识 | 手写常识库 + BM25 检索 | Settings 世界观字段（WorldDescription 等 6 个）+ TaikouContent 注入 |
| 记忆 | 四层 + 半衰期 + 思绪晋升 | 热度分档容量（Hot 20/Normal 10/Cold 4）+ LLM 总结 |
| 空间模型 | 无（纯大地图 + 战斗/场景硬门） | ActionSpace 三态（InScene/Remote/Party） |

## 4. 借鉴清单

### 4.1 立即做（低风险高收益）

1. **分节预算 + 尾部保底**：BuildPrompt_ImReply 改 `AppendSection(title, content, limit)` + Compose 尾部保底（近历史+结尾 JSON 指令优先）——防长记忆挤掉输出合同
2. **带化量纲**：WorldFactProvider 输出改档位词（补给/队伍规模/关系/天气地形表直接可抄）
3. **prompt 补两句纪律**（并入 LWN_plan_im_reply_rule）：
   - 「实时存档事实中的当前地点永远是最高依据；旧内容若说正在前往某地而实时事实显示已抵达，只能写成过去的事」
   - 「除非实时存档事实或近期已验证事件明确给出，不得凭空断言当前存在追兵/伏击/迫近威胁」
   - 「禁止为了展示知识而百科式讲解」（已有「禁固定句式」，补此条）
4. **回复防代写检测**：TryParseCasual 加「LLM 输出他人名字开头的台词 → 裁剪/降级」（BT 对 speaker_id/姓名前缀整条拒绝）

### 4.2 立项评估

1. **常识库 + 检索器**（配合 TaikouContent 内容包规划）：`[标签|权重|Any/All|可提取|可匹配]` 格式 + CJK BM25 + 变体条件 + 链式检索是现成设计；内容膨胀时值得抄
2. **NPC 自动闲聊会话**（无人驾驶）：会话状态机（模式选择/chattiness 加权/AutoInitiate/轮次上限）；⚠️ 注意我们铁律 8 只禁当面对话 NPC↔NPC StartConversation，IM 频道 NPC 互聊不在此列；需权衡 token 成本与「免费感」（用户倾向：少自动聊，多事件驱动——见下）
3. **人格卡结构化**：NPCProfile 补「长期目标/价值/禁忌」字段——对 NPC 计划自主性（提议/拒绝理由）有直接帮助
4. **事件召回冷却**：同一事件曝光 ≤2 + 冷却（防 NPC 反复念叨同一件事）
5. **会话地点边界**：进城/战斗结束 → 频道插场景切换提示（我们已有 NearbyFeed 基建）

### 4.3 不做

- **AI 代玩家发言**（破坏玩家在场感/自由感，设计哲学原则 2）
- **纯展示定位**（我们的动作/计划/卡片更深）
- **私密思绪三层晋升**（闲聊演出向，每 NPC 多一套异步请求成本高）
- **提示词玩家可编辑**（我们的 LWN_plan_* XML 是项目铁律，开放编辑会绕过纪律）
- **剪贴板整库导入 UI**（我们世界观走 config.json）
- **Probe 审计哲学**（DebugLogger 记完整 prompt 是开发期内部便利）

## 5. 用户专题结论

### 5.1 TTS 用什么 API（Q1）

Fish Audio（fish.audio）：`POST /v1/tts` + Bearer Key + model Header（默认 s2.1-pro）+ reference_id 音色克隆 + PCM 返回 + MCI 播放。完整请求体/参数见 §2.7。该 API 是 OpenAI 风格但非兼容 Chat Completions，独立实现。

### 5.2 是否涉及角色表情 API（Q2）

**不涉及**。action 是纯文本叙事（TTS 播报用），无任何引擎动画/表情/姿态调用（§2.8）。BT 的表现力上限 = 文本 + 语音，我们已有 Engine 动画动作（EMOTE/move_to/knockout 等）远比它深。

### 5.3 自动生成世界背景的方案（Q3）

用户需求：不手写世界书，进游戏初始化后基于**游戏内文化/王国/关键英雄百科**自动总结世界背景，存存档（单独字段）或 config.json（需处理跨背景 mod 问题）。

**建议：存存档（per-campaign），不存 config.json**：

1. **数据源（铁律 5 直接适用）**：遍历 `MBObjectManager` 已注册对象——CultureObject（名称+描述）、Kingdom（名称+家族+领袖）、关键 Hero（`EncyclopediaText` 百科条目）、Settlement（城镇/城堡）。**动态遍历，禁止硬编码 ID**——天然适配「当前装了什么背景 mod 就是什么世界」
2. **存储**：存档内 `SaveableField` 单独字段（WorldBackgroundBlob + GeneratedFingerprint）。理由：①世界观随战役变化（王国兴灭），per-campaign 快照才对；②config.json 是全局的，跨存档/跨背景 mod 会串（织丰世界背景写进卡拉迪亚存档的场景下 config 共享）；③符合双配置体系判断标准（小白玩家不需要在游戏里改，但它是运行时数据不是手编配置）
3. **跨背景 mod 处理**：指纹（Fingerprint）= 文化/王国数量+名称 hash。新建战役时若 LLM 可用且指纹变化 → 重新生成；TaikouContent 仍注入风格层（SpeechStyle/WarriorTerms 等），自动生成层只供内容层（阵营名/地理/名人），两层不冲突
4. **降级**：LLM 未配置（铁律 1）→ 跳过生成，保留现有本地化默认 WorldDescription
5. **生成时机**：Campaign 启动后（数据已加载）；**异步生成**（主线程禁止同步等 LLM）；生成 prompt 复用世界观纪律（禁面板术语等）
6. **纪律**：自动生成内容 = 静态 lore，优先级低于实时存档事实（BT 同款：「当前地点、人物关系、势力归属、存亡和近期事件等实时存档事实始终高于常识正文」）——生成结果进 prompt 的【世界背景】段，LiveFacts 段照常覆盖

### 5.4 事件驱动闲聊（Q4）

用户倾向：NPC 很少在没事时聊，更多是**玩家说话 / 游戏内有意义事件**（玩家、队员被俘等）触发。BT 可借鉴：

1. **已验证事件纪律**：只把「程序验证过的真实事件」注入（BT 的战后档案来自 LC 验证；我们的事件来自真实游戏回调，一致）——但 BT 把它们存成**可召回事件记忆**，我们有 WorldEventStore 但事件广播是即时一条，缺少「事后可再次提起」的召回
2. **相关性选人**：BT 按 事件↔发言者 的关系分级（Direct/Faction/World）打分选注入对象——我们广播挑人用热度+话题匹配，可补「事件关系」（如被俘事件 → 同家族 NPC 最有话讲）
3. **防复读**：同一事件曝光 ≤2 + 冷却（BT §2.3）——我们广播闸门管频率，没管「同一事件反复提」
4. **BT 自己 prompt 里的反例警示**：其提示词明令禁止「反复报告没有变化的天气、路程、口粮和危险」——这正是无人驾驶闲聊的失败形态，若我们加自动闲聊，此句是护栏；纯事件驱动则天然避免
5. **请求快照**：事件触发回复在生成期间场景/地点变了 → 丢弃迟到结果（我们已有频道版本检查，可加地点检查）

### 5.5 空间模型对照（Q5）

BT **没有动作空间模型**（纯展示，无动作）——它的对应物是一维「门 + 上下文边界」：

- **硬门**：战斗中/任务场景/战后稳定期/海上/暂停/（可配置）聚落内 → 全部阻断生成
- **地点上下文边界**：进城/离开聚落 → 结束旧会话（防「进城后还在聊赶路」）
- **请求快照**：地点身份在请求接受时固定，生成期间变化 → 取消

对照我们的 ActionSpace 三态（InScene/Remote/Party）：我们的模型是为「动作合法性」服务的，BT 不需要因为它的回复不能做事。**结论：空间模型不需要改**；可借鉴的是它的「地点变化 = 会话上下文边界」和「生成期间环境变化 → 丢弃迟到结果」两条（见 4.2.5 / 5.4.5）。

## 6. 文件位置

- 模组包：`Modules/OtherMods/BannerlordTalk-v1.0.0-BL1.4.8/`（README.md / 模组功能说明.txt / 通用主聊天提示词.txt / KnowledgeLibraries/）
- 分析对象 DLL：`BannerlordTalk/bin/Win64_Shipping_Client/BannerlordTalk.dll`
