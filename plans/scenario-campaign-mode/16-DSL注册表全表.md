# 16 — DSL 注册表全表（太阁5 ↔ 骑砍2 对照总表，单一事实源）

> 阶段：Phase 1 定稿 / 随覆盖需求扩展 ｜ 依赖：01（DSL 语法） ｜ 完成后：DSL 能表达太阁5 全集（除明确标注的后续扩展项）
>
> **审核状态：⏳ 未审核（禁止实施）** ｜ 审核人：用户 ｜ 审核日期：— ｜ 审核意见：—
> 通过后改为 ✅ 已审核（日期）+ 记录意见。

## 文件分工（2026-08-26 重构后，单一所有权）

- **词条翻译权威 = `16a-DSL翻译总表.csv`**（🔴 编号 16a = plan 16 的数据文件，与 plan 本体 `16-DSL注册表全表.md` 区分——文件夹惯例：字母后缀 = 主 plan 的子文件，先例 = 原 07a；840 行：域 42 / 属性 245 / 域值 372 / 命令 155 / 语法 19 / 函数 7——🔴 全部为 TK5 语料词（太阁原词列零 `—`、零 `::`/`.`、零英文 token，2026-08-27 用户裁定：mod 原生 18 动作 + 10 mod DSL 函数 token 移出 16a，权威 = 本文件 §六/§三）；**「语法」类别** = 条件子句/组合器/分支/循环/事件头字段 19 词（調查/ＡＮＤ調查/場合分歧/循環/發生條件…），命令区 155 = 真·命令（动作/演出/世界结算/容器）；列 = 类别·所属域·太阁原词·例句·我们侧名·值类型·语义·参数·备注·频率，🔴 2026-08-27 用户裁定列序：类别第一列（排序分区）、所属域第二列（排序第二级：属性按域分组）、太阁原词第三列、例句第四列（TK5 原句示范）、频率最后；行序 = 类别→所属域→太阁原词 三级排序）——**查询词条翻译只看这一处**，域/属性/域值/函数/动作 token 全表都在 CSV。CSV 由 `tools/build_registry_csv.py` 程序化生成（复跑 `TK5AllEvents_merged.txt` + 映射字典）；改词条 = 改 `gen_registry_tables.py`/`build_registry_csv.py` 字典 + 重跑。🔴 **v2 结构性修复（2026-08-27）**：属性 = 单键属性名 + 多段侧名（`Hero.clan / Settlement.clan`，域错配由生成期自检按语料实际域拦截——旧版 大名家.本城 2298 次被登记成 人物域 Hero.home 导致下游全量待注册）；**域值区** = 身份枚举/狀況值/命名槽等词条（实体引用如 人物::伊藤總十郎 **不入表**，走翻译器名字表 + 确定性兜底）；**表外词条 = 生成器缺陷**，生成期全语料覆盖自检不绿即 exit(1)（除 StringId 实体外，语料每个提取词都有行）
- **本文件 = 机制权威**（CSV 装不下的）：§一 Ctx 三档作用域 ｜ §二 trigger/facility 注册表 ｜ §三 函数注册表（🔴 2026-08-26 起**单所有权**，01 不再重复维护——trigger/facility 2026-08-26 先例）
- **01 = 语法规则/类型纪律/安全兜底**（怎么解析；trigger 调度模型/validator（校验器，打包前检查剧本语法的工具）检查项在 01，注册表数据在 16）；**08 = 转化流程**（怎么翻译）
- **步骤类型注册在 01 步骤类型表**（perform / inquiry / im_message / cutscene / wait / bgm / se / scene_enter / choice / 🔴 if / effect）；**05/03 = 台本指令/战斗预设格式注册**（actor_enter/camera/actor_action 等引擎内部 token，无 TK5 源词，不入 CSV）
- **表外用法 = 回填生成字典（见下方「CSV 编辑纪律」）+ 扩展 01 注册表**

## 🔴 CSV 编辑纪律（16a 是纯生成物，禁止直接编辑）

> `16a-DSL翻译总表.csv` 由 `tools/build_registry_csv.py` 每次**重写整个文件**——**任何列的任何直接编辑都会在下次重跑时被覆盖，禁止手改 CSV 本体**（🔴 CLAUDE.md 铁律 22）。改词条的唯一入口 = 改生成字典 + 重跑（重跑后 `git diff` 应只含预期变化）。**表外词条出现 = 生成器缺陷**：回填 `gen_registry_tables.py` 映射/规则 → 重跑 → 全语料覆盖自检（不绿 = exit(1)，禁止带病产出）。

