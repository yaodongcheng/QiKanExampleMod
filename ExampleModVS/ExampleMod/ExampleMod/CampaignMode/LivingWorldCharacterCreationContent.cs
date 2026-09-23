using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using Helpers;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 通用建号内容（自定义战役的建号阶段管线）。**两版形态完全不同，靠 #if 分家**：
	///
	/// · **1.2.12**：母本契约 = `CharacterCreationContentBase`（实现 `CharacterCreationStages`
	///   阶段类型列表 + `ReviewPageDescription`）。实例当状态构造参数传进去。
	///   阶段 = 文化 → 捏脸 → 出身 → 总览（4 阶）。
	///
	/// · **1.3.x / 1.4.x / 1.5.x**：`CharacterCreationContentBase` 整类已删，改实现
	///   `ICharacterCreationContentHandler`，且**必须同时是 `CampaignBehaviorBase`** ——
	///   内容是在建号管理器构造期通过 `OnCharacterCreationInitializedEvent` 挂进去的
	///   （见文件下半部分的详细说明与范本出处）。阶段同样复刻成 文化 → 捏脸 → 出身 → 总览。
	///
	/// 两版都**不含** 家纹 / 家名 阶段（1.2.12 本来就没有；1.3.x 把引擎默认的那两阶摘掉）。
	/// </summary>
	public class LivingWorldCharacterCreationContent
#if MB2_V1212
		: CharacterCreationContentBase
#else
		: CampaignBehaviorBase, ICharacterCreationContentHandler
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
		/// 🔴 时代无关（2026-09-10）：坐标读**当前战役**的 StartingPosition（基类按内容包覆写），
		///   不再硬编码某个时代的常量——加时代时本方法不用动。
		/// </summary>
		public override void OnCharacterCreationFinalized()
		{
			base.OnCharacterCreationFinalized();
			var taikouCampaign = Campaign.Current as TaikouCampaign;
			if (taikouCampaign != null && MobileParty.MainParty != null)
			{
				MobileParty.MainParty.Position2D = taikouCampaign.StartingPosition.GetValueOrDefault();
				// 🔴 相机拉回玩家（织丰母本同款：ShokuhoCharacterCreationContent.OnCharacterCreationFinalized 实锤）——
				// CC 完成落场时地图默认机位停在官方地图坐标（camera_top/默认），不拉回 = 开局看不见角色（2026-09-08 用户实测）。
				if (GameStateManager.Current.ActiveState is MapState mapState && mapState.Handler != null)
				{
					mapState.Handler.ResetCamera(true, true);
					mapState.Handler.TeleportCameraToMainParty();
				}
			}
		}
