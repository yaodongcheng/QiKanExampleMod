using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
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
#else
		// v1.5.x：TODO —— 接 CharacterCreationManager 新体系时实现
#endif
	}
}
