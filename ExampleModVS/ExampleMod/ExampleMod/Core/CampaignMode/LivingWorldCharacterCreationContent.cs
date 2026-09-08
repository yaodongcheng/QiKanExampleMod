using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using Helpers;

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

		/// <summary>
		/// 🔴 第 25 颗雷（2026-09-08）——阶段链必须含 FaceGenerator：
		/// FaceGenChars（Generic 阶段 3D 模型的数据源）由 CharacterCreationFaceGeneratorView 完成时
		/// ChangeFaceGenChars 填入；缺该阶段 = FaceGenChars 空 = RefreshCharacterEntity 置 null =
		/// GenericStageView.Tick foreach NRE。原版顺序（SandboxCharacterCreationContent 实锤）=
		/// Culture → FaceGenerator → Generic → …；织丰本机同名补丁 RacesPatch 收窄种族。
		/// </summary>
		public override IEnumerable<Type> CharacterCreationStages => new[]
		{
			typeof(CharacterCreationCultureStage),
			typeof(CharacterCreationFaceGeneratorStage),
			typeof(CharacterCreationGenericStage),
			typeof(CharacterCreationReviewStage),
		};

		/// <summary>
		/// 选文化完成（织丰母本同款：shokuho_full.cs 164600）——补上家族名与头衔默认值：
		/// 基类默认不生成家族名（我们在 Stage 链里砍了 ClanNaming 阶段——家名改这里一次性生成，为 v0 占位）。
		/// </summary>
		protected override void OnCultureSelected()
		{
			SelectedTitleType = 1;
			SelectedParentType = 0;
			TextObject clanName = FactionHelper.GenerateClanNameforPlayer();
			Clan.PlayerClan.ChangeClanName(clanName, clanName);
		}

		/// <summary>
		/// 🔴 第 24 颗雷（2026-09-08）—— Generic 阶段没有菜单 = 直接把建号界面搞崩：
		/// 1.2.12 建号链 = CharacterCreationState 按 CharacterCreationStages 见 stage 见 view；
		/// CharacterCreationGenericStageView 从菜单列表 0 开始逐页消费（_stageIndex++ < CharacterCreationMenuCount），
		/// CharacterCreationGenericStageVM 构造时调 OnInit(_stageIndex) → CharacterCreationMenus[] 越界（空表）。
		/// 菜单由 ContentBase.OnInitialized(CharacterCreation) 里 CharacterCreation.AddNewMenu 建造（织丰做派：
		/// ShokuhoCharacterCreationContent.OnInitialized = Add*Menu×6 全链——我们 v0 = 出身 1 菜单 3 选项）。
		/// </summary>
		protected override void OnInitialized(CharacterCreation characterCreation)
		{
			base.OnInitialized(characterCreation);
			AddBackgroundMenu(characterCreation);
		}

		/// <summary>
		/// v0 最小「出身」菜单（Generic 阶段第一页；菜单走完 → 进 Review）。
		/// 选项语义 = 技能/属性加成（引擎选项自带 ApplySkillAndAttributeEffects，选中即生效）。
		/// ⚠️ v0 内容包 flavor 仍以 _contentPack 分支（先例：ReviewPageDescription）；v1 改内容包数据驱动。
		/// </summary>
		private void AddBackgroundMenu(CharacterCreation characterCreation)
		{
			bool taikou = _contentPack == "Taikou";
			var menu = new CharacterCreationMenu(
				new TextObject("{=LWN_cc_bg_title}Your Origins"),
				taikou
					? new TextObject("{=LWN_cc_bg_intro_taikou}Before your fate carried you toward the capital, you were born into...")
					: new TextObject("{=LWN_cc_bg_intro}Before you took to the road, you were born into..."),
				null);
			CharacterCreationCategory category = menu.AddMenuCategory(() => true);
			if (taikou)
			{
				AddBackgroundOption(category,
					"{=LWN_cc_bg_taikou_retainer}A Samurai Retainer",
					"{=LWN_cc_bg_taikou_retainer_desc}Your family has served the clan for generations, carrying the sword and bearing the honor of their lord.",
					DefaultSkills.OneHanded, DefaultSkills.Athletics, DefaultCharacterAttributes.Vigor);
				AddBackgroundOption(category,
					"{=LWN_cc_bg_taikou_merchant}A Merchant's Child",
					"{=LWN_cc_bg_taikou_merchant_desc}You grew up counting coins in a town by the river; the ledger and the road are your inheritance.",
					DefaultSkills.Trade, DefaultSkills.Charm, DefaultCharacterAttributes.Social);
				AddBackgroundOption(category,
					"{=LWN_cc_bg_taikou_ronin}A Wandering Ronin",
					"{=LWN_cc_bg_taikou_ronin_desc}You lost your master and your purpose, so you drift between the provinces, taking work where it is offered.",
					DefaultSkills.Roguery, DefaultSkills.Bow, DefaultCharacterAttributes.Cunning);
			}
			else
			{
				AddBackgroundOption(category,
					"{=LWN_cc_bg_soldier}A Soldier",
					"{=LWN_cc_bg_soldier_desc}You served in several wars and learned to survive on the march and in the melee.",
					DefaultSkills.OneHanded, DefaultSkills.Athletics, DefaultCharacterAttributes.Vigor);
				AddBackgroundOption(category,
					"{=LWN_cc_bg_trader}A Merchant",
					"{=LWN_cc_bg_trader_desc}You carried goods along the trade roads and learned to sell a word as well as a ware.",
					DefaultSkills.Trade, DefaultSkills.Charm, DefaultCharacterAttributes.Social);
			}
			characterCreation.AddNewMenu(menu);
		}

		private void AddBackgroundOption(CharacterCreationCategory category, string titleText, string descriptionText, SkillObject skill1, SkillObject skill2, CharacterAttribute attribute)
		{
			var skills = new MBList<SkillObject>();
			skills.Add(skill1);
			skills.Add(skill2);
			category.AddCategoryOption(
				new TextObject(titleText),
				skills,
				attribute,
				FocusToAdd, SkillLevelToAdd, AttributeLevelToAdd,
				null,
				new CharacterCreationOnSelect(cc => { }),
				null,
				new TextObject(descriptionText));
		}

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
