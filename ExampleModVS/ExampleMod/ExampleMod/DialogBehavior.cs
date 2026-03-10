using System.Reflection;
using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Localization;

namespace ExampleMod
{
    public class DialogBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {            
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }
        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine("dialog_demo_start_conversation","hero_main_options","dialog_demo_choice","我有一些问题要问你",null,null);
            starter.AddDialogLine("dialog_demo_choice_intro", "dialog_demo_choice", "dialog_demo_choice_output", "当然，我可以回答你", null, null);

        }
        public override void SyncData(IDataStore dataStore)
        {
            
        }



        // 反射拿 internal 构造函数，缓存一次
        private static readonly ConstructorInfo ConversationSentenceCtor =
            typeof(ConversationSentence).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new Type[]
                {
                typeof(string),                                  // idString
                typeof(TextObject),                              // text
                typeof(string),                                  // inputToken
                typeof(string),                                  // outputToken
                typeof(ConversationSentence.OnConditionDelegate),
                typeof(ConversationSentence.OnClickableConditionDelegate),
                typeof(ConversationSentence.OnConsequenceDelegate),
                typeof(uint),                                    // flags
                typeof(int),                                     // priority
                typeof(int),                                     // agentIndex
                typeof(int),                                     // nextAgentIndex
                typeof(object),                                  // relatedObject
                typeof(bool),                                    // withVariation
                typeof(ConversationSentence.OnMultipleConversationConsequenceDelegate),
                typeof(ConversationSentence.OnMultipleConversationConsequenceDelegate),
                typeof(ConversationSentence.OnPersuasionOptionDelegate)
                },
                modifiers: null
            );

        // 通用创建函数
        private static ConversationSentence CreateConversationSentence(
            string id,
            string inputToken,
            string outputToken,
            string text,
            ConversationSentence.OnConditionDelegate conditionDelegate,
            ConversationSentence.OnClickableConditionDelegate clickableConditionDelegate,
            ConversationSentence.OnConsequenceDelegate consequenceDelegate,
            uint flags,
            int priority,
            ConversationSentence.OnPersuasionOptionDelegate persuasionOptionDelegate
        )
        {
            if (ConversationSentenceCtor == null)
                throw new InvalidOperationException("ConversationSentence ctor not found (internal signature changed?).");

            return (ConversationSentence)ConversationSentenceCtor.Invoke(new object[]
            {
            id,
            new TextObject(text),
            inputToken,
            outputToken,
            conditionDelegate,
            clickableConditionDelegate,
            consequenceDelegate,
            flags,
            priority,
            0,                  // agentIndex
            0,                  // nextAgentIndex
            null,               // relatedObject
            false,              // withVariation
            null,               // speakerDelegate
            null,               // listenerDelegate
            persuasionOptionDelegate
            });
        }

        // 邪道版 AddDialogLine（非玩家台词）
        public static ConversationSentence AddDialogLine(
            string id,
            string inputToken,
            string outputToken,
            string text,
            ConversationSentence.OnConditionDelegate conditionDelegate,
            ConversationSentence.OnConsequenceDelegate consequenceDelegate,
            int priority = 100,
            ConversationSentence.OnClickableConditionDelegate clickableConditionDelegate = null
        )
        {
            // flags: 0U（普通对话）
            var sentence = CreateConversationSentence(
                id,
                inputToken,
                outputToken,
                text,
                conditionDelegate,
                clickableConditionDelegate,
                consequenceDelegate,
                0u,
                priority,
                null
            );

            // 这里按你原本逻辑返回 / 注册，你如果有自己的容器可以在这里加
            return sentence;
        }

        // 邪道版 AddPlayerLine（玩家台词）
        public static ConversationSentence AddPlayerLine(
            string id,
            string inputToken,
            string outputToken,
            string text,
            ConversationSentence.OnConditionDelegate conditionDelegate,
            ConversationSentence.OnConsequenceDelegate consequenceDelegate,
            int priority = 100,
            ConversationSentence.OnClickableConditionDelegate clickableConditionDelegate = null,
            ConversationSentence.OnPersuasionOptionDelegate persuasionOptionDelegate = null
        )
        {
            // flags: 1U（玩家选项；你原来就是这么传的）
            var sentence = CreateConversationSentence(
                id,
                inputToken,
                outputToken,
                text,
                conditionDelegate,
                clickableConditionDelegate,
                consequenceDelegate,
                1u,
                priority,
                persuasionOptionDelegate
            );

            return sentence;
        }
    }
}