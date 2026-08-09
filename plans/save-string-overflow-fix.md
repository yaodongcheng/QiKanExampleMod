# 存档字符串溢出修复计划（TheftLedger 超长 → Strings 表损坏）

> 状态：**待实施**（2026-08-09 定案）
> 关联排查：`测试.sav` 无法读档——根因 = `lwn_theft_ledger` JSON 膨胀到 37853 字节，
> 超过 Bannerlord SaveSystem Strings 表 16 位长度上限（32767），写入时 short 溢出 → 表错位 → 读档必崩。

## 〇、机制回顾（为什么坏）

- SaveSystem 的 **Strings 表**：每条字符串的长度字段是 **16 位 signed short（上限 32767）**。
- 任何**单条字符串 > 32767 字节** → 写入时溢出成负数 → **整张表错位**（该 entry 之后全部读错）。
- 读档时 `ArchiveDeserializer.LoadFrom` 读到负长度 → `ReadBytes(负数)` → `OverflowException` → `LoadContext.Load` 内部 catch → 返回 null → 弹"载入存档时发生了一个错误"。
- **数据本身完整无损**（37853 字节 JSON 全写进文件了），只是长度字段溢出——**读档侧还原即可无损读取**。

## 一、全项目持久化字符串审计（MyBehavior.SyncData 16 个 + 其他）

| # | key | 内容 | 增长机制 | 膨胀上限 | 风险 |
|---|-----|------|---------|---------|------|
| 1 | `lwn_theft_ledger` | TheftLedger 偷窃账本 | **每次偷窃 +1 条**（中文 LocationName ~250B/条），`MarkCleared` 只标记不删除 | **无** ❌ | 🔴 **已爆**（37853B/约160条） |
| 2 | `lwn_crime_events` | WorldEventStore | 每事件 +1，ProcessDaily 清到 50 条 | 50 条 × ~1KB ≈ 50KB | 🔴 高危（也可能超） |
| 3 | `lwn_stability` | 定居点稳定性 | 定居点数固定 | ~几百 × 100B | 低 |
| 4 | `lwn_animal_theft` | 村庄动物偷窃 | 村庄数固定 + 记录数 | 待查 | 中 |
| 5 | `lwn_commission_narrative` | 委托叙事 | 按 NPC 积累 | 待查 | 中 |
| 6-11 | trust/infamy/tiers/cooldowns/honor/director | 信任/恶名/冷却等 | 按 NPC/定居点（有界） | 低 | 低 |
| 12 | `lwn_nemesis`/`lwn_conspiracy`/`lwn_infiltration` | 宿敌/阴谋/卧底 | 有界 | 低 | 低 |
| 13 | `_extendedProperties` (StoryContext) | **任意键值对字典** | 任意 subject 属性**无限累积** | **无** ❌ | 中 |
| 14 | `AIStory_Result` | 单条 LLM 输出 | 单条覆盖 | 单条 | 低（单条 32KB 也可能） |
| 15 | detention 系列 | 标量 | 固定 | 安全 | — |
| — | **SingNpcMemorySystem** | NPC 记忆 | **✅ 已确认不进存档**：`AllNpcMemoryManager._activeMemories` 是 static 字典无 SyncData；`StoryContext._npcProfiles` 的 SyncData 被注释；Memory/Story 无 Saveable 特性 | — | 🔴 **预期要进（AI 玩法前置）**——见下方扩展项 |

**规律**：`List<T>` 存储**无清理 = 线性膨胀** → 必超 32767；有上限的也可能因单条体积（中文 3B/字 + 列表）超限。

## 二、三层修复

### 第 1 层：读档侧（无损救档 + 永久容错）——必须做

patch `TaleWorlds.Library.BinaryReader.ReadBytes(int length)`（Prefix）：

```csharp
[HarmonyPrefix]
static void Prefix(ref int length)
{
    // short 溢出还原：超长字符串长度字段溢出成负数，+65536 即真实长度。
    // ⚠️ 仅限 short 溢出范围（-32768..-1）；其他负值 = 真正的数据损坏，保持抛异常（不掩盖）
    if (length < 0 && length >= -32768) length += 65536;
}
```

- 当前存档**无损读活**（37853B 完整读出，160 条记录不丢）
- **一劳永逸**：未来任何超长字符串不再崩档
- **语义安全**：仅还原 short 溢出（存档格式固有边界）；int32 层面的真损坏（负值 < -32768）保持抛异常暴露问题

