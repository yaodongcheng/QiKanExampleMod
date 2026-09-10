using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace LivingWorldNpcs
{
	/// <summary>
	/// 无文化角色的「生成期补全」——🔴 **通用修复，与太阁无关**（放 Core/ = 通用层，任何世界观/任何 mod 组合都吃）。
	///
	/// 出处（2026-09-02 用户裁定：Code 方案替代织丰专用 XSLT 数据补丁）：
	///   **织丰 Shokuho** 的 `spnpccharacters.xml` 有一批「镇民/乞丐/匠人」模板**漏写 culture 属性**
	///   （如 `beggar_saikai` —— 而同系列的 `caravan_master_saikai` 等都写了 `culture="Culture.saikai"`）
	///   → XML 加载期按 `culture="id"` 解析不到 → `CharacterObject.Culture == null`
	///   → **织丰自家伤害模型**裸解引用 `.Culture.IsBandit` → NRE 炸穿近战
	///   （同族防线 = `AgentDamageModelCultureNullFix`，那一半是"防崩"，本类这一半是"补数据"）。
	///   ⚠️ 2026-09-10 实测：织丰**至今仍有 24 个模板缺 culture**（`*_saikai` 系列 + 2 个 Special）——没修。
	///
	/// 为什么补在这里：缺 culture 的模板全部是 Townsfolk/Villager 职业——只在**村庄/城镇场景**里
	///   作为 idle 路人生成（兵种/商队/英雄模板都有 culture 无需补）——生成时刻的
	///   `Settlement.CurrentSettlement` 必非空——文化的依据 = 当前定居点文化，零编造、零猜。
	///   模板是全局共享对象（非 per-settlement 实例），但这类模板只在单一文化区使用，无冲突。
	///
	/// 实现与性能/刷屏：事件驱动（MissionLogic.OnAgentCreated）——只检查「新创建」的单个 agent，
	///   无每帧循环（村庄几百人时零成本）；日志按模板 StringId 去重（`_handledTemplates` 跨场次静态），
	///   漏写模板全生命周期最多 24 条日志。英雄跳过（HeroObject.Culture 独立于模板）。
	///
	/// 防御：写失败/反射异常只记日志绝不抛出；与 `AgentDamageModelCultureNullFix`（伤害模型兜底）
	///   是双层战线——本补全治本、兜底防漏网。
	///
	/// 🔴 退役评审记录（2026-09-10，**结论：不退役，已恢复**）：一度按"太阁数据 233/233 全带 culture、
	///   运行期 0 次触发"判为可退役并删除——**那次判定是错的**：本类的服务对象是**织丰**（及任何模板漏写
	///   culture 的内容包），不是太阁；太阁侧 0 触发恰恰说明它工作正常（太阁数据本就齐全）。
	///   凡评审此类补丁，先问「**它原本是为谁写的**」，别看"在我们自己的世界里触发了没有"。
	/// </summary>
	public class CharacterCultureBackfill : MissionLogic
	{
		/// <summary>已补过/已记录过的模板 StringId（跨场次去重）</summary>
		private static readonly HashSet<string> _handledTemplates = new HashSet<string>();

		/// <summary>跨版本反射：Culture 属性各版本挂基类/派生类不同，取基类写《_culture》字段</summary>
		private static readonly PropertyInfo BaseCultureProperty =
			typeof(BasicCharacterObject).GetProperty("Culture", BindingFlags.Public | BindingFlags.Instance);

		public override void OnAgentCreated(Agent agent)
		{
			base.OnAgentCreated(agent);
			try
			{
				// idle 路人在 settlement 场景生成；无 settlement 场景（野战/攻城）无需本补全
				Settlement settlement = Settlement.CurrentSettlement;
				CultureObject settlementCulture = settlement?.Culture;
				if (settlementCulture == null)
				{
					return;
				}

				if (!(agent?.Character is CharacterObject character))
				{
					return;
				}

				// 英雄文化的来源是 HeroObject，与模板无关，跳过
				if (character.HeroObject != null)
				{
					return;
				}

				if (character.Culture != null)
				{
					return;
				}

				Backfill(character, settlementCulture, settlement);
			}
			catch (Exception ex)
			{
				try { DebugLogger.Log($"[CharacterCultureBackfill] 巡检异常（忽略该 agent）: {ex}"); } catch { }
			}
		}

		private static void Backfill(CharacterObject character, CultureObject culture, Settlement settlement)
		{
			string key = character.StringId ?? "?";
			try
			{
				if (!_handledTemplates.Add(key))
				{
					return; // 已补过 -> 之后的 agent 在 Culture!=null 检查处就已跳过，不会重入这里
				}

				BaseCultureProperty?.SetValue(character, culture, null);
				DebugLogger.Log($"[CharacterCultureBackfill] 模板 {key}（{character.Name}）缺少 Culture，" +
								$"已按生成地点「{settlement.Name}」的文化「{culture.Name}」补全。");
			}
			catch (Exception ex)
			{
				_handledTemplates.Remove(key); // 写失败 -> 下次允许重试
				try { DebugLogger.Log($"[CharacterCultureBackfill] 模板 {key} 补全失败: {ex.Message}"); } catch { }
			}
		}
	}
}
