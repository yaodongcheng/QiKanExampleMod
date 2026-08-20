using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.SaveSystem;
using static TaleWorlds.CampaignSystem.CampaignBehaviors.LordConversationsCampaignBehavior;

namespace LivingWorldNpcs
{
    public class NPCProfile
    {
        public enum FriendshipImportanceEnum
        {
            // 程度：低 -> 中 -> 高
            NotImportant = 0,
            Normal = 1,
            Important = 2
        }

        public enum TemperEnum
        {
            // 程度：温和(低烈度) -> 普通 -> 急躁(高烈度)
            // 或者理解为脾气的“火爆程度”
            Mild = 0,
            Normal = 1,
            Impatient = 2
        }

        public enum SpiritEnum
        {
            // 程度：胆小(缺乏勇气) -> 普通 -> 勇敢(勇气极高)
            Timid = 0,
            Normal = 1,
            Brave = 2
        }

        public enum IsmEnum
        {
            // 这一项比较主观，取决于你认为哪个是“0”点。
            // 通常"现实"比较接地气(Low/Base)，"理想"比较高远(High/Abstract)
            Realistic = 0,
            Normal = 1,
            Ideal = 2
        }

        public enum ActStyleEnum
        {
            // 程度：轻浮(不稳重) -> 普通 -> 周到(非常稳重)
            // 按照“靠谱程度”或“慎重程度”排序
            Flippancy = 0,
            Normal = 1,
            Considerate = 2
        }

        public enum DesireEnum
        {
            // 程度：无欲(低欲望) -> 普通 -> 贪婪(高欲望)
            DesireLess = 0,
            Normal = 1,
            Greedy = 2
        }
        public enum AlcoholDesireEnum
        {
            Teetotal = 0,
            Normal = 1,
            Alcoholic = 2
        }
        public enum DesireTypeEnum
        {
            Book,
            Weapon,
            Nanman,
            Art,
            Money
        }
        public enum OriginEnum
        {
            Genji,
            Henshi,
            FujiwaraShi,
            Other
        }
        public enum WeaponTypeEnum
        {
            Sword,
            Gun,
            Bow,
            Spear,
            Other
        }
        public enum JobTendencyEnum
        {
            //没那个意思、只限武将、全职种、武将以外优先
            None,
            WarriorOnly,
            AllJob,
            NotWarrior
        }

        //骑砍2中能直接获取的关键信息
        public string StringId { get; set; } = "";
        public string Name { get; set; } = "";


        //大部分时候都是一个英雄
        public Hero BaseHero { get; set; }
        //但是也可能是一个普通士兵，没有自己的独立的sn
        public CharacterObject BaseCharacter { get; set; }
        public string Clan { get; set; } = "";
        public string ClanId { get; set; } = "";
        public string Kingdom { get; set; } = "";
        public string KingdomId { get; set; } = "";
        public string Spouse { get; set; } = "";
        public string SpouseId { get; set; } = "";
        public string Occupation { get; set; } = "";

        public bool IsFemale
        {
            get
            {
                if (BaseHero != null)
                {
                    return BaseHero.IsFemale;
                }
                return false;
            }
        }
        public string PersonalityTraits { get; set; } = "";// 比如: "勇敢, 狡猾, 贪婪"


        //一些Trait属性，Honor"(荣誉), "Ambition"(野心), "Loyalty"(忠诚), "Tradition"(传统)等
        public Dictionary<string, int> CoreValues { get; set; } = new Dictionary<string, int>();


        //以下特性在 Bannerlord 原生中不存在，由 Mod 自定义

        public string TemperStr {  get; set; } = "普通";//脾气性情：温和、性急、普通
        public TemperEnum Temper
        {
            get
            {
                switch (TemperStr)
                {
                    case "温和":
                        return TemperEnum.Mild;
                    case "性急":
                        return TemperEnum.Impatient;
                    default:
                        return TemperEnum.Normal;

                }                
            }
        }
        public string SpiritStr { get; set; } = "普通"; //精神胆量：胆小、勇敢、普通
        public SpiritEnum Spirit
        {
            get
            {
                switch (SpiritStr) { 
                case "胆小":
                    return SpiritEnum.Timid;
                case "勇敢":
                    return SpiritEnum.Brave;
                default:
                    return SpiritEnum.Normal;}
            }
        }
        public string IsmStr { get; set; } = "普通";//主义：理想、显示、普通
        public IsmEnum Ism
        {
            get
            {
                switch (IsmStr) { 
                case "现实":
                    return IsmEnum.Realistic;
                case "理想":
                    return IsmEnum.Ideal;
                default:
                    return IsmEnum.Normal;}
            }
        }
        public string ActStyleStr { get; set; } = "普通";//行动风格：慎重、轻率、普通
        public ActStyleEnum ActStyle
        {
            get
            {
                switch (ActStyleStr)
                {
                    case "慎重":
                        return ActStyleEnum.Considerate;
                        case "轻率":
                        return ActStyleEnum.Flippancy;
                        default:
                        return ActStyleEnum.Normal;
                }
            }
        }

        public string theImportanceOfFriendshipStr { get; set; } = "普通"; //对情义的重视程度：不重情义、重视情义、普通
        public FriendshipImportanceEnum theImportanceOfFriendship
        {
            get
            {
                switch (theImportanceOfFriendshipStr)
                {
                    case "不重情义":
                        return FriendshipImportanceEnum.NotImportant;
                        case "重视情义":
                        return FriendshipImportanceEnum.Important;
                        default:
                        return FriendshipImportanceEnum.Normal;
                }
            }
        }
        public int Ambition { get; set; } = 0;//野心
        public string DesireStr { get; set; } = "普通";//物欲：无欲、贪心、普通
        public DesireEnum Desire
        {
            get
            {
                switch (DesireStr)
                {
                    case "无欲":
                        return DesireEnum.DesireLess;
                    case "贪心":
                        return DesireEnum.Greedy;
                    default:
                        return DesireEnum.Normal;
                }
            }
        }
        public string DesireTypeStr { get; set; } = "金钱"; //物欲类型：书籍、武具、南蛮物、艺术品
        public DesireTypeEnum DesireType
        {
            get
            {
                switch (DesireTypeStr)
                {
                    case "书籍":
                        return DesireTypeEnum.Book;
                        case "武具":
                        return DesireTypeEnum.Weapon;
                        case "南蛮物":
                        return DesireTypeEnum.Nanman;
                        case "艺术品":
                        return DesireTypeEnum.Art;
                        default:
                        return DesireTypeEnum.Money;
                }
            }
        }
        public string AlcoholDesireStr { get; set; } = "普通";//酒精需求：滴酒不沾、嗜酒如命、普通
        public AlcoholDesireEnum AlcoholDesire
        {
            get
            {
                switch (AlcoholDesireStr)
                {
                    case "滴酒不沾":
                        return AlcoholDesireEnum.Teetotal;
                    case "嗜酒如命":
                        return AlcoholDesireEnum.Alcoholic;
                    default:
                        return AlcoholDesireEnum.Normal;
                }
            }
        }
        
        public string OriginStr { get; set; } = "其他";//出身：藤原氏、平氏、源氏、其他
        public OriginEnum Origin
        {
            get
            {
                switch (OriginStr)
                {
                    case "藤原氏":
                        return OriginEnum.FujiwaraShi;
                    case "平氏":
                        return OriginEnum.Henshi;
                    case "源氏":
                        return OriginEnum.Genji;
                    default:
                        return OriginEnum.Other;

                }
            }
        }
        public string WeaponDesireStr { get; set; } = "刀剑";//武器偏好：刀剑、枪、弓、火绳枪、锁镰
        public WeaponTypeEnum WeaponDesire
        {
            get
            {
                switch (WeaponDesireStr)
                {
                    case "刀剑":
                        return WeaponTypeEnum.Sword;
                    case "枪":
                        return WeaponTypeEnum.Spear;
                    case "弓":
                        return WeaponTypeEnum.Bow;
                    case "火绳枪":
                        return WeaponTypeEnum.Gun;
                    default:
                        return WeaponTypeEnum.Other;
                }
            }
        }

        public string JobTendencyStr { get; set; } = "全职种"; //职业倾向：没那个意思、只限武将、全职种、武将以外优先
        public JobTendencyEnum JobTendency
        {
            get { 
                switch (JobTendencyStr)
                {
                    case "没那个意思":
                        return JobTendencyEnum.None;
                    case "只限武将":
                        return JobTendencyEnum.WarriorOnly;
                    case "武将以外优先":
                        return JobTendencyEnum.NotWarrior;
                    default:
                        return JobTendencyEnum.AllJob;
                }
            
            }
        }
        public int JobCompatibility { get; set; } = 0;//仕官相性，0-100，两个人数值越接近越容易合作
        public int FriendCompatibility { get; set; } = 0;//朋友相性，0-100，两个人数值越接近越容易加好感


