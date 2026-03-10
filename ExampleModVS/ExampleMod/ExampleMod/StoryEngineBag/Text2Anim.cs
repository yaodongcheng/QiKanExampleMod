using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace ExampleMod.StoryEngineBag
{
    // 定义情感类别的配置结构
    public class EmotionCategory
    {
        public string Id { get; set; }
        public int Weight { get; set; }
        public List<string> Keywords { get; set; }
        public string Description { get; set; }

        public EmotionCategory(string id, int weight, List<string> keywords, string description)
        {
            Id = id;
            Weight = weight;
            Keywords = keywords;
            Description = description;
        }
    }

    // 匹配结果结构
    public class MatchResult
    {
        public string OriginalDialogue { get; set; }
        public string CleanedDialogue { get; set; }
        public string ActionId { get; set; } // 对应Bannerlord的动作ID
        public string EmotionType { get; set; }
        public string MatchedKeyword { get; set; }
    }

    public class DialogueActionMatcher
    {
        private List<EmotionCategory> _sortedCategories;
        private Dictionary<string, List<string>> _actionMap;
        private Random _random;
        public DialogueActionMatcher()
        {
            _random = new Random();
            InitializeConfig();
        }

        private void InitializeConfig()
        {

            _sortedCategories = new List<EmotionCategory>();
            _actionMap = new Dictionary<string, List<string>>();

            // --- 修改开始：从 GameDatabase 读取数据 ---

            // 确保 GameDatabase 已经初始化（在 Mod 启动时应调用过 GameDatabase.Initialize()）
            // 如果 Emotion 表为空，说明加载顺序有问题或文件没读到
            if (GameDatabase.Emotion == null)
            {
                // 防止崩溃的兜底数据（可选）
                return;
            }

            foreach (var record in GameDatabase.Emotion.GetAll())
            {
                string id = record.GetString("ID");
                int weight = record.GetInt("Weight");
                string desc = record.GetString("ScriptName"); // 对应中文描述

                // 1. 处理关键词 (CSV中是 string 类型，分号分隔)
                string rawKeywords = record.GetString("Keywords");
                List<string> keywordList = new List<string>();
                if (!string.IsNullOrWhiteSpace(rawKeywords))
                {
                    // 使用分号分割，并去除空项
                    keywordList = rawKeywords.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                // 2. 处理动作库 (CSV中是 string 类型，分号分隔)
                string rawAnims = record.GetString("Animations");
                List<string> animList = new List<string>();
                if (!string.IsNullOrWhiteSpace(rawAnims))
                {
                    animList = rawAnims.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }

                // 3. 构建配置对象
                // 只有当有关键词时才加入分类判断（normal这种没有关键词的作为默认值，不参与关键词匹配循环）
                if (keywordList.Count > 0)
                {
                    _sortedCategories.Add(new EmotionCategory(id, weight, keywordList, desc));
                }

                // 4. 构建动作映射表
                if (animList.Count > 0)
                {
                    if (!_actionMap.ContainsKey(id))
                    {
                        _actionMap.Add(id, animList);
                    }
                }
            }

            // --- 修改结束 ---

            // 预先按权重降序排序
            _sortedCategories = _sortedCategories.OrderByDescending(c => c.Weight).ToList();
           

        }
        public  string GetAnimByEmotion(string emotion)
        {
            if (string.IsNullOrEmpty(emotion)) return "";
            string actionId = "";
            if (_actionMap.TryGetValue(emotion, out List<string> actions) && actions.Count > 0)
            {
                return actions[_random.Next(actions.Count)];
            }
            else
            {
                if (_actionMap.TryGetValue("normal", out List<string> normalActions) && normalActions.Count > 0)
                {
                    actionId = normalActions[_random.Next(normalActions.Count)];
                }
                else
                {
                    // 终极兜底：如果连 normal 都没有，给个空或者硬编码的默认值
                    actionId = "act_conversation_normal_start";
                }
            }
            return actionId;
        }
        /// <summary>
        /// 清理台词，移除格式噪声
        /// </summary>
        private string CleanDialogue(string dialogue)
        {
            if (string.IsNullOrEmpty(dialogue)) return "";

            // 对应 Python: re.sub(r'對話:$[^)]+$$$\[', '', dialogue)
            // 假设原始格式类似于 Bannerlord 的本地化字符串键值
            string cleaned = Regex.Replace(dialogue, @"對話:\$[^)]+\$\$\$\[", "");

            cleaned = cleaned.Replace("]]", "");
            cleaned = cleaned.Replace("\\n", " ");
            cleaned = cleaned.Replace("\\r", " ");
            cleaned = ToSimplifiedChinese(cleaned);
            return cleaned.Trim();
        }
        public string ToSimplifiedChinese(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // 使用 VisualBasic 的 StrConv 进行转换
            // VbStrConv.SimplifiedChinese 对应枚举值 256 (0x100)
            
            return Strings.StrConv(input, VbStrConv.SimplifiedChinese, 0);
        }
        /// <summary>
        /// 核心匹配逻辑
        /// </summary>
        /// 
        public string GetEmotionByDialogue(string dialogue)
        {
            string cleaned = CleanDialogue(dialogue);
            string finalEmotion = "normal"; // 默认为 normal
            if (string.IsNullOrEmpty(dialogue))
                return finalEmotion;
            else
            {
                foreach (var category in _sortedCategories)
                {
                    foreach (var kw in category.Keywords)
                    {
                        if (cleaned.Contains(kw))
                        {
                            // 基础匹配
                            finalEmotion = category.Id;
                            return finalEmotion;
                        }
                    }

                }
            }
            return finalEmotion;
        }
            public MatchResult Match(string dialogue)
            {
                string cleaned = CleanDialogue(dialogue);
                string finalEmotion = "normal"; // 默认为 normal
                string matchedKeyword = "";

                bool matchFound = false;

                // 遍历排序后的类别
                foreach (var category in _sortedCategories)
                {
                    foreach (var kw in category.Keywords)
                    {
                        if (cleaned.Contains(kw))
                        {
                            // 基础匹配
                            finalEmotion = category.Id;
                            matchedKeyword = kw;
                            matchFound = true;
                            goto FoundMatch;
                        }
                    }
                }

            FoundMatch:

                // 获取动作ID
                string actionId = "";

                // 尝试获取对应的情感动作列表
                if (_actionMap.TryGetValue(finalEmotion, out List<string> actions) && actions.Count > 0)
                {
                    // 修改：从列表中随机选取一个动作
                    int index = _random.Next(actions.Count);
                    actionId = actions[index];
                }
                else
                {
                    // 兜底：如果没有找到对应的动作，尝试找 "normal"
                    if (_actionMap.TryGetValue("normal", out List<string> normalActions) && normalActions.Count > 0)
                    {
                        actionId = normalActions[_random.Next(normalActions.Count)];
                    }
                    else
                    {
                        // 终极兜底：如果连 normal 都没有，给个空或者硬编码的默认值
                        actionId = "act_conversation_normal_start";
                    }
                }
                //打印匹配到的关键字、动作ID和情感类型，使用Infomation
                InformationManager.DisplayMessage(new InformationMessage($"匹配到的关键字：{matchedKeyword}，动作ID：{actionId}，情感类型：{finalEmotion}"));

                return new MatchResult
                {
                    OriginalDialogue = dialogue,
                    CleanedDialogue = cleaned,
                    ActionId = actionId,
                    EmotionType = matchFound ? finalEmotion : "normal",
                    MatchedKeyword = matchedKeyword
                };
            }
    }
}
