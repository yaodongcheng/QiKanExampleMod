using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    public class NPCInfoVM : ViewModel
    {
        private readonly System.Action _onClose;
        private readonly NPCProfile _profile; // 这里换成你真实的 NPCProfile 类型
        private readonly SingNpcMemorySystem _memory;
        private readonly Agent _agent;
        private readonly Hero _hero;

        public NPCInfoVM(SingNpcMemorySystem memory, Agent agent, System.Action onClose)
        {
            _memory = memory;
            _agent = agent;
            _profile = memory?._profile;  // 模板 NPC 无 memory/profile，null 安全
            _hero = (agent?.Character as CharacterObject)?.HeroObject;
            _onClose = onClose;

            // 默认选中第一个 Tab
            ExecuteSelectPersonal();
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();

            // ── 标题（模板 NPC 用 Agent 名兜底）──
            string name = _profile?.Name ?? _agent?.Name?.ToString() ?? "未知";
            TitleText = $"{name}的信息面板";

            // ── 个人属性 ──
            SelfCognitionText = _profile?.GetSelfInfo() ?? "（非英雄单位，无详细人设）";
            MotivationText = "";
            AgentStateText = "";

            // ── 身上携带的金钱 ──
            int allocatedGold = StealManager.GetAgentGold(_agent);
            bool isClanLeader = _hero != null && _hero.Clan?.Leader == _hero;

            if (allocatedGold > 0 && isClanLeader)
                GoldInfoText = $"可偷窃现金: {allocatedGold} 第纳尔\n家族资金: {_hero.Gold} 第纳尔";
            else if (allocatedGold > 0)
                GoldInfoText = $"可偷窃现金: {allocatedGold} 第纳尔";
            else if (isClanLeader)
                GoldInfoText = $"家族资金: {_hero.Gold} 第纳尔";
            else if (_hero != null)
                GoldInfoText = $"个人资产: {_hero.Gold} 第纳尔";
            else
                GoldInfoText = "身上没有钱";

            // ── 家族信息 ──
            ClanInfoText = _profile?.GetClanInfo() ?? "（非英雄单位，无家族信息）";

            // ── 王国信息 ──
            KingdomInfoText = _profile?.GetKingdomInfo() ?? "（非英雄单位，无王国信息）";

            // ── 记忆 ──
            MemoryInfoText = _memory != null
                ? PromptBuilder.GetPrompt_History_Memory_Events(_memory)
                : "（非英雄单位，无记忆数据）";

            // ── 人际关系 ──
            if (_hero != null)
            {
                StringBuilder sbRel = new StringBuilder();
                sbRel.AppendLine($"配偶: {(_profile?.Spouse ?? "无")}");
                // 子女
                sbRel.Append("子女: ");
                if (_hero.Children != null && _hero.Children.Count > 0)
                {
                    foreach (var child in _hero.Children)
                    {
                        sbRel.Append($"{child.Name}, ");
                    }
                }
                else
                {
                    sbRel.Append("无");
                }
                sbRel.AppendLine("\n");

                int relationWithPlayer = _hero.GetRelation(Hero.MainHero);
                sbRel.AppendLine($"与玩家关系: {relationWithPlayer}");

                if (_profile != null)
                {
                    _profile.GetCloseRelations(_hero, out string relationStr);
                    RelationInfoText = relationStr + $"\n与玩家关系: {relationWithPlayer}";
                }
                else
                {
                    RelationInfoText = sbRel.ToString();
                }
            }
            else
            {
                RelationInfoText = "（非英雄单位，无人际关系数据）";
            }

            // ── 背包和部队 ──
            InventoryInfoText = _hero != null
                ? AgentControlHelper.GetBagInfo(_hero)
                : "（非英雄单位，无辎重信息）";
            PartyInfoText = _hero != null
                ? AgentControlHelper.GetPartyInfo(_hero)
                : "（非英雄单位，无部队信息）";
        }

        public void ExecuteClose()
        {
            _onClose?.Invoke();
        }

        // ================= Tab 切换逻辑 =================

        public void ExecuteSelectPersonal() => SetTab(1);
        public void ExecuteSelectClan() => SetTab(2);
        public void ExecuteSelectKingdom() => SetTab(3);
        public void ExecuteSelectRelation() => SetTab(4);
        public void ExecuteSelectMemory() => SetTab(5);
        public void ExecuteSelectInventory() => SetTab(6);
        public void ExecuteSelectParty() => SetTab(7);
        private void SetTab(int tabIndex)
        {
            IsPersonalSelected = tabIndex == 1;
            IsClanSelected = tabIndex == 2;
            IsKingdomSelected = tabIndex == 3;
            IsRelationSelected = tabIndex == 4;
            IsMemorySelected = tabIndex == 5;
            IsInventorySelected = tabIndex == 6;
            IsPartySelected = tabIndex == 7;
        }

        // ================= 属性定义 (Data Source Properties) =================
        [DataSourceProperty]
        public string TitleText { get; set; }



        // --- Tab 1: 个人属性 ---
        private string _selfCognitionText;
        [DataSourceProperty]
        public string SelfCognitionText
        {
            get => _selfCognitionText;
            set { if (value != _selfCognitionText) { _selfCognitionText = value; OnPropertyChangedWithValue(value, "SelfCognitionText"); } }
        }

        private string _motivationText;
        [DataSourceProperty]
        public string MotivationText
        {
            get => _motivationText;
            set { if (value != _motivationText) { _motivationText = value; OnPropertyChangedWithValue(value, "MotivationText"); } }
        }

        private string _agentStateText;
        [DataSourceProperty]
        public string AgentStateText
        {
            get => _agentStateText;
            set { if (value != _agentStateText) { _agentStateText = value; OnPropertyChangedWithValue(value, "AgentStateText"); } }
        }

        private string _goldInfoText;
        [DataSourceProperty]
        public string GoldInfoText
        {
            get => _goldInfoText;
            set { if (value != _goldInfoText) { _goldInfoText = value; OnPropertyChangedWithValue(value, "GoldInfoText"); } }
        }

        private string _clanInfoText;
        [DataSourceProperty]
        public string ClanInfoText
        {
            get => _clanInfoText;
            set { if (value != _clanInfoText) { _clanInfoText = value; OnPropertyChangedWithValue(value, "ClanInfoText"); } }
        }

        // --- Tab 3-6: 其他信息文本 ---
        private string _kingdomInfoText;
        [DataSourceProperty]
        public string KingdomInfoText
        {
            get => _kingdomInfoText;
            set { if (value != _kingdomInfoText) { _kingdomInfoText = value; OnPropertyChangedWithValue(value, "KingdomInfoText"); } }
        }

        private string _relationInfoText;
        [DataSourceProperty]
        public string RelationInfoText
        {
            get => _relationInfoText;
            set { if (value != _relationInfoText) { _relationInfoText = value; OnPropertyChangedWithValue(value, "RelationInfoText"); } }
        }

        private string _memoryInfoText;
        [DataSourceProperty]
        public string MemoryInfoText
        {
            get => _memoryInfoText;
            set { if (value != _memoryInfoText) { _memoryInfoText = value; OnPropertyChangedWithValue(value, "MemoryInfoText"); } }
        }

        private string _inventoryInfoText;
        [DataSourceProperty]
        public string InventoryInfoText
        {
            get => _inventoryInfoText;
            set { if (value != _inventoryInfoText) { _inventoryInfoText = value; OnPropertyChangedWithValue(value, "InventoryInfoText"); } }
        }

        private string _partyInfoText;
        [DataSourceProperty]
        public string PartyInfoText
        {
            get => _partyInfoText;
            set { if (value != _partyInfoText) { _partyInfoText = value; OnPropertyChangedWithValue(value, "PartyInfoText"); } }
        }

        // --- Tab 可见性控制 Bool ---

        private bool _isPersonalSelected;
        [DataSourceProperty]
        public bool IsPersonalSelected
        {
            get => _isPersonalSelected;
            set { if (value != _isPersonalSelected) { _isPersonalSelected = value; OnPropertyChangedWithValue(value, "IsPersonalSelected"); } }
        }

        private bool _isClanSelected;
        [DataSourceProperty]
        public bool IsClanSelected
        {
            get => _isClanSelected;
            set { if (value != _isClanSelected) { _isClanSelected = value; OnPropertyChangedWithValue(value, "IsClanSelected"); } }
        }

        private bool _isKingdomSelected;
        [DataSourceProperty]
        public bool IsKingdomSelected
        {
            get => _isKingdomSelected;
            set { if (value != _isKingdomSelected) { _isKingdomSelected = value; OnPropertyChangedWithValue(value, "IsKingdomSelected"); } }
        }

        private bool _isRelationSelected;
        [DataSourceProperty]
        public bool IsRelationSelected
        {
            get => _isRelationSelected;
            set { if (value != _isRelationSelected) { _isRelationSelected = value; OnPropertyChangedWithValue(value, "IsRelationSelected"); } }
        }

        private bool _isMemorySelected;
        [DataSourceProperty]
        public bool IsMemorySelected
        {
            get => _isMemorySelected;
            set { if (value != _isMemorySelected) { _isMemorySelected = value; OnPropertyChangedWithValue(value, "IsMemorySelected"); } }
        }

        private bool _isInventorySelected;
        [DataSourceProperty]
        public bool IsInventorySelected
        {
            get => _isInventorySelected;
            set { if (value != _isInventorySelected) { _isInventorySelected = value; OnPropertyChangedWithValue(value, "IsInventorySelected"); } }
        }

        private bool _isPartySelected;
        [DataSourceProperty]
        public bool IsPartySelected
        {
            get => _isPartySelected;
            set { if (value != _isPartySelected) { _isPartySelected = value; OnPropertyChangedWithValue(value, "IsPartySelected"); } }
        }
    }
}