### 第 2 层：存档侧（救活 `测试.sav`）——有第 1 层后**不需要**手工截断

> 存档数据完整（只是长度字段溢出），读档侧还原即可无损读取。
> 手工截断反而丢 ~15% 记录。**跳过。**

### 第 2.5 层：双向监控（诊断加固——玩家再遇问题可精确定位 key）

**现状缺口**：现有诊断只覆盖读档侧（FirstChance 异常 + ReadBytes 负长度），**存档侧是盲区**——
超长字符串写入时 **short 静默溢出、不抛异常**，玩家存档成功但已写坏，无从定位。
且读档侧堆栈只到 `ArchiveDeserializer.LoadFrom`，**定位不到具体 key**。

**原则**：**key 信息只存在于写入侧**——读档侧永远无法知道哪个 key 超长。
所以定位 key 的主力在**存档侧**，读档侧做配合。

**A. 存档侧（定位 key 主战场）：**

| 监控点 | 实现 | 产出日志 |
|--------|------|---------|
| **MyBehavior 统一守卫 GuardJson** | 16 个 SyncData key 逐个检查长度，超长即打日志并裁剪 | `[SyncDataGuard] lwn_theft_ledger 超长 37853B → 裁剪到 30000`——**精确到 key** |
| **SaveContext.AddOrGetStringId watchdog**（通用） | patch `TaleWorlds.SaveSystem.Save.SaveContext` 的 `AddOrGetStringId(string)`（程序集反射枚举定位，不依赖命名空间），text.Length > 30000 时打日志 + 调用栈（栈可显示正在序列化的对象类型） | `[SaveStrGuard] 超长字符串 {len}B\n{stack}`——覆盖**任何 mod** 的超长 |
| **SaveManager.Save FirstChance** | Save 期间挂 FirstChanceException（同 Load 侧），捕获保存时抛出的异常 | `[SaveDiag#n] {ex}`——捕获序列化异常 |

**B. 读档侧（配合）：**

| 监控点 | 实现 | 产出日志 |
|--------|------|---------|
| **SaveManager.Load FirstChance**（已有） | 捕获读档期间每次异常 | `[LoadDiag#n] {ex}\n{stack}` |
| **ReadBytes 负长度 watchdog**（已有，保留） | 负长度时打印调用链（`ArchiveDeserializer.LoadFrom` 等） | `[LoadDiag-ReadBytes] SUSPICIOUS length={n}\n{stack}` |

**玩家反馈流程**：玩家把 `Debug/StoryEngine_RuntimeLog.txt` 发来 → 搜 `[SyncDataGuard]` / `[SaveStrGuard]` / `[LoadDiag]` → 直接看到超长 key / 对象类型 / 异常堆栈。

### 第 3 层：根因侧（防复发）——必须做

**3a. 膨胀型存储逐个加"队列 + 老数据淘汰"：**

> 🔴 **淘汰必须保证引用完整性**——先审引用方再定淘汰：
> - **活引用**（数据不能淘汰）：`CommissionQuest._data.WorldEventId`（Quest 进行中）、
>   `PlayerDetentionBehavior._eventId`（玩家被扣押中）、对话链路 `Find() ?? pending`。
> - **历史引用**（可淘汰 + 调用方 null 降级）：UI 历史明细、背包赃物标注（走未清记录不受影响）、
>   旧 KnownEvents 的 EventId（已有 null 检查）。
> - **原则**：① 活跃数据永不淘汰；② 结案数据保留 N 天缓冲期再淘汰（防悬挂引用）；
>   ③ 淘汰打日志（`[Trim]`，可追溯）；④ 调用方 null 兜底按下方清单逐处审计。

| 存储 | 淘汰策略 | 引用保护 |
|------|---------|---------|
| **TheftLedger** | Serialize 时：**未清记录全保留**（活账：栽赃/赃物标注要用）+ **已清记录按 Day 只留最近 50 条**（死账：只做历史）→ 总量 ≤ ~30KB | 未清记录被 `GetSourceTag`/`GetFrameableTargets` 引用 → **不淘汰**；已清记录仅 UI 历史 → 淘汰后空列表，可接受 |
| **WorldEventStore** | 现有 50 条收紧为 **30 活跃 + 20 结案**，Serialize 前按 LastUpdateDay 淘汰**最老的已结案**事件 | 🔴 **被 Quest/扣押引用的事件绝不淘汰**：淘汰前检查 `CommissionQuest` 活跃任务的 WorldEventId + `PlayerDetentionBehavior` 进行中扣押的 `_eventId`，命中即保留（豁免） |
| **StoryContext._extendedProperties** | 总键上限 500 + 单值上限 1000 字符，超限淘汰最老 | 调用方全部 `?.` 传播（已确认）✓ |
| **AIStory_Result** | 单条截断 30000 | 单条覆盖，无引用问题 |
| VillageAnimalTracker / CommissionNarrative | 实施时核对实现，按同原则裁剪 | 实施时同步审计引用方 |

