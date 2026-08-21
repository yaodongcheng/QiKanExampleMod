# NPC 记忆存档读档周期修复计划（读档后未互动 NPC 记忆静默丢失）

> 状态：**待实施**（2026-08-21 定案）
> 触发：`AllNpcMemoryManager` 存档链路全面检查（对话历史/短期记忆/长期记忆的存档方式、容量管理、超限裁剪统一性）
> 关联背景：[plans/save-string-overflow-fix.md](save-string-overflow-fix.md)（Strings 表单条 32767B 上限）、[Knowledge/存档机制深度解析.md](../Knowledge/存档机制深度解析.md) §10.4、`plans/rules/wheels.d/save.md`（存档字符串超长防护）

## 〇、根因分析（三条链路叠加）

```
① _activeMemories 是 static（AllNpcMemoryManager.cs:51）
   → 游戏读档（LoadGame 不重启进程）时旧档内存残留，不清理
② 读档时记忆只缓存进 _pendingRestores（AllNpcMemoryManager.cs:224-241）
   → 时序防御设计（Hero.AllAliveHeroes 未就绪），不合并、不查 Hero
③ SerializeSlot 只遍历 _activeMemories，从不看 _pendingRestores（AllNpcMemoryManager.cs:199-219）
   → 读档后未互动的 NPC 记忆没有写回路径
```

**后果矩阵**：

| 场景 | 后果 |
|------|------|
| 🔴 新进程读档（最常见） | `_activeMemories` 为空 → 保存时只有本次互动过的 NPC 进新档 → **所有未互动 NPC 的记忆（含 LLM 生成的人设三字段）永久丢失**，每次读档重开还重复烧 LLM 重新生成 |
| 🔴 同进程切档/重载档 | `_activeMemories` 残留旧档内容 → 未互动 NPC 用**旧内容覆盖新档内容**（跨档污染） |

**为什么修**：对话历史/动态记忆/永久记忆/人设全是 LLM 成本产物，读档越频繁记忆库缩水越严重，且玩家无感知（只跟互动过的 NPC 对话，那些还在）。

## 一、修复方案

### A. `SerializeSlot` 写回 `_pendingRestores` 条目（主修复，防丢失）

**[AllNpcMemoryManager.cs:197-220](..%2FExampleModVS%2FExampleMod%2FExampleMod%2FMemory%2FAllNpcMemoryManager.cs) 改造**：

```csharp
// 🔴 新增一把字典锁（_dictLock），保护 _activeMemories / _pendingRestores 的全部读写
// （GetMemory 的查/建/加、DeserializeSlot 的填、TryMergePendingRestore 的移除、SerializeSlot 的遍历）。
// 理由：GetMemory 可能在 LLM 回调/IM 后台线程被调，普通 Dictionary 的遍历/写并发会抛
// InvalidOperationException——"快照 ToList()"只缩短窗口、不免疫（ToList 自身遍历期间被改照样炸）。
// 现有 _activeMemories 无锁遍历是既有隐患，本次一并修掉。
public static string SerializeSlot(int slot)
{
    var entries = new List<NpcMemorySaveEntry>();
    var seen = new HashSet<string>();
    List<KeyValuePair<string, NpcMemorySaveEntry>> pendingSnap;
    List<KeyValuePair<string, SingNpcMemorySystem>> activeSnap;
    lock (_dictLock)
    {
        pendingSnap = _pendingRestores.ToList();
        activeSnap = _activeMemories.ToList();
    }

    // ① _pendingRestores 条目（本次读档的权威数据）优先写回——防读档后未互动的 NPC 记忆丢失。
    //    读档语义：新档是当前世界状态；_activeMemories 里的残留是上一个世界的内容，不权威。
    foreach (var kv in pendingSnap)
    {
        if (string.IsNullOrEmpty(kv.Key)) continue;
        if ((StableHash(kv.Key) & 0x7FFFFFFF) % SaveSlots != slot) continue;
        var e = kv.Value;
        if (e == null) continue;
        if (IsEmptyEntry(e)) continue;              // 复用惰性空检查（防旧档脏数据）
        entries.Add(e);
        seen.Add(kv.Key);
    }

    // ② _activeMemories 条目（运行时新建 / 读档后互动已合并的；pending 里有的跳过——pending 优先）
    foreach (var kv in activeSnap)
    {
        if (string.IsNullOrEmpty(kv.Key) || kv.Key.StartsWith("TEMP_AGENT_")) continue;
        if (seen.Contains(kv.Key)) continue;        // pending 已写，跳过（新档内容权威）
        if ((StableHash(kv.Key) & 0x7FFFFFFF) % SaveSlots != slot) continue;
        var m = kv.Value;
        if (m == null) continue;
        if (IsEmptyEntry(m)) continue;
        entries.Add(new NpcMemorySaveEntry(kv.Key, m));
        seen.Add(kv.Key);
    }
    return Newtonsoft.Json.JsonConvert.SerializeObject(entries);
}
```

