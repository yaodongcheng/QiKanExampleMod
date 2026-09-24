# spells — 轮子速查分卷（wheels.md 索引导航）

> 域：**通用施法框架**（法术 = 四段拼装 + 自管实体投射物）。方案与验收 = `plans/法术体系-通用施法框架.md`。

## 法术四段框架 — `Combat/Spell{Def,Pieces,CastFlow,ProjectileLogic}.cs`

**解决什么问题**：加一个新法术/新形态不要再写一套系统 —— 四段各一个接口 + 一张注册表，**加形态 = 在某一段加一个实现**，其余三段不动。

四段：**起手**（谁/何时/什么姿势）· **瞄准**（往哪去）· **投送**（怎么过去）· **结算**（到了干什么）。

**现成的实现**（2026-09-23：阶段 1 + 阶段 2 全在）：

| 轴 | 实现 id | 干什么 | 关键数据字段 |
|---|---|---|---|
| 瞄准 | `aim` | 准星直视（顺带解抛物线） | `speed` / `gravity` |
| 瞄准 | `ground` | **落点**（看哪儿的地面/墙面） | 配 `indicator` 画落点圈 |
| 瞄准 | `locked` | **软锁**，一个目标一条意图（多目标火球） | `lock_max` / `lock_angle` / `lock_range` |
| 瞄准 | `self` | **自身为心**（光环 / 领域 / 自爆） | — |
| 投送 | `projectile` | 直线飞 / 抛物 / **追踪** / **天降** / **多发** | `turn_rate`（追踪）· `start_height`（天降）· `count`/`spread_deg`（多发，在瞄准轴摊） |
| 投送 | `place` | **留在落点上**，按间隔反复结算 | `duration` / `repeat_interval` / `start_delay`(+`_variance`) |
| 投送 | `channel` | **持续**（光束/领域）：每 `repeat_interval` 沿朝向打一条射线报命中；**结束时刻由起手轴决定**（松手/打断） | `repeat_interval` · `max_lifetime`（硬上限） |
| 结算 | `damage` | 单体伤害（打中谁算谁） | `damage` / `damage_type` |
| 结算 | `area` | 半径伤害（**不含**被直击者），边缘衰减，圆心 = 命中点 | `radius` / `radius_falloff` |
| 结算 | `aura` | 同 area，**圆心 = 施法者自己**（光环/领域）；有 status 就顺带挂给半径内所有人 | 同上 |
| 结算 | `status` | 持续伤害状态（燃烧/中毒…），**同目标同法术刷新时长不叠伤害** | `status`/`status_damage`/`status_duration`/`status_interval`/`status_particle` |

**起手轴（阶段 3）**：`cast_type` = `normal`（按住蓄力、松手发）· `channel`（按住持续、松手停）· `instant`（按下即出）。
玩家键 = `InteractionIds.SpellCast`（默认 **X**，2026-09-24 由 R 改 —— R 与原版切视角冲突；手柄 RT，走 `ModInput` 可改键）。
蓄力档位 → `Power`(0~1) → 随意图/命中传到结算 → 伤害 `× (1 + charge_bonus × Power)`。
代码 = `Combat/SpellCastInput.cs`（相位机 + 蓄力核视觉 + 打断 + 输入缓冲）。**手势细节见下方「施法 3C（手感）」一节。**

**NPC 施法者（阶段 4）**：NPC 手里是「法印 + 法术弹」= 它是法师，`Combat/SpellNpcCaster.cs` 每秒扫附近一次、
按 `ai_weight` 带权随机选法术、`ai_cooldown` 防重入、**从不碰移动**（施法期间照常走位）。
框架白名单只开 7 类族：`{projectile, channel, skyfall, place, buff, heal, teleport}`。数据还能 `ai="false"` 单独否掉。
测试：`custom.spell give`（给最近 NPC 装一套法印 → 它自己会放）。

🔴 **节流永远在投送手里**（契约 3）：`place` 是"每秒报一次"，`projectile` 是"撞到才报一次" —— 结算只管收一次算一次。
🔴 **多发在瞄准轴摊成 N 条意图**（契约 1 的意图表就是干这个的）；**追踪取的是目标引用"现在"的位置**（每帧重取）。
🔴 网格朝向约定：飞行物 = 本地 **+Z** 对齐飞行方向（`tilt_deg` 调横滚）；**躺在地上的**（放置区域 / 落点指示圈）= 本地 **+Y** 对齐世界朝上（`SpellMath.BuildGroundRotation()`；`lwn_flight_sigil` 就是这个约定）。

