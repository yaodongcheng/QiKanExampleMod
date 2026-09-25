# 动画状态机：XML 化 + 编辑器（2026-09-25 交接）

> **一句话**：飞行动画状态机的**定义从 C# 搬到了 XML**（改它不用重编译），并配了 **UE 式可视化页 + 拖拽编辑器**。
> 🔴 **但一次都没在游戏里跑过，两个页面也没做过视觉验收** —— 下一 session 第一件事就是这两件。
> 关联：[玩家飞行-实施方案.md](玩家飞行-实施方案.md)（飞行工程主文档）· [rules/wheels.d/agent.md](rules/wheels.d/agent.md)（通用件轮子）

---

## 一、现在是什么（本会话已落地的）

| # | 件 | 在哪 | 说明 |
|---|---|---|---|
| 1 | **定义** | `ModuleData/statemachine_flight.xml` | 20 状态 / 19 边 / 3 容器 / 21 谓词 + 3 命名标量。**改它不用重编译**，重启游戏即生效 |
| 2 | **装载 + 校验** | `Animation/AnimMachineLoader.cs` | 状态名 / 目标 / 容器成员 / 谓词名 / 数字**逐条校验**；不过就**整台不注册**，并把全部问题一次报到日志 |
| 3 | **判据（两层）** | `Flight/FlightAnimConditions.cs`（命名谓词）+ `Animation/AnimPrimitives.cs`（原语） | 复合条件用命名谓词；"按了什么键/动画还剩多久"直接在 XML 写原语：`key="W" key-mode="down"`、`anim="remaining" anim-lt="0.2"` |
| 4 | **键的载体** | `IAnimInputFacts`（新接口）← `FlightAnimContext` 实现，`PlayerFlightBehavior.UpdateKeyFacts()` 每帧填 | 8 键 × 电平/按下沿/松开沿 + 动画剩余秒数 |
| 5 | **边型三档** | `AnimEdgeDef` | 缺省 = 可打断一次性动作；`after-finish="true"` = 演完才进；`phase="true"` = 相位驱动（状态机不求值，只作文档） |
| 6 | **机外状态** | XML 里 `outside` | = "非飞行"那个外部状态。**只允许用在相位边上**：起飞 `outside → hoverstart`、落地 `superland → outside` |
| 7 | **演完兜底** | `<state … next="X"/>` | **条件优先、演完兜底**：先看有没有边命中，都没命中才回 X（避免"先回趴姿再被踢走"的两次交叉淡化） |
| 8 | 只读页 | `Debug/offline/statemachine_flight.html` | 节点图（分组盒 + 状态节点 + 边）+ 状态表 + 迁移表 + 谓词表 + 不变式体检 |
| 9 | 编辑器 | `Debug/offline/statemachine_editor.html` | 拖连线 / 建容器 / 分层 tab / 双击进容器或状态 / 导出完整 XML |

**生成器与自检**（都在 `Debug/offline/`）：`gen_statemachine_page.py` · `gen_statemachine_editor.py` · **`check_pages.py`**。
🔴 **改完生成器必须跑** `python check_pages.py` —— 它把页面里的 JS 抠出来喂 `node --check`。
（踩过：模板里一个转义写错 ⇒ 生成的 JS 语法错 ⇒ **整页一行 JS 都不跑**，界面看着"能显示"其实全是静态壳，肉眼完全看不出。就是它抓出来的。）

---

## 二、🔴 没验证的（下一 session 先做这三件）

1. **实机没跑过**。XML 装载器 / 键与动画原语 / `演完兜底` / `outside` 全部只到"编译通过 + 规则镜像通过"。
   **判据（重启后看日志，两行一眼可辨）**：
   ```
   ✅ [Anim:flight] 定义已从 XML 装载：20 个状态 / 19 条边 / 3 个族（statemachine_flight.xml）
   ❌ [Anim] 状态机 'flight' 定义有 N 处问题，**未注册**： + 逐条问题列表
      [Flight] 状态机未注册（定义有问题…）—— 飞行本身照常，只是没有姿态动画
   ```
2. **两个页面没做视觉验收**（Tabbit 浏览器一整天起不来，我一次都没看到渲染）。要验：
   - 只读页：图的**标签有没有撞字/压卡片**；那个"3 条相位边"、"outside 盒"在不在
   - 编辑器：**双击容器能不能进去**、拖连线 / 拖状态进容器 / 导出完整 XML 能不能用