**调用方 null 兜底审计清单**（实施时逐处处理）：

| 调用方 | 现状 | 处理 |
|--------|------|------|
| `CommissionQuest.cs:2058` `WorldEventStore.Find(_data.WorldEventId)` | 未确认 null 处理 | Quest 引用的事件被豁免不淘汰（双保险）；仍补 null 降级（事件缺失 → Quest 按无事件继续/自动失败提示） |
| `PlayerDetentionBehavior` 多处 `Find(_eventId)` | 未确认 null 处理 | 扣押中的事件豁免不淘汰；null 时按"无事件扣押"兜底（不崩） |
| `AgentBrain.cs:1386` `Find(pending.EventId)` | 未确认 | 补 null 检查 |
| `AccountabilityIntents.cs:34` | `?? pending` ✓ | 无需改 |
| `GetExtendedProperty` 系 | `?.` ✓ | 无需改 |

**3b. 统一守卫（兜底）**——MyBehavior.SyncData 入口一处覆盖全部 16 个：

```csharp
private static string GuardJson(string key, string json, int maxBytes = 30000)
{
    if (json == null) return json;
    // 🔴 必须按 UTF-8 字节数判断（存档长度字段是字节数，short 上限 32767）——
    // 字符数判断会被中文绕过（1 中文字 = 3 字节：30000 字符 = 90000 字节 > 32767）！
    if (Encoding.UTF8.GetByteCount(json) <= maxBytes) return json;
    DebugLogger.Log($"[SyncDataGuard] {key} 超长 {Encoding.UTF8.GetByteCount(json)}B → 裁剪到 {maxBytes}B");
    // 按字节截断（避免切坏 UTF-8 多字节字符：截到合法边界）
    var bytes = Encoding.UTF8.GetBytes(json);
    int cut = maxBytes;
    while (cut > 0 && (bytes[cut] & 0xC0) == 0x80) cut--;  // 回退到字符边界
    return Encoding.UTF8.GetString(bytes, 0, cut);
}
```

> ⚠️ 计划内所有"长度上限"（30000/300 字/100 字/40 条）统一按**字节**口径复核：
> 中文内容必须用 UTF-8 字节数，英文可按字符数近似（ASCII 1B/字）。

## 二.5、扩展项：NPC 记忆持久化（AI 玩法前置——用户预期要进存档）

**现状（已查实）**：`AllNpcMemoryManager._activeMemories`（static 字典）无 SyncData；
`StoryContext._npcProfiles` 的 SyncData 被注释；Memory/Story 无 Saveable 特性。
→ **记忆当前完全不入档，每次启动从零开始。**

**进存档前的超长风险评估**（比 TheftLedger 更危险，单份记忆内部几乎无上限）：

| 记忆字段 | 现状上限 | 进档风险 |
|---------|---------|---------|
| `PermanentMemory` | `MaxPermanentLength=300` **只是注释，代码没强制** | 🔴 LLM 总结输出不可控 |
| `KnownEvents` | **无清理**（ReceiveNews 只更新不删） | 🔴 无限膨胀 |
| `GlobalNews` | 无上限 | 🔴 |
| `RecentHistory`/`DynamicMemories` | 10/5 条滑动窗口 ✓ | 低 |
| `QuestHistory` | 20 条 ✓ | 低 |
| **NPC 数量维度** | 几十个 NPC × 单份记忆 | 总量必超 32767 |

**持久化设计要点**：
1. **粒度：每 NPC 一个 SyncData key**（`lwn_memory_<StringId>`）——单份记忆独立成键，
   天然隔离大小（单份 < 32KB 即安全），读档按需恢复（`GetMemory` 已惰性创建）。
   不合成一个大 JSON（必超限）。
