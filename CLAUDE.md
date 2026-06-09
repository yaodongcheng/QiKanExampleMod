# LivingWorldNpcs — 项目规则

详细规则见 `plans/rules/`，每次会话开始时按需加载：

| 规则文件 | 主题 |
|----------|------|
| [llm-optional.md](plans/rules/llm-optional.md) | **LLM 是可选功能**，IsLLMReady 总闸，所有入口点必须检查 |
| [worldview.md](plans/rules/worldview.md) | **禁止硬编码日本战国字串**，世界观通过 Settings.Instance 参数化 |
| [defensive-coding.md](plans/rules/defensive-coding.md) | **LLM JSON 响应必须 null-guard**，JSON key 必须匹配 [JsonProperty] |
| [architecture.md](plans/rules/architecture.md) | Namespace (`LivingWorldNpcs.*`)、目录结构、Mod A/B 拆分 |

## 三条铁律

1. **LLM 不可用时游戏不能崩** — 任何 LLM 代码路径入口检查 `Settings.Instance.IsLLMReady`，不存在就降级或 return
2. **LLM 返回的 JSON 不可信任** — 每个 `foreach` 前 null check，每个字段用 `?.` 传播
3. **LivingWorldNpcs 是通用 mod** — 代码里不能出现 `Shokuho`/`日本战国`/`太阁`/`织丰` 等字串

## 拆分架构

- **LivingWorldNpcs**（本 mod）= 通用玩法引擎，卡拉迪亚世界观
- **TaikouContent**（Mod B）= 纯内容包，往 Settings.Instance 注入日本战国 flavor
- 完整计划：`plans/ai-2mod-2-zippy-puppy.md`