```csharp
// ① 数据：内容包 ModuleData/AssetRegistry/Spells.xml，一行一个法术，**身份 = 它用哪个弹药物品**
//    <Spell id="projectile_yinmo_zhan" ammo="taikou_spell_crescent" family="projectile"
//           mesh="lwn_yinmo_crescent" trail_particle="…" impact_particle="…"
//           damage="60" damage_type="Cut" radius="3.5" radius_falloff="0.5"
//           speed="60" gravity="0" max_distance="120" max_lifetime="4"
//           hit_radius="1.2" pierce="1" scale="1" tilt_deg="90" />
SpellDef def = SpellRegistry.FindByAmmo("taikou_spell_crescent");   // 认领（补丁用）
SpellDef def = SpellRegistry.FindById("projectile_yinmo_zhan");

// ② 施法（唯一入口，玩家/NPC 共用 —— 铁律 18）
bool ok = SpellCastFlow.Cast(casterAgent, spellDef, origin, direction);

// ③ 加自己的一段（注册制，世界观无关）
SpellTargetingRegistry.Register(new MyLockOnTargeting());   // ISpellTargeting：BuildIntents(req, List<SpellCastIntent>)
SpellDeliveryRegistry.Register(new MyBeamDelivery());       // ISpellDelivery：Begin(shot) → ISpellDeliveryInstance（Tick(dt)→bool / End）
SpellPayloadRegistry.Register(new MyStatusPayload());       // ISpellPayload：OnHit(SpellHit)

// ④ 族预设（配表省事：不写三轴就吃族的默认；族名当法术 id 前缀）
SpellRegistry.Families   // 内建 11 个（projectile/channel/skyfall/place/…）+ 内容包 <Family> 行可覆盖
```

🔴 **三条契约（写对了才敢加新形态，计划 §3.2）**：
① 瞄准产出**一张意图表**（`List<SpellCastIntent>`，含 `Target` 目标引用 + `Velocity` **初速向量**），阶段 1 恒 1 条；
② 投送**有生命周期**（`Begin` / `Tick(dt) 返回 false = 结束` / `End`），持续光束以后只是换实现；
③ 结算**只认"收到一次命中"**，"多久上报一次"归**投送**管（否则光束每帧上报 = 每帧结算 = 秒杀）。

🔴 **数据语义两条容易配错**：`pierce` = **最多能命中几个目标**（1 = 打中一个就消失）；`area` 结算**不重复打"直接被命中的那位"**（单体伤害归 `damage`，否则 `damage=60 + radius=3.5` 变成 120）。

## 自管实体投射物（为什么不走引擎导弹）

🔴 引擎导弹渲染**约 110 米外必定剔除网格**（全局 LOD 距离表表尾；放大 5 倍 / 开 `MissileWithPhysics` / 补 LOD 到 8 档 / 换原版大网格**全都照样消失** —— 七次实机证据链见 `plans/法印施法体系-实施计划.md` §16.2）。**场景实体路径没有这个上限**。

```csharp
// 造飞行物（照抄范围：Knowledge/SwordBeam剑气_实现分析.md；本工程实现 = SpellWorld / ProjectileDelivery）
MetaMesh mm = MetaMesh.GetMultiMesh(name) ?? MetaMesh.GetCopy(name, showErrors:false, mayReturnNull:true); // 解析一次整局缓存
GameEntity e = GameEntity.CreateEmpty(scene, true);  e.AddMultiMesh(mm, true);
e.SetGlobalFrame(new MatrixFrame(rotation, position));               // 每帧只改 origin
ParticleSystem ps = ParticleSystem.CreateParticleSystemAttachedToEntity(name, e, ref MatrixFrame.Identity); // 拖尾自动跟着走
// 命中：**优先用引擎原生查询**，绝不自己遍历全场 agent 读 CollisionCapsule（1 次原生调用 × 几百人 × 20Hz = 灾难）
Agent v = V.RayCastForClosestAgent(mission, from, to, excludeIndex, hitRadius, out float dAgent);  // ①打到谁
bool hit = scene.RayCastForClosestEntityOrTerrain(from, to, out float dWorld, out Vec3 point, 0.01f,
                                                  BodyFlags.CommonCollisionExcludeFlagsForMissile); // ②打到墙/地
// 两条各给一个距离 → **取近的那个**；线段 = 上一帧位置→这一帧位置（天然连续碰撞，不会跨 3 米穿过人）
// 落地伤害：手搓 Blow(DamageCalculated=true) + AttackCollisionData.GetAttackCollisionDataForDebugPurpose(...)
//           → AgentDamageHelper.CastBlow(victim, in blow, in inCollision, damage, logTag: "Spell")   ← 复用现成轮子
```

