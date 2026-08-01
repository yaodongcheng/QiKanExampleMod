# 本地化迁移方案：CSV → 标准 Bannerlord XML

> **状态：Phase 1 ✅ | Phase 2 ⚠️ EN XML 已生成但不生效 | Phase 3 ✅ | CSV 已删除 ✅**

## 执行进度

| 步骤 | 状态 | 说明 |
|------|:---:|------|
| Step 0: LoadLocalizationXmls | ❌ 已移除 | 曾添加但发现会干扰全局语言注册表，已删除。引擎自动扫描 Languages/ 目录 |
| Step 1: LWNTextHelper | ✅ | `Localization/LWNTextHelper.cs`，三个入口：Resolve / ResolveText / ResolveCompound + TryResolveText |
| Step 2: XML 骨架 | ✅ | `CNs/language_data.xml` 加 include，`addition_2_CNs.xml` tag `Chinese`→`简体中文` |
| Step 3: CSV → XML 提取 | ✅ | 2218 条目已生成 |
| Step 4: C# 桥接 | ✅ | NarrativeResolver / NpcSpeechResolver XML 优先 + key 构造（CSV 已移除） |
| Step 5 Batch 0-8: 全部文件 | ✅ | ~80 个 C# 文件，~2900 站点迁移 |
| Step 6: Python 校验脚本 | ✅ | `validate_localization.py`，11 项铁律自动检查 |
| Step 7: 验收 | ✅ | build 0 errors, 0 CJK 硬编码, XML ~2200 条目 |
| CSV 删除 | ✅ | Narrative.csv + NpcSpeech.csv 已删除，C# 中 CSV 引用全清 |
| Emoji 清理 | ✅ | CNs XML 中 41 个 BMP+ emoji 已移除（会导致 UTF-16 解析崩溃） |
| **Phase 2: EN 翻译** | ✅ | 2218 条目已翻译，`EN/std_LivingWorldNpcs_strings.xml` |
| **Phase 2: EN 生效** | ❌ | 🔴 **EN XML 在游戏中不生效**，需排查原因 |
| **Phase 3: 收尾** | ✅ | 注释补全 + 白名单更新 + CJK 残留清理 |

### Phase 1 最终数据

| 指标 | 迁移前 | 迁移后 |
|------|--------|--------|
| 硬编码 CJK 站点 | 3020 (77 files) | 13 (7 files, 均为注释/debug) |
| `{=!}` 标记 | 9 | 0 |
| LWN_ XML 条目 | 0 | ~2200 |
| Build | ✅ | ✅ 0 errors, 0 warnings |

### Phase 1 新增文件

| 文件 | 说明 |
|------|------|
| `Localization/LWNTextHelper.cs` | TextObject + PlaceholderResolver 统一桥接 |
| `Scripts/csv_to_xml.py` | CSV → XML 一次性提取 |
| `Scripts/validate_localization.py` | 11 项铁律自动校验（`--cs-only` / `--xml-only` / `--strict`） |
| `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml` | ~2200 条中文本地化字符串 |

### Phase 1 架构要点

- `ResolveText(key, fallback)` — 纯文本，无占位符 → `TextObject("{=KEY}fallback")`
- `Resolve(key, resolver, fallback)` — 叙事文本，PlaceholderResolver 提供全部占位符值
- `ResolveCompound(key, fallback, ("VAR", val), ...)` — 调用方显式传值（DisplayMessage / 飘字场景）
- `InformationManager.DisplayMessage` / `AddQuickInformation` → 走 `ResolveCompound`（**不能豁免**，玩家可见）
- `DebugLogger.Log` → 不需本地化（豁免）

## 背景

mod 目前所有叙事文本存在 CSV 文件（`Narrative.csv`、`NpcSpeech.csv`）里，放在 `ModuleData/DesignData/` 下，通过自建的 `CsvLoader` → `GameDatabase` → `NarrativeResolver`/`NpcSpeechResolver` → `PlaceholderResolver` 管线查询和替换占位符。为了公开发布，需要迁移到骑砍2标准的 XML 本地化系统（`ModuleData/Languages/`），以支持多语言。

**影响范围：只改文本获取层，不动游戏逻辑。** `DialogueInjector` 的对话树结构、节点流转、条件判断、后果执行全部保持原样。唯一变化是节点里的文本从 `"硬编码中文"` / `CSV 行` 变成 `LWNTextHelper.Resolve("LWN_KEY", ...)`。`AgentBrain`、`WorldEvent`、`CommissionQuest`、`SocialEventManager` 等核心系统完全不受影响。

**核心难点**：CSV 文本是按中文语法语序写的（如 `{LOCATION}急缺{ITEM}！…买{COUNT}单位的{ITEM}…{REWARD}第纳尔。`）。翻译成英文或其它语言时，不能简单替换占位符周围的文本——每种语言需要自己的语法结构，但 **占位符集合必须完全一致**，语义也必须相同。

## 现状盘点

### 已有基础设施
- **Language 文件夹**：`ModuleData/Languages/CNs/` 已存在，含 `language_data.xml`（UTF-16）+ `addition_2_CNs.xml` + `output_strings2.xml`（氏族名等 TaikouContent 数据）
- **Key 前缀先例**：`PlayerDetentionBehavior.cs` 已用 `LWN_` 前缀调 `MBTextManager.SetTextVariable("LWN_FINE", ...)`
- **`{=!}` 模式**：`TextObject("{=!}中文")` 中的 `{=!}` 告诉引擎"不要查语言表，直接用字面文本"。目前大量玩家可见的 UI/对话都用了 `{=!}硬编码中文`——英文玩家也会看到中文。迁移时要把这些替换为 `{=LWN_KEY}English fallback`，让引擎有机会查语言表。`{=!}` 语法本身没问题，调试日志/内部文本继续用。
- **`{=key}fallback` 模式**：`QuestManager.cs` 已在用 `new TextObject("{=q_progress}当前进度")`，但缺少 XML 条目，永远回落

### 关键缺口（探索发现）
1. **没有 `LocalizedTextManager.LoadLocalizationXmls()` 调用** —— 现有的 CNs XML 文件完全没有被代码加载！必须在 `OnSubModuleLoad` 补上
2. **CSV 文本全部是硬编码中文** —— Narrative.csv 约 200+ 行，NpcSpeech.csv 约 25 行
3. **CrimeDialogueBuilder 里还有大量内联中文** —— C# 源码中硬编码的对话节点文本
4. **PlaceholderResolver 中也有硬编码中文** —— `ResolveOne()` 里的 `"犯罪"`、`"没人看见"` 等

