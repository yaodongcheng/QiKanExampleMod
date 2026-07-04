using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>荣誉段 — 给台词框架用。</summary>
    public enum HonorLevel { High, Neutral, Low }

    /// <summary>NPC 性别。</summary>
    public enum NpcGender { Male, Female }

    /// <summary>NPC 身份。</summary>
    public enum NpcIdentity { Lord, Soldier, Civilian }

    /// <summary>
    /// 台词选择因素：EventKey + Honor + Gender + Identity → CSV 查表。
    /// </summary>
    public struct DialogueFactors
    {
        public HonorLevel Honor;
        public NpcGender Gender;
        public NpcIdentity Identity;

        /// <summary>从 IntentContext + 当前 settlement 荣誉构建。</summary>
        public static DialogueFactors FromContext(IntentContext ctx)
        {
            var f = new DialogueFactors();

            // 荣誉：取当前 settlement 的荣誉值
            int honor = 0;
            if (Hero.MainHero != null && Hero.MainHero.CurrentSettlement != null)
                honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);
            f.Honor = honor >= 5 ? HonorLevel.High : (honor <= -5 ? HonorLevel.Low : HonorLevel.Neutral);

            // 性别
            if (ctx != null && ctx.IsHero && ctx.Hero != null)
                f.Gender = ctx.Hero.IsFemale ? NpcGender.Female : NpcGender.Male;
            else if (ctx != null && ctx.Agent != null && ctx.Agent.Character != null)
                f.Gender = ctx.Agent.Character.IsFemale ? NpcGender.Female : NpcGender.Male;
            else
                f.Gender = NpcGender.Male;

            // 身份
            if (ctx != null && ctx.IsHero && ctx.Hero != null)
            {
                if (ctx.Hero.IsLord) f.Identity = NpcIdentity.Lord;
                else f.Identity = NpcIdentity.Civilian;
            }
            else if (ctx != null && ctx.IsMySoldier)
                f.Identity = NpcIdentity.Soldier;
            else
                f.Identity = NpcIdentity.Civilian;

            return f;
        }
    }

    /// <summary>
    /// 多因素台词框架：升级版 DialogueTemplateHelper。
    ///
    /// 后端已切至 NarrativeResolver（查 Narrative.csv，维度渐进 fallback）。
    /// 公共 API 保持兼容，旧调用点无需改动。
    /// </summary>
    public static class DialogueTemplateHelper
    {
        /// <summary>对抗类：按成败取台词（旧 API，保持兼容）。</summary>
        public static string Get(string dialogueKey, bool success, out string emotion, Hero target, Agent agent)
        {
            return NarrativeResolver.GetDialogue(dialogueKey, success, out emotion, target, agent);
        }

        /// <summary>即时类/话题：按完整 ID 取台词（旧 API，保持兼容）。</summary>
        public static string Get(string id, out string emotion, Hero target, Agent agent)
        {
            return NarrativeResolver.GetDialogue(id, out emotion, target, agent);
        }

        /// <summary>多因素版：按 EventKey + Factors 查 CSV，逐级 fallback。</summary>
        public static string Get(string eventKey, DialogueFactors factors, out string emotion, Hero target = null, Agent agent = null)
        {
            return NarrativeResolver.GetDialogue(eventKey, factors, out emotion, target, agent);
        }
    }
}