3. **行为变化要实机确认**（3 条）：
   - **演完兜底**：入姿演完那一刻若已松开 Shift，应**直接**去 idle/hovermove（不再先闪一下趴姿）
   - `hovermoveLeanL/R` 两条边改成**键原语**（`key="A"/"D"` held），**去掉了迟滞** —— 看压弯会不会抖
   - 起飞/落地的相位边**只是文档**，行为应与改前**没有变化**

---

## 三、编辑器与 UE 动画蓝图的差距（用户已点名，按性价比排）

| # | 缺 | 说明 |
|---|---|---|
| 1 | **Entry 节点** | 状态机的"默认入口"。我们现在靠相位 Force，图上看不出"默认从哪开始" |
| 2 | **Blend Space** | 按参数插值；我们的压弯/俯仰本来就是"预烘 K 档 + 交叉淡化"，做成 Blend Space 顺路 |
| 3 | **Undo/Redo、复制粘贴、注释框、框选** | 纯编辑体验 |
| 4 | **实时调试** | 拿 `[Anim:flight]` 日志回放 / 高亮当前状态与剩余时间 |
| 5 | 真嵌套状态机 | 子机器有自己的 Entry / Any State / 内部边 —— **另起一轮，先写计划**（会动已验证的飞行链） |

---

## 四、用户已裁定的设计原则（**别推翻**）

1. **命名**：状态名 = 动作名 = clip 名（去掉 `act_fly_` / `flight_` 前缀与 `_a` 后缀）
2. **条件读输入语言**（`hold-left` = 按着 A），**不读内部量名**（曾叫 `BankBand < 0`，图上看不懂）
3. **结构进 XML、判据留 C#** —— 字符串表达式 = 另一种"打错了不报错"的静默失败
4. **容器只在编辑器里嵌套**（方案甲）：导出仍是 `<family name="…">成员…</family>` 名单，**运行期一行不改**
5. **演完兜底**：条件优先、演完兜底（不是无条件先回宿主）
6. **`outside`（机外）只给相位边**；"机内任意状态 `*`"是另一个东西，别混
7. 默认日志安静：`AnimDebug.Trace` 默认 **off**（`custom.anim_log on|full|off`）；**`✗ 被抢走` 那条永远打**

---

## 五、待办清单

- [ ] **实机验**（§二 三件）
- [ ] 两个页面的视觉验收 + 几何微调（撞字、遮挡）
- [ ] 按 §三 的 1~4 逐项补编辑器（每项都不动运行期）
- [ ] **轮子登记**（我问过多次、用户一直没答，下次问一次）：① 命名约定 ② 拐点日志三件套 ③ 转移表"模式量 vs 无输入量"互抢规则 ④ 状态机可视化生成器 ⑤ XML + 命名谓词的接缝设计 ⑥ 容器只在编辑器嵌套（方案甲）⑦ `check_pages.py`（生成物 JS 语法自检）
- [ ] 清理一次性补丁：`Debug/offline/_*.py`（`_` 前缀 = 改完即弃）可删
- [ ] 只读页生成器两处小瑕疵：docstring 的数据来源写过时了；`read_xml` 里有一行多余的 `path = …"STATEMACHINE_FILE"`（被下一行覆盖，无害）

---

## 六、文件清单（本次改动，git 归用户）

**新增**：`Animation/AnimPrimitives.cs` · `Animation/AnimConditions.cs` · `Animation/AnimMachineLoader.cs` ·
`Flight/FlightAnimConditions.cs` · `ModuleData/statemachine_flight.xml` ·
`Debug/offline/gen_statemachine_page.py` · `Debug/offline/gen_statemachine_editor.py` · `Debug/offline/check_pages.py`

**改动**：`Animation/AgentAnimStateMachine.cs`（演完兜底语义 + 跳相位边 + `CurrentRemaining`）·
`Animation/AnimMachineRegistry.cs`（`PhaseForced` + `AddEdge`）· `Flight/FlightAnimMachine.cs`（瘦成注册三件事）·
`Flight/FlightInput.cs`（`RawKeyAt`）· `Flight/PlayerFlightBehavior.cs`（`UpdateKeyFacts` + 输入沿日志）·
`Flight/FlightTuning.cs` · `ExampleMod.csproj` · `plans/玩家飞行-实施方案.md`

清单外的脚本产物：`Debug/offline/statemachine_*.html`（生成物，勿手改）· `Debug/offline/_*.py`（一次性补丁，可删）

⚠️ **`bin/Win64_Shipping_Client/LivingWorldNpcs.dll` 是 Claude 用 `dotnet build` 覆盖的**（只为验证语法）——
按纪律正式测试请用 **VS2022 重编**。