### 数据流（现状 vs 目标）

**现状**：
```
CSV 行 → CsvLoader → GameDatabase → NarrativeResolver.Resolve(filters)
→ 随机取一行 Text → SubstituteCommissionPlaceholders(template, data)
→ string.Replace("{LOCATION}", name) → 返回
```

**目标**：
```
localization XML → 引擎自动加载 → TextObject("{=LWN_KEY}English fallback")
→ .SetTextVariable("LOCATION", name) → .ToString() → 返回
```

## 🔴 铁律校验：双层防线

**脚本层**（`validate_localization.py`）：查机械错误——格式、完整性、一致性。跑得快，零误判。
**Claude Code 层**（`/check-localization` skill）：查语义问题——注释是否准确、翻译质量、语感。AI 才能判断。

| # | 铁律 | 脚本 | Claude | 说明 |
|---|------|:---:|:------:|------|
| A | C# 禁止硬编码中日文 | ✅ | ✅ | 脚本扫 CJK 字符；Claude 扫隐蔽的中文拼接/插值 |
| B | Key 上一行必须有中文注释 | ✅ | ✅ | 脚本检查注释**有没有**；Claude 检查注释**对不对**（是否准确描述了 key 的含义） |
| C | Key 跨语言齐全 | ✅ | — | 纯机械对比，脚本足够 |
| D | 占位符跨语言一致 | ✅ | ✅ | 脚本逐字对比；Claude 检查语义是否等价（如 `{TARGET}` 在两种语言中是否指向同一概念） |
| E | 占位符在白名单中 | ✅ | — | 纯正则匹配 |
| F | Key 命名符合 `LWN_` 规范 | ✅ | — | 纯正则匹配 |
| G | 禁止新 `{=!}` | ✅ | — | 脚本扫即可 |
| H | XML 无重复 key | ✅ | — | 纯机械 |
| I | language_data.xml 引用齐全 | ✅ | — | 纯文件检查 |
| J | CSV-XML 占位符迁移一致 | ✅ | — | 一次性校验 |
| K | PlaceholderResolver 无硬编码 | ✅ | — | 脚本扫即可 |
| L | 语义等价（翻译质量） | — | ✅ | **只能 AI 判**——需要理解两种语言的语义 |
| M | 语序/语法适配 | — | ✅ | **只能 AI 判**——需要理解语法和语感 |
| N | **禁止 C# 拼接多个本地化文本片段** | ✅ | ✅ | 脚本扫 `$"..."` 中含占位符的插值警告；Claude 审查语序是否被 C# 拼接顺序锁死。**语序由 XML 模板控制，不由 C# 控制。** |

> 原则：**能用脚本的就用脚本**（快 + 零误判），**脚本做不到的再用 Claude**（语义理解）。两层都通过才能合入。



## Key 命名规范

```
LWN_{领域}_{描述}
```

| 领域 | 模式 | 示例 |
|------|------|------|
| Narrative.csv → XML | `LWN_narr_{简化事件名}_{变体}` | `LWN_narr_supply_emergency_opening_brave` |
| NpcSpeech.csv → XML | `LWN_speech_{模板ID小写}` | `LWN_speech_alert_bubble_steal_cautious` |
| UI / 菜单 | `LWN_ui_{元素}` | `LWN_ui_detention_pay_fine` |
| PlaceholderResolver 返回值 | `LWN_ph_{占位符名小写}` | `LWN_ph_crime_verb`（替换以前的 `"做了"`） |
| 系统 / 错误 | `LWN_sys_{消息}` | `LWN_sys_llm_not_ready` |

规则：
- 全小写 + 下划线分隔
- 看 key 名就能读懂含义
- 最长约 60 字符
- CSV 中的复合 ID（如 `SupplyEmergency_Opening_Brave_0_100`）→ 按维度拆成 C# 侧的 fallback 链，每个落脚点有一个独立 key

## 🔴 字符串拼接：本地化的头号杀手

代码里大量存在这种模式：

```csharp
// ❌ 错误：C# 拼接控制了中文语序，英文/日文无法改
return $"{si}{sn}";                    // 中文"领主史密斯"，英文需要"Lord Smith"
return $"查了{days}天了";              // 中文动词在前，日文动词在后
return $"{victimPart}，还少了{desc}";  // 逗号、连接词全是中文
return $"{kv.Value}只{name}";          // 量词硬编码，"1头牛" vs "1 cow"
```

**为什么这是致命的**：
- 中文语序：Subject + Verb + Object
- 英文语序：Subject + Verb + Object（恰好相同，但这批文本也有不一样的地方）
- 日文语序：Subject + Object + Verb（动词在最后！）

C# 拼接锁死了语序 → 换语言就崩。**整个句子的语序必须由 XML 模板控制，不由 C# 拼接顺序控制。**

### 修复模式

**模式 1：简单拼接 → 单 key + 多变量**

```csharp
// ❌ 旧：C# 控制语序
case "SuspectDescription":
    return $"{si}{sn}";  // 中文：身份在前 → "领主史密斯"

// ✅ 新：XML 控制语序，C# 只提供数据
case "SuspectDescription":
    // 嫌疑人身份+姓名的完整描述（语序由 XML 控制）
    return LWNTextHelper.ResolveCompound("LWN_ph_suspect_description",
        ("IDENTITY", si), ("NAME", sn));
```

XML：
```xml
<!-- 中文：身份在前 -->
<string id="LWN_ph_suspect_description" text="{IDENTITY}{NAME}" />
<!-- 英文：名在前 -->
<string id="LWN_ph_suspect_description" text="{IDENTITY} {NAME}" />
```

**模式 2：复杂叙事（BuildDiscoveryFacts / BuildStolenItemsDescription）→ 分支选 key**

这类方法有大量分支逻辑（1 个受害者 vs N 个受害者，纯牲畜 vs 混合物品）。不能简单用一个模板，但可以**每个分支对应一个 key**：