| 列 | 来源 | LLM/人工能填吗 | 写入位置 |
|---|---|---|---|
| 类别（第一列） | 代码硬编码（域/属性/域值/命令/函数） | ❌ | — |
| 太阁原词 | TK5 语料正则提取（域/属性/域值/命令）；`—`（函数/mod 原生行） | ❌ **禁止**——语料事实，脚本复跑得出，手填必与语料不符 | — |
| 所属域（第三列） | 语料 (域,属性) 对聚合（多域 ` / ` 分隔）；域值行 = 域；其余 = `—` | ❌ 自动 | — |
| 我们侧名 | 字典映射派生（`DOMAIN_MAP`/`PAIR_OVERRIDE`/`ATTR_MAP`/`CMD_EXACT`/`DOMAIN_VAL_MAP` + 规则）；多域同名属性用 ` / ` 分段；🔴 DSL token 只收 ASCII（侧名合法性自检） | ✅ **映射决策可设计**，但写入位置 = 字典，不是 CSV | [gen_registry_tables.py](tools/gen_registry_tables.py) 映射字典 |
| 值类型 | 🔴 2026-08-27 用户裁定：**语料驱动推断**（优先级：算术运算 `*(`/`+(` = 数字强证据 > switch 分支 `場合別` = 数字 > 比较/赋值右值类型（域内容类型/真偽/裸值状态枚举）> 人工表 `ATTR_TYPES` 兜底）；標誌 属性有具名状态值（未出現/出撃中…）→ 枚举（0/1 只是编码）；真/偽 裸写 = 布尔；枚举值集合自动收集进语义列（原屬下標誌 → 原上司/原同事/原屬下…） | ✅ 人工表只兜无证据低频项 | build_registry_csv.py `ATTR_TYPES` + `infer_attr_types()` |
| 语义 | 语义字典（`DOMAIN_SEM`/`ATTR_SEM`/`CMD_SEM`/`ACT_SEM`），兜底 = 词条名自解释 | ✅ 低风险人读信息（中文释义），补字典即可；兜底自解释也可接受 | 两个脚本的 `*_SEM` 字典 |
| 参数 | `ACTIONS` 字典（动作行）；`—`（域/属性行） | ✅ 新动作参数 = 补 `ACTIONS` | build_registry_csv.py `ACTIONS` |
| 备注 | 🔴 2026-08-27 用户裁定：原「实现用法+状态」合并列——人读规划信息（翻译程序不消费）：`✅ 引擎查询器` / `🔴 需新增（13 主命 / 02 PartyBrain / 17 官职 / 数据包 / mod 外置属性）` / `❌ 放弃`；规则派生（侧名尾段 ∈ `ATTR_TYPES` → ✅，∉ → 🔴 需新增），命令行 = 实现归属 label | ❌ 禁止手填——想改 = 改侧名/label | 同上 |
| 频率（最后一列前） | 语料统计；`—`（函数/mod 原生行） | ❌ **禁止**——同上 | — |
| 例句（最后一列） | 🔴 2026-08-27 用户裁定新增：词条 → TK5 事件原句示范（语料首次出现行自动摘取，截 60 字）——给人检查「词条 ↔ 原句」对应是否成立；低频碎片（evm 等）无字面 = 留空 | ❌ 自动摘取 | 同上 |

**mod 原生动作**（无 TK5 源词，2026-08-26 起）：`太阁原词`/`频率` 列固定 `—`；token 加入 `MOD_NATIVE` 列表（build_registry_csv.py），参数/实现/语义入 `ACTIONS`/`ACT_SEM` 字典。

**流程**：翻译/写作遇到表外词 → ①查语料确认原词形态（`grep TK5AllEvents_merged.txt`）→ ②补对应字典（映射/参数/语义）→ ③重跑 `python plans/scenario-campaign-mode/tools/build_registry_csv.py` → ④`git diff` 核对只增预期行 → ⑤以 CSV 为引用源写作。**LLM 参与边界**：可以设计映射、补语义/参数（字典层）；禁止编造原词/频率、禁止直接改 CSV 任何单元格。

---

# 一、代入槽机制（Ctx 上下文变量，太阁"人物Ａ/主人公/發生據點"的映射）

- **背景**：太阁代入命令出现 ~15000 次（代入人物Ａ/代入城Ｂ…），转化必用——Phase 1 必须实现
- **设计**：剧本上下文变量 `Ctx::A` / `Ctx::B` / `Ctx::C` / `Ctx::D` / `Ctx::E` + 语义槽（🔴 2026-08-25 修正：槽名全部英文 token——原直译中文槽"主人公/發生據點"违反铁律 20 英文 token 纪律）：`Ctx::event_settlement`（事件发生地）/ `Ctx::event_hero`（事件人物）
- 🔴 **主人公不用 Ctx 槽（2026-08-25）**：玩家主角 = 引擎原生 `Hero::MainHero`（骑砍2 `Hero.MainHero`）——`(Hero::MainHero.clan) == (Clan::clan_oda_1)`；原 `Ctx::主人公` / `Ctx::主人公據點` 废弃（"主人公據點" = `(Hero::MainHero.settlement)`）
- 🔴 **命名槽（2026-08-25 扩展——分支选择用）**：`Ctx::tactic` / `Ctx::kiyasu` 等按语义命名。choice 选项 effect 用 `ctx_set` 写入（`{ "action": "ctx_set", "slot": "tactic", "value": "raid" }`），同事件 script 的步骤 `when` 门控读取（`(Ctx::tactic) == "raid"`）；**生命周期 = 事件上下文（触发时初始化、结束清理）**——玩家"点了什么选项"是事件内局部状态，不需要全局 flag；只有跨事件要读的选择结果才升级为 Flag/Variable（01 纪律：分支选择默认 Ctx，升级才用 Flag；🔴 2026-08-26 违规实录：09b `Ctx::imagawa_plan` 0i 写 1i 读 = 跨事件读 Ctx，已改 `Flag::okehazama_imagawa_plan`）
- **赋值动作**：`ctx_set`（参数：槽位 + 引用）——`{ "action": "ctx_set", "slot": "A", "value": "Hero::lord_1_oda" }`
- **条件引用**：`(Ctx::A.clan) == (Clan::clan_oda_1)`（Ctx 引用可带域属性）
- **生命周期**：事件触发时初始化（主人公/發生據點 自动赋值），代入动作修改；事件结束清理
- **太阁映射**：代入人物Ａ → `ctx_set A`；主人公 → `Hero::MainHero`（🔴 2026-08-25，原 Ctx::主人公 废弃）；發生據點 → `Ctx::event_settlement`；發生人物 → `Ctx::event_hero`