        // 我自己特殊定义的属性
        public int DebtOfGraditude { get; set; } = 0;//人情债，是一种资源，可以用来消耗请求Npc做事. 具体效用受到theImportanceOfFriendship影响
        public string LifeGoal = ""; //人生目标，比如：建立家族、扩大领土、建立帝国
        public string ShortGoal = ""; //短期目标，比如赚点钱，恢复名誉，上班
        public float EstimatedValue { get; private set; } = 0;//身价
        public void UpdateProfile(Hero hero,Agent agent)
        {
            if (hero == null && agent == null)
                return;
            BaseHero = hero;
            if (agent != null)
            {
                BaseCharacter = agent.Character as CharacterObject;
            }
            else if (hero != null)
            {
                BaseCharacter = hero.CharacterObject;
            }

            if (BaseHero != null)
            {
                Name = hero.Name.ToString();
                StringId = hero.StringId;
                if (hero.Clan != null)
                {
                    Clan = hero.Clan.Name.ToString();
                    ClanId = hero.Clan.StringId;
                }
                else
                {
                    Clan = "None";
                    ClanId = "";
                }
                if (hero.Clan?.Kingdom != null)
                {
                    Kingdom = hero.Clan.Kingdom.Name.ToString();
                    KingdomId = hero.Clan.Kingdom.StringId;
                }
                else
                {
                    Kingdom = "None";
                    KingdomId = "";
                }
                if (hero.Spouse != null)
                {
                    Spouse = hero.Spouse.Name.ToString();
                    SpouseId = hero.Spouse.StringId;
                }
                else
                {
                    Spouse = "None";
                    SpouseId = "";
                }
                // 职业判断
                if (hero.IsLord) Occupation = "贵族";
                else if (hero.IsMerchant) Occupation = "商人";
                else if (hero.IsGangLeader) Occupation = "帮派头目";
                else Occupation = "游民";

                // 个性特征提取 (Bannerlord 的 Traits)
                // 1. 荣誉 (Honor)
                int honorLevel = hero.GetTraitLevel(DefaultTraits.Honor);
                CoreValues["Honor"] = honorLevel;

                // 2. 仁慈 (Mercy) 
                int mercyLevel = hero.GetTraitLevel(DefaultTraits.Mercy);
                CoreValues["Mercy"] = mercyLevel;

                // 3. 计算 (Calculating) -> 类似智力、谋略
                int calcLevel = hero.GetTraitLevel(DefaultTraits.Calculating);
                CoreValues["Calculating"] = calcLevel;

                // 4. 胆略 (Valor) -> 类似 力、勇
                int valorLevel = hero.GetTraitLevel(DefaultTraits.Valor);
                CoreValues["Valor"] = valorLevel;



                //自定义属性，从 GameDatabase 读取
                var record = GameDatabase.Heroes.GetByID(StringId);
                if (record != null)
                {
                    TemperStr = record.GetString("Temper", "普通") ;
                    SpiritStr = record.GetString("Spirit", "普通") ;
                    IsmStr = record.GetString("Ism", "普通") ;
                    ActStyleStr = record.GetString("ActStyle", "普通") ;
                    theImportanceOfFriendshipStr = record.GetString("theImportanceOfFriendship", "普通") ;
                    Ambition = record.GetInt("Ambition", 0);
                    DesireStr = record.GetString("Desire", "普通");
                    DesireTypeStr = record.GetString("DesireType", "普通");
                    AlcoholDesireStr = record.GetString("AlcoholDesire", "普通");
                    OriginStr = record.GetString("Origin", "其他");
                    WeaponDesireStr = record.GetString("WeaponDesire", "刀剑");
                    JobTendencyStr = record.GetString("JobTendency", "全职种");
                    JobCompatibility = record.GetInt("JobCompatibility", 0);
                    FriendCompatibility = record.GetInt("FriendCompatibility", 0);
                }
                DebtOfGraditude = 0;

                CalCurrentMotivation();


                PersonalityTraits = "";
                //基于数值大小确定词条
                /*

                if (mercy > 0) traits.Add("仁慈"); else if (mercy < 0) traits.Add("残忍");
                if (valor > 0) traits.Add("勇敢"); else if (valor < 0) traits.Add("胆小");
                if (honor > 0) traits.Add("诚实"); else if (honor < 0) traits.Add("狡诈");
                if (calculating > 0) traits.Add("精明"); else if (calculating < 0) traits.Add("冲动");

                if (traits.Count > 0) PersonalityTraits = string.Join(", ", traits);
                */
            }
            else if(BaseCharacter != null)
            {
                Name = BaseCharacter.Name.ToString();
                StringId = BaseCharacter.StringId; // 兵种 ID，如 "imperial_recruit"


                ClanId = "";

                Clan clan = null;
                Kingdom kingdom = null;

                // --- 第一步：尝试通过 Agent 的 Origin 获取 (最准确) ---
                // 很多站岗士兵其实是属于 Settlement.GarrisonParty (守军部队) 的
                if (agent.Origin is PartyAgentOrigin partyOrigin && partyOrigin.Party != null)
                {
                    // 这里的 Party 可能是 MobileParty (领主带进来的兵) 
                    // 也可能是 PartyBase (城市的守军/民兵)
                    clan = partyOrigin.Party.Owner?.Clan;

                    // 如果 Owner 为空，尝试检查是否是民兵/守军对应的定居点拥有者
                    if (clan == null && partyOrigin.Party.IsSettlement)
                    {
                        clan = partyOrigin.Party.Settlement.OwnerClan;
                    }
                }
                // --- 第二步：如果 Origin 没找到，且 Agent 是守卫，使用当前场景归属 ---
                // 场景：你在城市里闲逛，看到的守卫是“生成的”，可能没有具体的 Party 对应
                if (clan == null && Hero.MainHero.CurrentSettlement != null)
                {
                    // 检查这个 Agent 是否是敌对的或者确实是守卫
                    // 通常可以通过 Character 对象判断，或者简单粗暴地认为：
                    // 在和平模式场景下，拿着武器站岗的通常就是城主的人

                    Settlement currentSettlement = Hero.MainHero.CurrentSettlement;

                    // 获取该定居点的拥有家族
                    clan = currentSettlement.OwnerClan;
                }
                if (clan != null)
                {
                    kingdom = clan.Kingdom;
                    ClanId = clan.StringId;
                    Clan = clan.Name.ToString();
                    if(kingdom!=null)
                    {
                        KingdomId = kingdom.StringId;
                        Kingdom = kingdom.Name.ToString();
                    }
                }
                else
                {

                    ClanId = "";
                    KingdomId = "";
                    Kingdom = "无";
                }               

                Spouse = "无";

                // 职业判断
                if (BaseCharacter.IsSoldier)
                {
                    Occupation = "足轻";
                    if(ClanId == "")
                        Clan = "军籍";
                }
                else if (BaseCharacter.IsFemale)
                {
                    Occupation = "村民";
                    if (ClanId == "")
                        Clan = "平民";
                }
                else
                { 
                    Occupation = "村民";
                    if (ClanId == "")
                        Clan = "平民";
                }

                // 填充默认性格 (士兵通常稍微勇敢一点，或者是完全看 Tier)
                CoreValues["Honor"] = 0;
                CoreValues["Mercy"] = 0;
                CoreValues["Valor"] = BaseCharacter.Tier > 3 ? 1 : 0; // 高级兵更勇敢
                CoreValues["Calculating"] = 0;

                CalCurrentMotivation();
            }
            else
            {
                Name = agent?.Name.ToString() ?? "无名";
                CalCurrentMotivation();
            }
        }
        public NPCProfile(Hero hero = null,Agent agent = null)
        {
            UpdateProfile(hero,agent);
        }
  

       
        #region Localization Helpers

        /// <summary>将中文 trait 数据值映射到 LWN 本地化 key 并返回当前语言显示文本。</summary>
        private static string LocalizeTrait(string chineseValue)
        {
            if (string.IsNullOrEmpty(chineseValue)) return "";
            if (TraitKeyMap.TryGetValue(chineseValue, out string key))
                // 不传 fallback —— 让 GetEnglishFallback 从 English prompts XML 取英文文本
                return LWNTextHelper.ResolveText(key);
            return chineseValue; // fallback: raw value for unknown traits
        }

        /// <summary>快捷性别本地化</summary>
        private static string LocalizeGender(bool isFemale) =>
            // 本地化性别标签
            LWNTextHelper.ResolveText(isFemale ? "LWN_prompt_trait_gender_female" : "LWN_prompt_trait_gender_male",
                isFemale ? "Female" : "Male");

