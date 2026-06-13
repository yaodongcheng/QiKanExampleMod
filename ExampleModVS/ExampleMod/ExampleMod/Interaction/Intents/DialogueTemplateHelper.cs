using LivingWorldNpcs;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs.Story
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
    /// CSV ID 命名规则: {EventKey}_{Honor}_{Gender}_{Identity}
    /// 查表 fallback: exact → 逐维改 Any → 代码兜底
    ///
    /// 旧 API 保持兼容，内部转调多因素版。
    /// </summary>
    public static class DialogueTemplateHelper
    {
        /// <summary>对抗类：按成败取台词（旧 API，保持兼容）。</summary>
        public static string Get(string dialogueKey, bool success, out string emotion, Hero target, Agent agent)
        {
            return Lookup(dialogueKey + (success ? "_Success" : "_Fail"), out emotion, target, agent, success);
        }

        /// <summary>即时类/话题：按完整 ID 取台词（旧 API，保持兼容）。</summary>
        public static string Get(string id, out string emotion, Hero target, Agent agent)
        {
            return Lookup(id, out emotion, target, agent, true);
        }

        /// <summary>多因素版：按 EventKey + Factors 查 CSV，逐级 fallback。</summary>
        public static string Get(string eventKey, DialogueFactors factors, out string emotion, Hero target = null, Agent agent = null)
        {
            // 构建 fallback ID 列表
            string[] ids = BuildFallbackIds(eventKey, factors);
            string raw = null;
            emotion = "normal";

            foreach (string id in ids)
            {
                try
                {
                    var rec = GameDatabase.Dialogue != null ? GameDatabase.Dialogue.GetByID(id) : null;
                    if (rec != null)
                    {
                        var lines = rec.GetList("Lines");
                        if (lines != null && lines.Count > 0)
                        {
                            raw = lines[MBRandom.RandomInt(lines.Count)];
                            string emo = rec.GetString("Emotion", "normal");
                            if (!string.IsNullOrEmpty(emo)) emotion = emo;
                            break;
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(raw))
            {
                raw = "……（微微颔首）";
                emotion = "normal";
            }

            return ApplyPlaceholders(raw, target, agent);
        }

        /// <summary>构建 fallback ID 链：从精确到宽泛。</summary>
        private static string[] BuildFallbackIds(string eventKey, DialogueFactors f)
        {
            string h = f.Honor.ToString();   // High / Neutral / Low
            string g = f.Gender.ToString();  // Male / Female
            string i = f.Identity.ToString(); // Lord / Soldier / Civilian

            return new[]
            {
                $"{eventKey}_{h}_{g}_{i}",       // exact
                $"{eventKey}_{h}_{g}_Any",       // wildcard identity
                $"{eventKey}_Any_{g}_{i}",       // wildcard honor, keep gender+identity
                $"{eventKey}_Any_{g}_Any",       // wildcard honor+identity, keep gender
                $"{eventKey}_{h}_Any_Any",       // wildcard gender+identity, keep honor
                $"{eventKey}_Any_Any_Any",       // 最宽泛多因素
                eventKey,                         // 裸 key（兼容旧 CSV 里没后缀的条目）
            };
        }

        // ============================================================
        // 内部实现（从旧版迁移）
        // ============================================================

        private static string Lookup(string id, out string emotion, Hero target, Agent agent, bool success)
        {
            emotion = "normal";
            string raw = null;
            try
            {
                var rec = GameDatabase.Dialogue != null ? GameDatabase.Dialogue.GetByID(id) : null;
                if (rec != null)
                {
                    var lines = rec.GetList("Lines");
                    if (lines != null && lines.Count > 0)
                        raw = lines[MBRandom.RandomInt(lines.Count)];
                    string emo = rec.GetString("Emotion", "normal");
                    if (!string.IsNullOrEmpty(emo)) emotion = emo;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(raw))
            {
                raw = success ? "……（微微颔首，似是默许了）" : "……（摇了摇头，并未应允）";
                emotion = success ? "positive" : "negative";
            }
            return ApplyPlaceholders(raw, target, agent);
        }

        private static string ApplyPlaceholders(string raw, Hero target, Agent agent)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string playerName = Hero.MainHero != null && Hero.MainHero.Name != null ? Hero.MainHero.Name.ToString() : "你";
            string npcName = target != null && target.Name != null
                ? target.Name.ToString()
                : (agent != null && agent.Name != null ? agent.Name.ToString() : "对方");
            string world = Settings.Instance != null ? Settings.Instance.WorldDescription : "";
            return raw
                .Replace("{PLAYER}", playerName)
                .Replace("{NPC}", npcName)
                .Replace("{WORLD}", world ?? "")
                .Replace("{TERM_LORD}", "大人");
        }
    }
}