### 🔴 代入槽三档作用域（2026-08-26——Ctx vs Variable vs GlobalSlot，存档边界）

> TK5 代入命令 ~15000 次，语义分两档：**同事件内**临时计算 vs **跨事件**事件链传递（事件 A 设"人物Ａ"、事件 B 读——TK5 槽是全局存档变量）。翻译时必须按作用域归档，禁止一律 Ctx（Ctx 不存档，跨事件用 Ctx = 读档后丢失）。

| 档 | 载体 | 存什么 | 存档 | 纪律 |
|---|---|---|---|---|
| **事件内局部** | `Ctx::<命名槽>`（ctx_set 写） | 任意（引用/数字/字符串） | ❌ 不存档（事件上下文，触发初始化/结束清理） | 同事件内代入/读取用这个；**禁止跨事件读 Ctx**（01/16 纪律，09b 教训） |
| **持久数值/字符串** | `Variable::<名>`（set_variable 写） | 数字/字符串 | ✅ 存档（SyncData） | 跨事件计数/标志（日數計數器/出奔計數器/02 到达感知 `imagawa_army_arrived`） |
| **🔴 持久对象引用** | `GlobalSlot::<名>`（global_set 写，2026-08-26 新增） | **角色/城/家族/势力引用**（Variable 只支持数字/字符串，引用存不了） | ✅ 存档（SyncData） | 跨事件事件链传递的对象（TK5 人物Ａ/城Ｂ 跨事件用法）；新系统状态（14 当前战略目标、17 当前官职等）；**新 SyncData key 同步补 `ResetAllCampaignState()`**（存档纪律） |

**转化判定（08 纪律）**：代入命令翻译时查作用域——①同一事件内设读 → `Ctx` ②跨事件/跨剧本读且为数值/字符串 → `Variable` ③跨事件读且为对象引用 → `GlobalSlot`；**新系统要持久的状态一律走 Variable/GlobalSlot，不走 Ctx**（Ctx 不存档）。

**动作**：`ctx_set`（Ctx，已有）／ `set_variable`（Variable，已有）／ `global_set`（GlobalSlot，🔴 新增动作，参数 slot + 引用，存档）——全部在 CSV 命令行动作行（mod 原生段）。

## 二、触发时机注册表（trigger / once / priority / facility——token 注册权威；调度语义见 01）

> 太阁5 事件头字段 = `屬性`/`發生契機`/`發生條件`/`執行` 四样（实测 2594 事件头字段只有这四样），**四段全部落 JSON 字段**——触发时机禁止只写注释（数据驱动，09b 教训）。完整调度模型/互斥选路/validator 检查见 01「事件触发时机」节；🔴 触发时运行环境 → 演出形态约束见 01（选错 = 实现期才发现演不了）。

**once / priority 转化**（`屬性` 实测全量分布：一次 2146 + 一次｜弱 256；多次 163 + 多次｜弱 29）：

| 屬性 | once | priority |
|---|---|---|
| 一次 | true（默认） | normal（默认） |
| 多次 | false | normal |
| 一次｜弱 | true | weak |
| 多次｜弱 | false | weak |

- 🔴 弱 = 互斥选路低优先级（实测同契機组内弱与非弱混排；TK5 无数字优先级字段）
- 调度语义（详 01）：trigger 触发 → 只遍历挂名事件 → 按 priority 分层（weak < normal）→ 层内按文件声明顺序逐事件检查 condition → 第一个满足的触发，其余本轮跳过（一次时机只演一个事件）

**trigger 注册表 v1**（TK5 發生契機 词表转化，英文 token，题材无关）：

