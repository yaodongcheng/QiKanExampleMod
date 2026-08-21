using System;
using System.Collections.Generic;
using System.Linq;
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

            // ── Tab 标签（本地化）──
            // 探查面板 Tab：个人属性
            TabPersonalLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_personal", "Personal");
            // 探查面板 Tab：家族信息
            TabClanLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_clan", "Clan");
            // 探查面板 Tab：势力信息
            TabKingdomLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_kingdom", "Kingdom");
            // 探查面板 Tab：人际关系
            TabRelationLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_relation", "Relations");
            // 探查面板 Tab：记忆传闻
            TabMemoryLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_memory", "Memories");
            // 探查面板 Tab：背包辎重
            TabInventoryLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_inventory", "Inventory");
            // 探查面板 Tab：部队信息
            TabPartyLabel = LWNTextHelper.ResolveText("LWN_ui_info_tab_party", "Party");
            // 探查面板按钮：关闭面板
            CloseButtonLabel = LWNTextHelper.ResolveText("LWN_ui_info_btn_close", "Close");

            RefreshValues();
        }

        public override void RefreshValues()
        {
            base.RefreshValues();

            // ── 标题 ──
            string name = _profile?.Name ?? _agent?.Name?.ToString() ?? LWNTextHelper.ResolveText("LWN_ui_info_name_unknown", "Unknown");
            // 本地化：LWN_ui_info_title（玩家可见文本）
            TitleText = LWNTextHelper.ResolveCompound("LWN_ui_info_title", "{NAME}'s Info Panel", ("NAME", name));

            // ── 个人属性 ──
            // 个人属性 Tab：模板 NPC 无详细人设的兜底文案
            SelfCognitionText = _profile?.GetSelfInfo() ?? LWNTextHelper.ResolveText("LWN_ui_info_no_persona", "(Non-hero unit: no detailed profile)");
            MotivationText = "";
            AgentStateText = "";

            // ── 身上携带的金钱 ──
            int allocatedGold = StealManager.GetAgentGold(_agent);
            bool isClanLeader = _hero != null && _hero.Clan?.Leader == _hero;

            // 金钱 Tab：可偷现金+家族资金两行（{STEAL}=可偷现金，{CLAN}=家族资金）
            if (allocatedGold > 0 && isClanLeader)
                // 可偷窃现金: {STEAL} 第纳尔 家族资金: {CLAN} 第纳尔
                GoldInfoText = LWNTextHelper.ResolveCompound("LWN_ui_info_gold_steal_and_clan", "Stealable cash: {STEAL} denars\nClan funds: {CLAN} denars",
                    ("STEAL", allocatedGold.ToString()), ("CLAN", _hero.Gold.ToString()));
            // 金钱 Tab：仅可偷现金（{STEAL}=可偷现金）
            else if (allocatedGold > 0)
                // 可偷窃现金: {STEAL} 第纳尔
                GoldInfoText = LWNTextHelper.ResolveCompound("LWN_ui_info_gold_stealable", "Stealable cash: {STEAL} denars",
                    ("STEAL", allocatedGold.ToString()));
            // 金钱 Tab：仅家族资金（{CLAN}=家族资金）
            else if (isClanLeader)
                // 家族资金: {CLAN} 第纳尔
                GoldInfoText = LWNTextHelper.ResolveCompound("LWN_ui_info_gold_clan", "Clan funds: {CLAN} denars",
                    ("CLAN", _hero.Gold.ToString()));
            // 金钱 Tab：个人资产（{GOLD}=个人资产）
            else if (_hero != null)
                // 个人资产: {GOLD} 第纳尔
                GoldInfoText = LWNTextHelper.ResolveCompound("LWN_ui_info_gold_personal", "Personal wealth: {GOLD} denars",
                    ("GOLD", _hero.Gold.ToString()));
            // 金钱 Tab：身上没钱
            else
                // 身上没有钱
                GoldInfoText = LWNTextHelper.ResolveText("LWN_ui_info_gold_none", "Carrying no money");

            // ── 家族信息 ──
            // 家族 Tab：模板 NPC 无家族信息的兜底文案
            ClanInfoText = _profile?.GetClanInfo() ?? LWNTextHelper.ResolveText("LWN_ui_info_no_clan", "(Non-hero unit: no clan info)");

            // ── 王国信息 ──
            // 王国 Tab：模板 NPC 无王国信息的兜底文案
            KingdomInfoText = _profile?.GetKingdomInfo() ?? LWNTextHelper.ResolveText("LWN_ui_info_no_kingdom", "(Non-hero unit: no kingdom info)");

            // ── 记忆 ──
            // 记忆 Tab（2026-08-21 改为调试全量视图）：对话历史/短期记忆/长期记忆/人设/大事记全量展开；
            // 模板 NPC 无记忆数据的兜底文案
            MemoryInfoText = _memory != null
                ? BuildMemoryDebugText(_memory)
                // （非英雄单位，无记忆数据）
                : LWNTextHelper.ResolveText("LWN_ui_info_no_memory", "(Non-hero unit: no memory data)");

            // ── 人际关系 ──
            if (_hero != null)
            {
                StringBuilder sbRel = new StringBuilder();
                // 人际关系 Tab：配偶行（{SPOUSE}=配偶名，无配偶显示"无"）
                string spouseName = _profile?.Spouse ?? LWNTextHelper.ResolveText("LWN_ui_info_none", "None");
                // 配偶: {SPOUSE}
                sbRel.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_info_spouse", "Spouse: {SPOUSE}", ("SPOUSE", spouseName)));
                // 子女
                // 人际关系 Tab：子女标签
                sbRel.Append(LWNTextHelper.ResolveText("LWN_ui_info_children_label", "Children: "));
                if (_hero.Children != null && _hero.Children.Count > 0)
                {
                    foreach (var child in _hero.Children)
                    {
                        sbRel.Append($"{child.Name}, ");
                    }
                }
                else
                {
                    // 人际关系 Tab：无子女
                    sbRel.Append(LWNTextHelper.ResolveText("LWN_ui_info_none", "None"));
                }
                sbRel.AppendLine("\n");

                int relationWithPlayer = _hero.GetRelation(Hero.MainHero);
                // 人际关系 Tab：与玩家关系行（{RELATION}=关系值）
                sbRel.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_info_relation_with_player", "Relation with player: {RELATION}",
                    ("RELATION", relationWithPlayer.ToString())));

                if (_profile != null)
                {
                    _profile.GetCloseRelations(_hero, out string relationStr);
                    // 人际关系 Tab：与玩家关系追加行（前置换行，{RELATION}=关系值）
                    RelationInfoText = relationStr + LWNTextHelper.ResolveCompound("LWN_ui_info_relation_with_player_suffix", "\nRelation with player: {RELATION}",
                        ("RELATION", relationWithPlayer.ToString()));
                }
                else
                {
                    RelationInfoText = sbRel.ToString();
                }
            }
            else
            {
                // 人际关系 Tab：模板 NPC 无人际关系数据的兜底文案
                RelationInfoText = LWNTextHelper.ResolveText("LWN_ui_info_no_relations", "(Non-hero unit: no relations data)");
            }

            // ── 背包和部队 ──
            // 背包 Tab：模板 NPC 无辎重信息的兜底文案
            InventoryInfoText = _hero != null
                ? AgentControlHelper.GetBagInfo(_hero)
                // （非英雄单位，无辎重信息）
                : LWNTextHelper.ResolveText("LWN_ui_info_no_inventory", "(Non-hero unit: no inventory info)");
            // 部队 Tab：模板 NPC 无部队信息的兜底文案
            PartyInfoText = _hero != null
                ? AgentControlHelper.GetPartyInfo(_hero)
                // （非英雄单位，无部队信息）
                : LWNTextHelper.ResolveText("LWN_ui_info_no_party", "(Non-hero unit: no party info)");
        }

        /// <summary>
        /// 记忆 Tab 调试全量视图（2026-08-21）：长期记忆/短期记忆/对话历史/人设/大事记/委托记录/新闻/传闻
        /// 全部展开，段头带「当前数/容量上限」，可直接核对读档钳制（plan D）与人设持久化（plan A 修复目标）。
        /// 只读快照（Snapshot* 走实例锁，LLM 后台线程写时安全）；段标题走 LWN 本地化，内容为动态数据豁免。
        /// 调试视图绝不抛异常——构建失败降级为兜底文案 + 日志，保证探查面板永远可开。
        /// </summary>
        private static string BuildMemoryDebugText(SingNpcMemorySystem memory)
        {
            try
            {
                var sb = new StringBuilder();
                string heroId = memory._profile?.StringId ?? "";
                if (!string.IsNullOrEmpty(heroId))
                    sb.AppendLine("ID: " + heroId);   // 数据行：跨存档定位（与 save_inspect dump 对照）

                // 1. 长期记忆（远期记忆，含 字符数/上限）
                string perm = memory.SnapshotPermanentMemory();
                // 本地化：LWN_ui_info_mem_perm（记忆段：长期记忆标题）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_perm", "Long-term Memory") + $" ({perm.Length}/{memory.MaxPermanentLength})");
                sb.AppendLine(string.IsNullOrEmpty(perm) ? LWNTextHelper.ResolveText("LWN_ui_info_none", "None") : perm);
                sb.AppendLine();

                // 2. 短期记忆（动态记忆，含 条数/上限；行首带相对日前缀，I5 词表与 prompt 一致）
                var dynamics = memory.SnapshotDynamicMemories();
                // 本地化：LWN_ui_info_mem_dynamic（记忆段：短期记忆标题）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_dynamic", "Short-term Memories") + $" ({dynamics.Count}/{memory.MaxDynamicMemoryCount})");
                foreach (var d in dynamics)
                {
                    if (string.IsNullOrEmpty(d.Content)) continue;
                    sb.AppendLine($"- #{d.SeqId} " + PromptBuilder.RelativeDayPrefix(d.CampaignDay) + d.Content);
                }
                sb.AppendLine();

                // 3. 对话历史（含 条数/上限；行 = 相对日 + Role + 内容）
                var history = memory.SnapshotRecentHistory();
                // 本地化：LWN_ui_info_mem_history（记忆段：对话历史标题）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_history", "Dialogue History") + $" ({history.Count}/{memory.MaxRecentHistoryCount})");
                foreach (var msg in history)
                {
                    if (string.IsNullOrEmpty(msg.Content)) continue;
                    string stamp = PromptBuilder.RelativeDayPrefix(msg.CampaignDay);
                    string role = string.IsNullOrEmpty(msg.Role) ? "" : msg.Role + ": ";
                    sb.AppendLine($"- #{msg.SeqId} " + stamp + role + msg.Content);
                }
                sb.AppendLine();

                // 4. 人设三字段（常驻人设 = 存档关键字段，调试存档修复（plan A）是否生效用）
                // 本地化：LWN_ui_info_mem_persona（记忆段：人设标题）
                sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_persona", "Persona"));
                // 本地化：LWN_ui_info_persona_line（人设行 {LABEL}: {VALUE}，双桶）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_info_persona_line", "{LABEL}: {VALUE}",
                    ("LABEL", LWNTextHelper.ResolveText("LWN_ui_info_persona_bg", "Background")),
                    ("VALUE", string.IsNullOrEmpty(memory.BackgroundStory) ? LWNTextHelper.ResolveText("LWN_ui_info_none", "None") : memory.BackgroundStory)));
                // 本地化：LWN_ui_info_persona_line（人设行）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_info_persona_line", "{LABEL}: {VALUE}",
                    ("LABEL", LWNTextHelper.ResolveText("LWN_ui_info_persona_personality", "Personality")),
                    ("VALUE", string.IsNullOrEmpty(memory.Personality) ? LWNTextHelper.ResolveText("LWN_ui_info_none", "None") : memory.Personality)));
                // 本地化：LWN_ui_info_persona_line（人设行）
                sb.AppendLine(LWNTextHelper.ResolveCompound("LWN_ui_info_persona_line", "{LABEL}: {VALUE}",
                    ("LABEL", LWNTextHelper.ResolveText("LWN_ui_info_persona_specialty", "Specialty")),
                    ("VALUE", string.IsNullOrEmpty(memory.Specialty) ? LWNTextHelper.ResolveText("LWN_ui_info_none", "None") : memory.Specialty)));
                sb.AppendLine();

                // 5. 大事记（方案 N，≤12 条）
                var important = memory.SnapshotImportantEvents();
                if (important.Count > 0)
                {
                    // 本地化：LWN_ui_info_mem_important（记忆段：大事记标题）
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_important", "Milestones") + $" ({important.Count})");
                    foreach (var evt in important)
                    {
                        if (!string.IsNullOrEmpty(evt)) sb.AppendLine("- " + evt);
                    }
                    sb.AppendLine();
                }

                // 6. 委托记录（结构化历史）
                var quests = memory.SnapshotQuestHistory();
                if (quests.Count > 0)
                {
                    // 本地化：LWN_ui_info_mem_quests（记忆段：委托记录标题）
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_quests", "Commission Records") + $" ({quests.Count})");
                    for (int i = quests.Count - 1; i >= 0; i--)
                        sb.AppendLine("- " + quests[i].GetDisplaySummary());
                    sb.AppendLine();
                }

                // 7. 重大新闻（外部注入）
                if (!string.IsNullOrEmpty(memory.GlobalNews))
                {
                    // 本地化：LWN_ui_info_mem_news（记忆段：重大新闻标题）
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_news", "Global News"));
                    sb.AppendLine(memory.GlobalNews);
                    sb.AppendLine();
                }

                // 8. 事件传闻（调试视图：EventId + 感知严重度 + 描述，不按玩家相关性裁剪）
                var known = memory.KnownEvents?.ToList();
                if (known != null && known.Count > 0)
                {
                    // 本地化：LWN_ui_info_mem_rumors（记忆段：相关传闻标题）
                    sb.AppendLine(LWNTextHelper.ResolveText("LWN_ui_info_mem_rumors", "Rumors") + $" ({known.Count})");
                    foreach (var evt in known.OrderByDescending(e => e.PerceivedSeverity))
                    {
                        string desc = "";
                        try
                        {
                            var se = NewsSpreadSystem.Instance?.GetEventById(evt.EventId);
                            if (se != null) desc = se.Description;
                        }
                        catch { }
                        sb.AppendLine($"- [{evt.PerceivedSeverity:0.#}] {evt.EventId} {desc}");
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NPCInfo] 记忆调试视图构建失败: {ex.Message}");
                return LWNTextHelper.ResolveText("LWN_ui_info_no_memory", "(Non-hero unit: no memory data)");
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

        // ── Tab 标签属性 ──
        [DataSourceProperty]
        public string TabPersonalLabel { get; set; }
        [DataSourceProperty]
        public string TabClanLabel { get; set; }
        [DataSourceProperty]
        public string TabKingdomLabel { get; set; }
        [DataSourceProperty]
        public string TabRelationLabel { get; set; }
        [DataSourceProperty]
        public string TabMemoryLabel { get; set; }
        [DataSourceProperty]
        public string TabInventoryLabel { get; set; }
        [DataSourceProperty]
        public string TabPartyLabel { get; set; }
        [DataSourceProperty]
        public string CloseButtonLabel { get; set; }



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