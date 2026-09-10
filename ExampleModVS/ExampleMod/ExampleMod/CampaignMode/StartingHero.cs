using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 「选人开局」的静态交接 + 落地执行（机制源自 StartAsAnyone 反编译，见
	/// <c>plans/选人开局-实施计划.md</c>；**只用其机制，不用其 GUI**）。
	///
	/// 数据流（🔴 时点见 <see cref="ApplyPending"/> 注释）：
	///   选剧本 → 开战役 → 世界建好（OnLoadFinished）→ 弹选人界面 →
	///   选中英雄 → <see cref="ApplyPending"/> = 换人（魂穿）+ 走完引擎的建号收尾（推入大地图）。
	///
	/// 🔴 换人动作本身 = <c>ChangePlayerCharacterAction.Apply(hero)</c> 一行；
	///   光调它不够——生日会被重置、`PlayerDefaultFaction` 仍指向占位家族、原占位家族会留成空壳，
	///   故另补三件事，见 <see cref="Apply"/>。
	/// </summary>
	public static class StartingHero
	{
		/// <summary>待开局的英雄 StringId；null = 没选人（走自定义建号）。</summary>
		private static string _pendingHeroId;

		/// <summary>是否已选了要扮演的英雄。</summary>
		public static bool HasPending => !string.IsNullOrEmpty(_pendingHeroId);

		/// <summary>选人界面点定英雄时调用（随后启动战役）。</summary>
		public static void SetPending(Hero hero)
		{
			_pendingHeroId = hero?.StringId;
			DebugLogger.Log($"[StartingHero] 已选开局英雄：{_pendingHeroId ?? "(null)"}");
		}

		/// <summary>待选英雄的 StringId（GameManager 用它解析；无 = null）。</summary>
		public static string PendingHeroId => _pendingHeroId;

		/// <summary>走「自定义英雄」时调用（清掉可能残留的待选，防第二次开局误用）。</summary>
		public static void ClearPending()
		{
			if (_pendingHeroId != null)
			{
				DebugLogger.Log($"[StartingHero] 清除待选（{_pendingHeroId}）——本次走自定义建号");
			}
			_pendingHeroId = null;
		}

		/// <summary>
		/// 用给定英雄落地（世界建好后调用）。换人 + 走完引擎的"建号结束"收尾。
		/// 成功 = 玩家已变成该英雄，且正在进入大地图。
		/// </summary>
		public static bool ApplyPending(Hero hero)
		{
			if (hero == null)
			{
				DebugLogger.Log("[StartingHero] ApplyPending 拿到 null 英雄 —— 不落地");
				return false;
			}
			try
			{
				Apply(hero);
				FinalizeCampaignStart();
				return true;
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] FAILED：{ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
				return false;
			}
		}

		/// <summary>
		/// 走完引擎的「建号结束」收尾（我们跳过了建号，但**这几步不能省**）。
		/// 对照 <c>CharacterCreationState.FinalizeCharacterCreation</c>（反编译实锤）逐步照做，
		/// **唯一省略** = 它开头的 `CharacterCreation.ApplyFinalEffects()`（那是把建号菜单里选的
		/// 出身/加点写进角色——我们没走建号，没有这类选择要应用）。
		/// 其余一步不落：推入大地图 + 视觉刷新 + 通知建号内容 + 广播 OnCharacterCreationIsOver。
		/// </summary>
		private static void FinalizeCampaignStart()
		{
			// ⓪ 🔴 **撤销"禁止其它状态激活"请求**（引擎 FinalizeCharacterCreation 的第 2 步，最容易漏）：
			//   `GameStateManager.ActiveStateDisabledByUser` 只要还有未撤销的请求就为真 → 地图状态
			//   永远不激活 = 游戏卡死（能看见画面但无法操作，实机 2026-09-10 踩过）。
			//   请求由建号状态 `RegisterActiveStateDisableRequest(this)` 注册；
			//   我们若推过建号状态（自定义人物路径）就必须撤，没推过则本次调用无副作用。
			try
			{
				var ccState = GameStateManager.Current?.ActiveState as CharacterCreationState;
				if (ccState != null)
				{
					Game.Current.GameStateManager.UnregisterActiveStateDisableRequest(ccState);
					DebugLogger.Log("[StartingHero] 已撤销建号状态的禁用请求（ActiveStateDisableRequest）");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 撤销禁用请求失败：{ex.Message}");
			}

			// ① 推入大地图（引擎同款：CleanAndPushState——清掉加载屏/选人屏，只留 MapState）
			try
			{
				Game.Current.GameStateManager.CleanAndPushState(
					Game.Current.GameStateManager.CreateState<MapState>());
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 推入大地图失败：{ex.Message}");
			}

			// ② 主队视觉刷新（换人后立绘/图标要重算）
			try
			{
				PartyBase.MainParty?.SetVisualAsDirty();
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] SetVisualAsDirty 失败：{ex.Message}");
			}

			// ③ 通知建号内容收尾（我们的内容类里会 ResetCamera + TeleportCameraToMainParty）
			try
			{
				CharacterCreationContentBase.Instance?.OnCharacterCreationFinalized();
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] OnCharacterCreationFinalized 通知失败：{ex.Message}");
			}

			// ④ 广播「建号结束」（引擎收尾的最后一步）
			try
			{
				CampaignEventDispatcher.Instance.OnCharacterCreationIsOver();
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] OnCharacterCreationIsOver 广播失败：{ex.Message}");
			}
		}

		/// <summary>
		/// 把玩家身份换成该英雄。动作序列（原版机制 + 我们裁定的取舍）共 5 步：
		/// ①换身份（引擎 API）②还原生日 ③玩家默认势力 ④**保留**物品栏（魂穿：人和家当一起要）
		/// ⑤销毁原占位家族 + 相机归位。
		/// </summary>
		private static void Apply(Hero hero)
		{
			// ── 换人前先取好要用的东西（换完 Hero.MainHero 就变了）──
			Hero placeholderHero = Hero.MainHero;
			Clan placeholderClan = placeholderHero?.Clan;
			TaleWorlds.CampaignSystem.CampaignTime birthday = hero.BirthDay;

			// ── ① 换玩家身份（唯一的引擎 API）──
			//   引擎内部：Game.PlayerTroop = hero.CharacterObject → OnPlayerCharacterChanged
			//   → MobileParty.MainParty 变成该英雄的部队（人/钱/粮/装备全跟过来）
			ChangePlayerCharacterAction.Apply(hero);

			// ── ② 还原生日：换人会把生日重置（年龄变 18）──
			try
			{
				Hero.MainHero.SetBirthDay(birthday);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 生日还原失败（年龄可能不对）：{ex.Message}");
			}

			// ── ③ 玩家默认势力 ──
			//   🔴 `Campaign.PlayerDefaultFaction` 是 **internal**（引擎建号期自己写成 player_faction 占位家族），
			//   而 `Clan.PlayerClan => Campaign.Current.PlayerDefaultFaction` —— 不改写它，换人后
			//   玩家家族相关的判定会继续指向那个已被销毁的占位家族。故用反射写（StartAsAnyone 同款）。
			SetPlayerDefaultFaction(Hero.MainHero?.Clan);

			// ── ④ 物品栏**不清空**（2026-09-10 用户裁定：魂穿 = 人和家当一起要；
			//   换人后主队 = 该领主的部队，其库存原样保留）──
			//   ⚠️ 曾经此处清空物品栏（照抄 StartAsAnyone 的平衡口味），已按裁定移除。

			// ── ⑤ 销毁原占位家族（换人后它被清空成无主，留着会出问题）──
			DestroyPlaceholderClan(placeholderClan);

			// ── 相机归位（否则镜头停在旧位置）──
			ResetCameraToMainParty();

			DebugLogger.Log($"[StartingHero] 完成：玩家现为 {Hero.MainHero?.StringId}"
				+ $"（家族 {Hero.MainHero?.Clan?.StringId} / 部队 {MobileParty.MainParty?.StringId}）");
		}

		/// <summary>
		/// 写 `Campaign.PlayerDefaultFaction`（internal 属性，反射写）。
		/// 不写 = 换人后玩家默认势力仍指向已被销毁的占位家族（`Clan.PlayerClan` 会跟着错）。
		/// </summary>
		private static void SetPlayerDefaultFaction(Clan clan)
		{
			try
			{
				var prop = typeof(Campaign).GetProperty("PlayerDefaultFaction",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop != null && prop.CanWrite)
				{
					prop.SetValue(Campaign.Current, clan, null);
					DebugLogger.Log($"[StartingHero] PlayerDefaultFaction 已置为 {clan?.StringId ?? "(null)"}");
				}
				else
				{
					DebugLogger.Log("[StartingHero] PlayerDefaultFaction 属性不可写（反射未命中）——玩家势力判定可能指向旧占位家族");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] PlayerDefaultFaction 置位失败：{ex.Message}");
			}
		}

		/// <summary>
		/// 销毁占位家族：先注销其全部英雄，再从 CampaignObjectManager 移除家族。
		/// 🔴 用反射——`UnregisterDeadHero` / `RemoveClan` 是引擎内部方法（StartAsAnyone 同款做法）。
		/// 失败不致命（家族留空壳），只记日志。
		/// </summary>
		private static void DestroyPlaceholderClan(Clan clan)
		{
			if (clan == null)
			{
				return;
			}
			try
			{
				var objectManager = Campaign.Current?.CampaignObjectManager;
				if (objectManager == null)
				{
					return;
				}
				Type omType = objectManager.GetType();
				MethodInfo unregister = omType.GetMethod("UnregisterDeadHero",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				MethodInfo removeClan = omType.GetMethod("RemoveClan",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				var heroes = new List<Hero>(clan.Heroes);
				foreach (Hero h in heroes)
				{
					try
					{
						unregister?.Invoke(objectManager, new object[] { h });
						if (h.CharacterObject != null)
						{
							MBObjectManager.Instance?.UnregisterObject(h.CharacterObject);
						}
					}
					catch (Exception exInner)
					{
						DebugLogger.Log($"[StartingHero] 注销占位英雄 {h?.StringId} 失败：{exInner.Message}");
					}
				}
				removeClan?.Invoke(objectManager, new object[] { clan });
				DebugLogger.Log($"[StartingHero] 占位家族 {clan.StringId} 已销毁（英雄 {heroes.Count} 名）");
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 销毁占位家族失败（不致命，家族留空壳）：{ex.Message}");
			}
		}

		/// <summary>相机拉回主队（换人后位置变了）。</summary>
		private static void ResetCameraToMainParty()
		{
			try
			{
				if (GameStateManager.Current?.ActiveState is MapState mapState && mapState.Handler != null)
				{
					mapState.Handler.ResetCamera(true, true);
					mapState.Handler.TeleportCameraToMainParty();
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 相机归位失败：{ex.Message}");
			}
		}

		/// <summary>按 StringId 找英雄（在世优先；找不到返回 null）。</summary>
		private static Hero FindHeroById(string heroId)
		{
			if (string.IsNullOrEmpty(heroId))
			{
				return null;
			}
			Hero found = null;
			try
			{
				found = Hero.FindFirst(h => h.StringId == heroId);
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[StartingHero] 查英雄抛异常：{ex.Message}");
			}
			return found;
		}
	}
}