| trigger | TK5 發生契機 | 引擎监听点（🔴 2026-08-26 逐项反编译验证） | 说明 |
|---|---|---|---|
| daily | 每日處理的開頭 | ✅ `CampaignEvents.DailyTick` | 每日检查只是其中一个 trigger |
| monthly | 每月處理的最後 | 🔴 无原生每月事件（仅 Daily/Hourly/Weekly）→ 自建钩子（DailyTick 计数 30 天 / Time::month 变化检测） | |
| game_start | 遊戲開始時 | ✅ `CampaignEvents.OnNewGameCreatedEvent` | |
| settlement_enter | 據點畫面表示後（主人公據點/無效/具體城名） | ✅ `CampaignEvents.OnSettlementEntered`（打开据点菜单即触发，无需进室内 mission） | 实测同契機组最大（主人公據點 632 事件）→ 互斥选路重灾区 |
| house_enter | 室內畫面表示後（主人公據點,自宅/酒場…） | ✅ `OnMissionStarted` + facility 判定（场景名查「场景 → facility」映射表） | 🔴 设施参数 = 事件字段 facility（见下） |
| council_start | 評定開始時 | 🔴 17 系统自定义事件（评定会开始） | 134 事件 |
| travel_screen | 移動畫面表示後 | 🔴 **无原生对应**（实测 0 命中）→ 暂不注册，降级 daily + 移动中条件 | |
| field_battle_start | 野戰開始時 | 🔴 **02 锁军拦截遭遇（两军遭遇、进战斗前，Campaign 层）**（2026-08-26 修正：**不走 OnMissionStarted**——01 分流模型明确 field_battle_start 不在此列） | 演出在进战斗前发生（09b 野战开场主案：大地图立绘对白 → 解除拦截 → 进战役）；战斗内演出走 field_battle_end / 03 战斗内钩子 |
| field_battle_end | 野戰結束時 | ✅ `CampaignEvents.OnPlayerBattleEnd` + 03 剧情战战果钩子 | |
| siege_battle_start | 攻城戰開始時 | ✅ `OnMissionStarted` + `MissionMode.Siege` 判定 | |
| siege_battle_end | 攻城戰結束時 | 🔴 03 战果结算钩子（同 field_battle_end 路径） | |
| army_move_end | 軍團移動結束時 | 🔴 **02 PartyBrain 到位检测**（2026-08-26：通用 party 行为脑，AgentBrain 的 Campaign 层对称物，见 02——**不做全量轮询**，全局 tick 只遍历受控集合；到位判断 = 原生 `MobilePartyAi` 行为完成/Ai 目标距离） | |
| chapter_freeze | 章節凍結時 | 🔴 14 系统自定义事件 | |
| game_clear | 遊戲通關時 | 🔴 mod 剧本结算自定义事件 | |

**facility 注册表 v1**（house_enter 设施参数，TK5「室內畫面表示後」第二参数全量统计）：

> 事件 JSON：`"trigger": "house_enter", "facility": "tavern"`（facility 仅 house_enter 合法，validator 检查项 16）。判定 = OnMissionStarted → `Mission.Current.SceneName` 查「场景 → facility」映射表（07 素材表产出，两轮策略：预设场景名清单 → predicate 兜底，铁律 5）。

| facility | TK5 设施 | 次数 | 场景落点 | 状态 |
|---|---|---|---|---|
| house | 自宅 | 229（🔴 口径注 2026-08-26：229 = 自宅设施出现**总次数**；01/README 的 211 = 「室內畫面表示後(主人公據點,自宅)」**组合契機数**——两个口径不同，非矛盾） | 🔴 原版无玩家住宅（实测 scn_player_house 0 命中）→ 织丰御殿/城主间顶替（09b opening 已用 sho_meeting_castle_a） | 🔴 07 素材表确认织丰住宅场景 |
| tavern | 酒場 | 45 | 原版 scn_*_tavern_a/b | ✅ |
| castle_hall | 城主間 | 15 | 原版 scn_*_lords_hall / 织丰御殿 | ✅ |
| council_room | 評定間 | — | 织丰御殿（09b 评定会已用） | ✅ |
| za | 座 | 15 | 织丰？无 → 就近映射/降级 | ⏳ 07 |
| clinic | 主人公診療所 | 14 | 织丰诊疗所？无 → 降级 | ⏳ 07 |
| dojo | 主人公道場 | 13 | 织丰道场？无 → 降级 | ⏳ 07 |
| house_min | 民家 | 12 | 原版村落民家 / 织丰 | ⏳ 07 |
| shop | 商家 | 11 | 织丰？无 → 降级 | ⏳ 07 |
| nanban_trade | 南蠻商館 | 10 | 🔴 织丰大概率无 → 降级 menu_dialogue | ⏳ 07 |
| smithy | 主人公鍛冶屋 | 10 | 织丰？无 → 降级 | ⏳ 07 |
| tea_room | 主人公茶室 | 9 | 织丰茶室？无 → 降级 | ⏳ 07 |
| temple | 寺 | 5 | 织丰寺社？无 → 降级 | ⏳ 07 |
| 其余（醫師宅/職人宅/公家宅/海賊宅/武家宅/茶人宅/米屋/海外交易所/忍者宅/宿屋…） | — | ≤4 每种 | 🔴 边缘设施——无场景一律降级 menu_dialogue/inquiry（不注册 house_enter 场景判定） | 🔴 降级 |

纪律：①边缘设施不假装有场景——翻译时降级 menu_dialogue；②house 自宅 = 织丰御殿/城主间顶替是既定方案（09b opening 先例），07 素材表确认后登记正式映射；③映射表两轮策略（预设场景名 → predicate 兜底，铁律 5）；④翻译对照：`室內畫面表示後(無效,酒場)` → `"trigger": "house_enter", "facility": "tavern"`。

