# BannerlordTalk 逆向参考（反编译快照）

> 用途：`Knowledge/BannerlordTalk_技术实现分析.md` 的事实来源 + 版本更新时的 diff 基线。
> **模组包目录会被玩家整体替换**（`Modules/OtherMods/BannerlordTalk-*/`），因此反编译产物放在这里（`Knowledge/BannerlordTalk_逆向/`），不随模组包走。

## 目录结构

```
Knowledge/BannerlordTalk_逆向/
├── README.md                  ← 本文档（更新流程）
├── redecompile_diff.sh        ← 重新反编译 + diff 一键脚本
├── v1.0.0/                    ← v1.0.0（BL1.4.8 包）的快照
│   ├── type_list.txt          ← 全类型清单（ilspycmd -l c）
│   └── *.decompiled.cs        ← 11 个关键类型（见下）
└── v1.0.3/                    ← v1.0.3（BL1.4.8 包）的快照（2026-08-19）
    ├── type_list.txt
    └── *.decompiled.cs        ← 11 个关键类型 + ChatterManagerVM + ManagerTextPreviewPolicy
```

## 关键类型清单

| 文件 | 分析文档对应章节 |
|------|----------------|
| PromptBuilder | §2.1 双层 Prompt 组装 + Presentation 合同 |
| UserPromptBudget | §2.1 尾部保底（Compose） |
| ResponseParser | §2.1 严格 JSON 合同 |
| CampaignEventMemoryService | §2.3 世界事件记忆 |
| NativeHeroContextProvider | §2.2 LiveFacts 带化量纲 |
| ChatterManagerDataSource | 管理面板 CRUD（人格/记忆/常识/提示词） |
| CampaignChatterBehavior | §2.6 调度与会话状态机（3391 行，主行为类；v1.0.1 起含 Gemini 3.7 ReasoningEffort 供应商分支） |
| StandaloneKnowledgeRetriever | §2.4 常识库检索（BM25/CJK 切词/链式/变体） |
| TtsPlaybackService / FishTtsOptions / TtsTextComposer | §2.7 TTS 实现 |
| ChatterManagerVM | §2.9 管理面板 VM（v1.0.2 有界预览 + v1.0.3 常识页摘要/布局节流；v1.0.0 快照未收录，基线取游戏已安装 DLL） |
| ManagerTextPreviewPolicy | §2.9 长文本 UI 纪律（v1.0.3 新增类型） |
| ChatterOverlayVM / ChatterOverlay | §2.10 闲聊窗 UI 设计（双高度收起模型 + 命中门控 + 可见性三态叠加 + 缩放/锚点/响应式；v1.0.3 快照补充收录） |

## 更新流程（用户替换新版本模组包后）

```bash
# 1. 用户把新版本包放进 Modules/OtherMods/BannerlordTalk-<新版本>/
# 2. 按 README（Knowledge/BannerlordTalk_逆向/README.md）执行：
./redecompile_diff.sh "<新版本目录>/BannerlordTalk/bin/Win64_Shipping_Client/BannerlordTalk.dll" "<版本号>"
#    → 输出 v1.1.0/ 快照 + 与 v1.0.0 的 diff 摘要
# 3. 依据 diff 更新 Knowledge/BannerlordTalk_技术实现分析.md：
#    - 行为变化 → 更新对应章节（注意：函数名/常量若变，全文档引用要同步 grep）
#    - 新增系统 → 新增章节
#    - 版本号 + 日期 → 更新文头
# 4. 若出现新的可借鉴轮子 → 按工作流约定登记 wheels.d/
```

## 重反编译命令（单类型）

```bash
# 从模组包 DLL 反编译单个类型到快照目录：
ilspycmd "<包>/BannerlordTalk/bin/Win64_Shipping_Client/BannerlordTalk.dll" \
  -t "BannerlordTalk.Runtime.PromptBuilder" -o "Knowledge/BannerlordTalk_逆向/<版本>/"
```

> ilspycmd 输出带中文，`-o <目录>` 会把每个类型写成 `<类型名>.decompiled.cs`（UTF-8，Read 工具可正常读）。
> 反编译产物仅用于本仓库内部对照分析，不随模组分发。