        /// <summary>快捷士兵/平民本地化</summary>
        private static string LocalizeRole(bool isSoldier) =>
            // 本地化士兵/平民标签
            LWNTextHelper.ResolveText(isSoldier ? "LWN_prompt_trait_role_soldier" : "LWN_prompt_trait_role_civilian",
                isSoldier ? "Soldier" : "Civilian");

        /// <summary>中文 trait 值 → LWN key 后缀 映射表</summary>
        /// lwn-ignore: B (all entries below — data lookup keys, not display text)
        private static readonly Dictionary<string, string> TraitKeyMap = new Dictionary<string, string>
        {
            // Temper
            {"温和", "LWN_prompt_trait_temper_mild"}, // lwn-ignore: B,
            {"性急", "LWN_prompt_trait_temper_impatient"}, // lwn-ignore: B,
            {"普通", "LWN_prompt_trait_temper_normal"}, // lwn-ignore: B,
            // Spirit
            {"胆小", "LWN_prompt_trait_spirit_timid"}, // lwn-ignore: B,
            {"勇敢", "LWN_prompt_trait_spirit_brave"}, // lwn-ignore: B,
            // Ism
            {"现实", "LWN_prompt_trait_ism_realistic"}, // lwn-ignore: B,
            {"理想", "LWN_prompt_trait_ism_ideal"}, // lwn-ignore: B,
            // ActStyle
            {"慎重", "LWN_prompt_trait_style_cautious"}, // lwn-ignore: B,
            {"轻率", "LWN_prompt_trait_style_reckless"}, // lwn-ignore: B,
            // Friendship
            {"不重情义", "LWN_prompt_trait_friendship_pragmatic"}, // lwn-ignore: B,
            {"重视情义", "LWN_prompt_trait_friendship_loyal"}, // lwn-ignore: B,
            // Desire
            {"无欲", "LWN_prompt_trait_desire_content"}, // lwn-ignore: B,
            {"贪心", "LWN_prompt_trait_desire_greedy"}, // lwn-ignore: B,
            // DesireType
            {"金钱", "LWN_prompt_trait_desiretype_money"}, // lwn-ignore: B,
            {"书籍", "LWN_prompt_trait_desiretype_books"}, // lwn-ignore: B,
            {"武具", "LWN_prompt_trait_desiretype_weapons"}, // lwn-ignore: B,
            {"南蛮物", "LWN_prompt_trait_desiretype_exotic"}, // lwn-ignore: B,
            {"艺术品", "LWN_prompt_trait_desiretype_art"}, // lwn-ignore: B,
            // Alcohol
            {"滴酒不沾", "LWN_prompt_trait_alcohol_teetotaler"}, // lwn-ignore: B,
            {"嗜酒如命", "LWN_prompt_trait_alcohol_alcoholic"}, // lwn-ignore: B,
            // Origin
            {"藤原氏", "LWN_prompt_trait_origin_fujiwara"}, // lwn-ignore: B,
            {"平氏", "LWN_prompt_trait_origin_taira"}, // lwn-ignore: B,
            {"源氏", "LWN_prompt_trait_origin_minamoto"}, // lwn-ignore: B,
            {"其他", "LWN_prompt_trait_origin_other"}, // lwn-ignore: B,
            // Weapon
            {"刀剑", "LWN_prompt_trait_weapon_sword"}, // lwn-ignore: B,
            {"枪", "LWN_prompt_trait_weapon_spear"}, // lwn-ignore: B,
            {"弓", "LWN_prompt_trait_weapon_bow"}, // lwn-ignore: B,
            {"火绳枪", "LWN_prompt_trait_weapon_gun"}, // lwn-ignore: B,
            {"锁镰", "LWN_prompt_trait_weapon_kusarigama"}, // lwn-ignore: B,
            // Job
            {"没那个意思", "LWN_prompt_trait_job_uninterested"}, // lwn-ignore: B,
            {"只限武将", "LWN_prompt_trait_job_warrior_only"}, // lwn-ignore: B,
            {"全职种", "LWN_prompt_trait_job_all"}, // lwn-ignore: B,
            {"武将以外优先", "LWN_prompt_trait_job_prefers_noncombat"}, // lwn-ignore: B,
            // Occupation 职业
            {"贵族", "LWN_prompt_trait_occupation_noble"}, // lwn-ignore: B,
            {"商人", "LWN_prompt_trait_occupation_merchant"}, // lwn-ignore: B,
            {"帮派头目", "LWN_prompt_trait_occupation_gang_leader"}, // lwn-ignore: B,
            {"游民", "LWN_prompt_trait_occupation_wanderer"}, // lwn-ignore: B,
            {"足轻", "LWN_prompt_trait_occupation_foot_soldier"}, // lwn-ignore: B,
            {"村民", "LWN_prompt_trait_occupation_villager"}, // lwn-ignore: B,
        };

        #endregion

        /// <summary>领主级判定（身份深度用，2026-08-10）：领主 / 阵营领袖 / 家族族长 才有
        /// 家族与王国的百科级认知；平民保持坊间常识。
        /// 🔴 2026-08-17（铁律 18 共享化）：判定公式抽到 NpcTierHelper.IsNoble（玩家/NPC 共用单管线）。</summary>
        private bool IsNobleTier()
        {
            return NpcTierHelper.IsNoble(BaseHero);
        }

        public string GetClanInfo()
        {
            StringBuilder sb = new StringBuilder();
            if (BaseHero == null && BaseCharacter == null)
            {
                // 无家族势力兜底
                return LWNTextHelper.ResolveText("LWN_prompt_clan_no_hero");
            }
            else if (BaseHero == null && BaseCharacter != null)
            {
                // 模板 NPC：在某家族服役
                string localizedRole = LocalizeTrait(Occupation);
                if (ClanId != "")
                    // --- 家族背景 ---\n无显赫家族背景。作为一名普通的{ROLE}，依靠在{CLAN_NAME}家族中服役维持生计。
                    return LWNTextHelper.ResolveCompound("LWN_prompt_clan_template_has_clan",
                        ("ROLE", localizedRole), ("CLAN_NAME", Clan));
                else
                    // --- 家族背景 ---\n无显赫家族背景。作为一名普通的{ROLE}，依靠在别人家族中服役维持生计。
                    return LWNTextHelper.ResolveCompound("LWN_prompt_clan_template_no_clan",
                        ("ROLE", localizedRole));
            }
            else
            {
                Clan clan = BaseHero.Clan;
                if (clan == null)
                {
                    // 游民兜底
                    return LWNTextHelper.ResolveText("LWN_prompt_clan_wanderer");
                }

                // 1. 家族内身份
                string selfStatus;
                if (BaseHero == clan.Leader)
                    // 家族族长 (拥有家族最高决策权)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_leader"); // lwn-ignore: B
                else if (BaseHero == clan.Leader.Spouse)
                    // 族长配偶 (享有极高尊荣)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_spouse"); // lwn-ignore: B
                else if (BaseHero.Father == clan.Leader || BaseHero.Mother == clan.Leader)
                    // 家族少主/千金 (嫡系血亲)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_heir"); // lwn-ignore: B
                else if (clan.Companions.Contains(BaseHero))
                    // 家族家臣/同伴 (因能力被招募，地位取决于功绩)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_companion"); // lwn-ignore: B
                else
                    // 普通成员
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_member"); // lwn-ignore: B

                // 2. 领地统计
                int myTowns = clan.Fiefs.Count(f => f.IsTown);
                int myCastles = clan.Fiefs.Count(f => f.IsCastle);
                int myTotalFiefs = myTowns + myCastles;
                var allSettlements = Campaign.Current.Settlements;
                int worldTotalTowns = allSettlements.Count(s => s.IsTown);
                int worldTotalCastles = allSettlements.Count(s => s.IsCastle);
                int worldTotalFiefs = worldTotalTowns + worldTotalCastles;
                double fiefPercentage = worldTotalFiefs > 0 ? (double)myTotalFiefs / worldTotalFiefs * 100 : 0;

                // 3. 经济评估
                int clanWealth = clan.Leader.Gold;
                string wealthDesc;
                if (clanWealth > 1000000)
                    // 富可敌国
                    wealthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_wealth_fabulous");
                else if (clanWealth > 500000)
                    // 腰缠万贯
                    wealthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_wealth_rich");
                else if (clanWealth > 100000)
                    // 家境殷实
                    wealthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_wealth_comfortable");
                else if (clanWealth > 30000)
                    // 勉强维持
                    wealthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_wealth_struggling");
                else
                    // 囊中羞涩
                    wealthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_wealth_poor");

                // 4. 综合实力评估
                string strengthDesc;
                if (fiefPercentage >= 10.0)
                    // 一方诸侯 (领土广阔，足以自立)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_warlord");
                else if (fiefPercentage >= 3.0 && clan.Tier >= 4)
                    // 顶级权贵 (拥有大量封地)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_top");
                else if (clanWealth > 1000000 && clan.Tier >= 3)
                    // 金融巨鳄 (虽领地不多但财力惊人)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_finance");
                else if (clan.Tier >= 5)
                    // 传统豪门 (声望极高)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_traditional");
                else if (myTotalFiefs > 0)
                    // 有地贵族 (拥有根基)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_landed");
                else
                    // 无地游族 (飘摇不定)
                    strengthDesc = LWNTextHelper.ResolveText("LWN_prompt_clan_strength_landless");

                // 5. 组装
                string clanInfo = LWNTextHelper.ResolveCompoundMixed("LWN_prompt_clan_hero",
                    ("CLAN", (object)clan.Name),
                    ("TIER", clan.Tier.ToString()),
                    ("RENOWN", clan.Renown.ToString("F0")),
                    ("STRENGTH", strengthDesc),
                    ("STATUS", selfStatus),
                    ("TOWNS", myTowns.ToString()),
                    ("CASTLES", myCastles.ToString()),
                    ("PCT", fiefPercentage.ToString("F2")),
                    ("MY", myTotalFiefs.ToString()),
                    ("TOTAL", worldTotalFiefs.ToString()),
                    ("WEALTH", wealthDesc),
                    ("GOLD", clanWealth.ToString()),
                    ("CURRENCY", Settings.Instance.CurrencyName),
                    ("INF", clan.Influence.ToString("F0")));

                // 🔴 身份深度（2026-08-10）：领主级 NPC 追加家族百科正文（引擎 XML 本地化描述，世界书层）。
                // 平民不注入——村民不知道自己家族"百科"里写的那些，保持坊间常识级。
                if (IsNobleTier())
                {
                    string encText = clan.EncyclopediaText?.ToString();
                    if (!string.IsNullOrWhiteSpace(encText))
                        clanInfo += "\n（家族百科）" + encText;
                    // 领地百科（第一座城/堡）：领主对自己封地的认知
                    try
                    {
                        var fief = clan.Fiefs.FirstOrDefault(f => f.IsTown || f.IsCastle);
                        string fiefEnc = fief?.Settlement?.EncyclopediaText?.ToString();
                        if (!string.IsNullOrWhiteSpace(fiefEnc))
                            clanInfo += "\n（领地百科）" + fiefEnc;
                    }
                    catch { }
                }
                return clanInfo;
            }
        }

