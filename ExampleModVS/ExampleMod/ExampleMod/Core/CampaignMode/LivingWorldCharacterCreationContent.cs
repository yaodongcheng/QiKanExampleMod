using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 通用建号内容（自定义战役的建号阶段管线）。
	/// 母本契约 = TaleWorlds.CampaignSystem.CharacterCreationContentBase（1.2.12 反编译）：
	/// 实现 CharacterCreationStages（阶段类型列表，引擎按类型配默认 View）+ ReviewPageDescription。
	/// v0 = 2 阶段（选文化 → 总览确认）；太阁式出身/特典等阶段为 v1+（按内容包扩展）。
	/// 🔴 1.5.x 的建号系统为全新体系（CharacterCreationContent 已 sealed + CharacterCreationManager），
	/// 本类仅在 1.2.12 编译生效；1.5.x 下为空占位（无内容包运行，建号不经过这里）。
	/// </summary>
	public class LivingWorldCharacterCreationContent
#if MB2_V1212
		: CharacterCreationContentBase
#endif
	{
#if MB2_V1212
		private readonly string _contentPack;

		public LivingWorldCharacterCreationContent(string contentPack)
		{
			_contentPack = contentPack;
		}

		public override TextObject ReviewPageDescription => _contentPack == "Taikou"
			? new TextObject("{=LWN_charc_review_taikou}You are a samurai of the Sengoku period of Japan.")
			: new TextObject("{=LWN_charc_review}You are a traveler of a new world.");

		public override IEnumerable<Type> CharacterCreationStages => new[]
		{
			typeof(CharacterCreationCultureStage),
			typeof(CharacterCreationReviewStage),
		};

		/// <summary>
		/// 建号完成 → 重设主队出生位置。
		/// 🔴 引擎 Campaign.DefaultStartingPosition 非 virtual（InitializeMainParty / CC finalize 兜底
		/// 都锁定基类实现）→ 出生点在本处写入（织丰母本同法：shokuho.txt:164613）。
		/// </summary>
		public override void OnCharacterCreationFinalized()
		{
			base.OnCharacterCreationFinalized();
			if (_contentPack == "Taikou" && MobileParty.MainParty != null)
			{
				MobileParty.MainParty.Position2D = TaikouCampaign.TaikouStartingPosition;
			}
		}
#else
		// v1.5.x：TODO —— 接 CharacterCreationManager 新体系时实现
#endif
	}
}