## 三、函数注册表（单所有权，2026-08-26 起）

> 关系判断（带参函数）——与属性区分：属性是单值、函数是关系。🔴 **分工（2026-08-27 用户裁定）**：本表 = **mod DSL 函数 token 定义（13 个）**——mod token 无 TK5 源词，CSV 太阁原词列只收 TK5 词，故 token 注册地在本表；16a CSV 函数区 = **TK5 调用词翻译行**（外交同盟→isAllied 等 7 行：太阁原词列 = TK5 调用词，侧名列 = 本表 token）；hasCard/canMove/canAttack 3 个只有 TK5 调用词无手写剧本先例，token 定义在此补登（2026-08-27）。函数 = **带返回值的函数**——返回值类型在 CSV 值类型列（外交同盟/外交感情 = **数字**，例句 `外交感情(...)+(10)` 做算术、`外交同盟(...)!=(2)` 与数字比较；其余 = 布尔），所属域列 = 语料调用方域。语法语义（条件求值、四形态判断）见 01。**新函数 = 本表加行 + CSV 调用词翻译行（若 TK5 有源词）+ 实现（01 条件求值）**。

| 函数 | 参数 | 返回值 | 语义 | 状态 |
|---|---|---|---|---|
| exists | 引用 | 布尔 | 对象存在 | ✅ 已设计 |
| atWar | a, b（势力引用） | 布尔 | a 与 b 交战 | ✅ 已设计 |
| isAllied | a, b（势力引用） | 数字 | a 与 b 同盟（数值：!=0 即同盟） | 注册表加行 |
| isNeighbor | a, b（据点引用） | 布尔 | a 与 b 相邻 | 注册表加行 |
| hasRelation | hero, hero, op, 数字 | 布尔 | 亲密度比较 | 注册表加行 |
| relation | a, b, op, 数字 | 数字 | 势力间外交关系数值 | 注册表加行 |
| hasMet | a, b（角色引用） | 布尔 | 是否认识 | 注册表加行 |
| sameSettlement | hero, hero | 布尔 | 同据点 | 注册表加行 |
| canPromote | hero | 布尔 | 功勋 ≥ 晋升链下一级阈值 | 注册表加行（17） |
| allControlled | region, clan | 布尔 | 区域全部据点由 clan 控制 | 注册表加行（全城壓制 481 次带参调用） |
| hasCard | hero, card | 布尔 | 是否持有技能卡 | 注册表加行（卡持有 500 次带参调用） |
| canMove | settlement, hero | 布尔 | 角色能否前往该据点 | 注册表加行（移動可能） |
| canAttack | settlement, hero | 布尔 | 角色能否攻击该据点 | 注册表加行（攻擊可能） |

## 四、枚举注册表（值 token 权威，2026-08-26）

> 枚举属性（类型 ∈ 01 值类型表：identity/gender/state/title…）的值 = **英文 token 字符串字面量**：`(Hero::X.identity) == "daimyo"`。🔴 **禁止中文/自由字符串**（铁律 20 + 类型纪律）——validator 检查：①比较对象类型 = 枚举属性 ②字符串值 ∈ 对应枚举注册表（静态可查）。值表 v1 见下；🔴 全量 token 权威 = 16a CSV 域值区（身份枚举 29 值等，2026-08-27 从语料提取），本表为人读摘要；表外值 = 回填 gen_registry_tables.DOMAIN_VAL_MAP。

### 🔴 值类型体系（2026-08-27 用户裁定：标量 / 枚举 / 对象 三类全量定义，16a 值类型列写法权威）

**一、标量（非对象）**：

| 类型 | 值类型列写法 | 说明 | 例 |
|---|---|---|---|
| 布尔 | `布尔` | true / false；🔴 **标誌类二态属性统一布尔（2026-08-27 用户裁定）**——TK5 的 0/1 与语义词（成立/不成立、已出現/未出現、已發生/未發生…）是同一状态的两套拼写：**true = 1/成立/已發生（已发生/已置位），false = 0/不成立/未發生（未发生/未置位）**；翻译器统一规范化 | `(Hero::X.alive)` / `(Flag::okehazama_started) == true` |
| 数字 | `数字` | 整数/数值（比较 `>`/`<`/`>=`/`<=` 仅数字） | `(Time::year)` |
| 字符串 | `字符串` | 自由文本（显示名、文字列槽） | `代入文字列` |
| 空 | `空` | null（無效） | `人物::無效` |

**二、枚举（受限值集合；值 = 英文 token 字面量，🔴 禁止中文/自由字符串（铁律 20）；值集合权威 = 16a CSV 域值区 + §四 枚举注册表；🔴 值类型列写法 = `枚举:XX` 具体类型——域值枚举 = 所属域（`枚举:天氣`）、属性枚举 = 属性名（`枚举:出撃標誌`）、带序枚举 = `枚举:身份（带序：17 身份链）`，禁止裸「枚举」，2026-08-27 用户裁定）**：