```csharp
// ❌ 旧：分支拼接中文字符串
if (HasAssault && hasStolen)
{
    string victimPart = names.Count == 1
        ? $"{names[0]}被人打晕了"
        : $"有{names.Count}人被人打晕了";
    return $"{victimPart}，还少了{BuildStolenItemsDescription()}";
}

// ✅ 新：每个分支选不同的 localization key
if (HasAssault && hasStolen)
{
    string key = names.Count == 1
        ? "LWN_narr_discovery_assault_theft_single"
        : "LWN_narr_discovery_assault_theft_multi";
    return LWNTextHelper.ResolveCompound(key,
        ("VICTIM", names[0]),
        ("COUNT", names.Count.ToString()),
        ("STOLEN", BuildStolenItemsKey()),  // 不是字符串，是子 key！
        ("VALUE", TotalStolenValue.ToString()));
}
```

XML 中文：
```xml
<string id="LWN_narr_discovery_assault_theft_single" 
        text="{VICTIM}被人打晕了，还少了{STOLEN}，市值{VALUE}第纳尔" />
<string id="LWN_narr_discovery_assault_theft_multi" 
        text="有{COUNT}人被人打晕了，还少了{STOLEN}，市值{VALUE}第纳尔" />
```

XML 英文：
```xml
<string id="LWN_narr_discovery_assault_theft_single" 
        text="{VICTIM} was knocked out, and {STOLEN} worth {VALUE} denars went missing" />
<string id="LWN_narr_discovery_assault_theft_multi" 
        text="{COUNT} people were knocked out, and {STOLEN} worth {VALUE} denars went missing" />
```

**模式 3：量词/单位 → 和物品名一起进 XML**

```csharp
// ❌ 旧：量词硬编码 "一只羊" → 英文无法表达 "1 sheep"（不需要量词）
string unit = isAnimal ? "只" : "件";
parts.Add(kv.Value == 1 ? $"一{unit}{name}" : $"{kv.Value}{unit}{name}");

// ✅ 新：数量+物品作为一个占位符变量，各语言 XML 各自处理量词
// C# 侧只提供数量和物品名，不拼接量词
text.SetTextVariable("ITEM_DESC", $"{kv.Value} {name}"); // 只提供原始数据
// XML 英文："{ITEM_DESC}" → "1 sheep" / "3 tunics"
// XML 中文：需要更细粒度的 key 来处理量词
```

> **注意**：模式 3（量词/单位）是最复杂的——中文有丰富的量词系统（只/件/头/匹/第纳尔），英文大多不需要。理想方案是每种"数量+物品"组合都有一个 key，但量太大。**务实方案**：`BuildStolenItemsDescription()` 不迁移到 XML，仍然生成自然语言描述，由 `{StolenItemDesc}` 占位符整体注入到外层 XML 模板中——即**内层拼接，外层模板化**。

### 拼接点审计清单

以下是当前代码库中已知的拼接点，迁移时必须逐一处理：

| 位置 | 拼接内容 | 处理方式 |
|------|----------|----------|
| `PlaceholderResolver:232` | `$"{si}{sn}"` — 身份+姓名 | 模式 1：单 key + 多变量 |
| `PlaceholderResolver:257` | `$"{pwi}{pwn}"` — 目击者身份+姓名 | 模式 1：单 key + 多变量 |
| `PlaceholderResolver:206` | `$"查了{days}天了"` — 调查天数 | 模式 2：key `LWN_ph_investigation_duration` |
| `PlaceholderResolver:178` | `$"，{desc}不见了"` — 被盗物品从句 | 模式 2：key `LWN_ph_stolen_clause` |
| `WorldEvent:708-710` | `$"{names[0]}被人打晕了"` 等 | 模式 2：按分支选 key |
| `WorldEvent:750-751` | `$"一{unit}{name}"` / `$"{kv.Value}{unit}{name}"` | 模式 3：内层保留，外层模板化 |
| `WorldEvent:758` | `$"等{totalCount}只牲口"` / 财物 | 模式 3：内层保留，`{StolenItemDesc}` 整体注入 |
| `WorldEvent:550` | `$"丢了...市值...第纳尔..."` | 模式 2：key `LWN_narr_theft_summary` |
| `NarrativeResolver:172` | `evt.BuildStolenItemsDescription()` | 模式 3：保持为子函数 |
| `NarrativeResolver:175` | `evt.BuildDiscoveryFacts()` | 模式 2：重构为 key 选择器 |

## 占位符处理方案

### 核心原则：替换机制从 Regex 切到 TextObject.SetTextVariable

Bannerlord 的 `TextObject` 原生支持 `{VAR}` 语法。XML 里直接写 `{VAR}`，代码侧 `.SetTextVariable("VAR", value)` 填充。

例如 XML：
```xml
<string id="LWN_narr_supply_emergency_opening_any" 
        text="{LOCATION} urgently needs {ITEM}! ...{REWARD} denars." />
```

代码：
```csharp
// 城镇居民紧急求购：某地缺货，出赏金托玩家跑腿
TextObject text = new TextObject("{=LWN_narr_supply_emergency_opening_any}"
    + "{LOCATION} urgently needs {ITEM}! ...{REWARD} denars.", null);
text.SetTextVariable("LOCATION", settlementName);
text.SetTextVariable("ITEM", itemName);
text.SetTextVariable("REWARD", reward);
return text.ToString();
```

### PlaceholderResolver 的定位变化

**不删**，但角色从"文本替换引擎"变为"占位符值计算器"：
- `ResolveOne(key)` 继续负责计算每个占位符对应的游戏数据
- 不再自己拼接字符串，改为给 `TextObject.SetTextVariable` 提供值
- `LWNTextHelper` 做桥接：从 `PlaceholderResolver` 取所有占位符值 → 批量 `SetTextVariable` → `TextObject.ToString()`

## 实施步骤

### Overview：两阶段推进

```
Phase 1（本次实施）：中文 CSV → 中文 XML + C# 桥接
  ├── Step 0: LoadLocalizationXmls
  ├── Step 1: LWNTextHelper
  ├── Step 2: XML 骨架
  ├── Step 3: CSV → XML 提取脚本
  ├── Step 4: 桥接 NarrativeResolver + NpcSpeechResolver
  ├── Step 5: 清理 C# 硬编码中文
  ├── Step 6: Python 自检脚本
  └── Step 7: 验收

Phase 2（体现为 skill）：基于中文 XML 自动生成其他语言
  └── /localize skill：读 CNs XML → LLM 翻译 → 输出 EN/JP/... XML
      → 自动跑 validate_localization.py 校验 → 提示人工抽查
```

---

### Step 0：补上 `LoadLocalizationXmls` 调用（前提条件）

**修改**：`Core/MySubModule.cs` 的 `OnSubModuleLoad`

