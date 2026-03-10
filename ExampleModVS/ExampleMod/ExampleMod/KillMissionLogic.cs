using TaleWorlds.MountAndBlade;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ExampleMod
{
    internal class KillMissionLogic : MissionLogic
    {
        private const int HealValue = 20;
        //当有角色从战场上移除的时候触发
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            if(IsValidKill(affectedAgent,affectorAgent) && affectedAgent.IsHuman && IsPlayer(affectorAgent))
            {
                float newHealth = MathF.Clamp(affectorAgent.Health + HealValue, 0, affectorAgent.HealthLimit);
                affectorAgent.Health = newHealth;
            }
            base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, blow);
            
        }
        //验证击杀是否有效,并且不是自杀
        private bool IsValidKill(Agent affectedAgent, Agent affectorAgent)
        {
            return affectedAgent != null && affectorAgent != null && affectedAgent != affectorAgent;
        }
        //验证是否是玩家
        private bool IsPlayer(Agent agent)
        {
            return agent != null && agent.IsHero && (agent.Character as CharacterObject).HeroObject == Hero.MainHero;
        }
    }
}