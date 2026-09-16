using HarmonyLib;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 火器射击的技能映射补漏 —— 🔴 **通用修复，与太阁无关**（放 Core/ = 通用层；纯功能包模式跑原版战役同样生效）。
	///
	/// **崩溃现场**（实机 2026-09-16，太阁火器工程；玩家用铁炮打中商队护卫）：
	///   <c>System.NullReferenceException</c>，托管栈完整可读：
	///   <c>DefaultCharacterDevelopmentModel.CalculateLearningRate(Hero hero, SkillObject skill)</c>
	///   ← <c>HeroDeveloper.AddSkillXp(skill, …)</c>
	///   ← <c>DefaultSkillLevelingManager.OnCombatHit(…, WeaponComponentData affectorWeapon, …)</c>
	///   ← <c>BattleAgentLogic.EnemyHitReward</c> ← <c>Mission.MissileHitCallback</c>。
	///   崩的是 <c>skill.CharacterAttribute</c> —— <c>skill</c> 是 <b>null</b>。
	///
	/// **根因 = 引擎自己的映射表漏了它自己定义的三个火器类**：
	///   <c>WeaponComponentData.GetRelevantSkillFromWeaponClass(WeaponClass)</c> 用 switch 映射技能，
	///   覆盖了 Arrow/Bow、Bolt/Crossbow、投掷、单手、双手、长杆、盾 ——
	///   **唯独没有 <c>Cartridge</c> / <c>Musket</c> / <c>Pistol</c>**，落到 <c>return null</c>。
	///   而 <c>DefaultSkillLevelingManager.OnCombatHit</c> 拿它直接 <c>AddSkillXp(skillObject, …)</c>，**无 null 守卫**。
	///   ⚠️ 命中时传进来的 <c>affectorWeapon</c> 是**弹药**的 WeaponComponentData
	///   （实锤：把弹药写成 <c>weapon_class="Cartridge"</c> 必崩；写成 <c>Bolt</c> 不崩 ——
	///    织丰与 CA_BloodSmoke 的铁炮弹药都写 Bolt，所以它们没暴露这条）。
	///   旁证「这是遗漏不是设计」：同一个类的 <c>GetItemTypeFromWeaponClass</c> **有**这三个类
	///   （Cartridge→Bullets / Pistol→Pistol / Musket→Musket），只有技能映射这一处漏了。
	///
	/// **为什么打补丁而不是改数据**（数据侧原本能用「弹药写 Bolt」绕过）：
	///   绕过的代价是**火器失去与普通弩的区分标记** —— 内容包侧靠 <c>ammo_class</c> 认火器
	///   （决定播枪声还是弩声），一旦退回 Bolt，原版弩兵开火会被一并认成火器（Taikou 是内容包，
	///   原版弩兵还在；织丰全库没有普通弩所以碰不到）。用「口径」当火器身份是内容包唯一的
	///   非世界观依赖判据，不能为了绕一个引擎遗漏而放弃。
	///   本补丁性质 = **补引擎遗漏**（不是绕过限制、不是给别人 mod 兜底），属可保留一类。
	///
	/// **覆盖策略**：Postfix 只在 <c>__result == null</c> 时介入，且只认那三个火器类 →
	///   原版一切既有映射**逐字节不变**（弓弩近战各归各的技能），补丁在最坏情况下是 no-op。
	///   火器统一归 <c>Crossbow</c> 技能：引擎没有火器专属技能，而弩技能是唯一同类
	///   （武将/兵种侧的火器也都配 Crossbow 技能，两边一致）。**不改任何数值**，只让经验有处可加。
	///
	/// **版本证据**（本机四版本参考 DLL 逐个反编译，2026-09-16）：
	///   <c>GetRelevantSkillFromWeaponClass</c> 在 **1.2.12 / 1.3.15 / 1.4.6 / 1.5.1 全部存在**，
	///   且四个版本的映射表里**都没有** Cartridge/Musket/Pistol —— 官方至今没补过，方法名跨版本稳定。
	///
	/// **退役条件**：官方把 Cartridge/Musket/Pistol 补进 <c>GetRelevantSkillFromWeaponClass</c> 之日
	///   （届时本补丁因 <c>__result != null</c> 自动 no-op，可连同登记台账一并删除）。
	/// </summary>
	[HarmonyPatch(typeof(WeaponComponentData), "GetRelevantSkillFromWeaponClass")]
	internal static class FirearmSkillMappingPatch
	{
		private static void Postfix(WeaponClass weaponClass, ref SkillObject __result)
		{
			if (__result != null)
			{
				return;
			}
			if (weaponClass == WeaponClass.Cartridge
				|| weaponClass == WeaponClass.Musket
				|| weaponClass == WeaponClass.Pistol)
			{
				__result = DefaultSkills.Crossbow;
			}
		}
	}
}