```csharp
// 加载本 mod 的语言包（XML 字符串表），必须在引擎初始化之后调用
LocalizedTextManager.LoadLocalizationXmls(
    new[] { ModuleHelper.GetModuleInfo("LivingWorldNpcs").FolderPath });
```

> 参照：`Knowledge/csdn_column_articles/骑砍Ⅱ霸主MOD开发(3)-…语言…字体.md` 第五节。这步不做，后面全部白费。

同时修正 `CNs/language_data.xml` 中 `<tag language="..."/>` 与 `LanguageData id` 的一致性——目前 `addition_2_CNs.xml` 的 tag 是 `language="Chinese"` 但 `language_data.xml` 的 id 是 `简体中文`，引擎可能匹配不上。

---

### Step 1：创建 `LWNTextHelper` 桥接类

**新建**：`ExampleModVS/ExampleMod/ExampleMod/Localization/LWNTextHelper.cs`

统一入口，封装 `TextObject` + `PlaceholderResolver`。不需要 `GameTexts.FindText`（Bannerlord 没有这个 API）——直接用 `new TextObject("{=KEY}fallback", null)`，引擎自动处理 XML 查找 + 兜底。

```csharp
public static class LWNTextHelper
{
    /// <summary>
    /// 用 localization key 取文本 + 用 PlaceholderResolver 填占位符。
    /// TextObject("{=KEY}fallback") 自带 XML 查找：查到用翻译，查不到用 fallback。
    /// </summary>
    public static string Resolve(string key, PlaceholderResolver resolver, string fallback = null)
    {
        string fallbackText = fallback ?? key;
        TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
        ApplyAllVariables(text, resolver);
        return text.ToString();
    }

    /// <summary>不带 PlaceholderResolver 的纯文本解析。</summary>
    public static string ResolveText(string key, string fallback = null)
    {
        string fallbackText = fallback ?? key;
        TextObject text = new TextObject($"{{={key}}}{fallbackText}", null);
        return text.ToString();
    }

    /// <summary>
    /// 拼接场景专用：key + 显式指定的键值对（不走 PlaceholderResolver 全局扫）。
    /// 用于语序敏感拼接——每个变量由调用方显式传递，不由 ResolveOne 全局匹配。
    /// </summary>
    public static string ResolveCompound(string key, params (string var, string value)[] variables)
    {
        TextObject text = new TextObject($"{{={key}}}{key}", null);
        foreach (var (var, value) in variables)
        {
            if (!string.IsNullOrEmpty(value))
                text.SetTextVariable(var, value);
        }
        return text.ToString();
    }

    /// <summary>从 PlaceholderResolver 提取全部占位符值，批量 SetTextVariable。</summary>
    private static void ApplyAllVariables(TextObject text, PlaceholderResolver r)
    {
        foreach (var key in AllKnownPlaceholders)
        {
            string value = r.ResolveOne(key);
            if (!string.IsNullOrEmpty(value))
                text.SetTextVariable(key, value);
        }
    }

    private static readonly string[] AllKnownPlaceholders = {
        "LOCATION", "ITEM", "COUNT", "REWARD", "TARGET", "PLAYER",
        "SPEAKER", "NPC", "GIVER", "DEPOSIT", "DAYS", "PAYER",
        "INSTIGATOR", "VICTIM", "StolenItemName", "StolenItemDesc",
        // ... 完整列表从 PlaceholderResolver.ResolveOne 的所有 case 提取
    };
}
```

此后 **所有叙事文本获取都走这个入口**，不直接调 `GameTexts.FindText`。

---

### Step 2：构建 XML 骨架

**修改**：`ModuleData/Languages/CNs/language_data.xml`（加 include + 修正 tag 一致性）

**新建**：
```
ModuleData/Languages/
├── CNs/
│   ├── language_data.xml                        (修改：加 include)
│   ├── addition_2_CNs.xml                       (已有，不动)
│   ├── output_strings2.xml                      (已有，不动)
│   └── std_LivingWorldNpcs_strings.xml          (NEW — 全部叙事+警戒台词中文)
├── EN/
│   ├── language_data.xml                        (NEW — 英文清单)
│   └── std_LivingWorldNpcs_strings.xml          (Phase 2 由 skill 自动生成)
```

> **Narrative 和 NpcSpeech 合并到一个 XML。** XML 引擎按 key 查找，不关心 key 来自哪个 CSV。Key 前缀（`LWN_narr_*` vs `LWN_speech_*`）本身就区分了语义来源。

XML 格式（严格对齐原版 `std_*.xml`）：
```xml
<?xml version="1.0" encoding="utf-8"?>
<base xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" 
      xmlns:xsd="http://www.w3.org/2001/XMLSchema" type="string">
  <tags>
    <tag language="简体中文" />
  </tags>
  <strings>
    <!-- ═══════════════════════════════════════════ -->
    <!-- Narrative — 委托开场/结账叙事                 -->
    <!-- ═══════════════════════════════════════════ -->
    <string id="LWN_narr_supply_emergency_opening_any" 
            text="{LOCATION}急缺{ITEM}！我已经撑不了多久了。去市场帮我买{COUNT}单位的{ITEM}——越快越好！{REWARD}第纳尔。" />
    <!-- 更多条目 -->
    
    <!-- ═══════════════════════════════════════════ -->
    <!-- NpcSpeech — 警戒气泡 / L3 质问台词            -->
    <!-- ═══════════════════════════════════════════ -->
    <string id="LWN_speech_alert_bubble_crouching_suspicious" 
            text="（嘀咕）{PLAYER}在这做什么……" />
    <!-- 更多条目 -->
  </strings>
</base>
```

`EN/language_data.xml`（Phase 2 用）：
```xml
<?xml version="1.0" encoding="utf-8"?>
<LanguageData id="English" name="English" subtitle_extension="en-GB" 
  supported_iso="en-GB,en-US,en,eng,..."
  text_processor="TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor"
  under_development="false">
  <LanguageFile xml_path="EN/std_LivingWorldNpcs_strings.xml" />
</LanguageData>
```

> 英文需要 `text_processor`（处理复数/语法），中文不需要。每个语言只需一个 XML + 一行 include。

---

### Step 3：提取实际使用的文本 → XML

**核心原则**：只迁 C# 代码**真正引用**的条目。审计结果（`Scripts/csv_to_xml.ps1` 据此过滤）：

#### 来源 1：NpcSpeech.csv — 全部 25 行均有引用 ✅

