using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ExampleMod
{
    public class NPCInfoVM : ViewModel
    {
        private readonly System.Action _onClose;
        private readonly NPCProfile _profile; // 这里换成你真实的 NPCProfile 类型
        private readonly SingNpcMemorySystem _memory;
        public NPCInfoVM(SingNpcMemorySystem memory, System.Action onClose)
        {
            _memory = memory;
            _profile = _memory._profile;
            _onClose = onClose;

            // 默认选中第一个 Tab
            ExecuteSelectPersonal();
            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();
            TitleText = $"{_profile.Name}的信息面板";
            SelfCognitionText = _profile.GetSelfInfo();
            MotivationText = "";
            AgentStateText = "";
            ClanInfoText = _profile.GetClanInfo();

            KingdomInfoText = _profile.GetKingdomInfo();
            Hero _hero = _profile.BaseHero;

            MemoryInfoText = PromptBuilder.GetPrompt_History_Memory_Events(_memory);
            if (_hero == null)
            {

            }
            else
            {

                StringBuilder sbRel = new StringBuilder();
                sbRel.AppendLine($"配偶: {(_profile.Spouse)}");
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

                RelationInfoText = sbRel.ToString();


                _profile.GetCloseRelations(_hero, out string relationStr);
                RelationInfoText = relationStr + $"\n与玩家关系: {relationWithPlayer}";




                InventoryInfoText = AgentControlHelper.GetBagInfo(_hero);

                PartyInfoText = AgentControlHelper.GetPartyInfo(_hero);
            }
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