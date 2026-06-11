using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 招募入伍：普通模板 NPC（村民/镇民）最大的价值——花钱招为己方士兵。
    /// 复用骑砍2 募兵价（PartyWageModel）招「对方文化的基础兵」(culture.BasicTroop)，
    /// 「特殊」机制 = 魅力砍价：一次魅力检定，成功打折(甚至免费)，失败原价。
    /// 即时类（Goal=null），砍价检定在 OnInstant 内自行处理。
    /// </summary>
    public class RecruitSoldierIntent : IntentBase
    {
        public override InteractionOptionType Type { get { return InteractionOptionType.RecruitSoldier; } }
        public override string DisplayName { get { return "【招募】 应募入伍"; } }
        public override string ToolTip { get { return "花钱招募此地平民为兵（魅力高可砍价）"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            return ctx.IsRecruitableCivilian ? Eligibility.Show() : Eligibility.Hide();
        }

        public override void OnInstant(IntentContext ctx)
        {
            CharacterObject co = ctx.Agent != null ? ctx.Agent.Character as CharacterObject : null;
            CultureObject culture = co != null ? co.Culture as CultureObject : null;
            CharacterObject troop = culture != null ? culture.BasicTroop : null;
            if (troop == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("此人无从应募。"));
                return;
            }

            int baseCost = Campaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(troop, Hero.MainHero);
            if (baseCost < 1) baseCost = 10;

            // 魅力砍价：一次检定（成功打折，魅力越高折越多；失败原价）
            int charm = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float chance = MathF.Clamp(0.30f + charm * 0.003f, 0.05f, 0.90f);
            bool haggled = MBRandom.RandomFloat < chance;
            int finalCost = baseCost;
            if (haggled)
            {
                float discount = MathF.Clamp(0.25f + charm / 400f, 0.25f, 0.75f);
                finalCost = (int)(baseCost * (1f - discount));
                if (finalCost < 0) finalCost = 0;
            }

            if (Hero.MainHero.Gold < finalCost)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"招募 {troop.Name} 需 {finalCost} 第纳尔，你的钱不够。", Colors.Red));
                return;
            }

            // 确认弹窗
            string priceHint = haggled ? $"（原价 {baseCost}）" : "";
            InformationManager.ShowInquiry(new InquiryData(
                "应募入伍",
                $"招募 {troop.Name} 需要 {finalCost} 第纳尔{priceHint}，是否招募？",
                true, true, "招募", "算了",
                () =>
                {
                    if (finalCost > 0) AgentControlHelper.TransferGold(Hero.MainHero, null, finalCost, false);
                    MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Charm, 10);

                    string priceDesc = haggled
                        ? $"经一番说和 只花了 {finalCost}（原价 {baseCost}）"
                        : $"按例付了 {finalCost}";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{troop.Name} 应募入伍！{priceDesc} 第纳尔。", Colors.Green));

                    ctx.Controller._vm.Close();
                    if (Agent.Main != null)
                        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
                    GroupStageManager.Reset(Agent.Main);
                }, null));
        }
    }
}