**纪律（计划 §4.4）**：碰撞检查 **0.05 秒一次**（不是每帧）· 容器一律复用 · **并发上限 32，超了直接拒绝施放** · 命中音效/爆散挂在**投送**（撞到东西是物理事件），伤害挂在**结算**。

## 起手：法印开火拦截补丁 — `Combat/SpellSealFirePatch.cs`

```csharp
[HarmonyPatch(typeof(Mission), "OnAgentShootMissile")]   // 4 个版本（1.2.12/1.3.15/1.4.8/1.5.2）签名逐字相同
public static class SpellSealFirePatch { [HarmonyPrefix] public static bool Prefix(...) }
```
判据 = **这一发用的弹药物品在我们的法术表里**（`shooterAgent.Equipment[weaponIndex].AmmoWeapon`，与引擎取值口径同源）；不在表里一律放行（别的 mod 射箭、原版射弩、`AddCustomMissile` 那条路根本不经过本方法）。
🔴 **六条纪律**：守卫要窄 / 前缀内**吞异常并放行**（出错就 `return true` 让引擎照常发导弹）/ 补一次 `UpdateLastRangedAttackTimeDueToAnAttack`（引擎原方法末尾那句，AI 计时用）/ 可 `DisabledPatchClasses` 单关 / 挂 `contentPackOnly` 名单（内容包专属补丁在纯功能包模式不挂）/ 兜底天然优雅（补丁关了 = 退回旧表现 `flying_mesh` 照飞）。

## 🔴 施法 3C（手感）—— 起手手势 · 蓄力视觉 · 方向来源（2026-09-24 登记）

> 「3C」= 角色 / 相机 / 操作的手感面。同类先例：`plans/铁炮射击手感3C-实施计划.md`（后坐、准星、音效那一套）。
> **这一节是"按哪个键、球长在哪、朝哪飞"的唯一速查**；相位机与数据字段的实现分别在 `SpellCastInput` / `Spells.xml`。

### 两套手势（同一个相位机，`_flightGesture` 在**起手那一刻定死**）

| | 地面 | **飞行中**（`PlayerFlightBehavior.Current?.IsFlying`） |
|---|---|---|
| 蓄力键 | 按住 **X**（`InteractionIds.SpellCast`） | 按住 **右键**（`FlightInput.AimHeld` —— 它同时也是瞄准机位键，一举两得） |
| 发射 | **松 X** 即发 | **点左键**才发（`FlightInput.ConsumeFirePress()`）；**松右键 = 取消**（不发射） |
| 连发 | 要松一次手才认下一发（防按住连发） | 右键还按着就能接着蓄下一发（左键那一次点击把关） |
| 装备 | 必须手持「法印 + 法术弹」 | **不要求装备**：手里认得出就用它，认不出回落 **`SpellRegistry.DefaultFlightSpell`**（表里第一条 `projectile` 族，按加载顺序） |
| 引导型（`channel`） | 按住 X 持续放、松手停 | 按住右键持续放、松右键停（引导中左键不参与） |

**蓄力视觉**（球 + 粒子，两套手势共用，代码 = `SpellCastInput.UpdateCore/CoreAnchor`）：

| 件 | 怎么调 |
|---|---|
| 球的位置 | 地面 = 身前近似手位（等"法阵 prefab"换真挂点）；**飞行 = 右手上方**（身体朝向绕 Up 转 90° 取 `.s` 当右向；真挂手骨 = `Monster.MainHandBoneIndex` + `AgentVisuals.GetBoneEntitialFrame`，范本 `CampaignMode/Tools/FlySpike.cs:1962`）—— 左右反了就把 `CoreAnchor` 里的右向取反 |
| 球的大小 | **数据 `charge_scale`** = **满蓄力时**的放大倍率（默认 `0.375` ⇒ 核 ⌀0.72 m 时满蓄力 ⌀0.27 m，起手是它的 1/3）；运行时 `custom.spell core <倍率>`（不用重启） |
| 粒子的浓淡 | 随 `Power` 调**发射率倍数** `0.3 → 1.5`：`ParticleSystem.SetRuntimeEmissionRateMultiplier(mult)`（引擎为此专门开的接口；同一颗粒子不重建） |

### 🔴 方向来源 = `Camera/CameraLook.cs`（唯一入口，CLAUDE.md 铁律 35）

