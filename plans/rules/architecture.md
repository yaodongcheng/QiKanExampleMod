# 架构与命名

## Mod 拆分

- **Mod A: LivingWorldNpcs** — 通用玩法引擎（所有 .cs + GUI prefab），卡拉迪亚世界观
- **Mod B: TaikouContent** — 纯内容包（Shokuho XML + StoryJson + 约30行 DLL），`<DependedModule Id="LivingWorldNpcs"/>`

Mod B 只做一件事：在 `OnSubModuleLoad` 中覆盖 `Settings.Instance` 的世界观字段，不包含任何游戏逻辑。

## Namespace

全部使用 `LivingWorldNpcs.*`，**不使用** `ExampleMod.*`。

## 日志前缀

```csharp
// ✅ 正确
$"[LivingWorldNpcs] ..."

// ❌ 错误（已废弃）
$"[ShokuhoMod] ..."
$"[Shokuho] ..."
```

## 目录结构

```
ExampleModVS/ExampleMod/ExampleMod/
├── Core/         Settings.cs, MySubModule.cs, ...
├── Interaction/  对话系统（InteractionController, InteractionMissionView, StoryDialogVM...）
├── Stealth/      偷窃系统
├── Combat/       战斗触发、决斗
├── AI/           NPC AI 控制器、行为
├── Bubble/       气泡对话
├── Notify/       通知系统
├── Camera/       镜头控制
├── LLM/          LLMService, PromptBuilder
├── Memory/       NPC 记忆系统（拆分为 7 文件）
├── Negotiation/  谈判系统
├── Social/       社交事件
├── Story/        剧情演出引擎（纯引擎）
├── Script/       JSON 脚本加载器
├── Quest/        任务系统
├── Spawner/      NPC 生成
├── Data/         设计数据加载
├── Debug/        调试工具
└── Properties/   AssemblyInfo.cs
```

## 验证

```bash
# namespace 检查
grep -r "namespace ExampleMod" --include="*.cs" .
# 应该为空

# 日志前缀检查
grep -r "\[Shokuho" --include="*.cs" .
# 应该为空
```
