using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TaleWorlds.Library; // Debug需要
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using System.Linq;

namespace ExampleMod
{
    public class DebugBehavior : CampaignBehaviorBase
    {
        // ================= 配置区域 =================
        // ★★★ 请务必修改为你电脑上的真实路径 (注意双斜杠 \\ ) ★★★
        private string NpcXmlPath = @"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\ExampleMod\ModuleData\lords.xml";
        private string HeroesXmlPath = @"H:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\ExampleMod\ModuleData\heroes.xml";
        // ===========================================

        private StreamWriter _writer;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Mod_Final_Report.txt");

            try
            {
                using (_writer = new StreamWriter(logPath, false))
                {
                    LogHeader("诊断开始", DateTime.Now.ToString());
                    _writer.WriteLine("说明：本程序通过手动读取XML，去游戏数据库中查找ID是否存在。");
                    _writer.WriteLine("--------------------------------------------------");

                    // 1. 检查 NPCCharacters.xml
                    if (File.Exists(NpcXmlPath))
                    {
                        CheckNPCCharacters();
                    }
                    else
                    {
                        LogError("文件不存在", NpcXmlPath);
                    }

                    _writer.WriteLine("\n\n");
                    _writer.WriteLine("----------检查英雄库。");
                    CheckAllHeroes();
                    _writer.WriteLine("----------结束检查英雄库。");
                    // 2. 检查 Heroes.xml
                    if (File.Exists(HeroesXmlPath))
                    {
                        CheckHeroes();
                    }
                    else
                    {
                        LogError("文件不存在", HeroesXmlPath);
                    }

                    LogHeader("诊断结束", "请查看上方错误信息，若无错误则显示空白。");
                }
            }
            catch
            {
                // 如果诊断程序本身崩了，至少留个言
                // 此时虽然写不进文件，但可以尝试弹窗或者忽略
            }
        }

        // =======================================================
        //                    NPC 检查逻辑
        // =======================================================
        private void CheckNPCCharacters()
        {
            LogHeader("正在检查文件", "NPCCharacters.xml");

            XmlDocument doc = new XmlDocument();
            try { doc.Load(NpcXmlPath); }
            catch (Exception ex) { LogError("XML格式错误", ex.Message); return; }

            // 获取所有 NPCCharacter 节点
            XmlNodeList npcNodes = doc.GetElementsByTagName("NPCCharacter");
            _writer.WriteLine($"找到 {npcNodes.Count} 个 NPC 定义。开始逐个核对依赖项...\n");

            foreach (XmlNode node in npcNodes)
            {
                string npcId = node.Attributes["id"]?.Value ?? "未知ID";
                _writer.WriteLine($"[检查 NPC] ID: {npcId}");

                // 1. 检查 Culture (文化)
                string cultureId = node.Attributes["culture"]?.Value;
                if (!string.IsNullOrEmpty(cultureId))
                {
                    string cleanId = cultureId.Replace("Culture.", "");
                    var obj = MBObjectManager.Instance.GetObject<CultureObject>(cleanId);
                    if (obj == null) LogError($"NPC [{npcId}]", $"找不到文化 ID: {cleanId} (原始值: {cultureId})");
                }

                // 2. 检查 Traits (特质)
                // XML 结构通常是 <face>... <traits> <trait id="xxx" value="1"/> </traits>
                XmlNode traitsNode = node.SelectSingleNode("traits");
                if (traitsNode != null)
                {
                    foreach (XmlNode trait in traitsNode.ChildNodes)
                    {
                        if (trait.Name != "trait") continue;
                        string traitId = trait.Attributes["id"]?.Value;
                        if (!string.IsNullOrEmpty(traitId))
                        {
                            var obj = MBObjectManager.Instance.GetObject<TraitObject>(traitId);
                            if (obj == null) LogError($"NPC [{npcId}]", $"找不到特质 ID: {traitId}");
                        }
                    }
                }

                // 3. 检查 Equipments (装备) - 最容易出错的地方
                // 检查 <equipmentSet> 下的 <equipment slot="xxx" id="yyy" />
                XmlNodeList equipSets = node.SelectNodes("equipmentSet");
                foreach (XmlNode set in equipSets)
                {
                    foreach (XmlNode eq in set.ChildNodes)
                    {
                        if (eq.Name != "equipment") continue;
                        string itemId = eq.Attributes["id"]?.Value;
                        if (!string.IsNullOrEmpty(itemId))
                        {
                            string cleanItem = itemId.Replace("Item.", "");
                            var obj = MBObjectManager.Instance.GetObject<ItemObject>(cleanItem);
                            if (obj == null) LogError($"NPC [{npcId}]", $"找不到装备 ID: {cleanItem}");
                        }
                    }
                }

                // 4. 检查 UpgradeTargets (升级树)
                XmlNodeList upgrades = node.SelectNodes("upgrade_targets/upgrade_target");
                foreach (XmlNode upg in upgrades)
                {
                    string targetId = upg.Attributes["id"]?.Value;
                    if (!string.IsNullOrEmpty(targetId))
                    {
                        string cleanTarget = targetId.Replace("NPCCharacter.", "");
                        // 注意：这里我们也是找 NPCCharacter
                        var obj = MBObjectManager.Instance.GetObject<CharacterObject>(cleanTarget);
                        if (obj == null) LogError($"NPC [{npcId}]", $"找不到升级目标 ID: {cleanTarget}");
                    }
                }
            }
        }

        // =======================================================
        //                    Hero 检查逻辑
        // =======================================================