直接用 `LWN_speech_{id小写}`。所有 ID 都被 `NpcSpeechResolver.Resolve(templateId, ...)` 引用（`AgentBrain.cs` + `CrimeDialogueBuilder.cs`）。

#### 来源 2：Narrative.csv — 以下 ID 族为 live，其余为死数据

| 族 | 引用位置 | Key 命名 | 数量 |
|---|---------|---------|------|
| `Steal_Caught*` | `AtomicAction.cs` | `LWN_narr_steal_{action}` | ~8 |
| `NPC_Opening_{Warm/Neutral/Cold}_{High/Neutral/Low}` | `WorldEventDirector.cs:558` | `LWN_narr_npc_opening_{relation}_{honor}` | 9 |
| `Gossip_WorldEvent_{Type}` / `Gossip_EventExpired_{Type}` | `WorldEventDirector.cs:315` | `LWN_narr_gossip_{type}` / `LWN_narr_gossip_expired_{type}` | ~52 |
| `WorldEvent_{Greeting/Weather}_{Type}_{Role}` | `WorldEventDirector.cs:775` | `LWN_narr_event_{topic}_{type}_{role}` | ~104 |
| `EventNotify_{Type}[_{Stage}]` | `NotificationPipeline.cs:137` | `LWN_narr_event_notify_{type}_{stage}` | ~78 |
| `Chat_{Greeting/Weather/Gossip/Praise}` + 因素 | `InteractionController.cs:552` | `LWN_narr_chat_{topic}` | 4 |
| `BubbleGreet` + 因素 | `AgentBrain.cs:501` | `LWN_narr_bubble_greet` | 1 |
| `RecruitSoldier_{Outcome}`, `RecruitHero_Success` | `RecruitSoldierIntent.cs` | `LWN_narr_recruit_{type}_{outcome}` | ~4 |
| `Order` | `GeneralIntents.cs:52` | `LWN_narr_order` | 1 |
| Commission Opening/Closure（按 Category+Phase+Trait+Grade 维度） | `CommissionNarrative.cs` | `LWN_narr_commission_{category}_{phase}_{trait}_{grade}` | ~17×N |
| WorldEvent 委托叙事 | `NarrativeResolver.cs:723` | `LWN_narr_event_{type}_{role}_{category}_{phase}_{grade}` | 稀疏 |
| 对抗性 Intent（Goal/Type × Success/Fail） | `InteractionController.cs:470` | `LWN_narr_intent_{goal_or_type}_{outcome}` | **注意：这些大多无 CSV 行，走的是 `GetCodeFallback()` 硬编码兜底，需要新建 key** |

> `GameDatabase.Dialogue` / `GameDatabase.CommissionNarrative` — **零引用，死数据，不迁。**

#### 来源 3：PlaceholderResolver 硬编码返回值（约 30 个）

**不在 CSV 里**。手动从 `PlaceholderResolver.ResolveOne()` 的 case 分支提取。每个 `return "中文"` → `LWN_ph_{key小写}`。

#### 来源 4：C# 内联硬编码（CrimeDialogueBuilder / PlayerDetentionBehavior 等）

Step 5 手动处理。

#### 输出

`Scripts/csv_to_xml.ps1` 读取两个 CSV → 只保留 live ID 族 → 按上述命名规则生成 `CNs/std_LivingWorldNpcs_strings.xml` + `_placeholder_manifest.csv`。来源 3/4 手动补充。

---

### Step 4：桥接 C# 代码 → XML 优先 + CSV 兜底

**修改**：`Interaction/Intents/NarrativeResolver.cs`

在 `Resolve(NarrativeFilters filters)` 里：
1. 按 filters 构造预期的 `LWN_` key（新增 `BuildLocalizationKey(filters)` 方法）
2. 尝试 `LWNTextHelper.Resolve(key, resolver)` → 命中直接返回
3. 未命中 → 走现有 CSV 逻辑（不变）

```csharp
// 构造 NarrativeFilters 对应的 localization key
private static string BuildLocalizationKey(NarrativeFilters filters)
{
    string trait = string.IsNullOrEmpty(filters.PersonalityTrait) || filters.PersonalityTrait == "Any" 
        ? "any" : filters.PersonalityTrait.ToLower();
    if (!string.IsNullOrEmpty(filters.Category))
        return $"LWN_narr_{filters.Category.ToLower()}_{filters.Phase.ToLower()}_{trait}";
    return $"LWN_narr_{filters.EventName?.ToLower()}_{filters.Outcome?.ToLower() ?? "neutral"}";
}
```

**修改**：`Interaction/Dialogue/NpcSpeechResolver.cs`

同样模式：`LWNTextHelper.Resolve($"LWN_speech_{templateId.ToLower()}", resolver)` → 命中返回 → 未命中走 CSV。

**这是非破坏性变更**——CSV 管线完整保留，XML 只是叠一层。迁移可以逐步推进。

---

### Step 5：逐步清理 C# 硬编码中文

全项目 ~128 个文件、~1 万处命中 `[一-鿿]`。**大部分是注释和调试日志，不需要本地化**。玩家可见的 UI 文本按优先级分批：

**每处替换的铁律**：上一行必须有中文注释 + fallback 用英文 + key 走 `LWN_ui_*` 或 `LWN_narr_*`。

