# audio — 轮子速查分卷（wheels.md 索引导航）
## 音效链路（音频文件 → 游戏内事件）— `ModuleSounds/` + `ModuleData/module_sounds.xml` + `Combat/SoundFx.cs`

2026-10-10 建（钩索绳声 + 飞行风噪落地时）。**加任何新音效照这五步走，别另造一套。**

| 步 | 做什么 | 位置 / 判据 |
|---|---|---|
| 1 | 音频文件（`.wav` **直接可用，不用转格式**） | 模块根 `ModuleSounds/`（引擎标准目录；XML 里 `path` 只写文件名） |
| 2 | 声明事件 | `ModuleData/module_sounds.xml`：`<module_sound name=… sound_category=… path=…>`；多变体用 `<variation path=… weight=…>` 子节点（随机挑一条播） |
| 3 | 注册 soln 行 | `ModuleData/project.mbproj`：`<file id="soln_module_sound" name="ModuleData/module_sounds.xml" type="module_sound"/>` —— 🔴 **文件在、行没挂 = 一个都不注册**（Taikou 火器踩过：开火只剩引擎默认弩声）。离线 `Scripts/check_module_registration.py` 把守 |
| 4 | 代码播放 | `Combat/SoundFx.cs`：`Play3D(名字, 位置)`（**返回句柄**，供"跟着走"用）/ `ListenerPosition()` / `TryResolve` / `Describe`（id 缓存 + 查不到只记一次日志 + 绝不外抛；🔴 2D 那条实测不出声、**已删，别再恢复**） |
| 5 | 打包白名单 | `package_mod.py` 的 `ITEMS` 必须含 `"ModuleSounds"`（不加 = 发布包丢音效文件，本机测试看不出来） |

**sound_category 选档**（单次/循环 + 时长上限；完整表在 `Native/ModuleData/module_sounds.xml` 的注释里）：
`mission_combat`（单次 ≤8s，**前景层 —— 我们两个事件用的这个**，火器枪声同档）· `mission_foley`（单次 ≤4s，**背景动效层、混音刻意压低**——曾用它做绳声/风噪，实机"太小"后换掉）· `mission_siege_loud`（爆炸/大动静）· `mission_ambient_3d_*`（persistent，场景环境音）· `ui`（UI 音）· `campaign_bed`（大地图 2D 循环，⚠️ 只用于 BGM 那条；我们自己的一次性音效**别用 2D**）。
⚠️ **分类不合法 = 不播**（原生注释原话）。

**🔴 一律用 3D（省略 `is_2d`）—— 2D 路径本项目实测不出声**（2026-10-10 实机：`custom.grapple sound` 3D 响、`custom.flight sound` 2D 全程静音；事件 id 解析正常、分类合法，原因在 native 不在 C# 层）。
**"自己身上的声音"的正确挂法 = 3D + 每帧把位置跟到锚点**：锚点首选**玩家本体** `Agent.Main.Position`（玩家不在时兜底相机）—— 锚点处 ≈ 听者处 = 满音量不衰减 = 听感等价 2D；
🔴 **并且必须每帧跟**：1.8 秒的阵风在冲刺 26 m/s 下会漂到 45 米外，世界锚定的尾巴会被距离吃掉。范本 = `Flight/FlightWindFx.cs`（`AnchorPosition()` + `FollowAnchor()`，带场景守卫：换场景只丢句柄不碰实体）。

**格式结论**（2026-10-10，双向证据）：引擎**直接吃 WAV** —— Taikou 火器 32 个 wav = 48kHz/24bit **实机验证**；Shokuho 抽样 250 个里 240 个 = 44.1kHz/16bit 且**正式发布中** ⇒ 16bit / 44.1k / 48k / 立体声都在范围内。

**播放写法**（照抄即用；与火器 / BGM 同款）：
```csharp
int id = SoundEvent.GetEventIdFromString(name);   // 查不到 = -1（缓存它，别每次查字符串）
if (id < 0) return;
SoundEvent sound = SoundEvent.CreateEvent(id, Mission.Current.Scene);  // ⚠️ scene 不能为 null（此版裸解引用 .Pointer）
sound.SetPosition(pos);   // 只 3D 要
sound.Play();
```
- 引擎**没有音量 API**（`SoundEvent` 只有位置 / 速度 / 参数）⇒ 强度靠**间隔密度**表达（风噪范本：`Flight/FlightWindFx.cs`，速度越快两阵越密）。
- 循环音 = 素材必须无缝循环 + persistent 分类；**带衰减包络的"阵风"类素材用"重复单次"更自然**（别硬做循环）。

**🔴 "声音太小"怎么办**（引擎没有音量 API —— 2026-10-10 实战，两条杠杆**都不用重编译**）：
① **素材响度规整** = `Scripts/gen_lwn_module_sounds.py`（源目录 → `ModuleSounds/`，幂等生成器；链路 = 压缩 → 缩放到目标短时 RMS → **前视限幅** −1 dBFS。
   两个必踩坑写在脚本里：压缩器阈值以下必须原样、包络追不上单样本尖峰 ⇒ 必须前视限幅）；
② **换分类** = `mission_foley` 是**背景动效层**（混音刻意压低！）、`mission_combat` / `mission_siege_loud` 是**前景层**（火器同档、实机够响），改 XML 一行。
实测：4 条"几乎听不见"的风声被拉起 12~14 dB、6 条风响度差 18.6 dB → 1.6 dB；两事件 foley→combat 后用户确认可闻。

**🔴 短动作要"强制起一阵"**（`FlightWindFx.Kick()`，2026-10-10 实机）：只按速度密度播，遇上**又短又要速度达标**的动作（钩索拉拽全场 ~1 秒、速度曲线中后段掉到门槛以下）会**一阵都凑不出来**（计时器只在达标帧里走）⇒ 事件型触发（"起拽那一刻"）直接 Kick，不受门槛管。
**🔴 3D 听不见先量距离**：听者 = 相机，本项目机位常在身后几米（拉拽机位臂长 8 米）⇒ 用 `SoundEvent.GetEventMinMaxDistance()` 打出衰减区间（`SoundFx` 首次播放打 `[SoundFx] '<名>' 衰减距离 vec=(…)`）再对比。

**已挂的事件**（LWN）：`lwn_grapple_rope`（3D，钩索抛出瞬间）· `lwn_flight_wind`（3D，飞行与被拽两路按速度密度，6 变体随机）。
**验收命令**：`custom.grapple sound` / `custom.flight sound`（试听 + 状态；**名字查不到直接报** = 静音故障的第一诊断入口）。
**同款先例**：`Combat/FirearmFxLogic.cs`（火器三档枪声；音效名走内容包 `AssetRegistry/FirearmFx.xml` 契约 —— 基座不硬编码世界观词汇）· `SpellPieces.cs` 的 `SpellWorld.PlaySound`（法术 `sound_release`/`sound_hit` 字段）。
**📚 完整版**（能力边界 / API 全貌 / 未挖清单 / 三条管线怎么分）=[Knowledge/骑砍2音效系统_引擎能力与实现.md](../../../Knowledge/骑砍2音效系统_引擎能力与实现.md) —— 本卷只是速查，细节看它。
**方案与落地记录**：[plans/音效-钩索与飞行.md](../../音效-钩索与飞行.md)。
