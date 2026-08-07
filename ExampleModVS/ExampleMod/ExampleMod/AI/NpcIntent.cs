using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// NPC 的当前高层意图。
    /// 与 IntentBase（玩家交互选项）对应但不同：
    ///   IntentBase = 玩家能对 NPC 做什么（对话菜单选项）
    ///   NpcIntent  = NPC 自己此刻的内在状态 / 想干什么
    /// 互斥状态机，由 AgentBrain.SetNpcIntent 设置。
    /// </summary>
    public class NpcIntent
    {
        public NpcIntentType Type { get; }
        public Agent Target { get; }  // 意图针对的目标（可为 null）

        /// <summary>
        /// 质问子类型（仅在 Type == Confronting 时有值）。
        /// 复用自原 NpcInterceptIntent，融合后改名为 ConfrontationType。
        /// </summary>
        public ConfrontationType? InterceptDetail { get; }

        /// <summary>
        /// 命令类别 detail（仅在 Type == ExecutingCommand 时有值）。
        /// 复用 Confronting + InterceptDetail 的 detail 模式：
        /// 计划层键 CommandIntentType 被 NpcIntent 直接持有（包含关系，无映射）。
        /// </summary>
        public CommandIntentType? CommandDetail { get; }

        public NpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null, CommandIntentType? commandDetail = null)
        {
            Type = type;
            Target = target;
            InterceptDetail = interceptDetail;
            CommandDetail = commandDetail;
        }

        public override string ToString()
        {
            string typeName = Type switch
            {
                // 空闲意图：NPC 无特定行为
                NpcIntentType.None => LWNTextHelper.ResolveText("LWN_ui_npcintent_none"),
                // 战斗中意图：NPC 正在交战
                NpcIntentType.Fighting => LWNTextHelper.ResolveText("LWN_ui_npcintent_fighting"),
                // 想认输意图：NPC 想要投降
                NpcIntentType.Surrendering => LWNTextHelper.ResolveText("LWN_ui_npcintent_surrendering"),
                // 质问意图：NPC 正在质问/对峙玩家
                NpcIntentType.Confronting => LWNTextHelper.ResolveText("LWN_ui_npcintent_confronting"),
                // 跟随意图：NPC 正在跟随某人
                NpcIntentType.Following => LWNTextHelper.ResolveText("LWN_ui_npcintent_following"),
                // 交互中意图：NPC 正在与玩家交互
                NpcIntentType.Interacting => LWNTextHelper.ResolveText("LWN_ui_npcintent_interacting"),
                // 被击晕意图：NPC 被击晕倒地
                NpcIntentType.KnockedOut => LWNTextHelper.ResolveText("LWN_ui_npcintent_knockedout"),
                // 执行命令中：NPC 正在执行玩家密谋计划（携带 CommandDetail）
                NpcIntentType.ExecutingCommand => LWNTextHelper.ResolveText("LWN_ui_npcintent_executingcommand"),
                _ => Type.ToString()
            };
            string detailStr = InterceptDetail switch
            {
                // 威慑子类型：驱离警告
                ConfrontationType.Deter => LWNTextHelper.ResolveText("LWN_ui_npcintent_deter"),
                // 搜查子类型：要求搜查包裹
                ConfrontationType.Search => LWNTextHelper.ResolveText("LWN_ui_npcintent_search"),
                // 追回子类型：人赃并获追回赃物
                ConfrontationType.Recover => LWNTextHelper.ResolveText("LWN_ui_npcintent_recover"),
                // 制止子类型：制止暴力行为
                ConfrontationType.Stop => LWNTextHelper.ResolveText("LWN_ui_npcintent_stop"),
                _ => ""
            };
            // 命令类别 detail（"执行计划中·引开→守卫"；与既有 Confronting 拼接模式同构）
            string cmdStr = "";
            if (CommandDetail != null)
                cmdStr = LWNTextHelper.ResolveText($"LWN_ui_commandintent_{CommandDetail.ToString().ToLowerInvariant()}",
                    CommandDetail.ToString());
            string targetStr = Target != null ? $"→{Target.Name}" : "";
            return $"{typeName}{cmdStr}{targetStr}";
        }
    }

    /// <summary>
    /// NPC 高层意图类型。所有值互斥。
    /// </summary>
    public enum NpcIntentType
    {
        None,           // 无特定意图（默认/空闲）
        Fighting,       // 战斗中（正在与某人交战）
        Surrendering,   // 想要认输（仍处于战斗中，但意图已转变）
        Confronting,    // 质问/对峙玩家（L3 警戒触发）。携带 ConfrontationType detail。
        Following,      // 跟随某人（护卫/命令跟随）
        Interacting,    // 正在与玩家交互/对话中
        KnockedOut,     // 被击晕（StayAction 占位）
        ExecutingCommand, // 正在执行玩家密谋计划（§10）。携带 CommandIntentType detail。
    }
}
