# 世界观参数化

## 核心原则

LivingWorldNpcs 是**通用 mod**（卡拉迪亚中世纪世界观）。**禁止**在代码中硬编码任何日本战国/织丰题材字串。

Mod B（TaikouContent）通过覆盖 `Settings.Instance` 的字段来注入日本战国世界观，不需要改 LivingWorldNpcs 一行代码。

## 禁止出现的字串

以下字串**不允许**出现在 LivingWorldNpcs 的任何 `.cs` 文件、注释、或 JSON 配置中：

- `Shokuho`、`Taikou`、`织丰`
- `日本战国`、`大河剧`
- `妾身`、`在下`、`主公`、`混账`
- `太阁`

## 正确做法：通过 Settings.Instance 引用

```csharp
private static Settings S => Settings.Instance;

// ✅ 正确
string world = S.WorldDescription;       // 默认: "骑马与砍杀2 卡拉迪亚中世纪世界"
string era = S.EraDescription;           // 默认: "中世纪卡拉迪亚大陆"
string style = S.SpeechStyle;            // 默认: 中性中世纪口语
string terms = S.WarriorTerms;           // 默认: "大人"、"爵士"
string female = S.FemaleSelfAddress;     // 默认: ""

// ❌ 错误
string world = "日本战国时代";           // 硬编码，破坏通用性
string prompt = "口吻符合日本战国背景";   // 同上
```

## Settings 字段速查

| 字段 | 卡拉迪亚默认 | Mod B 注入（日本战国） |
|------|-------------|---------------------|
| `WorldDescription` | "骑马与砍杀2 卡拉迪亚中世纪世界" | "骑马与砍杀2织丰Mod塑造的日本战国世界" |
| `EraDescription` | "中世纪卡拉迪亚大陆" | "日本战国时代" |
| `SpeechStyle` | 中性中世纪口语 | 大河剧风格 |
| `WarriorTerms` | "大人"、"爵士" | "在下"、"主公"、"混账" |
| `FemaleSelfAddress` | ""（空） | "妾身" |

## 验证

```bash
grep -ri "Shokuho\|织丰\|日本战国\|太阁\|大河剧\|妾身\|在下.*主公\|混账" --include="*.cs" .
```
结果必须为空。
