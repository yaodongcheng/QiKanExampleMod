using System;

namespace LivingWorldNpcs
{
    // ═══════════════════════════════════════════════════════════════
    // GoalTemplates.cs — 意图分类 → 目标状态表（§2.3）
    //
    // CommandIntentType = 计划层键（LLM 分类输出 → GoalTemplate 查表）。
    // 与 NpcIntentType 是"包含关系，不是映射"：执行期作为
    // NpcIntent.ExecutingCommand 的 detail 被持有（§10）。
    //
    // GoalTemplate.Success 是权威；plan 里的 goal 是可选具体化
    // （可省略，缺省回落模板）。目标状态必须执行器可验证。
    // ═══════════════════════════════════════════════════════════════

    /// <summary>命令层意图枚举（§2.1 意图全表 29 类别）。LLM JSON 层的 intent_type 是 string，
    /// C# 层 Parse 成此枚举，未知值 → validator 拒收/降级 CUSTOM（封闭词表纪律）。</summary>
    public enum CommandIntentType
    {
        None,
        Follow,        // 跟我走（v0 已有 order_follow）
        Wait,          // 在这等我（StayAction 已有）
        Stop,          // 住手/别打了（ClearAllActions 已有）
        Attack,        // 干掉他（order_attack 已有）
        Guard,         // 护住他/条件参战（护主逻辑已有）
        Bring,         // 请村长到我面前（v1）
        Distract,      // 引开那守卫（v1）
        Lookout,       // 帮我望风（v1）
        Deliver,       // 告诉他我在老地方见（v1）
        Engage,        // 缠住掌柜/拖住他（v1.5）
        DriveAway,     // 把那醉鬼赶走（v2）
        Steal,         // 去偷那箱子/他的钱袋（v1.5）
        Formation,     // 站我身后/你们排成一列（v1.5）
        Spar,          // 和我切磋一下（v1.5）
        Fetch,         // 去把我的剑拿来（v2）
        Purchase,      // 去买两桶酒（v2）
        Knockout,      // 打晕他（v2）
        Guide,         // 带我去河边（v2）
        Scout,         // 去那边看看有什么（v2）
        TalkTo,        // 去和掌柜谈酒钱（v2）
        Find,          // 找到卖药的郎中（v2）
        Shadow,        // 悄悄跟着那黑衣人（v2）
        Collect,       // 去张员外家讨回那笔债（v2）
        Duel,          // 去和那剑客切磋（v2）
        Annihilate,    // 把全村人都杀了（v2）
        Commotion,     // 闹出点动静（v3）
        Interact,      // 把门打开/把灯吹灭（v3 待验证）
        Discreet,      // 低调点/别惹事（v2 行为参数）
        Custom,        // 词表外 → 诚实拒绝
    }

    /// <summary>意图 → 目标状态模板（C# 薄层）。Success 是权威 GOAL；保持型意图有 Maintain。</summary>
    public class GoalTemplate
    {
        public CommandIntentType IntentType;
        public string DisplayName;               // 本地化显示名（LWN_ui_commandintent_*）
        public Func<WorldState, bool> Success;   // GOAL：成立 = 意图达成（一次性成功）；保持型 = 达成锚点
        public Func<WorldState, bool> Maintain;  // MAINTAIN（保持型）：达成之后保持；翻转 = 掉线预案
        public bool IsKeepType;                  // 保持型意图（ENGAGE/COMMOTION/LOOKOUT/GUARD）：达成后进入保持期
        public bool IsEventDriven;               // 事件驱动（LOOKOUT/SHADOW）：Success 为 null，R6 豁免总时长
        public bool IsCombatIntent;              // 战斗意图（ATTACK/ANNIHILATE/批量 KNOCKOUT）：R2 目标死亡豁免 + R5 自动豁免
    }

    /// <summary>意图静态属性表（GuardrailEngine/执行器判定用；§2.3 GoalTemplate 的轻量静态侧）。</summary>
    public static class GoalTemplates
    {
        /// <summary>战斗意图：combat 是正常进展（R2 目标死亡豁免 + R5 自动豁免）。</summary>
        public static bool IsCombatIntent(CommandIntentType t)
        {
            return t == CommandIntentType.Attack
                || t == CommandIntentType.Annihilate
                || t == CommandIntentType.Knockout;
        }

        /// <summary>事件驱动意图：无限期待命（LOOKOUT/SHADOW），R6 总时长豁免。</summary>
        public static bool IsEventDriven(CommandIntentType t)
        {
            return t == CommandIntentType.Lookout
                || t == CommandIntentType.Shadow;
        }

        /// <summary>保持型意图：达成后进入保持期，掉线走预案（ENGAGE/COMMOTION/LOOKOUT/GUARD）。</summary>
        public static bool IsKeepType(CommandIntentType t)
        {
            return t == CommandIntentType.Engage
                || t == CommandIntentType.Commotion
                || t == CommandIntentType.Lookout
                || t == CommandIntentType.Guard;
        }
    }

    /// <summary>执行器每 tick 求值的世界状态（谓词判定入口）。由 PlanExecutor 构建。</summary>
    public abstract class WorldState
    {
        /// <summary>条件求值（§5.2 封闭谓词 + and/or 组合 + sustained/was 修饰符）。</summary>
        public abstract bool Evaluate(Condition c);

        /// <summary>条件翻转检测（was 语义）：曾成立过吗。</summary>
        public abstract bool WasEverTrue(Condition c);
    }
}