| 批次 | 文件 | 命中数 | 文本类型 | 说明 |
|------|------|--------|----------|------|
| **0** | `PlayerDetentionBehavior.cs` | ~154 | 扣押菜单 | `{=!}赔钱了事` 等已用 `SetTextVariable`，只需把 `{=!}` 换成 `{=LWN_ui_*}` |
| **1** | `InteractionMissionView.cs` | ~414 | 交互标签、提示、搜刮对话框 | 偷窃/击晕/对话/认输/搜刮 等标签 + `InformationMessage` 提示 + `InquiryData` 文本 |
| **1** | `StealBarVM.cs` | ~198 | 偷窃条 UI | 扒窃/撬锁/抓动物的进度条文字 |
| **1** | `StealManager.cs` | ~277 | 偷窃系统提示 | `AddQuickInformation` + `InformationMessage` |
| **2** | `PlaceholderResolver.cs` | ~66 | 占位符返回值 | `ResolveOne()` 中约 30 个 `return "中文"` → `LWN_ph_*` |
| **2** | `NarrativeResolver.cs` | ~200 | 叙事兜底文本 | `GetCodeFallback` + `BuildHardcodedEventOpening` + 占位符替换 |
| **2** | `AgentBrain.cs` | ~222 | 警戒气泡/NPC 台词 | BubbleSay 文本 |
| **3** | `CrimeDialogueBuilder.cs` | ~336 | 犯罪对话节点 | 对峙/赔偿/讨价/认罪 等对话树节点内联文本 |
| **3** | `AccountabilityIntents.cs` | ~286 | 问责对话 | 指控/辩解/自首 等对话节点 |
| **4** | `InteractionController.cs` | ~398 | 对话控制器 | 对话选项/提示/系统消息 |
| **4** | `DialogueInjector.cs` | ~150 | 对话注入 | 对话树构建文本 |
| **5** | `CommissionQuest.cs` | ~370 | 委托任务 | Quest 日志/描述/进度文本 |
| **5** | `QuestManager.cs` | ~240 | 任务管理 | 任务进度/完成/失败文本 |
| **6** | `WorldEvent` 系列（`WorldEvent.cs`, `WorldEventDirector.cs`, `SocialEventManager.cs` 等）| ~1k+ | 世界事件叙事 | 事件描述/通知/传闻 |
| **7** | `NegotiationSystem.cs` | ~476 | 谈判文本 | 谈判选项/结果 |
| **7** | `DiplomacyIntents.cs` 等 Intent 文件 | ~各几十 | 意图对话 | 各类交互意图的对话文本 |
| **8** | 其余 ~100 个文件 | ~4k | 混杂 | 大部分是注释和 `DebugLogger.Log`——这些**不需要本地化**，铁律 A 脚本会豁免注释行 |

> **Phase 1 MVP 最低完成 0~3 批**（扣押/交互UI/偷窃条/PlaceholderResolver/叙事兜底/警戒气泡）——覆盖最核心的玩家可见文本路径。4~8 批可后续 PR 渐进迁移，不必一次搞完。铁律 A 脚本从第一批开始就拦截新硬编码。 **DebuggLogger.Log / 注释 / LLM prompt 不需要本地化**。

---

### Step 6：Python 铁律校验脚本（🔴 每次改代码必跑，不通过不准合入）

**新建**：`Scripts/validate_localization.py`

这是整个本地化体系的**持续质保闸门**——每次改完 C# 或 XML 后跑一次，通过再 commit。不碰 git，纯检查。

**最核心的两条检查**（零容忍，直接阻断合入）：

| 检查 | 它做什么 | 为什么关键 |
|------|---------|-----------|
| 🔴 `check_cs_no_hardcoded_cjk` | 扫所有 `*.cs`，命中非注释行内的 `[一-鿿]` 即报错 | **只要这个检查开着，就不会有新的硬编码中文漏进代码库**。从第一批迁移完成开始就终身生效。 |
| 🔴 `check_cs_placeholder_resolver_clean` | 扫 `PlaceholderResolver.cs`，`return "中文"` 即报错 | PlaceholderResolver 是占位符值计算器，不能自己拼接中文字符串。 |

其余 9 条检查详见下方清单。**全部通过才 exit 0**。

```
用法：
  python Scripts/validate_localization.py              # 全量检查
  python Scripts/validate_localization.py --cs-only    # 只查 C# 侧（铁律 A/B/G/K）
  python Scripts/validate_localization.py --xml-only   # 只查 XML 侧（铁律 C/D/E/F/H/I/J）
  python Scripts/validate_localization.py --strict     # 警告也视为错误
```

#### 检查清单

| 检查 | 对应铁律 | 严重度 | 逻辑 |
|------|----------|--------|------|
| `check_cs_no_hardcoded_cjk` | A | **ERROR** | 扫 `ExampleModVS/**/*.cs`，提取所有字符串字面量（`"..."` 和 `@"..."`），匹配 `[一-鿿぀-ゟ゠-ヿ]`。排除注释行（以 `//` 开头或在 `/* */` 中）。命中即报错 `文件:行号: 硬编码中日文文本 "xxx"`。 |
| `check_cs_lwn_comment` | B | **ERROR** | 扫 `*.cs`，正则搜索 `"LWN_[a-z0-9_]+"` 模式的字符串。对每个命中行，检查其**上一行**（跳过空行）是否包含 `[一-鿿]`。无中文注释 → 报错 `文件:行号: LWN_ key "xxx" 上一行缺中文注释`。 |
| `check_xml_key_completeness` | C | **ERROR** | 遍历 `ModuleData/Languages/*/` 下每个语言文件夹，解析其 `std_*.xml`（如 CNs 还要解析已有的 `addition_2_CNs.xml` 和 `output_strings2.xml`）。取所有 key 的并集 `K_all`。对每个语言，`K_lang = K_all` 必须成立。`K_all - K_lang` → 报错缺 key。`K_lang - K_all` → 警告（该语言多出的 key，可能是新增未同步）。 |
| `check_xml_placeholder_consistency` | D | **ERROR** | 对 `K_all` 中每个 key，提取每个语言的 `text` 中的 `{PLACEHOLDER}`（正则 `\{([A-Z][A-Z_]*[A-Z])\}`）。取集合。所有语言的集合必须相等。不等 → 报错 `key "xxx": CN 有 {A,B,C}, EN 有 {A,B}，缺 {C}`。 |
| `check_xml_placeholder_whitelist` | E | **WARNING** | 所有 XML 中出现的占位符名，必须在已知白名单中。白名单：`LOCATION, ITEM, COUNT, REWARD, TARGET, PLAYER, SPEAKER, NPC, GIVER, DEPOSIT, DAYS, PAYER, INSTIGATOR, VICTIM, StolenItemName, StolenItemDesc, WORLD, TERM_LORD, GOLD_ICON, ...`（约 80 个，从 PlaceholderResolver.ResolveOne 的所有 case 提取）。不在白名单的 → 警告 `疑似新占位符 {XXX}，请确认是否已在 PlaceholderResolver 注册`。 |
| `check_xml_key_naming` | F | **ERROR** | 所有 `<string id>` 值必须匹配 `^LWN_(narr|speech|ui|ph|sys)_[a-z0-9_]+$`。不匹配 → 报错。 |
| `check_cs_no_escape_bang` | G | **WARNING** | 扫 `*.cs`，搜索 `{=!}`。命中行与豁免列表（`PlayerDetentionBehavior.cs` 的特定行号，标记为待迁移）对比。不在豁免列表的 → 警告 `文件:行号: 新出现的 {=!}，应替换为 {=LWN_*}`。 |
| `check_xml_no_duplicate_keys` | H | **ERROR** | 解析每个 XML 时用 dict 累积，同一个 id 出现两次 → 报错。 |
| `check_language_data_refs_exist` | I | **ERROR** | 解析每个 `language_data.xml`，提取 `<LanguageFile xml_path="..."/>`。对每个 `xml_path`，检查 `ModuleData/Languages/{xml_path}` 文件是否存在。不存在 → 报错。 |
| `check_csv_xml_placeholder_parity` | J | **WARNING** | 读取 `_placeholder_manifest.csv`（如果存在），对每个 key 对比 XML 中的占位符集合。不一致 → 警告。迁移完成后此检查可移除。 |
| `check_cs_placeholder_resolver_clean` | K | **ERROR** | 扫 `PlaceholderResolver.cs`，在 `ResolveOne` 方法体内，`case "...":` 后的 `return "..."` 中如果含有 CJK 字符 → 报错。这些应该走 `LWNTextHelper.ResolveText("LWN_ph_*")`。 |

