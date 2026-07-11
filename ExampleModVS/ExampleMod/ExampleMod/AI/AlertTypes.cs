using System;
using System.Collections.Generic;

namespace LivingWorldNpcs
{
    /// <summary>玩家行为类型（用于警戒值分类累加）</summary>
    public enum PlayerActionType
    {
        Crouching,      // 蹲下
        WeaponDrawn,    // 武器出鞘（和平区域）
        StealUIOpen,    // 偷窃界面打开未确认
        Steal,          // 偷窃（脉冲）
        AttackAlly,     // 攻击友方（脉冲）
        Knockout,       // 击晕（脉冲）
    }

    /// <summary>警戒阶段（UI 颜色 + NPC 行为分级）</summary>
    public enum AlarmPhase
    {
        Normal,      // 0.0 → 白底
        Suspicious,  // 0.25+ → 白→黄
        Cautious,    // 1.0+ → 黄→橙
        Alarmed,     // 2.0+ → 纯红
    }

    /// <summary>L3 质问时 NPC 的意图（决定要求什么、接受什么）</summary>
    public enum ConfrontationType
    {
        /// <summary>威慑 — NPC 看到可疑但非犯罪的行为（蹲下/拔刀）。要求：停止行为 + 给解释。</summary>
        Deter,
        /// <summary>搜查 — 玩家翻了半天包，NPC 怀疑偷了东西但没亲眼看见。要求：打开背包接受检查。</summary>
        Search,
        /// <summary>追回 — NPC 亲眼目击了偷窃（脉冲触发）。要求：归还物品 + 赔偿。</summary>
        Recover,
        /// <summary>制止 — NPC 目击了暴力行为（攻击/击晕）。要求：立刻住手 + 赔偿 + 离开。</summary>
        Stop,
    }

    /// <summary>L3 质问对话模式开关</summary>
    public enum AlertDialogueMode
    {
        /// <summary>StoryDialogVM（默认）— PrepareOpeningAction → ForceTalkAction → StoryDialogVM</summary>
        StoryVM,
        /// <summary>原版 ConversationManager — AlertForceConversationAction → CrimeDialogueBuilder → DialogueInjector</summary>
        VanillaConversation,
    }

    /// <summary>单条警戒条目：值 + 脉冲附加信息（供台词拼接）</summary>
    public struct AlertEntry
    {
        public float Value;
        public string TargetName;  // 脉冲事件附加：受害者名（持续累加时为空）
        public string ItemName;    // 脉冲事件附加：被盗物品名（持续累加时为空）
    }

    // ═══════════════════════════════════════════════════════════════
    // 🆕 PendingWorldEvent — Mission 作用域犯罪记录
    // ═══════════════════════════════════════════════════════════════

    /// <summary>单个目击者的证词：这位目击者看到了玩家哪些行为。仅 Alarmed 阶段才写入。</summary>
    [Serializable]
    public class WitnessTestimony
    {
        public string WitnessHeroId;   // null = 模板村民
        public string TemplateId;      // null = 有脸英雄
        public List<ActionRecord> Actions;
    }

    /// <summary>目击者看到的单条行为</summary>
    [Serializable]
    public class ActionRecord
    {
        public string ActionType;      // PlayerActionType 名称
        public float AlertValue;       // 目击者对此行为的警戒值
        public string TargetName;      // 受害者名（Knockout/AttackAlly）；Crouching/WeaponDrawn 为 null
        public string ItemId;          // Steal 赃物 ID
        public string ItemName;        // Steal 赃物显示名
    }
}
