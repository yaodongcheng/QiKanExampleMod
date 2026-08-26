# 16 — DSL 注册表全表（太阁5 ↔ 骑砍2 对照总表，单一事实源）

> 阶段：Phase 1 定稿 / 随覆盖需求扩展 ｜ 依赖：01（DSL 语法） ｜ 完成后：DSL 能表达太阁5 全集（除明确标注的后续扩展项）
>
> **审核状态：⏳ 未审核（禁止实施）** ｜ 审核人：用户 ｜ 审核日期：— ｜ 审核意见：—
> 通过后改为 ✅ 已审核（日期）+ 记录意见。

## 文件分工（2026-08-26 重构后，单一所有权）

- **词条翻译权威 = `16a-DSL翻译总表.csv`**（🔴 编号 16a = plan 16 的数据文件，与 plan 本体 `16-DSL注册表全表.md` 区分——文件夹惯例：字母后缀 = 主 plan 的子文件，先例 = 原 07a；442 行：域 42 / 属性 199 / 命令 192（太阁5（简称 TK5）语料（从太阁5 事件文本统计出的词表） 174 + mod 原生 18）/ 谓词 9；列 = 太阁原词·频率·类别·我们侧名·类型·语义·参数·实现用法·状态）——**查询词条翻译只看这一处**，域/属性/谓词/动作 token 全表都在 CSV。CSV 由 `tools/build_registry_csv.py` 程序化生成（复跑 `TK5AllEvents_merged.txt` + ACTIONS/MOD_NATIVE 字典）；改词条 = 改 `gen_registry_tables.py`/`build_registry_csv.py` 字典 + 重跑
- **本文件 = 机制权威**（CSV 装不下的）：§一 Ctx 三档作用域 ｜ §二 trigger/facility 注册表（🔴 2026-08-26 起**单所有权**，01 不再重复维护）
- **01 = 语法规则/类型纪律/安全兜底**（怎么解析；trigger 调度模型/validator（校验器，打包前检查剧本语法的工具）检查项在 01，注册表数据在 16）；**08 = 转化流程**（怎么翻译）
- **步骤类型注册在 01 步骤类型表**（perform / inquiry / im_message / cutscene / wait / bgm / se / scene_enter / choice / effect）；**05/03 = 台本指令/战斗预设格式注册**（actor_enter/camera/actor_action 等引擎内部 token，无 TK5 源词，不入 CSV）
- **表外用法 = 回填生成字典（见下方「CSV 编辑纪律」）+ 扩展 01 注册表**

## 🔴 CSV 编辑纪律（16a 是纯生成物，禁止直接编辑）

> `16a-DSL翻译总表.csv` 由 `tools/build_registry_csv.py` 每次**重写整个文件**——**任何列的任何直接编辑都会在下次重跑时被覆盖，禁止手改 CSV 本体**。改词条的唯一入口 = 改生成字典 + 重跑（重跑后 `git diff` 应只含预期变化）。

| 列 | 来源 | LLM/人工能填吗 | 写入位置 |
|---|---|---|---|
| 太阁原词 | TK5 语料正则提取（域/属性/命令）；`—`（谓词/mod 原生行） | ❌ **禁止**——语料事实，脚本复跑得出，手填必与语料不符 | — |
| 频率 | 语料统计；`—`（谓词/mod 原生行） | ❌ **禁止**——同上 | — |
| 类别 | 代码硬编码（域/属性/命令/谓词） | ❌ | — |
| 我们侧名 | 字典映射派生（`DOMAIN_MAP`/`ATTR_MAP`/`CMD_EXACT` + side_name 规则）；mod 原生行 = `MOD_NATIVE` 列表 token | ✅ **映射决策可设计**，但写入位置 = 字典，不是 CSV | [gen_registry_tables.py](tools/gen_registry_tables.py) 映射字典 |
| 类型 | `ATTR_TYPES` + 规则推断（布尔/数字启发式） | ✅ 新属性类型 = 补 `ATTR_TYPES` | build_registry_csv.py `ATTR_TYPES` |
| 语义 | 语义字典（`DOMAIN_SEM`/`ATTR_SEM`/`CMD_SEM`/`ACT_SEM`），兜底 = 词条名自解释 | ✅ 低风险人读信息（中文释义），补字典即可；兜底自解释也可接受 | 两个脚本的 `*_SEM` 字典 |
| 参数 | `ACTIONS` 字典（动作行）；`—`（域/属性行） | ✅ 新动作参数 = 补 `ACTIONS` | build_registry_csv.py `ACTIONS` |
| 实现用法 | 从 label 提取 / `ACTIONS` 字典 | ✅ 随动作登记（同上） | 同上 |
| 状态 | 规则派生（label 前缀 → `✅`/`🔴`/`❌`） | ❌ **禁止手填**——由 label 决定；想改状态 = 改 label | — |

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
| field_battle_start | 野戰開始時 | ✅ `OnMissionStarted` + `MissionMode.Battle` 判定 | 🔴 与 house_enter 同监听点分流（OnMissionStarted 分流模型见 01） |
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
| house | 自宅 | 229 | 🔴 原版无玩家住宅（实测 scn_player_house 0 命中）→ 织丰御殿/城主间顶替（09b opening 已用 sho_meeting_castle_a） | 🔴 07 素材表确认织丰住宅场景 |
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

## 三、覆盖结论

- **Phase 1 可全覆盖**（除明确标注后续扩展）：6 操作符 + 9 引擎域（含 Ctx 代入槽 + 🔴 Event 事件域）+ 🔴 Card 能力卡域（数据包扩展）+ 全属性白名单 + 9 谓词（含 canPromote，17）+ 动作全表（CSV 命令区：TK5 映射 + mod 原生 18 行，含 grant_merit/set_title/promote 17、duel 03）
- 🔴 **状态表达三层纪律（执行过 = `Event::<id>.done` / 分支选择 = `Ctx::` 命名槽 / 世界状态 = 本体属性与谓词，`Flag::` 只留跨事件标记）权威 = 01 纪律节**（违规实录与细节见 01）
- **后续扩展**：容器 `pick` 谓词、组织扩展域（Org）、演出视觉元素（圖片/背景）
- **纪律**：转化管线遇到表外模式 = 回填 CSV 词条/动作行（mod 原生动作 `太阁原词` 列 = `—`）+ 扩展 01 注册表（validator 同步更新）

## 复跑（词表统计命令）

```bash
# 域：grep -oE '([一-鿿A-Za-z]{1,6})::' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 属性：grep -oE '::[^.（()]+\.([一-鿿A-Za-z]+)' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 命令：grep -oE '^\s*([一-鿿]{2,8}):' TK5AllEvents_merged.txt | sort | uniq -c | sort -rn
# 动作行（mod 原生段）：build_registry_csv.py ACTIONS + MOD_NATIVE 字典
```

## 验收

1. 太阁5 全集抽样事件（附录第二/三节各类形态）→ 按本表翻译成 DSL → validator 全部通过
2. 代入槽全流程：ctx_set 赋值 → Ctx::A 引用求值 → 事件结束清理
3. 表内所有域/属性/谓词/动作有实现 + validator 可检
4. 表外用法 → validator 报错（阻断），不会静默