#### 豁免机制

部分检查支持行级豁免注释：

```csharp
string hardcoded = "第纳尔"; // lwn-ignore: A  -- 跳过铁律 A
string key = LWNTextHelper.Resolve("LWN_narr_xxx"); // lwn-ignore: B  -- 跳过铁律 B
```

豁免数量在执行结果中汇总报告，超过阈值（如 5 个）警告。

#### 示例输出

```
=== validate_localization.py ===
Checking 13 .cs files, 4 language folders, 6 XML files...

[PASS] A: 无硬编码中日文
[PASS] B: LWN_ key 注释齐全 (42/42)
[FAIL] C: key 不齐全
  EN/std_LivingWorldNpcs_strings.xml 缺 3 个 key:
    - LWN_narr_bounty_hunt_opening_desperate
    - LWN_narr_village_defense_closure_good
    - LWN_ph_crime_verb_gerund
[PASS] D: 占位符跨语言一致
[WARN] E: 疑似新占位符 {BOUNTY_AMOUNT} (EN, key LWN_narr_bounty_hunt_opening)
       未在白名单中，请确认 PlaceholderResolver 是否已注册
[PASS] F: Key 命名规范
[WARN] G: 新出现 {=!}: PlayerDetentionBehavior.cs:573
[PASS] H: 无重复 key
[PASS] I: language_data.xml 引用文件齐全
[PASS] J: CSV-XML 占位符一致
[PASS] K: PlaceholderResolver 无硬编码

3 ERROR, 2 WARNING — 不通过
```



---

### Step 7：验收

1. **基本功能**：中文环境启动游戏 → 对话树与迁移前完全一致
2. **CSV 兜底**：故意删某 XML key → CSV 路径自动激活，不崩
3. **脚本层校验**：`python Scripts/validate_localization.py` 全部 PASS，零 ERROR
4. **Claude 层校验**：`/check-localization` skill 跑一遍，铁律 A/B/D/L/M 无问题
5. **编译**：`dotnet build` 零 warning

---

## Phase 2：EN 语言包（`/localize` skill）

> 状态：⏳ 待执行

中文 XML 稳定后，`/localize` skill 根据中文 XML 自动生成目标语言（先出 EN）。

**为什么用 Claude Code 而非翻译 API**：游戏对话有语境——委托开场、警戒气泡、情绪台词。API 机翻只能逐句翻译，不懂 `LWN_narr_` 是委托叙事、`LWN_speech_` 是 NPC 喊话。Claude 能理解上下文，翻译质量远超机翻。

### Step 2.1：创建 EN 语言目录骨架

```xml
<!-- ModuleData/Languages/EN/language_data.xml -->
<?xml version="1.0" encoding="utf-8"?>
<LanguageData id="English" name="English" subtitle_extension="en-GB"
  supported_iso="en-GB,en-US,en,eng,..."
  text_processor="TaleWorlds.Localization.TextProcessor.LanguageProcessors.EnglishTextProcessor"
  under_development="false">
  <LanguageFile xml_path="EN/std_LivingWorldNpcs_strings.xml" />
</LanguageData>
```

> 英文需要 `text_processor`（处理复数/语法），中文不需要。

### Step 2.2：运行 `/localize --target EN`

```
1. 读 CNs/std_LivingWorldNpcs_strings.xml
  ↓
2. 按 XML 注释段落理解语境（Narrative / NpcSpeech / UI / Placeholder 分类）
  ↓
3. 逐条翻译
   - 保持 {PLACEHOLDER} 原封不动
   - 语序适配英文语法
   - 中文（括号动作描述）→ *action* 斜体
   - …… → ...
   - 第纳尔 → denars
  ↓
4. 输出 EN/std_LivingWorldNpcs_strings.xml
  ↓
5. 自动跑 validate_localization.py（铁律 C/D/E/F/H/I）
  ↓
6. 全部通过 → 完成，提示抽查 10%
   失败 → 标出问题条目，循环修复
```

### Step 2.3：验收

1. `python Scripts/validate_localization.py` 全 PASS
2. 抽查 10% 翻译质量
3. `dotnet build` 零 warning

---

## Phase 3：收尾

> 状态：⏳ 待执行

### Step 3.1：补 273 个缺失的中文注释

用脚本批量扫描 `LWN_` key 行 → 上一行无 CJK 注释 → 自动插入注释（key 名本身可读性强，可用 key 名生成注释）。

### Step 3.2：KNOWN_PLACEHOLDERS 白名单更新

各系统引入了新占位符（`TITLE`, `AMOUNT`, `GRADE`, `EXTRA`, `TRUSTDELTA` 等 ~30 个），`validate_localization.py` check E 会对未知占位符 warn。需要从 XML 中提取全部占位符并加入白名单。

### Step 3.3：clean 最后 13 个 CJK 残留

| 文件 | 行 | 实际情况 |
|------|-----|---------|
| AtomicAction.cs | 1031 | debug 输出 |
| AgentBrain.cs | 1012 | debug 输出 |
| CrimeDialogueBuilder.cs | 85 | 内部字符串 |
| DialogueInjector.cs | 301, 303 | debug |
| IntentRegistry.cs | 174, 175, 211 | debug |
| InteractionMissionView.cs | 331, 337 | 注释块内 |
| StealBarVM.cs | 71, 473, 475 | 注释/debug |

全部都是非玩家可见的 debug/注释文本，不影响多语言。可加 `lwn-ignore: A` 豁免注释消除 validator ERROR，或直接不改。

---

