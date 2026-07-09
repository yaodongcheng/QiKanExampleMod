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

        public NpcIntent(NpcIntentType type, Agent target = null, ConfrontationType? interceptDetail = null)
        {
            Type = type;
            Target = target;
            InterceptDetail = interceptDetail;
        }

        public override string ToString()
        {
            string typeName = Type switch
            {
                NpcIntentType.None => "空闲",
                NpcIntentType.Fighting => "战斗中",
                NpcIntentType.Surrendering => "想认输",
                NpcIntentType.Confronting => "质问",
                NpcIntentType.Following => "跟随",
                NpcIntentType.Interacting => "交互中",
                NpcIntentType.KnockedOut => "被击晕",
                _ => Type.ToString()
            };
            string detailStr = InterceptDetail switch
            {
                ConfrontationType.Deter => "(威慑)",
                ConfrontationType.Search => "(搜查)",
                ConfrontationType.Recover => "(追回)",
                ConfrontationType.Stop => "(制止)",
                _ => ""
            };
            string targetStr = Target != null ? $"→{Target.Name}" : "";
            return $"{typeName}{detailStr}{targetStr}";
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
    }
}
