using System;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 角色详情页 ViewModel（配 <c>GUI/Prefabs/HeroDetail.xml</c>）。
	/// 复刻太阁5「选择角色 → 角色详情」那一步：**先看人，再决定**。
	///
	/// 三块数据面板：
	///   ① 基本情报 —— 读**游戏内实时数据**（身份/所属/据点/名声/装备），永远有
	///   ② 能力情报 —— 五维，读画像表（<see cref="HeroProfileRegistry"/>）
	///   ③ 技能情报 —— 16 项，同源
	/// 画像表查无此人（占位期英雄 / 未进全量数据）→ ② ③ 显示「暂无史料」占位，① 照常。
	///
	/// 🔴 [决定] 才真正开局——开局那段代码一行不动，本页只负责把它往后挪一步（防误触）。
	/// </summary>
	public class HeroDetailVM : ViewModel
	{
		/// <summary>能力条的一项（五维）。</summary>
		public class AbilityItemVM : ViewModel
		{
			/// <summary>能力条底槽宽度（px，与 prefab 里的槽宽一致；填充 = Value/100 × 此值）。</summary>
			public const int SlotWidth = 150;

			internal AbilityItemVM(string labelRaw, int value)
			{
				Name = new TextObject(labelRaw).ToString();
				Value = value;
				ValueText = value.ToString();
			}

			[DataSourceProperty] public string Name { get; }
			[DataSourceProperty] public string ValueText { get; }
			[DataSourceProperty] public int Value { get; }

			/// <summary>填充宽度（px）。值 0 = 空槽（引擎对 0 宽 widget 不出错，照常给）。
			/// 🔴 类型必须是 float——Gauntlet 的 SuggestedWidth 是 float 依赖属性，int 绑定可能被静默丢弃。</summary>
			[DataSourceProperty] public float FillWidth => Math.Max(0, Math.Min(100, Value)) * SlotWidth / 100f;
		}

		/// <summary>技能项（16 项之一）：档位点阵 + 数值。</summary>
		public class SkillItemVM : ViewModel
		{
			/// <summary>档位点数（太阁5 是星级观感，这里用 6 点表示 0–100）。</summary>
			private const int DotCount = 6;

			internal SkillItemVM(string labelRaw, int value)
			{
				Name = new TextObject(labelRaw).ToString();
				Value = value;
				ValueText = value.ToString();
			}

			[DataSourceProperty] public string Name { get; }
			[DataSourceProperty] public string ValueText { get; }
			[DataSourceProperty] public int Value { get; }

			/// <summary>档位点阵（实心 ● / 空心 ○，共 <see cref="DotCount"/> 个）。</summary>
			[DataSourceProperty]
			public string Dots
			{
				get
				{
					int filled = (int)Math.Round(Math.Max(0, Math.Min(100, Value)) * DotCount / 100.0);
					var sb = new StringBuilder(DotCount);
					for (int i = 0; i < DotCount; i++)
					{
						sb.Append(i < filled ? '●' : '○');	// ● / ○（BMP 内字符，安全）
					}
					return sb.ToString();
				}
			}
		}

		/// <summary>基本情报的一行（标签 + 值）。</summary>
		public class InfoRowVM : ViewModel
		{
			internal InfoRowVM(string label, string value)
			{
				Label = label;
				Value = value;
			}

			[DataSourceProperty] public string Label { get; }
			[DataSourceProperty] public string Value { get; }
		}

		private readonly Action _onBack;
		private readonly Action _onConfirm;

		public HeroDetailVM(Hero hero, Action onBack, Action onConfirm)
		{
			_onBack = onBack;
			_onConfirm = onConfirm;

			Abilities = new MBBindingList<AbilityItemVM>();
			SkillsLeft = new MBBindingList<SkillItemVM>();
			SkillsRight = new MBBindingList<SkillItemVM>();
			BasicRows = new MBBindingList<InfoRowVM>();

			if (hero == null)
			{
				return;
			}
			Hero = hero;

			// ── ① 基本情报（游戏内实时数据；**只列有值的行**——没有的字段不摆空行糊弄）──
			NameText = HeroSelectData.GetDisplayName(hero);
			AddRow(IdentityLabel, HeroSelectData.GetHeroRoleText(hero));
			AddRow(AllegianceLabel, Safe(() => hero.Clan?.Name?.ToString()));
			AddRow(SeatLabel, ResolveSeat(hero));
			AddRow(RenownLabel, Safe(() => ((int)(hero.Clan?.Renown ?? 0f)).ToString()));
			AddRow(WeaponLabel, ResolveEquipment(hero, EquipmentIndex.Weapon0));
			AddRow(ArmorLabel, ResolveEquipment(hero, EquipmentIndex.Body));

			// ── ② ③ 画像表（内容包数据；查无此人 = 占位）──
			HeroProfileRegistry.Profile profile = HeroProfileRegistry.GetProfile(hero.StringId);
			HasProfile = profile != null;

			// 生卒：画像表没有就用游戏内的年龄兜底（**不编造生年**）
			// 🔴 年龄 = 时代年份 − 生年（史实口径）。**不能用 `CampaignTime.Now.GetYear`**——
			//   那个是"开局以来经过的年数"，全新存档为 0（见 EraCatalog.SelectedEraYear 注释）。
			if (profile != null && profile.HasLifespan)
			{
				int eraYear = EraCatalog.SelectedEraYear;
				int age = eraYear > 0 ? eraYear - profile.Birth : (int)hero.Age;
				AgeText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
					.SetTextVariable("AGE", age.ToString()).ToString();
				LifespanText = $"{profile.Birth} – {profile.Die}";
			}
			else
			{
				AgeText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
					.SetTextVariable("AGE", ((int)hero.Age).ToString()).ToString();
				LifespanText = string.Empty;
			}

			// 立绘：画像表与立绘表**同键**，有画像基本就有立绘；查不到 = 界面画占位框
			BustupSprite = HeroProfileRegistry.GetBustupSpriteName(hero.StringId) ?? string.Empty;
			HasBustup = !string.IsNullOrEmpty(BustupSprite);

			// 型别 / 目标描述：仅「推荐」人配了（普通人物留空 → 界面隐藏那一行）
			HeroProfileRegistry.Recommendation rec = FindRecommendation(hero.StringId);
			StoryType = rec != null ? new TextObject(rec.StoryTypeRaw).ToString() : string.Empty;
			StoryGoal = rec != null ? new TextObject(rec.StoryGoalRaw).ToString() : string.Empty;
			HasStory = !string.IsNullOrEmpty(StoryType) || !string.IsNullOrEmpty(StoryGoal);

			if (profile != null)
			{
				// 标签由**内容包**给（铁律 3：LWN 不认识「统率/足轻」这类具体词汇）——
				// 读到的就是 {=KEY}fallback 原样串，交给 TextObject 走引擎本地化。
				string[] dims = HeroProfileRegistry.DimensionLabels;
				Abilities.Add(new AbilityItemVM(dims[0], profile.Command));
				Abilities.Add(new AbilityItemVM(dims[1], profile.Force));
				Abilities.Add(new AbilityItemVM(dims[2], profile.Govern));
				Abilities.Add(new AbilityItemVM(dims[3], profile.Wisdom));
				Abilities.Add(new AbilityItemVM(dims[4], profile.Charm));

				string[] skillLabels = HeroProfileRegistry.SkillLabels;
				// 16 项拆左右两列（各 8 项）——太阁5 原版就是两列，prefab 用两个 ListPanel 并排
				for (int i = 0; i < HeroProfileRegistry.SkillCount; i++)
				{
					var item = new SkillItemVM(skillLabels[i], profile.Skills[i]);
					if (i < HeroProfileRegistry.SkillCount / 2)
					{
						SkillsLeft.Add(item);
					}
					else
					{
						SkillsRight.Add(item);
					}
				}
			}
		}

		internal Hero Hero { get; }

		// ── 文本 ──
		[DataSourceProperty] public string NameText { get; private set; } = string.Empty;
		[DataSourceProperty] public string AgeText { get; private set; } = string.Empty;
		[DataSourceProperty] public string LifespanText { get; private set; } = string.Empty;
		[DataSourceProperty] public string StoryType { get; private set; } = string.Empty;
		[DataSourceProperty] public string StoryGoal { get; private set; } = string.Empty;
		[DataSourceProperty] public string BustupSprite { get; private set; } = string.Empty;

		/// <summary>基本情报各行的「标签 + 值」（空值的行不入列）。</summary>
		[DataSourceProperty] public MBBindingList<InfoRowVM> BasicRows { get; }

		/// <summary>
		/// 列传正文。🔴 **太阁5 的列传不在 TaikouHero.csv 里**（129 列无此字段），
		/// 在 tg5msg 文本包里（已破解 XOR A5，见 Knowledge/太阁5/太阁5_破解总索引.md）——
		/// 抽出来之前一律显示「暂无史料」占位，**不编造**。
		/// </summary>
		[DataSourceProperty] public string BiographyText => NoRecordText;

		/// <summary>是否查到了画像（false = 太阁专属面板显示「暂无史料」）。</summary>
		[DataSourceProperty] public bool HasProfile { get; private set; }

		/// <summary>反过来：是否要显示「暂无史料」提示。</summary>
		[DataSourceProperty] public bool HasNoRecord => !HasProfile;

		[DataSourceProperty] public bool HasBustup { get; private set; }

		/// <summary>没立绘时画空白底板（不画 ImageWidget——空 sprite 的图片控件行为不可靠）。</summary>
		[DataSourceProperty] public bool HasNoBustup => !HasBustup;

		/// <summary>是否显示「型别 / 目标描述」（推荐人专属）。</summary>
		[DataSourceProperty] public bool HasStory { get; private set; }

		/// <summary>是否显示生卒行。</summary>
		[DataSourceProperty] public bool HasLifespan => !string.IsNullOrEmpty(LifespanText);

		[DataSourceProperty] public MBBindingList<AbilityItemVM> Abilities { get; }

		/// <summary>技能左列（前 8 项）。</summary>
		[DataSourceProperty] public MBBindingList<SkillItemVM> SkillsLeft { get; }

		/// <summary>技能右列（后 8 项）。</summary>
		[DataSourceProperty] public MBBindingList<SkillItemVM> SkillsRight { get; }

		// ── 固定文案（本地化键，铁律 13）──
		[DataSourceProperty] public string BasicHeader => new TextObject("{=LWN_hero_detail_basic}Profile").ToString();
		[DataSourceProperty] public string AbilityHeader => new TextObject("{=LWN_hero_detail_ability}Attributes").ToString();
		[DataSourceProperty] public string SkillHeader => new TextObject("{=LWN_hero_detail_skills}Skills").ToString();
		[DataSourceProperty] public string IdentityLabel => new TextObject("{=LWN_hero_detail_identity}Station").ToString();
		[DataSourceProperty] public string AllegianceLabel => new TextObject("{=LWN_hero_detail_allegiance}Allegiance").ToString();
		[DataSourceProperty] public string SeatLabel => new TextObject("{=LWN_hero_detail_seat}Seat").ToString();
		[DataSourceProperty] public string RenownLabel => new TextObject("{=LWN_hero_detail_renown}Renown").ToString();
		[DataSourceProperty] public string WeaponLabel => new TextObject("{=LWN_hero_detail_weapon}Weapon").ToString();
		[DataSourceProperty] public string ArmorLabel => new TextObject("{=LWN_hero_detail_armor}Armour").ToString();
		[DataSourceProperty] public string BioHeader => new TextObject("{=LWN_hero_detail_biography}Biography").ToString();
		[DataSourceProperty] public string NoRecordText => new TextObject("{=LWN_hero_detail_no_record}No record of this one survives.").ToString();
		[DataSourceProperty] public string BackText => new TextObject("{=LWN_hero_detail_back}Back").ToString();
		[DataSourceProperty] public string ConfirmText => new TextObject("{=LWN_hero_detail_confirm}Decide").ToString();

		/// <summary>[返回] → 回选人列表（不落地）。</summary>
		public void ExecuteBack()
		{
			_onBack?.Invoke();
		}

		/// <summary>[决定] → 用这个人开局（走原有魂穿落地，一行不动）。</summary>
		public void ExecuteConfirm()
		{
			_onConfirm?.Invoke();
		}

		public override void RefreshValues()
		{
			base.RefreshValues();
			OnPropertyChanged(nameof(BasicHeader));
			OnPropertyChanged(nameof(AbilityHeader));
			OnPropertyChanged(nameof(SkillHeader));
			OnPropertyChanged(nameof(NoRecordText));
			OnPropertyChanged(nameof(BackText));
			OnPropertyChanged(nameof(ConfirmText));
		}

		// ───────────────────────── 取值助手（全部 null 安全）─────────────────────────

		/// <summary>加一行基本情报；**值为空就不加**（列不出来 = 数据确实没有，不摆空行）。</summary>
		private void AddRow(string label, string value)
		{
			if (!string.IsNullOrEmpty(value))
			{
				BasicRows.Add(new InfoRowVM(label, value));
			}
		}

		private static HeroProfileRegistry.Recommendation FindRecommendation(string heroId)
		{
			foreach (HeroProfileRegistry.Recommendation r in HeroProfileRegistry.Recommendations)
			{
				if (r.HeroId == heroId)
				{
					return r;
				}
			}
			return null;
		}

		private static string Safe(Func<string> get)
		{
			try
			{
				return get() ?? string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

		/// <summary>「据点」= 家族首府 → 英雄当前所在定居点 → 空（不编造）。</summary>
		private static string ResolveSeat(Hero hero)
		{
			try
			{
				if (hero.Clan != null)
				{
					foreach (Town t in hero.Clan.Fiefs)
					{
						if (t?.Settlement?.Name != null)
						{
							return t.Settlement.Name.ToString();
						}
					}
				}
				return hero.CurrentSettlement?.Name?.ToString() ?? string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}

		/// <summary>按装备槽取物品名（空槽 = 空串，界面隐藏该行）。</summary>
		private static string ResolveEquipment(Hero hero, EquipmentIndex slot)
		{
			try
			{
				ItemObject item = hero.BattleEquipment?[slot].Item;
				return item?.Name?.ToString() ?? string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}
	}
}
