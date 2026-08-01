# 发布前清理计划

> **目的**：删除开发过程积累的废弃文件、测试数据、重复文件，确保对外发布时仓库干净。
> **原则**：LivingWorldNpcs C# 代码没引用的就删。TaikouContent 需要的让它自己管。
>
> **用户已确认**：全部待确认项已确认，一次性全清。

---

## 一、GUI Prefabs — 删除 13 个废弃备份/临时文件

所有 GUI prefab 加载方式为 `LoadMovie("PrefabName", ...)` 不带 `.xml` 后缀。
已全文搜索 `.cs`，以下 13 个文件名**零引用**：

| 文件 | 性质 |
|------|------|
| `GUI/Prefabs/DialogChoice -左上角备份.xml` | 备份文件 |
| `GUI/Prefabs/新文件 1.xml` | 临时文件（VS 新建 XML 默认名） |
| `GUI/Prefabs/DialogChoice - 跑通了选项和属性显示.xml` | 开发里程碑备份 |
| `GUI/Prefabs/DialogSay - 废弃.xml` | 标废弃，"DialogSay" 代码中是方法名，无 LoadMovie |
| `GUI/Prefabs/MyCustomPopup - 标题-正文-左确定右取消.xml` | 旧版本 |
| `GUI/Prefabs/MyCustomPopup - 简单的标题-内容-一个按钮.xml` | 旧版本 |
| `GUI/Prefabs/MyCustomPopup-相机调试.xml` | 调试用 |
| `GUI/Prefabs/SpringArmCameraDebugger - 新版相机.xml` | 旧版本 |
| `GUI/Prefabs/InteractArea - 右侧交互选项-正式版.xml` | 旧版本 |
| `GUI/Prefabs/NPCInfoBoard - 副本.xml` | 副本（Windows 复制） |
| `GUI/Prefabs/MyCustomPopup - 偷窃临时.xml` | 临时文件 |
| `GUI/Prefabs/SceneSay.xml` | 无 LoadMovie 引用 |
| `GUI/Prefabs/SceneSay - 正式版.xml` | 旧版本，无 LoadMovie 引用 |

**保留的 10 个活跃 prefab**：DialogChoice, MyCustomPopup, SpringArmCameraDebugger, CameraDebugger, InteractArea, NPCInfoBoard, AgentHudNearby, CustomNotify, StealBar, DuelUI

---

## 二、GUI SpriteParts — 删除立绘资源（6 个）

C# 代码零引用（`MyCustomUIVM.cs` 有注释掉的 `H:\\taikou.png`，文件本身也要删）。

| 文件 | 性质 |
|------|------|
| `GUI/SpriteParts/ui_character_illustration.xml` | sprite 定义 |
| `GUI/SpriteParts/ui_character_illustration/taikou.png` | 织田信长立绘 |
| `GUI/SpriteParts/ui_character_illustration/taikou-oda.png` | 同上 |
| `GUI/SpriteParts/ui_character_illustration/taikou_oda2.png` | 同上 |
| `GUI/SpriteParts/ui_character_illustration/taikou_oda3.png` | 同上 |
| `GUI/ui_character_illustration.xml` | 与 SpriteParts 下重复 |

**同步操作**：`SubModule.xml:27-29` 删除对应的 `<XmlNode>`（`SpriteData` → `GUI/SpriteParts/ui_character_illustration`）。

---

## 三、ModuleData/Native/ — 删除全部 4 个测试数据

> 🔴 这 4 个文件通过 `SubModule.xml` 注册，每次启动游戏注入幽灵测试数据。必须同步删注册。

### 3.1 删除文件

| 文件 | SubModule.xml 行号 | 内容 |
|------|-------------------|------|
| `ModuleData/Native/clans.xml` | 30-38 | 测试 clan "My Test Clan" |
| `ModuleData/Native/heroes.xml` | 57-65 | 测试 hero `lord_maedakeiji` |
| `ModuleData/Native/lords.xml` | 48-56 | 测试 NPC "Maeda KeijiSon" |
| `ModuleData/Native/artisan_beer.xml` | 39-47 | 测试物品 `artisan_beer` |