| 枚举 | 带序 | 值集合（token） | 权威/实现 |
|---|---|---|---|
| 身份 | ✅（17 身份链） | 30 值：daimyo 大名 / city_lord 城主 / province_lord 國主 / elder 家老 / general 部將 / ronin 浪人 / samurai_captain 侍大將 / ashigaru_captain 足輕大將 / ashigaru_leader 足輕組頭 / foreman 番頭 / ninja_high 上忍 / ninja_mid 中忍 / ninja_low 下忍 / chief 頭·頭領 / overseer 元締 / manager 支配人 / clerk 手代 / merchant_owner 大老闆 / girl 姑娘 / apprentice 見習 / sword_master 師範 / sword_deputy 師範代 / doctor 醫師 / smith 鍛冶匠 / tea_master 茶人 / sailor 水夫 / boat_leader 水夫頭 / boatswain 船頭 / naval_captain 船大將 等 | 17 身份系统 |
| 官位 | ✅（品级序） | 正一位 > 正二位 > 從二位 > 正三位…（品级链） | 17 官职表 |
| 官職 | ❌（称号） | 筑前守 / 征夷大將軍 / 日向守 / 修理亮 / 大納言 / 左大臣…（守护职/官位名） | 17 官职表 |
| 性別 | ❌ | male / female | 引擎 |
| 天氣 | ❌ | weather_clear 晴 / weather_rain 雨 / weather_cloudy 雲 | 03 预设 weather |
| 人物類別 | ❌ | general 武將 / ronin 浪人 / ninja 忍者 / pirate 海賊 / generic_rival 泛用對手 / townsman 町人 / event_person 事件人物 | 容器筛选 |
| 戰鬥結束種類 | ❌ | ended 終結 | 03 BattleResult |
| 物品類型 | ❌ | 茶器 / 武器… | 物品数据包 |
| ~~狀態標誌族~~ → 布尔（2026-08-27 用户裁定） | ❌ | 二态标誌（出現/出撃/死亡/所持/戰鬥/生病/離家/鑑定/死刑標誌 等）值类型 = **布尔**（见「一、标量」布尔行），不再属枚举；唯一保留 = 原屬下標誌（3 态，枚举:原屬下標誌） | 引擎属性查询器 |
| 戰略/大方針 | ❌ | 國內統一 / 分國統一…（大名家战略方针） | 14 drift |
| 軍団方針 | ❌ | 歸還… | 02 PartyIntent |
| 仕官傾向 | ❌ | 沒那個意思… | 引擎属性查询器 |
| 勢力類型 / 據點種類 / 主命狀態 / 使用狀況 / 結果 | ❌ | 各域枚举值（BattleResult 等） | 引擎/13/03 |

> 🔴 **带序枚举的序 = 显式等级表，禁止按枚举值排列推断（2026-08-27 用户裁定）**——身份/官位参与 `>=`/`<=` 时，等级数查 **17 产出的「身份等级映射表」（= 17 RankLadder 数据包，16 与 17 同一张表）**，与 CSV 无关。**不是** CSV 行序、**不是** token 字母序、**不是**频次序。
>
> **表长什么样**（格式示意，17 产出）：`daimyo=10, city_lord=9, province_lord=8, elder=7, general=6, samurai_captain=5, ninja_high=4, ninja_mid=3, ninja_low=2, …`
>
> **判定怎么走**（validator 三步）：
> 1. `身份 >= 城主` → 右值查表：城主=9
> 2. 左值也在表内（部將=6）→ 6 >= 9 正常求值
> 3. 左值表外（浪人/姑娘 等无等级身份）→ **阻断报错**「等级外值参与序比较」
> 4. 表外值互相之间只准 `==`/`!=`
>
> **为什么不能按枚举排列**：CSV 行序是生成器排的（姑娘可能排在大名前）、字母序与官职等级无关、新增一个值会推移后面所有序号——三样都不能当等级。

**三、对象（引用，StringId 指向引擎对象；🔴 铁律 20：引用一律 StringId 禁止显示名）**：

| 类型 | 值类型列写法 | DSL 引用 | TK5 域 | 说明 |
|---|---|---|---|---|
| 人物 | `对象:人物` | `Hero::` / `Agent::` | 人物 | 有 HeroObject 角色 + 模板 NPC（铁律 8 平权） |
| 据点 | `对象:据点` | `Settlement::` | 城/據點/町/里/砦 | 城主/归属/士气/战斗标志 |
| 家族 | `对象:家族` | `Clan::` | 大名家 | 本城/当主/支配力 |
| 王国 | `对象:王国` | `Faction::Kingdom.` | 勢力 | 外交/战争 |
| 区域 | `对象:区域` | `Region::` | 國/地方 | 全城压制等 |
| 部队 | `对象:部队` | `Army::` | 軍團 | 02 PartyBrain 受控集合（军团 1/2、主人公军团） |
| 组织 | `对象:组织` | `Org::` | 忍者衆/商家/海賊衆 | 07 数据包（伊贺众/茶屋…） |
| 卡 | `对象:卡` | `Card::` | 卡/流派 | 技能卡数据包（🔴 2026-08-27 用户裁定：流派不放弃——后续可能在 mod 做流派系统，数据包补充） |
| 物品 | `对象:物品` | `Item::` | 物品 | 价格/所有者/持有数 |
| 交易品 | `对象:物品` | `Item::` | 交易品 | 具体商品实体（交易品::蜜柑/生糸/茜…，交易品数据包） |
| 设施 | `对象:设施` | `Facility::` | 場面 | 05 演出设施（自宅/評定間/城主間） |
| 任务 | `对象:任务` | `QuestDef::` | 主命/工作/事件主命 | 13 QuestDef 框架（🔴 官職/官位不是对象——属性 title/court_rank 的值 = 枚举 token，见二、枚举） |
| 旗标 | `对象:旗标` | `Flag::` | 事件標誌 | 编号旗标（Flag::flag_38） |
| 事件 | `对象:事件` | `Event::` | 事件 | 事件状态查询（`(Event::EFF0C300_159.done)`，调度器记录） |
| 位置 | `对象:位置` | `position` | — | 坐标/移动 |

