using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.SaveSystem;

namespace ExampleMod.StoryEngineBag
{
    public enum StoryGenStatus
    {
        Idle,       // 空闲
        Processing, // 生成中 (LLM正在思考)
        Ready,      // 生成完成，等待播放
        Error       // 出错了
    }

    public class GeneratedStoryResult
    {
        public List<ScriptNode> ScriptNodes { get; set; } // 解析好的引擎脚本

        [SaveableProperty(1)]
        public string RawJson { get; set; } // 原始JSON备份
        public string EventSummary { get; set; } // 也就是 directorBook，剧情梗概
        public ScreenPlayOutline Outline { get; set; } // 原始的大纲上下文
    }


    public class AIStoryGeneratorBehavior : CampaignBehaviorBase
    {
        // --- 单例访问 ---
        public static AIStoryGeneratorBehavior Instance { get; private set; }

        // --- 全局状态 ---
        public StoryGenStatus Status { get; private set; } = StoryGenStatus.Idle;
        private GeneratedStoryResult CurrentResult;

        // 简单的锁对象，防止并发读写冲突
        private readonly object _lock = new object();
        public override void RegisterEvents()
        {
        }
        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("AIStory_Result", ref CurrentResult);
        }

        public AIStoryGeneratorBehavior()
        {
            Instance = this; // 当游戏创建这个 Behavior 时，自动赋值给单例
        }

        public GeneratedStoryResult GetCurrentResult()
        {
            if (CurrentResult == null) return null;
            if (CurrentResult.ScriptNodes == null && !string.IsNullOrEmpty(CurrentResult.RawJson))
            {
                try
                {
                    AIScriptRoot root;
                    AIStoryAdapt.ParseJson_AIStory(CurrentResult.RawJson, out root);
                    CurrentResult.ScriptNodes = AIStoryAdapt.ConvertToEngineScript(root.Script);
                }
                catch
                {
                    // 解析失败处理
                    return null;
                }
            }

            return CurrentResult;

            
        }
        
        public void StartGeneration(SocialEvent evt, SpreadReport report)
        {
            if (Status == StoryGenStatus.Processing)
            {
                InformationManager.DisplayMessage(new InformationMessage("剧本正在生成中，请稍后..."));
                return;
            }

            // 重置状态
            lock (_lock)
            {
                Status = StoryGenStatus.Processing;
                CurrentResult = null;
            }

            // 启动后台任务 (Fire and Forget)
            _ = GenerateTaskAsync(evt, report);
        }
        private async Task GenerateTaskAsync(SocialEvent evt, SpreadReport report)
        {
            try
            {
                // 1. 准备大纲
                ScreenPlayOutline outline = new ScreenPlayOutline(evt, report);
                if (outline == null) throw new Exception("Outline creation failed");

                // 2. 生成梗概 (Director)
                string directorPrompt = PromptBuilder.BuildDirectorPrompt(outline);
                string directorBook = await LLMService.Instance.ChatAsync(directorPrompt, 500, false);

                // 3. 生成详细脚本 (Show)
                string showPrompt = PromptBuilder.BuildShowPrompt(outline, directorBook);
                string showBookJson = await LLMService.Instance.ChatAsync(showPrompt, 1600, false);

                // 4. 解析 JSON (利用你提供的解析代码)
                AIScriptRoot root;
                AIStoryAdapt.ParseJson_AIStory(showBookJson, out root);

                if (root == null || root.Script == null)
                    throw new Exception("JSON Parse Failed");

                // 5. 转换为引擎脚本节点
                var engineNodes = AIStoryAdapt.ConvertToEngineScript(root.Script);

                // 6. 存入结果
                lock (_lock)
                {
                    CurrentResult = new GeneratedStoryResult
                    {
                        Outline = outline,
                        EventSummary = directorBook,
                        RawJson = showBookJson,
                        ScriptNodes = engineNodes
                    };
                    Status = StoryGenStatus.Ready;
                }

                InformationManager.DisplayMessage(new InformationMessage("新的传闻剧本已生成！", Colors.Green));
                
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    Status = StoryGenStatus.Error;
                    // 记录错误日志
                    DebugLogger.Log($"Story Generation Error: {ex.Message}");
                }
                
            }
        }
    }
}
