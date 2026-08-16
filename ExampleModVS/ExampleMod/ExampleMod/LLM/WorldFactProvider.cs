using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ObjectSystem;
using TaleWorlds.ScreenSystem;

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
            public Func<string> Query;     // 普世查询
            public Func<string> QueryMemberOnly;   // 🔴 2026-08-16（方案 G9 L1 附加）：队伍成员附加段
                                                   //（可选；isPartyMember 时在 Query 之后额外拼入）
        }

        /// <summary>主题注册表：覆盖「游戏内有数据的任何情况」的常见主题；新增主题 = 追加一条。</summary>
        private static readonly List<FactTopic> Topics = new List<FactTopic>
        {
            new FactTopic
            {
                Id = "party", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_party"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "队伍", "部队", "兵力", "人数", "人马", "士兵", "弟兄", "兄弟", "手下", "多少人",
                    // 🔴 2026-08-10 日志实锤：缺"军队"→"我的军队规模咋样"不注入队伍事实 → LLM 编"百来号人"
                    "军队", "兵种",
                    "army", "troop", "troops", "soldier", "soldiers", "manpower", "men", "party", "companion", "companions" },
                Query = QueryPartyFacts,
            },
            new FactTopic
            {
                Id = "gold", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_gold"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "钱", "金币", "第纳尔", "金子", "积蓄", "身家", "盘缠", "富", "穷",
                    "gold", "coin", "coins", "money", "denar", "denars", "wealth", "poor", "rich" },
                Query = QueryGoldFact,
            },
            new FactTopic
            {
                Id = "location", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_location"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "在哪", "位置", "何处", "何方", "哪里", "身在哪",
                    // 🔴 2026-08-13 实锤补词：玩家问"波罗斯城离这多远"不命中 → 无位置注入 → NPC 答"四五日脚程"
                    "距离", "多远", "远近", "路程", "脚程", "离这",
                    "where", "location", "position", "how far", "distance", "far" },
                Query = QueryLocationFact,
            },
            new FactTopic
            {
                Id = "food", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_food"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "粮食", "食物", "口粮", "补给", "粮草", "food", "supply", "supplies", "provision", "provisions", "rations" },
                Query = QueryFoodFact,
            },
            new FactTopic
            {
                Id = "prisoner", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_prisoner"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "俘虏", "囚犯", "战俘", "prisoner", "prisoners", "captive", "captives" },
                Query = QueryPrisonerFact,
            },
            new FactTopic
            {
                Id = "wounded", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_wounded"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "伤员", "伤兵", "受伤", "wounded", "injured", "hurt" },
                Query = QueryWoundedFact,
            },
            new FactTopic
            {
                Id = "fief", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_fief"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "领地", "封地", "城镇", "城堡", "村庄", "辖区",
                    "fief", "fiefs", "town", "castle", "village", "land", "lands" },
                Query = QueryFiefFact,
            },
            new FactTopic
            {
                Id = "renown", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_renown"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "声望", "影响力", "名誉", "威名", "名气", "renown", "influence", "fame" },
                Query = QueryRenownFact,
            },
            new FactTopic
            {
                Id = "family", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_family"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "家族", "家人", "亲人", "配偶", "妻子", "丈夫", "孩子", "儿女", "儿子", "女儿",
                    "family", "clan", "wife", "husband", "child", "children", "kid", "kids" },
                Query = QueryFamilyFact,
            },
            new FactTopic
            {
                Id = "war", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_war"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "王国", "战争", "开战", "敌对", "交战", "停战", "和平", "敌人", "盟国",
                    "kingdom", "war", "enemy", "enemies", "allies", "peace", "battle" },
                Query = QueryWarFact,
            },
            new FactTopic
            {
                Id = "time", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_time"), NeedsPartyMember = false, // lwn-ignore: B
                // 🔴 2026-08-16（修复）：补天气词——G1 天气词在 QueryTimeFact 内但主题关键词表没有
                // "天气"，问天气 rag[] 空 → 天气答全靠 LLM 从"夏季"编（实机"旷野上正吹着干爽的风"）。
                // I1 触发词表（NumericStatusKeywords）独立于本表，补词不影响 T05 聊天气零注入。
                Keywords = new[] { "今天", "几号", "几月", "季节", "日期", "何时", "日子", "时辰", "是日",
                    "天气", "晴", "雨", "雪", "weather", "rain", "snow",
                    "today", "date", "season", "month", "day", "when" },
                Query = QueryTimeFact,
            },
            new FactTopic
            {
                Id = "quest", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_quest"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "任务", "委托", "差事", "悬赏", "村子", "村庄", "quest", "quests", "issue", "issues", "errand", "errands" },
                Query = QueryQuestFact,
            },
            new FactTopic
            {
                Id = "skill", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_skill"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "剑术", "刀法", "箭术", "弓术", "骑术", "战术", "医术", "技能", "本领", "武艺", "武艺如何",
                    "skill", "skills", "sword", "archery", "riding", "medicine", "tactics", "warrior" },
                Query = QuerySkillFact,
            },
            new FactTopic
            {
                Id = "level", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_level"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "几级", "等级", "经验", "历练", "level", "experience", "xp" },
                Query = QueryLevelFact,
            },
            new FactTopic
            {
                Id = "business", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_business"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "商队", "工坊", "产业", "生意", "收入", "铺子", "caravan", "workshop", "business", "income", "shop" },
                Query = QueryBusinessFact,
            },
            new FactTopic
            {
                Id = "morale", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_morale"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "士气", "军心", "人心", "精神头", "低迷", "morale", "spirit", "morale low", "morale high" },
                Query = QueryMoraleFact,
            },
            new FactTopic
            {
                Id = "garrison", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_garrison"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "驻军", "守军", "守备", "城防", "garrison", "defenders", "guards" },
                Query = QueryGarrisonFact,
            },
            // 🔴 队伍成员名单（2026-08-10 幻觉修复）：玩家问"其他人呢/还有谁/随从是谁"时注入真实名单，
            // 否则 LLM 开放式编人（日志实锤：编出"两个随从小满山杏"并持续圆谎）。
            new FactTopic
            {
                Id = "member", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_member"), NeedsPartyMember = true, // lwn-ignore: B
                Keywords = new[] { "其他人", "还有谁", "有谁", "谁在", "成员", "随从", "护卫", "侍从", "学徒", "有名", "有姓", "名字", "人都在",
                    "who else", "member", "members", "retainer", "retainers", "servant", "servants", "apprentice", "companions" },
                Query = QueryMemberFact,
            },
            // 🔴 2026-08-16（方案 G4）：竞技场/比武（信息面 #13）——Town.HasTournament 实锤
            //（v1.4.8 Town 无公开 TournamentGame 属性，走 TournamentManager.GetTournamentGame 封装）
            new FactTopic
            {
                Id = "tournament", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_tournament"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "比武", "竞技场", "锦标赛", "tournament", "tournaments", "joust", "arena" },
                Query = QueryTournamentFact,
            },
            // 🔴 2026-08-16（方案 G5）：市场物价（信息面 #23）——IMarketData.GetPrice 实锤（4 参）
            new FactTopic
            {
                Id = "market", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_market"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "物价", "价格", "市价", "行情", "多少钱", "买卖", "price", "prices", "market", "trade", "cost" },
                Query = QueryMarketFact,
            },
            // 🔴 2026-08-16（方案 G9）：玩法建议（赚钱途径）——派生式组装（从已有事实取，不新造数据）；
            // L1 附加（QueryMemberOnly）= 赃物处理建议（队伍成员才见，依赖犯罪感知记忆自然带出）
            new FactTopic
            {
                Id = "moneymaking", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_moneymaking"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "赚钱", "发财", "来钱", "搞钱", "挣钱", "怎么挣", "谋生", "生计", "赚多少",
                    "money", "rich", "earn", "fortune", "make a living", "profit" },
                Query = QueryMoneyMakingFact,
                QueryMemberOnly = QueryFenceTip,
            },
            // 🔴 2026-08-16（方案 G3③）：通缉状态（信息面 #12）——Clan.IsOutlaw 实锤（SaveableProperty 70）
            new FactTopic
            {
                Id = "outlaw", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_outlaw"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "犯罪", "通缉", "犯法", "悬赏", "作恶", "outlaw", "wanted", "crime", "criminal" },
                Query = QueryOutlawFact,
            },
            // 🔴 2026-08-16（方案 Q）：随从画像统计（触发式注入——聊过战绩才注入，不聊零开销；
            // 计数全 0（早期游戏）→ BuildRecordLine 返回空 → 整段跳过）
            new FactTopic
            {
                Id = "record", Title = LWNTextHelper.ResolvePrompt("LWN_fact_title_record"), NeedsPartyMember = false, // lwn-ignore: B
                Keywords = new[] { "输赢", "胜", "败", "战绩", "名声", "运气", "栽", "翻车", "常胜", "老吃败仗", "我这几仗",
                    "record", "win", "loss", "luck", "track record", "victory", "defeat" },
                Query = () => PlayerImageStore.BuildRecordLine(),
            },
        };

        /// <summary>玩家文本命中知识主题 → 返回事实段（多行，可直接拼入 prompt）；未命中返回空串（零注入）。</summary>
        /// <summary>主题表诊断快照（供 PromptAudit 注入审计，2026-08-16）：所有已注册 RAG 主题的
        /// {Id, 本地化 Title}。审计按 Title 子串匹配 prompt 判断主题是否命中注入（Id 只作标签，
        /// 不参与匹配——Title 即 prompt 里的段标题）。</summary>
        public static List<KeyValuePair<string, string>> GetTopicDescriptors()
        {
            try
            {
                return Topics
                    .Where(t => t != null && !string.IsNullOrEmpty(t.Title))
                    .Select(t => new KeyValuePair<string, string>(t.Id, t.Title))
                    .ToList();
            }
            catch { return new List<KeyValuePair<string, string>>(); }
        }

        /// <param name="numericCovered">🔴 2026-08-16（prompt 精简）：I1【此刻现状】已注入（数值话题命中）时
        /// 世界概要跳过钱/粮/季节行——同一数值不在 prompt 里出现两遍；未命中则概要照常全量（问句兜底）。</param>
        /// <param name="conv">🔴 2026-08-16（用户裁定：你们俩 = 频道最近两人）：群聊会话——"你们俩/两位"
        /// 没点名时从频道消息尾部取最近两个不同 NPC 发言人作双实体查询；respond 链路（当面对话）传 null
        /// 不启用（无群聊语境）。</param>
        /// <param name="responderHeroId">🔴 2026-08-16（当事人放行）：回复者本人 StringId——pair 含本人时
        /// 不受 NeedsPartyMember 裁剪（问"你俩关系怎么样"的当事人亲见自己的关系，第一人称无边界；
        /// 裁剪只挡第三方路人打听队伍成员关系）。</param>
        public static string BuildFactsForIm(string playerText, bool isPartyMember, bool numericCovered = false,
            ImConversation conv = null, string responderHeroId = null)
        {
            if (string.IsNullOrWhiteSpace(playerText)) return "";
            // 🔴 2026-08-16（方案 T）：双实体关系查询优先（文本命中两个不同 Hero + 关系词 → X↔Y 硬事实）
            var pair = ResolvePairQuery(playerText, isPartyMember, conv, responderHeroId);
            if (pair != null)
            {
                string fact = QueryHeroPairFact(pair.Value.Item1, pair.Value.Item2);
                if (!string.IsNullOrWhiteSpace(fact))
                {
                    var sbPair = new StringBuilder();
                    sbPair.AppendLine(fact);
                    sbPair.AppendLine();
                    return sbPair.ToString();
                }
            }
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
                    string q = t.Query();
                    // 🔴 2026-08-16（方案 Q）：查询命中但无内容（如画像计数全 0——早期游戏无画像可说）→
                    // 整段跳过（不打印空标题）
                    if (string.IsNullOrEmpty(q)) continue;
                    anyHit = true;
                    body.AppendLine(t.Title);
                    body.AppendLine(q);
                    // 🔴 2026-08-16（方案 G9 L1 附加）：队伍成员附加段（如赃物处理建议——外人不知道赃物）
                    if (isPartyMember && t.QueryMemberOnly != null)
                    {
                        string extra = t.QueryMemberOnly();
                        if (!string.IsNullOrEmpty(extra)) body.AppendLine(extra);
                    }
                    body.AppendLine();
                }
            }
            // 问句兜底：关键词全没命中但玩家在问 → 轻量世界概要（有数据的问题至少拿到基础事实可答）
            if (!anyHit && IsQuestion(playerText))
            {
                body.AppendLine(LWNTextHelper.ResolveText("LWN_fact_title_summary", "## World Overview (common facts you know)")); // lwn-ignore: B
                body.AppendLine(QuerySummary(isPartyMember, numericCovered));
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
            string key = eq.Property switch
            {
                "location" => "LWN_fact_title_hero_location", // lwn-ignore: B
                "relation" => "LWN_fact_title_hero_relation", // lwn-ignore: B
                "age" => "LWN_fact_title_hero_age", // lwn-ignore: B
                _ => "LWN_fact_title_hero_other", // lwn-ignore: B
            };
            return LWNTextHelper.ResolveCompound(key,
                "## About {NAME}", ("NAME", name));
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
        /// 🔴 2026-08-13（场景内优先）：目标在当前 Mission 有 Agent → 场景内相对方位描述（"你左侧约 58 米"）——
        /// 骑砍2 场景无区域划分标记（语义 tag 原生场景多为空），相对方位是唯一可靠的位置语义；
        /// 区域锚点作可选附加（探测到才说，探测不到纯相对描述）。
        /// 分级是 C# 确定性逻辑（王国比对 + IsAtWarWith），LLM 只拿分级后的文本做措辞。</summary>
        private static string QueryHeroLocationFact(Hero hero)
        {
            // 场景内优先：同场 NPC 的位置用相对方位（坐标玩家不可用，念坐标也出戏）
            string inScene = QueryInSceneLocation(hero);
            if (inScene != null) return inScene;

            // 🔴 2026-08-16（被俘 = 属于俘虏他的队伍，用户裁定）：被俘英雄 PartyBelongedTo 被引擎
            // 清空，但 PartyBelongedToAsPrisoner 就是他**当前所属的队伍**——俘虏他的那支。
            // 查询第一性 = "hero 属于什么 party"：被俘不是兜底，是主查询分支——必须答出
            // 被哪个队伍俘虏（实机：阿速甘被吕卡隆俘获，同队百草被问"阿速甘在哪"应答
            // "被吕卡隆的守军俘虏"，而非"无人知晓其下落"）。
            var prisonParty = hero.PartyBelongedToAsPrisoner;
            if (prisonParty != null)
            {
                // 被关在定居点（城镇/城堡/村庄的牢里）——俘虏者 = 该定居点守军
                if (prisonParty.IsSettlement && prisonParty.Settlement != null)
                    return $"- {hero.Name} 被{prisonParty.Settlement.Name}的守军俘虏，关押在那里。";
                // 被移动部队俘虏押解（如"乌尔玻斯的部队"）——答出俘虏队伍
                string captor = prisonParty.Name?.ToString();
                return string.IsNullOrEmpty(captor)
                    ? $"- {hero.Name} 被俘了，正被人押着行军。"
                    : $"- {hero.Name} 被 {captor} 俘虏，正被押着行军。";
            }

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

        /// <summary>场景内位置：目标在当前 Mission 有存活 Agent → 相对玩家方位+距离；无 → null（落大地图逻辑）。
        /// 骑砍2 场景无区域划分标记，语义 tag 原生场景多为空——相对方位（左/右/前/后）+ 距离是唯一可靠位置语义；
        /// 最近语义区域只作附加（探测到才说，探测不到 → 纯相对描述）。</summary>
        private static string QueryInSceneLocation(Hero hero)
        {
            try
            {
                if (hero?.CharacterObject == null) return null;
                if (Mission.Current == null || Agent.Main == null) return null;
                Agent target = null;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == Agent.Main) continue;
                    if (a.Character == hero.CharacterObject) { target = a; break; }
                }
                if (target == null) return null;
                var player = Agent.Main;
                float dist = target.Position.Distance(player.Position);
                DebugLogger.Log($"[SceneDir-Hero] {hero.Name}: target=({target.Position.x:F1},{target.Position.y:F1},{target.Position.z:F1}) " +
                    $"player=({player.Position.x:F1},{player.Position.y:F1},{player.Position.z:F1}) dist={dist:F1}");
                if (dist < 3f) return $"- {hero.Name} 眼下就在你跟前。";
                string dir = DirectionDesc(player, target.Position);
                string zone = NearestSemanticZoneDesc(target.Position, out float zoneDist);
                if (zone != null && zoneDist <= 12f)
                    return $"- {hero.Name} 眼下在{zone}附近，{dir}约 {dist:0} 米。";
                return $"- {hero.Name} 眼下{dir}约 {dist:0} 米处。";
            }
            catch { return null; }
        }

        /// <summary>相对方位（8 向）：按玩家**摄像机**水平朝向分前/后/左/右/斜向。
        /// 🔴 2026-08-13（实机验证）：right 必须用引擎官方 Side 轴（frame.rotation.s，+X 为右）——
        /// 手算 (-fwd.y, fwd.x) 在引擎左手系（Side=+X/Forward=+Y/Up=+Z，Vec3.Side 常量）下是**左向量**，
        /// 实测 prompt 报"左前方"而 NPC 实际从玩家右前方跑来，左右整体翻转。
        /// 🔴 调试日志：本函数只被问坐标路径调用，每次打印几何数据供回查。
        /// internal：ActionHandler 模板 NPC 候选方位标签复用（选择卡按钮「① 右侧约10米」）。</summary>
        internal static string DirectionDesc(Agent player, Vec3 targetPos)
        {
            try
            {
                Vec3 diff = targetPos - player.Position;
                diff.z = 0f;
                float len = diff.Length;
                if (len < 1.5f) return "正对面";
                Vec3 fwd = GetPlayerForward();
                MatrixFrame frame = GetPlayerCameraFrame();
                Vec3 right = frame.rotation.s;          // 引擎官方 Side 轴（+X 为右）
                right.z = 0f;
                if (right.LengthSquared < 0.01f) right = new Vec3(-fwd.y, fwd.x, 0f);
                else right.Normalize();
                float f = Vec3.DotProduct(diff, fwd) / len;
                float r = Vec3.DotProduct(diff, right) / len;
                string lat = r > 0.35f ? "右侧" : (r < -0.35f ? "左侧" : "");
                string lon = f > 0.35f ? "前方" : (f < -0.35f ? "后方" : "");
                string result;
                if (lat.Length == 0 && lon.Length == 0) result = "正对面";
                else if (lat.Length == 0) result = lon;
                else if (lon.Length == 0) result = lat;
                else result = lat + lon;
                DebugLogger.Log($"[SceneDir] target=({targetPos.x:F1},{targetPos.y:F1},{targetPos.z:F1}) " +
                    $"player=({player.Position.x:F1},{player.Position.y:F1},{player.Position.z:F1}) " +
                    $"diff=({diff.x:F1},{diff.y:F1}) dist={len:F1} " +
                    $"camFwd=({fwd.x:F2},{fwd.y:F2}) camSide=({right.x:F2},{right.y:F2}) → \"{result}\"");
                return result;
            }
            catch { return "附近"; }
        }

        /// <summary>玩家摄像机帧（CustomCamera ?? Mission.GetCameraFrame() 既有范式）。
        /// 🔴 右向量必须取 frame.rotation.s（引擎 Side 轴），禁止手算 (-fwd.y, fwd.x)（左手系下左右翻转）。</summary>
        private static MatrixFrame GetPlayerCameraFrame()
        {
            try
            {
                if (Mission.Current == null) return MatrixFrame.Identity;
                var cam = (ScreenManager.TopScreen as MissionScreen)?.CustomCamera;
                return cam != null ? cam.Frame : Mission.Current.GetCameraFrame();
            }
            catch { return MatrixFrame.Identity; }
        }

        /// <summary>玩家水平正前方 = 摄像机朝向（CustomCamera ?? Mission.GetCameraFrame() 既有范式，水平投影）。
        /// 🔴 不用 Agent.LookDirection：自由视角（按住 F 环绕镜头）时角色朝向 ≠ 玩家视角方向，方位描述会错。</summary>
        private static Vec3 GetPlayerForward()
        {
            try
            {
                if (Mission.Current == null || Agent.Main == null)
                    return new Vec3(0f, 1f, 0f);
                Vec3 fwd = GetPlayerCameraFrame().rotation.f;
                fwd.z = 0f;
                if (fwd.LengthSquared < 0.01f)
                    return Agent.Main.LookDirection;
                fwd.Normalize();
                return fwd;
            }
            catch { return new Vec3(0f, 1f, 0f); }
        }

        /// <summary>最近语义区域（SceneSnapshot 同源 tag 列表；原生场景多探测不到 → null 纯相对描述）。</summary>
        private static string NearestSemanticZoneDesc(Vec3 pos, out float zoneDist)
        {
            zoneDist = float.MaxValue;
            try
            {
                if (Mission.Current?.Scene == null) return null;
                string best = null;
                foreach (var tag in SceneSnapshot.SemanticZoneTags)
                {
                    var entity = Mission.Current.Scene.FindEntityWithTag(tag);
                    if (entity == null) continue;
                    float d = entity.GlobalPosition.Distance(pos);
                    if (d < zoneDist) { zoneDist = d; best = tag; }
                }
                return best;
            }
            catch { return null; }
        }

        /// <summary>🔴 2026-08-13（场景认知注入）：回复者当前在本场景 → 处境段
        /// （"你此刻在 波罗斯（镇中心）。主公就在你左前方约 4 米处。"）。
        /// 根治 IM 闲聊无场景认知（实锤：药僧在玩家 4 米外答"波罗斯城距您四五日脚程"）。
        /// 在场即亲历（叙事铁律）；不在场返回空串（零注入）。
        /// ⚠️ 主线程调用（引擎对象 Mission/Agent/Settlement 只读主线程）——ImReplyService.ScheduleReply 构建快照。</summary>
        public static string BuildSceneAwareness(string heroId)
        {
            try
            {
                if (string.IsNullOrEmpty(heroId) || Mission.Current == null || Agent.Main == null) return "";
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId);
                if (hero?.CharacterObject == null) return "";
                Agent self = null;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == Agent.Main) continue;
                    if (a.Character == hero.CharacterObject) { self = a; break; }
                }
                if (self == null) return "";
                return BuildSceneAwarenessForAgent(self, heroId);
            }
            catch { return ""; }
        }

        /// <summary>
        /// 🔴 2026-08-16（方案 H4.2）：respond 链路补场景锚点（L2 同场景任意 agent，含模板 NPC）——
        /// 当面对话问"我们在哪/附近有什么"也答得准（方案 A 的最近定居点兜底随此自动生效）。
        /// 模板 NPC（无 Hero）用本入口（BuildSceneAwareness 的 heroId 版解析不到 Hero 时回落）。
        /// </summary>
        public static string BuildSceneAwarenessForAgent(Agent self, string heroIdForLog = null)
        {
            try
            {
                if (self == null || Mission.Current == null || Agent.Main == null) return "";

                var sb = new StringBuilder();
                // 地点：定居点 + 子场景（Location.Name 引擎本地化）
                string place = Settlement.CurrentSettlement?.Name?.ToString();
                string locName = CampaignMission.Current?.Location?.Name?.ToString();
                if (!string.IsNullOrEmpty(locName))
                    place = string.IsNullOrEmpty(place) ? locName : $"{place}（{locName}）";
                // 🔴 2026-08-16（方案 A）：场景无定居点锚点（野战/城门遇袭场景无 CurrentSettlement）→
                // 最近定居点兜底（"X 附近的旷野"）；仍为空才保留"同处一场景"（真正无锚点的场景）
                if (string.IsNullOrEmpty(place))
                {
                    string near = NearestSettlementName(15f);
                    if (near != null) place = $"{near}附近的旷野";
                }
                sb.AppendLine("【此刻处境】" + (string.IsNullOrEmpty(place) ? "你和主公同处一场景。" : $"你此刻在 {place}。"));
                // 🔴 2026-08-16（方案 G8 确认不可用，P3）：quest 目标锚点行不实现——ilspycmd 反编译
                // v1.4.8 实锤 QuestBase 无 TargetSettlement 公开属性（任务目标定居点散落在各任务子类/
                // JournalLog 变量，无统一公开 API），按计划「不可用则跳过该行」口径跳过；
                // 罗盘标记主体（#35）已由在场采样 + E 方案覆盖，缺的只是"此处正是主公差事的所在"一层。
                // 主公相对本 NPC 的方位（以主公视线方向为基准，水平投影）
                string rel = DescribePlayerRelative(self, heroIdForLog);
                if (!string.IsNullOrEmpty(rel)) sb.AppendLine(rel);
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>本 NPC 相对主公的方位距离（🔴 以主公视线方向为基准——NPC 转述零视角反转，
        /// 玩家听到"我就在您左前方约 73 米"就是玩家该走的方向；以前 NPC 朝向为基准需反转易错）。</summary>
        private static string DescribePlayerRelative(Agent self, string heroId)
        {
            try
            {
                var player = Agent.Main;
                Vec3 diff = player.Position - self.Position;
                diff.z = 0f;
                float dist = diff.Length;
                DebugLogger.Log($"[SceneDir-IM] {heroId}: self=({self.Position.x:F1},{self.Position.y:F1},{self.Position.z:F1}) " +
                    $"player=({player.Position.x:F1},{player.Position.y:F1},{player.Position.z:F1}) dist={dist:F1}");
                if (dist < 3f) return $"你就在主公跟前（约 {MathF.Ceiling(dist)} 米）。";
                string dir = DirectionDesc(player, self.Position);   // NPC 相对玩家的方位（玩家朝向为基准）
                return $"你正在主公{dir}约 {MathF.Ceiling(dist)} 米处。";
            }
            catch { return ""; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-14（npc-risk-aware-planning.md M3）：命令注入场景感知（【目之所及】段）
        // ═══════════════════════════════════════════════════════════
        /// <summary>命令触发词表（中英双语，对齐 WorldFactProvider 主题表惯例）：命中 → 注入风险场景段；
        /// 闲聊（无命令语义）零开销不注入。L0 低风险动作（move_to/emote/follow 等）不在此表。</summary>
        private static readonly string[] RiskCommandKeywords =
        {
            "偷", "摸", "扒", "抢", "打", "揍", "敲", "杀", "击晕", "晕", "抓", "绑", "收拾",
            "跟", "盯", "卸", "夺", "潜", "偷袭", "动手", "制服",
            "steal", "pickpocket", "rob", "knock", "attack", "hit", "kill", "grab", "catch",
            "strike", "ambush", "disarm", "seize", "take down", "beat", "follow", "trail",
        };
        /// <summary>
        /// 🔴 2026-08-14（M3）：命令注入场景感知——命中动作命令关键词（偷/击晕/打/跟/抓/抢……）时，
        /// 在 BuildSceneAwareness（此刻处境）基础上扩展返回【目之所及】段：该 NPC 一眼扫到的场面
        ///（在场人员/目标位置与移动状态/视线/阵营/双方合计战力+武装档位/在场潜在援军）。
        /// 用途 = M4 风险审视的输入 + 随从 think-aloud 的事实来源（拒绝/计划时理由有事实依据，
        /// 把 P3 的"常识泛化"升级成"感知判断"）。
        /// 叙事铁律：全是该 NPC 亲见（快照 = 它的眼睛）；背对的人视线状态按 CanSee 为准；
        /// 阵营信息 = 同袍穿同款甲胄/熟面孔、守卫的制式装备——看得出来的。
        /// 快慢变量分层：慢变量（人群构成/职业/阵营/守卫位置/战力合计）作判断依据；
        /// 快变量（视线"正看着谁"、精确距离）弱化措辞（"此刻"/"随时会变"）。
        /// ⚠️ 主线程调用（引擎对象只读主线程）——ImReplyService.ScheduleReply 构建快照。
        /// </summary>
        public static string BuildRiskSceneContext(string npcHeroId, string commandText)
        {
            try
            {
                if (string.IsNullOrEmpty(npcHeroId) || string.IsNullOrEmpty(commandText)
                    || Mission.Current == null || Agent.Main == null) return "";
                // 触发范围 = 命令（闲聊零开销）：命中动作命令关键词才注入
                string cmd = commandText;
                bool hit = false;
                foreach (var kw in RiskCommandKeywords)
                    if (cmd.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                if (!hit) return "";
                // 该 NPC 的物理载体（快照 = 它的眼睛）
                Agent self = null;
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == Agent.Main) continue;
                    var hero = (a.Character as CharacterObject)?.HeroObject;
                    if (hero != null && hero.StringId == npcHeroId) { self = a; break; }
                }
                if (self == null) return "";
                var snap = SceneSnapshot.Build(Mission.Current);
                var sb = new StringBuilder();
                // 本地化：【目之所及】段标题（M3 风险审视 prompt 段标题，铁律 13 走 LWN_fact_title_risk）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_fact_title_risk",
                    "【目之所及】（You are in the scene; this is what you see at a glance — people move around, the impression may be stale）"));
                DebugLogger.Log($"[RiskScene] {npcHeroId} 命令命中注入（{cmd.Length} 字符，在场 {snap.Agents.Count} 人）");
                // ── 目标解析：命令文本里出现的人名/角色名 → 快照五层匹配（复用 FindAgent 口径）──
                SceneSnapshot.AgentInfo targetInfo = null;
                foreach (var info in snap.Agents)
                {
                    if (info == null || info.Agent == null || info.Agent == self) continue;
                    string name = info.DisplayName ?? "";
                    string charName = info.Agent.Character?.Name?.ToString() ?? "";
                    string role = info.Role ?? "";
                    if ((!string.IsNullOrEmpty(name) && cmd.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrEmpty(charName) && cmd.IndexOf(charName, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrEmpty(role) && role.Length >= 2 && cmd.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        targetInfo = info;
                        break;
                    }
                }
                Agent target = targetInfo?.Agent;
                DebugLogger.Log($"[RiskScene] 目标解析: {target?.Name?.ToString() ?? "无（只给在场概况）"}");
                // ── 在场概况（🔴 2026-08-15 采样优化，用户裁定：楼层聚类 + 分层配额 + 优先级采样）──
                // 旧实现全量按角色合并计数，无楼层概念、无个体多样性。新实现：候选池 → 楼层聚类（Position.Z）
                // → 每层保底 1 + 按人数比例配额 → 层内优先级（目标 > Hero > 独特模板 > 近距 15m > 空间均匀随机）
                // → 个体行 + 未采样者合并计数行。token 预算内信息价值最大化。
                string presenceSample = BuildPresenceSample(snap, self, target);
                if (!string.IsNullOrEmpty(presenceSample))
                    sb.Append(presenceSample);
                // ── 目标段（解析到目标才给；目标在走动 → 时效声明）──
                if (target != null && target.IsActive())
                {
                    // 🔴 2026-08-15（目标唯一标记）：名字带 [#N] index 标记（引擎 Agent.Index，Mission 内稳定）——
                    // LLM 基于场景语义指认目标（「酒馆老板」→ 场景里标着 [#N] 的「酒馆店主」），
                    // action_target 输出带 index（如 酒馆店主#3）→ C# 精确解析，不靠字符串匹配。
                    string tName = $"{target.Name?.ToString() ?? targetInfo?.DisplayName ?? "目标"}[#{target.Index}]";
                    // 🔴 2026-08-15（楼层感知）：目标行标注所在楼层（「楼上」→ LLM 计划天然含上楼步骤）
                    string tFloor = FloorLabelOf(target, self);
                    if (!string.IsNullOrEmpty(tFloor)) tName += $"（{tFloor}）";
                    // 目标相对本 NPC 的方位 + 距离（以 self 自身朝向为基准——亲见视角）
                    string rel = DescribeTargetRelative(self, target);
                    // 移动状态（RuntimeWorldState.cs:285 同口径：Velocity.LengthSquared > 0.25f = 走动中）
                    bool moving = target.Velocity.LengthSquared > 0.25f;
                    sb.AppendLine($"- {tName} {rel}" + (moving ? "，他正在走动（位置随时会变）" : "，他站着没动（位置大致稳定）") + "。");
                    // 目标身边 3 米内人数
                    int nearby = 0;
                    foreach (var info in snap.Agents)
                    {
                        if (info == null || info.Agent == null || info.Agent == target || info.Agent == self) continue;
                        if (info.Agent.Position.Distance(target.Position) <= 3f) nearby++;
                    }
                    if (nearby > 0)
                        sb.AppendLine($"- 他身边 3 米内有 {nearby} 人站着。");
                    // 视线（快变量：弱化措辞）——「至少有 N 人的视线落在他身上（此刻）」
                    int watchers = 0;
                    foreach (var info in snap.Agents)
                    {
                        if (info == null || info.Agent == null || info.Agent == target || info.Agent == self) continue;
                        if (snap.CanSee(info.Agent, target)) watchers++;
                    }
                    if (watchers > 0)
                        sb.AppendLine($"- 此刻至少有 {watchers} 人的视线落在他身上（转头就可能变）。");
                    // 谁正看着本 NPC 自己（亲见：自己被盯着）
                    int selfWatchers = 0;
                    foreach (var info in snap.Agents)
                    {
                        if (info == null || info.Agent == null || info.Agent == self) continue;
                        if (snap.CanSee(info.Agent, self)) selfWatchers++;
                    }
                    if (selfWatchers > 0)
                        sb.AppendLine($"- 可能有 {selfWatchers} 人正看着你（你自己也会被看见）。");
                }
                // ── 阵营段（与 AgentBrain 实际行为同口径：友方旁观者豁免/护主参战/守卫站秩序/中立目击告发）──
                var buddies = new List<string>();
                var targetFriends = new List<string>();
                int guards = 0;
                var neutralNames = new List<string>();
                foreach (var info in snap.Agents)
                {
                    if (info == null || info.Agent == null || info.Agent == self || info.Agent == Agent.Main) continue;
                    if (info.Role == "guard") { guards++; continue; }
                    // 🔴 2026-08-15（目标唯一标记）：名单带 [#N]——LLM 可引用「你的同袍#7」等指定任意在场者
                    string label = $"{info.DisplayName ?? "同袍"}[#{info.Agent.Index}]";
                    if (FriendlinessHelper.IsFriendlyToPlayer(info.Agent)) buddies.Add(label);
                    else if (target != null && FriendlinessHelper.IsFriendlyBetween(target, info.Agent)) targetFriends.Add(label);
                    else neutralNames.Add(label);
                }
                if (buddies.Count + targetFriends.Count + guards + neutralNames.Count > 0)
                {
                    sb.AppendLine("- 阵营（谁站谁那边——你犯事时他们的真实反应）：");
                    if (buddies.Count > 0)
                        sb.AppendLine($"  - 在场 {buddies.Count} 人是你的同袍（玩家队伍的人）：{string.Join("、", buddies.Take(4))}——你犯事他们假装没看见，你被打他们会帮你。");
                    if (targetFriends.Count > 0)
                        sb.AppendLine($"  - {tNameOf(target)}身边 {targetFriends.Count} 人是他的同伴：会帮他、会告发你。");
                    if (guards > 0)
                        sb.AppendLine($"  - 场上有 {guards} 名守卫：你若犯事他会抓你。");
                    if (neutralNames.Count > 0)
                        sb.AppendLine($"  - 其余 {neutralNames.Count} 人是中立旁观者：不参战，但会看见并告发。");
                }
                // ── 战力段（双方合计 + 武装档位 + 结论词；禁止给数字公式让 LLM 编——给结论词）──
                int selfSide = AgentStatsHelper.GetAgentStatTotal(self);
                int enemySide = 0;
                if (target != null && target.IsActive()) enemySide += AgentStatsHelper.GetAgentStatTotal(target);
                foreach (var info in snap.Agents)
                {
                    if (info == null || info.Agent == null || info.Agent == self || info.Agent == Agent.Main) continue;
                    if (info.Role == "guard") { enemySide += AgentStatsHelper.GetAgentStatTotal(info.Agent); continue; }  // 守卫站秩序 = 随从犯事即敌（保守计入）
                    if (target != null && FriendlinessHelper.IsFriendlyBetween(target, info.Agent))
                        enemySide += AgentStatsHelper.GetAgentStatTotal(info.Agent);
                    else if (FriendlinessHelper.IsFriendlyToPlayer(info.Agent))
                        selfSide += AgentStatsHelper.GetAgentStatTotal(info.Agent);
                }
                // 本地化：战力结论五档词（双方合计比 → 结论词，LLM 只读结论不给数字公式）
                string verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_even", "evenly matched");
                // 本地化：敌方无战力 → 稳赢
                if (enemySide <= 0) verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_overwhelming", "an overwhelming win");
                else
                {
                    float ratio = (float)selfSide / enemySide;
                    // 本地化：五档阈值分档（稳赢/略占上风/势均力敌/略处下风/悬殊）
                    if (ratio >= 2.5f) verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_overwhelming", "an overwhelming win");
                    // 本地化：略占上风
                    else if (ratio >= 1.6f) verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_slight_advantage", "a slight advantage");
                    // 本地化：势均力敌
                    else if (ratio >= 0.85f) verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_even", "evenly matched");
                    // 本地化：略处下风
                    else if (ratio >= 0.5f) verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_slight_disadvantage", "a slight disadvantage");
                    // 本地化：悬殊
                    else verdict = LWNTextHelper.ResolveText("LWN_risk_verdict_overwhelmed", "overwhelmingly outmatched");
                }
                sb.AppendLine("- 战力对比（真打起来算两边合计——你+在场同袍 vs 目标+他同伴+守卫）：");
                string selfArmor = AgentStatsHelper.ArmorProfileWord(AgentStatsHelper.GetArmorProfile(self));
                sb.AppendLine($"  - 你这边的总战力 vs 对面的总战力——结论：{verdict}。");
                sb.AppendLine($"  - 你自己的状态：{selfArmor}，Vigor {AgentStatsHelper.GetAgentStats(self).vigor}/Control {AgentStatsHelper.GetAgentStats(self).control}。");
                if (target != null && target.IsActive())
                {
                    string tArmor = AgentStatsHelper.ArmorProfileWord(AgentStatsHelper.GetArmorProfile(target));
                    sb.AppendLine($"  - {target.Name?.ToString() ?? "目标"}[#{target.Index}]：{tArmor}，Vigor {AgentStatsHelper.GetAgentStats(target).vigor}/Control {AgentStatsHelper.GetAgentStats(target).control}。");
                }
                // ── 在场潜在援军（30m 内可能赶来的人，按阵营分列——IsFriendlyBetween 双向判定）──
                var helpUs = new List<string>();
                var helpThem = new List<string>();
                var watchOnly = new List<string>();
                foreach (var info in snap.Agents)
                {
                    if (info == null || info.Agent == null || info.Agent == self || info.Agent == Agent.Main) continue;
                    if (target != null && info.Agent.Position.Distance(target.Position) <= 15f) continue;  // 已在目标区（上面算过）
                    if (info.Agent.Position.Distance(self.Position) > 30f) continue;
                    // 🔴 2026-08-15（目标唯一标记）：援军名单带 [#N]
                    string label = $"{info.DisplayName ?? "友军"}[#{info.Agent.Index}]";
                    if (FriendlinessHelper.IsFriendlyToPlayer(info.Agent)) helpUs.Add(label);
                    else if (target != null && FriendlinessHelper.IsFriendlyBetween(target, info.Agent)) helpThem.Add(label);
                    else watchOnly.Add(label);
                }
                if (helpUs.Count + helpThem.Count + watchOnly.Count > 0)
                {
                    sb.AppendLine("- 但附近 30 米内还有人（可能 10 秒内赶来）：");
                    if (helpUs.Count > 0)
                        sb.AppendLine($"  - 帮你的：{string.Join("、", helpUs.Take(3))}。");
                    if (helpThem.Count > 0)
                        sb.AppendLine($"  - 帮他的：{string.Join("、", helpThem.Take(3))}。");
                    if (watchOnly.Count > 0)
                        sb.AppendLine($"  - 中立观望的：{string.Join("、", watchOnly.Take(3))}。");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }
        private static string tNameOf(Agent target)
        {
            return target?.Name?.ToString() ?? "目标";
        }
        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-15（用户裁定采样优化）：在场人员采样——楼层聚类 + 分层配额 + 优先级采样
        // ═══════════════════════════════════════════════════════════

        /// <summary>个体行预算（token 权衡：16 行 ≈ 550 token，flash 无压力；全量 27 人 ≈ 800+ token
        /// 且镇民×14 高度重复助长幻觉——采样 + 合并计数正好防幻觉，prompt 明确其余人只是数量）。</summary>
        private const int SightSampleBudget = 16;

        /// <summary>楼层聚类容差（Z 差 ≤ 2m 归同层；骑砍层高 ~3.5-4m，同层地表起伏 < 2m）。</summary>
        private const float FloorTolerance = 2.0f;

        /// <summary>楼层标签阈值（相对 self 所在层）：±1m 内=本层；±4m 内=楼上/楼下；更远=更上层/更下层。</summary>
        private const float FloorSameThreshold = 1.0f;
        private const float FloorAdjacentThreshold = 4.0f;

        /// <summary>运行时楼层词（prompt 段材料，铁律 13 豁免）。</summary>
        private static readonly string[] FloorWords = { "本层", "楼上", "楼下", "更上层", "更下层" };

        /// <summary>独特职业表（每个角色采样保底 1 个——用户裁定「每个模板的人都能采样到 1 个」；
        /// 镇民/村民等大众职业不在此列，走空间随机兜底）。</summary>
        private static readonly string[] UniqueRoleTable =
        {
            "tavernkeeper", "merchant", "musician", "bard", "drunkard", "gambler",
            "ransom_broker", "taverngamehost", "guard", "priest", "blacksmith", "cook", "waiter", "chief",
        };

        /// <summary>通用模板名（名字 = 纯职业词 → 不算"独特名字"，走兜底采样）。</summary>
        private static readonly HashSet<string> GenericTemplateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "镇民", "村民", "女镇民", "农民", "市民", "士兵", "守卫", "卫兵", "侍女", "仆役", "学徒", "流浪汉",
            "townsman", "townswoman", "villager", "peasant", "citizen", "soldier", "guard", "servant", "apprentice",
        };

        /// <summary>楼层标签（相对 self 所在层；self 为 null → 空串）。</summary>
        private static string FloorLabelOf(Agent agent, Agent self)
        {
            if (agent == null || self == null) return "";
            float dz = agent.Position.Z - self.Position.Z;
            if (dz > FloorAdjacentThreshold) return FloorWords[3];
            if (dz > FloorSameThreshold) return FloorWords[1];
            if (dz < -FloorAdjacentThreshold) return FloorWords[4];
            if (dz < -FloorSameThreshold) return FloorWords[2];
            return FloorWords[0];
        }

        /// <summary>独特名字判定：非通用词集合 && 与职业/角色不全等（"酒馆店主"≠"店主"= 有具体身份 → 独特）。</summary>
        private static bool IsDistinctiveName(SceneSnapshot.AgentInfo i)
        {
            if (i == null || string.IsNullOrEmpty(i.DisplayName)) return false;
            string n = i.DisplayName.Trim();
            if (GenericTemplateNames.Contains(n)) return false;
            if (!string.IsNullOrEmpty(i.Occupation) && string.Equals(n, i.Occupation.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(i.Role) && string.Equals(n, i.Role, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>
        /// 🔴 2026-08-15（采样优化，用户裁定）：在场人员采样——token 预算内个体行信息价值最大化。
        /// 流水线：候选池（排除观察者 self 与玩家）→ 楼层聚类（Position.Z，容差 2m，防碎层）→
        /// 层配额（每层保底 1 + 剩余按人数比例）→ 层内优先级采样：
        ///   P0 目标（已解析 target，必含）> P1 Hero > P1.5 己方（IsFriendlyToPlayer，打架会帮忙）>
        ///   P2 独特模板（独特职业表各取 1 + 独特名字）> P3 近距 15m（用户裁定身边范围）> P4 空间均匀随机
        ///   （方位角 8 扇区 × 距离环，桶内随机取 1）
        /// 输出：总览（人数 + 分层）+ 个体行（楼层/己方标记 + [#N] index）+ 未采样者合并计数行（防幻觉）。
        /// 保底：总人数 ≤ 预算 → 全列个体；异常 → 返回 null（调用方回退现状）。
        /// </summary>
        private static string BuildPresenceSample(SceneSnapshot snap, Agent self, Agent target)
        {
            try
            {
                if (snap == null || self == null) return null;
                // 🔴 2026-08-15（玩家在场信息，用户质询）：候选池**包含玩家**——随从的目之所及清单
                // 缺了主公 = 叙事不完整（主公在场却不在清单；远距传讯时 LLM 不知道主公不在附近）。
                // 玩家是 Hero → P1 必被采样，个体行带（主公）标记；self（随从自己）仍排除（不看自己）。
                var candidates = snap.Agents
                    .Where(i => i != null && i.Agent != null && i.Agent != self)
                    .ToList();
                int total = candidates.Count;
                if (total == 0) return null;
                // ── 楼层聚类（Z 排序贪心分组）──
                var floors = new List<List<SceneSnapshot.AgentInfo>>();
                foreach (var info in candidates.OrderBy(i => i.Agent.Position.Z))
                {
                    if (floors.Count > 0
                        && Math.Abs(info.Agent.Position.Z - floors[floors.Count - 1][0].Agent.Position.Z) <= FloorTolerance)
                        floors[floors.Count - 1].Add(info);
                    else
                        floors.Add(new List<SceneSnapshot.AgentInfo> { info });
                }
                // 防碎层：聚类出 5+ 层时，最小层并入最近层（山坡/台阶噪声）
                while (floors.Count > 5)
                {
                    var smallest = floors.OrderBy(f => f.Count).First();
                    float z = smallest[0].Agent.Position.Z;
                    var nearest = floors.Where(f => f != smallest)
                        .OrderBy(f => Math.Abs(f[0].Agent.Position.Z - z)).First();
                    nearest.AddRange(smallest);
                    floors.Remove(smallest);
                }
                // ── 层配额：每层保底 1，剩余按人数比例（余数给人数最多层）──
                var quota = new Dictionary<List<SceneSnapshot.AgentInfo>, int>();
                int remaining = Math.Min(SightSampleBudget, total);
                foreach (var f in floors) { quota[f] = 1; remaining--; }
                if (remaining > 0)
                {
                    var extra = new Dictionary<List<SceneSnapshot.AgentInfo>, int>();
                    foreach (var f in floors)
                        extra[f] = (int)Math.Floor(remaining * (double)f.Count / total);
                    int used = extra.Values.Sum();
                    foreach (var f in floors.OrderByDescending(f => f.Count))
                    {
                        if (used >= remaining) break;
                        extra[f]++; used++;
                    }
                    foreach (var f in floors) quota[f] += extra[f];
                }
                // ── 层内采样 ──
                var selected = new List<SceneSnapshot.AgentInfo>();
                var selectedSet = new HashSet<SceneSnapshot.AgentInfo>();
                foreach (var f in floors)
                {
                    var pool = new List<SceneSnapshot.AgentInfo>(f);
                    SceneSnapshot.AgentInfo Pick(Func<SceneSnapshot.AgentInfo, bool> pred)
                    {
                        var hit = pool.FirstOrDefault(pred);
                        if (hit != null) { pool.Remove(hit); return hit; }
                        return null;
                    }
                    int quotaOf = quota[f];
                    // P0 目标（已解析 → 必含；目标段单独描述，此处仅保证计入但不重复列——见下方输出跳过）
                    Pick(i => target != null && i.Agent == target);
                    // P1 Hero
                    while (selected.Count < quotaOf)
                    {
                        var h = Pick(i => (i.Agent.Character as CharacterObject)?.HeroObject != null);
                        if (h == null) break;
                        if (!selectedSet.Contains(h)) { selected.Add(h); selectedSet.Add(h); }
                    }
                    // P1.5 己方（IsFriendlyToPlayer——打架会帮忙的人，必须可见）
                    while (selected.Count < quotaOf)
                    {
                        var h = Pick(i => FriendlinessHelper.IsFriendlyToPlayer(i.Agent));
                        if (h == null) break;
                        selected.Add(h); selectedSet.Add(h);
                    }
                    // P2 独特职业表 各取 1（每个模板的人采样 1 个）
                    foreach (var r in UniqueRoleTable)
                    {
                        if (selected.Count >= quotaOf) break;
                        var h = Pick(i => string.Equals(i.Role, r, StringComparison.OrdinalIgnoreCase));
                        if (h != null) { selected.Add(h); selectedSet.Add(h); }
                    }
                    // P2 独特名字（酒馆店主/带名字的模板 NPC）
                    while (selected.Count < quotaOf)
                    {
                        var h = Pick(IsDistinctiveName);
                        if (h == null) break;
                        selected.Add(h); selectedSet.Add(h);
                    }
                    // P3 近距 15m（用户裁定身边范围）
                    while (selected.Count < quotaOf)
                    {
                        var h = Pick(i => i.Agent.Position.Distance(self.Position) <= 15f);
                        if (h == null) break;
                        selected.Add(h); selectedSet.Add(h);
                    }
                    // P4 空间均匀随机：方位角 8 扇区桶，桶内随机取 1（确定性种子，防抖动）
                    if (selected.Count < quotaOf && pool.Count > 0)
                    {
                        var rng = new Random(f.Count * 7919 + selected.Count * 131 + 17);
                        var buckets = new Dictionary<int, List<SceneSnapshot.AgentInfo>>();
                        foreach (var i in pool)
                        {
                            Vec2 d = i.Agent.Position.AsVec2 - self.Position.AsVec2;
                            float ang = MathF.Atan2(d.Y, d.X);
                            int sec = ((int)MathF.Floor((ang + MathF.PI) / (MathF.PI / 4))) % 8;
                            if (!buckets.TryGetValue(sec, out var b)) { b = new List<SceneSnapshot.AgentInfo>(); buckets[sec] = b; }
                            b.Add(i);
                        }
                        foreach (var sec in buckets.Keys.OrderBy(k => k))
                        {
                            if (selected.Count >= quotaOf) break;
                            var b = buckets[sec];
                            if (b.Count == 0) continue;
                            var h = b[rng.Next(b.Count)];
                            selected.Add(h); selectedSet.Add(h);
                        }
                    }
                }
                // ── 输出组装 ──
                var sb = new StringBuilder();
                // 合并计数行（未采样者；目标由目标段单独描述 → 排除，避免重复）——提前声明供统计用
                var unselected = candidates
                    .Where(c => !selectedSet.Contains(c) && !(target != null && c.Agent == target))
                    .ToList();
                // 统计口径（用户裁定：详写 vs 归入计数）：详写 = 个体行数 + 目标段（目标独立描述）；
                // 合并 = 未采样者。total = 详写 + 合并（自洽）。
                int listedRows = selected.Count(s => !(target != null && s.Agent == target))
                    + (target != null && target.IsActive() ? 1 : 0);
                int mergedCount = unselected.Count;
                // 总览：人数 + 详写/合并比例 + 分层
                var floorParts = floors.Select(f => $"{FloorLabelOf(f[0].Agent, self)} {f.Count} 人");
                sb.AppendLine($"- 此处共 {total} 人在场（详写 {listedRows} 人、其余 {mergedCount} 人归入计数；{string.Join("、", floorParts)}）：");
                DebugLogger.Log($"[RiskScene] 在场采样: 共 {total} 人，详写 {listedRows}，合并 {mergedCount}，楼层 {floors.Count}（{string.Join("、", floorParts)}）");
                // 个体行（目标由目标段单独描述，此处跳过避免重复；楼层 + 己方标记 + [#N]）。
                // 🔴 2026-08-15（视角修正，实机）：方位用 DescribeTargetRelative(self, …) 相对**随从自身**
                // ——PositionDesc 是相对玩家的（"你东南侧"的"你"=玩家），而【目之所及】叙述视角是随从
                //（"你"=随从），同一"你"指两人 = 视角串台。朝向 FacingDesc 保留（谁面朝主公，随从看得见）。
                foreach (var sel in selected
                    .Where(s => !(target != null && s.Agent == target))
                    .OrderBy(s => s.Agent.Position.DistanceSquared(self.Position)))
                {
                    string floor = FloorLabelOf(sel.Agent, self);
                    // 🔴 2026-08-15（玩家在场）：主公特殊标记——随从视角直接叫「主公」（名字即标记，
                    // 不再重复标（主公））；其余己方标（己方）；中立无标记。
                    string mark = sel.Agent == Agent.Main ? "" : FriendlinessHelper.IsFriendlyToPlayer(sel.Agent) ? "（己方）" : "";
                    string name = sel.Agent == Agent.Main
                        ? $"主公[#{sel.Agent.Index}]"
                        : $"{sel.DisplayName ?? sel.Role ?? "某人"}[#{sel.Agent.Index}]";
                    string occ = string.IsNullOrEmpty(sel.Occupation) ? "" : $"（{sel.Occupation}）";
                    string rel = DescribeTargetRelative(self, sel.Agent);
                    sb.AppendLine($"  - [{floor}] {name}{occ}{mark}：{rel}，{sel.FacingDesc}，{sel.State}");
                }
                // 合并计数行（未采样者；目标由目标段单独描述 → 排除，避免重复；unselected 已在上方声明）。
                // 🔴 2026-08-15（叙事口吻，用户裁定）：收尾一句「看不过来」——把采样预算的技术限制转成
                // 随从的亲见局限（铁律：情报来自渠道，禁止上帝视角——未列出的人只有数量没有底细）。
                // 🔴 2026-08-15 人称统一（实机）：段首旁白是第二人称（"你是局中人"），内心独白改用
                // **无主句**（"一眼看不过来"）——避免同一段"你"（旁白指随从）与"我"（独白自指）混用。
                if (unselected.Count > 0)
                {
                    var groups = unselected
                        .GroupBy(i => $"{FloorLabelOf(i.Agent, self)}|{i.Role ?? i.Occupation ?? "镇民"}")
                        .OrderByDescending(g => g.Count());
                    var parts = groups.Select(g =>
                    {
                        var label = g.First().Role ?? g.First().Occupation ?? "镇民";
                        if (string.IsNullOrEmpty(label) || label == "hero") label = g.First().Occupation ?? "镇民";
                        return $"{label}×{g.Count()}（{g.Key.Split('|')[0]}）";
                    });
                    sb.AppendLine($"  - 其余 {string.Join("、", parts)}——人太多，一眼看不过来，只留个印象。");
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RiskScene] 在场采样失败: {ex.Message} → 回退现状");
                return null;
            }
        }        /// <summary>目标相对本 NPC 的方位（以 self 自身朝向为基准——亲见视角，零视角反转）。</summary>
        private static string DescribeTargetRelative(Agent self, Agent target)
        {
            try
            {
                Vec3 diff = target.Position - self.Position;
                diff.z = 0f;
                float dist = diff.Length;
                if (dist < 2f) return "就在你跟前";
                Vec2 look = self.LookDirection.AsVec2.Normalized();
                Vec3 look3 = new Vec3(look.X, look.Y, 0f);
                float f = Vec3.DotProduct(diff, look3) / dist;
                float r = Vec3.DotProduct(diff, new Vec3(-look.Y, look.X, 0f)) / dist;
                string lat = r > 0.35f ? "右" : (r < -0.35f ? "左" : "");
                string lon = f > 0.35f ? "前方" : (f < -0.35f ? "后方" : "");
                string dir = (lat.Length == 0 && lon.Length == 0) ? "正对面" : lat + lon;
                return $"在你{dir}约 {MathF.Ceiling(dist)} 米处";
            }
            catch { return "在附近"; }
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
            // 货币单位走 Settings（默认原版 hYgmzZJX 本地化：第纳尔/Denar；Mod B 可注入"两"）
            return $"- 队伍钱袋现有 {hero.Gold} 枚{Settings.Instance.CurrencyName}可供开销。";
        }

        /// <summary>最近定居点（城镇/城堡/村庄/藏身处，Settlement.All 动态遍历，铁律 5）：
        /// 队伍位置为基准、半径内最近者。玩家/队伍在定居点附近但未进入（路过/城门遇袭/藏身处）时，
        /// 位置事实给"在 X 附近"锚点，禁止答"旷野"（实机 2026-08-16：玩家在吕卡隆附近，随从答"旷野"
        /// 且 LLM 顺势编造"荒草连天"）。半径 15 地图单位（行军约 4.5 单位/天，城镇-村庄 10~20 单位，
        /// 15 = 地平线上可见）；值按 [LocFact] 日志调。public：方案 D（mission_battle 描述）/ G3（犯罪地点）
        /// 复用此 helper（单一实现）。</summary>
        public static string NearestSettlementName(float radius)
        {
            return NearestSettlementName(MobileParty.MainParty, radius);
        }

        /// <summary>同 NearestSettlementName，但以指定 party 的位置为基准（🔴 2026-08-16 分兵补漏：
        /// 分兵随从自己的队伍位置锚点——自己 party 的位置是亲历级，主队版 helper 硬编码 MainParty
        /// 不可复用）。</summary>
        private static string NearestSettlementName(MobileParty party, float radius)
        {
            try
            {
                if (party == null || Campaign.Current == null) return null;
                Vec2 basePos = V.Pos(party);
                Settlement best = null;
                float bestSq = radius * radius;
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    float d = basePos.DistanceSquared(V.Pos(s));
                    if (d < bestSq) { bestSq = d; best = s; }
                }
                if (best == null) return null;
                DebugLogger.Log($"[LocFact] 最近定居点: {best.Name}（距离 {MathF.Sqrt(bestSq):F1} 地图单位，半径 {radius}）");
                return best.Name?.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[LocFact] NearestSettlementName 失败: {ex.Message}");
                return null;
            }
        }

        private static string QueryLocationFact()
        {
            var party = MobileParty.MainParty;
            if (party == null) return "- （此刻无从查知）";
            if (party.CurrentSettlement != null)
                return $"- 此刻队伍正在 {party.CurrentSettlement.Name}。";
            if (party.TargetSettlement != null)
                return $"- 此刻队伍行进在旷野中，正前往 {party.TargetSettlement.Name}。";
            // 🔴 2026-08-16（方案 A）：定居点附近但未进入（路过/藏身处/城门遇袭）→ 最近定居点兜底，
            // 禁止答"旷野"（实机：玩家在吕卡隆附近，随从答"旷野"且 LLM 编造"荒草连天"）
            string near = NearestSettlementName(15f);
            if (near != null)
                return $"- 此刻队伍在 {near} 附近（旷野中）。";
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
            // 🔴 2026-08-16（方案 A3 + G1）：时辰词表（5-7/8-10/11-13/14-16/17-19/20-22/23-4 →
            // 清晨/上午/正午/午后/黄昏/入夜/深夜）+ 天气词（V.GetWeatherAt，信息面 #36 增强）
            return $"- 现在是{GetSeasonName()}、{(CampaignTime.Now.IsDayTime ? "白天" : "夜里")}、{GetTimeOfDayWord()}，" +
                   $"本季第 {CampaignTime.Now.GetDayOfSeason + 1} 天，{GetWeatherWord()}。";
        }

        /// <summary>时辰词（prompt 材料，铁律 13 豁免；与方向词同口径）。</summary>
        private static string GetTimeOfDayWord()
        {
            try
            {
                int hour = (int)(CampaignTime.Now.ToHours % 24);
                return hour switch
                {
                    >= 5 and <= 7 => "清晨",
                    >= 8 and <= 10 => "上午",
                    >= 11 and <= 13 => "正午",
                    >= 14 and <= 16 => "午后",
                    >= 17 and <= 19 => "黄昏",
                    >= 20 and <= 22 => "入夜",
                    _ => "深夜",
                };
            }
            catch { return "白日"; }
        }

        /// <summary>天气词（prompt 材料，铁律 13 豁免；WeatherEvent 枚举 → 词）。</summary>
        private static string GetWeatherWord()
        {
            try
            {
                var party = MobileParty.MainParty;
                if (party == null) return "天气如常";
                return V.GetWeatherAt(V.Pos(party)) switch
                {
                    TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.LightRain => "细雨绵绵",
                    TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.HeavyRain => "大雨滂沱",
                    TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Snowy => "白雪纷飞",
                    TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Blizzard => "风雪漫天",
                    TaleWorlds.CampaignSystem.ComponentInterfaces.MapWeatherModel.WeatherEvent.Storm => "狂风暴雨",
                    _ => "晴空万里",
                };
            }
            catch { return "天气如常"; }
        }

        /// <summary>任务日志里的定居点链接（QuestJournal 转储实锤格式：
        /// <a style="Link.Settlement" href="event:Settlement-village_ES3_2">特维亚</a>）——
        /// 提取 StringId 后走 Settlement.Find（铁律 5 动态查找，不硬编码 ID）。</summary>
        private static readonly System.Text.RegularExpressions.Regex QuestSettlementLinkRegex =
            new System.Text.RegularExpressions.Regex(@"event:Settlement-([A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static string QueryQuestFact()
        {
            var qm = Campaign.Current?.QuestManager;
            if (qm == null || qm.Quests == null || qm.Quests.Count == 0) return "- 眼下没有进行中的委托。";
            var names = string.Join("、", qm.Quests.Take(3).Select(q => q.Title?.ToString() ?? "一桩委托"));
            var sb = new StringBuilder();
            sb.AppendLine($"- 进行中的委托 {qm.Quests.Count} 桩（如：{names}）。");
            // 🔴 2026-08-16（追问详情防编造）：标题不够——玩家追问"哪个村子/什么差事"时 LLM 手头
            // 只有标题，会把 E 段附近的村庄名当任务目标（实机：村民需要帮助 → LLM 答"萨戈拉"，
            // 实际是特维亚）。QuestBase 无 TargetSettlement（ilspycmd 实锤），目标地取两路：
            // ① QuestGiver.CurrentSettlement（委托人所在地，村老在村、贵族在堡；村民任务委托人是
            // "男孩"非 Hero 时会 null）；② 任务日志里的定居点链接 <a href="event:Settlement-{StringId}">
            // （QuestJournal 转储实锤格式）→ 正则提取 StringId → Settlement.Find 本地化村名（铁律 5）。
            foreach (var q in qm.Quests.Take(3))
            {
                string place = null;
                try
                {
                    if (q?.QuestGiver?.CurrentSettlement != null)
                        place = q.QuestGiver.CurrentSettlement.Name?.ToString();
                    if (place == null && q?.JournalEntries != null)
                    {
                        foreach (var entry in q.JournalEntries)
                        {
                            // JournalLog.LogText（反编译实锤属性名，非 Text）
                            var m = QuestSettlementLinkRegex.Match(entry?.LogText?.ToString() ?? "");
                            if (m.Success)
                            {
                                var s = Settlement.Find(m.Groups[1].Value);
                                if (s != null) { place = s.Name?.ToString(); break; }
                            }
                        }
                    }
                }
                catch { }
                if (!string.IsNullOrEmpty(place))
                    sb.AppendLine($"- {q.Title} 的差事在 {place}。");
            }
            return sb.ToString();
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

        /// <summary>队伍成员名单（幻觉修复）：有名有姓的 Hero 成员 + 无名士兵按兵种构成。
        /// 玩家问"其他人呢/随从是谁/还有谁"时注入——名单完整（含兵种）LLM 就不会编造不存在的人。
        /// 2026-08-10 v2：去掉"没有其他随从学徒侍从"式负向列举（覆盖不全且生硬），改正面收尾
        /// "队伍里的人就这些了"——防幻觉由 IM 纪律的事实自检兜底。</summary>
        private static string QueryMemberFact()
        {
            var sb = new StringBuilder();
            try
            {
                // 有名有姓的成员（频道成员 = roster 里的 Hero，实时取）
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                if (members != null && members.Count > 0)
                    sb.AppendLine("- 有名有姓的成员：" + string.Join("、", members.Select(m => m.Name?.ToString() ?? "无名")) + "。");
            }
            catch { }
            // 无名士兵按兵种（复用 QueryPartyFacts 的构成逻辑，实时取）
            try
            {
                var party = MobileParty.MainParty;
                if (party?.MemberRoster != null)
                {
                    var top = party.MemberRoster.GetTroopRoster()
                        .Where(e => e.Number > 0 && e.Character != null && !e.Character.IsHero)
                        .OrderByDescending(e => e.Number)
                        .Take(3)
                        .Select(e => $"{e.Character.Name} {e.Number} 人")
                        .ToList();
                    if (top.Count > 0)
                        sb.AppendLine("- 无名士兵（主要兵力）：" + string.Join("、", top) + "。");
                }
            }
            catch { }
            sb.AppendLine("- 队伍里的人就这些了。");
            return sb.ToString();
        }

        /// <summary>问句兜底概要：一行式核心状态（队伍成员版含同行者隐私，外人版只有普世事实）。</summary>
        private static string QuerySummary(bool isPartyMember, bool numericCovered = false)
        {
            var sb = new StringBuilder();
            var party = MobileParty.MainParty;
            // 🔴 2026-08-16（prompt 精简）：numericCovered（I1 已注入）→ 跳过队伍钱/粮/兵行（I1 有同值）
            if (isPartyMember && party != null && !numericCovered)
            {
                sb.AppendLine($"- 队伍现有 {party.MemberRoster.TotalRegulars} 名士兵、{party.MemberRoster.TotalHeroes} 名将领；" +
                              $"钱袋 {Hero.MainHero?.Gold ?? 0} {Settings.Instance.CurrencyName}；粮草约 {party.Food:0.0} 天。");
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
            // 🔴 2026-08-16（prompt 精简）：季节行同样被 I1 覆盖（I1 有"夏季、白天、午后、晴空万里"）
            if (!numericCovered)
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

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 E）：campaign 版【目之所及】（环境感知视野）
        // ═══════════════════════════════════════════════════════════
        /// <summary>
        /// 🔴 2026-08-16（方案 E）：campaign 版【目之所及】——大地图上随从"亲眼望见"周围环境。
        /// 结构对齐 BuildSceneAwareness 的【此刻处境】叙事：
        /// - 当前位置：复用方案 A 判定链（CurrentSettlement / TargetSettlement / NearestSettlementName / 旷野）
        /// - 望得见的定居点（半径 25 地图单位，IsVisible 过滤——城镇/城堡常显，被迷雾挡的村庄/藏身处不给；
        ///   按距离排序取前 5，超出合并计数"看不过来"叙事——复用【目之所及】叙事口径）
        /// - 望得见的部队（半径 15，party.IsVisible 过滤，排除主队自身；取前 5，超出合并计数）
        /// 方位 = 地图绝对方向（campaign 俯视无玩家朝向）：atan2(deltaY, deltaX) 分 8 扇区
        ///（方向词为 prompt 材料，豁免铁律 13）；距离单位转"里"（1 地图单位 ≈ 10 里：
        /// 行军约 4.5 单位/天 ≈ 40~50 里/天，2026-08-16 修正原稿 1 单位≈1 里量纲差）。
        /// 认知边界：仅队伍成员注入（同行=亲见，与方案 D 感知层、方案 A 可见性裁剪同口径）——
        /// 调用方（ImReplyService.ScheduleReply）按回复者身份判定（IsInMainParty + 分兵，
        /// 2026-08-16 裁定：注入看说话人身份不看频道）。
        /// 全部 try/catch（铁律 1）；构建行打 [CampaignSight] 日志（几定居点/几支部队，供调半径）。
        /// ⚠️ 主线程调用（引擎对象只读主线程）。
        /// </summary>
        public static string BuildCampaignAwareness()
        {
            try
            {
                if (Campaign.Current == null || MobileParty.MainParty == null) return "";
                var party = MobileParty.MainParty;
                var sb = new StringBuilder();
                sb.AppendLine("【此刻处境（大地图）】");
                // 当前位置（复用方案 A 判定链）
                if (party.CurrentSettlement != null)
                    sb.AppendLine($"- 队伍正在 {party.CurrentSettlement.Name}。");
                else if (party.TargetSettlement != null)
                    sb.AppendLine($"- 队伍行进在旷野中，正前往 {party.TargetSettlement.Name}。");
                else
                {
                    string near = NearestSettlementName(15f);
                    sb.AppendLine(near != null ? $"- 队伍在 {near} 附近（旷野中）。" : "- 队伍行进在旷野中。");
                }
                Vec2 basePos = V.Pos(party);
                // ── 望得见的定居点（半径 25，IsVisible 过滤，按距离排序取前 5）──
                var settlements = new List<(Settlement s, float d)>();
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    try { if (!s.IsVisible) continue; } catch { continue; }
                    float d = basePos.DistanceSquared(V.Pos(s));
                    if (d <= 25f * 25f) settlements.Add((s, d));
                }
                settlements.Sort((a, b) => a.d.CompareTo(b.d));
                int shownS = 0;
                foreach (var (s, d) in settlements)
                {
                    if (shownS >= 5) break;
                    shownS++;
                    sb.AppendLine("- " + BuildSettlementSightLine(s, MathF.Sqrt(d)));
                }
                if (settlements.Count > shownS)
                    sb.AppendLine($"- 还有 {settlements.Count - shownS} 处更远的定居点，一眼看不过来。");
                // ── 望得见的部队（半径 15，IsVisible 过滤，排除主队自身与守军；取前 5）──
                var parties = new List<(MobileParty p, float d)>();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || p == party) continue;
                    try { if (!p.IsVisible) continue; } catch { continue; }
                    if (p.IsGarrison || p.IsMainParty) continue;   // 守军不在地图游走
                    float d = basePos.DistanceSquared(V.Pos(p));
                    if (d <= 15f * 15f) parties.Add((p, d));
                }
                parties.Sort((a, b) => a.d.CompareTo(b.d));
                int shownP = 0;
                foreach (var (p, d) in parties)
                {
                    if (shownP >= 5) break;
                    shownP++;
                    sb.AppendLine("- " + BuildPartySightLine(p, MathF.Sqrt(d)));
                }
                if (parties.Count > shownP)
                    sb.AppendLine($"- 更远处还有 {parties.Count - shownP} 支部队，看不太清。");
                DebugLogger.Log($"[CampaignSight] 定居点 {settlements.Count}（详写 {shownS}）、部队 {parties.Count}（详写 {shownP}）");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[CampaignSight] 构建失败: {ex.Message}");
                return "";
            }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 J3 补漏）：分兵随从自己的队伍状态（【分兵近况】）
        // ═══════════════════════════════════════════════════════════
        /// <summary>
        /// 🔴 2026-08-16（方案 J3 补漏）：分兵随从自己的队伍状态——第一人称亲历（我是这支队伍的统帅，
        /// 知道自己率部在哪、在干什么），补【此刻处境（大地图）】被 L1 裁剪后的自我定位空白。
        /// 实机（2026-08-16 18:05）：分兵随从被问"你的队伍要去哪"答"就在离主队不远处的旷野上扎营候命"，
        /// 实际部队已因 initiative flee 自行他往——prompt 里唯一的动态信息是分兵瞬间的旁白，AI 一偏离即失实。
        /// 认知边界：只注入**自己的** party 状态（位置/AI 行为/兵力），主队信息（位置/账目/物资）维持裁剪。
        /// 调用方（ImReplyService.ScheduleReply）按 PartySplitFlow.IsSplitPartyLeader 判定。
        /// 全部 try/catch（铁律 1）；构建行打 [Party] 日志（与分兵/归队执行日志同标签，验证用）。
        /// ⚠️ 主线程调用（引擎对象只读主线程）。
        /// </summary>
        public static string BuildSplitPartyAwareness(Hero hero)
        {
            try
            {
                if (hero == null || Campaign.Current == null) return "";
                var party = hero.PartyBelongedTo;
                if (party == null || party == MobileParty.MainParty) return "";
                var sb = new StringBuilder();
                sb.AppendLine("【分兵近况】");
                // 自己的队伍位置（复用方案 A 判定链，基准 = 自己的 party）
                if (party.CurrentSettlement != null)
                    sb.AppendLine($"- 我正率部在 {party.CurrentSettlement.Name}。");
                else if (party.TargetSettlement != null)
                    sb.AppendLine($"- 我正率部行进在旷野中，前往 {party.TargetSettlement.Name}。");
                else
                {
                    string near = NearestSettlementName(party, 15f);
                    sb.AppendLine(near != null ? $"- 我正率部在 {near} 附近（旷野中）。" : "- 我正率部行进在旷野中。");
                }
                sb.AppendLine($"- 我手下约 {party.MemberRoster.TotalRegulars} 名兵。");
                // AI 行为（DefaultBehavior → 中文词：跟随/前往/巡逻/追击/守卫/待命/躲避）
                string ai = DescribePartyAi(party);
                if (ai != null) sb.AppendLine(ai);
                DebugLogger.Log($"[Party] BuildSplitPartyAwareness {hero.Name}（{party.MemberRoster.TotalRegulars} 兵，行为 {party.DefaultBehavior}）");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Party] BuildSplitPartyAwareness 失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>队伍 AI 行为 → 中文词（prompt 材料，豁免铁律 13；DefaultBehavior 反编译实锤
        /// MobileParty.SetMoveEscortParty/SetMoveGoToSettlement 等直接写此字段）。</summary>
        private static string DescribePartyAi(MobileParty party)
        {
            try
            {
                switch (party.DefaultBehavior)
                {
                    case AiBehavior.EscortParty:
                        return "- 队伍眼下的差事：率部跟随主公。";
                    case AiBehavior.GoToSettlement:
                    case AiBehavior.RaidSettlement:
                    case AiBehavior.BesiegeSettlement:
                        var ts = party.TargetSettlement;
                        if (ts != null)
                            return party.DefaultBehavior == AiBehavior.GoToSettlement
                                ? $"- 队伍眼下的差事：率部前往 {ts.Name}。"
                                : $"- 队伍眼下的差事：率部围住 {ts.Name}。";
                        return null;
                    case AiBehavior.PatrolAroundPoint:
                        return "- 队伍眼下的差事：率部在附近巡逻。";
                    case AiBehavior.EngageParty:
                        var tp = party.ShortTermTargetParty ?? party.TargetParty;
                        return tp != null ? $"- 队伍眼下的差事：率部追击 {tp.Name}。" : "- 队伍眼下的差事：率部与敌交战。";
                    case AiBehavior.DefendSettlement:
                        var ds = party.TargetSettlement;
                        return ds != null ? $"- 队伍眼下的差事：率部守卫 {ds.Name}。" : null;
                    case AiBehavior.Hold:
                    case AiBehavior.None:
                        return "- 队伍眼下的差事：原地待命。";
                    case AiBehavior.FleeToPoint:
                    case AiBehavior.FleeToGate:
                    case AiBehavior.FleeToParty:
                        return "- 队伍眼下的差事：正在躲避敌情。";
                    default:
                        return null;
                }
            }
            catch { return null; }
        }

        /// <summary>定居点视野行：方位 + 距离 + 名字 + 类型 + 所有者/敌我/被围（信息面 #2 增强）。
        /// 🔴 null 兜底（2026-08-16 审查）：OwnerClan 为 null（藏身处/无主定居点）→ 整段不写所有者，
        /// 禁止拼出"的城"破句；被围 = settlement.BesiegerCamp != null（玩家地图能看到围城图标，同行亲见）。</summary>
        private static string BuildSettlementSightLine(Settlement s, float distMapUnits)
        {
            try
            {
                string dir = MapDirectionWord(V.Pos(MobileParty.MainParty), V.Pos(s));
                string li = MapDistLi(distMapUnits);
                string type = s.IsTown ? "城镇" : s.IsCastle ? "城堡" : s.IsVillage ? "村庄" : "藏身处";
                string ownerPart = "";
                try
                {
                    if (s.OwnerClan != null)
                    {
                        if (s.OwnerClan == Clan.PlayerClan)
                            ownerPart = "，咱们的地盘";
                        else
                        {
                            string kName = s.OwnerClan.Kingdom?.Name?.ToString();
                            if (kName != null && IsKingdomAtWarWithPlayer(s.OwnerClan.Kingdom))
                                ownerPart = $"，{kName}（敌国）的城";
                            else if (kName != null)
                                ownerPart = $"，{kName}的城";
                            // 无王国领主（独立/雇佣兵）→ 不写所有者段
                        }
                    }
                }
                catch { }
                string siege = "";
                try { if (s.SiegeEvent != null) siege = "，正被围"; } catch { }
                string name = s.Name?.ToString() ?? "无名之地";
                return $"{dir}约 {li} 外是 {name}（{type}{ownerPart}{siege}）";
            }
            catch { return ""; }
        }

        /// <summary>部队视野行：方位 + 距离 + 类型 + 敌我 + 规模 + 战力对比（信息面 #3 增强）。
        /// 类型词（已验证 API）：IsCaravan→商队 / IsBandit→匪徒（IsBanditBossParty→匪首）/ IsVillager→农夫 /
        /// IsLordParty→领主部队（名字=party.Name"XXX 的部队"）/ IsMilitia→民兵；IsGarrison 已排除。
        /// 战力对比五档：我方（MainParty 同口径合计）比 → 远超/人多/均势/不如/差得远。</summary>
        private static string BuildPartySightLine(MobileParty p, float distMapUnits)
        {
            try
            {
                string dir = MapDirectionWord(V.Pos(MobileParty.MainParty), V.Pos(p));
                string li = MapDistLi(distMapUnits);
                string type;
                if (p.IsCaravan) type = "商队";
                else if (p.IsBandit) type = p.IsBanditBossParty ? "匪首的匪帮" : "匪徒";
                else if (p.IsVillager) type = "农夫";
                else if (p.IsLordParty) type = p.Name?.ToString() ?? "领主部队";
                else if (p.IsMilitia) type = "民兵";
                else type = "部队";
                string side;
                try
                {
                    if (p.MapFaction != null && p.MapFaction.IsAtWarWith(Clan.PlayerClan))
                        side = "敌军";
                    else if (p.MapFaction == Clan.PlayerClan
                        || (Clan.PlayerClan?.Kingdom != null && p.MapFaction == Clan.PlayerClan.Kingdom))
                        side = "友军";
                    else side = "中立";
                }
                catch { side = "中立"; }
                int enemyCount = 0;
                try { enemyCount = p.MemberRoster.TotalRegulars + p.MemberRoster.TotalHeroes; } catch { }
                if (enemyCount <= 0)
                    return $"{dir}约 {li} 外有一支{type}（{side}）";
                string scale = "";
                try
                {
                    int myCount = MobileParty.MainParty.MemberRoster.TotalRegulars + MobileParty.MainParty.MemberRoster.TotalHeroes;
                    if (myCount > 0)
                    {
                        float ratio = enemyCount / (float)myCount;
                        scale = ratio >= 2.5f ? "，远超咱们人多" : ratio >= 1.3f ? "，比咱们人多"
                            : ratio >= 0.8f ? "，势均力敌" : ratio >= 0.4f ? "，不如咱们" : "，比咱们差得远";
                    }
                }
                catch { }
                return $"{dir}约 {li} 外有一支{type}（{side}，约 {enemyCount} 人{scale}）";
            }
            catch { return ""; }
        }

        /// <summary>地图绝对方向（campaign 俯视无玩家朝向）：atan2(deltaY, deltaX) 8 扇区。
        /// 方向词为 prompt 材料，豁免铁律 13。地图坐标：+X 东、+Y 北。</summary>
        private static string MapDirectionWord(Vec2 from, Vec2 to)
        {
            try
            {
                float ang = MathF.Atan2(to.Y - from.Y, to.X - from.X);
                string[] words = { "东边", "东北", "北边", "西北", "西边", "西南", "南边", "东南" };
                int idx = (int)MathF.Round(ang / (MathF.PI / 4)) & 7;
                return words[idx];
            }
            catch { return "附近"; }
        }

        /// <summary>地图距离 → "里"（1 地图单位 ≈ 10 里：行军约 4.5 单位/天 ≈ 40~50 里/天，2026-08-16 修正）。</summary>
        private static string MapDistLi(float distMapUnits)
        {
            int li = (int)MathF.Round(distMapUnits * 10f);
            return li <= 0 ? "跟前" : $"{li} 里";
        }

        /// <summary>王国是否与玩家王国交战（双方无王国 → 非交战）。</summary>
        private static bool IsKingdomAtWarWithPlayer(Kingdom k)
        {
            try
            {
                var pk = Clan.PlayerClan?.Kingdom;
                if (pk == null || k == pk) return false;
                return pk.IsAtWarWith(k);
            }
            catch { return false; }
        }

        /// <summary>
        /// 🔴 2026-08-16（方案 H4.3 L3 邻军互见段——函数 + 规则落地，入口另议）：
        /// campaign 对部队喊话的对话入口不存在（grep 无命中），本函数为 L3 预留——
        /// 对等互见：我方概况（部队名/规模/首领）vs 对方（同款）。campaign 部队规模可见 = 战场侦察，
        /// 可注入；**禁止队伍私事**（钱/粮/物资/犯罪——L3 不注入主队私事）。入口落地后调用。
        /// </summary>
        public static string BuildPartyEncounterAwareness(MobileParty other)
        {
            try
            {
                if (other == null || MobileParty.MainParty == null) return "";
                var sb = new StringBuilder();
                sb.AppendLine("【互见】");
                var main = MobileParty.MainParty;
                sb.AppendLine($"- 你方：{main.Name}（{main.MemberRoster.TotalRegulars} 人，首领 {main.LeaderHero?.Name?.ToString() ?? Hero.MainHero?.Name?.ToString()}）。");
                sb.AppendLine($"- 对方：{other.Name}（{other.MemberRoster.TotalRegulars} 人，首领 {other.LeaderHero?.Name?.ToString() ?? "无名"}）。");
                return sb.ToString();
            }
            catch { return ""; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 F + G2 + G7）：自我认知（第一人称亲见）
        // ═══════════════════════════════════════════════════════════
        /// <summary>
        /// 🔴 2026-08-16（方案 F + G2 + G7 + 在押认知）：自我认知——随从知道自己身上的装备、等级武艺、
        /// 队伍物资（物品可能很多，需合并简化），以及主公的行头（同行亲见）。
        /// 【我的状态】第一人称亲见——谁都知道自己穿什么、几斤几两，无认知边界，任何 Hero 对话注入；
        /// 【主公的行头】同行亲见，仅 isPartyMember 注入（玩家 UI 私密物品不注入，装备属外观亲见）；
        /// 【队伍物资】同行亲见，仅 isPartyMember 注入（物品多 → 5 类合并简化，防几百行刷屏）。
        /// G7 血况：mission 内且是 Agent → 血况三档（Agent.Health/HealthLimit；弹药仍不做——数据口径不稳）。
        /// 🔴 在押认知（2026-08-16 用户裁定：被俘随从必须知道自己被俘）：第一人称亲见——被关在哪
        /// 自己最清楚，无认知边界；实机百草药僧在押却答「主公，我在」，完全不知道自己被关着。
        /// 全部 try/catch；构建行打 [SelfAware] 日志。⚠️ 主线程调用（引擎对象只读主线程）。
        /// </summary>
        public static string BuildSelfAwareness(string heroId, bool isPartyMember)
        {
            try
            {
                if (string.IsNullOrEmpty(heroId)) return "";
                Hero hero = null;
                try { hero = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == heroId); } catch { }
                if (hero == null) return "";
                var sb = new StringBuilder();
                sb.AppendLine("【我的状态】");
                string gear = BuildEquipmentLine(hero, firstPerson: true);
                if (!string.IsNullOrEmpty(gear)) sb.AppendLine(gear);
                sb.AppendLine(BuildLevelSkillLine(hero));
                string wound = BuildSelfHealthLine(heroId);
                if (!string.IsNullOrEmpty(wound)) sb.AppendLine(wound);
                // 🔴 2026-08-16（在押认知）：被关押是随从自身处境（第一人称亲见）——答「我们在哪」
                // 时应说「我被关在 X，主公的行踪我不知晓」，而不是报主公位置或假装正常
                string detention = BuildDetentionLine(hero);
                if (!string.IsNullOrEmpty(detention)) sb.AppendLine(detention);
                if (isPartyMember && Hero.MainHero != null)
                {
                    string lordGear = BuildEquipmentLine(Hero.MainHero, firstPerson: false);
                    if (!string.IsNullOrEmpty(lordGear))
                    {
                        sb.AppendLine("【主公的行头】");
                        sb.AppendLine(lordGear);
                    }
                }
                if (isPartyMember)
                {
                    string supplies = BuildPartySuppliesLine(hero);
                    if (!string.IsNullOrEmpty(supplies))
                    {
                        sb.AppendLine("【队伍物资】");
                        sb.AppendLine(supplies);
                    }
                }
                DebugLogger.Log($"[SelfAware] {hero.Name} 自我认知构建完成（{sb.Length} 字符）");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[SelfAware] 构建失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>在押认知一行（第一人称亲见，prompt 材料豁免铁律 13）：
        /// 「我如今被关押在 {X}，身陷囹圄，无法与主公的队伍同行。」
        /// 数据源 = CompanionDetentionBehavior.GetDetentionSettlement（登记表 + 引擎实况校验）。
        /// 不在押 → null（零开销）。⚠️ 主线程调用。</summary>
        private static string BuildDetentionLine(Hero hero)
        {
            try
            {
                if (hero == null) return null;
                var jail = CompanionDetentionBehavior.GetDetentionSettlement(hero);
                if (jail == null) return null;
                string jailName = jail.Name?.ToString() ?? "某处";
                return $"我如今被关押在 {jailName}，身陷囹圄，无法与主公的队伍同行。";
            }
            catch { return null; }
        }

        /// <summary>装备一行式（部位枚举：头/身/腿/靴/武器0-2/坐骑；空部位省略；物品名引擎本地化）。
        /// firstPerson=true → "我这身行头：…"（随从自述）；false → "主公的打扮：…"（亲见转述）。
        /// hero.BattleEquipment = 战斗装备（随等级变）；逐位 null-guard（铁律 1/2）。</summary>
        private static string BuildEquipmentLine(Hero hero, bool firstPerson)
        {
            try
            {
                if (hero?.BattleEquipment == null) return null;
                var eq = hero.BattleEquipment;
                string Get(EquipmentIndex i)
                {
                    try
                    {
                        var el = eq[i];
                        if (el.IsEmpty || el.Item == null) return null;
                        string n = el.Item.Name?.ToString();
                        return string.IsNullOrEmpty(n) ? null : n;
                    }
                    catch { return null; }
                }
                var parts = new List<string>();
                string head = Get(EquipmentIndex.Head);
                if (!string.IsNullOrEmpty(head)) parts.Add($"头戴{head}");
                string body = Get(EquipmentIndex.Body);
                if (!string.IsNullOrEmpty(body)) parts.Add($"身穿{body}");
                string leg = Get(EquipmentIndex.Leg);
                if (!string.IsNullOrEmpty(leg)) parts.Add($"脚蹬{leg}");
                // 🔴 2026-08-16（方案 F 补漏，P3）：手套部位（ilspycmd 实锤 EquipmentIndex 无 Boots 位——
                // 靴子/护腿挂在 Leg 槽，已由上面 leg 覆盖；Gloves 位存在）
                string gloves = Get(EquipmentIndex.Gloves);
                if (!string.IsNullOrEmpty(gloves)) parts.Add($"手戴{gloves}");
                string w0 = Get(EquipmentIndex.Weapon0);
                if (!string.IsNullOrEmpty(w0)) parts.Add($"手持{w0}");
                string w1 = Get(EquipmentIndex.Weapon1);
                if (!string.IsNullOrEmpty(w1)) parts.Add($"另一手{w1}");
                string w2 = Get(EquipmentIndex.Weapon2);
                if (!string.IsNullOrEmpty(w2)) parts.Add($"背着{w2}");
                string horse = Get(EquipmentIndex.Horse);
                if (!string.IsNullOrEmpty(horse)) parts.Add($"跨下{horse}");
                if (parts.Count == 0) return null;
                return firstPerson
                    ? $"我这身行头：{string.Join("，", parts)}。"
                    : $"主公的打扮：{string.Join("，", parts)}。";
            }
            catch { return null; }
        }

        /// <summary>等级 + 技能前 3（MBObjectManager 动态遍历已注册技能——QuerySkillFact 同款，铁律 5）。</summary>
        private static string BuildLevelSkillLine(Hero hero)
        {
            try
            {
                var skills = MBObjectManager.Instance.GetObjectTypeList<SkillObject>()
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Name?.ToString()) && hero.GetSkillValue(s) > 0)
                    .OrderByDescending(s => hero.GetSkillValue(s))
                    .Take(3)
                    .Select(s => $"{s.Name} {hero.GetSkillValue(s)}")
                    .ToList();
                if (skills.Count == 0)
                    return $"我如今 {hero.Level} 级，还没精研出什么拿手的技艺。";
                return $"我如今 {hero.Level} 级，练得最熟的几手：{string.Join("、", skills)}。";
            }
            catch { return $"我如今 {hero.Level} 级。"; }
        }

        /// <summary>G7 自身血况（mission 内且是 Agent）：&lt;0.3 重伤 / &lt;0.7 挂彩 / 否则状态正好；
        /// 骑乘时坐骑同款阈值（&lt;0.7 → "跨下的马也受了伤"）。弹药仍不做（武器槽弹药数据口径不稳）。</summary>
        private static string BuildSelfHealthLine(string heroId)
        {
            try
            {
                if (Mission.Current == null || Agent.Main == null) return "";
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a == Agent.Main) continue;
                    var h = (a.Character as CharacterObject)?.HeroObject;
                    if (h == null || h.StringId != heroId) continue;
                    if (a.HealthLimit <= 0f) return null;
                    float ratio = a.Health / a.HealthLimit;
                    string line = ratio < 0.3f ? "我带着重伤" : ratio < 0.7f ? "我挂了彩" : "我状态正好";
                    try
                    {
                        if (a.HasMount && a.MountAgent != null && a.MountAgent.HealthLimit > 0f
                            && a.MountAgent.Health / a.MountAgent.HealthLimit < 0.7f)
                            line += "，跨下的马也受了伤";
                    }
                    catch { }
                    return line + "。";
                }
                return "";
            }
            catch { return ""; }
        }

        /// <summary>队伍物资一行式（5 类合并简化：IsFood→食物 / IsMountable→坐骑 / IsAnimal→牲畜 /
        /// IsTradeGood→货物 / 其余→杂物；🔴 判定顺序 IsMountable 先于 IsAnimal（马匹同时满足两 flag，
        /// 防重复计数）；按数量降序取前 5 类；0 的类别不写；数量 = ItemRoster 元素 Amount 合计）。
        /// 分兵随从 = 自己带的 party 的账目（🔴 2026-08-16 审查修正：原稿硬编码 MainParty——分兵（J）后
        /// 随从报的应是**自己带的 party**；主队随从自然落到 MainParty）。</summary>
        private static string BuildPartySuppliesLine(Hero hero)
        {
            try
            {
                MobileParty party = null;
                try
                {
                    // 🔴 Hero.PartyBelongedTo 返回 MobileParty（直接比较主队；分兵随从 = 自己带的 party）
                    if (hero?.PartyBelongedTo != null && hero.PartyBelongedTo != MobileParty.MainParty)
                        party = hero.PartyBelongedTo;
                }
                catch { }
                if (party == null) party = MobileParty.MainParty;
                if (party?.ItemRoster == null) return null;
                long food = 0, mounts = 0, animals = 0, goods = 0, misc = 0;
                foreach (var el in party.ItemRoster)
                {
                    // ItemRosterElement/EquipmentElement 是 struct（无 null 语义）——用 IsEmpty 判定
                    if (el.IsEmpty || el.EquipmentElement.IsEmpty || el.EquipmentElement.Item == null) continue;
                    var item = el.EquipmentElement.Item;
                    int n = el.Amount;
                    if (n <= 0) continue;
                    if (item.IsFood) food += n;
                    else if (item.IsMountable) mounts += n;      // 先于 IsAnimal（马匹同时满足两 flag）
                    else if (item.IsAnimal) animals += n;
                    else if (item.IsTradeGood) goods += n;
                    else misc += n;
                }
                var cats = new List<(string label, long count)>();
                if (food > 0) cats.Add(("食物", food));
                if (mounts > 0) cats.Add(("坐骑", mounts));
                if (animals > 0) cats.Add(("牲畜", animals));
                if (goods > 0) cats.Add(("货物", goods));
                if (misc > 0) cats.Add(("杂物", misc));
                cats.Sort((a, b) => b.count.CompareTo(a.count));
                cats = cats.Take(5).ToList();
                if (cats.Count == 0) return null;
                return $"咱们队伍带着：{string.Join("、", cats.Select(c => $"{c.label} ×{c.count}"))}。";
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 G4/G5/G9/G3③）：信息面收官主题查询
        // ═══════════════════════════════════════════════════════════

        /// <summary>G4 竞技场/比武（信息面 #13）：当前在城镇且 Town.HasTournament → 今日比武。
        /// 只报"今日"（TournamentGame 只在当天存在，不预测未来日程）。
        /// ✅ 实锤：v1.4.8 Town 无公开 TournamentGame 属性，HasTournament = TournamentManager.GetTournamentGame。</summary>
        private static string QueryTournamentFact()
        {
            var town = MobileParty.MainParty?.CurrentSettlement?.Town;
            if (town == null) return "- 队伍眼下不在城中，比武的消息无从查知。";
            try
            {
                return town.HasTournament
                    ? $"- {town.Name} 今日正有一场比武，冠军可得奖赏。"
                    : "- 今日城里没有比武的安排。";
            }
            catch { return "- 今日比武的消息无从查知。"; }
        }

        /// <summary>G5 市场物价（信息面 #23）：仅在城内（CurrentSettlement 为城镇）注入——
        /// "市场行情：谷物 X 第纳尔一石、马匹 Y、羊毛 Z…"（3~5 样）。
        /// ✅ IMarketData.GetPrice(ItemObject, MobileParty, bool, PartyBase) 实锤 4 参（v1.4.8）。
        /// 铁律 5 两轮策略：第一轮预设常见商品 ID（谷物/马/羊毛/铁/面粉）；第二轮 GetObject 谓词兜底
        ///（CategoryId 匹配食品/坐骑/贸易品——确定性 OrderBy StringId）。</summary>
        private static string QueryMarketFact()
        {
            var town = MobileParty.MainParty?.CurrentSettlement?.Town;
            if (town == null || town.MarketData == null) return "- 队伍眼下不在城中，物价无从查知。";
            try
            {
                var items = new List<ItemObject>();
                string[] presetIds = { "grain", "horse", "wool", "iron", "flour" };
                foreach (var id in presetIds)
                {
                    try
                    {
                        var it = MBObjectManager.Instance.GetObject<ItemObject>(id);
                        if (it != null && !items.Contains(it)) items.Add(it);
                    }
                    catch { }
                }
                if (items.Count < 3)
                {
                    try
                    {
                        var extra = MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                            .Where(o => o != null && (o.IsFood || o.IsMountable || o.IsTradeGood)
                                && !items.Contains(o))
                            .OrderBy(o => o.StringId)
                            .Take(5 - items.Count);
                        foreach (var it in extra) items.Add(it);
                    }
                    catch { }
                }
                var parts = new List<string>();
                foreach (var it in items.Take(5))
                {
                    try
                    {
                        int price = town.MarketData.GetPrice(it, MobileParty.MainParty, false, null);
                        parts.Add($"{it.Name} {price}");
                    }
                    catch { }
                }
                if (parts.Count == 0) return "- 城里的行情一时问不清楚。";
                return "- 市场行情：" + string.Join("、", parts) + "。";
            }
            catch { return "- 城里的行情一时问不清楚。"; }
        }

        /// <summary>G9 玩法建议（赚钱途径，信息面基础认知）：普世途径清单（派生式组装——从已有事实取，
        /// 不新造数据，逐条 try/catch）。零途径 → 常识兜底（普世常识，非具体数据）。</summary>
        private static string QueryMoneyMakingFact()
        {
            try
            {
                var ways = new List<string>();
                var town = MobileParty.MainParty?.CurrentSettlement?.Town;
                // 附近城镇今日有比武（G4）
                try { if (town != null && town.HasTournament) ways.Add($"{town.Name} 今日有比武，冠军有赏钱"); } catch { }
                // 附近有匪徒/敌军（E 部队段口径：半径 15 可见匪徒）
                try
                {
                    var basePos = V.Pos(MobileParty.MainParty);
                    bool banditNear = MobileParty.All.Any(p => p != null && p != MobileParty.MainParty
                        && p.IsBandit && basePos.DistanceSquared(V.Pos(p)) <= 15f * 15f);
                    if (banditNear) ways.Add("附近有匪徒可以讨伐，缴获不少");
                }
                catch { }
                // 名下有商队/工坊（business）
                try
                {
                    int caravans = MobileParty.All.Count(p => p.IsCaravan && p.Owner == Hero.MainHero);
                    int workshops = Hero.MainHero?.OwnedWorkshops?.Count ?? 0;
                    if (caravans + workshops > 0) ways.Add("名下商队工坊月月进账");
                }
                catch { }
                // 城里有买卖可做（G5 物价）
                try { if (town != null && town.MarketData != null) ways.Add($"去 {town.Name} 低买高卖"); } catch { }
                if (ways.Count == 0)
                    return "- 打仗最来钱，但刀头舔血；安稳些就做买卖。";
                return "- " + string.Join("；", ways) + "。";
            }
            catch { return "- 打仗最来钱，但刀头舔血；安稳些就做买卖。"; }
        }

        /// <summary>G9 L1 附加（仅队伍成员，QueryMemberOnly）：赃物处理建议——"把刚弄到手的赃物带到
        /// 附近的 X 城卖了"（依赖犯罪感知记忆在【近期回忆】自然带出 + E 位置锚点，函数不查犯罪数据本身）。</summary>
        private static string QueryFenceTip()
        {
            try
            {
                string near = NearestSettlementName(15f);
                return near != null
                    ? $"- 把刚弄到手的赃物带到附近的 {near} 卖了，来钱最快。"
                    : "- 把刚弄到手的赃物找座远点的城卖了，来钱最快。";
            }
            catch { return null; }
        }

        /// <summary>G3③ 通缉状态（信息面 #12）：Clan.PlayerClan.IsOutlaw（✅ 实锤 SaveableProperty 70，
        /// v1.2.12/v1.4.8 均有）。</summary>
        private static string QueryOutlawFact()
        {
            var clan = Clan.PlayerClan;
            if (clan == null) return null;
            try
            {
                return clan.IsOutlaw
                    ? "- 咱们家族已被宣告为法外之徒。"
                    : "- 咱们家族眼下没有背着通缉令。";
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 T）：NPC 关系网认知（双实体关系查询）
        // ═══════════════════════════════════════════════════════════

        /// <summary>双实体关系触发词（"谁和谁关系咋样"）；FindHeroInText 扩展为一次找两个不同 Hero。</summary>
        private static readonly string[] PairRelationKeywords =
        {
            "谁和谁", "他们俩", "他俩", "关系怎么样", "交情", "有仇", "闹翻", "交好", "联姻", "世仇",
            "how are", "relation between", "relationship between", "get along",
        };

        /// <summary>双实体指代词（2026-08-16 用户裁定：你们俩 = 频道最近两人）：
        /// 关系语境下玩家没点名（"你们俩之间的关系怎么样"）→ 从当前频道消息尾部取最近两个
        /// 不同 NPC 发言人作查询对象（微信群聊语义：指最近说话的两个人）。</summary>
        private static readonly string[] PairAddressKeywords =
        {
            "你们俩", "你俩", "二位", "两位", "这俩", "那俩", "他俩",
        };

        /// <summary>识别双实体关系问题：文本命中两个不同 Hero + 关系词 → (Hero, Hero)；否则 null。
        /// 可见性裁剪：两 Hero 任一在队伍 → NeedsPartyMember=true（同行亲见——队伍成员关系是亲见级）；
        /// 两 Hero 均为外人 → 普世（传闻级人尽皆知，路人也能说，与 QueryHeroRelationFact 同口径）。
        /// 单命中 → 回落现状单实体查询（ResolveEntityQuery）。
        /// 🔴 2026-08-16（用户裁定：你们俩 = 频道最近两人）：文本找不到两个 Hero 名但命中指代词
        /// （你们俩/两位…）→ 从当前群聊频道取最近两个不同 NPC 发言人（群聊语义：指最近说话的两人，
        /// 实机"你们俩之间的关系怎么样"被当默认主题零注入，LLM 靠语境猜无数值支撑）。</summary>
        private static (Hero, Hero)? ResolvePairQuery(string text, bool isPartyMember, ImConversation conv = null,
            string responderHeroId = null)
        {
            try
            {
                if (!ContainsAny(text, PairRelationKeywords)) return null;
                var found = new List<Hero>();
                foreach (var hero in Hero.AllAliveHeroes)
                {
                    if (hero == null || hero == Hero.MainHero) continue;
                    string firstName = hero.FirstName?.ToString();
                    string fullName = hero.Name?.ToString();
                    if (!string.IsNullOrEmpty(firstName) && firstName.Length >= 2
                        && text.IndexOf(firstName, StringComparison.OrdinalIgnoreCase) >= 0
                        && !found.Contains(hero)) found.Add(hero);
                    else if (!string.IsNullOrEmpty(fullName) && fullName.Length >= 2 && fullName != firstName
                        && text.IndexOf(fullName, StringComparison.OrdinalIgnoreCase) >= 0
                        && !found.Contains(hero)) found.Add(hero);
                    if (found.Count >= 2) break;
                }
                (Hero, Hero)? pair = null;
                if (found.Count >= 2)
                {
                    pair = (found[0], found[1]);
                }
                else if (ContainsAny(text, PairAddressKeywords))
                {
                    // 没点名 + 指代词 → 频道最近两个不同 NPC 发言人
                    pair = GetRecentPairFromChannel(conv);
                    if (pair == null && found.Count == 1)
                    {
                        // 频道凑不齐两人：已点名的那个 + 频道最近另一个不同的
                        var one = GetRecentPeerOf(found[0], conv);
                        if (one != null) pair = (found[0], one);
                    }
                }
                if (pair == null) return null;
                var a = pair.Value.Item1;
                var b = pair.Value.Item2;
                // 可见性裁剪：任一在队伍 → 需要队伍成员身份
                bool anyInParty = ImChatManager.GetChannelMembers(ImConversationType.Party)
                    .Any(m => m != null && (m == a || m == b));
                // 🔴 2026-08-16（当事人放行）：pair 含回复者本人 → 第一人称亲见自己的关系，无情报边界，
                // 不裁剪（实机 2026-08-16：阿速甘被问"你俩关系怎么样"——他就是当事人之一，却被
                // NeedsPartyMember 整段裁剪 → 编"牢里同难之交"→ 被质问后圆谎；裁剪只挡第三方路人）
                bool selfInvolved = !string.IsNullOrEmpty(responderHeroId)
                    && (a.StringId == responderHeroId || b.StringId == responderHeroId);
                if (anyInParty && !isPartyMember && !selfInvolved) return null;
                return pair;
            }
            catch { return null; }
        }

        /// <summary>频道最近两个不同 NPC 发言人（2026-08-16 用户裁定「你们俩」语义）：
        /// 群聊消息尾部往前扫，排除玩家/系统/非 Hero 发送者，取两个不同的人；
        /// 私聊或无群聊上下文 → null（"你们俩"在私聊里没意义）。</summary>
        private static (Hero, Hero)? GetRecentPairFromChannel(ImConversation conv)
        {
            try
            {
                if (conv == null || conv.Type == ImConversationType.Direct) return null;
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                if (msgs == null) return null;
                Hero a = null, b = null;
                for (int i = msgs.Count - 1; i >= 0 && b == null; i--)
                {
                    var m = msgs[i];
                    if (m == null || m.IsSystem || string.IsNullOrEmpty(m.SenderHeroId)) continue;
                    if (m.SenderHeroId == ImChatManager.PlayerId) continue; // 玩家自己不算
                    Hero h = null;
                    try { h = Hero.FindFirst(x => x.StringId == m.SenderHeroId); } catch { }
                    if (h == null) continue;
                    if (a == null) a = h;
                    else if (h.StringId != a.StringId) b = h;
                }
                if (a == null || b == null) return null;
                return (a, b);
            }
            catch { return null; }
        }

        /// <summary>频道最近一个与指定 Hero 不同的 NPC 发言人（「你俩」点名一人的兜底配对）。</summary>
        private static Hero GetRecentPeerOf(Hero self, ImConversation conv)
        {
            try
            {
                if (self == null || conv == null || conv.Type == ImConversationType.Direct) return null;
                var msgs = ImChatStore.GetGroupMessages(conv.Id);
                if (msgs == null) return null;
                for (int i = msgs.Count - 1; i >= 0; i--)
                {
                    var m = msgs[i];
                    if (m == null || m.IsSystem || string.IsNullOrEmpty(m.SenderHeroId)) continue;
                    if (m.SenderHeroId == ImChatManager.PlayerId || m.SenderHeroId == self.StringId) continue;
                    Hero h = null;
                    try { h = Hero.FindFirst(x => x.StringId == m.SenderHeroId); } catch { }
                    if (h != null) return h;
                }
            }
            catch { }
            return null;
        }

        /// <summary>双实体关系硬事实（方案 T3b/T4）：任意两 Hero 的关系——等级词（QueryHeroRelationFact
        /// 同款档位，Hero↔Hero 复用）+ 情谊数值 + 硬事实标记（姻亲/同族/敌国交战）。
        /// 只做 C# 可查硬事实不编来历（BackgroundStory 是第一人称身世，转述他人 = 幻觉高风险，铁律 2）；
        /// 问"怎么认识的"无数据 → 契约模糊化（"这我倒没打听过"——LLM 不编）。</summary>
        private static string QueryHeroPairFact(Hero a, Hero b)
        {
            try
            {
                int rel = a.GetRelation(b);
                string level;
                if (rel >= 50) level = "挚友";
                else if (rel >= 20) level = "交好";
                else if (rel >= -5) level = "泛泛之交";
                else if (rel >= -50) level = "面和心不和";
                else level = "仇深似海";
                var marks = new List<string>();
                if (a.Spouse == b) marks.Add("姻亲");
                if (a.Clan != null && a.Clan == b.Clan) marks.Add("同族");
                try
                {
                    var ka = a.Clan?.Kingdom;
                    var kb = b.Clan?.Kingdom;
                    if (ka != null && kb != null && ka != kb && ka.IsAtWarWith(kb)) marks.Add("敌国交战");
                }
                catch { }
                string markStr = marks.Count > 0 ? "（" + string.Join("、", marks) + "）" : "";
                return $"- {a.Name} 与 {b.Name} 的关系：{level}（情谊 {rel}）{markStr}。";
            }
            catch { return null; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 G10 + T3a）：常态注入段（L1 队伍成员）
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// 🔴 2026-08-16（方案 G10）：主公的人缘常态段——【主公的人缘】。
        /// Hero.AllAliveHeroes 遍历 GetRelationWithPlayer()，显著值 |rel|≥20 才上榜，按绝对值降序各取
        /// 前 4（友好/记恨），空集不注入（零开销）；人名走 Hero.Name 本地化。
        /// 预算 ~40 token/轮（L1 常态；仅队伍成员注入——调用方按回复者身份判定，2026-08-16 裁定）。
        /// 关系是酒馆传闻级信息（人尽皆知 + 随从亲见），认知边界无虞。
        /// </summary>
        public static string BuildPlayerRelationSection()
        {
            try
            {
                if (Hero.MainHero == null) return "";
                var friends = new List<(Hero h, int rel)>();
                var foes = new List<(Hero h, int rel)>();
                foreach (var h in Hero.AllAliveHeroes)
                {
                    if (h == null || h == Hero.MainHero) continue;
                    try
                    {
                        int rel = Hero.MainHero.GetRelation(h);
                        if (rel >= 20) friends.Add((h, rel));
                        else if (rel <= -20) foes.Add((h, rel));
                    }
                    catch { }
                }
                if (friends.Count == 0 && foes.Count == 0) return "";
                var sb = new StringBuilder();
                sb.AppendLine("【主公的人缘】");
                if (friends.Count > 0)
                    sb.AppendLine("与主公交好的：" + string.Join("、",
                        friends.OrderByDescending(f => f.rel).Take(4).Select(f => $"{f.h.Name}")) + "。");
                if (foes.Count > 0)
                    sb.AppendLine("记恨主公的：" + string.Join("、",
                        foes.OrderBy(f => f.rel).Take(4).Select(f => $"{f.h.Name}")) + "。");
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>
        /// 🔴 2026-08-16（方案 T3a）：【咱们人的关系】常态段——队伍成员两两关系网
        ///（同行天天见 = 亲见级 + 百科关系数值）。显著关系才上榜（|rel|≥20 或姻亲/同族标记）；
        /// 全队两两 |rel|<20 → 空段不注入（零开销）。预算 ~40 token，上限 4 行。
        /// 构建行打 [RelWeb] 日志（几行关系/裁剪判定，供调阈值）。
        /// </summary>
        public static string BuildPartyRelationSection()
        {
            try
            {
                var members = ImChatManager.GetChannelMembers(ImConversationType.Party);
                if (members == null || members.Count < 2) return "";
                var lines = new List<string>();
                for (int i = 0; i < members.Count && lines.Count < 4; i++)
                {
                    for (int j = i + 1; j < members.Count && lines.Count < 4; j++)
                    {
                        var a = members[i];
                        var b = members[j];
                        if (a == null || b == null) continue;
                        try
                        {
                            int rel = a.GetRelation(b);
                            if (a.Spouse == b)
                            {
                                lines.Add($"- {a.Name} 与 {b.Name} 是姻亲。");
                                continue;
                            }
                            if (a.Clan != null && a.Clan == b.Clan)
                            {
                                lines.Add($"- {a.Name} 与 {b.Name} 是同族的袍泽。");
                                continue;
                            }
                            if (Math.Abs(rel) >= 20)
                                lines.Add($"- {a.Name} 和 {b.Name} {(rel > 0 ? "交好" : "不对付")}（情谊 {rel}）。");
                        }
                        catch { }
                    }
                }
                if (lines.Count == 0) return "";
                DebugLogger.Log($"[RelWeb] 咱们人的关系 {lines.Count} 行（成员 {members.Count} 人）");
                return "【咱们人的关系】\n" + string.Join("\n", lines) + "\n";
            }
            catch { return ""; }
        }

        // ═══════════════════════════════════════════════════════════
        // 🔴 2026-08-16（方案 I1）：触发式现状行【此刻现状】（聊过数值才注入）
        // ═══════════════════════════════════════════════════════════

        /// <summary>数值类关键词（gold/food/morale/party/prisoner/wounded/time 主题 Keywords 并集——
        /// 复用 RAG 主题表，不新维护词表）。
        /// 🔴 2026-08-16（触发词收紧）：删「队伍/部队/人马」——NPC 回复高频泛称（"我随队伍在旷野里"、
        /// E 段"西北有支部队"），历史 12 条检测几乎总命中 → 【此刻现状】常态化注入，违背 I1
        /// 「不聊零开销」（实机"我们周围什么情况"误触发）；删「天气」——T05 用例要求聊天气零注入
        /// （天气走 time RAG 即时查，无旧值引用问题）。</summary>
        private static readonly string[] NumericStatusKeywords =
        {
            "钱", "金币", "第纳尔", "金子", "积蓄", "盘缠", "gold", "coin", "coins", "money", "denar", "denars",
            "粮食", "食物", "口粮", "粮草", "补给", "food", "supply", "provision", "ration",
            "士气", "军心", "morale", "spirit",
            "兵力", "多少人", "士兵", "troop", "troops", "army", "soldier",
            "俘虏", "囚犯", "prisoner", "prisoners", "captive",
            "伤员", "伤兵", "wounded", "injured",
            "今天", "几号", "季节", "日期", "时辰", "today", "date", "season",
        };

        /// <summary>
        /// 🔴 2026-08-16（方案 I1）：触发式现状行【此刻现状】——聊过数值才注入（历史提及检测），不聊零开销。
        /// 触发条件（主线程，零新词表）：玩家本条消息 或 对话历史最近 12 条命中数值类关键词 →
        /// 注入 ~30 token 现状行：【此刻现状】初夏、上午、细雨；在吕卡隆附近；钱袋 2000、粮 5.2 天、士气 62、兵 45。
        /// 实现：复用各 Query 取数（try/catch 逐段 guard）；历史扫描 = 遍历 RecentHistory 尾部 12 条做
        /// ContainsAny。位置不在本行：方案 E 段已常态带（用户批准预算）；时间/天气由本行在触发时覆盖。
        /// </summary>
        public static string BuildCurrentStatusLine(string playerText, List<ChatMessage> recentHistory)
        {
            try
            {
                if (!NumericTopicHit(playerText) && !HistoryHitsNumericTopic(recentHistory)) return "";
                var parts = new List<string>();
                // 时间/天气（复用 QueryTimeFact 的取数逻辑 → 简短版）
                parts.Add($"{GetSeasonName()}、{(CampaignTime.Now.IsDayTime ? "白天" : "夜里")}、{GetTimeOfDayWord()}、{GetWeatherWord()}");
                // 位置（方案 A 判定链）
                var party = MobileParty.MainParty;
                if (party != null)
                {
                    if (party.CurrentSettlement != null) parts.Add($"在 {party.CurrentSettlement.Name}");
                    else if (party.TargetSettlement != null) parts.Add($"正前往 {party.TargetSettlement.Name}");
                    else
                    {
                        string near = NearestSettlementName(15f);
                        parts.Add(near != null ? $"在 {near} 附近" : "在旷野");
                    }
                }
                // 数值（逐段 try/catch）
                try { if (Hero.MainHero != null) parts.Add($"钱袋 {Hero.MainHero.Gold}"); } catch { }
                try { if (party != null) parts.Add($"粮 {party.Food:0.0} 天"); } catch { }
                try { if (party != null) parts.Add($"士气 {party.Morale:0}"); } catch { }
                try { if (party != null) parts.Add($"兵 {party.MemberRoster.TotalRegulars}"); } catch { }
                return "【此刻现状】" + string.Join("、", parts) + "。";
            }
            catch { return ""; }
        }

        private static bool NumericTopicHit(string playerText)
        {
            return !string.IsNullOrWhiteSpace(playerText) && ContainsAny(playerText, NumericStatusKeywords);
        }

        /// <summary>历史提及检测（🔴 关键洞察：LLM 主动引用数值必发生在"之前聊过数值"的上下文之后——
        /// 历史提及检测触发精准覆盖，收益全得、开销为零）。</summary>
        private static bool HistoryHitsNumericTopic(List<ChatMessage> recentHistory)
        {
            if (recentHistory == null || recentHistory.Count == 0) return false;
            int from = Math.Max(0, recentHistory.Count - 12);
            for (int i = recentHistory.Count - 1; i >= from; i--)
            {
                var m = recentHistory[i];
                if (m == null || string.IsNullOrEmpty(m.Content)) continue;
                if (ContainsAny(m.Content, NumericStatusKeywords)) return true;
            }
            return false;
        }
    }
}