**法术朝哪飞 = "当前真正在管相机的那台"的视线**。我们接管相机（飞行/演出）后引擎的 `CameraBearing/Elevation` 是**冻的**
—— 直接读它 = 法术永远朝"接管那一刻看的方向"飞（2026-09-21 在飞行上栽过同一条；2026-09-24 飞行中施法又差点栽）。

```csharp
if (CameraLook.TryGet(out Vec3 look)) { /* 用 look */ }   // 接管中自动问接管方（ICameraLookProvider），没接管才用引擎角度
// 飞行相机自己实现 ICameraLookProvider（PlayerFlightBehavior），进入时注册、每帧幂等同步、退出清
```

### 已知风险（实机第一眼看的）

1. **飞行中会不会一发变两发**（左键顺带触发原版攻击）：理论上冻结档 `aipause` 下引擎不吃玩家输入；真出现就在飞行中按住右键时屏蔽左键攻击。
2. **球的左右**（见上，一行取反）。
3. **没做的**：飞行中真挂手骨（手挥动时球不跟随）。

**诊断/验收**：日志 `[Spell] 方向对比（<法术>）：取用=… look=… camBearing=… frameF=…` 只对**玩家自己**的每一发打（`frameF` 是反面对照 —— 它是"上"，别拿它当视线）。

## 诊断 — `custom.spell`（`SpellProjectileLogic.cs`）

```
custom.spell                     状态（表里几条 / 在飞数 / 上限 / 朝向覆盖）
custom.spell list                列出全部法术
custom.spell cast <spellId>      从玩家眼睛沿视线直接放一发（**不用装备法印**）
custom.spell npc [spellId]       让最近的那个人朝玩家放一发（**验"管线不关心施法者是谁"**，铁律 18）
custom.spell core <倍率>|off     **手心蓄力核**大小覆盖（按住施法键当场看）
custom.spell tilt <度>|off       飞行姿态横滚角覆盖（朝向标定：一次实机试遍几个候选）
custom.spell verbose on|off      每次碰撞检查打日志
custom.spell probe               对最近的 agent 做三条实测定性（射线粗细/是否扫掠/场景查询打不打 agent）
```

🔴 **朝向约定 = 网格本地 +Z 是飞行方向**（原版弹丸一致）；朝向不对先调数据里的 `tilt_deg`（绕飞行轴转），**别去改网格几何**。

## 配套接线（缺一处 = 静默不生效）

1. `ExampleMod.csproj` 显式登记 5 个 `.cs`（本项目不登记 = 不编译）。
2. `Core/MySubModule.cs`：`mission.AddMissionBehavior(new SpellProjectileLogic())` **必须挂在玩法闸门 `IsInteractionDisabled()` 之前**（法术的主战场就是战场/攻城）。
3. `Core/VersionCompat.cs` 的 `V.RayCastForClosestAgent(...)` —— 1.2.12 与 1.3.0+ **形参顺序不同**（`out dist` 在前 vs 在后）；`Scene.RayCastForClosestEntityOrTerrain` 带实体出参那版 1.2.12 是 `GameEntity`、1.3.0+ 是 `WeakGameEntity` ⇒ **换用不带实体出参的重载**绕开。
4. **粒子 XML 属 soln 体系**：要在 `ModuleData/project.mbproj` 挂 `soln_particle_systems` 行，否则**编辑器**里看不到这个效果（体检 `check_module_registration.py` 已覆盖，按前缀匹配 `particle_systems*`）。
   🔴🔴 **但挂好 ≠ 游戏里能用**（2026-09-24 实机证伪，我在这上面判断错过两次）：**引擎运行期的粒子表来自 AssetPackages 里的 `Particle` 资产（编译产物）**，ModuleData 的 XML + mbproj 注册只是**编辑器源**。
   判据（可复现）：`tpaccli dump --packdir Modules/Native/AssetPackages --filter <效果名>` —— 原版效果打出的是 **`META Particle`**；同名的材质/贴图会打成 `material` / `texture`（`waterfall_splash` 就属于后者，所以引擎运行时报 `Unable to find particle system with name waterfall_splash`）。
   外部反证：HikageRising **一个粒子 XML 都没有**，122 个粒子全是 tpac 资产且生效。
   ⇒ **要给内容包加粒子：XML + mbproj 注册只是第一步，必须再在编辑器里发布成粒子包，产物改名拷进内容包的 `AssetPackages/`**。详见 [法术体系-通用施法框架.md](../../法术体系-通用施法框架.md) §B。
