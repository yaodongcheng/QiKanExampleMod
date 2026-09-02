using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
    /// <summary>
    /// 主动伤害接口轮子（2026-09-02 用户裁定）：**用引擎公开 API 造成伤害，禁止改引擎判定**。
    ///
    /// 引擎伤害入口全景（1.2.12 反编译确认）：
    ///   ① Agent.RegisterBlow(Blow, in AttackCollisionData) — 标准伤害管线（HandleBlow）：
    ///      扣血 → Mission.OnAgentHit（所有 MissionBehavior）→ 死亡判定（Health&lt;1 → Die）→ 打击音效。
    ///      注意：**不包含**血粒子/碰撞反馈（那些在 native MeleeHitCallback 碰撞回调里，API 路径不走）；
    ///      音效在 HandleBlow 开头无条件播（伤害为 0 也播）——重放必须带 BlowFlags.NoSound 防双音。
    ///   ② Agent.Health setter — 纯 C# 字段写入（_health）+ OnAgentHealthChanged 公共事件；
    ///      **不走伤害管线**：无 OnAgentHit、无音效、降到 0 不会死（致死要自己调 Die）——毒/燃烧 DoT 用它。
    ///   ③ Agent.Die(Blow, KillInfo) — 处决式死亡（Health=0 + MBAPI.IMBAgent.Die + 编队死亡登记）。
    ///
    /// 来源：Agent.HandleBlow 内 `if (b.InflictedDamage &lt;= 0) return;` 之前的早期返回 +
    /// Mission.MeleeHitCallback → CancelsDamageAndBlocksAttackBecauseOfNonEnemyCase
    ///（单机对「非敌方人类」取消近战伤害）——这是和平场景打村民 0 伤害的引擎地板行为。
    /// </summary>
    public static class AgentDamageHelper
    {
        /// <summary>
        /// 低层：按模板 blow 重放一条真伤害（模板 = 引擎 OnRegisterBlow 收到的 0 伤害 blow——
        /// 部位/武器/方向字段齐全，仅伤害被清零）。复制所有字段，只覆盖伤害相关。
        /// 🔴 语义：走 HandleBlow 全管线（扣血 + OnAgentHit + 死亡判定 + 打击音效）；
        /// 音效默认抑制（引擎第一遍 HandleBlow 已播过——见类注释①），需要自己播时传 false。
        /// </summary>
        public static void CastBlow(Agent victim, in Blow template, in AttackCollisionData collisionData,
            float damage, bool suppressSound = true)
        {
            if (victim == null || !AgentControlHelper.SafeIsActive(victim)) return;

            Blow b = template;                    // struct 拷贝，模板字段（WeaponRecord/方向/部位）全部保留
            b.InflictedDamage = MathF.Round(damage);
            b.BaseMagnitude = MathF.Min(template.BaseMagnitude, 1000f);
            b.SelfInflictedDamage = 0;
            if (suppressSound) b.BlowFlag |= BlowFlags.NoSound;   // ①防双音（第一遍已播）
            DebugLogger.Log($"[WarningStrike] CastBlow: {victim.Name}(Idx={victim.Index}) 扣血 {(int)b.InflictedDamage}（模板来源 attackType={template.AttackType} ownedBy={template.OwnerId}）");
            victim.RegisterBlow(b, in collisionData);
        }

        /// <summary>警告刀基础伤害（用户裁定 2026-09-02：第一刀"模拟造成一点伤害"，激活血条反馈）。</summary>
        private const float WarningStrikeBaseDamage = 9f;

        /// <summary>警告刀随机浮动幅度（9 ~ 15，绝不致命：保命 clamp 兜底）。</summary>
        private const float WarningStrikeRandomRange = 6f;

        /// <summary>
        /// 警告刀（第一刀语义，2026-09-02 用户裁定）：玩家攻击「非敌队」的真实命中目标（和平村民），
        /// 引擎取消伤害（0 伤害）但 OnRegisterBlow 照常触发——本方法补一条真伤害：
        ///   - 伤害 = 固定警告值（9~15）——不论武器轻重，都是"警告"统一手感；
        ///   - 保命 clamp：最多把目标打到 1 血（警告不死，血条反馈保留）；
        ///   - 不建敌队、不改队伍关系（不进战斗——设计上后续对话/警戒由脑流程处理）；
        ///   - recordAsAssault 时记袭击身价（犯罪事件 AssaultValue 首记；同人重复打击不重复累计身价）。
        /// 调用点：AttackTriggerMissionLogic.OnRegisterBlow（玩家攻击 + 0 伤害 + 非敌队条件段）。
        /// 用法范例：AgentDamageHelper.ApplyWarningStrike(victim, b, collisionData, record);
        /// </summary>
        public static void ApplyWarningStrike(Agent victim, in Blow template,
            in AttackCollisionData collisionData, bool recordAsAssault)
        {
            if (victim == null || !AgentControlHelper.SafeIsActive(victim)) return;

            float dmg = WarningStrikeBaseDamage + MBRandom.RandomFloat * WarningStrikeRandomRange;

            // 保命 clamp：已经 1 血 → 不再补刀（0 伤害），否则下调到只剩 1 血
            float maxSafe = victim.Health - 1f;
            if (maxSafe <= 0f)
            {
                DebugLogger.Log($"[WarningStrike] 跳过（目标已临死 Health={victim.Health:F0}）：{victim.Name}");
                return;
            }
            dmg = MathF.Min(dmg, maxSafe);
            DebugLogger.Log($"[WarningStrike] 警告刀: {victim.Name}(Idx={victim.Index}) Health={victim.Health:F0} → 扣 {dmg:F1}（保命后）记身价={recordAsAssault}");

            CastBlow(victim, in template, in collisionData, dmg);

            if (recordAsAssault)
                AgentAIController.Instance?.RecordAssaultVictim(victim);
        }
    }
}
