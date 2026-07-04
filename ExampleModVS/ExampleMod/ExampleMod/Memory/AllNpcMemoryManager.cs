
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
public static class AllNpcMemoryManager
    {
        private static Dictionary<string, SingNpcMemorySystem> _activeMemories = new Dictionary<string, SingNpcMemorySystem>();


        /// <summary>
        /// 获取或创建该 Agent 的记忆系统
        /// </summary>
        public static string GetPlayerDescription(NPCProfile targetNpcProfile)
        {
            if (Hero.MainHero == null) return "一个普通的旅行者。";

            Hero player = Hero.MainHero;
            string playerId = player.StringId;
            var playerMemory  = GetMemory(playerId);
            if(playerMemory!= null)
            {
                return playerMemory.GetPersonaPrompt();
            }

            StringBuilder sb = new StringBuilder();

            sb.Append($"名字：{player.Name}。");
            sb.Append($"身份：{(player.Clan != null ? player.Clan.Name.ToString() : "无家族")}的{(player.IsFemale ? "女武士" : "武士")}。");

            if (player.Clan?.Kingdom != null)
            {
                sb.Append($"效忠于：{player.Clan.Kingdom.Name}。");
            }


            // 简单通用描述
            sb.Append($"荣誉值：{player.GetTraitLevel(DefaultTraits.Honor)}。");
            sb.Append($"目前持有金钱：{player.Gold}。");

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