> `IsEmptyEntry` 抽成 helper（现在 ① 的惰性空检查逻辑复用，见原实现 208-215 行——active 用 `SingNpcMemorySystem` 字段判断，pending 用 `NpcMemorySaveEntry` 字段判断，两套字段对应，可写一个重载/泛化）。

**设计决策**：
- **pending 优先于 active 残留**：读档语义 = 新档权威。旧档残留内容（含"旧档保存后、读档前"的互动）按读档语义应丢弃（世界已回退）。同理 `RestoreFromSave` 的覆盖逻辑无需改动。
- **写回后不移除 pending**：条目必须保留——将来 `GetMemory` 惰性合并仍要从 pending 取（内容已写回存档 ≠ 内存已恢复）。代价：多轮读档 pending 会累积，但**同档覆盖**（`_pendingRestores[key] = entry`），实际有界（≈ 各档 NPC 并集，几十~几百，可接受）。
- **字典并发保护**：新增 `_dictLock` 保护两个字典的全部读写（含现有 `_activeMemories` 无锁遍历的既有隐患——`GetMemory` 可能在 LLM 回调/IM 后台线程被调，`SerializeSlot` 主线程遍历普通 Dictionary 在并发写时会抛 InvalidOperationException；`ToList()` 快照只缩短窗口不免疫，必须真锁）。加锁点：`DeserializeSlot` 填充 / `TryMergePendingRestore` 移除 / `SerializeSlot` 快照 / `GetMemory`、`GetMemoryForAgent` 的查-建-加 / `ResetActiveMemories`。
- **旧档结构兼容**：旧档反序列化出的 `NpcMemorySaveEntry` 缺新字段（如 `ImportantEvents` = null）→ 原样写回 JSON 带 null → 读回后 `RestoreFromSave` 的 `if (importantEvents != null)` 不覆盖 → 无害。

### B. 读档时清空 `_activeMemories`（防跨档残留污染）

**[MyBehavior.cs:452-458](..%2FExampleModVS%2FExampleMod%2FExampleMod%2FCore%2FMyBehavior.cs) SyncData 的 IsLoading 分支**，在 `DeserializeSlot` 循环前加：

```csharp
if (dataStore.IsLoading)
    AllNpcMemoryManager.ResetActiveMemories();   // 清空 static 残留（上一个世界的记忆）
```

```csharp
// AllNpcMemoryManager 新增
public static void ResetActiveMemories()
{
    _activeMemories.Clear();
}
```

- **清空后**：任何 `GetMemory` 走"新建 + TryMergePendingRestore"路径 → 内容来自新档 ✓
- **安全性**：读档发生在主菜单 Load，无活跃对话/互动；清空字典不销毁实例，正在跑的 LLM 总结后台任务继续写旧实例（已脱离字典，不会被序列化，无害）
- **与 A 的关系**：A 单独修数据丢失（写回），B 修运行时表现（读档后 NPC 记忆立即正确）+ 双重保险。**A、B 必须同时上**——只上 B 不上 A：清空后未互动 NPC 既不合并也不写回 → 真丢。只上 A 不上 B：写回正确，但运行时 NPC 记忆仍是残留的（对话会出戏）。