        public string GetKingdomInfo()
        {
            StringBuilder sb = new StringBuilder();
            if (BaseHero == null)
            {
                if (KingdomId != "")
                    // --- 国家势力 ---\n隶属于 {KINGDOM} 。
                    return LWNTextHelper.ResolveCompound("LWN_prompt_kingdom_template_has",
                        ("KINGDOM", Kingdom));
                else
                    // --- 国家势力 ---\n不效忠任何国家。
                    return LWNTextHelper.ResolveText("LWN_prompt_kingdom_template_no");
            }
            else
            {
                Clan clan = BaseHero.Clan;
                if (clan == null || clan.Kingdom == null)
                {
                    // --- 国家势力 ---\n当前不效忠于任何国家，处于独立状态。这意味着没有国王的庇护，但也无需纳税或响应征召。
                    return LWNTextHelper.ResolveText("LWN_prompt_kingdom_independent");
                }

                Kingdom kingdom = clan.Kingdom;

                // 1. 国家实力评估
                var allKingdoms = Campaign.Current.Kingdoms
                    .Where(k => !k.IsEliminated)
                    .OrderByDescending(k => V.KingdomStr(k))
                    .ToList();

                int totalKingdomCount = allKingdoms.Count;
                int rankIndex = allKingdoms.IndexOf(kingdom);
                int rank = rankIndex + 1;
                double rankPercent = (double)rank / totalKingdomCount;

                string powerStatus;
                if (rank == 1)
                    // 大陆霸主 (最强帝国)
                    powerStatus = LWNTextHelper.ResolveText("LWN_prompt_kingdom_power_hegemon");
                else if (rankPercent <= 0.3)
                    // 列强之一 (第一梯队)
                    powerStatus = LWNTextHelper.ResolveText("LWN_prompt_kingdom_power_great");
                else if (rankPercent <= 0.6)
                    // 中等国家 (区域势力)
                    powerStatus = LWNTextHelper.ResolveText("LWN_prompt_kingdom_power_middle");
                else
                    // 弱势国家 (风雨飘摇)
                    powerStatus = LWNTextHelper.ResolveText("LWN_prompt_kingdom_power_weak");

                // 2. 战争状态
                var enemies = V.GetEnemyKingdoms(kingdom).ToList();
                string warStatus = enemies.Count > 0
                    // 处于战争状态！正在与 [{ENEMIES}] 交战。
                    ? LWNTextHelper.ResolveCompound("LWN_prompt_kingdom_war_active",
                        ("ENEMIES", string.Join(", ", enemies.Select(e => e.Name))))
                    // 当前处于和平时期，休养生息。
                    : LWNTextHelper.ResolveText("LWN_prompt_kingdom_war_peace");

                // 3. 统治者关系
                string rulerRel;
                if (kingdom.Leader == BaseHero)
                    // 自身就是君主
                    rulerRel = LWNTextHelper.ResolveText("LWN_prompt_kingdom_ruler_self");
                else
                {
                    int relation = BaseHero.GetRelation(kingdom.Leader);
                    if (relation > 50)
                        // 君臣相知 (关系 {REL})
                        rulerRel = LWNTextHelper.ResolveCompound("LWN_prompt_kingdom_ruler_trusted", ("REL", relation.ToString()));
                    else if (relation < -10)
                        // 受到猜忌 (关系 {REL})
                        rulerRel = LWNTextHelper.ResolveCompound("LWN_prompt_kingdom_ruler_suspect", ("REL", relation.ToString()));
                    else
                        // 泛泛之交 (关系 {REL})
                        rulerRel = LWNTextHelper.ResolveCompound("LWN_prompt_kingdom_ruler_neutral", ("REL", relation.ToString()));
                }

                // --- 效忠国家 ---
                string kingdomInfo = LWNTextHelper.ResolveCompoundMixed("LWN_prompt_kingdom_hero",
                    ("NAME", (object)kingdom.Name),
                    ("CULTURE", (object)kingdom.Culture.Name),
                    ("POWER", powerStatus),
                    ("RANK", rank.ToString()),
                    ("TOTAL", totalKingdomCount.ToString()),
                    ("ARMIES", kingdom.Armies.Count.ToString()),
                    ("STRENGTH", V.KingdomStr(kingdom).ToString("F0")),
                    ("WAR", warStatus),
                    ("RULER_REL", rulerRel));

                // 🔴 身份深度（2026-08-10）：领主级 NPC 追加王国百科正文（引擎 XML 本地化描述，世界书层）。
                // 平民不注入——王国百科是贵族常识，村民只有坊间认知。
                if (IsNobleTier())
                {
                    string encText = kingdom.EncyclopediaText?.ToString();
                    if (!string.IsNullOrWhiteSpace(encText))
                        kingdomInfo += "\n（王国百科）" + encText;
                }
                return kingdomInfo;
            }
        }