**类型规则**：
1. 值类型列写法 = 上表「值类型列写法」（属性/域值/代入槽/函数返回值 全表一致）
2. 🔴 **域 = 容器，容器本身无类型（2026-08-27 用户裁定）**——域行值类型列 = `域`（标记）；**类型属于域值**（`事件標誌::38` = 布尔 true/false、`事件::X` = 布尔（done：已發生=true）、`天氣::雲` = 枚举:天氣、`城::鳴海` = 对象:据点）
3. 赋值/比较两侧同型（见下「值类型一致性纪律」），validator 阻断违规
4. 枚举值 = 英文 token 字面量（§四），禁止中文/自由字符串（铁律 20）；带序枚举 `>=`/`<=` 顺序由 17 系统定义（显式等级链，禁止按枚举值排列推断）
5. 对象引用 = StringId（铁律 20），实体查名字表（人物::X → HERO_MAP），不在 16a CSV（2026-08-27 用户裁定）
6. 🔴 **值类型来源与推断（2026-08-27 用户裁定，生成器 build_registry_csv.py 实施）**——语料证据优先，人工表/兜底次之：
   - **算式/不等式（两侧都是属性）→ 双方计数字**（`格>格` → 数字；单侧不等式如 `身份>=(身份::城主)` 仍按右值判定——带序枚举不受污染）
   - **标誌类二态判定**：语义词 ≤2 个（已發生/未發生 为跨域借用不计）且无 ≥2 数字 → **布尔**；语义词 ≥3 → 枚举（原屬下標誌）；出现 ≥2 数字 → 数字（天覧試合標誌==(3)）
   - **槽引用推断「数字/对象」= 模糊** → FUZZY_TYPE_OVERRIDE 接管（五维属性 武力/統率力 → 数字）
   - **全数字属性名 = 域::具名值.编号 的数值编码**（`人物類別::泛用對手.60` → Identity.attr_60）→ 兜底数字
   - **域值类型用法推断**：代入槽:(域::值) / 更新:(域::值)(X) / 調查 比较 → 对方类型反推（`代入人物Ｂ:(儲存號::X)` → X=对象:人物；`調查:(儲存號::X)<=(45)` → X=数字）；域默认动态（数字/字符串/对象）时接管，域默认明确不接管，人工 OVERRIDE 最高优先

### 🔴 值类型一致性纪律（2026-08-27 用户裁定，validator 检查项）

> 值类型体系（16a 值类型列）：布尔 / 数字 / 字符串 / 枚举 / 空 / 对象:子类型（据点/人物/家族/王国/区域/部队/组织/卡/物品/设施/官职/任务/旗标/位置）。**赋值与比较两侧值类型必须一致**：

| 语法 | 规则 | 例 |
|---|---|---|
| `更新:(目标属性)(新值)` | 目标属性的值类型 == 值的类型 | `更新:(人物::出雲阿國.所屬據點)(町::松江)`——所屬據點=对象:据点 == 町::松江=对象:据点 ✓ |
| `調查:(A) 运算符 (B)` | A 值类型 == B 值类型（`>`/`<`/`>=`/`<=` 仅数字） | `調查:(狀況::年)>=(1598)`——数字 == 数字 ✓ |
| `代入槽:(源值)` | 槽类型 == 源值类型（槽类型见 16a 代入命令值类型列） | `代入城Ａ:(城::鳴海)`——城Ａ=对象:据点 == 鸣海=对象:据点 ✓ |
| 函数调用 `属性(参数)` | 参数按函数注册表参数类型校验 | `全城壓制(國::甲斐, 大名家::X)`——region, clan ✓ |

validator 对违规（如 `更新:(城::X.城主)(町::松江)` 对象:王国 vs 对象:据点）**阻断报错**。

