# Ollama / 自定义 OpenAI 兼容端点接入 — 实测验证

> 日期: 2026-08-08
> 相关文件: `LLM/LLMService.cs`（客户端）, `Core/Settings.cs` + `Core/MCMSettings.cs`（LLM 三字段唯一来源 = MCM）, `Scripts/test_llm_plan.py`（py 回归）
> 触发: 玩家问"本地 Ollama 部署能不能用"，实测环境: Ollama 0.32.6 + qwen2.5:3b / qwen3:4b / qwen2.5:7b（RTX 4060 Ti 8G, i7-13700K, 64G 内存）

---

## 一、结论：代码零改动，协议层完全兼容 ✅

客户端是 **OpenAI 兼容格式**（`POST {base}/chat/completions` + `Bearer` 头 + `max_tokens/temperature/response_format/reasoning_effort`），Ollama 的 OpenAI 兼容端点（`/v1/chat/completions`）逐字段接受：

| 我们发送的字段 | Ollama 行为 |
|---|---|
| `model` | 必须 = `ollama list` 的完整名字（如 `qwen2.5:3b`） |
| `messages`（system/user 角色） | ✅ |
| `temperature` / `max_tokens` | ✅（max_tokens 映射 num_predict） |
| `response_format: {type:"json_object"}` | ✅ 支持（映射 format=json） |
| `reasoning_effort` | ⚪ 忽略但不报错（本地模型无思考模式概念，qwen3 的思考靠模板控制） |
| `Authorization: Bearer <任意>` | ⚪ 完全忽略，不校验 |

**所有请求形状实测 200**：TestConnection（1 token ping）、ChatAsync（json_object + reasoning_effort）、SummarizeAsync（裸形状）、玩家对话六字段 JSON（0.9s 返回、schema 完美解析）、py 回归整链路（请求→解析→validator 全通）。

## 二、⚠️ 玩家配置与常见教程（Reddit 等）的差异——不纠正必挂

| 项 | Reddit 教程写法 | 我们的正确写法 | 原因 |
|---|---|---|---|
| Base URL | `http://127.0.0.1:11434` | **`http://127.0.0.1:11434/v1`** | 我们拼接后缀后是 `/chat/completions`；Ollama 的 OpenAI 兼容端点带 `/v1` 前缀。裸根路径实测 **404**（`/api/chat` 是原生端点，我们不用） |
| API Key | 留空 | **填任意占位符**（如 `ollama`） | `IsLLMConfigured` 三字段非空门控（铁律 1），空 key 直接拒绝请求。Ollama 忽略 key 内容，实测 `Bearer ollama-dummy` 通过 |
| Model | `qwen2.5:3b` | 同左（`ollama list` 的完整 StringId） | — |

配置后游戏内 MCM「测试连接」按钮即可验证（走 TestConnection 同步路径，同样 200）。

## 三、质量观察：小模型在「计划生成」上的能力边界（2026-08-08 实测）

**密谋计划生成 prompt ≈ 1 万字符封闭词表任务**（18 条纪律 + 意图表 + 示范模板 + 质量要求）。本地模型回归结果：

| 模型 | 计划回归分类正确率 | 症状 |
|---|---|---|
| qwen2.5:3b | 16% | 全部输出锚定 BRING 示范（"我去请村长过来见你"） |
| qwen3:4b | 16% | 同上 |
| qwen2.5:7b | 8% | 全部锚定 DISTRACT 示范（"我去引开敌人"） |
| deepseek-v4-flash（云端 API） | 91%（py 基线） | — |

**规律**：3b~7b 模型会把示范/示例**逐字复读**，不按命令分类——这是模型能力问题，**不是兼容问题**（同一 prompt 云端大模型 91%）。方案：

- **玩家侧**：计划生成玩法建议 ≥14b 开源模型（`deepseek-r1:14b` ≈9GB、`qwen3:14b`）或继续用云端 API；**对话类路径**（LLMResponse/SceneConflict）3b 就完美（0.9s、schema 稳定）。
- **产品侧**（未做，待定）：「本地轻量模式」——计划 prompt 去示范/压缩纪律，小模型有望胜任。A/B 思路已验证可行方向：短 prompt 时 7b 能正确分类。
- **注**：`deepseek-v4-flash` 是雷火网关的 API 服务名，**不是**开源模型；本地 DeepSeek 系只有 `deepseek-r1` 蒸馏版。

## 四、延迟预算注意

- 模型**冷启动**（首次请求载入显存）~28s，之后单次请求：短回复 0.1~1.4s，436 字中文回复 2.4s。
- `ChatOnceAsync`（ReactiveAgent 实时回应）预算 **2s**——本地模型长回复会超时降级为模板台词。4060 Ti 8G 上 3b 短回复可过，长回复需更大显存或调参（未改）。

## 五、部署速查（Windows）

```bash
winget install --id Ollama.Ollama -e --source winget   # 安装（注册系统服务，开机自启）
ollama pull qwen2.5:3b                                 # 拉模型（~1.9GB；3b=1.9G, 7b=4.7G, r1:14b≈9G）
ollama list                                            # 查模型完整名 → 填进 MCM
# 服务默认 127.0.0.1:11434；OpenAI 兼容端点为 /v1/chat/completions，原生端点为 /api/chat、/api/generate
```

## 六、参考

- 客户端唯一事实源：`LLM/LLMService.cs`（`ApiUrl` = base + `/chat/completions`，key 每请求现读，见 wheels.d/llm.md）
- 配置唯一来源 MCM：`LLMBaseUrl / LLMApiKey / LLMModel`（`Settings.IsLLMConfigured` 三字段非空）
- py 回归：`Scripts/test_llm_plan.py`（读同一 MCM 配置文件）