        // 核心：推导当前的人物动机
        public string CalCurrentMotivation()
        {
            if (BaseHero == null)
            {
                if (Occupation == "村民")
                {
                    // 村民的长期/短期目标
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_villager_long");
                    // 做完手头的活。
                    ShortGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_villager_short");
                }
                else
                {
                    // 模板 NPC 默认目标
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_template_long");
                    // 执行当前的站岗或巡逻任务
                    ShortGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_template_short");
                }
                // 长期目标：{LIFE_GOAL}
                return LWNTextHelper.ResolveCompound("LWN_prompt_motivation_label",
                    ("LIFE_GOAL", LifeGoal), ("SHORT_GOAL", ShortGoal));
            }

            // --- 1. 数据准备 & 特性提取 ---
            Clan clan = BaseHero.Clan;
            Kingdom kingdom = clan?.Kingdom;

            int honor = CoreValues.ContainsKey("Honor") ? CoreValues["Honor"] : 0;
            int valor = CoreValues.ContainsKey("Valor") ? CoreValues["Valor"] : 0;
            int calculating = CoreValues.ContainsKey("Calculating") ? CoreValues["Calculating"] : 0;

            bool isPoor = (BaseHero.Gold < 10000);
            bool isRich = (BaseHero.Gold > 500000);
            bool atWar = kingdom != null && V.GetEnemyKingdoms(kingdom).Any();

            bool isKing = (kingdom != null && kingdom.Leader == BaseHero);
            bool isClanLeader = (clan != null && clan.Leader == BaseHero);
            bool isWanderer = BaseHero.IsWanderer;

            // --- 2. 计算人生目标 (LifeGoal) ---
            if (isKing)
            {
                if (Ism == IsmEnum.Ideal && Ambition > 50)
                    // 统一大陆，建立万世不朽的理想国度
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_king_ideal");
                else if (Ambition > 80)
                    // 征服一切，让所有国家臣服于我的脚下
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_king_conquer");
                else if (ActStyle == ActStyleEnum.Considerate)
                    // 维持国内稳定，确保王朝平稳传承
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_king_stable");
                else
                    // 享受权力的巅峰，维持现状
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_king_default");
            }
            else if (isClanLeader)
            {
                if (Ambition > 80 && honor < 0)
                    // 积蓄力量，推翻现有的君主，自立为王
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_clan_leader_usurp");
                else if (Ambition > 60)
                    // 带领家族成为王国中最有权势的豪门
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_clan_leader_magnate");
                else if (Ism == IsmEnum.Realistic && Desire == DesireEnum.Greedy)
                    // 垄断贸易，建立富可敌国的商业帝国
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_clan_leader_commerce");
                else if (honor > 0)
                    // 作为家族的守护者，尽忠职守，光耀门楣
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_clan_leader_loyal");
                else
                    // 在这个乱世中保全家族，使其延续下去
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_clan_leader_default");
            }
            else if (isWanderer)
            {
                if (Ambition > 50)
                    // 寻找明主或机会，摆脱流浪身份，晋升为贵族
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_wanderer_ambitious");
                else if (Desire == DesireEnum.Greedy)
                    // 作为雇佣兵或强盗，攫取尽可能多的财富
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_wanderer_greedy");
                else
                    // 四海为家，寻找属于自己的归宿
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_wanderer_default");
            }
            else // 普通家族成员/配偶
            {
                if (Ambition > 70 && calculating > 0)
                    // 在幕后操纵家族政治，掌握实权
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_member_manipulate");
                else if (valor > 1)
                    // 在战场上证明自己，成为家族的利剑
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_member_blade");
                else if (theImportanceOfFriendship == FriendshipImportanceEnum.Important)
                    // 辅佐家主（或配偶），维系家族成员间的羁绊
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_member_support");
                else
                    // 享受贵族生活，安稳度过一生
                    LifeGoal = LWNTextHelper.ResolveText("LWN_prompt_goal_member_default");
            }

            // --- 3. 计算短期目标 (ShortGoal) ---
            List<string> shortGoals = new List<string>();

            // [优先级0]：特殊癖好 (酒鬼)
            if (AlcoholDesire == AlcoholDesireEnum.Alcoholic)
            {
                // 非常渴求酒精，现在的首要念头是找个酒馆喝个烂醉
                shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_alcoholic"));
            }

            // [优先级1]：战争状态
            if (atWar)
            {
                if (valor > 0 || Spirit == SpiritEnum.Brave)
                    // 备战：渴望在当前的战争中击败敌将，赢取声望与战利品
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_war_eager"));
                else if (Spirit == SpiritEnum.Timid || valor < 0)
                    // 避战：战火纷飞，只想躲在安全的城墙后，避免被俘虏
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_war_avoid"));
                else if (honor < 0 && calculating > 0)
                    // 投机：趁着战争混乱，通过掠夺村庄或发战争财来获利
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_war_profit"));
                else
                    // 尽职：响应国家号召，保卫领土
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_war_duty"));
            }

            // [优先级2]：经济危机
            if (isPoor)
            {
                if (honor > 0)
                    // 筹款：家族财政赤字，需要通过正当贸易或任务来维持开支
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_economy_shortfall"));
                else
                    // 搞钱：缺钱了，不论是抢劫商队还是敲诈勒索，必须尽快弄到第纳尔
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_economy_cash"));
            }

            // [优先级3]：平稳时期的个人欲望
            if (shortGoals.Count == 0)
            {
                switch (DesireType)
                {
                    case DesireTypeEnum.Book:
                        // 求知：希望能在这个城市找到珍稀的古籍或知识
                        shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_desire_knowledge"));
                        break;
                    case DesireTypeEnum.Weapon:
                        // 整备：正在寻找一把趁手的神兵利器，或者改良现有的装备
                        shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_desire_gear"));
                        break;
                    case DesireTypeEnum.Nanman:
                    case DesireTypeEnum.Art:
                        // 收藏：对异域的珍宝或艺术品非常感兴趣，想要将其收入囊中
                        shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_desire_collect"));
                        break;
                    case DesireTypeEnum.Money:
                        if (Desire == DesireEnum.Greedy)
                            // 敛财：虽然不缺钱，但看到金币增加是最快乐的事
                            shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_desire_hoard"));
                        else
                            // 经营：管理好现有的产业和商队
                            shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_desire_manage"));
                        break;
                }

                if (Spouse == "None" && BaseHero.Age < 40 && BaseHero.Age > 16)
                {
                    // 联姻：正在物色合适的政治联姻对象
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_marriage"));
                }
                else if (JobTendency == JobTendencyEnum.WarriorOnly)
                {
                    // 磨炼：在竞技场打磨武艺，或者训练手下的士兵
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_training"));
                }
                else if (ActStyle == ActStyleEnum.Flippancy)
                {
                    // 享乐：最近只想举办宴会，吃喝玩乐
                    shortGoals.Add(LWNTextHelper.ResolveText("LWN_prompt_shortgoal_pleasure"));
                }
            }

            ShortGoal = shortGoals.Count > 0
                ? shortGoals[0]
                // 待命：目前没有什么特别的打算，随遇而安。
                : LWNTextHelper.ResolveText("LWN_prompt_shortgoal_standby");

            // 长期目标：{LIFE_GOAL}
            return LWNTextHelper.ResolveCompound("LWN_prompt_motivation_label",
                ("LIFE_GOAL", LifeGoal), ("SHORT_GOAL", ShortGoal));
        }

        public string GetSelfInfo()
        {


            StringBuilder sb = new StringBuilder();

            // ## 人物核心设定
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_self_hero_header",
                "## Core Identity\n- Name: [{NAME}]\n- Gender: {GENDER}\n- Age: {AGE}\n- Spouse: [{SPOUSE}]\n- Occupation: [{OCC}]"));

