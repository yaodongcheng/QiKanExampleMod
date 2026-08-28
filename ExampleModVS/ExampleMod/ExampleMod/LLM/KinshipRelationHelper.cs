using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 🔴 2026-08-28（公孙瓒老丈人案 + 用户补全裁定）：亲属关系推导引擎——「关系圈 BFS ≤3 + 语义表」。
    /// 覆盖 玩家↔Hero NPC 之间距离 1~3 的**全部有称谓关系**：岳父/公公/小舅子/大姨子/外甥/侄子/
    /// 舅妈/姑父/姨夫/亲家翁/连襟/妯娌/祖辈/孙辈/表堂亲/继亲 等（语义表 21 类）。
    /// 0 注入裁定：乱婚窗组合（继亲再联合、姻亲的姻亲再联合、表亲的配偶再联合等未入表路径）→ 返回
    /// null，靠「无段=不知道」兜底——LLM 顶多说记不清，不会瞎编称谓（无信息也编 = 认知污染）。
    /// 输出「链式描述」（"你是 X 的弟弟，X 是汤某某的父亲"），不写死称谓词——女玩家娶妻/再婚等
    /// 无序性别场景下死称谓必错，事实链交由 LLM 按语境称呼（用户裁定：距离 3 内必须全部完善）。
    /// 调用链：PromptBuilder.DescribeKinship（深 1 闭集判不成） → 本引擎；旧 DescribeKinshipInLaw
    /// 的 4 情形已并入语义表（key 不变），避免两套逻辑分叉。
    /// 路径模式约定：BFS 从玩家（other）出发，edges[i] = node[i-1]→node[i] 的边序；hist[0] = 玩家邻接
    /// 层中间人，hist.Last() = NPC（npc）。语义表按「深度 2 / 深度 3」两套分支匹配（严防前缀误命中）。
    /// </summary>
    public static class KinshipRelationHelper
    {
        private enum Edge { Spouse, Father, Mother, Child, Sibling }

        /// <summary>玩家（other）↔ NPC（npc）距离 ≤3 的关系推导。无匹配（含超 3 跳/乱婚窗）返回 null。</summary>
        public static string DescribeInLaw(Hero npc, Hero other)
        {
            if (npc == null || other == null || npc == other) return null;
            var path = FindPath(other, npc);
            if (path == null) return null;
            if (path.Count == 2) return MatchDepth2(path);
            if (path.Count == 3) return MatchDepth3(path);
            return null;
        }

        /// <summary>BFS 最短路（先到先得；深度 ≤3 封顶，visited 防环）。返回 P→N 的 (节点,边) 序列。</summary>
        private static List<(Hero node, Edge edge)> FindPath(Hero from, Hero target)
        {
            var visited = new HashSet<Hero> { from };
            var queue = new Queue<(Hero node, List<(Hero node, Edge edge)> path)>();
            queue.Enqueue((from, new List<(Hero node, Edge edge)>()));
            while (queue.Count > 0)
            {
                var (cur, path) = queue.Dequeue();
                if (path.Count >= 3) continue;
                foreach (var (next, edge) in NeighborsOf(cur))
                {
                    if (visited.Contains(next)) continue;
                    var full = new List<(Hero node, Edge edge)>(path) { (next, edge) };
                    if (next == target) return full;
                    visited.Add(next);
                    queue.Enqueue((next, full));
                }
            }
            return null;
        }

        /// <summary>一阶关系圈（Spouse/Father/Mother/Children/Siblings，null 自动跳过）。</summary>
        private static IEnumerable<(Hero node, Edge edge)> NeighborsOf(Hero h)
        {
            if (h.Spouse != null) yield return (h.Spouse, Edge.Spouse);
            if (h.Father != null) yield return (h.Father, Edge.Father);
            if (h.Mother != null) yield return (h.Mother, Edge.Mother);
            foreach (var c in h.Children ?? Enumerable.Empty<Hero>())
                if (c != null) yield return (c, Edge.Child);
            foreach (var s in h.Siblings ?? Enumerable.Empty<Hero>())
                if (s != null) yield return (s, Edge.Sibling);
        }

        // ───────────────────────── 深度 2 ─────────────────────────

        private static string MatchDepth2(List<(Hero node, Edge edge)> path)
        {
            Hero m1 = path[0].node;   // 中间人（玩家邻接层）
            Hero n = path[1].node;    // NPC
            Edge e0 = path[0].edge;
            Edge e1 = path[1].edge;

            // [spouse, father|mother]：岳父/岳母/公公/婆婆（配偶的父母）
            if (e0 == Edge.Spouse && (e1 == Edge.Father || e1 == Edge.Mother))
                return RelSpouseParent(m1, n);
            // [spouse, sibling]：大舅哥/小舅子/大姨子/小姨子（配偶的手足）
            if (e0 == Edge.Spouse && e1 == Edge.Sibling)
                // 本地化：LWN_prompt_kinship_inlaw_sibling（配偶手足链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_inlaw_sibling",
                    ("M", NameOf(m1)), ("SIB", SiblingRoleWord(m1, n)));
            // [child, spouse]：女婿/儿媳（子女的配偶）
            if (e0 == Edge.Child && e1 == Edge.Spouse)
                // 本地化：LWN_prompt_kinship_inlaw_my_spouse_your_child（子女配偶链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_inlaw_my_spouse_your_child",
                    ("M", NameOf(m1)), ("CHILDREN", ChildRoleWord(m1)));
            // [sibling, spouse]：嫂子/姐夫/妹夫/弟媳（手足的配偶）
            if (e0 == Edge.Sibling && e1 == Edge.Spouse)
                // 本地化：LWN_prompt_kinship_inlaw_my_spouse_your_sibling（手足配偶链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_inlaw_my_spouse_your_sibling",
                    ("M", NameOf(m1)), ("SIB", SiblingRoleWord(m1, n)));
            // [father|mother, spouse]：继父/继母（父/母的配偶且非另一亲——另一亲最短路径 1 已被闭集吃掉）
            if ((e0 == Edge.Father || e0 == Edge.Mother) && e1 == Edge.Spouse)
                // 本地化：LWN_prompt_kinship_stepparent（继亲链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_stepparent",
                    ("M", NameOf(m1)), ("PTH", ParentRoleWord(e0)));
            // [spouse, child]：继子女（配偶的孩子且非本人亲生——亲生者最短路径 1 已被闭集吃掉）
            if (e0 == Edge.Spouse && e1 == Edge.Child)
                // 本地化：LWN_prompt_kinship_spouse_child（继子/继女链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_spouse_child",
                    ("M", NameOf(m1)));
            // [sibling, child]：外甥/侄子/甥女/侄女（手足的孩子）
            if (e0 == Edge.Sibling && e1 == Edge.Child)
                // 本地化：LWN_prompt_kinship_niece_nephew（甥侄链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_niece_nephew",
                    ("M", NameOf(m1)), ("SIB", SiblingRoleWord(m1, n)));
            // [father|mother, sibling]：舅舅/姨妈/姑母/叔伯（父/母的手足）
            if ((e0 == Edge.Father || e0 == Edge.Mother) && e1 == Edge.Sibling)
                // 本地化：LWN_prompt_kinship_parent_sibling（父/母手足链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_parent_sibling",
                    ("M", NameOf(m1)), ("PTH", ParentRoleWord(e0)), ("SIB", SiblingRoleWord(n, m1)));
            // [child, child]：孙辈（子女的孩子）
            if (e0 == Edge.Child && e1 == Edge.Child)
                // 本地化：LWN_prompt_kinship_grandchild（孙辈链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_grandchild",
                    ("M", NameOf(m1)), ("CHILDREN", ChildRoleWord(m1)));
            // [father|mother, father|mother]：祖辈（父/母的父母）
            if ((e0 == Edge.Father || e0 == Edge.Mother) && (e1 == Edge.Father || e1 == Edge.Mother))
                // 本地化：LWN_prompt_kinship_grandparent（祖辈链，双桶）
                return RelGrandparent("LWN_prompt_kinship_grandparent", m1, e0, e1);
            return null;
        }

        // ───────────────────────── 深度 3 ─────────────────────────

        private static string MatchDepth3(List<(Hero node, Edge edge)> path)
        {
            Hero m1 = path[0].node;
            Hero m2 = path[1].node;
            Hero n = path[2].node;
            Edge e0 = path[0].edge;
            Edge e1 = path[1].edge;
            Edge e2 = path[2].edge;

            // [father|mother, sibling, spouse]：舅妈/姑父/姨夫/伯母/婶婶（父/母的手足的配偶）
            if ((e0 == Edge.Father || e0 == Edge.Mother) && e1 == Edge.Sibling && e2 == Edge.Spouse)
                // 本地化：LWN_prompt_kinship_parent_sibling_spouse（舅妈族链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_parent_sibling_spouse",
                    ("M", NameOf(m2)), ("PTH", ParentRoleWord(e0)), ("SIB", SiblingRoleWord(m2, m1)));
            // [father|mother, sibling, child]：表/堂兄弟姐妹（父/母的手足的孩子）
            if ((e0 == Edge.Father || e0 == Edge.Mother) && e1 == Edge.Sibling && e2 == Edge.Child)
                // 本地化：LWN_prompt_kinship_cousin（表堂亲链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_cousin",
                    ("M", NameOf(m2)), ("PTH", ParentRoleWord(e0)), ("SIB", SiblingRoleWord(m2, m1)));
            // [spouse, sibling, spouse]：连襟/妯娌（配偶的手足的配偶）
            if (e0 == Edge.Spouse && e1 == Edge.Sibling && e2 == Edge.Spouse)
                // 本地化：LWN_prompt_kinship_spouse_sibling_spouse（连襟/妯娌链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_spouse_sibling_spouse",
                    ("M", NameOf(m2)), ("SIB", SiblingRoleWord(m2, m1)));
            // [child, spouse, father|mother]：亲家翁/亲家母（子女的配偶的父母）
            if (e0 == Edge.Child && e1 == Edge.Spouse && (e2 == Edge.Father || e2 == Edge.Mother))
                // 本地化：LWN_prompt_kinship_co_parent（亲家链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_co_parent",
                    ("M", NameOf(m2)), ("PTH", ParentRoleWord(e2)), ("CHILDREN", ChildRoleWord(m1)));
            // [spouse, sibling, child]：配偶的甥侄（配偶的手足的孩子）
            if (e0 == Edge.Spouse && e1 == Edge.Sibling && e2 == Edge.Child)
                // 本地化：LWN_prompt_kinship_spouse_niece_nephew（配偶甥侄链，双桶）
                return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_spouse_niece_nephew",
                    ("M", NameOf(m2)), ("SIB", SiblingRoleWord(m2, m1)));
            // [spouse, father|mother, father|mother]：配偶的祖辈（配偶的父/母的父母）
            if (e0 == Edge.Spouse && (e1 == Edge.Father || e1 == Edge.Mother)
                && (e2 == Edge.Father || e2 == Edge.Mother))
                // 本地化：LWN_prompt_kinship_spouse_grandparent（配偶祖辈链，双桶）
                return RelGrandparent("LWN_prompt_kinship_spouse_grandparent", m2, e1, e2);
            return null;
        }

        // ───────────────────────── 词与组合器 ─────────────────────────

        /// <summary>名字兜底：null/空名 → null（不产出坏链）。</summary>
        private static string NameOf(Hero h)
        {
            if (h == null) return null;
            return h.Name?.ToString();
        }

        private static string ParentRoleWord(Edge edge)
        {
            // 本地化：LWN_word_kin_role_father（父词，双桶）
            string father = LWNTextHelper.ResolvePrompt("LWN_word_kin_role_father");
            // 本地化：LWN_word_kin_role_mother（母词，双桶）
            string mother = LWNTextHelper.ResolvePrompt("LWN_word_kin_role_mother");
            return edge == Edge.Father ? father : mother;
        }

        private static string ChildRoleWord(Hero h)
        {
            // 本地化：LWN_word_kin_role_daughter（女儿词，双桶）
            string daughter = LWNTextHelper.ResolvePrompt("LWN_word_kin_role_daughter");
            // 本地化：LWN_word_kin_role_son（儿子词，双桶）
            string son = LWNTextHelper.ResolvePrompt("LWN_word_kin_role_son");
            return h.IsFemale ? daughter : son;
        }

        /// <summary>手足词：a 相对 b 的年长（哥哥/姐姐或弟弟/妹妹）。</summary>
        private static string SiblingRoleWord(Hero a, Hero b)
        {
            // 本地化：LWN_word_kin_elder_sis（姐姐词，双桶）
            string elderSis = LWNTextHelper.ResolvePrompt("LWN_word_kin_elder_sis");
            // 本地化：LWN_word_kin_younger_sis（妹妹词，双桶）
            string youngerSis = LWNTextHelper.ResolvePrompt("LWN_word_kin_younger_sis");
            // 本地化：LWN_word_kin_elder_bro（哥哥词，双桶）
            string elderBro = LWNTextHelper.ResolvePrompt("LWN_word_kin_elder_bro");
            // 本地化：LWN_word_kin_younger_bro（弟弟词，双桶）
            string youngerBro = LWNTextHelper.ResolvePrompt("LWN_word_kin_younger_bro");
            bool elder = a.Age >= b.Age;
            string wordFemale = elder ? elderSis : youngerSis;
            string wordMale = elder ? elderBro : youngerBro;
            return a.IsFemale ? wordFemale : wordMale;
        }

        /// <summary>配偶的父母（你是我孩子的配偶）：{M}=配偶名。</summary>
        private static string RelSpouseParent(Hero spouseNode, Hero npc)
        {
            string m = NameOf(spouseNode);
            if (string.IsNullOrEmpty(m)) return null;
            // 本地化：LWN_prompt_kinship_inlaw_child（岳父/公公链，双桶）
            return LWNTextHelper.ResolveCompound("LWN_prompt_kinship_inlaw_child",
                ("M", m), ("CHILDREN", ChildRoleWord(spouseNode)));
        }

        /// <summary>祖辈（你是 {M} 的{PTH1}，{M}是{NAME}的{PTH2}；配偶祖辈变体第二段带"的配偶"）：
        /// {M}=子辈名（父/母名），{PTH1}=我相对 M 的父/母词，{PTH2}=M 相对玩家的父/母词。</summary>
        private static string RelGrandparent(string key, Hero midNode, Edge edgeToMe, Edge edgeToPlayer)
        {
            string m = NameOf(midNode);
            if (string.IsNullOrEmpty(m)) return null;
            return LWNTextHelper.ResolveCompound(key,
                ("M", m), ("PTH1", ParentRoleWord(edgeToMe)), ("PTH2", ParentRoleWord(edgeToPlayer)));
        }
    }
}
