
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
                ? m.DynamicMemories.Select(x => new RecentMemory(x.Content, x.TimeStamp_Start, x.TimeStamp_End, x.CampaignDay)).ToList()
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
            if (_activeMemories.ContainsKey(stringId))
            {
                return _activeMemories[stringId];
            }

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

            // 如果内存里已经有这个人的脑子了，直接返回
            if (_activeMemories.ContainsKey(uniqueId))
            {
                return _activeMemories[uniqueId];
            }

            // 否则，创建一个新脑子
            NPCProfile profile = GenerateProfileFromGameData(agent);
            SingNpcMemorySystem newMemory = new SingNpcMemorySystem(profile);

            _activeMemories[uniqueId] = newMemory;
            return newMemory;
        }
        public static void ClearTemporaryMemories()
        {
            var keysToRemove = _activeMemories.Keys.Where(k => k.StartsWith("TEMP_AGENT_")).ToList();
            foreach (var key in keysToRemove)
            {
                _activeMemories.Remove(key);
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

        /// <summary>序列化一个槽：Hero 记忆（StringId 稳定），惰性跳过全空；TEMP 模板不存（键含 agent.Index 不稳定）。</summary>
        public static string SerializeSlot(int slot)
        {
            var entries = new List<NpcMemorySaveEntry>();
            foreach (var kv in _activeMemories)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Key.StartsWith("TEMP_AGENT_")) continue;
                if ((StableHash(kv.Key) & 0x7FFFFFFF) % SaveSlots != slot) continue;

                var m = kv.Value;
                if (m == null) continue;
                // 惰性：无任何内容的记忆不写盘（含人设三字段——只有人设也要存）
                if ((m.RecentHistory == null || m.RecentHistory.Count == 0)
                    && (m.DynamicMemories == null || m.DynamicMemories.Count == 0)
                    && (m.PermanentMemory == null || m.PermanentMemory.Length == 0)
                    && string.IsNullOrEmpty(m.BackgroundStory)
                    && string.IsNullOrEmpty(m.Personality)
                    && string.IsNullOrEmpty(m.Specialty)
                    && (m.ImportantEvents == null || m.ImportantEvents.Count == 0))
                    continue;

                entries.Add(new NpcMemorySaveEntry(kv.Key, m));
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(entries);
        }

        /// <summary>读档缓存一个槽（旧存档无 key → json 为空直接跳过，兼容）。
        /// 🔴 不在此查 Hero（时序风险，见 _pendingRestores 注释）——只缓存，GetMemory 时合并。</summary>
        public static void DeserializeSlot(int slot, string json)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(json)) return;
                var entries = Newtonsoft.Json.JsonConvert.DeserializeObject<List<NpcMemorySaveEntry>>(json);
                if (entries == null) return;
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.HeroId)) continue;
                    _pendingRestores[entry.HeroId] = entry;
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
            if (_pendingRestores.TryGetValue(stringId, out var entry))
            {
                _pendingRestores.Remove(stringId);
                memory.RestoreFromSave(entry.RecentHistory, entry.DynamicMemories, entry.PermanentMemory,
                    entry.BackgroundStory, entry.Personality, entry.Specialty, entry.ImportantEvents);
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
