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
| — | **SingNpcMemorySystem** | NPC 记忆 | **当前不进存档**（MyBehavior 注释明确） | — | 未来若加入必须守卫 |

**规律**：`List<T>` 存储**无清理 = 线性膨胀** → 必超 32767；有上限的也可能因单条体积（中文 3B/字 + 列表）超限。

## 二、三层修复

### 第 1 层：读档侧（无损救档 + 永久容错）——必须做

patch `TaleWorlds.Library.BinaryReader.ReadBytes(int length)`（Prefix）：

```csharp
[HarmonyPrefix]
static void Prefix(ref int length)
{
    // short 溢出还原：超长字符串长度字段溢出成负数，+65536 即真实长度
    if (length < 0) length += 65536;
}
```

- 当前存档**无损读活**（37853B 完整读出，160 条记录不丢）
- **一劳永逸**：未来任何超长字符串不再崩档
- **语义安全**：负 length 只有 short 溢出一种来源（int32 负数长度不可能被写入）

### 第 2 层：存档侧（救活 `测试.sav`）——有第 1 层后**不需要**手工截断

> 存档数据完整（只是长度字段溢出），读档侧还原即可无损读取。
> 手工截断反而丢 ~15% 记录。**跳过。**

### 第 3 层：根因侧（防复发）——必须做

**3a. 膨胀型存储逐个加"队列 + 老数据淘汰"：**

| 存储 | 淘汰策略 |
|------|---------|
| **TheftLedger** | Serialize 时：**未清记录全保留**（活账：栽赃/赃物标注要用）+ **已清记录按 Day 只留最近 50 条**（死账：只做历史）→ 总量 ≤ ~30KB |
| **WorldEventStore** | 现有 50 条收紧为 **30 活跃 + 20 结案**，Serialize 前按 LastUpdateDay 淘汰最老 |
| **StoryContext._extendedProperties** | 总键上限 500 + 单值上限 1000 字符，超限淘汰最老 |
| **AIStory_Result** | 单条截断 30000 |
| VillageAnimalTracker / CommissionNarrative | 实施时核对实现，按同原则裁剪 |

**3b. 统一守卫（兜底）**——MyBehavior.SyncData 入口一处覆盖全部 16 个：

```csharp
private static string GuardJson(string key, string json, int maxLen = 30000)
{
    if (json == null || json.Length <= maxLen) return json;
    DebugLogger.Log($"[SyncDataGuard] {key} 超长 {json.Length}B → 裁剪到 {maxLen}");
    return json.Substring(0, maxLen);   // Deserialize catch 分支安全降级为空，不崩
}
```

## 三、实施顺序

1. **读档侧 patch**（ReadBytes 还原）+ 编译 → 用户验证 `测试.sav` 能读 ✓（无损救档）
2. **TheftLedger.Serialize 裁剪**（未清全保留 + 已清留 50）——核心根因
3. **WorldEventStore.Serialize 裁剪**（30/20 淘汰）
4. **MyBehavior 统一守卫**（16 个 key 全部接入，兜底）
5. 其他膨胀点（_extendedProperties / AIStory_Result / 待查两个）逐个处理
6. **回归**：正常读档 → 玩一会儿 → 存档 → 再读档（Strings 表解析验证无负长度）
7. 删除 `SaveLoadDiagnostics.cs` + csproj 条目（排查诊断代码）

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