### 3.2 同步清理

| 文件 | 操作 | 行号 |
|------|------|------|
| `SubModule.xml` | 删除 5 个 `<XmlNode>` 块（4 个 Native + 1 个 SpriteData） | 27-65 |
| `Core/MySubModule.cs` | 删除 `mission.AddMissionBehavior(new ArtisanBeerMissionView());` | 89 |
| `Combat/ArtisanBeerMissionView.cs` | **删除整个文件** | — |
| `Debug/DebugBehavior.cs` | **删除整个文件**（死代码，硬编码了 lords.xml 路径，无注册无调用） | — |

---

## 四、调试 UI 系统 — 删除 MyCustomUIVM.cs + 清理 MySubModule.cs

### 4.1 删除文件

| 文件 | 说明 |
|------|------|
| `Debug/MyCustomUIVM.cs` | F9 调试 UI 的 ViewModel，含 `H:\\taikou.png` 硬编码路径 |

### 4.2 MySubModule.cs 清理（4 处）

| 代码 | 行号 | 操作 |
|------|------|------|
| F9 按键处理 `if (Input.IsKeyPressed(InputKey.F9)) { … }` | 264-284 | 删除整个 if-else 块 |
| `private GauntletLayer myLayer;` | 353 | 删除 |
| `private MyCustomUIVM myVM;` | 354 | 删除 |
| `#if … private GauntletMovieIdentifier myMovie; … #endif` | 355-359 | 删除 |
| `private void OpenMyScreen() { … }` | 361-370 | 删除整个方法 |
| `private void CloseMyScreen() { … }` | 371-384 | 删除整个方法 |

---

## 五、test_talk.json — 删除测试对话

- C# 引用：仅 `Debug/MyCommands.cs` 的 xmldoc 注释中作为示例
- 加载：`custom.inject_dialogue test_talk` 开发者控制台命令
- **无生产代码自动加载**

**操作**：删除 `ModuleData/DesignData/Dialogues/test_talk.json`，更新 `MyCommands.cs` 中的 xmldoc 注释。

---

## 六、语言文件 — 删除 addition_2_CNs.xml + output_strings2.xml

### 原则：LivingWorldNpcs C# 代码零引用，删。TaikouContent 需要的字符串让它自己管。

| 文件 | 大小 | 内容 | C# 引用 |
|------|------|------|----------|
| `addition_2_CNs.xml` | 16 行 | 前田庆次/火影忍者/海贼王/无之国/中立家族/忍巾 | **零** |
| `output_strings2.xml` | 1796 行 | ~600 战国家族名 + ~950 战国人物名 | **零**（`my_clan_*`/`my_lord_*` 无 `.cs` 引用） |

> TaikouContent 的 `lords.xml`/`heroes.xml`/`clans.xml` 使用 `{=my_lord_1_oda}fallback` 引用这些 string ID，但 TaikouContent 的 `DesignData/output_strings2.xml` 已有相同内容，自行注册语言文件即可。

### 同步操作

1. 删除两个 XML 文件
2. `ModuleData/Languages/CNs/language_data.xml` 删除以下两行，只保留 std_ 文件：
   ```xml
   <LanguageFile xml_path="CNs/addition_2_CNs.xml" />
   <LanguageFile xml_path="CNs/output_strings2.xml" />
   ```
3. `CNs/language_data.xml` 从 **UTF-16 LE 转 UTF-8**（当前 Read 工具读出来是乱码，铁律 14 相关）

---

## 七、GUI/Brushes/MyBrush.xml — 清理废弃笔刷

以下笔刷在 C# 和活跃 prefab 中零引用（仅被已删废弃 prefab 引用）：

