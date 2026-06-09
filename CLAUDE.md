# LivingWorldNpcs — 项目规则

> **会话必读（写任何代码前先做）：完整读一遍 [plans/rules/wheels.md](plans/rules/wheels.md)。**
> 这是已造轮子速查，避免重复造轮子 / 绕过既有引擎。**不读 wheels.md 不准动手写新功能。**

详细规则见 `plans/rules/`。**wheels.md 每次会话必读**，其余按需加载：

| 规则文件 | 主题 |
|----------|------|
| [wheels.md](plans/rules/wheels.md) | 🔴**【必读】已造轮子速查**，加功能前先查再写，命中即复用 |
| [llm-optional.md](plans/rules/llm-optional.md) | **LLM 是可选功能**，IsLLMReady 总闸，所有入口点必须检查 |
| [worldview.md](plans/rules/worldview.md) | **禁止硬编码日本战国字串**，世界观通过 Settings.Instance 参数化 |
| [defensive-coding.md](plans/rules/defensive-coding.md) | **LLM JSON 响应必须 null-guard**，JSON key 必须匹配 [JsonProperty] |
| [architecture.md](plans/rules/architecture.md) | Namespace (`LivingWorldNpcs.*`)、目录结构、Mod A/B 拆分 |
| [coding-style.md](plans/rules/coding-style.md) | 命名/单例/异步/异常/ViewModel 绑定 等编码约定 |
| [tech-debt.md](plans/rules/tech-debt.md) | 架构待调整清单（硬编码泄漏、守卫缺失、巨型文件） |

## 四条铁律

1. **LLM 不可用时游戏不能崩** — 任何 LLM 代码路径入口检查 `Settings.Instance.IsLLMReady`，不存在就降级或 return
2. **LLM 返回的 JSON 不可信任** — 每个 `foreach` 前 null check，每个字段用 `?.` 传播
3. **LivingWorldNpcs 是通用 mod** — 代码里不能出现 `Shokuho`/`日本战国`/`太阁`/`织丰` 等字串
4. **资源进出统一归口、禁止半截操作** — 凡「看上去像资源进出」的地方都走 `AgentControlHelper`（**金钱 = 特殊物品**，Item==null），禁止业务层裸调 `Hero.ChangeHeroGold` / `ItemRoster.AddToCounts` 等单边 API。三类操作各有纪律：①**转移 Transfer**（贿赂/罚款/赏赐/买卖）守恒，一方扣一方加，**禁止只做半截**（钱扣了没人收）；②**收发 Grant/Sink**（战利品/凭空奖励/消耗）单边对接「世界」，用 `null` 显式标注虚空来源/去向，**合法非违规**；③**转换 Convert**（冶炼/工坊/吃苹果回饱腹）按配方刻意非守恒，但必须**守卫 + 原子**（输入不足则整体不发生）。

## 工作流约定

**每完成一个功能后，必须主动询问用户：是否要把本次产出提炼成新的轮子并登记进 [wheels.md](plans/rules/wheels.md)。**

- 判断标准：本次是否产生了可复用的基础设施、新的引擎扩展点、或值得固化的模式。
- 若用户同意 → 在 wheels.md 增补条目（解决什么问题 + 关键签名 + 调用范例 + 文件路径），与现有格式一致。
- 即使本次只是用了已有轮子、没产出新轮子，也简短说明一句"无新轮子"，不要跳过这一步。

## 拆分架构

- **LivingWorldNpcs**（本 mod）= 通用玩法引擎，卡拉迪亚世界观
- **TaikouContent**（Mod B）= 纯内容包，往 Settings.Instance 注入日本战国 flavor
- 完整计划：`plans/ai-2mod-2-zippy-puppy.md`
