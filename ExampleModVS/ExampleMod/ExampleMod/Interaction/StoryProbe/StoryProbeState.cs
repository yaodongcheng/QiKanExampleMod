using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>序章阶段（= 进城次数）。</summary>
    public enum StoryProbePhase
    {
        /// <summary>第一次进城：只有守门人，武力检定强制失败 → 进牢。</summary>
        FirstAttempt = 0,
        /// <summary>牢里：等玩家换装（旧衣 + 面罩），换完才回到城门。</summary>
        Jail = 1,
        /// <summary>第二次进城：完整序章（四家 + 6 起事件 + 目标点）。</summary>
        Second = 2,
        /// <summary>序章完成（走到门内目标点，钱已归还）。</summary>
        Done = 3,
    }

    /// <summary>三种能力（钱是第四手段，不参与对拼）。</summary>
    public enum StoryAbility
    {
        Charm = 0,
        Guile = 1,
        Force = 2,
    }

    /// <summary>
    /// 序章状态（2026-11-12，plans/逆水寒511玩法验证-城池剧情任务.md）。
    /// 单一数据源：一切"玩家走到哪一步"的判断都读这里；存档走 MyBehavior.SyncData 的
    /// `lwn_story_probe` 键（JSON blob），新档由 ResetAllCampaignState 清空。
    ///
    /// 🔴 只放**需要跨 Mission / 跨存档**的东西。Mission 内的临时状态（当前事件、当前对话节点、
    /// 已生成 Agent 引用）放 StoryProbeMissionBehavior / StreetLifeDirector 的实例字段。
    /// </summary>
    [Serializable]
    public class StoryProbeStateData
    {
        // ── 阶段 ──
        public int Phase = (int)StoryProbePhase.FirstAttempt;
        public bool TutorialLost;          // 第一次已演完必败教学
        public bool JailDone;              // 牢里换装已完成
        public bool JailSceneEntered;      // 已在真实牢内站点醒来，不是仅打开城镇菜单
        public int PendingTransition;      // 0=无，1=进入牢房，2=换装后回门外（应用层消费）
        public string TransitionSettlementId = "";
        public string StorySettlementId = ""; // 整轮剧情的城，读档后也能识别 city 设置变化
        // 首遇的临时真实技能增强需跨存档恢复；第二次沿用同 Hero，不能把强化带过去。
        public string FirstGuardBoostHeroId = "";
        public Dictionary<string, int> FirstGuardOriginalSkills = new Dictionary<string, int>();
        public bool IntroPlayed;           // 第二次进场随从 3 句已播
        public bool GoalReached;           // 已走到门内目标点
        public string GoalStage = "enter"; // 屏幕目标（enter/asked/quest/tool/done）

        // ── nobody（剥夺）──
        public bool Stripped;              // 身上那套已被没收；有尚欠账时保持为 true
        public int EscrowVersion;          // 1 = 有逐槽销账；旧档在首次恢复时迁移
        public bool ConfiscationComplete;
        public List<int> PendingBattleRestoreSlots = new List<int>();
        public List<int> PendingCivilianRestoreSlots = new List<int>();
        public bool CancellationPending;   // 停止剧情后仍有归还/技能恢复未成功；禁止重新入场
        public int DebtCollected;          // debtor 实际交到玩家手中的钱，不是要求金额
        public int DebtRepaid;             // 已实际交还给当铺的账款，重试只补尚欠
        public int PawnbrokerRewardPaid;    // 30 跑腿费中实际已付部分
        public Dictionary<string, List<string>> RewardReceipts = new Dictionary<string, List<string>>();
        public int GoldBeforeStrip;        // 尚欠没收款；每次实际归还后减少，结清为 0
        public List<string> SavedBattleEquip = new List<string>();   // 没收前的战斗装备（槽位顺序，"" = 空）
        public List<string> SavedCivilianEquip = new List<string>(); // 没收前的民用装备
        public List<string> SavedBattleModifiers = new List<string>();
        public List<string> SavedCivilianModifiers = new List<string>();
        public double StripAtHours;        // 剥夺发生的时刻（R3 兜底归还用）
        public bool Disguised;             // 遮面（旧衣 + 面罩）
        public bool NobleDisguise;         // 贵族制服 + 通行文书
        public string DisguiseHeroId = ""; // 冒充的是谁（本阶段只存 id 与次数）
        public int DisguiseFrameCount;     // 冒充犯法的次数

        // ── 进度 ──
        public List<string> Asked = new List<string>();          // "<npc>:<tag>"（问过的不再注入）
        public Dictionary<string, int> NpcStep = new Dictionary<string, int>(); // 0..5（五步关系）
        public List<string> QuestAccepted = new List<string>();  // npcId
        public List<string> QuestDone = new List<string>();      // npcId（目标已完成，可交差）
        public List<string> QuestTurnedIn = new List<string>();  // npcId
        public List<string> Clues = new List<string>();          // sister_1..4
        public List<string> Hazards = new List<string>();        // 隐患 flag
        public string ChosenApproach = "";                       // charm/guile/force/money
        public bool ApproachResolved;                           // 过门路线后果已经结算，防止重入重复给奖
        public bool GateOpened;                                  // 门已经让开了（跨 Mission 记住）
        public int TalentPoints;                                 // 天赋点（第二阶段才用）
        public int FactionFixers;                                // 四家好感（自己记账，见 §9）
        public int FactionNightwalkers;
        public int FactionPlayers;
        public int FactionDockhands;

        // ── 运行时锚点 ──
        public Dictionary<string, string> SpawnedHeroIds = new Dictionary<string, string>(); // npcId → Hero.StringId
        public string CompanionHeroId = "";     // 第二次进场招的随从
        public int BribePaid;                   // 打点付了多少（用于 status）
        public int CoinsFound;                  // 零活与收账实际到账累计，不含要求但未到账的金额
        public double NextErrandHour;           // 下一次零活可接的时刻（CampaignTime 小时）

        // ── 调试（不进存档：这是"下一次掷点"的一次性旋钮，带进存档会污染下一局）──
        [JsonIgnore] public float RollOverride = -1f;
        [JsonIgnore] public string RollOverrideAbility = "";
    }

    /// <summary>
    /// 序章状态的静态门面：读写 <see cref="Data"/>，并提供"问过没有 / 第几步 / 委托什么状态"这类
    /// 统一判断。业务代码禁止直接读 Data 里的 List/Dictionary（口径会分叉）。
    /// </summary>
    public static class StoryProbeState
    {
        private static StoryProbeStateData _data = new StoryProbeStateData();

        public static StoryProbeStateData Data => _data ?? (_data = new StoryProbeStateData());

        // 不持久化：读档/取消/重置都使旧 Mission、弹窗和延迟回调失效。
        public static int RuntimeToken { get; private set; }
        public static void InvalidateRuntime() { unchecked { RuntimeToken++; } }
        public static bool IsRuntimeCurrent(int token)
            => token == RuntimeToken && !Data.CancellationPending && StoryProbeSettings.Enabled;

        public static bool HasRewardReceipt(string npcId, string key)
            => Data.RewardReceipts.TryGetValue(npcId, out var receipts) && receipts != null && receipts.Contains(key);

        public static void RecordRewardReceipt(string npcId, string key)
        {
            if (!Data.RewardReceipts.TryGetValue(npcId, out var receipts) || receipts == null)
                Data.RewardReceipts[npcId] = receipts = new List<string>();
            if (!receipts.Contains(key)) receipts.Add(key);
        }

        /// <summary>本次进场是否由「入城（玩法测试）」菜单触发。**不存档**（transient）。</summary>
        public static bool EnterViaOption { get; set; }
        public static bool EnterJailViaOption { get; set; }

        public static StoryProbePhase Phase
        {
            get => (StoryProbePhase)Data.Phase;
            set => Data.Phase = (int)value;
        }

        public static void ResetAll()
        {
            InvalidateRuntime();
            _data = new StoryProbeStateData();
            EnterViaOption = false;
            EnterJailViaOption = false;
        }

        // ── 问过 / 步数 ──

        public static bool HasAsked(string npcId, string tag)
            => !string.IsNullOrEmpty(npcId) && !string.IsNullOrEmpty(tag)
               && Data.Asked.Contains(npcId + ":" + tag);

        public static void MarkAsked(string npcId, string tag)
        {
            string key = npcId + ":" + tag;
            if (!Data.Asked.Contains(key)) Data.Asked.Add(key);
            if (Step(npcId) < 2) SetStep(npcId, 2);
        }

        public static int Step(string npcId)
            => npcId != null && Data.NpcStep.TryGetValue(npcId, out int v) ? v : 0;

        public static void SetStep(string npcId, int step)
        {
            if (string.IsNullOrEmpty(npcId)) return;
            if (Step(npcId) >= step) return;   // 只前进，不后退（闲聊不会把熟人降回陌生人）
            Data.NpcStep[npcId] = step;
        }

        public static void MarkMet(string npcId) => SetStep(npcId, Math.Max(1, Step(npcId)));

        // ── 委托 ──

        public static bool IsQuestAccepted(string npcId) => Data.QuestAccepted.Contains(npcId);
        public static bool IsQuestDone(string npcId) => Data.QuestDone.Contains(npcId);
        public static bool IsQuestTurnedIn(string npcId) => Data.QuestTurnedIn.Contains(npcId);

        public static void AcceptQuest(string npcId)
        {
            if (!Data.QuestAccepted.Contains(npcId)) Data.QuestAccepted.Add(npcId);
            SetStep(npcId, 3);
            SetGoal("quest");
        }

        public static void CompleteQuest(string npcId)
        {
            if (!Data.QuestDone.Contains(npcId)) Data.QuestDone.Add(npcId);
        }

        public static void TurnInQuest(string npcId)
        {
            if (!Data.QuestTurnedIn.Contains(npcId)) Data.QuestTurnedIn.Add(npcId);
            SetStep(npcId, 4);
        }

        // ── 线索 / 隐患 / 目标 ──

        public static bool HasClue(string id) => Data.Clues.Contains(id);

        public static void AddClue(string id)
        {
            if (!Data.Clues.Contains(id)) Data.Clues.Add(id);
        }

        public static void AddHazard(string id)
        {
            if (!string.IsNullOrEmpty(id) && !Data.Hazards.Contains(id)) Data.Hazards.Add(id);
        }

        public static bool HasHazard(string id) => Data.Hazards.Contains(id);

        public static string Goal => Data.GoalStage ?? "enter";

        /// <summary>屏幕目标只前进不后退（enter → asked → quest → tool → done）。</summary>
        public static void SetGoal(string stage)
        {
            string[] order = { "enter", "asked", "quest", "tool", "done" };
            int cur = Array.IndexOf(order, Goal);
            int next = Array.IndexOf(order, stage);
            if (next <= cur) return;
            Data.GoalStage = stage;
            // §6.8：屏幕左上一句话，随进度换一行（只在真的换阶段时播一次）
            try { StoryProbeScreenGoal.Show(); } catch { }
        }

        // ── 势力好感（§9：走 ChangeRelationAction 记到代表英雄身上，这里留一份可读账）──

        public static int GetFactionRelation(string factionId)
        {
            switch (factionId)
            {
                case "fixers": return Data.FactionFixers;
                case "nightwalkers": return Data.FactionNightwalkers;
                case "players": return Data.FactionPlayers;
                case "dockhands": return Data.FactionDockhands;
                default: return 0;
            }
        }

        public static void AddFactionRelation(string factionId, int delta)
        {
            switch (factionId)
            {
                case "fixers": Data.FactionFixers += delta; break;
                case "nightwalkers": Data.FactionNightwalkers += delta; break;
                case "players": Data.FactionPlayers += delta; break;
                case "dockhands": Data.FactionDockhands += delta; break;
            }
        }

        // ── 存档 ──

        public static string Serialize()
        {
            try { return JsonConvert.SerializeObject(Data); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[StoryProbe] Serialize failed: {ex.Message}");
                return null;
            }
        }

        public static void Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var loaded = JsonConvert.DeserializeObject<StoryProbeStateData>(json);
                if (loaded != null)
                {
                    _data = loaded;
                    // 老存档的 null 字段兜底（Newtonsoft 会保留声明初值，但手改/半截文件可能给 null）
                    _data.Asked ??= new List<string>();
                    _data.NpcStep ??= new Dictionary<string, int>();
                    _data.QuestAccepted ??= new List<string>();
                    _data.QuestDone ??= new List<string>();
                    _data.QuestTurnedIn ??= new List<string>();
                    _data.Clues ??= new List<string>();
                    _data.Hazards ??= new List<string>();
                    _data.SpawnedHeroIds ??= new Dictionary<string, string>();
                    _data.FirstGuardOriginalSkills ??= new Dictionary<string, int>();
                    _data.SavedBattleEquip ??= new List<string>();
                    _data.SavedCivilianEquip ??= new List<string>();
                    _data.SavedBattleModifiers ??= new List<string>();
                    _data.SavedCivilianModifiers ??= new List<string>();
                    _data.PendingBattleRestoreSlots ??= new List<int>();
                    _data.PendingCivilianRestoreSlots ??= new List<int>();
                    _data.RewardReceipts ??= new Dictionary<string, List<string>>();
                }
                InvalidateRuntime();
                EnterJailViaOption = false;
                EnterViaOption = false;
                DebugLogger.Log($"[StoryProbe] Loaded state: phase={LoadedPhaseName()} step={string.Join(",", _data.NpcStep.Select(kv => kv.Key + "=" + kv.Value))}");
            }
            catch (Exception ex)
            {
                // 反序列化失败不能把尚欠账和守卫原技能备份抹掉。
                DebugLogger.Log($"[StoryProbe] Deserialize failed (keeping existing escrow): {ex.Message}");
                InvalidateRuntime();
                EnterViaOption = false;
                EnterJailViaOption = false;
            }
        }

        private static string LoadedPhaseName() => ((StoryProbePhase)Data.Phase).ToString();

        // ── 状态摘要（status 命令 / 日志共用，英文，控制台可读）──

        public static string Describe()
        {
            var d = Data;
            return "phase=" + Phase
                 + " gameKeys=" + StoryProbeSettings.Enabled
                 + " stage=" + d.GoalStage
                 + " tutorialLost=" + d.TutorialLost
                 + " jailDone=" + d.JailDone
                 + " intro=" + d.IntroPlayed
                 + " goal=" + d.GoalReached
                 + " stripped=" + d.Stripped
                 + " disguised=" + d.Disguised
                 + " noble=" + d.NobleDisguise
                 + " escrowGoldOwed=" + d.GoldBeforeStrip
                 + " escrowSlots[battle=" + d.PendingBattleRestoreSlots.Count + ",civilian=" + d.PendingCivilianRestoreSlots.Count + "]"
                 + " cancelPending=" + d.CancellationPending
                 + " token=" + RuntimeToken
                 + " debt[collected=" + d.DebtCollected + ",repaid=" + d.DebtRepaid + ",fee=" + d.PawnbrokerRewardPaid + "]"
                 + " talent=" + d.TalentPoints
                 + " approach=" + (string.IsNullOrEmpty(d.ChosenApproach) ? "-" : d.ChosenApproach)
                 + " rel[fixers=" + d.FactionFixers + ",night=" + d.FactionNightwalkers
                 + ",players=" + d.FactionPlayers + ",dock=" + d.FactionDockhands + "]";
        }

        public static string DescribeSteps()
        {
            var d = Data;
            var parts = new List<string>();
            foreach (var def in StoryProbeDefs.Npcs)
            {
                parts.Add(def.Id + "=" + Step(def.Id)
                    + (IsQuestAccepted(def.Id) ? "/accepted" : "")
                    + (IsQuestDone(def.Id) ? "/done" : "")
                    + (IsQuestTurnedIn(def.Id) ? "/turned" : ""));
            }
            return string.Join(" ", parts);
        }

        public static int ClueCount => Data.Clues.Count;
        public static int AskedCount => Data.Asked.Count;
    }

    /// <summary>序章可调开关的单一读取口（config.json，见 Settings.StoryProbeEnabled）。</summary>
    public static class StoryProbeSettings
    {
        public static bool Enabled => Settings.Instance != null && Settings.Instance.StoryProbeEnabled;
        public static string SettlementId =>
            Settings.Instance == null || string.IsNullOrWhiteSpace(Settings.Instance.StoryProbeSettlementId)
                ? "town_EN1"
                : Settings.Instance.StoryProbeSettlementId.Trim();
    }
}
