using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 意图注册表：替代以前的 RegisterAllOptions 大方法。加新意图只需在 RegisterDefaults 里 Register 一行。
    /// 仿 Story 引擎 CommandManager.RegisterAll 的注册式范式。
    /// </summary>
    public static class IntentRegistry
    {
        private static readonly List<IntentBase> _all = new List<IntentBase>();
        private static bool _initialized = false;

        public static void Register(IntentBase intent)
        {
            if (intent != null) _all.Add(intent);
        }

        /// <summary>按意图类名查找（用于 INTENT:xxx 委托）</summary>
        public static IntentBase FindByName(string name)
        {
            EnsureInitialized();
            return _all.FirstOrDefault(i =>
                i.GetType().Name.Equals(name, System.StringComparison.OrdinalIgnoreCase) ||
                i.GetType().Name.Equals($"{name}Intent", System.StringComparison.OrdinalIgnoreCase));
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            RegisterDefaults();
        }

        private static void RegisterDefaults()
        {
            // ── 社交/个人 ──
            Register(new ProposeMarriageIntent());
            Register(new GiftIntent());
            Register(new TeaCeremonyIntent());
            Register(new SparIntent());
            // ── 外交/人事（对抗类）──
            Register(new RecruitWandererIntent());   // 登庸流浪者
            Register(new DefectEnemyIntent());        // 劝诱敌将倒戈
            Register(new BetrayalIntent());           // 同阵营策反
            Register(new RequestFundsIntent());       // 请求军资
            Register(new RequestWorkIntent());        // 仕官
            // ── 委托/找工作 ──
            Register(new RequestCommissionIntent());   // 看告示板/直接接委托（已重写为接入原版 Quest）
            // Register(new ConfirmCommissionIntent());   // [已废弃] 从告示板找委托人确认——原版无两段式流程
            Register(new CollectCommissionRewardIntent()); // 目标完成后，找结账人领报酬
            // ── 公务/通用 ──
            Register(new InfoIntent());               // 查看情报
            Register(new RecruitSoldierIntent());     // 普通平民应募入伍（花钱+魅力砍价）
            Register(new PersuadeSurrenderIntent()); // 劝降敌方士兵
            Register(new OrderSoldierIntent());       // 命令士兵
            Register(new FollowIntent());             // 跟随
            Register(new ChatIntent());               // 寒暄/话题
            Register(new ExpandOptionsIntent());      // 危机时展开折叠的选项
            Register(new LeaveIntent());              // 离开（永远最后）

            // ── 犯罪追责 Intent ──
            Register(new PayRestitutionIntent());
            Register(new CharmDefenseIntent());
            Register(new FrameSuspectIntent());
            Register(new ThreatIntent());
            Register(new InvestigateIntent());
            Register(new ConfessIntent());
            Register(new WalkAwayIntent());
            Register(new SilenceWitnessIntent());
            Register(new LeadRetaliationIntent());
            Register(new WorkOffDebtIntent());
            Register(new FightVillagersIntent());
            Register(new BetrayQuestIntent());
            Register(new InnocenceProofIntent());
            Register(new SettleIntent());
            Register(new AcceptBountyQuestIntent());
            Register(new LureArrestIntent());
            Register(new ArrestIntent());
            Register(new SurrenderJailIntent());
            Register(new ComplyIntent());
            Register(new ContinueChatIntent());
            Register(new ReturnStolenItemsIntent());

            // ═══ 战斗投降 ═══
            Register(new PlayerSurrenderPayIntent());
            Register(new PlayerSurrenderBegIntent());
            Register(new PlayerSurrenderThreatenIntent());
            Register(new ResolveNpcSurrenderIntent());
            Register(new FightOnIntent());

            // ═══ 系统 Intent（旧式 Action 迁移） ═══
            Register(new IncreaseRelationIntent());
            Register(new DecreaseRelationIntent());
            Register(new GiveGoldIntent());
            Register(new TakeGoldIntent());
        }

        /// <summary>资格层：产出当前可见（含置灰）的意图，隐藏的过滤掉。</summary>
        public static List<KeyValuePair<IntentBase, Eligibility>> GetVisible(IntentContext ctx)
        {
            EnsureInitialized();
            var result = new List<KeyValuePair<IntentBase, Eligibility>>();
            foreach (var intent in _all)
            {
                Eligibility e;
                try { e = intent.Evaluate(ctx); }
                catch { e = Eligibility.Hide(); }   // 单个意图判定异常不连累整个菜单
                if (e.State != EligState.Hidden)
                    result.Add(new KeyValuePair<IntentBase, Eligibility>(intent, e));
            }
            return result;
        }
    }

    /// <summary>单次检定计算结果。</summary>
    public struct RollResult
    {
        public float Chance;        // 成功率 0..1（点击前显示）
        public float Threshold;     // 难度阈值
        public NegotiationState State;
        public string Log;
    }

    /// <summary>
    /// 太阁5 式单次检定：把「一回合一次性清空谈判进度条」当作成功率。
    /// 复用 NegotiationState（难度/开局优势/性格抗性）、SkillCheckSystem（技能胜率）、
    /// NegotiationRegistry.CalculateMultiplier（性格倍率）——不新增第三套公式。
    /// </summary>
    public static class SingleRollResolver
    {
        // 公式权重（可调）：技能贡献 30%，献礼/出价 70%。
        private const float SkillWeight = 0.30f;
        private const float OfferWeight = 0.70f;

        public static RollResult Compute(IntentContext ctx, NegotiationGoalType goal, NegotiationTactic tactic, float offerValue)
        {
            var r = new RollResult();
            Hero npc = ctx.Speaker;
            string desc = NegotiationRegistry.GetGoalInfo(goal).Name;

            // 复用难度计算器（只读 阈值/开局优势/性格 三项，不跑回合循环）
            // Agent-based path for Mission scenes; Hero-based fallback for campaign-map conversations
            NegotiationState state = ctx.Agent != null
                ? new NegotiationState(ctx.Agent, goal.ToString(), desc)
                : new NegotiationState(ctx.Speaker, goal.ToString(), desc);
            r.State = state;
            r.Threshold = state.TargetThreshold;

            float skillWin = npc != null
                ? MathF.Clamp(SkillCheckSystem.CalculateSkillCheck(Hero.MainHero, npc, tactic).WinChance, 0f, 1f)
                : 0.5f;

            float offerFactor = state.TargetThreshold > 0f ? offerValue / state.TargetThreshold : 0f;
            offerFactor = MathF.Clamp(offerFactor, 0f, 1f);

            // 性格倍率：构造一张无筹码的虚拟卡（CostAmount=0 → 不创建 Chip，规避 P1 弹消息副作用），只取手段修正
            NegotiationCard card = new NegotiationCard(tactic.ToString(), desc);
            float traitMult = NegotiationRegistry.CalculateMultiplier(card, state);

            float reach = (SkillWeight * skillWin + OfferWeight * offerFactor) * traitMult;
            float finalProg = state.CurrentProgress + reach * state.TargetThreshold;
            float chance = state.TargetThreshold > 0f ? finalProg / state.TargetThreshold : 0f;
            r.Chance = MathF.Clamp(chance, 0.02f, 0.95f);

            r.Log = $"[单次检定] 目标={goal} 阈值={state.TargetThreshold:0} 开局={state.CurrentProgress:0} " +
                    $"技能胜率={skillWin:0.00} 献礼占比={offerFactor:0.00} 性格倍率={traitMult:0.00} → 成功率={r.Chance:0.00}";
            return r;
        }

        /// <summary>实际掷骰。</summary>
        public static bool Roll(float chance)
        {
            return MBRandom.RandomFloat < chance;
        }

        /// <summary>
        /// 简单版单次检定：只比较双方同一技能的等级，不依赖 NegotiationState。
        /// 用于大地图对话 / 犯罪 Intent 等无 Agent 的场景。
        /// </summary>
        /// <param name="ctx">Intent 上下文（Hero 可能为 null）</param>
        /// <param name="tactic">手段 → 映射到对应技能</param>
        /// <param name="offerValue">0..1 献礼/证物加成</param>
        public static RollResult SimpleCompute(IntentContext ctx, NegotiationTactic tactic, float offerValue = 0f)
        {
            var r = new RollResult();
            Hero npc = ctx.Speaker;
            SkillObject skill = SkillCheckSystem.MapTacticToSkill(tactic);

            float playerLevel = Hero.MainHero.GetSkillValue(skill);
            float npcLevel = npc?.GetSkillValue(skill) ?? 50f; // 缺值默认 50

            // 基础胜率：同技能比拼
            float baseChance = playerLevel / (playerLevel + npcLevel);

            // 献礼加成（技能 70% + 献礼 30%）
            float offerBonus = MathF.Clamp(offerValue, 0f, 1f);
            float chance = baseChance * 0.7f + offerBonus * 0.3f;

            r.Chance = MathF.Clamp(chance, 0.05f, 0.95f);
            r.State = null;   // 不依赖 NegotiationState
            r.Threshold = 0f;
            r.Log = $"[单次检定] 手段={tactic} 技能={skill.Name} 你的等级={playerLevel:F0} 对方等级={npcLevel:F0} 献礼={offerBonus:0.00} → 成功率={r.Chance:0.00}";

            return r;
        }
    }
}
