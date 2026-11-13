---
name: story-line-action
description: 给剧情 NPC 的**每一句台词配一个原版动作**，并保证这个 NPC 真的能播出来。凡涉及"这句台词跟着做什么动作""常驻动作/吆喝配什么""某个 act_ 在游戏里没反应""从原版 action set 里挑个手势"——都走本技能。核心是一条硬约束：动作必须来自 `as_human_warrior`，因为 `monsters.xml` 写明 human 的默认 action_set 就是它；别的合集（`as_human_hideout_bandit` / `as_human_musician` / `as_human_bartender` 这类职业集）里的动作，我们的 NPC 播了**没有任何反应**。不要凭名字猜动作可不可用。
---

# 台词配动作 — 入口

> **一句话**：台词动作是**数据**（对话节点里的 `NpcActions` 字段），不是代码；
> 每一个动作都必须能通过本技能的闸门（离线体检 + 游戏内探针）。

## 🔴 唯一的硬约束

```
monsters.xml:  <Monster id="human" action_set="as_human_warrior" female_action_set="as_human_female_warrior">
```

⇒ 剧情 NPC（帝国平民/守卫/匪帮，都是 `human`）**只会** `as_human_warrior` 里的动作。
`act_bartender_wipe` / `act_beggar_idle` / `act_play_celtic_harp_stand` / `act_bully` 这些看起来很贴人设，
但它们只存在于**职业专用集**（`as_human_tavern_keeper` / `as_human_beggar` / `as_human_musician` / `as_human_hideout_bandit`）
⇒ **播不出来**（2026-11-13 实测，9 个 NPC 的常驻动作 16/19 条是这种，已全部换掉）。

## 能用的动作库（`as_human_warrior` 里的对话手势）

| 类别 | 动作 |
|---|---|
| **风格 × 情绪矩阵** | `act_conversation_<风格>_<情绪>`，风格 = `aggressive` `closed` `confident` `demure` `hip`（各有 `2`/`3` 变体），情绪 = `start` `loop` `trivial` `positive` `very_positive` `negative` `very_negative` `unsure` |
| **专项手势** | `act_conversation_talk_explain`（解释）· `act_conversation_talk_commenting`（盘算/随口）· `act_conversation_talk_dunno`（耸肩）· `act_conversation_talk_crossedarms`（抱臂）· `act_conversation_talk_promise`（承诺）· `act_conversation_threat_arm` / `_point` / `_body` / `_cuttrhoat`（威胁）· `act_conversation_point_somewhere`（指方向） |

⚠️ 不是每个"风格×情绪"组合都存在。**写之前先查**（见下）。

## 三步

### ① 挑

```bash
# 列出 as_human_warrior 里所有对话手势
python Scripts/gen_line_actions.py            # 会打印生成结果与兜底情况
```

挑的原则：**说话人气质定风格，这句话的语气定情绪**。

| 谁 | 风格 |
|---|---|
| 老周（当铺掌柜） | `closed` |
| 寡妇 / 对台领班（戏班） | `hip` |
| 小贼 | `confident` |
| 屠户 / 收数人 | `closed` |
| 铁头 / 打手 / 野贼 | `aggressive` |

语气 → 情绪：威胁辱骂 = `very_negative`；拒绝/冷笑 = `negative`；谢/爽快/成交 = `positive`；其余 = `trivial`。

### ② 写

对话节点里的 `NpcActions` 字段：

```jsonc
{
  "LWN_story_pawnbroker_intro": ["act_conversation_closed_trivial", "act_conversation_closed_trivial", ""],
}
```

🔴 **数组长度必须等于这句台词的中文行数**（第 i 个动作配第 i 行）；`""` = 这行不配（玩家说话 / 旁白）。
运行期：`SayOnAgent` 播这条台词时，按行依次延迟 2.2 秒播动作。

改完 `custom.story_probe reload` 生效（不用重编）。

### ③ 验（两条都得绿）

```bash
python Scripts/check_story_probe.py       # 必须 problems=0
#   · 每个动作必须在 as_human_warrior 里
#   · 动作条数必须等于台词行数
#   · 常驻动作（StoryProbeCast.StreetLife）同样查
```

游戏内（**这条才是"真能播"的最终证据**）：

```
custom.story_probe action blocker act_conversation_aggressive_trivial
→ OK: '...' resolves in this agent's action set (index=N)
→ NO: '...' is NOT in this agent's action set (plays nothing)
```

## 已知坑

1. **职业动作集播不出来** —— 见上。看到"NPC 站着不动"先跑 `custom.story_probe action`。
2. **`_loop` 会一直循环** —— 常驻氛围用 `_loop`；配合台词用 `_trivial` / `_negative` 这类一次性档。
3. **`restoreAfter: true`** —— 播完自动回站位姿势；不传会一直定格在那个手势。
4. **女性 NPC** 理论走 `as_human_female_warrior`（只有 302 条）。寡妇目前用的是 `as_human_warrior` 的动作，
   实机若发现她不播，用 `custom.story_probe action widow <act>` 确认，必要时只给她挑两套里都有的动作。
5. **别在代码里写动作名** —— 全部走 JSON；生成器 `Scripts/gen_line_actions.py` 只负责**给初稿**，定稿由人改 JSON。
