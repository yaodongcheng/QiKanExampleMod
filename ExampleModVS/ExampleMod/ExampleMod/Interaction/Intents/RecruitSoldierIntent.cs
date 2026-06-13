using System.Collections.Generic;
using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
{
    /// <summary>
    /// 招募入伍：普通模板 NPC（村民/镇民）花钱招为己方士兵。
    /// 基础价 20 第纳尔，荣誉打折 + 魅力砍价叠加。
    /// 即时类（Goal=null），砍价检定在 OnInstant 内自行处理。
    /// </summary>
    public class RecruitSoldierIntent : IntentBase
    {
        // 已招募追踪（运行时，场景销毁即失效）
        private static HashSet<int> _recruitedAgents = new HashSet<int>();

        public override InteractionOptionType Type { get { return InteractionOptionType.RecruitSoldier; } }
        public override string DisplayName { get { return "【招募】 应募入伍"; } }
        public override string ToolTip { get { return "花钱招募此地平民为兵（荣誉高可打折）"; } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx == null) return Eligibility.Hide();
            if (ctx.IsChild) return Eligibility.Hide();
            if (!ctx.IsRecruitableCivilian) return Eligibility.Hide();
            // 已招募仍可点击——会进 OnInstant 走台词 bubblesay 路线
            if (ctx.Agent != null && _recruitedAgents.Contains(ctx.Agent.Index))
                return Eligibility.Show();
            return Eligibility.Show();
        }

        public override void OnInstant(IntentContext ctx)
        {
            if (ctx == null || ctx.Agent == null) return;

            // ── 兜底：已招募 ──
            if (_recruitedAgents.Contains(ctx.Agent.Index))
            {
                string emotion;
                var factors = DialogueFactors.FromContext(ctx);
                string line = DialogueTemplateHelper.Get("RecruitSoldier_AlreadyRecruited", factors, out emotion, ctx.Hero, ctx.Agent);
                BubbleSayMissionView.AgentBubbleSay(ctx.Agent, line);
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
                return;
            }

            // ── 兜底：小孩 ──
            if (ctx.IsChild)
            {
                string line;
                string emotion;
                var factors = DialogueFactors.FromContext(ctx);
                line = DialogueTemplateHelper.Get("RecruitSoldier_TooYoung", factors, out emotion, ctx.Hero, ctx.Agent);
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
                return;
            }

            CharacterObject co = ctx.Agent.Character as CharacterObject;
            CultureObject culture = co != null ? co.Culture as CultureObject : null;
            CharacterObject troop = culture != null ? culture.BasicTroop : null;
            if (troop == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("此人无从应募。"));
                return;
            }

            // ── 荣誉打折 ──
            int honor = 0;
            if (Hero.MainHero.CurrentSettlement != null)
                honor = SettlementHonorStore.Get(Hero.MainHero.CurrentSettlement);
            int baseCost = 20;
            float honorDiscount = MathF.Clamp(honor * 0.02f, 0f, 0.50f); // 每点荣誉 2%，最高 50%
            int afterHonor = (int)(baseCost * (1f - honorDiscount));
            if (afterHonor < 5) afterHonor = 5;

            // ── 魅力砍价 ──
            int charm = Hero.MainHero.GetSkillValue(DefaultSkills.Charm);
            float charmChance = MathF.Clamp(0.30f + charm * 0.003f, 0.05f, 0.90f);
            bool haggled = MBRandom.RandomFloat < charmChance;
            float charmDiscount = haggled ? MathF.Clamp(0.25f + charm / 400f, 0.25f, 0.75f) : 0f;
            int finalCost = (int)(afterHonor * (1f - charmDiscount));
            if (finalCost < 0) finalCost = 0;

            if (Hero.MainHero.Gold < finalCost)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    $"招募 {troop.Name} 需 {finalCost} 第纳尔，你的钱不够。", Colors.Red));
                return;
            }

            // ── 确认弹窗 ──
            string honorHint = honor > 0 ? $"（声望折扣 {honor * 2}%）" : "";
            string charmHint = haggled ? $" 讨价还价成功！" : "";
            InformationManager.ShowInquiry(new InquiryData(
                "应募入伍",
                $"招募 {troop.Name} 需要 {finalCost} 第纳尔{honorHint}{charmHint}，是否招募？",
                true, true, "招募", "算了",
                () =>
                {
                    if (finalCost > 0) AgentControlHelper.TransferGold(Hero.MainHero, null, finalCost, false);
                    MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Charm, 10);

                    string priceDesc = haggled
                        ? $"经一番说和 只花了 {finalCost}"
                        : $"按例付了 {finalCost}";
                    if (honor > 0) priceDesc += $"（原价 20，声望折 {honor * 2}%）";
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"{troop.Name} 应募入伍！{priceDesc} 第纳尔。", Colors.Green));

                    // 追踪已招募
                    _recruitedAgents.Add(ctx.Agent.Index);

                    // 据点荣誉 +1
                    if (Hero.MainHero.CurrentSettlement != null)
                        SettlementHonorStore.Modify(Hero.MainHero.CurrentSettlement, 1);

                    // NPC 台词 bubble（女性→木兰台词，男性→效劳台词）
                    var factors = DialogueFactors.FromContext(ctx);
                    string emotion;
                    string farewell;
                    if (ctx.Agent.Character != null && ctx.Agent.Character.IsFemale)
                        farewell = DialogueTemplateHelper.Get("RecruitSoldier_Female", factors, out emotion, ctx.Hero, ctx.Agent);
                    else
                        farewell = DialogueTemplateHelper.Get("RecruitHero", true, out emotion, ctx.Hero, ctx.Agent);
                    if (!string.IsNullOrEmpty(farewell))
                        BubbleSayMissionView.AgentBubbleSay(ctx.Agent, farewell);

                    ctx.Controller._vm.Close();
                    if (Agent.Main != null)
                        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", Agent.Main);
                    GroupStageManager.Reset(Agent.Main);
                }, null));
        }
    }
}
