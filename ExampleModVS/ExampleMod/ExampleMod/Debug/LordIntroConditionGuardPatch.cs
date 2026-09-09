using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.Core;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 领主自我介绍句（LordConversationsCampaignBehavior.conversation_lord_introduction_on_condition）
	/// 空段防护（通用基座，2026-09-09 雷 35 —— Taikou 找织田信长对话崩溃）。
	///
	/// 机制实锤（反编译 1.2.12 TaleWorlds.CampaignSystem.dll）：
	///   该条件先查文本 str_comment_*（FindMatchingTextOrNull：文本键不存在时**返回 null**，
	///   无空保护），随后直接 textObject.SetTextVariable("FACTION", ...) → NRE。
	///   这些文本定义在 SandBox/ModuleData/comment_strings.xml ——**注册时带 GameType 白名单**
	///   （Campaign/CampaignStoryMode，SandBox SubModule.xml:227-231 实锤）—— 自定义 GameType
	///   （TaikouCampaign 等）下整文件被过滤 → 文本缺失 → 崩。
	///   织丰对照：织丰自带全套文本（ShokuhoLordConversationBehavior + 自备 str_comment_*）。
	///
	/// 本补丁 = 通用兜底（数据侧修复 = Taikou 自备 comment_strings.xml 后介绍句正常运行；
	/// 本补丁保底任何内容包/任何数据残缺的"不崩"纪律）：
	///   文本缺失 / Hero.Clan 缺失 / 任一城镇 OwnerClan 缺失 → 跳过该句返回 false，
	///   对话照常进行（不显示介绍句），并打日志说明原因。
	/// 版本：类型名+方法名均为字符串运行期解析（1.2.12 二进制 grep 命中；
	///   改动/改名 = 静默跳过，以 [LordIntroGuard] 日志缺失即可察觉）。
	/// </summary>
	[HarmonyPatch("TaleWorlds.CampaignSystem.CampaignBehaviors.LordConversationsCampaignBehavior", "conversation_lord_introduction_on_condition")]
	public static class LordIntroConditionGuardPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(ref bool __result)
		{
			try
			{
				ConversationManager cm = Campaign.Current.ConversationManager;
				Hero hero = Hero.OneToOneConversationHero;
				if (hero == null || !cm.CurrentConversationIsFirst || !hero.IsLord)
				{
					return true; // 非领主介绍场景 → 原版自己处理
				}

				Clan clan = hero.Clan;
				var mapFaction = clan?.MapFaction;
				bool clanBroken = clan == null || mapFaction == null || !mapFaction.IsKingdomFaction;

				// 与vanilla同款的文本id选择（null-safe），查文本是否存在
				string textId = null;
				bool textMissing = false;
				if (!clanBroken)
				{
					textId = (mapFaction.Leader == Hero.MainHero) ? "str_comment_vassal_introduces_self" :
						(mapFaction.Leader == hero) ? "str_comment_liege_introduces_self" :
						(mapFaction.Culture != hero.CharacterObject?.Culture) ? "str_comment_noble_generic_intro" :
						(clan.Renown >= 200f) ? "str_comment_noble_introduces_self_and_clan" : "str_comment_noble_introduces_self";
					textMissing = cm.FindMatchingTextOrNull(textId, CharacterObject.OneToOneConversationCharacter) == null;
				}

				var townsWithoutOwner = Campaign.Current.Settlements
					.Where(s => s.IsTown && s.OwnerClan == null).ToList();

				// 全合格 → 放行原版
				if (!clanBroken && !textMissing && townsWithoutOwner.Count == 0)
				{
					return true;
				}

				string townList = townsWithoutOwner.Count == 0
					? "-"
					: string.Join(",", townsWithoutOwner.Select(s => s.StringId));
				DebugLogger.Log($"[LordIntroGuard] 跳过原版领主介绍句（防崩）：hero={hero.Name?.ToString()} " +
					$"clanBroken={clanBroken}(clanNull={clan == null},mapFactionNull={mapFaction == null},isKingdomFaction={mapFaction?.IsKingdomFaction}) " +
					$"textId={textId ?? "-"} textMissing={textMissing} townsNoOwner=[{townList}]");
				__result = false;
				return false;
			}
			catch (Exception ex)
			{
				// 诊断段自身异常：不拦截，交给原版（宁可原版行为，不替换成我们的空转）
				DebugLogger.Log($"[LordIntroGuard] 诊断段异常（未拦截）：{ex}");
				return true;
			}
		}
	}
}
