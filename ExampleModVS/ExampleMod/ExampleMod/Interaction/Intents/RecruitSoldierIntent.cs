using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
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
        // 招募意图名：花钱招募平民入伍
        public override string DisplayName { get { return LWNTextHelper.ResolveText("LWN_intent_recruit_name", "Recruit: Enlist"); } }
        // 招募意图提示：花钱招募本地平民，荣誉高可打折
        public override string ToolTip { get { return LWNTextHelper.ResolveText("LWN_intent_recruit_tooltip", "Pay to recruit local civilians as soldiers (high honor earns a discount)"); } }

        public override Eligibility Evaluate(IntentContext ctx)
        {
            if (ctx == null) return Eligibility.Hide();
            if (ctx.HasUrgentWorldEvent && !ctx.ExpandedOptions) return Eligibility.Hide();
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
                string line = DialogueTemplateHelper.Get("RecruitSoldier_AlreadyRecruited", factors, out emotion, ctx.Speaker, ctx.Agent);
                // 🔴 统一说话框架 + M4 双轨润色：招募对话台词（前因=spoken_to）
                SpeechChannel.SayPolished(ctx.Agent, line, SpeechPriority.Dialogue,
                    SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(ctx.Agent), Agent.Main, "spoken_to", "招募"));
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
                return;
            }

            // ── 兜底：小孩 ──
            if (ctx.IsChild)
            {
                string line;
                string emotion;
                var factors = DialogueFactors.FromContext(ctx);
                line = DialogueTemplateHelper.Get("RecruitSoldier_TooYoung", factors, out emotion, ctx.Speaker, ctx.Agent);
                ctx.Controller.ShowNpcLineKeepMenu(ctx.Agent, line, emotion);
                return;
            }

            CharacterObject co = ctx.Agent.Character as CharacterObject;
            CultureObject culture = co != null ? co.Culture as CultureObject : null;
            CharacterObject troop = culture != null ? culture.BasicTroop : null;
            if (troop == null)
            {
                // 招募失败：该文化没有可招募的基础兵种
                InformationManager.DisplayMessage(new InformationMessage(LWNTextHelper.ResolveText("LWN_intent_recruit_msg_no_troop", "There is no one to enlist here.")));
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
            // 🔴 2026-08-13（d20 风格全局统一）：掷点 ≥ 目标阈值成功（目标 = 1 − 成功率），概率不变
            bool haggled = MBRandom.RandomFloat >= (1f - charmChance);
            float charmDiscount = haggled ? MathF.Clamp(0.25f + charm / 400f, 0.25f, 0.75f) : 0f;
            int finalCost = (int)(afterHonor * (1f - charmDiscount));
            if (finalCost < 0) finalCost = 0;

            if (Hero.MainHero.Gold < finalCost)
            {
                // 招募失败：钱不够（报出所需金额）
                InformationManager.DisplayMessage(new InformationMessage(
                    // 招募 {TROOP} 需 {COST} 第纳尔，你的钱不够。
                    LWNTextHelper.ResolveCompound("LWN_intent_recruit_msg_no_gold",
                        "Recruiting {TROOP} costs {COST} denars — you don't have enough.",
                        ("TROOP", troop.Name.ToString()), ("COST", finalCost.ToString())), Colors.Red));
                return;
            }

            // ── 确认弹窗 ──
            // 声望折扣附注（荣誉高时出现，可为空，作为片段注入确认文案）
            string honorHint = honor > 0
                // （声望折扣 {PERCENT}%）
                ? LWNTextHelper.ResolveCompound("LWN_intent_recruit_hint_honor", " (reputation discount {PERCENT}%)", ("PERCENT", (honor * 2).ToString()))
                : "";
            // 砍价成功附注（可为空，作为片段注入确认文案）
            string charmHint = haggled
                //  讨价还价成功！
                ? LWNTextHelper.ResolveText("LWN_intent_recruit_hint_haggle", " Haggled successfully!")
                : "";
            InformationManager.ShowInquiry(new InquiryData(
                // 招募确认弹窗标题
                LWNTextHelper.ResolveText("LWN_intent_recruit_inquiry_title", "Enlist"),
                // 招募确认正文：价格 + 折扣附注 + 是否招募（语序由 XML 控制）
                LWNTextHelper.ResolveCompound("LWN_intent_recruit_inquiry_prompt",
                    "Recruiting {TROOP} costs {COST} denars{HONOR}{CHARM}. Recruit?",
                    ("TROOP", troop.Name.ToString()), ("COST", finalCost.ToString()),
                    ("HONOR", honorHint), ("CHARM", charmHint)),
                true, true,
                // 招募确认按钮：确认
                LWNTextHelper.ResolveText("LWN_intent_recruit_btn_yes", "Recruit"),
                // 招募确认按钮：取消
                LWNTextHelper.ResolveText("LWN_intent_recruit_btn_no", "Never mind"),
                () =>
                {
                    if (finalCost > 0) AgentControlHelper.TransferGold(Hero.MainHero, null, finalCost, false);
                    MobileParty.MainParty.MemberRoster.AddToCounts(troop, 1);
                    Hero.MainHero.AddSkillXp(DefaultSkills.Charm, 10);

                    // 成交价描述：砍价成功 / 按标准价（片段，供外层模板注入）
                    string priceDesc = haggled
                        // 经一番说和 只花了 {COST}
                        ? LWNTextHelper.ResolveCompound("LWN_intent_recruit_price_haggle",
                            "after some talk, only {COST}",
                            ("COST", finalCost.ToString()))
                        // 按例付了 {COST}
                        : LWNTextHelper.ResolveCompound("LWN_intent_recruit_price_standard",
                            "paid the usual {COST}",
                            ("COST", finalCost.ToString()));
                    // 成交价附注：原价与声望折扣（荣誉高时出现，可为空）
                    if (honor > 0)
                        // （原价 20，声望折 {PERCENT}%）
                        priceDesc += LWNTextHelper.ResolveCompound("LWN_intent_recruit_price_honor_note",
                            " (original 20, {PERCENT}% off by reputation)",
                            ("PERCENT", (honor * 2).ToString()));
                    // 招募成功消息：兵种应募入伍 + 成交价描述
                    InformationManager.DisplayMessage(new InformationMessage(
                        // {TROOP} 应募入伍！{PRICE} 第纳尔。
                        LWNTextHelper.ResolveCompound("LWN_intent_recruit_success_msg",
                            "{TROOP} enlisted! {PRICE} denars.",
                            ("TROOP", troop.Name.ToString()), ("PRICE", priceDesc)), Colors.Green));

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
                        farewell = DialogueTemplateHelper.Get("RecruitSoldier_Female", factors, out emotion, ctx.Speaker, ctx.Agent);
                    else
                        farewell = DialogueTemplateHelper.Get("RecruitHero", true, out emotion, ctx.Speaker, ctx.Agent);
                    if (!string.IsNullOrEmpty(farewell))
                    {
                        // 🔴 统一说话框架 + M4 双轨润色：招募成功告别台词（前因=spoken_to）
                        SpeechChannel.SayPolished(ctx.Agent, farewell, SpeechPriority.Dialogue,
                            SpeechContext.FromBrain(AgentAIController.GetBrainForAgent(ctx.Agent), Agent.Main, "spoken_to", "招募"));
                    }

                    ctx.Controller._vm.Close();
                    if (Agent.Main != null)
                        AgentAIController.Instance.BroadcastEventInRange(Agent.Main.Position, 15.0f, "EndInteraction", false, Agent.Main);
                    GroupStageManager.Reset(Agent.Main);
                }, null));
        }
    }
}