| 笔刷名 | 引用情况 |
|--------|----------|
| `MyBrush_18` | 仅在废弃 prefab |
| `MyBrush_18_White` | 仅在废弃 prefab |
| `MyBrush_24` | 仅在废弃 prefab |
| `MyBrush_16_Left` | 零引用 |
| `MyBrush_22_Left` | 零引用 |
| `Test.Button1` | 仅在废弃 prefab |
| `Test.Button2` | 仅在废弃 prefab |
| `Test.Button3` | 仅在废弃 prefab |
| `Simple.Button.Bg` | 零引用 |
| `Simple.Button.Text` | 零引用 |

**活跃笔刷（保留）**：`StealBar.RuleText`, `Brush_DialogName`, `Brush_DialogText`, `Brush_HeadName`, `Brush_HeadText`, `Brush_CircleButton_NinjaReport`, `Brush_CircleButton_Close`

---

## 八、代码改动（非文件删除）

| 项目 | 文件 | 操作 |
|------|------|------|
| `DebugInstantFollowUps = true` | `Quests/Causality/QuestConsequenceResolver.cs:99` | 改 `true` → `false` |
| `CNs/language_data.xml` 编码 | `ModuleData/Languages/CNs/language_data.xml` | UTF-16 LE → UTF-8 |

---

## 九、目录清理

| 目录 | 操作 | 原因 |
|------|------|------|
| `ExampleModVS/ExampleMod/ExampleMod/obj/Debug_v1.2.12/` | 删除 | 废弃编译配置，CLAUDE.md 已声明该配置废弃 |
| `ModuleData/Native/` | 删除（空目录） | 所有文件已删 |
| `GUI/SpriteParts/ui_character_illustration/` | 删除（空目录） | 所有 PNG 已删 |

---

## 执行顺序

| 步骤 | 操作 | 风险 |
|------|------|------|
| 1 | 删除 GUI 废弃 prefab（13 个文件） | 零 |
| 2 | 删除 SpriteParts 立绘（6 个文件）+ SubModule.xml:27-29 | 低 |
| 3 | 删除 Native/ 测试数据（4 个文件）+ SubModule.xml:30-65 | 低 |
| 4 | 删除 `ArtisanBeerMissionView.cs` + MySubModule.cs:89 | 低 |
| 5 | 删除 `DebugBehavior.cs` | 低 |
| 6 | 删除 `MyCustomUIVM.cs` + MySubModule.cs 清理（~30 行） | 低 |
| 7 | 删除 `test_talk.json` + 更新 MyCommands.cs 注释 | 低 |
| 8 | 删除 `addition_2_CNs.xml` + `output_strings2.xml` + 更新 CNs/language_data.xml | 低 |
| 9 | CNs/language_data.xml 编码转换 UTF-16→UTF-8 | 低 |
| 10 | `DebugInstantFollowUps = false` | 低 |
| 11 | 清理 MyBrush.xml 废弃笔刷 | 低 |
| 12 | 清理空目录 + `obj/Debug_v1.2.12/` | 零 |

### 文件统计

| 类别 | 数量 | 文件 |
|------|------|------|
| GUI 废弃 prefab | 13 | 各种备份/临时/废弃 |
| SpriteParts 立绘 | 6 | taikou PNG + sprite 定义 |
| Native 测试数据 | 4 | clans/heroes/lords/artisan_beer |
| 测试 C# 代码 | 3 | ArtisanBeerMissionView.cs, DebugBehavior.cs, MyCustomUIVM.cs |
| 测试对话 JSON | 1 | test_talk.json |
| 未引用语言 XML | 2 | addition_2_CNs.xml, output_strings2.xml |
| **文件删除合计** | **29** | |
| **代码改动** | 3 处 | MySubModule.cs, QuestConsequenceResolver.cs, CNs/language_data.xml |
| **目录清理** | 3 个 | obj/Debug_v1.2.12, ModuleData/Native, SpriteParts/ui_character_illustration |