### C. 槽内按最后活动时间排序（让 GuardJson"丢最老"语义成立）

**现状问题**：`GuardJson` 的结构感知截断按"数组尾部 = 最老"丢（[SaveGuard.cs:85](..%2FExampleModVS%2FExampleMod%2FExampleMod%2FDebug%2FSaveGuard.cs#L85) 的设计假设），但 `SerializeSlot` 槽内条目顺序 = Dictionary 迭代顺序（**无任何保证**）→ 超限时被裁掉的是随机 NPC，不是最老。

**改法**：`entries` 序列化前排序——按"最后活动时间戳"降序（最新在前，尾部 = 最老）：

```csharp
static double LastActivityOf(NpcMemorySaveEntry e) =>
    e.RecentHistory?.LastOrDefault()?.TimeStamp ??
    e.DynamicMemories?.LastOrDefault()?.TimeStamp_End ?? 0;
entries.Sort((a, b) => LastActivityOf(b).CompareTo(LastActivityOf(a)));
```

- 与问题 1 无依赖，独立可上
- 中文 3B/字 + 满配 NPC ~8KB：单槽约 4 个满配 NPC 就会触发裁剪，**C 不是理论问题，实际会命中**

### D. `RestoreFromSave` 最小安全钳制（无 LLM 参与，即时生效）

**[SingNpcMemorySystem.cs:427-461](..%2FExampleModVS%2FExampleMod%2FExampleMod%2FMemory%2FSingNpcMemorySystem.cs) RestoreFromSave 末尾**追加：

```csharp
// 🔴 读档容量钳制（防热度档位变化后超限状态长期存在）：
// 只做无 LLM 的硬钳制——动态记忆 FIFO 到上限、永久记忆截断到上限。
// RecentHistory 不硬裁：超量只是 prompt 变长，等下次 AddHistory 自然总结（避免读档即触发 LLM + 弹窗打扰）。
while (DynamicMemories.Count > MaxDynamicMemoryCount)
    DynamicMemories.RemoveFirst();
if (PermanentMemory.Length > MaxPermanentLength)
    PermanentMemory.Remove(MaxPermanentLength, PermanentMemory.Length - MaxPermanentLength);
```

- 现状：读档后若热度从 Hot(20/8/500) 掉到 Cold(4/2/100)，超量数据要等下次 `AddHistory` 才延迟自愈（且触发条件是 2× 上限）
- **不触发 LLM 总结**（读档即弹 LLM 失败红字打扰玩家；`SuppressFailureAlerts` 此时未必设置）

### E.（可选，本期不做）批量合并时点

读档后把 `_pendingRestores` 全部物化进 `_activeMemories`（Hero 就绪后逐 tick 重试）。**不做理由**：① A 已保证数据不丢；② `GetMemory` 惰性合并对"对话"零影响（对话必先 GetMemory）；③ 批量合并的唯一收益是"非对话路径读记忆"（IM 群聊选人、AgentBrain 决策）——需要时再说。

## 二、验证计划

| # | 验证项 | 方法 | 预期 |
|---|--------|------|------|
| 1 | 读档后未互动 NPC 记忆不丢 | 新档互动 NPC A → 保存 → 重启进程读档（不互动 A）→ 再保存 → `Scripts/save_inspect.py --dump=lwn_npc_mem_{slot}` | A 的记忆在（含人设三字段） |
| 2 | 跨档无污染 | 同进程读另一档（A 记忆不同）→ 不互动 A → 保存 → dump | A 是新档内容，非旧残留 |
| 3 | 读档后互动正常 | 读档 → 对话 A → 保存 → dump | 合并正确（新档 + 新互动） |
| 4 | 槽内裁剪丢最老 | 单槽塞满 >30KB → 保存 → dump | 丢的是最后活动时间最早的 NPC |
| 5 | 并发安全 | 高强度 IM 对话中保存 | 无 ConcurrentModificationException |
| 6 | 回归 | 旧档（修复前存的）直接读 | 正常，无字段缺失异常 |

## 三、未决问题（需用户确认，本期不动）

**记忆型字段未存档清单**（`NpcMemorySaveEntry` 之外）：

| 字段 | 现状 | 影响 |
|------|------|------|
| `QuestHistory`（≤20 条） | 不存档 | 委托因果引擎 `MapQuestToId`/`ExtractCausalityContext` 读档后为空——因果链断（若依赖） |
| `KnownEvents`（事件传闻） | 不存档 | 读档后传闻清空，需重新传播 |
| `CurrentUrgentEvent` | 不存档 | 注释说由 WorldEventDatabase 推送，但读档后无重建逻辑（WorldEventStore 恢复了，指针没恢复） |
| `HiddenAttitudeTowardPlayer` / `CurrentGoal` / `ActiveConflict` | 不存档 | 会话级状态，丢可接受 |

> 判断标准：这些是"该跟世界一起持久化"还是"会话级临时状态"。若确认要存 → `NpcMemorySaveEntry` 加字段（旧档 null 兼容，向后安全），走本 plan 的既有管线。

**顺带审计项（本期不修，仅记录）**：其他 static 单例（`ImHeatTracker` / `PlayerImageStore` / `ImChatStore` 等）读档同样存在残留问题——它们的 `Deserialize` 若只做"合并"而非"清空再填"，读档后会有旧世界数据残留（同构 bug）。修本 plan 时顺手核对各 `Deserialize` 是否全量覆盖，发现残留就在各自系统按本 plan 的 B 方案模式处理（读档入口清空）。

## 四、方案自查记录（完善性复查，2026-08-21）

对方案 A/B 的复查，逐项排除的疑点：

1. **pending 条目写回会不会丢"读档后新互动"？** 不会——互动必走 `GetMemory` → 合并后 pending 被移除 → 写回走 active 分支（含新互动）✓
2. **active 残留 vs pending 谁权威？** pending（新档）。残留内容 = 上一个世界，按读档语义丢弃 ✓（B 方案同时让运行时表现一致）
3. **写回后 pending 不移除 → 累积泄漏？** 有界：`_pendingRestores[key] = entry` 同档覆盖，总量 ≈ 读过的档的 NPC 并集 ✓
4. **清空 `_activeMemories` 会不会打断进行中的 LLM 总结？** 后台任务持旧实例引用，清字典不销毁实例，写旧实例无害（已脱离序列化路径）✓
5. **并发遍历会不会抛异常？** 🔴 复查修正（原方案写"快照 ToList() 防并发"——**不充分**）：`.ToList()` 自身在遍历期间被其他线程修改仍会抛 InvalidOperationException，快照只是缩短窗口。必须加 `_dictLock` 保护两个字典的全部读写（A 方案代码已更新）。附带修复既有隐患：现状 `SerializeSlot` 遍历 `_activeMemories` 本就无锁，而 `GetMemory` 可在 LLM 回调/IM 后台线程写字典 ✓
6. **`IsEmptyEntry` 双签名**：active 判 `SingNpcMemorySystem` 字段、pending 判 `NpcMemorySaveEntry` 字段——抽 helper 时注意别把两套字段混用 ✓
7. **GuardJson 兜底仍有效**：A/B 修复后槽内容量语义不变，外层 30KB 守卫仍按 C 的排序丢尾部 ✓
8. **不动分槽机制**：槽数/哈希/FNV 全部保持——本修复与分片设计正交 ✓
9. **旧档兼容**：修复前存的档读进来，pending 条目结构完整，写回无 schema 变更 ✓
10. **读档后立即保存**（最短路径）：DeserializeSlot 填满 pending → 清空 active → 保存走 A ① → 全量写回，零丢失 ✓
