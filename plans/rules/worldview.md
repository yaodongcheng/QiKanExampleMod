# 世界观与称呼纪律

## 核心原则

LivingWorldNpcs 是**通用 mod**（卡拉迪亚中世纪世界观）。**禁止**在代码/XML 中硬编码任何日本战国/织丰题材字串（含"主公"等称谓词）。

**🔴 世界观完全自动生成（2026-08-17 用户裁定，world-background-auto-summary.md）**：静态 flavor（`Settings.WorldDescription`/`EraDescription`）已删除。世界观 = LLM 基于游戏内文化/王国/关键英雄百科自动生成的单段文本（`WorldBackgroundBehavior` 生成 → `WorldBackgroundStore.Blob` → 存档 `lwn_world_background`），指纹（文化/王国/关键英雄 StringId 序列 + 语言 id）判定重新生成；LLM 未配置/生成失败 → 世界观段整段省略（铁律 1）。读取唯一入口 = `WorldBackgroundProvider.GetWorldSection(heroId)`（纯字符串查表，线程安全——PlanReplan 在 Task.Run 内构建 prompt，**禁止**在读取路径做引擎对象查找）。

**🔴 称呼纪律（2026-08-17 三版迭代定稿）**：称呼 = LLM 每次生成回复时按双方身份/阵营/阶级/性别/年龄**现场发挥**（生成产物，非配置参数）——禁止在 prompt 里硬编码任何称谓词（"主公"/"My lord" 等）。落地三件事：
1. 【称呼纪律】段：每次 prompt 构建时注入双方性别年龄 + 对方（玩家）族长/队长身份（`PromptBuilder.BuildAddressAndKinshipSections`）
2. 亲缘与身份认知段：NPC 与对方有亲缘时注入第一人称亲缘段（那塔诺斯案——亲缘关系重点说明 + 玩家族长/队长身份）
3. 模板中"主公/lord"已按语义替换为名字/中性词（A 层）；B 层（实时事实段/记忆材料/降级兜底）豁免——称呼纪律管出口称呼，材料中"主公"只是角色标签

**数据型背景 mod 适配**：文化/王国百科（`CultureObject.EncyclopediaText` 等）来自引擎 XML，数据型 mod（自定义文化/王国）天然被指纹机制适配；纯文本型 mod 需自行替换百科数据（已知降级）。Mod B 覆盖 Settings 字段的注入方式已失效（字段已删）。

## 禁止出现的字串

以下字串**不允许**出现在 LivingWorldNpcs 的任何 `.cs` 文件、注释、或 JSON 配置中：

- `Shokuho`、`Taikou`、`织丰`
- `日本战国`、`大河剧`
- `妾身`、`在下`、`主公`、`混账`
- `太阁`

> 例外（B 层豁免，2026-08-17）：实时事实段/记忆材料/调试文案/玩家名兜底中的"主公"是角色标签，非出口称呼——LLM 按【称呼纪律】段自然称名。A 层（纪律/模板/段标题/出口称呼）已全部清除。验收：prompt 转储无 A 层"主公/lord 直呼"残留。

## 正确做法

```csharp
// ✅ 正确：世界观走自动生成（纯字符串查表，任意线程安全）
string worldSection = WorldBackgroundProvider.GetWorldSection(heroId);
if (!string.IsNullOrWhiteSpace(worldSection))
    sb.AppendLine(LWNTextHelper.ResolvePrompt("LWN_plan_section_world") + worldSection);

// ✅ 正确：称呼纪律段（IM/respond/群聊/附近频道统一注入）
string addressSection = PromptBuilder.BuildAddressAndKinshipSections(agent, otherAgent);

// ✅ 正确：口吻参数仍走 Settings（config.json 侧）
string style = S.SpeechStyle;            // 默认: 中性中世纪口语
string female = S.FemaleSelfAddress;     // 默认: ""

// ❌ 错误
string world = "日本战国时代";           // 硬编码，破坏通用性
sb.AppendLine("你的主公 X 刚刚...");      // 硬编码称谓词（称呼纪律）
string prompt = "口吻符合日本战国背景";   // 同上
```

## Settings 口吻字段速查（config.json 侧）

| 字段 | 默认 | 说明 |
|------|------|------|
| `SpeechStyle` | 中性中世纪口语 | 说话风格（LLM 润色） |
| `WarriorTerms` | "大人"、"爵士" | 战斗台词风格 |
| `FemaleSelfAddress` | ""（空） | 女性自称 |
| `CurrencyName` | 随游戏语言 | 货币单位（不可硬编码） |

> 🔴 `WorldDescription`/`EraDescription` 已删除（2026-08-17）。新增世界观 flavor 类配置 → 改自动生成管线，不进 Settings。

## 验证

```bash
grep -ri "Shokuho\|织丰\|日本战国\|太阁\|大河剧\|妾身\|在下.*主公\|混账" --include="*.cs" .
# A 层残留检查（B 层材料允许出现）：
grep -n "主公" ModuleData/Languages/CNs/std_LivingWorldNpcs_prompts.xml   # 应为 0
grep -n "lord" ModuleData/Languages/std_LivingWorldNpcs_prompts.xml      # 仅保留项：warlord 职业名/a worthy lord 泛称/key 名
```

运行时验收：`custom.worldbg_status`（状态/指纹）/ `custom.worldbg_dump`（blob 人工抽查：无实时状态、无点名在位者人名）/ `custom.worldbg_regenerate`（强制重生成）；日志 `[WorldBg]`。