2. **单份记忆硬上限**（进档前强制——总结机制是主防线，这里是**最终防线**，不依赖 LLM 好坏）：
   - `PermanentMemory` 代码级强制 300 字（`CheckAndPromoteToPermanent` 里 Append 前截断）+ `GlobalNews` 100 字截断
   - `KnownEvents` 只留最近 30 条（队列淘汰，最老丢弃）
   - **LLM 失败兜底**（铁律 1）：总结依赖 LLM——LLM 未配置/失败时 `RecentHistory` 不裁剪（`MaintainMemoryAsync` 只在总结成功后 RemoveRange）→ 在 Serialize 时强制 RecentHistory 最多 40 条（超了丢最老）
   - 汇总后单份 < ~8KB ✓
3. **序列化范围**：只存 `_activeMemories` 已创建的记忆（懒加载语义不变）；
   可选"结案 NPC 记忆回收"（长期不互动的 NPC 记忆移出存档，需互动时重建——按 LastInteractDay 淘汰）
4. **守卫**：`GuardJson` 同样覆盖 `lwn_memory_*` 键（超长裁剪 + `[SyncDataGuard]` 日志）
5. **引用完整性**：记忆里的 `EventId`（KnownEvents）引用 WorldEvent——事件淘汰后
   `GetEventById` 返回 null（已有兜底 ✓）；记忆本身的淘汰按 NPC 维度（无跨 NPC 引用）
6. **实施时机**：随本次根因修复一起落地（守卫先就位，再开持久化，避免带病进档）

## 三、实施顺序

0. **三版本兼容验证**（1.2.12 / 1.3.15 / 1.4.6 备份 DLL 反编译对比）：
   `TaleWorlds.Library.BinaryReader.ReadBytes(int)`、`SaveManager.Save(object, MetaData, string, ISaveDriver)`、
   `SaveManager.Load(string, ISaveDriver, bool)` 三版本签名一致性；不一致则按 `MB2_GE_*` 宏分写
1. **读档侧 patch**（ReadBytes 还原）+ **双向监控**（SaveManager.Load/Save FirstChance + AddOrGetStringId watchdog + MyBehavior GuardJson）→ 编译 → 用户验证 `测试.sav` 能读 ✓（无损救档 + 监控就位）
2. **TheftLedger.Serialize 裁剪**（未清全保留 + 已清留 50，按**字节**核算总量）——核心根因
3. **WorldEventStore.Serialize 裁剪**（30/20 淘汰 + Quest/扣押引用豁免）
4. **MyBehavior 统一守卫**（16 个 key 全部接入 GuardJson，UTF-8 字节口径，兜底 + key 定位日志）
5. 其他膨胀点（_extendedProperties / AIStory_Result / VillageAnimalTracker / CommissionNarrative）逐个处理
6. **回归闭环**：读档 ✓ → 游玩 → **存档** → **再读档**（验证 Strings 表解析全绿 + 新存档无负长度）→ 多偷 200 次 + 多事件后再存再读
7. 删除 `SaveLoadDiagnostics.cs` + csproj 条目（排查诊断代码——**GuardJson / AddOrGetStringId watchdog 保留**，作为常驻诊断，玩家反馈用）

## 四、验证标准

- `测试.sav` 无损读活（TheftLedger 160 条完整）
- 新存档 Strings 表解析全绿（Python 脚本验证无负长度/无超长）
- 长时间游玩（多偷 200 次 + 多事件）后存档仍可读

## 五、已完成的排查足迹（本次会话）

- 触发点：`SandBox.dll` → `SandBoxSaveHelper.LoadGameAction` → `MBSaveLoad.LoadSaveGameData` 返回 null → 弹"载入存档时发生了一个错误"（`{=onLDP7mP}`）
- 异常被 `TaleWorlds.SaveSystem.Load.LoadContext.Load` 内部 catch 吞掉（只打 `Debug.Print(ex.Message)`，1.3.15 不落盘）
- 诊断：FirstChanceException 捕获 + `ReadBytes` watchdog → 精确定位 `ArchiveDeserializer.LoadFrom` 读 Strings 表 entry 9834 负长度 -27683
- 存档解析：`测试.sav` GameData 解压 44MB，Header 完好；Strings 表 entry 9834 = `lwn_theft_ledger` JSON 实际 37853B（short 溢出）
- 附带发现：`StealManager.LootChestItem` 多 element 负值 bug（13:17 抛过 MBUnderFlowException）——独立真 bug，另案修复