#else
		// ═══════════════════════════════════════════════════════════════════════════
		// 1.3.x / 1.4.x / 1.5.x 形态：**战役行为 + 建号内容处理器**（2026-09-23 接入）
		// ═══════════════════════════════════════════════════════════════════════════
		// 🔴 为什么换形态：1.3.0 起 `CharacterCreationContentBase` 整类删除，内容不再由
		//   「状态构造参数」注入。新契约（1.3.15 反编译实证）：
		//     `CharacterCreationState`（无参构造）→ 内部 new `CharacterCreationManager`
		//     → 构造期广播 `CampaignEvents.OnCharacterCreationInitializedEvent`
		//     → 监听者在回调里 `RegisterCharacterCreationContentHandler(this, 优先级)`
		//     → manager 随即逐个 handler 调 `InitializeContent`（我们在这里加文化/改阶段/换菜单）。
		//   优先级：原版内容 800 / 剧情模式 900 → **我们 1000，排最后**（必须等原版建完菜单才删得掉）。
		// 🔴 3D 界面**一行都不用写**：游戏自带 `SandBox.View.dll` 的 `CharacterCreationScreen`
		//   带 `[GameStateScreen(typeof(CharacterCreationState))]`，建号状态一激活就自动挂上，
		//   再按**阶段类型**找 view。所以本支只复用原版阶段类型、不自建 stage/view。
		//   范本：`TaleWorlds.CampaignSystem.CampaignBehaviors.CharacterCreationCampaignBehavior`（原版）、
		//        `StoryMode.GameComponents.CampaignBehaviors.StoryModeCharacterCreationCampaignBehavior`（剧情）。

		/// <summary>处理器优先级（原版 800 / 剧情 900 → 我们最后跑）。</summary>
		private const int HandlerPriority = 1000;

		/// <summary>舞台上那个玩家角色的 id（要与 <see cref="GetNarrativeMenuCharacterArgs"/> 返回的一致）。</summary>
		private const string PlayerCharacterId = "lwn_player_character";

		/// <summary>原版叙事菜单 id（1.3.15 实读）——整套删掉，换成我们自己的出身菜单。</summary>
		private static readonly string[] VanillaNarrativeMenuIds =
		{
			"narrative_parent_menu",
			"narrative_childhood_menu",
			"narrative_education_menu",
			"narrative_youth_menu",
			"narrative_adulthood_menu",
			"narrative_age_selection_menu",
		};

		private readonly string _contentPack = CampaignModeActivator.ActiveContentPack;

		// 数值口径：从 content 读（与 1.2.12 传基类 FocusToAdd/SkillLevelToAdd/AttributeLevelToAdd 等价）
		private int _focusToAdd = 1;
		private int _skillLevelToAdd = 10;
		private int _attributeLevelToAdd = 1;

		public override void RegisterEvents()
		{
			CampaignEvents.OnCharacterCreationInitializedEvent.AddNonSerializedListener(this, OnCharacterCreationInitialized);
		}

		public override void SyncData(IDataStore dataStore)
		{
		}

		/// <summary>
		/// 建号管理器刚建好（事件在它的构造期广播）→ 把自己挂成内容处理器。
		/// 🔴 只接管**内容包战役**：纯原版战役也会建 `CharacterCreationState`，
		///   我们不该去改它的建号内容（内容包模式下原版战役入口已被主菜单剔除，这里是纵深防御）。
		/// </summary>
		private void OnCharacterCreationInitialized(CharacterCreationManager manager)
		{
			if (manager == null || !(Campaign.Current is LivingWorldCampaign))
			{
				return;
			}
			manager.RegisterCharacterCreationContentHandler(this, HandlerPriority);
			DebugLogger.Log($"[LWN-cc13] 建号内容处理器已注册（priority={HandlerPriority}，包={_contentPack ?? "(无内容包)"}）");
		}

		void ICharacterCreationContentHandler.InitializeContent(CharacterCreationManager manager)
		{
			try
			{
				_focusToAdd = manager.CharacterCreationContent.FocusToAdd;
				_skillLevelToAdd = manager.CharacterCreationContent.SkillLevelToAdd;
				_attributeLevelToAdd = manager.CharacterCreationContent.AttributeLevelToAdd;

				SetupStages(manager);
				SetupCultures(manager);
				SetupMenus(manager);
				manager.CharacterCreationContent.ChangeReviewPageDescription(
					_contentPack == "Taikou"
						? new TextObject("{=LWN_charc_review_taikou}You are a samurai of the Sengoku period of Japan.")
						: new TextObject("{=LWN_charc_review}You are a traveler of a new world."));
			}
			catch (Exception ex)
			{
				// 建号内容布置失败不该让玩家卡死：记日志，剩下的交给引擎默认内容。
				DebugLogger.Log($"[LWN-cc13] 建号内容初始化 FAILED：{ex.GetType().Name} {ex.Message}\n{ex.StackTrace}");
			}
		}

		void ICharacterCreationContentHandler.AfterInitializeContent(CharacterCreationManager manager)
		{
			// 无需补做：菜单替换已在 InitializeContent 完成（优先级 1000 = 原版之后）
		}

		void ICharacterCreationContentHandler.OnStageCompleted(CharacterCreationStageBase stage)
		{
		}

		/// <summary>
		/// 建号收尾（引擎 `ApplyFinalEffects` 里调）→ 重设主队出生位置。
		/// 🔴 与 1.2.12 的 `OnCharacterCreationFinalized` 同义，但**相机不在这一步拉**：
		///   此刻 ActiveState 还是建号态（MapState 要等紧接着的 `FinalizeCharacterCreationState` 才推），
		///   拿不到 `mapState.Handler`。好在推入 MapState 时地图相机读的就是主队坐标 ——
		///   我们只要把坐标写对，相机天然对准玩家（与 1.2.12 世界创建期置位同一原理）。
		/// </summary>
		void ICharacterCreationContentHandler.OnCharacterCreationFinalize(CharacterCreationManager manager)
		{
			try
			{
				var campaign = Campaign.Current as LivingWorldCampaign;
				Vec2? spawn = campaign?.StartingPosition;
				if (spawn.HasValue)
				{
					V.SetMainPartyPosition(spawn.Value);
					DebugLogger.Log($"[LWN-cc13] 建号收尾：主队位置重设为 ({spawn.Value.X:F1},{spawn.Value.Y:F1})");
				}
			}
			catch (Exception ex)
			{
				DebugLogger.Log($"[LWN-cc13] 建号收尾置位 FAILED：{ex.Message}");
			}
		}

		/// <summary>
		/// 阶段表复刻 1.2.12：**文化 → 捏脸 → 出身(叙事) → 总览**。
		/// 引擎默认七阶段 = 文化 → 捏脸 → 叙事 → 家纹 → 家名 → 总览 → 难度；
		/// 按 2026-09-23 裁定砍掉 家纹 / 家名 / 难度 三阶，与 1.2.12 逐阶段一致。
		/// 🔴 单靠 `RemoveStage` 不动数据：`RemoveStage` 只摘阶段，被摘阶段对应的数据（菜单/文化）
		///   要不要清由我们决定 —— 菜单见 <see cref="SetupMenus"/>，文化见 <see cref="SetupCultures"/>。
		/// </summary>
		private static void SetupStages(CharacterCreationManager manager)
		{
			manager.RemoveStage<CharacterCreationBannerEditorStage>();
			manager.RemoveStage<CharacterCreationClanNamingStage>();
			manager.RemoveStage<CharacterCreationOptionsStage>();
		}

		/// <summary>
		/// 可选文化 = 全部「主文化」（`IsMainCulture`）。
		/// 🔴 这正是 1.2.12 的口径（`CharacterCreationContentBase.GetCultures()` 就是按 IsMainCulture
		///   过滤的）——照抄口径，免得两个版本给出不同的文化表。太阁包实测 = 10 个（ikoku + 9 地域）。
		/// ⚠️ 引擎默认内容（`CharacterCreationCampaignBehavior`，priority 800）只认卡拉迪亚六文化的
		///   **硬编码 id**，在内容包世界里一个都匹配不上 → 文化表本来是空的，**必须我们自己填**。
		/// </summary>
		private static void SetupCultures(CharacterCreationManager manager)
		{
			CharacterCreationContent content = manager.CharacterCreationContent;
			var all = MBObjectManager.Instance?.GetObjectTypeList<CultureObject>();
			if (all == null)
			{
				DebugLogger.Log("[LWN-cc13] 文化表构建：GetObjectTypeList<CultureObject>() 为 null → 跳过（建号将无可选文化）");
				return;
			}
			int added = 0;
			foreach (CultureObject culture in all)
			{
				if (culture != null && culture.IsMainCulture)
				{
					content.AddCharacterCreationCulture(culture, content.FocusToAdd, content.SkillLevelToAdd);
					added++;
				}
			}
			DebugLogger.Log($"[LWN-cc13] 文化表注入 {added} 个主文化（Focus+{content.FocusToAdd} / Skill+{content.SkillLevelToAdd}）");
		}

		/// <summary>把原版那套叙事菜单整体换成我们的出身菜单（只有一个菜单）。</summary>
		private void SetupMenus(CharacterCreationManager manager)
		{
			foreach (string menuId in VanillaNarrativeMenuIds)
			{
				manager.DeleteNarrativeMenuWithId(menuId);
			}
			manager.AddNewMenu(BuildOriginsMenu());
			DebugLogger.Log("[LWN-cc13] 叙事菜单已替换为 LWN 出身菜单（lwn_origins_menu）");
		}

		/// <summary>
		/// 出身菜单（复刻 1.2.12 的「Your Origins」选项集）。
		/// 🔴 菜单链语义（1.3.15 实读）：引擎从 `InputMenuId == "start"` 的菜单起步，
		///   「下一个」= 找第一个 `InputMenuId == 当前菜单.StringId` 的菜单，找不到就结束叙事阶段。
		///   我们只有一个菜单 ⇒ InputMenuId 必须是 "start"，成功时它走完自然进总览阶段。
		/// </summary>
		private NarrativeMenu BuildOriginsMenu()
		{
			bool taikou = _contentPack == "Taikou";
			var characters = new List<NarrativeMenuCharacter>();

			// 舞台上站一个玩家角色（原版/剧情模式同款做法）。逻辑上可有可无，但空角色列表 = 一块空场景。
			CharacterObject playerChar = CharacterObject.PlayerCharacter;
			if (playerChar != null)
			{
				characters.Add(new NarrativeMenuCharacter(
					PlayerCharacterId, BodyProperties.Default, playerChar.Race, playerChar.IsFemale));
			}

			var menu = new NarrativeMenu(
				"lwn_origins_menu",
				"start",
				string.Empty,
				new TextObject("{=LWN_cc_bg_title}Your Origins"),
				taikou
					? new TextObject("{=LWN_cc_bg_intro_taikou}Before your fate carried you toward the capital, you were born into...")
					: new TextObject("{=LWN_cc_bg_intro}Before you took to the road, you were born into..."),
				characters,
				GetNarrativeMenuCharacterArgs);

			if (taikou)
			{
				menu.AddNarrativeMenuOption(BuildOption("lwn_bg_retainer",
					"{=LWN_cc_bg_taikou_retainer}A Samurai Retainer",
					"{=LWN_cc_bg_taikou_retainer_desc}Your family has served the clan for generations, carrying the sword and bearing the honor of their lord.",
					DefaultSkills.OneHanded, DefaultSkills.Athletics, DefaultCharacterAttributes.Vigor));
				menu.AddNarrativeMenuOption(BuildOption("lwn_bg_merchant",
					"{=LWN_cc_bg_taikou_merchant}A Merchant's Child",
					"{=LWN_cc_bg_taikou_merchant_desc}You grew up counting coins in a town by the river; the ledger and the road are your inheritance.",
					DefaultSkills.Trade, DefaultSkills.Charm, DefaultCharacterAttributes.Social));
				menu.AddNarrativeMenuOption(BuildOption("lwn_bg_ronin",
					"{=LWN_cc_bg_taikou_ronin}A Wandering Ronin",
					"{=LWN_cc_bg_taikou_ronin_desc}You lost your master and your purpose, so you drift between the provinces, taking work where it is offered.",
					DefaultSkills.Roguery, DefaultSkills.Bow, DefaultCharacterAttributes.Cunning));
			}
			else
			{
				menu.AddNarrativeMenuOption(BuildOption("lwn_bg_soldier",
					"{=LWN_cc_bg_soldier}A Soldier",
					"{=LWN_cc_bg_soldier_desc}You served in several wars and learned to survive on the march and in the melee.",
					DefaultSkills.OneHanded, DefaultSkills.Athletics, DefaultCharacterAttributes.Vigor));
				menu.AddNarrativeMenuOption(BuildOption("lwn_bg_trader",
					"{=LWN_cc_bg_trader}A Merchant",
					"{=LWN_cc_bg_trader_desc}You carried goods along the trade roads and learned to sell a word as well as a ware.",
					DefaultSkills.Trade, DefaultSkills.Charm, DefaultCharacterAttributes.Social));
			}
			return menu;
		}

		/// <summary>
		/// 菜单切换时刷新舞台角色的装备/动作/出生点（引擎每换一个菜单调一次）。
		/// ⚠️ 委托**不能传 null** —— `CharacterCreationManager.ModifyMenuCharacters()` 是直接调它，
		///   没有空判（1.3.15 实读）→ 传 null 当场 NRE。
		/// 装备 id 用引擎自带的默认名册 `player_char_creation_default`：管理器找不到名册时的兜底也是它，
		/// 所以这个名字一定存在（EquipmentRosters 段我们在 `LivingWorldCampaign.OnInitialize` 补载过）。
		/// </summary>
		private List<NarrativeMenuCharacterArgs> GetNarrativeMenuCharacterArgs(
			CultureObject culture, string occupationType, CharacterCreationManager manager)
		{
			var list = new List<NarrativeMenuCharacterArgs>();
			CharacterObject playerChar = CharacterObject.PlayerCharacter;
			if (playerChar == null)
			{
				return list;
			}
			list.Add(new NarrativeMenuCharacterArgs(
				PlayerCharacterId,
				(int)playerChar.Age,
				"player_char_creation_default",
				"act_inventory_idle_start",
				"spawnpoint_player_1"));
			return list;
		}

		/// <summary>
		/// 造一个出身选项，效果与 1.2.12 的 `AddBackgroundOption` 等价：
		/// 两个技能各 +focus/+level、一个属性 +1。数值走 content 自己的三个 Add 值。
		/// 条件/选中/离开三个回调都传 null（引擎对它们都有空判），效果全在 args 委托里。
		/// </summary>
		private NarrativeMenuOption BuildOption(string id, string titleText, string descriptionText,
			SkillObject skill1, SkillObject skill2, CharacterAttribute attribute)
		{
			int focus = _focusToAdd;
			int level = _skillLevelToAdd;
			int attr = _attributeLevelToAdd;
			return new NarrativeMenuOption(
				id,
				new TextObject(titleText),
				new TextObject(descriptionText),
				args =>
				{
					args.SetAffectedSkills(new[] { skill1, skill2 });
					args.SetFocusToSkills(focus);
					args.SetLevelToSkills(level);
					args.SetLevelToAttribute(attribute, attr);
				},
				null,
				null,
				null);
		}
#endif

		/// <summary>
		/// 把建号内容挂到战役上（内容包模式才挂）。**版本分叉收在这一个方法里**，
		/// 调用方（MySubModule.OnGameStart）不必知道版本。
		///
		/// · 1.2.12：本类是 `CharacterCreationContentBase`、**不是**战役行为 —— 内容由
		///   `LivingWorldCampaignGameManager.PushCharacterCreation()` 当状态构造参数带进去，这里什么都不做。
		/// · 1.3.x+：本类是战役行为，必须在这里注册，才能在 `CharacterCreationState` 建起来时
		///   收到 `OnCharacterCreationInitializedEvent`（详见文件下半部分）。
		/// </summary>
		public static void RegisterIfNeeded(CampaignGameStarter starter)
		{
#if MB2_V1212
			// 1.2.12 无需注册（见上）
#else
			if (starter == null || CampaignModeActivator.ActiveContentPack == null)
			{
				return;
			}
			starter.AddBehavior(new LivingWorldCharacterCreationContent());
			DebugLogger.Log("[LWN-cc13] 建号内容行为已挂入战役（等待建号状态创建时接管内容）");
#endif
		}
	}
}
