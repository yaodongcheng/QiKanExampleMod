
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
namespace LivingWorldNpcs
{
    /// <summary>记忆存档条目（每 Hero 一条；Heat 独立走 ImHeatTracker 的小 key）。</summary>
    [Serializable]
    public class NpcMemorySaveEntry
    {
        public string HeroId { get; set; }
        public List<ChatMessage> RecentHistory { get; set; }
        public List<RecentMemory> DynamicMemories { get; set; }
        public string PermanentMemory { get; set; }
        // 人设精炼三字段（常驻人设，2026-08-10）：一次性生成长期使用，必须存档否则读档后重复生成
        public string BackgroundStory { get; set; }
        public string Personality { get; set; }
        public string Specialty { get; set; }
        // 🔴 2026-08-16（方案 N）：大事记槽（写入时 C# 白名单分级锚定；旧档无字段 → 空，不补写）
        public List<string> ImportantEvents { get; set; }

        public NpcMemorySaveEntry() { }

        public NpcMemorySaveEntry(string heroId, SingNpcMemorySystem m)
        {
            HeroId = heroId;
            RecentHistory = m.RecentHistory != null ? new List<ChatMessage>(m.RecentHistory) : new List<ChatMessage>();
            DynamicMemories = m.DynamicMemories != null
                ? m.DynamicMemories.Select(x => new RecentMemory(x.Content, x.TimeStamp_Start, x.TimeStamp_End, x.CampaignDay) { SeqId = x.SeqId }).ToList()  // 拷贝时保留调试编号（跨存档稳定）
                : new List<RecentMemory>();
            PermanentMemory = m.PermanentMemory?.ToString() ?? "";
            BackgroundStory = m.BackgroundStory ?? "";
            Personality = m.Personality ?? "";
            Specialty = m.Specialty ?? "";
            ImportantEvents = m.ImportantEvents != null ? new List<string>(m.ImportantEvents) : null;
        }
    }

    public static class AllNpcMemoryManager
    {
        /// <summary>记忆存档分槽数（单 key ≤ 30KB 防 SaveSystem Strings 表溢出；槽 = 稳定哈希 % 槽数，跨存档稳定）。</summary>
        public const int SaveSlots = 24;

        private static Dictionary<string, SingNpcMemorySystem> _activeMemories = new Dictionary<string, SingNpcMemorySystem>();

        /// <summary>
        /// 读档待合并条目（heroId → 存档数据）。🔴 关键时序防御：CampaignBehavior.SyncData 加载时
        /// Hero.AllAliveHeroes 可能尚未填充（对象图遍历顺序不定），直接查 Hero 会全部落空 → 记忆静默丢失。
        /// 方案：DeserializeSlot 只缓存条目，不查 Hero；GetMemory 惰性创建时自然合并（幂等覆盖）。
        /// </summary>
        private static readonly Dictionary<string, NpcMemorySaveEntry> _pendingRestores = new Dictionary<string, NpcMemorySaveEntry>();

        /// <summary>
        /// 🔴 双字典统一锁（2026-08-21，plan npc-memory-save-restore-fix）：保护 _activeMemories /
        /// _pendingRestores 的全部读写。GetMemory 可能在 LLM 回调/IM 后台线程被调，普通 Dictionary
        /// 遍历/写并发会抛 InvalidOperationException——"快照 ToList()"只缩短窗口不免疫。
        /// 🔴 锁序纪律（防死锁）：_dictLock → 实例 _lock 单向（GetMemory → TryMergePendingRestore →
        /// RestoreFromSave 取实例锁）；SingNpcMemorySystem 内不得反向取 _dictLock。
        /// </summary>
        private static readonly object _dictLock = new object();


        /// <summary>
        /// 获取或创建该 Agent 的记忆系统
        /// </summary>
        public static string GetPlayerDescription(NPCProfile targetNpcProfile)
        {
            // 本地化：LWN_prompt_player_desc_fallback（无主英雄兜底，双桶）
            if (Hero.MainHero == null) return LWNTextHelper.ResolvePrompt("LWN_prompt_player_desc_fallback");

            Hero player = Hero.MainHero;
            string playerId = player.StringId;
            var playerMemory  = GetMemory(playerId);
            if(playerMemory!= null)
            {
                return playerMemory.GetPersonaPrompt();
            }

            StringBuilder sb = new StringBuilder();

            // 本地化：LWN_prompt_player_desc_name（名字，双桶）
            sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_player_desc_name", ("NAME", player.Name.ToString())) + " ");
            // 本地化：LWN_prompt_player_desc_identity（身份：{CLAN}的{GENDER}，双桶）
            // 本地化：LWN_prompt_player_desc_no_clan（无家族，双桶）
            string clanText = player.Clan != null ? player.Clan.Name.ToString() : LWNTextHelper.ResolvePrompt("LWN_prompt_player_desc_no_clan");
            // 本地化：LWN_prompt_player_desc_gender_female / LWN_prompt_player_desc_gender_male（性别称呼，双桶）
            string genderText = player.IsFemale
                // 本地化：LWN_prompt_player_desc_gender_female（双桶）
                ? LWNTextHelper.ResolvePrompt("LWN_prompt_player_desc_gender_female")
                // 本地化：LWN_prompt_player_desc_gender_male（双桶）
                : LWNTextHelper.ResolvePrompt("LWN_prompt_player_desc_gender_male");
            // 本地化：LWN_prompt_player_desc_identity（双桶）
            sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_player_desc_identity",
                ("CLAN", clanText), ("GENDER", genderText)) + " ");

