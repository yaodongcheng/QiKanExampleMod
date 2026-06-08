using LivingWorldNpcs.Story;
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
  

       
        public string GetClanInfo()
        {
            StringBuilder sb = new StringBuilder();
            if (BaseHero == null && BaseCharacter == null)
            {
                return "无家族势力，孤身一人，没有任何家族背景支持。";
            }
            else if (BaseHero == null && BaseCharacter != null)
            {
                if(ClanId!="")
                {
                    return $"--- 家族背景 ---\n无显赫家族背景。作为一名普通的{Occupation}，依靠在{Clan}家族中服役维持生计。";
                }
                else
                {
                    return $"--- 家族背景 ---\n无显赫家族背景。作为一名普通的{Occupation}，依靠在别人家族中服役维持生计。";
                }
            }
            else
            {

                Clan clan = BaseHero.Clan;
                if (clan == null)
                {
                    return "无家族势力（游民），孤身一人，没有任何家族背景支持。";
                }


                // 1. 确定家族内身份 (细化版)
                string selfStatus = "普通成员";
                if (BaseHero == clan.Leader) selfStatus = "家族族长 (拥有家族最高决策权)";
                else if (BaseHero == clan.Leader.Spouse) selfStatus = "族长配偶 (享有极高尊荣)";
                else if (BaseHero.Father == clan.Leader || BaseHero.Mother == clan.Leader) selfStatus = "家族少主/千金 (嫡系血亲)";
                else if (clan.Companions.Contains(BaseHero)) selfStatus = "家族家臣/同伴 (因能力被招募，地位取决于功绩)";

                // 2. 领地统计 (计算世界占比)
                int myTowns = clan.Fiefs.Count(f => f.IsTown);
                int myCastles = clan.Fiefs.Count(f => f.IsCastle);
                int myTotalFiefs = myTowns + myCastles;

                // 获取全图所有的城镇和城堡数量
                var allSettlements = Campaign.Current.Settlements;
                int worldTotalTowns = allSettlements.Count(s => s.IsTown);
                int worldTotalCastles = allSettlements.Count(s => s.IsCastle);
                int worldTotalFiefs = worldTotalTowns + worldTotalCastles;

                // 计算占比
                double fiefPercentage = worldTotalFiefs > 0 ? (double)myTotalFiefs / worldTotalFiefs * 100 : 0;
                // 通常主要看 Leader 的钱，因为钱袋子是通用的
                // 3. 经济评估
                int clanWealth = clan.Leader.Gold;
                string wealthDesc = clanWealth > 1000000 ? "富可敌国" :
                                   (clanWealth > 500000 ? "腰缠万贯" :
                                   (clanWealth > 100000 ? "家境殷实" :
                                   (clanWealth > 30000 ? "勉强维持" : "囊中羞涩")));

                // 4. 综合实力评估 (不再只看 Tier)
                string strengthDesc;
                if (fiefPercentage >= 10.0) strengthDesc = "一方诸侯 (领土广阔，足以自立)";
                else if (fiefPercentage >= 3.0 && clan.Tier >= 4) strengthDesc = "顶级权贵 (拥有大量封地)";
                else if (clanWealth > 1000000 && clan.Tier >= 3) strengthDesc = "金融巨鳄 (虽领地不多但财力惊人)";
                else if (clan.Tier >= 5) strengthDesc = "传统豪门 (声望极高)";
                else if (myTotalFiefs > 0) strengthDesc = "有地贵族 (拥有根基)";
                else strengthDesc = "无地游族 (飘摇不定)";

                // 5. 组装 Prompt
                sb.AppendLine($"--- 家族背景 ---");
                sb.AppendLine($"家族名称：[{clan.Name}]，等级：{clan.Tier}级。声望：{clan.Renown:F0}。");
                sb.AppendLine($"综合评价：{strengthDesc}。");
                sb.AppendLine($"在家族中的地位：{selfStatus}。");

                // 显示具体领地数据和全球占比
                sb.Append($"家族领地：拥有 {myTowns} 座城市 和 {myCastles} 座城堡");
                sb.AppendLine($" (共占全境的 {fiefPercentage:F2}%，{myTotalFiefs}/{worldTotalFiefs})。");

                sb.AppendLine($"家族财力：{wealthDesc} (现有资金 {clanWealth} 两黄金)。");
                sb.AppendLine($"家族影响力：{clan.Influence:F0}。");
            }
            return sb.ToString();
        }

        public string GetKingdomInfo()
        {
            StringBuilder sb = new StringBuilder();
            if (BaseHero == null)
            {
                if(KingdomId!="")
                    return $"--- 国家势力 ---\n隶属于 {Kingdom} 。";
                else
                    return $"--- 国家势力 ---\n不效忠任何国家。";
            }
            else
            {

                Clan clan = BaseHero.Clan;
                if (clan == null || clan.Kingdom == null)
                {
                    return "--- 国家势力 ---\n当前不效忠于任何国家，处于独立状态。这意味着没有国王的庇护，但也无需纳税或响应征召。";
                }

                Kingdom kingdom = clan.Kingdom;

                // 1. 国家实力评估 (引入相对排名)
                // 获取所有未灭亡的国家并按实力排序
                var allKingdoms = Campaign.Current.Kingdoms
                    .Where(k => !k.IsEliminated) // 排除已经灭亡的国家
                    .OrderByDescending(k => k.TotalStrength)
                    .ToList();

                int totalKingdomCount = allKingdoms.Count;
                int rankIndex = allKingdoms.IndexOf(kingdom);
                int rank = rankIndex + 1; // 索引转排名

                // 计算排位百分比 (前20%算霸主)
                double rankPercent = (double)rank / totalKingdomCount;

                string powerStatus;
                if (rank == 1) powerStatus = "大陆霸主 (最强帝国)";
                else if (rankPercent <= 0.3) powerStatus = "列强之一 (第一梯队)";
                else if (rankPercent <= 0.6) powerStatus = "中等国家 (区域势力)";
                else powerStatus = "弱势国家 (风雨飘摇)";

                // 2. 战争状态
                var enemies = FactionManager.GetEnemyKingdoms(kingdom).ToList();
                string warStatus = enemies.Count > 0
                    ? $"处于战争状态！正在与 [{string.Join(", ", enemies.Select(e => e.Name))}] 交战。"
                    : "当前处于和平时期，休养生息。";

                // 3. 统治者关系
                string rulerRel;
                if (kingdom.Leader == BaseHero) rulerRel = "自身就是君主";
                else
                {
                    int relation = BaseHero.GetRelation(kingdom.Leader);
                    rulerRel = relation > 50 ? $"君臣相知 (关系 {relation})" :
                              (relation < -10 ? $"受到猜忌 (关系 {relation})" : $"泛泛之交 (关系 {relation})");
                }

                sb.AppendLine($"--- 效忠国家 ---");
                sb.AppendLine($"国家名称：[{kingdom.Name}]，文化：{kingdom.Culture.Name}。");
                // 显示排名/总数
                sb.AppendLine($"国家国力：{powerStatus} (综合战力排名：第 {rank} / {totalKingdomCount} 位)。");
                sb.AppendLine($"军事规模：现有正规军团 {kingdom.Armies.Count} 支，总战力指数 {kingdom.TotalStrength:F0}。");
                sb.AppendLine($"外交局势：{warStatus}");
                sb.AppendLine($"与君主关系：{rulerRel}");
            }
            return sb.ToString();
        }

        // 核心：推导当前的人物动机
        public string CalCurrentMotivation()
        {
            if (BaseHero == null && Occupation == "临时工")
            {
                LifeGoal = "完成当下的工作";
                ShortGoal = "听从雇主安排，不出差错。";
                return $"长期目标：{LifeGoal}\n短期目标：{ShortGoal}";
            }
            if (BaseHero == null) 
            {
                if (Occupation == "士兵")
                {
                    LifeGoal = "在军队中晋升或退役还乡。";
                    ShortGoal = "严格执行长官的命令，守卫此地，排除可疑人员。";                    
                }
                else if(Occupation == "村民")
                {
                    LifeGoal = "平平安安过日子。";
                    ShortGoal = "做完手头的活。";
                }
                else
                {
                    LifeGoal = "活着领到军饷/工钱";
                    ShortGoal = "执行当前的站岗或巡逻任务";
                }


                return $"长期目标：{LifeGoal}\n短期目标：{ShortGoal}";
            }

            // --- 1. 数据准备 & 特性提取 ---
            Clan clan = BaseHero.Clan;
            Kingdom kingdom = clan?.Kingdom;

            // 获取核心性格数值 (如果没有则默认为0)
            int honor = CoreValues.ContainsKey("Honor") ? CoreValues["Honor"] : 0;
            int valor = CoreValues.ContainsKey("Valor") ? CoreValues["Valor"] : 0;
            int calculating = CoreValues.ContainsKey("Calculating") ? CoreValues["Calculating"] : 0;
            int mercy = CoreValues.ContainsKey("Mercy") ? CoreValues["Mercy"] : 0;

            // 经济状况
            bool isPoor = (BaseHero.Gold < 10000);
            bool isRich = (BaseHero.Gold > 500000);

            // 战争状态
            bool atWar = kingdom != null && FactionManager.GetEnemyKingdoms(kingdom).Any();

            // 身份判断
            bool isKing = (kingdom != null && kingdom.Leader == BaseHero);
            bool isClanLeader = (clan != null && clan.Leader == BaseHero);
            bool isWanderer = BaseHero.IsWanderer;

            // --- 2. 计算人生目标 (LifeGoal) ---
            // 逻辑：身份决定上限，野心决定高度，主义(Ism)决定方向

            if (isKing)
            {
                if (Ism == IsmEnum.Ideal && Ambition > 50) LifeGoal = "统一大陆，建立万世不朽的理想国度";
                else if (Ambition > 80) LifeGoal = "征服一切，让所有国家臣服于我的脚下";
                else if (ActStyle == ActStyleEnum.Considerate) LifeGoal = "维持国内稳定，确保王朝平稳传承";
                else LifeGoal = "享受权力的巅峰，维持现状";
            }
            else if (isClanLeader)
            {
                // 野心极高且不重荣誉 -> 篡位/自立
                if (Ambition > 80 && honor < 0) LifeGoal = "积蓄力量，推翻现有的君主，自立为王";
                // 野心高但重荣誉 -> 权臣
                else if (Ambition > 60) LifeGoal = "带领家族成为王国中最有权势的豪门";
                // 现实主义者 -> 搞钱
                else if (Ism == IsmEnum.Realistic && Desire == DesireEnum.Greedy) LifeGoal = "垄断贸易，建立富可敌国的商业帝国";
                // 荣誉高 -> 忠臣
                else if (honor > 0) LifeGoal = "作为家族的守护者，尽忠职守，光耀门楣";
                else LifeGoal = "在这个乱世中保全家族，使其延续下去";
            }
            else if (isWanderer)
            {
                if (Ambition > 50) LifeGoal = "寻找明主或机会，摆脱流浪身份，晋升为贵族";
                else if (Desire == DesireEnum.Greedy) LifeGoal = "作为雇佣兵或强盗，攫取尽可能多的财富";
                else LifeGoal = "四海为家，寻找属于自己的归宿";
            }
            else // 普通家族成员/配偶
            {
                // 即使是配偶，如果野心大能力强，也不只是想安稳
                if (Ambition > 70 && calculating > 0) LifeGoal = "在幕后操纵家族政治，掌握实权";
                else if (valor > 1) LifeGoal = "在战场上证明自己，成为家族的利剑";
                else if (theImportanceOfFriendship == FriendshipImportanceEnum.Important) LifeGoal = "辅佐家主（或配偶），维系家族成员间的羁绊";
                else LifeGoal = "享受贵族生活，安稳度过一生";
            }

            // --- 3. 计算短期目标 (ShortGoal) ---
            // 逻辑：生理需求 > 紧迫危机(战争/破产) > 个人欲望

            List<string> shortGoals = new List<string>();

            // [优先级0]：特殊癖好 (酒鬼)
            if (AlcoholDesire == AlcoholDesireEnum.Alcoholic)
            {
                shortGoals.Add("非常渴求酒精，现在的首要念头是找个酒馆喝个烂醉");
            }

            // [优先级1]：战争状态
            if (atWar)
            {
                if (valor > 0 || Spirit == SpiritEnum.Brave)
                    shortGoals.Add("备战：渴望在当前的战争中击败敌将，赢取声望与战利品");
                else if (Spirit == SpiritEnum.Timid || valor < 0)
                    shortGoals.Add("避战：战火纷飞，只想躲在安全的城墙后，避免被俘虏");
                else if (honor < 0 && calculating > 0)
                    shortGoals.Add("投机：趁着战争混乱，通过掠夺村庄或发战争财来获利");
                else
                    shortGoals.Add("尽职：响应国家号召，保卫领土");
            }

            // [优先级2]：经济危机
            if (isPoor)
            {
                if (honor > 0) shortGoals.Add("筹款：家族财政赤字，需要通过正当贸易或任务来维持开支");
                else shortGoals.Add("搞钱：缺钱了，不论是抢劫商队还是敲诈勒索，必须尽快弄到第纳尔");
            }

            // [优先级3]：平稳时期的个人欲望
            if (shortGoals.Count == 0) // 如果没有紧急情况
            {
                // 根据物欲类型
                switch (DesireType)
                {
                    case DesireTypeEnum.Book:
                        shortGoals.Add("求知：希望能在这个城市找到珍稀的古籍或知识");
                        break;
                    case DesireTypeEnum.Weapon:
                        shortGoals.Add("整备：正在寻找一把趁手的神兵利器，或者改良现有的装备");
                        break;
                    case DesireTypeEnum.Nanman:
                    case DesireTypeEnum.Art:
                        shortGoals.Add("收藏：对异域的珍宝或艺术品非常感兴趣，想要将其收入囊中");
                        break;
                    case DesireTypeEnum.Money:
                        if (Desire == DesireEnum.Greedy) shortGoals.Add("敛财：虽然不缺钱，但看到金币增加是最快乐的事");
                        else shortGoals.Add("经营：管理好现有的产业和商队");
                        break;
                }

                // 根据社交/性格
                if (Spouse == "None" &&  BaseHero.Age < 40 && BaseHero.Age > 16)
                {
                    shortGoals.Add("联姻：正在物色合适的政治联姻对象");
                }
                else if (JobTendency == JobTendencyEnum.WarriorOnly)
                {
                    shortGoals.Add("磨炼：在竞技场打磨武艺，或者训练手下的士兵");
                }
                else if (ActStyle == ActStyleEnum.Flippancy)
                {
                    shortGoals.Add("享乐：最近只想举办宴会，吃喝玩乐");
                }
            }

            // 整合短期目标，取最重要的一条
            if (shortGoals.Count > 0)
            {
                ShortGoal = shortGoals[0]; // 取优先级最高的
            }
            else
            {
                ShortGoal = "待命：目前没有什么特别的打算，随遇而安。";
            }

            return $"长期目标：{LifeGoal}\n短期目标：{ShortGoal}";
        }

        public string GetSelfInfo()
        {


            StringBuilder sb = new StringBuilder();




            sb.AppendLine("## 人物核心设定");

            if (BaseHero == null && BaseCharacter == null)
            { 
                sb.AppendLine("无");
            }
            if (BaseHero == null && BaseCharacter != null)
            {
                sb.AppendLine($"-身份：{BaseCharacter.Name}\n" +
                    $"-等级：{BaseCharacter.Tier}\n职业1：{BaseCharacter.Occupation}\n" +
                    $"-职业2：{(BaseCharacter.IsSoldier ? "士兵" : "平民")}");
            }
            else
            {
                sb.AppendLine($"-姓名：[{BaseHero.Name}]。\n" +
                    $"-性别：{(BaseHero.IsFemale ? "女" : "男")}。" +
                    $"\n-年龄：{(int)BaseHero.Age}岁。");
                sb.AppendLine($"-配偶[{Spouse}]" +
                    $"\n-职业[{Occupation}]。");
                //sb.AppendLine($"性格特征：{PersonalityTraits}。" ); // 使用游戏自带的性格描述字符串
                /*
                if (CoreValues != null && CoreValues.Count > 0)
                {
                    sb.Append("核心价值观(Values)：");
                    foreach (var kv in CoreValues) sb.Append($"{kv.Key}({kv.Value}), ");
                    sb.AppendLine();
                }
                */

                sb.AppendLine("\n## 自我阶级认知##");
                sb.AppendLine(GetSelfWorthDescription());

                // --- 行为动机 (AI 的思考指引) ---
                sb.AppendLine("\n##目标与动机##");
                sb.AppendLine(CalCurrentMotivation());

                /*
                sb.AppendLine("\n##角色属性技能##");
                sb.AppendLine($"-仁慈：{BaseHero.GetTraitLevel(DefaultTraits.Mercy)}\n");
                sb.AppendLine($"-勇敢：{BaseHero.GetTraitLevel(DefaultTraits.Valor)}\n");
                sb.AppendLine($"-荣誉：{BaseHero.GetTraitLevel(DefaultTraits.Honor)}\n");
                sb.AppendLine($"-精明：{BaseHero.GetTraitLevel(DefaultTraits.Calculating)}\n");
                sb.AppendLine($"-魅力：{BaseHero.GetSkillValue(DefaultSkills.Charm)}\n");
                sb.AppendLine($"-智力（策略）：{BaseHero.GetSkillValue(DefaultSkills.Tactics)}\n");
                sb.AppendLine($"-流氓：{BaseHero.GetSkillValue(DefaultSkills.Roguery)}\n");
                sb.AppendLine($"-体力：{BaseHero.GetSkillValue(DefaultSkills.Athletics)}\n");
                sb.AppendLine($"-侦查：{BaseHero.GetSkillValue(DefaultSkills.Scouting)}\n");
                */

                //各种性格、喜好信息
                sb.AppendLine("\n##喜好信息##");
                sb.AppendLine($"-物欲：{DesireStr}\n" +
                    $"-喜好类型：{DesireTypeStr}。\n" +
                    $"对酒的态度: {AlcoholDesireStr}\n" +
                    $"偏好武器: {WeaponDesireStr}\n" +
                     $"工作偏好: {JobTendencyStr}\n");

                sb.AppendLine("##性格和价值观##");
                sb.AppendLine($"-野心程度：{Ambition}\n" +
                    $"-行事风格: {ActStyleStr}\n" +
                    $"-脾气: {TemperStr}\n" +
                     $"-精神: {SpiritStr}\n" +
                    $"-主义: {IsmStr}\n" +
                    $"-对情义的态度: {theImportanceOfFriendshipStr}\n");
            }
            // --- 当前状态 (Agent 层面) ---
            if (Mission.Current != null && Mission.Current.MainAgent != null)
            {

                var agent = Mission.Current.Agents.FirstOrDefault(a => a.Character == BaseCharacter);
                if (agent != null)
                {
                    float hpPercent = agent.Health / agent.HealthLimit * 100;
                    string healthDesc = hpPercent > 80 ? "精力充沛" : (hpPercent > 30 ? "受了些伤" : "身负重伤，濒临倒下");
                    sb.AppendLine($"##当前生理状态##");
                    sb.AppendLine($"健康状况：{healthDesc} (HP: {hpPercent:F0}%)。");
                }
            }
            return sb.ToString();

        }

        public string GetPersonaPrompt()
        {
            StringBuilder sb = new StringBuilder();

            
            sb.AppendLine(GetSelfInfo());
            // --- 势力背景 (调用上面的方法) ---
            sb.AppendLine(GetClanInfo());
            sb.AppendLine(GetKingdomInfo());

           


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
                    // 流浪者的身价主要取决于招募费用，通常很低，但我们可以根据装备加成
                    baseValue = 2000 + (BaseHero.Level * 500);
                }
                EstimatedValue = baseValue;
                return baseValue;
            }
        }
        public string GetSelfWorthDescription()
        {
            float val = CalculateEstimatedValue();
            string valDesc = "";

            if (val < 5000) valDesc = "微不足道。只要对方给点小恩小惠，或者表现出诚意，就愿意跟随对方。";
            else if (val < 50000) valDesc = "颇有身价。是有一定身份的人，一般的筹码打动不了，对方需要拿出真金白银。";
            else if (val < 200000) valDesc = "价值连城。作为名门望族的核心成员，身价极高。除非有巨大的利益交换（如城池、巨额财富），否则对方免谈。";
            else valDesc = "无价之宝/权倾天下。你想收买？这简直是天方夜谭，除非你能拿出半个王国的财富。";

            return $"出身源头：{OriginStr}\n" +
                $"身价：{val:F0} 两金子。" +
                $"\n自我认知：{valDesc}";
        }

        public List<Hero> GetCloseRelations(Hero hero, out string relationStr)
        {
            //获取配偶、家人、好友
            HashSet<Hero> relations = new HashSet<Hero>();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"获取 {hero.Name}的关系网：");
            // 返回 true 表示成功添加（也就是之前没加过）
           


            if (hero.Spouse != null)
            {
                relations.Add(hero.Spouse);
                sb.AppendLine($"-配偶：{hero.Spouse.Name} {((!hero.Spouse.IsAlive)? "已过世":"") }");
            }

            if(hero.Children.Count >0)
            {
                string children = "-子女：";
                sb.Append(children);
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
                sb.AppendLine($"-父亲：{hero.Father.Name}{((!hero.Father.IsAlive) ? "(已过世)" : "")}");
            }
            if (hero.Mother != null)
            {
                relations.Add(hero.Mother);
                rawSiblings.AddRange(hero.Mother.Children);
                sb.AppendLine($"-母亲：{hero.Mother.Name}{((!hero.Mother.IsAlive) ? "(已过世)" : "")}");
            }

            if (rawSiblings.Count > 0)
            {
                string brothers = "-兄弟：";
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

                string sisters = "-姐妹：";
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
                string uncles = "-叔伯姑舅姨：";
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
                string family = "-其他家族成员：";

                


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
                string friends = "-好友：";
                foreach (var other in hero.MapFaction.Heroes)
                {
                    if (other != hero && other.IsAlive && other.GetRelation(hero) > 20 && !relations.Contains(other))
                    {
                        relations.Add(other);
                        friends += $"{other.Name} ";
                    }
                }
                sb.AppendLine(friends);

                string enemy = "-政敌：";
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
            if (hero.Clan != null && hero.Clan.Leader == hero)
            {
                string leaders = "-族长圈子：";
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
            if (hero.Clan != null && hero.Clan.Kingdom.Leader == hero)
            {
                //foreach (var kingdom in hero.Clan.Kingdom.)
            }

            //DebugLogger.Log(sb.ToString());
            relationStr = sb.ToString();
            return relations.ToList();
        }
    }
}