        private void CheckAllHeroes()
        {
            if (!File.Exists(HeroesXmlPath)) return;

            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(HeroesXmlPath);
                XmlNodeList heroes = doc.GetElementsByTagName("Hero");

                _writer.WriteLine($"\n开始检查 Heroes.xml 中的 {heroes.Count} 个定义...");

                foreach (XmlNode hero in heroes)
                {
                    string heroId = hero.Attributes["id"]?.Value;
                    string factionId = hero.Attributes["faction"]?.Value;

                    if (string.IsNullOrEmpty(factionId)) continue;

                    // 这里改用更稳健的 Campaign.Current.Clans 查找方式
                    
                    var faction = Campaign.Current.Clans.FirstOrDefault(c => c.StringId == factionId);

                    if (faction == null)
                    {
                        _writer.WriteLine($"[ERROR] Hero [{heroId}] -> 找不到 Faction ID: {factionId}");
                        _writer.WriteLine($"        (代码尝试在 Campaign.Current.Clans 中查找失败)");
                    }
                    else
                    {
                        _writer.WriteLine($"[OK] Hero [{heroId}] -> 家族正常: {faction.Name}");
                    }
                }
            }
            catch { }
        }
        private void CheckHeroes()
        {
            LogHeader("正在检查文件", "Heroes.xml");

            XmlDocument doc = new XmlDocument();
            try { doc.Load(HeroesXmlPath); }
            catch (Exception ex) { LogError("XML格式错误", ex.Message); return; }

            XmlNodeList heroNodes = doc.GetElementsByTagName("Hero");
            _writer.WriteLine($"找到 {heroNodes.Count} 个 Hero 定义。开始逐个核对依赖项...\n");

            foreach (XmlNode node in heroNodes)
            {
                string heroId = node.Attributes["id"]?.Value ?? "未知ID";
                _writer.WriteLine($"[检查 Hero] ID: {heroId}");

                // 1. 检查 Faction (家族/派系)
                string factionId = node.Attributes["faction"]?.Value;
                if (!string.IsNullOrEmpty(factionId))
                {
                    string cleanFaction = factionId.Replace("Clan.", "").Replace("Kingdom.", "").Replace("Faction.", "");
                    // 先试着找 Clan
                    var clanObj = MBObjectManager.Instance.GetObject<Clan>(cleanFaction);
                    // 再试着找 Kingdom (有些特殊情况)
                    var kingdomObj = MBObjectManager.Instance.GetObject<Kingdom>(cleanFaction);

                    if (clanObj == null && kingdomObj == null)
                    {
                        LogError($"Hero [{heroId}]", $"找不到 Faction (家族/王国) ID: {cleanFaction}");
                    }
                }

                // 2. 检查 Character 模板 (这是最关键的！)
                // <Hero id="xxx" character="npc_template_id" />
                // 如果这个找不到，游戏直接崩溃或不加载
                string charTemplateId = node.Attributes["character"]?.Value; // XML里通常没写Character.前缀，但如果有也要处理
                if (!string.IsNullOrEmpty(charTemplateId))
                {
                    string cleanChar = charTemplateId.Replace("NPCCharacter.", "");
                    var charObj = MBObjectManager.Instance.GetObject<CharacterObject>(cleanChar);
                    if (charObj == null)
                    {
                        LogError($"Hero [{heroId}]", $"严重错误：找不到 Character 模板 ID: {cleanChar}。请检查 npccharacters.xml 是否加载成功，或者 ID 是否拼写一致。");
                    }
                }

                // 3. 检查 Spouse (配偶)
                string spouseId = node.Attributes["spouse"]?.Value;
                if (!string.IsNullOrEmpty(spouseId))
                {
                    // 检查配偶是否被定义在 Hero 列表里
                    // 注意：这里很难直接从 ObjectManager 拿，因为如果是新加的 Hero，可能还没注册进去
                    // 但如果是原版 Hero，应该能找到。这里我们只检查是否是原版存在的 Hero。
                    var spouseObj = MBObjectManager.Instance.GetObject<Hero>(spouseId);
                    // 如果是新加的mod hero，可能在加载阶段查不到，这里只做警告
                    if (spouseObj == null)
                    {
                        _writer.WriteLine($"    [警告] Hero [{heroId}] 的配偶 ID '{spouseId}' 在当前 ObjectManager 中未找到。(如果是同一个 Mod 新加的 Hero，可忽略此条)");
                    }
                }

                // 4. 检查 Mother / Father
                CheckParent(node, "mother", heroId);
                CheckParent(node, "father", heroId);
            }
        }

        private void CheckParent(XmlNode node, string attrName, string heroId)
        {
            string parentId = node.Attributes[attrName]?.Value;
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentObj = MBObjectManager.Instance.GetObject<Hero>(parentId);
                if (parentObj == null)
                {
                    _writer.WriteLine($"    [警告] Hero [{heroId}] 的 {attrName} ID '{parentId}' 未找到。(如果是同一个 Mod 新加的，可忽略)");
                }
            }
        }

        // ================= 辅助方法 =================
        private void LogHeader(string title, string content)
        {
            _writer.WriteLine("========================================");
            _writer.WriteLine($"{title}: {content}");
            _writer.WriteLine("========================================");
        }

        private void LogError(string scope, string message)
        {
            _writer.WriteLine($"!!! 错误发现 !!! [{scope}] -> {message}");
            _writer.Flush(); // 立即写入文件，防止崩了没保存
        }
    }
}