            if (player.Clan?.Kingdom != null)
            {
                // 本地化：LWN_prompt_player_desc_kingdom（效忠于{KINGDOM}，双桶）
                sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_player_desc_kingdom",
                    ("KINGDOM", player.Clan.Kingdom.Name.ToString())) + " ");
            }


            // 简单通用描述
            // 本地化：LWN_prompt_player_desc_honor（荣誉值，双桶）
            sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_player_desc_honor",
                ("HONOR", player.GetTraitLevel(DefaultTraits.Honor).ToString())) + " ");
            // 本地化：LWN_prompt_player_desc_gold（持有金钱，双桶）
            sb.Append(LWNTextHelper.ResolveCompound("LWN_prompt_player_desc_gold",
                ("GOLD", player.Gold.ToString())));

            return sb.ToString();
        }

       
        
        public static SingNpcMemorySystem GetMemory(string stringId)
        {
            //目前是只有互动需要时候才调用
            lock (_dictLock)
            {
                if (_activeMemories.TryGetValue(stringId, out var mem)) return mem;

                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == stringId);
                if (hero != null)
                {
                    // 否则，创建一个新脑子
                    NPCProfile profile = GenerateHeroProfile(hero);
                    SingNpcMemorySystem newMemory = new SingNpcMemorySystem(profile);
                    _activeMemories[stringId] = newMemory;
                    // 读档数据合并（Hero 就绪后自然生效；无存档条目 = 无操作）
                    TryMergePendingRestore(newMemory, stringId);
                    return newMemory;
                }
                return null;
            }
        }

        public static SingNpcMemorySystem GetMemoryForAgent(Agent agent)
        {
            //目前是只有互动需要时候才调用


            if (agent == null || agent.Character == null) return null;

            // 获取唯一ID (如果是英雄用 HeroStringId，如果是普通士兵用 Name)
            string uniqueId = agent.Character.StringId;
            if (agent.Character.IsHero && agent.Character is CharacterObject charObj && charObj.HeroObject != null)
            {
                uniqueId = charObj.HeroObject.StringId;
                return GetMemory(uniqueId);
            }
            else
            {
                // 普通士兵没有持久化ID，暂时用名字+HashCode，或者直接不存长时记忆
                uniqueId = $"TEMP_AGENT_{agent.Index}_{agent.Name}";
            }

            lock (_dictLock)
            {
                // 如果内存里已经有这个人的脑子了，直接返回
                if (_activeMemories.TryGetValue(uniqueId, out var mem)) return mem;

                // 否则，创建一个新脑子
                NPCProfile profile = GenerateProfileFromGameData(agent);
                SingNpcMemorySystem newMemory = new SingNpcMemorySystem(profile);
                _activeMemories[uniqueId] = newMemory;
                return newMemory;
            }
        }
        public static void ClearTemporaryMemories()
        {
            lock (_dictLock)
            {
                var keysToRemove = _activeMemories.Keys.Where(k => k.StartsWith("TEMP_AGENT_")).ToList();
                foreach (var key in keysToRemove)
                {
                    _activeMemories.Remove(key);
                }
            }
        }

        // ───────────────────────── 存档（24 槽分片，MyBehavior.SyncData 接线） ─────────────────────────

        /// <summary>FNV-1a 稳定哈希（不依赖 .NET GetHashCode 的实现差异，跨进程/版本稳定）。</summary>
        private static int StableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }

        /// <summary>
        /// 序列化一个槽（🔴 2026-08-21 plan npc-memory-save-restore-fix 重写）：
        /// ① _pendingRestores 条目（本次读档的权威数据）优先写回——防读档后未互动的 NPC 记忆丢失
        ///    （旧实现只遍历 _activeMemories：读档后未互动 NPC 不在字典 → 逐轮保存覆盖丢失，实锤 2026-08-21）。
        /// ② _activeMemories 条目（运行时新建 / 读档后互动已合并的）；pending 已写的 key 跳过（防重复）。
        /// ③ 槽内按最后活动时间降序（最新在前，尾部 = 最老）——GuardJson 结构感知截断"丢最老"语义成立。
        /// ④ 写回前容量钳制（plan D①）：pending 条目按当前热度档位硬钳（save 时点 heat 已加载，可信；
        ///    DeserializeSlot 时点 heat 尚未反序列化，按错误档位钳制会过度裁剪 = 数据丢失，故钳制放这里）。
        /// TEMP 模板不存（键含 agent.Index 不稳定）。锁内只做快照（不持锁调 user 代码）。
        /// </summary>
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

            // ① pending（读档权威数据）优先
            foreach (var kv in pendingSnap)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Key.StartsWith("TEMP_AGENT_")) continue;
                if ((StableHash(kv.Key) & 0x7FFFFFFF) % SaveSlots != slot) continue;
                var e = kv.Value;
                if (e == null) continue;
                if (IsEmptyEntry(e)) continue;              // 复用惰性空检查（防旧档脏数据）
                ClampEntryToCap(e, kv.Key);                 // D①：写回前按当前热度档位钳制
                entries.Add(e);
                seen.Add(kv.Key);
            }

            // ② active（运行时新建 / 合并后）；pending 已写 → 跳过（防同一 NPC 双写）
            foreach (var kv in activeSnap)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Key.StartsWith("TEMP_AGENT_")) continue;
                if (seen.Contains(kv.Key)) continue;
                if ((StableHash(kv.Key) & 0x7FFFFFFF) % SaveSlots != slot) continue;
                var m = kv.Value;
                if (m == null) continue;
                if (IsEmptyEntry(m)) continue;              // 惰性：无任何内容的记忆不写盘（含人设三字段）
                entries.Add(new NpcMemorySaveEntry(kv.Key, m));
                seen.Add(kv.Key);
            }

            // C：槽内按最后活动时间降序（最新在前，尾部 = 最老）——GuardJson 数组裁剪丢尾部 = 丢最老
            entries.Sort((a, b) => LastActivityOf(b).CompareTo(LastActivityOf(a)));

            return Newtonsoft.Json.JsonConvert.SerializeObject(entries);
        }

        /// <summary>惰性空检查：无任何内容的记忆不写盘（RecentHistory / DynamicMemories / 永久记忆 / 人设三字段 / 大事记全空）。</summary>
        private static bool IsEmptyEntry(NpcMemorySaveEntry e)
        {
            return (e.RecentHistory == null || e.RecentHistory.Count == 0)
                && (e.DynamicMemories == null || e.DynamicMemories.Count == 0)
                && string.IsNullOrEmpty(e.PermanentMemory)
                && string.IsNullOrEmpty(e.BackgroundStory)
                && string.IsNullOrEmpty(e.Personality)
                && string.IsNullOrEmpty(e.Specialty)
                && (e.ImportantEvents == null || e.ImportantEvents.Count == 0);
        }

        /// <summary>惰性空检查（active 侧：SingNpcMemorySystem 字段版，与 NpcMemorySaveEntry 版两套字段对应）。</summary>
        private static bool IsEmptyEntry(SingNpcMemorySystem m)
        {
            return (m.RecentHistory == null || m.RecentHistory.Count == 0)
                && (m.DynamicMemories == null || m.DynamicMemories.Count == 0)
                && (m.PermanentMemory == null || m.PermanentMemory.Length == 0)
                && string.IsNullOrEmpty(m.BackgroundStory)
                && string.IsNullOrEmpty(m.Personality)
                && string.IsNullOrEmpty(m.Specialty)
                && (m.ImportantEvents == null || m.ImportantEvents.Count == 0);
        }

        /// <summary>槽内排序键：最近活动时间戳（对话历史/动态记忆的最大时间戳；旧档全 0 → 排最后 = 最先被裁）。</summary>
        private static double LastActivityOf(NpcMemorySaveEntry e)
        {
            double t = 0;
            if (e.RecentHistory != null)
                foreach (var m in e.RecentHistory)
                    if (m != null && m.TimeStamp > t) t = m.TimeStamp;
            if (e.DynamicMemories != null)
                foreach (var m in e.DynamicMemories)
                    if (m != null && m.TimeStamp_End > t) t = m.TimeStamp_End;
            return t;
        }

        /// <summary>当前热度档位容量（与 SingNpcMemorySystem.ComputeCap 同公式的静态版本；null/未知 → Normal）。</summary>
        private static (int dynamicCap, int permCap) CapsFor(string heroId)
        {
            if (string.IsNullOrEmpty(heroId)) return (5, 300);
            switch (ImHeatTracker.TierOf(heroId))
            {
                case ImHeatTier.Hot: return (8, 500);
                case ImHeatTier.Cold: return (2, 100);
                default: return (5, 300);
            }
        }

        /// <summary>D①：写回前容量钳制（纯数据、无 LLM）——动态记忆 FIFO 到上限、永久记忆截断到上限。
        /// RecentHistory 不硬裁（超量只 prompt 变长，等 AddHistory 自然总结）。</summary>
        private static void ClampEntryToCap(NpcMemorySaveEntry e, string heroId)
        {
            var (dCap, pCap) = CapsFor(heroId);
            if (e.DynamicMemories != null && e.DynamicMemories.Count > dCap)
                e.DynamicMemories = e.DynamicMemories.Skip(e.DynamicMemories.Count - dCap).ToList();  // 留最新
            if (e.PermanentMemory != null && e.PermanentMemory.Length > pCap)
                e.PermanentMemory = e.PermanentMemory.Substring(0, pCap);
        }

        /// <summary>读档缓存一个槽（旧存档无 key → json 为空直接跳过，兼容）。
        /// 🔴 不在此查 Hero（时序风险，见 _pendingRestores 注释）——只缓存，GetMemory 时合并。
        /// 加锁：与 SerializeSlot/GetMemory 同一 _dictLock（读档主线程，防御性）。</summary>
        public static void DeserializeSlot(int slot, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<NpcMemorySaveEntry>>(json);
                if (entries == null) return;
                lock (_dictLock)
                {
                    foreach (var entry in entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.HeroId)) continue;
                        _pendingRestores[entry.HeroId] = entry;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MemorySave] DeserializeSlot({slot}) 失败: {ex.Message}");
            }
        }

        /// <summary>GetMemory 创建记忆后合并读档数据（幂等：RestoreFromSave 覆盖；合并后移除防泄漏/防重复）。</summary>
        private static void TryMergePendingRestore(SingNpcMemorySystem memory, string stringId)
        {
            if (memory == null || string.IsNullOrEmpty(stringId)) return;
            lock (_dictLock)
            {
                if (_pendingRestores.TryGetValue(stringId, out var entry))
                {
                    _pendingRestores.Remove(stringId);
                    memory.RestoreFromSave(entry.RecentHistory, entry.DynamicMemories, entry.PermanentMemory,
                        entry.BackgroundStory, entry.Personality, entry.Specialty, entry.ImportantEvents);
                }
            }
        }

        /// <summary>
        /// 🔴 读档入口清空双字典（2026-08-21 plan B）：防跨档残留污染。
        /// _activeMemories：读档后未互动 NPC 的旧世界记忆残留（旧档内容覆盖新档 = 跨档污染）；
        /// _pendingRestores：同进程切档后 A 档独有 NPC 的 pending 条目仍在 → B 世界互动该 NPC 会
        /// 合并 A 的旧记忆（P0 缺口，2026-08-21 评估发现）——必须连 pending 一起清。
        /// 清空后由本次 DeserializeSlot 从新档重新填充；幂等安全，任何时刻调用都是全量清空。
        /// 🔴 clearPendingRestores=false（读档完成事件 OnGameLoadedEvent 用，2026-08-21 实机修复）：
        /// OnGameLoadedEvent 在 SyncData **之后**触发——此时 pending 已被 DeserializeSlot 填充为
        /// 新档权威数据，再清 pending = 读档记忆全丢（实机 14:31:29 面板 History=0 的根因）。
        /// 该时点只需清 _activeMemories（读档期间后台线程重新加回的旧世界条目），pending 必须保留。
        /// </summary>
        public static void ResetActiveMemories(bool clearPendingRestores = true)
        {
            lock (_dictLock)
            {
                _activeMemories.Clear();
                if (clearPendingRestores) _pendingRestores.Clear();
            }
        }

        /// <summary>
        /// 从 Bannerlord 游戏数据中提取真实信息，生成 Prompt
        /// </summary>
        /// 
        public static NPCProfile GenerateHeroProfile(Hero hero)
        {
            var profile = new NPCProfile(hero);
            
            return profile;
        }
        private static NPCProfile GenerateProfileFromGameData(Agent agent)
        {
            Hero hero = null;
            if (agent.Character is CharacterObject character && character.HeroObject != null)
            {
                hero = character.HeroObject;
            }
            var profile = new NPCProfile(hero, agent);
            return profile;            
        }
    }
}
