---
name: story-dialogue-writing
description: 写 / 改剧情 NPC 的**对话脚本**（台词 + 选项 + 分段 + 路由拓扑）时走本技能。凡涉及"这句台词怎么写""把长对话拆短""选项该给什么""为什么玩家说他一说再说""对话读起来像独白/像 AVG 旁白""节点几行合适""检查对话漏洞"——都从这里起步。它给一份**可执行的写作与拓扑规格**（≤2 行、信息只说一次、每句必须推进、选项必须有代价或检定、禁止假选项、禁止孤立不可达节点、禁止自指复读、禁止条件与代价脱节、禁止直接关窗吃掉余下话题），配一条离线硬门禁命令，和「改完 → 写进 XML → 配动作 → 刷新页面 → 体检全绿」的固定流程。不要凭感觉发挥，也不要机械切行。
---

# 剧情对话写作 — 入口

> 🔴 **规则正文只有一份**：`plans/rules/dialogue-design.md`（15 条硬规则 + 标准形状 + 反例库）。本文件只讲"怎么用"。

> **一句话**：一个节点 = **NPC 的一次开口**，最多 2 行；每句话必须**推进**（给信息 / 给态度 / 给任务）；
> 同一件事**只许说一次**；玩家的每个选项都**得有代价或检定**；**每一个节点都必须可达、有合法出口、称呼与玩家认知一致**。写不出新东西的话，删掉，不要拿填充句凑。

## 十条硬规格（机器自动查 + 人工走查）

| # | 规则 | 怎么查 |
|---|---|---|
| 1 | **单节点 ≤2 行**（一个节点是他一次开口，不是一段独白） | `check_story_probe.py` **硬门禁报错** |
| 2 | **禁止假选项**：`……（听他说下去）`、`继续`、`嗯` 这类不给玩家选择的填充项一律禁用 | `check_story_probe.py` 自动查 |
| 3 | **禁止孤立不可达节点（Orphan Node）**：每个节点必须能从合法入口（`_intro`/`_hub`/`_turnin`/`_chat`/`event_*_join`/`blocker_*`）通过 `NextNodeOnSuccess`/`NextNodeOnFail` 到达 | `check_story_probe.py` 自动查 |
| 4 | **禁止自指循环（Self-Loop）与回跳 `_intro`**：选项的目标节点不得等于当前节点，也不得从对话中途跳回 `_intro`（重播初次见面） | `check_story_probe.py` 自动查 |
| 5 | **禁止问句直接跳菜单（`*_hub`）**：带问号的选项必须跳专门回答节点，不得跳回 `_hub`（否则对方只回"还有事？"） | `check_story_probe.py` 自动查 |
| 6 | **禁止两种不同问法指向同一个非菜单节点**：首问与再问、不同措辞不可共用同一回答节点 | `check_story_probe.py` 自动查 |
| 7 | **禁止两条不同选项共用同一个 `Once` 闸门**：一条选项一个 `Once`，否则点一条两条同时消失 | `check_story_probe.py` 自动查 |
| 8 | **付费/塞钱动作必须配 `Requires: gold>=N` 且选项标明金额**：`PROBE:pay` / `pay_to` / `bribe` / `offer_money` / `money_deal` 不可无条件暴露（铁律 12 + §5.5 钱不够不出现） | `check_story_probe.py` 自动查 |
| 9 | **台词格式强制双语严格对齐**：每行必须是 `（说话人，听话人）：文本` / `(Speaker, Listener): Text`，禁混用英文 `Player` 或未翻译 ID（如 `debtor`/`thug`） | `check_story_probe.py` 自动查 |
| 10 | **同一信息只说一次 & 称呼匹配玩家认知 & 打听完回 `_hub`**：名字/价码/线索不复读；未打听出真名前不直呼其名（或挂 `Requires`）；非终局回答节点给回 `_hub` 的收尾项而非直接关窗 | 人工 + 结构走查 |

## 🔴 最常见的六个错（都是实战踩过的）

### ① 重复自报家门 / 跨节点复读

```
intro  ：我姓周，街坊都叫老周。这铺子开了二十三年。
   ↓ 你选"先说说你自己吧"
ask_self：我？姓周，人家都叫老周。        ← 同一句说两遍
```
✅ 正确：**名字只在"说说你自己"里给**；开场只说"这是当铺"。交差节点只说新进展，不复读前面打听过的弱点。

### ② 机械切行 + 加填充选项

把 4 行的节点切成 2+2，中间塞一句 `……（听他说下去）`。
**错**：玩家没有做任何选择，而且多一次点击。
✅ **对**：重写这段，让第一段 ≤2 行直接立住核心信息，第二段由**玩家主动追问**引出。

