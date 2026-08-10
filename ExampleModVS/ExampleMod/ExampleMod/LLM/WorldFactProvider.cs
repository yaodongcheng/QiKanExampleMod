using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 动态知识注入（轻量 RAG，2026-08-10）：玩家消息命中「数据主题」时，实时查询游戏状态
    /// 拼成事实段注入 prompt；未命中返回空串（平时零开销、零注入）。
    /// 架构：**主题注册表**——每条主题 = 触发词表 + 可见性 + 查询函数，覆盖「游戏内有数据的任何情况」；
    /// 新增主题 = 在 Topics 注册一条，不堆 if。**问句兜底**——关键词全没命中但玩家在问问题时，
    /// 注入一份轻量世界概要（队伍/金钱/声望/领地/季节），漏网的问题也能拿到基础事实。
    /// 叙事铁律：事实按可见性裁剪——同行者隐私（队伍/金钱/位置/粮草/俘虏/伤员/任务）只注入给
    /// 队伍成员（队伍频道/随从私聊，他们亲历）；普世事实（领地/声望/家族/战争/时间——地图可见、
    /// 人尽皆知）任何会话可注入。LLM 拿到的是「NPC 亲眼所见/人尽皆知」，非上帝视角。
    /// 铁律 5：全部数据从 Campaign 对象实时读取（MobileParty.MainParty / Hero / Clan / Kingdom /
    /// CampaignTime / QuestManager），无硬编码 ID。
    /// </summary>
    public static class WorldFactProvider
    {
        private sealed class FactTopic
        {
            public string Id;
            public string Title;           // prompt 段标题（prompt 内容豁免本地化铁律，与 BuildPrompt_ImReply 中文风格一致）
            public string[] Keywords;      // 触发词（中英双语，对齐 ImTopicMatcher 惯例）
            public bool NeedsPartyMember;  // true = 仅队伍成员可见（同行者隐私）；false = 普世事实
            public Func<string> Query;
        }

        /// <summary>主题注册表：覆盖「游戏内有数据的任何情况」的常见主题；新增主题 = 追加一条。</summary>
        private static readonly List<FactTopic> Topics = new List<FactTopic>
        {
            new FactTopic
            {
                Id = "party", Title = "## 队伍现状（你亲眼所见，如实作答）", NeedsPartyMember = true,
                Keywords = new[] { "队伍", "部队", "兵力", "人数", "人马", "士兵", "弟兄", "兄弟", "手下", "多少人",
                    "army", "troop", "troops", "soldier", "soldiers", "manpower", "men", "party", "companion", "companions" },
                Query = QueryPartyFacts,
            },
            new FactTopic
            {
                Id = "gold", Title = "## 钱财（队伍共有，你大致知晓）", NeedsPartyMember = true,
                Keywords = new[] { "钱", "金币", "第纳尔", "金子", "积蓄", "身家", "盘缠", "富", "穷",
                    "gold", "coin", "coins", "money", "denar", "denars", "wealth", "poor", "rich" },
                Query = QueryGoldFact,
            },
            new FactTopic
            {
                Id = "location", Title = "## 当前位置（你与主人同行，亲眼所见）", NeedsPartyMember = true,
                Keywords = new[] { "在哪", "位置", "何处", "何方", "哪里", "身在哪", "where", "location", "position" },
                Query = QueryLocationFact,
            },
            new FactTopic
            {
                Id = "food", Title = "## 粮草（队伍辎重，你亲眼所见）", NeedsPartyMember = true,
                Keywords = new[] { "粮食", "食物", "口粮", "补给", "粮草", "food", "supply", "supplies", "provision", "provisions", "rations" },
                Query = QueryFoodFact,
            },
            new FactTopic
            {
                Id = "prisoner", Title = "## 俘虏（队伍押解，你亲眼所见）", NeedsPartyMember = true,
                Keywords = new[] { "俘虏", "囚犯", "战俘", "prisoner", "prisoners", "captive", "captives" },
                Query = QueryPrisonerFact,
            },
            new FactTopic
            {
                Id = "wounded", Title = "## 伤员（队伍军医处，你亲眼所见）", NeedsPartyMember = true,
                Keywords = new[] { "伤员", "伤兵", "受伤", "wounded", "injured", "hurt" },
                Query = QueryWoundedFact,
            },
            new FactTopic
            {
                Id = "fief", Title = "## 领地（地图明示，人尽皆知）", NeedsPartyMember = false,
                Keywords = new[] { "领地", "封地", "城镇", "城堡", "村庄", "辖区",
                    "fief", "fiefs", "town", "castle", "village", "land", "lands" },
                Query = QueryFiefFact,
            },
            new FactTopic
            {
                Id = "renown", Title = "## 声望与影响力（坊间传颂，半公开）", NeedsPartyMember = false,
                Keywords = new[] { "声望", "影响力", "名誉", "威名", "名气", "renown", "influence", "fame" },
                Query = QueryRenownFact,
            },
            new FactTopic
            {
                Id = "family", Title = "## 家族（亲缘关系，普世可知）", NeedsPartyMember = false,
                Keywords = new[] { "家族", "家人", "亲人", "配偶", "妻子", "丈夫", "孩子", "儿女", "儿子", "女儿",
                    "family", "clan", "wife", "husband", "child", "children", "kid", "kids" },
                Query = QueryFamilyFact,
            },
            new FactTopic
            {
                Id = "war", Title = "## 王国与战事（驿站流言，人尽皆知）", NeedsPartyMember = false,
                Keywords = new[] { "王国", "战争", "开战", "敌对", "交战", "停战", "和平", "敌人", "盟国",
                    "kingdom", "war", "enemy", "enemies", "allies", "peace", "battle" },
                Query = QueryWarFact,
            },
            new FactTopic
            {
                Id = "time", Title = "## 时日（普世可知）", NeedsPartyMember = false,
                Keywords = new[] { "今天", "几号", "几月", "季节", "日期", "何时", "日子", "时辰", "是日",
                    "today", "date", "season", "month", "day", "when" },
                Query = QueryTimeFact,
            },
            new FactTopic
            {
                Id = "quest", Title = "## 委托（主公的事务，你略有耳闻）", NeedsPartyMember = true,
                Keywords = new[] { "任务", "委托", "差事", "悬赏", "quest", "quests", "issue", "issues", "errand", "errands" },
                Query = QueryQuestFact,
            },
            new FactTopic
            {
                Id = "skill", Title = "## 主公的武艺与本事（队伍成员朝夕相处，看得真切）", NeedsPartyMember = true,
                Keywords = new[] { "剑术", "刀法", "箭术", "弓术", "骑术", "战术", "医术", "技能", "本领", "武艺", "武艺如何",
                    "skill", "skills", "sword", "archery", "riding", "medicine", "tactics", "warrior" },
                Query = QuerySkillFact,
            },
            new FactTopic
            {
                Id = "level", Title = "## 主公的历练（队伍成员看在眼里）", NeedsPartyMember = true,
                Keywords = new[] { "几级", "等级", "经验", "历练", "level", "experience", "xp" },
                Query = QueryLevelFact,
            },
            new FactTopic
            {
                Id = "business", Title = "## 产业（主公的营生，随行者略有耳闻）", NeedsPartyMember = true,
                Keywords = new[] { "商队", "工坊", "产业", "生意", "收入", "铺子", "caravan", "workshop", "business", "income", "shop" },
                Query = QueryBusinessFact,
            },
            new FactTopic
            {
                Id = "morale", Title = "## 士气（队伍上下精神头，你亲眼所见）", NeedsPartyMember = true,
                Keywords = new[] { "士气", "军心", "人心", "精神头", "低迷", "morale", "spirit", "morale low", "morale high" },
                Query = QueryMoraleFact,
            },
            new FactTopic
            {
                Id = "garrison", Title = "## 驻军（城头旗帜与甲兵，人尽皆知）", NeedsPartyMember = false,
                Keywords = new[] { "驻军", "守军", "守备", "城防", "garrison", "defenders", "guards" },
                Query = QueryGarrisonFact,
            },
        };

        /// <summary>玩家文本命中知识主题 → 返回事实段（多行，可直接拼入 prompt）；未命中返回空串（零注入）。</summary>
        public static string BuildFactsForIm(string playerText, bool isPartyMember)
        {
            if (string.IsNullOrWhiteSpace(playerText)) return "";
            // 🔴 实体优先：文本命中已知 Hero 的属性问题（在哪/关系/几岁）→ 走实体查询，
            // 不再落进主题表（根治"拉盖娅在哪"命中队伍位置主题的答非所问）
            var eq = ResolveEntityQuery(playerText);
            if (eq != null)
            {
                var sb = new StringBuilder();
                string fact = QueryEntityFact(eq);
                if (!string.IsNullOrWhiteSpace(fact))
                {
                    sb.AppendLine(GetEntityTitle(eq));
                    sb.AppendLine(fact);
                    sb.AppendLine();
                }
                return sb.ToString();
            }
            var body = new StringBuilder();
            bool anyHit = false;
            foreach (var t in Topics)
            {
                // 🔴 可见性裁剪：同行者隐私仅队伍成员（他们亲历；外人问了也不知道）
                if (t.NeedsPartyMember && !isPartyMember) continue;
                if (ContainsAny(playerText, t.Keywords))
                {
                    anyHit = true;
                    body.AppendLine(t.Title);
                    body.AppendLine(t.Query());
                    body.AppendLine();
                }
            }
            // 问句兜底：关键词全没命中但玩家在问 → 轻量世界概要（有数据的问题至少拿到基础事实可答）
            if (!anyHit && IsQuestion(playerText))
            {
                body.AppendLine("## 世界概要（你知晓的普世事实）");
                body.AppendLine(QuerySummary(isPartyMember));
                body.AppendLine();
            }
            return body.ToString();
        }

        // ── 实体层：指定 Hero 的属性查询（在哪/关系/几岁）──

        /// <summary>实体属性识别结果：目标 Hero + 要查的属性。</summary>
        private sealed class EntityQuery
        {
            public Hero Hero;
            public string Property;   // "location" / "relation" / "age"
        }

        /// <summary>实体属性触发词（中英双语；"拉盖娅在哪"→ location）。</summary>
        private static readonly string[] EntityLocationKeywords =
        {
            "在哪", "位置", "何处", "何方", "哪里", "行踪", "身在", "下落",
            "where", "location", "position", "whereabouts", "here",
        };
        private static readonly string[] EntityRelationKeywords =
        {
            "关系", "交情", "友好", "好感", "讨厌", "喜欢", "仇", "敌视", "怎么看我",
            "relation", "relationship", "friendship", "like", "dislike", "enemy", "friend",
        };
        private static readonly string[] EntityAgeKeywords =
        {
            "几岁", "多大", "年龄", "岁数", "多大了",
            "age", "how old", "years old",
        };

        /// <summary>称号表：玩家文本中的称呼 → 指向的英雄（v1：国王/族长两级）。</summary>
        private static readonly string[] TitleSovereignKeywords =
        {
            "陛下", "国王", "女王", "王上", "国主", "monarch", "king", "queen", "majesty", "sovereign",
        };
        private static readonly string[] TitleClanLeaderKeywords =
        {
            "首领", "族长", "头领", "当家的", "chief", "clan leader", "head of clan",
        };

        /// <summary>识别实体属性问题：文本命中已知 Hero 名 + 属性词 → 返回查询；否则 null。</summary>
        private static EntityQuery ResolveEntityQuery(string text)
        {
            try
            {
                var hero = FindHeroInText(text);
                if (hero == null) return null;
                if (ContainsAny(text, EntityLocationKeywords)) return new EntityQuery { Hero = hero, Property = "location" };
                if (ContainsAny(text, EntityRelationKeywords)) return new EntityQuery { Hero = hero, Property = "relation" };
                if (ContainsAny(text, EntityAgeKeywords)) return new EntityQuery { Hero = hero, Property = "age" };
            }
            catch { }
            return null;
        }

        /// <summary>人名 → Hero：遍历存活英雄匹配 FirstName/Name（本地化名字，中英文环境通用）；称号表兜底。</summary>
        private static Hero FindHeroInText(string text)
        {
            foreach (var hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero == Hero.MainHero) continue;
                string firstName = hero.FirstName?.ToString();
                string fullName = hero.Name?.ToString();
                if (!string.IsNullOrEmpty(firstName) && firstName.Length >= 2 &&
                    text.IndexOf(firstName, StringComparison.OrdinalIgnoreCase) >= 0) return hero;
                if (!string.IsNullOrEmpty(fullName) && fullName.Length >= 2 && fullName != firstName &&
                    text.IndexOf(fullName, StringComparison.OrdinalIgnoreCase) >= 0) return hero;
            }
            // 称号 → 君主/族长（玩家无王国/家族时自然为 null）
            if (ContainsAny(text, TitleSovereignKeywords))
            {
                var kingdom = Clan.PlayerClan?.Kingdom;
                if (kingdom != null && kingdom.Leader != null) return kingdom.Leader;
            }
            if (ContainsAny(text, TitleClanLeaderKeywords))
            {
                var clan = Clan.PlayerClan;
                if (clan != null && clan.Leader != null) return clan.Leader;
            }
            return null;
        }

        /// <summary>实体事实段标题（按属性给"信息来源"标注，LLM 措辞贴合来源级别）。</summary>
        private static string GetEntityTitle(EntityQuery eq)
        {
            string name = eq.Hero?.Name?.ToString() ?? "对方";
            return eq.Property switch
            {
                "location" => $"## {name}的行踪（流言与见闻）",
                "relation" => $"## 主公与{name}的交情（你随侍在侧，看在眼里）",
                "age" => $"## {name}的年岁（坊间皆知）",
                _ => $"## {name}的情况",
            };
        }

        private static string QueryEntityFact(EntityQuery eq)
        {
            if (eq?.Hero == null) return null;
            try
            {
                switch (eq.Property)
                {
                    case "location": return QueryHeroLocationFact(eq.Hero);
                    case "relation": return QueryHeroRelationFact(eq.Hero);
                    case "age": return QueryHeroAgeFact(eq.Hero);
                }
            }
            catch { }
            return null;
        }

        /// <summary>实体位置（🔴 情报分级）：与玩家交战的阵营 → 传闻级（不给精确下落）；否则定居点/行军目标级。
        /// 分级是 C# 确定性逻辑（王国比对 + IsAtWarWith），LLM 只拿分级后的文本做措辞。</summary>
        private static string QueryHeroLocationFact(Hero hero)
        {
            string where = null;
            var party = hero.PartyBelongedTo;
            if (party != null)
            {
                if (party.CurrentSettlement != null) where = $"正在 {party.CurrentSettlement.Name}";
                else if (party.TargetSettlement != null) where = $"正行军前往 {party.TargetSettlement.Name}";
            }
            if (where == null && hero.CurrentSettlement != null) where = $"正在 {hero.CurrentSettlement.Name}";
            if (where == null)
                return $"- {hero.Name} 行踪不定，无人知晓其确切下落。";

            if (IsAtWarWithPlayer(hero))
                // 敌国：传闻级（交战国的军情是机密，精确下落不可知）
                return $"- 传闻 {hero.Name} 正在领兵在外，行踪难料。";
            return $"- {hero.Name} 眼下{where}。";
        }

        /// <summary>玩家阵营 vs 目标阵营是否交战（双方无王国 → 非交战）。</summary>
        private static bool IsAtWarWithPlayer(Hero hero)
        {
            var playerKingdom = Clan.PlayerClan?.Kingdom;
            var heroKingdom = hero.Clan?.Kingdom;
            if (playerKingdom == null || heroKingdom == null || playerKingdom == heroKingdom) return false;
            try { return playerKingdom.IsAtWarWith(heroKingdom); } catch { return false; }
        }

        /// <summary>实体关系：数值 → 形容词档位（数值给 LLM 作依据，措辞由 LLM 转化）。</summary>
        private static string QueryHeroRelationFact(Hero hero)
        {
            int rel = Hero.MainHero.GetRelation(hero);
            string level;
            if (rel >= 50) level = "挚友";
            else if (rel >= 20) level = "友好";
            else if (rel >= -5) level = "中立";
            else if (rel >= -30) level = "反感";
            else level = "仇视";
            return $"- 主公与 {hero.Name} 的交情：{level}（情谊 {rel}）。";
        }

        /// <summary>实体年龄（普世）。</summary>
        private static string QueryHeroAgeFact(Hero hero)
        {
            return $"- {hero.Name} 年约 {hero.Age:0} 岁。";
        }

        // ── 查询函数（全部实时读 Campaign 对象；异常兜底「无从查知」防崩）──

        private static string QueryPartyFacts()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            int regulars = party.MemberRoster.TotalRegulars;
            int heroes = party.MemberRoster.TotalHeroes;
            var sb = new StringBuilder();
            sb.AppendLine($"- 队伍现有 {regulars} 名士兵、{heroes} 名将领随行。");
            var top = party.MemberRoster.GetTroopRoster()
                .Where(e => e.Number > 0)
                .OrderByDescending(e => e.Number)
                .Take(3)
                .Select(e => $"{e.Character?.Name?.ToString() ?? "无名之辈"} {e.Number} 人")
                .ToList();
            if (top.Count > 0)
                sb.AppendLine("- 主要兵力：" + string.Join("、", top) + "。");
            return sb.ToString();
        }

        private static string QueryGoldFact()
        {
            var hero = Hero.MainHero;
            if (hero == null) return "- （此刻无从查知）";
            return $"- 队伍钱袋现有 {hero.Gold} 枚第纳尔可供开销。";
        }

        private static string QueryLocationFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            if (party.CurrentSettlement != null)
                return $"- 此刻队伍正在 {party.CurrentSettlement.Name}。";
            if (party.TargetSettlement != null)
                return $"- 此刻队伍行进在旷野中，正前往 {party.TargetSettlement.Name}。";
            return "- 此刻队伍行进在旷野中。";
        }

        private static string QueryFoodFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            return $"- 队伍粮草还剩约 {party.Food:0.0} 天的口粮。";
        }

        private static string QueryPrisonerFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            int regulars = party.PrisonRoster.TotalRegulars;
            int heroes = party.PrisonRoster.TotalHeroes;
            if (regulars + heroes == 0) return "- 队伍眼下没有押着战俘。";
            return $"- 队伍押着 {regulars} 名战俘" + (heroes > 0 ? $"，其中 {heroes} 名贵人" : "") + "。";
        }

        private static string QueryWoundedFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            int wounded = party.MemberRoster.GetTroopRoster().Sum(e => e.WoundedNumber);
            return $"- 队伍现有 {wounded} 名伤员待照料。";
        }

        private static string QueryFiefFact()
        {
            var clan = Clan.PlayerClan;
            if (clan == null) return "- （此刻无从查知）";
            int towns = clan.Fiefs?.Count(t => t.IsTown) ?? 0;
            int castles = clan.Fiefs?.Count(t => t.IsCastle) ?? 0;
            int villages = clan.Villages?.Count ?? 0;
            if (towns + castles + villages == 0) return "- 家族名下暂无领地。";
            var names = new List<string>();
            if (clan.Fiefs != null) names.AddRange(clan.Fiefs.Take(3).Select(f => f.Name?.ToString() ?? "无名之地"));
            if (clan.Villages != null) names.AddRange(clan.Villages.Take(2).Select(v => v.Name?.ToString() ?? "无名村庄"));
            return $"- 家族名下现有 {towns} 城镇、{castles} 城堡、{villages} 村庄（如：{string.Join("、", names)}）。";
        }

        private static string QueryRenownFact()
        {
            var clan = Clan.PlayerClan;
            if (clan == null) return "- （此刻无从查知）";
            return $"- 家族声望 {clan.Renown:0}，影响力 {clan.Influence:0}。";
        }

        private static string QueryFamilyFact()
        {
            var hero = Hero.MainHero;
            var clan = Clan.PlayerClan;
            if (hero == null) return "- （此刻无从查知）";
            var sb = new StringBuilder();
            string spouse = hero.Spouse?.Name?.ToString();
            if (!string.IsNullOrEmpty(spouse)) sb.AppendLine($"- 主公的配偶是 {spouse}。");
            int members = clan?.Heroes?.Count ?? 0;
            sb.AppendLine($"- 家族共有 {members} 名成员" + (clan?.Leader != null ? $"，族长是 {clan.Leader.Name}" : "") + "。");
            return sb.ToString();
        }

        private static string QueryWarFact()
        {
            var kingdom = Clan.PlayerClan?.Kingdom;
            var sb = new StringBuilder();
            if (kingdom == null)
            {
                sb.AppendLine("- 家族不属于任何王国（独立自由之身）。");
                return sb.ToString();
            }
            try
            {
                var atWar = Kingdom.All.Where(k => k != kingdom && k.IsAtWarWith(kingdom))
                    .Select(k => k.Name?.ToString()).ToList();
                if (atWar.Count > 0)
                    sb.AppendLine($"- {kingdom.Name} 正与 {string.Join("、", atWar.Take(5))} 交战。");
                else
                    sb.AppendLine($"- {kingdom.Name} 眼下与各方相安无事。");
            }
            catch { /* 阵营数据异常时跳过战争段 */ }
            var army = MobileParty.MainParty?.Army;
            if (army != null)
                sb.AppendLine($"- 主公正随军团行动（{army.Parties?.Count ?? 0} 支部队同行）。");
            return sb.ToString();
        }

        private static string QueryTimeFact()
        {
            return $"- 现在是{GetSeasonName()}、{(CampaignTime.Now.IsDayTime ? "白天" : "夜里")}，本季第 {CampaignTime.Now.GetDayOfSeason + 1} 天。";
        }

        private static string QueryQuestFact()
        {
            var qm = Campaign.Current?.QuestManager;
            if (qm == null || qm.Quests == null || qm.Quests.Count == 0) return "- 眼下没有进行中的委托。";
            var names = string.Join("、", qm.Quests.Take(3).Select(q => q.Title?.ToString() ?? "一桩委托"));
            return $"- 进行中的委托 {qm.Quests.Count} 桩（如：{names}）。";
        }

        /// <summary>技能等级：动态遍历 MBObjectManager 已注册技能（铁律 5 第二轮策略，非硬编码 ID），
        /// 输出已练技能（&gt;0）前 8 项，名称走游戏本地化（中英文环境自动正确）。</summary>
        private static string QuerySkillFact()
        {
            var hero = Hero.MainHero;
            if (hero == null) return "- （此刻无从查知）";
            try
            {
                var skills = MBObjectManager.Instance.GetObjectTypeList<SkillObject>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name?.ToString()) && hero.GetSkillValue(s) > 0)
                    .OrderByDescending(s => hero.GetSkillValue(s))
                    .Take(8)
                    .Select(s => $"{s.Name} {hero.GetSkillValue(s)}")
                    .ToList();
                if (skills.Count == 0) return "- 主公尚未精研任何技艺。";
                return "- " + string.Join("、", skills) + "。";
            }
            catch { return "- （此刻无从查知）"; }
        }

        /// <summary>等级 + 年龄（队伍成员看在眼里）。</summary>
        private static string QueryLevelFact()
        {
            var hero = Hero.MainHero;
            if (hero == null) return "- （此刻无从查知）";
            return $"- 主公现为 {hero.Level} 级的历练好手，年约 {hero.Age:0} 岁。";
        }

        /// <summary>商队数 + 工坊数（玩家产业）。</summary>
        private static string QueryBusinessFact()
        {
            var hero = Hero.MainHero;
            if (hero == null) return "- （此刻无从查知）";
            int caravans = 0, workshops = 0;
            try { caravans = MobileParty.All.Count(p => p.IsCaravan && p.Owner == hero); } catch { }
            try { workshops = hero.OwnedWorkshops?.Count ?? 0; } catch { }
            return $"- 主公名下现有 {caravans} 支商队、{workshops} 间工坊。";
        }

        /// <summary>部队士气（队伍成员可见）：数值 + 档位形容词。</summary>
        private static string QueryMoraleFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            float morale = party.Morale;
            string mood = morale >= 75 ? "士气高昂" : morale >= 50 ? "尚可" : morale >= 25 ? "有些低迷" : "濒临崩溃";
            return $"- 队伍士气 {morale:0}（{mood}）。";
        }

        /// <summary>当前所在定居点的驻军规模（普世——城头甲兵人尽皆知；不在城里则无从查知）。</summary>
        private static string QueryGarrisonFact()
        {
            var settlement = MobileParty.MainParty?.CurrentSettlement;
            var town = settlement?.Town;
            if (town == null || town.GarrisonParty == null) return "- 队伍眼下不在城中，驻军事宜无从查知。";
            return $"- {settlement.Name} 现有驻军 {town.GarrisonParty.MemberRoster.TotalRegulars} 人。";
        }

        /// <summary>问句兜底概要：一行式核心状态（队伍成员版含同行者隐私，外人版只有普世事实）。</summary>
        private static string QuerySummary(bool isPartyMember)
        {
            var sb = new StringBuilder();
            var party = MobileParty.MainParty;
            if (isPartyMember && party != null)
            {
                sb.AppendLine($"- 队伍现有 {party.MemberRoster.TotalRegulars} 名士兵、{party.MemberRoster.TotalHeroes} 名将领；" +
                              $"钱袋 {Hero.MainHero?.Gold ?? 0} 第纳尔；粮草约 {party.Food:0.0} 天。");
            }
            var clan = Clan.PlayerClan;
            if (clan != null)
            {
                int towns = clan.Fiefs?.Count(t => t.IsTown) ?? 0;
                int castles = clan.Fiefs?.Count(t => t.IsCastle) ?? 0;
                int villages = clan.Villages?.Count ?? 0;
                sb.AppendLine($"- 家族声望 {clan.Renown:0}，影响力 {clan.Influence:0}，领地 {towns + castles + villages} 处（城镇 {towns}、城堡 {castles}、村庄 {villages}）。");
            }
            var hero = Hero.MainHero;
            if (hero != null)
            {
                int caravans = 0, workshops = 0;
                try { caravans = MobileParty.All.Count(p => p.IsCaravan && p.Owner == hero); } catch { }
                try { workshops = hero.OwnedWorkshops?.Count ?? 0; } catch { }
                sb.AppendLine($"- 主公现为 {hero.Level} 级好手，年约 {hero.Age:0} 岁；名下商队 {caravans} 支、工坊 {workshops} 间。");
            }
            sb.AppendLine($"- 现在是{GetSeasonName()}。");
            return sb.ToString();
        }

        private static string GetSeasonName()
        {
            return CampaignTime.Now.GetSeasonOfYear switch
            {
                CampaignTime.Seasons.Spring => "春季",
                CampaignTime.Seasons.Summer => "夏季",
                CampaignTime.Seasons.Autumn => "秋季",
                CampaignTime.Seasons.Winter => "冬季",
                _ => "不明季节",
            };
        }

        private static bool IsQuestion(string text)
        {
            if (text.Contains("?") || text.Contains("？")) return true;
            string[] markers = { "吗", "多少", "什么", "啥", "谁", "哪", "何时", "怎么", "为何", "有没有",
                "how", "what", "who", "which", "why", "where", "when", "is there", "are we", "do we", "do you" };
            foreach (var m in markers)
            {
                if (text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            foreach (var kw in keywords)
            {
                if (text.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