## 关键设计决策
```

---

## 关键设计决策

### 1. CSV 不删，XML 叠在上面
零风险渐进迁移。XML 稳定后，未来版本可删除 CSV 兜底。

### 2. Narrative + NpcSpeech 合并一个 XML
NpcSpeech 是早期分表思路，XML 按 key 查找不需要分表。Key 前缀自然区分语义。

### 3. 先出中文，再用 Claude Code 生成其他语言
Phase 1 只产出中文 XML + C# 桥接 + 脚本校验。Phase 2 用 `/localize` skill（Claude Code）翻译 + `/check-localization` skill（Claude Code）校验语义。两层防线：脚本查机械错误，Claude 查语义问题。

### 4. Emotion 数据不动
`Emotion` 列是游戏逻辑元数据，不是显示文本，不需要本地化。

### 5. `{=key}fallback` 兜底，**fallback 默认英文**

代码里：`new TextObject("{=LWN_KEY}English fallback text")`。引擎先查 XML → 命中用对应语言的翻译 → 未命中（语言包缺失/不完整）用英文兜底。中文翻译在 `CNs/std_LivingWorldNpcs_strings.xml` 中定义。

**为什么 fallback 用英文而非中文**：mod 面向全球发布，英文是最大公约数。某语言包缺失时，玩家看到英文至少可读，看到中文就是乱码。

---

## 涉及文件总览

| 动作 | 文件 | 说明 | 状态 |
|------|------|------|:---:|
| **CREATE** | `Localization/LWNTextHelper.cs` | TextObject + PlaceholderResolver 桥接 | ✅ |
| **CREATE** | `ModuleData/Languages/CNs/std_LivingWorldNpcs_strings.xml` | 中文：Narrative+NpcSpeech+UI+WorldEvent+Quest 全部合并，~2200 条目 | ✅ |
| **CREATE** | `ModuleData/Languages/EN/language_data.xml` | 英文语言清单 | ⏳ Phase 2 |
| **CREATE** | `ModuleData/Languages/EN/std_LivingWorldNpcs_strings.xml` | 英文翻译 | ⏳ Phase 2 |
| **CREATE** | `Scripts/csv_to_xml.py` | 一次性 CSV→XML 提取 | ✅ |
| **CREATE** | `Scripts/validate_localization.py` | 11 项铁律自检 | ✅ |
| **MODIFY** | `Core/MySubModule.cs` | 加 `LoadLocalizationXmls` 调用 | ✅ |
| **MODIFY** | `ModuleData/Languages/CNs/language_data.xml` | 加 include + 修正 tag 一致性 | ✅ |
| **MODIFY** | `ModuleData/Languages/CNs/addition_2_CNs.xml` | tag `Chinese` → `简体中文` | ✅ |
| **MODIFY** | `Interaction/Intents/NarrativeResolver.cs` | XML 优先 + CSV 兜底 + TryResolveFromXml | ✅ |
| **MODIFY** | `Interaction/Dialogue/NpcSpeechResolver.cs` | XML 优先 + CSV 兜底 | ✅ |
| **MODIFY** | `Interaction/Dialogue/PlaceholderResolver.cs` | ResolveOne: 硬编码中文 → LWN_ph_* key | ✅ |
| **MODIFY** | `WorldEvent/PlayerDetentionBehavior.cs` | `{=!}` → `{=LWN_ui_*}` | ✅ |
| **MODIFY** | ~80 个 C# 文件 | 全部玩家可见文本迁移到 XML | ✅ |

---

## 🔴 计划自检清单（每次 ExitPlanMode 前必过）

| # | 检查项 | |
|---|--------|---|
| 1 | 计划中所有 LWN_ key 示例符合 `^LWN_(narr\|speech\|ui\|ph\|sys)_[a-z0-9_]+$`？ | |
| 2 | 计划中所有 fallback 示例是英文（不是中文）？ | |
| 3 | 计划中所有 `{=!}` 出现都标注了"待迁移"或"旧代码"？ | |
| 4 | 计划中无新的硬编码中文 C# 示例（迁移前的旧代码示例除外）？ | |
| 5 | 有硬编码中文的 C# 文件都在 Step 5 批次表里有覆盖？ | |
| 6 | `validate_localization.py` 覆盖了铁律 A~K 的可程序化检查？ | |
| 7 | 拼接修复三种模式都有代码示例？ | |
| 8 | Step 0 `LoadLocalizationXmls` 调用没漏？ | |
| 9 | 涉及文件总览表与实施步骤一致？ | |
| 10 | 新 session 从读这个文件开始就能执行？ | |

**任一未通过 → 修计划 → 重检 → 全部通过再结束任务。**

---

## 🔴 EN 语言包不生效 — 待排查

### 现象
- CNs XML 在简体中文下正常加载，mod 中文文本正确显示
- EN XML 已放入 `ModuleData/Languages/EN/std_LivingWorldNpcs_strings.xml`（2218 条目，0 BMP+ 字符）
- `EN/language_data.xml` 已创建，引用 `EN/std_LivingWorldNpcs_strings.xml`
- **但切换到 English 后，mod 文本仍显示为 C# fallback，未加载 EN XML 翻译**

### 已尝试
1. `EN/language_data.xml` + `EN/std_LivingWorldNpcs_strings.xml` → ❌ 不生效
2. 把 en xml 直接放 `Languages/` 根目录并修改路径引用 → ❌ 不生效
3. `LoadLocalizationXmls` 调用 → ❌ 反而导致全局语言系统崩溃（语言选项只剩简体中文）

### 待排查方向
- Bannerlord 引擎到底如何加载子模块（非 Native）的 `Languages/EN/`？
- Native 模块没有 `EN/` 子目录，英文是引擎内置 fallback。子模块是否需要特殊处理才能注册新语言？
- 是否需要在 `SubModule.xml` 或其他地方声明语言支持？
- 检查其他有 EN 翻译的 mod（如 RBM、Diplomacy 等）是怎么组织的
- 反编译 `LocalizedTextManager` 看 `LoadLocalizationXmls` 和自动扫描的具体逻辑

### 相关铁律（已写入 CLAUDE.md）
- 铁律 13：所有玩家可见文本走 `LWNTextHelper` → `{=LWN_KEY}fallback`
- 铁律 14：语言 XML 禁止 emoji/BMP+ 字符（UTF-16 代理对崩溃）
- 铁律 15：禁止手动调 `LoadLocalizationXmls`（干扰全局语言注册）