| 枚举属性 | 值（token） | TK5 源词（语料频次） | 状态 |
|---|---|---|---|
| identity（身份） | `daimyo` 大名(426) / `city_lord` 城主(388) / `ronin` 浪人(246) / `province_lord` 國主(230) / `elder` 家老(132) / `general` 部將(86) / `sword_master` 師範(77) / `chief` 頭(62)+頭領(58) / `overseer` 元締(50) / `samurai_captain` 侍大將(49) / `ninja_high` 上忍(35) / `foreman` 番頭(29) / `clerk` 手代(29) / `manager` 支配人(28) / `ninja_mid` 中忍(28) | 全量 29 值已提取（16a 域值区） | ✅ 语料提取 |
| gender（性别） | `male` / `female` | 男/女（语料写法待核对） | ⏳ 08 |
| state（出现标志） | 布尔（true=已出現，2026-08-27 标誌族统一布尔） | 出現標誌 | ✅ 引擎属性查询器 |
| title（官职） | 17 官职表产出 | 官職 | 17 |

## 五、覆盖结论

- **Phase 1 可全覆盖**（除明确标注后续扩展）：6 操作符 + 9 引擎域（含 Ctx 代入槽 + 🔴 Event 事件域）+ 🔴 Card 能力卡域（数据包扩展）+ 全属性白名单 + 10 函数（含 canPromote，17）+ 动作全表（CSV 命令区纯 TK5 174 行；mod 原生 18 动作 token 见 §六）

## 六、动作 token 注册表（mod 原生，无 TK5 源词，2026-08-27 自 16a 移出）

> 09b/01/09c 手写剧本在用的 mod DSL 动作 token——**不是 TK5 翻译词条**（16a CSV 太阁原词列只收 TK5 词，用户裁定），权威 = 本表；validator 校验剧本动作 token 查本表。参数/实现/语义权威 = `build_registry_csv.py` ACTIONS/ACT_SEM 字典。

| token | 参数 | 语义 | 实现 |
|---|---|---|---|
| set_flag | flag | 剧本标志 | 本 Phase |
| clear_flag | flag | 剧本标志 | 本 Phase |
| set_variable | variable, value | 剧本变量 | 本 Phase |
| global_set | slot, 引用 | 全局槽赋值 | 🔴 新加（存档） |
| declare_war | a, b | 宣战 | 本 Phase |
| make_peace | a, b | 停战 | 本 Phase |
| spawn_clan | clanId, leader, home | 新建家族 | 06（需核实 CreateClan） |
| make_alliance | a, b | 结盟 | 02（需核实 StanceType.Allied） |
| relation_change | a, b, value | 关系变更 | 本 Phase（ChangeRelationAction） |
| change_clan | actor, clan | 阵营变更 | 06 |
| release_party | leader | 释放部队 | 02 PartyBrain |
| grant_troops | troopIds, counts | 给兵 | 06 |
| card_gain | hero, card | 获得能力卡 | 数据包扩展（存档持久） |
| card_lose | hero, card | 失去能力卡 | 数据包扩展（存档持久） |
| grant_merit | actor, value | 功勋增减 | 17（WorldActionExecutor Scenario 层） |
| set_title | actor, titleId | 设官职 | 17 |
| promote | actor | 晋升 | 17 |
| duel | opponent, outcomeSlot | 个人战（1v1） | 03（个人战，CombatManager） |
- 🔴 **状态表达三层纪律（执行过 = `Event::<id>.done` / 分支选择 = `Ctx::` 命名槽 / 世界状态 = 本体属性与函数，`Flag::` 只留跨事件标记）权威 = 01 纪律节**（违规实录与细节见 01）
- **后续扩展**：容器 `pick` 函数、组织扩展域（Org）、演出视觉元素（圖片/背景）
- **纪律**：转化管线遇到表外模式 = 回填 CSV 词条/动作行（mod 原生动作 `太阁原词` 列 = `—`）+ 扩展 01 注册表（validator 同步更新）

## 复跑（词表统计命令）

> 🔴 提取前先过滤注释行（`#` 开头——文件名/说明行会污染词条，2026-08-27 实机：evm 就是注释行提取出来的；生成器已内置，复跑直接跑 build_registry_csv.py 即可）

```bash
# 全量生成 + 覆盖自检（唯一入口）：python plans/scenario-campaign-mode/tools/build_registry_csv.py（表外 = exit(1)）
# 域：grep -oE '([一-鿿A-Za-z]{1,6})::' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 属性（v2 保留域维度：(域,属性) 对）：grep -oE '([一-鿿A-Za-z]{1,6})::[^.（()）]+\.([一-鿿A-Za-zＡ-Ｚａ-ｚ0-9０-９]+)' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 域值（v2 新增：域::值 形态——身份枚举/狀況值/命名槽）：grep -oE '([一-鿿A-Za-z]{1,6})::([一-鿿A-Za-zＡ-Ｚａ-ｚ0-9０-９]{1,14})(?=[),，）])' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 命令：grep -oE '^\s*([一-鿿]{2,8}):' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 动作行（mod 原生段）：16.md §六（token 已移出 16a，2026-08-27 用户裁定）
```

## 验收

1. 太阁5 全集抽样事件（附录第二/三节各类形态）→ 按本表翻译成 DSL → validator 全部通过
2. 代入槽全流程：ctx_set 赋值 → Ctx::A 引用求值 → 事件结束清理
3. 表内所有域/属性/函数/动作有实现 + validator 可检
4. 表外用法 → validator 报错（阻断），不会静默