            if (BaseHero == null && BaseCharacter == null)
            {
                // 无
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_self_none"));
            }
            if (BaseHero == null && BaseCharacter != null)
            {
                // -身份：{NAME}\n-等级：{TIER}\n职业1：{OCC1}\n-职业2：{OCC2}
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_prompt_self_template_npc",
                    ("NAME", (object)BaseCharacter.Name),
                    ("TIER", BaseCharacter.Tier.ToString()),
                    ("OCC1", BaseCharacter.Occupation.ToString()),
                    ("OCC2", LocalizeRole(BaseCharacter.IsSoldier))));
            }
            else
            {
                // 人物核心设定
                var heroHeader = LWNTextHelper.ResolveCompoundMixed("LWN_prompt_self_hero_header",
                    ("NAME", (object)BaseHero.Name),
                    ("GENDER", LocalizeGender(BaseHero.IsFemale)),
                    ("AGE", ((int)BaseHero.Age).ToString()),
                    ("SPOUSE", Spouse ?? ""),
                    ("OCC", LocalizeTrait(Occupation)));
                // Replace the generic header with actual data
                sb.Clear();
                sb.AppendLine(heroHeader);

                // 自我阶级认知
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_self_worth_header"));
                sb.AppendLine(GetSelfWorthDescription());

                // 🔴 Hero 百科文本（2026-08-10，世界书层）：有名英雄（国王/领主）带引擎 XML 写的百科介绍
                // （如蒙楚格："...dreams of glory, of surpassing his ancestor Urkhun..."）——每个英雄不同，
                // 是宝贵个性素材（区别于被裁剪的雷同数据）；平民英雄无此字段（空）自然跳过。
                try
                {
                    string heroEnc = BaseHero.EncyclopediaText?.ToString();
                    if (!string.IsNullOrWhiteSpace(heroEnc))
                    {
                        sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_section_reputation", "## What People Say of Me")); // lwn-ignore: B
                        sb.AppendLine(heroEnc);
                    }
                }
                catch { }

                // 🔴 2026-08-10 裁剪：目标与动机 / 喜好信息 / 性格和价值观 三段不再拼入——
                // 这些数据原版不存在（GameDatabase 无记录），所有 NPC 取值雷同（普通/0），
                // 拼入只有噪声没有差异，还白白烧 token。要差异化的动机走谈判特质等游戏内真实数据。
            }
            // --- 当前状态 (Agent 层面) ---
            if (Mission.Current != null && Mission.Current.MainAgent != null)
            {
                var agent = Mission.Current.Agents.FirstOrDefault(a => a.Character == BaseCharacter);
                if (agent != null)
                {
                    float hpPercent = agent.Health / agent.HealthLimit * 100;
                    string healthDesc = hpPercent > 80
                        // 精力充沛
                        ? LWNTextHelper.ResolveText("LWN_prompt_self_health_energetic")
                        : (hpPercent > 30
                            // 受了些伤
                            ? LWNTextHelper.ResolveText("LWN_prompt_self_health_wounded")
                            // 身负重伤，濒临倒下
                            : LWNTextHelper.ResolveText("LWN_prompt_self_health_critical"));
                    // ## 当前生理状态
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_self_status_header"));
                    // 健康状况：{DESC} (HP: {HP}%)。
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_self_health_line",
                        ("DESC", healthDesc), ("HP", hpPercent.ToString("F0"))));
                }
            }
            return sb.ToString();

        }

        public string GetPersonaPrompt()
        {
            StringBuilder sb = new StringBuilder();


            sb.AppendLine(GetSelfInfo());
            // 🔴 2026-08-10 重构：家族背景/国家势力 平时只拼一句自我认知（GetStandingSummary），
            // 全量数据（领地/财富/国家实力/战争状态等）在玩家提到相关话题时由
            // GetMentionedBackgroundPrompt 按需拼入——平时零噪声，聊到了才给细节。
            sb.AppendLine(GetStandingSummary());
            // 🔴 2026-08-17（自动世界观 §5 增量②）：NPC 自身文化百科拼入——引擎原产
            // （CultureObject.EncyclopediaText，spcultures.xml 每文化 200+ 字 lore），天然贴合该 NPC
            // 文化、零生成成本（每存档只跑一份世界格局生成，不可能按文化跑 8 份——文化内容对话时
            // 从 NPC 自身文化百科直接拼入）。平民/领主/模板 NPC 全量注入（无身份裁剪）。
            sb.AppendLine(GetCultureInfo());
            // 队伍身份：NPC 知道自己随主公同行（否则会答出"我不认识你"这种出戏回复）
            sb.AppendLine(GetPartyRoleInfo());

            return sb.ToString();
        }

        /// <summary>自身文化百科（引擎原产，2026-08-17）：Hero 用 Culture（兜底 Clan/Kingdom），
        /// 模板 NPC 用 BaseCharacter?.Culture。文化名 + EncyclopediaText 拼入 persona（世界书层）。</summary>
        public string GetCultureInfo()
        {
            try
            {
                CultureObject culture = null;
                if (BaseHero != null)
                {
                    culture = BaseHero.Culture ?? BaseHero.Clan?.Culture ?? BaseHero.Clan?.Kingdom?.Culture;
                }
                else if (BaseCharacter != null)
                {
                    culture = BaseCharacter.Culture;
                }
                if (culture == null) return "";
                string enc = culture.EncyclopediaText?.ToString();
                // 模板 NPC（无 Hero）即使无百科也补文化名（§5 增量③）；Hero 无百科 → 零注入
                //（StandingSummary 已有归属，不重复造"我是X人"行）
                if (string.IsNullOrWhiteSpace(enc) && BaseHero != null) return "";
                var sb = new StringBuilder();
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_section_culture", "## My People and Culture")); // lwn-ignore: B
                // 本地化：LWN_prompt_culture_member（我是{NAME}人，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_culture_member", ("NAME", culture.Name.ToString()))
                    + (string.IsNullOrWhiteSpace(enc) ? "" : enc));
                return sb.ToString();
            }
            catch { return ""; }
        }

        /// <summary>一句话的出身与立场（家族/国家的自我认知，无具体数据）。
        /// 与 GetClanInfo/GetKingdomInfo（全量数据版）对应；平时人设只拼这段。</summary>
        public string GetStandingSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine(LWNTextHelper.ResolveText("LWN_prompt_section_standing", "## My Origins and Allegiance")); // lwn-ignore: B
            if (BaseHero == null && BaseCharacter == null)
            {
                // 本地化：LWN_prompt_standing_origin_unknown（出身不明，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_origin_unknown")))); // lwn-ignore: B
                // 本地化：LWN_prompt_standing_kingdom_none（不效忠任何国家，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_kingdom", "- Kingdom: {TEXT}", ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_kingdom_none")))); // lwn-ignore: B
                return sb.ToString();
            }
            if (BaseHero == null)
            {
                // 模板 NPC（士兵/村民）：一介平民的自我认知
                string role = LocalizeRole(BaseCharacter.IsSoldier);
                // 本地化：LWN_prompt_standing_template_serving（服役谋生，双桶）
                sb.AppendLine(ClanId != ""
                    ? LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", ("TEXT", LWNTextHelper.ResolveCompound("LWN_prompt_standing_template_serving", ("ROLE", role), ("CLAN", Clan)))) // lwn-ignore: B
                    // 本地化：LWN_prompt_standing_template_commoner（出身平民，双桶）
                    : LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", ("TEXT", LWNTextHelper.ResolveCompound("LWN_prompt_standing_template_commoner", ("ROLE", role))))); // lwn-ignore: B
                // 本地化：LWN_prompt_standing_kingdom_none（不效忠任何国家，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_kingdom", "- Kingdom: {TEXT}", ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_kingdom_none")))); // lwn-ignore: B
                return sb.ToString();
            }
            // Hero 版
            Clan clan = BaseHero.Clan;
            if (clan != null)
            {
                string selfStatus;
                if (BaseHero == clan.Leader)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_leader"); // lwn-ignore: B
                else if (BaseHero == clan.Leader.Spouse)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_spouse"); // lwn-ignore: B
                else if (BaseHero.Father == clan.Leader || BaseHero.Mother == clan.Leader)
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_heir"); // lwn-ignore: B
                else if (clan.Companions.Contains(BaseHero))
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_companion"); // lwn-ignore: B
                else
                    selfStatus = LWNTextHelper.ResolveText("LWN_prompt_clan_status_member"); // lwn-ignore: B
                // "家族家臣/同伴"等状态文本自带"家族"前缀，用"地位"措辞避免"家族的家族X"重复
                // 本地化：LWN_prompt_standing_clan_status（家族地位，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", // lwn-ignore: B
                    // 本地化：LWN_prompt_standing_clan_status（双桶）
                    ("TEXT", LWNTextHelper.ResolveCompound("LWN_prompt_standing_clan_status",
                        ("CLAN", clan.Name.ToString()), ("STATUS", selfStatus)))));
            }
            else if (BaseHero.IsWanderer)
            {
                // 本地化：LWN_prompt_standing_wanderer（游民，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_wanderer")))); // lwn-ignore: B
            }
            else
            {
                // 本地化：LWN_prompt_standing_no_background（无显赫背景，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_family", "- Family: {TEXT}", ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_no_background")))); // lwn-ignore: B
            }

            Kingdom kingdom = clan?.Kingdom;
            if (kingdom != null)
                // 本地化：LWN_prompt_standing_kingdom_serve（效忠{KINGDOM}，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_kingdom", "- Kingdom: {TEXT}", // lwn-ignore: B
                    // 本地化：LWN_prompt_standing_kingdom_serve（双桶）
                    ("TEXT", LWNTextHelper.ResolveCompound("LWN_prompt_standing_kingdom_serve",
                        ("KINGDOM", kingdom.Name.ToString())))));
            else
                // 本地化：LWN_prompt_standing_kingdom_free（自由之身，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_standing_kingdom", "- Kingdom: {TEXT}", // lwn-ignore: B
                    // 本地化：LWN_prompt_standing_kingdom_free（双桶）
                    ("TEXT", LWNTextHelper.ResolvePrompt("LWN_prompt_standing_kingdom_free"))));
            return sb.ToString();
        }

        /// <summary>与玩家的关系（队伍身份注入）：NPC 在玩家家族/队伍里时，明确告知玩家是其主公。
        /// 解决"NPC 不知道自己在玩家队伍、玩家是其主公"的出戏（2026-08-10 日志实锤）。
        /// 🔴 2026-08-16（用户裁定：不在队伍就老老实实说不清楚）：三态化——同行（主队内）→
        /// 原「正随他一同闯荡」文案；带队在外（分兵）→ 同左（原行为不变）；家族但不在队伍
        /// （留守/他处）→ 新文案明确"不在队伍、对主公行踪所知有限"，禁止 LLM 自称同行答「咱们」
        /// （实机 2026-08-16 阿速甘案：家族成员答"咱们眼下在卡拉迪亚的大道上"）。</summary>
        public string GetPartyRoleInfo()
        {
            if (BaseHero == null || Hero.MainHero == null || BaseHero == Hero.MainHero) return "";
            // 🔴 注意：本类有 string Clan 字段遮蔽类型名，静态访问必须全限定
            bool inPlayerClan = BaseHero.Clan != null && BaseHero.Clan == TaleWorlds.CampaignSystem.Clan.PlayerClan;
            // 同行判定用严格口径（IsInMainParty），不用 IsPlayerPartyMember（IsPlayerCompanion
            // 捷径把留守随从也算队伍成员）；分兵随从 = 带队在外，仍算"在队伍体系里"（原行为不变）
            bool inPlayerParty = FriendlinessHelper.IsInMainParty(BaseHero)
                || PartySplitFlow.IsSplitPartyLeader(BaseHero);
            if (!inPlayerClan && !inPlayerParty) return "";
            string playerName = Hero.MainHero.Name.ToString();
            var sb = new StringBuilder();
            // 🔴 2026-08-17（称呼纪律）：段标题不再写死"主公"——"## 你与 {NAME} 的关系"（{NAME} = 玩家名运行时拼）
            sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_section_lord",
                "## Your Relationship with {NAME}", ("NAME", playerName))); // lwn-ignore: B
            if (inPlayerParty)
            {
                if (inPlayerClan)
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_lord_clan", // lwn-ignore: B
                        "You are a member of the clan of {NAME} (your lord), traveling with him. He feeds and commands you, he is your lord.",
                        ("NAME", playerName)));
                else
                    sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_lord_party", // lwn-ignore: B
                        "You are in the party of {NAME} (your lord), his retainer on campaign.",
                        ("NAME", playerName)));
            }
            else
            {
                // 家族但不在队伍：老实承认不知行踪——位置认知边界（L4 遥距）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_prompt_lord_clan_away", // lwn-ignore: B
                    "You are a member of the clan of {NAME}. {NAME} is your lord, but you are not with his party right now — you know little of his current whereabouts.",
                    ("NAME", playerName)));
            }
            return sb.ToString();
        }

        /// <summary>玩家是否自己的主公（同一家族或同队伍）——PromptBuilder 措辞用（"你的主公 vs 对方"）。</summary>
        public bool IsPlayerSubordinate()
        {
            if (BaseHero == null || Hero.MainHero == null || BaseHero == Hero.MainHero) return false;
            // 🔴 注意：本类有 string Clan 字段遮蔽类型名，静态访问必须全限定
            if (BaseHero.Clan != null && BaseHero.Clan == TaleWorlds.CampaignSystem.Clan.PlayerClan) return true;
            return FriendlinessHelper.IsPlayerPartyMember(BaseHero);
        }

        // 家族/国家话题触发词（提到才拼全量背景；与 WorldFactProvider 主题注册表同思路）
        // 本地化：LWN_prompt_family_topic_kw（家族话题关键词表，双桶，逗号分隔）——功能性匹配词表，
        // 运行时按当前语言取词（CN 桶中文词 / EN 桶英文词），禁止静态缓存（语言可能热切换）
        private static string[] LoadTopicKeywords(string key) =>
            LWNTextHelper.ResolvePrompt(key)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        private static string[] FamilyTopicKeywords =>
            // 本地化：LWN_prompt_family_topic_kw（双桶）
            LoadTopicKeywords("LWN_prompt_family_topic_kw");

        private static string[] KingdomTopicKeywords =>
            // 本地化：LWN_prompt_kingdom_topic_kw（双桶）
            LoadTopicKeywords("LWN_prompt_kingdom_topic_kw");

        /// <summary>玩家提到家族/国家话题时，才拼入全量背景（GetClanInfo/GetKingdomInfo）。
        /// 平时人设只有 GetStandingSummary 的一句自我认知，零噪声。调用方：IM 回复 / 当面闲聊 / 谈判。</summary>
        public string GetMentionedBackgroundPrompt(string playerText)
        {
            if (string.IsNullOrWhiteSpace(playerText)) return "";
            var sb = new StringBuilder();
            foreach (var kw in FamilyTopicKeywords)
            {
                if (playerText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.AppendLine(GetClanInfo());
                    break;
                }
            }
            foreach (var kw in KingdomTopicKeywords)
            {
                if (playerText.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sb.AppendLine(GetKingdomInfo());
                    break;
                }
            }
            return sb.ToString();
        }

        public float CalculateEstimatedValue()
        {

            if (BaseHero == null)
            {
                int tier = BaseCharacter?.Tier ?? 1;
                EstimatedValue = 50 + (tier * 100); // 比如 3级兵值 350金
                return EstimatedValue;
            }
            else
            {

                float baseValue = 0f;

                // 1. 基础身价 (基于等级和声望)
                // 一个 1级的路人大概 100金，一个 30级的大佬至少值 5000金
                baseValue += BaseHero.Level * 200;
                if (BaseHero.Clan != null)
                {
                    baseValue += BaseHero.Clan.Renown * 10;
                }

                // 2. 家族等级修正 (Clan Tier) - 阶级壁垒是指数级的
                if (BaseHero.Clan != null)
                {
                    float tierMultiplier = 0;
                    switch (BaseHero.Clan.Tier)
                    {
                        case 0: tierMultiplier = 0; break;     // 平民/流浪者
                        case 1: tierMultiplier = 500; break;  // 微末小族
                        case 2: tierMultiplier = 1500; break; // 此后指数上升
                        case 3: tierMultiplier = 4000; break;
                        case 4: tierMultiplier = 10000; break;
                        case 5: tierMultiplier = 25000; break; // 顶级豪门
                        case 6: tierMultiplier = 50000; break; // 皇亲国戚
                        default: tierMultiplier = BaseHero.Clan.Tier * 100000; break;
                    }
                    baseValue += tierMultiplier;

                    // 3. 家族内地位修正
                    if (BaseHero == BaseHero.Clan.Leader)
                    {
                        baseValue *= 2.5f; // 族长非常贵，因为动了族长等于动了整个家族
                    }
                    else if (BaseHero.Clan.Leader.Spouse == BaseHero)
                    {
                        baseValue *= 1.5f; // 族长夫人/丈夫也很贵
                    }
                    // 简单判断是否是继承人 (简单逻辑：子女)
                    else if (BaseHero.Father == BaseHero.Clan.Leader || BaseHero.Mother == BaseHero.Clan.Leader)
                    {
                        baseValue *= 1.2f; // 嫡系子女
                    }

                    // 4. 资产修正 (领地)
                    // 拥有封地的人，身价会极高，通常意味着如果要挖角，你得付出天价或者交换领地
                    int townCount = BaseHero.Clan.Fiefs.Count(f => f.IsTown);
                    int castleCount = BaseHero.Clan.Fiefs.Count(f => f.IsCastle);

                    // 如果他是族长，家族的钱就是他的身价一部分
                    if (BaseHero == BaseHero.Clan.Leader)
                    {
                        baseValue += (townCount * 200000); // 一座城价值极高
                        baseValue += (castleCount * 50000);
                        baseValue += BaseHero.Gold * 0.2f; // 现金流也是身价
                    }
                }

                // 5. 王国地位修正
                if (BaseHero.Clan?.Kingdom != null)
                {
                    if (BaseHero.Clan.Kingdom.Leader == BaseHero)
                    {
                        baseValue *= 5.0f; // 国王几乎是无价的，除非亡国
                    }
                    else if (BaseHero.Clan.Tier >= 5)
                    {
                        baseValue *= 1.2f; // 王国重臣
                    }
                }

                // 6. 特殊修正：流浪者 (Wanderer)
                if (BaseHero.IsWanderer)
                {
                    // 🔴 身价 = 实际招募价（与 RecruitHero 谈判 BaseDifficulty=1.0 统一，见 NegotiationSystem.cs）。
                    // 旧公式 2000 + level*500（17 级 = 10500）虚高——玩家按谈判 5% 系数实际只花 ~525 就招到人，
                    // 但 prompt 里 NPC 自报身价 10500，自抬身价出戏。新公式 100 + level*25：17 级 ≈ 525，与招募花费同量级。
                    baseValue = 100 + (BaseHero.Level * 25);
                }
                EstimatedValue = baseValue;
                return baseValue;
            }
        }
        public string GetSelfWorthDescription()
        {
            float val = CalculateEstimatedValue();
            string valDesc;

            if (val < 5000)
                // 微不足道。只要对方给点小恩小惠，或者表现出诚意，就愿意跟随对方。
                valDesc = LWNTextHelper.ResolveText("LWN_prompt_worth_low");
            else if (val < 50000)
                // 颇有身价。是有一定身份的人，一般的筹码打动不了，对方需要拿出真金白银。
                valDesc = LWNTextHelper.ResolveText("LWN_prompt_worth_medium");
            else if (val < 200000)
                // 价值连城。作为名门望族的核心成员，身价极高。除非有巨大的利益交换（如城池、巨额财富），否则对方免谈。
                valDesc = LWNTextHelper.ResolveText("LWN_prompt_worth_high");
            else
                // 无价之宝/权倾天下。你想收买？这简直是天方夜谭，除非你能拿出半个王国的财富。
                valDesc = LWNTextHelper.ResolveText("LWN_prompt_worth_extreme");

            // 出身源头：{ORIGIN}
            return LWNTextHelper.ResolveCompound("LWN_prompt_worth_template",
                ("ORIGIN", LocalizeTrait(OriginStr)),
                ("VAL", val.ToString("F0")),
                ("DESC", valDesc),
                ("CURRENCY", Settings.Instance.CurrencyName));
        }

        public List<Hero> GetCloseRelations(Hero hero, out string relationStr)
        {
            //获取配偶、家人、好友
            HashSet<Hero> relations = new HashSet<Hero>();
            StringBuilder sb = new StringBuilder();
            // 本地化：关系网头部（{NAME}=英雄名）
            string deceased = LWNTextHelper.ResolveText("LWN_prompt_relations_deceased", " (deceased)");
            // 获取 {NAME} 的关系网：
            sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_prompt_relations_header",
                ("NAME", (object)hero.Name)));
            // 返回 true 表示成功添加（也就是之前没加过）
           


            if (hero.Spouse != null)
            {
                relations.Add(hero.Spouse);
                // 本地化：配偶行（{NAME}=配偶名，{DECEASED}=已过世标记或空）
                string spouseDeceased = !hero.Spouse.IsAlive ? deceased : "";
                // -配偶：{NAME}{DECEASED}
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_prompt_relations_spouse",
                    ("NAME", (object)hero.Spouse.Name), ("DECEASED", spouseDeceased)));
            }

            if(hero.Children.Count >0)
            {
                // 本地化：子女标签
                sb.Append(LWNTextHelper.ResolveText("LWN_prompt_relations_children_label"));
                foreach (var child in hero.Children)
                {
                    sb.Append($"{child.Name} ");
                    relations.Add(child);
                }
                sb.Append("\n");
            }
            List<Hero> rawSiblings = new List<Hero>();
            if (hero.Father != null)
            {
                relations.Add(hero.Father);
                rawSiblings.AddRange(hero.Father.Children);
                // 本地化：父亲行（{NAME}=父亲名，{DECEASED}=已过世标记）
                string fatherDeceased = !hero.Father.IsAlive ? deceased : "";
                // -父亲：{NAME}{DECEASED}
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_prompt_relations_father",
                    ("NAME", (object)hero.Father.Name), ("DECEASED", fatherDeceased)));
            }
            if (hero.Mother != null)
            {
                relations.Add(hero.Mother);
                rawSiblings.AddRange(hero.Mother.Children);
                // 本地化：母亲行（{NAME}=母亲名，{DECEASED}=已过世标记）
                string motherDeceased = !hero.Mother.IsAlive ? deceased : "";
                // -母亲：{NAME}{DECEASED}
                sb.AppendLine(LWNTextHelper.ResolveCompoundMixed("LWN_prompt_relations_mother",
                    ("NAME", (object)hero.Mother.Name), ("DECEASED", motherDeceased)));
            }

            if (rawSiblings.Count > 0)
            {
                // 本地化：兄弟标签
                string brothers = LWNTextHelper.ResolveText("LWN_prompt_relations_siblings_label");
                bool hasBrother = false;
                foreach (var sibling in rawSiblings)
                {
                    // 排除自己，且未被记录过
                    if (sibling != hero && !relations.Contains(sibling) &&!sibling.IsFemale)
                    {
                        relations.Add(sibling); // 标记为已记录
                        brothers += $"{sibling.Name} ";
                        hasBrother = true;
                    }
                }
                if (hasBrother) sb.AppendLine(brothers);

                // 本地化：姐妹标签
                string sisters = LWNTextHelper.ResolveText("LWN_prompt_relations_siblings_label");
                bool hasSister = false;
                foreach (var sibling in rawSiblings)
                {
                    // 排除自己，且未被记录过
                    if (sibling != hero && !relations.Contains(sibling) && sibling.IsFemale)
                    {
                        relations.Add(sibling); // 标记为已记录
                        sisters+= $"{sibling.Name} ";
                        hasSister = true;
                    }
                }
                if (hasSister) sb.AppendLine(sisters);
            }

            List<Hero> rawUncles = new List<Hero>();
            // 父系亲属
            if (hero.Father != null)
            {
                if (hero.Father.Father != null) rawUncles.AddRange(hero.Father.Father.Children);
                if (hero.Father.Mother != null) rawUncles.AddRange(hero.Father.Mother.Children);
            }
            // 母系亲属
            if (hero.Mother != null)
            {
                if (hero.Mother.Father != null) rawUncles.AddRange(hero.Mother.Father.Children);
                if (hero.Mother.Mother != null) rawUncles.AddRange(hero.Mother.Mother.Children);
            }

            if (rawUncles.Count > 0)
            {
                // 本地化：叔伯姑舅姨标签
                string uncles = LWNTextHelper.ResolveText("LWN_prompt_relations_uncles_label");
                bool hasUncle = false;
                foreach (var uncle in rawUncles)
                {
                    // 排除父母 (因为父母已经在前面加过了)，排除自己(虽然不可能)，且未被记录过
                    if (uncle != hero.Father && uncle != hero.Mother && !relations.Contains(uncle))
                    {
                        relations.Add(uncle);
                        uncles += $"{uncle.Name} ";
                        hasUncle = true;
                    }
                }
                if (hasUncle) sb.AppendLine(uncles);
            }



            if (hero.Clan != null)
            {
                // 本地化：其他家族成员标签
                string family = LWNTextHelper.ResolveText("LWN_prompt_relations_other_clan_label");

                


                foreach (var member in hero.Clan.Heroes)
                {
                    if (member != hero && member.IsAlive && member.IsLord) // 只传给贵族，不传给小兵
                    {
                        if (!relations.Contains(member))
                        {
                            relations.Add(member);
                            family += $"{member.Name} ";
                        }
                    }
                }
                sb.AppendLine(family);
            }
            // 注意：遍历所有英雄比较耗时，可以限制范围，比如只遍历同一国家的
            if (hero.MapFaction != null)
            {
                // 本地化：好友标签
                string friends = LWNTextHelper.ResolveText("LWN_prompt_relations_friends_label");
                foreach (var other in hero.MapFaction.Heroes)
                {
                    if (other != hero && other.IsAlive && other.GetRelation(hero) > 20 && !relations.Contains(other))
                    {
                        relations.Add(other);
                        friends += $"{other.Name} ";
                    }
                }
                sb.AppendLine(friends);

                // 本地化：政敌标签
                string enemy = LWNTextHelper.ResolveText("LWN_prompt_relations_rivals_label");
                foreach (var other in hero.MapFaction.Heroes)
                {
                    if (other != hero && other.IsAlive && other.GetRelation(hero) < -10 && !relations.Contains(other))
                    {
                        relations.Add(other);
                        enemy += $"{other.Name} ";
                    }
                }
                sb.AppendLine(enemy);
            }
            //如果自己是Clan的leader，那么同一王国内的其他Clan的leader也会收到传闻（同事）
            if (hero.Clan != null && hero.Clan.Leader == hero && hero.Clan.Kingdom != null)
            {
                // 本地化：族长圈子标签
                string leaders = LWNTextHelper.ResolveText("LWN_prompt_relations_clan_circle_label");
                foreach (var clan in hero.Clan.Kingdom.Clans)
                {
                    if (clan.Leader != hero && clan.Leader.IsAlive && !relations.Contains(clan.Leader))
                    {
                        relations.Add(clan.Leader);
                        leaders += $"{clan.Leader.Name} ";
                    }
                }
                sb.AppendLine(leaders);
            }

            //


            //如果自己是Kingdom的Leader，那么同盟的Kingdom的Leader也会收到，后续补充
            if (hero.Clan != null && hero.Clan.Kingdom != null && hero.Clan.Kingdom.Leader == hero)
            {
                //foreach (var kingdom in hero.Clan.Kingdom.)
            }

            //DebugLogger.Log(sb.ToString());
            relationStr = sb.ToString();
            return relations.ToList();
        }
    }
}
