# 防御性编码：LLM 响应处理

## 核心原则

LLM 返回的 JSON **不可靠**——可能缺失字段、格式错误、被截断、或为空。所有对 LLM 响应数据的访问必须有防御性检查。

## 铁律

### 1. foreach 遍历 LLM 集合前必须 null check

```csharp
// ❌ 危险 — PlayerNextOptions 可能是 null
foreach (var opt in openingData.PlayerNextOptions) { ... }

// ✅ 安全
if (openingData.PlayerNextOptions == null) return;
foreach (var opt in openingData.PlayerNextOptions) { ... }

// ✅ 更安全 — 空集合也能正常走
if (openingData.PlayerNextOptions == null || openingData.PlayerNextOptions.Count == 0)
{
    // 显示兜底选项，防止 UI 卡死
    ShowFallbackOptions();
    return;
}
```

### 2. Fallback JSON 的 key 必须与 [JsonProperty] 严格一致

```csharp
// ❌ 错误 — JSON key "options" 不匹配 C# 属性 [JsonProperty("player_next_options")]
"{ \"npc_reply\": \"...\", \"options\": [] }"

// ✅ 正确
"{ \"npc_reply\": \"...\", \"player_next_options\": [] }"
```

JSON 反序列化器（Newtonsoft.Json）按 `[JsonProperty]` 属性名严格匹配，不会做模糊匹配。

### 3. 反序列化本身也要 try-catch

```csharp
try
{
    var data = JsonConvert.DeserializeObject<LLMResponse_Opening>(jsonResponse);
    memory.CurrentInitiative.CachedOpening = data;
}
catch
{
    memory.CurrentInitiative.CachedOpening = null;  // 上游的 null check 会处理
}
```

### 4. LLM 调用必须 try-catch，且 catch 里给可用降级

```csharp
try
{
    string jsonResponse = await LLMService.Instance.ChatAsync(prompt, 500, true);
    // ...
}
catch (Exception ex)
{
    // 必须给能用的降级数据，不能让后续代码崩
    memory.JsonResponse = "{ \"npc_reply\": \"(沉默)\", \"player_next_options\": [] }";
}
```

## 常见崩溃模式

| 崩溃 | 根因 | 修复 |
|------|------|------|
| `NullReferenceException` on `foreach` LLM list | JSON 缺失该字段 | 加 null check |
| `NullReferenceException` on `.Name.ToString()` | `RelatedSkill` 反序列化为 null | 加 null 传播 `?.` |
| UI 卡死无选项 | LLM 返回空数组但没给 fallback | 空数组时显示兜底选项 |
| JSON 解析异常 | LLM 返回了 markdown 包裹的 JSON | 调用 `LLMService.CleanJson()` 清洗 |