### ③ 删选项留孤岛 / 只配 `[离开]` 导致死路

在 `_story_text_*.py` 写了全套节点和选项，但 JSON 里把入口选项删了，或节点只放一个 `[离开]`，导致：
- `pawnbroker_errand`（零活）、`pawnbroker_turnin_ok`（正常交差拿钱）、`thug_talk`、`debtor_collect`（收账）全成了**永远进不去的死节点**！
- 玩家在寡妇/小贼/屠户处问完一个问题，没有 `[收住] 别的先不问了 → _hub`，只能直接关窗退出演讲！
✅ **对**：所有非终局（非开打、非交差完结、非过门）的回答节点，末尾必须挂回 `_hub` 的收尾选项；`check_story_probe.py` 会强制检查所有节点可达性。

### ④ 称呼穿越（玩家未卜先知）

玩家还没跟老周打听守门人叫什么，一上来选项就写 `门口那个铁头`；或者老周嘴里蹦出英文 ID `他叫 debtor`、`那个收数的 thug`。
✅ **对**：统一用符合世界观的中文名或身份称呼（如 `账房老蔡`、`疤脸刀客`），且只有在获知名字的节点之后（或挂 `Requires: clue:...`）才直呼其名。

### ⑤ 塞钱不标价、不挂 `gold>=N`、重复扣费

- `pawnbroker_money_covert` 用了 `pawnbroker_opt_deal`（`[求] 借我点钱`）去触发 `PROBE:money_deal`（扣玩家 80 第纳尔）——文案是借钱，实际是花钱！
- `widow_money_open` / `son_money_open` 塞钱选项在 `intro`/`hub` 就说"拿着"，到了下一层才点"问问题"才扣钱，且没挂 `gold>=60` / `gold>=40`，0 钱玩家也能点进去！
✅ **对**：塞钱/买消息必须挂 `Requires: gold>=N`，选项文案写明价码（如 `[塞钱] 六十第纳尔，买你两句实话。`），动作与文案语义严格一致。

### ⑥ 事件胜负后没有后续入口

事件 2/3/4/6 赢了或帮了忙，节点只有 `[离开]`，玩家不知道该找谁接委托。
✅ **对**：所有事件收尾节点（`event_*_win` / `lose` / `paid`）统一提供 `[跟他说两句] → <host>_hub`。

## 格式（两条都强制）

```
（说话人，听话人）：文本          ← NPC 与玩家之间
（打手，老周）：钱呢？这月的。     ← NPC 之间
（旁白，玩家）：路让开了。         ← 只用于"结果播报"，不是动作描写
```

英文同构：`(Thug, Old Zhou): The money. This month's.`

## 写一场对话的顺序

1. **先列信息清单**：这场戏玩家必须拿到几件事？（例：妹妹押过镯子 / 码头帮押人 / 铁头贪钱）
2. **一件事 = 一个节点**，每个节点 ≤2 行。信息多的拆成**并列选项**或**递进追问**（玩家问哪个就听哪个），不是排队说。
3. **核对五步关系闭环**：
   - `_intro`（初见）与 `_hub`（菜单，出度 ≥5，无条件非离开选项 ≤4）
   - 打听分支（妹妹线索 / 对手底细 / 过门门路）→ 答完可回 `_hub`
   - `_deal`（求帮忙/派委托）→ `_quest_accept`（`EnterAction: PROBE:accept_quest`）
   - 仇家节点（`_intro` / `_hub` / 对质或讨账或劝退 / `_fight`）
   - `_turnin` → `_turnin_ok`（`EnterAction: PROBE:turn_in`）→ `_chat`（熟人闲聊 + 零活/后续）
4. **给每个行动/检定选项配代价或检定**（给钱带 `gold>=N` / 掷点带 `NextNodeOnFail` / 关系变化）。
5. **配动作**：每行台词配一个动作（走 `story-line-action` 技能，动作必须在 `as_human_warrior`）。

## 改完怎么落地（五步缺一不可）

```bash
# ① 改 Debug/offline/_story_text_*.py（全部文本统一维护，含金钱提示与系统提示）
# ② 写进中英两个语言 XML
cd "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs\Debug\offline"
python _insert_story_probe_text.py

# ③ 按台词行数重新配动作（写回每个节点的 NpcActions）
cd "H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\LivingWorldNpcs"
python Scripts/gen_line_actions.py --write

# ④ 重新生成玩法演示页（台词树 + 角色配表卡）
python plans/逆水寒511/tools/insert_dialogue_html.py
python plans/逆水寒511/tools/gen_cast_html.py

# ⑤ 跑离线体检（必须 problems=0 且 long_nodes_todo=0）
python Scripts/check_story_probe.py
```

