using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面 ViewModel（配 <c>GUI/Prefabs/HeroSelect.xml</c>）。两种罗列模式：
	///   · **树模式**（默认）：选王国 → 刷新家族列 → 选家族 → 刷新英雄列
	///   · **推荐模式**：只列「推荐」五人（内容包配的名单，见 HeroProfileRegistry.Recommendations）
	///
	/// 🔴 **点人名不再直接开局**（2026-09-11 复刻太阁5 两步流程）：
	///   点行 → 开**角色详情页**（HeroDetailVM）→ 详情页 [决定] 才真正魂穿。
	///   故本 VM 不再有「确认」按钮——它的职责移交给详情页的 [决定]。
	/// 另有并行路：「自定义英雄」按钮 → 走现有建号流程（本界面不参与）。
	/// </summary>
	public class HeroSelectVM : ViewModel
	{
		/// <summary>左侧王国项。</summary>
		public class KingdomItemVM : ViewModel
		{
			internal Kingdom Kingdom { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public KingdomItemVM(Kingdom kingdom, HeroSelectVM owner)
			{
				Kingdom = kingdom;
				_owner = owner;
			}

			[DataSourceProperty]
			public string Name => Kingdom?.Name?.ToString() ?? "?";

			[DataSourceProperty]
			public bool IsSelected
			{
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						OnPropertyChangedWithValue(value, nameof(IsSelected));
						OnPropertyChanged(nameof(TextColor));
					}
				}
			}

			[DataSourceProperty]
			public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.SelectKingdom(this);
		}

		/// <summary>中间家族项。</summary>
		public class ClanItemVM : ViewModel
		{
			internal Clan Clan { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public ClanItemVM(Clan clan, HeroSelectVM owner)
			{
				Clan = clan;
				_owner = owner;
			}

			[DataSourceProperty]
			public string Name => Clan?.Name?.ToString() ?? "?";

			[DataSourceProperty]
			public bool IsSelected
			{
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						OnPropertyChangedWithValue(value, nameof(IsSelected));
						OnPropertyChanged(nameof(TextColor));
					}
				}
			}

			[DataSourceProperty]
			public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.SelectClan(this);
		}

		/// <summary>右侧英雄项（树模式）。点它 = 开详情页，不直接开局。</summary>
		public class HeroItemVM : ViewModel
		{
			internal Hero Hero { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public HeroItemVM(Hero hero, HeroSelectVM owner)
			{
				Hero = hero;
				_owner = owner;
			}

			[DataSourceProperty]
			public string Name => HeroSelectData.GetDisplayName(Hero);

			[DataSourceProperty]
			public string Role => HeroSelectData.GetHeroRoleText(Hero);

			/// <summary>行内小头像 sprite 名（无 = 空串，界面画占位）。</summary>
			[DataSourceProperty]
			public string MiniSprite => HeroProfileRegistry.GetMiniheadSpriteName(Hero?.StringId) ?? string.Empty;

			[DataSourceProperty]
			public bool HasMini => !string.IsNullOrEmpty(MiniSprite);

			[DataSourceProperty]
			public bool IsSelected
			{
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						OnPropertyChangedWithValue(value, nameof(IsSelected));
						OnPropertyChanged(nameof(TextColor));
					}
				}
			}

			[DataSourceProperty]
			public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.OnHeroClicked(Hero);
		}

		/// <summary>推荐人列表项（推荐模式）——头像 + 姓名/年龄 + 型别 + 目标描述。</summary>
		public class RecommendedItemVM : ViewModel
		{
			internal Hero Hero { get; }

			/// <summary>名单里的英雄 id（世界里查不到人时 Hero 为 null，靠它取立绘/占位）。</summary>
			internal string ProfileId { get; }

			private readonly HeroSelectVM _owner;
			private bool _isSelected;

			internal RecommendedItemVM(string profileId, Hero hero, string name, string ageText,
				string storyType, string storyGoal, bool missing, HeroSelectVM owner)
			{
				ProfileId = profileId;
				Hero = hero;
				Name = name;
				AgeText = ageText;
				StoryType = storyType;
				StoryGoal = storyGoal;
				IsMissing = missing;
				_owner = owner;
			}

			[DataSourceProperty] public string Name { get; }

			/// <summary>「25 years old」式年龄小字（数据没有 = 空串）。</summary>
			[DataSourceProperty] public string AgeText { get; }

			/// <summary>型别标签（如「武士型故事」）——推荐人专属。</summary>
			[DataSourceProperty] public string StoryType { get; }

			/// <summary>目标描述（两行小字）。</summary>
			[DataSourceProperty] public string StoryGoal { get; }

			/// <summary>true = 这个人还没在世界里（占位期/未进全量数据）→ 灰显且点了没反应。</summary>
			[DataSourceProperty] public bool IsMissing { get; }

			[DataSourceProperty] public bool IsAvailable => !IsMissing;

			[DataSourceProperty]
			public string MiniSprite => HeroProfileRegistry.GetMiniheadSpriteName(ProfileId) ?? string.Empty;

			[DataSourceProperty] public bool HasMini => !string.IsNullOrEmpty(MiniSprite);

			[DataSourceProperty]
			public bool IsSelected
			{
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						OnPropertyChangedWithValue(value, nameof(IsSelected));
						OnPropertyChanged(nameof(TextColor));
					}
				}
			}

			[DataSourceProperty]
			public string TextColor => IsMissing ? "#7A7A8AFF" : (IsSelected ? "#F0CE7AFF" : "#F2F2F2FF");

			public void ExecutePick() => _owner.OnRecommendedClicked(this);
		}

		private readonly Action _onBack;
		private readonly Action<Hero> _onHeroChosen;      // → 开详情页（不是开局）
		private readonly Action _onCustomHero;

		public HeroSelectVM(Action onBack, Action<Hero> onHeroChosen, Action onCustomHero, bool recommended = false)
		{
			_onBack = onBack;
			_onHeroChosen = onHeroChosen;
			_onCustomHero = onCustomHero;
			IsRecommendedMode = recommended;

			Kingdoms = new MBBindingList<KingdomItemVM>();
			Clans = new MBBindingList<ClanItemVM>();
			Heroes = new MBBindingList<HeroItemVM>();
			RecommendedItems = new MBBindingList<RecommendedItemVM>();

			if (recommended)
			{
				BuildRecommended();
			}
			else
			{
				foreach (Kingdom k in HeroSelectData.GetSelectableKingdoms())
				{
					Kingdoms.Add(new KingdomItemVM(k, this));
				}
				if (Kingdoms.Count > 0)
				{
					SelectKingdom(Kingdoms[0]);      // 默认选第一个王国，界面一打开就有内容
				}
			}
			RefreshValues();
		}

		// ── 列表属性（🔴 单实例，构造里 new 一次）──
		[DataSourceProperty] public MBBindingList<KingdomItemVM> Kingdoms { get; }
		[DataSourceProperty] public MBBindingList<ClanItemVM> Clans { get; }
		[DataSourceProperty] public MBBindingList<HeroItemVM> Heroes { get; }

		/// <summary>推荐人列表（推荐模式用）。</summary>
		[DataSourceProperty] public MBBindingList<RecommendedItemVM> RecommendedItems { get; }

		/// <summary>true = 推荐模式（只列推荐五人）→ prefab 隐藏三列树、显示推荐列。</summary>
		[DataSourceProperty] public bool IsRecommendedMode { get; }

		/// <summary>反过来：树模式（prefab 里布尔取反没有语法，故两边都给）。</summary>
		[DataSourceProperty] public bool IsTreeMode => !IsRecommendedMode;

		[DataSourceProperty] public string TitleText => IsRecommendedMode
			? new TextObject("{=LWN_hero_select_title_recommended}Recommended Lords").ToString()
			: new TextObject("{=LWN_hero_select_title}Choose Your Lord").ToString();
		[DataSourceProperty] public string BackText => new TextObject("{=LWN_hero_select_back}Back").ToString();
		[DataSourceProperty] public string CustomHeroText => new TextObject("{=LWN_hero_select_custom}Custom Hero").ToString();
		[DataSourceProperty] public string KingdomHeader => new TextObject("{=LWN_hero_select_kingdom}Realm").ToString();
		[DataSourceProperty] public string ClanHeader => new TextObject("{=LWN_hero_select_clan}House").ToString();
		[DataSourceProperty] public string HeroHeader => new TextObject("{=LWN_hero_select_hero}Lords").ToString();

		/// <summary>点英雄行 → 交给覆盖层开详情页（**不在这里落地**）。</summary>
		internal void OnHeroClicked(Hero hero)
		{
			if (hero == null)
			{
				return;
			}
			foreach (HeroItemVM h in Heroes)
			{
				h.IsSelected = h.Hero == hero;
			}
			_onHeroChosen?.Invoke(hero);
		}

		/// <summary>点推荐人行 → 同上；未登场的人点了没反应。</summary>
		internal void OnRecommendedClicked(RecommendedItemVM item)
		{
			if (item == null || item.IsMissing || item.Hero == null)
			{
				return;
			}
			foreach (RecommendedItemVM r in RecommendedItems)
			{
				r.IsSelected = r == item;
			}
			_onHeroChosen?.Invoke(item.Hero);
		}

		/// <summary>
		/// 用内容包的「推荐」名单（<see cref="HeroProfileRegistry.Recommendations"/>）铺列表。
		/// 🔴 名单里的 id 要在**世界里真找得到人**才能选——找不到就灰显「未登场」
		///   （占位期 / 全量数据未接入时的正常情况；T4 到位后自然全亮）。
		/// 年龄按 **开局年 − 生年** 现算（不抄太阁5 截图的数字）。
		/// </summary>
		private void BuildRecommended()
		{
			foreach (HeroProfileRegistry.Recommendation rec in HeroProfileRegistry.Recommendations)
			{
				Hero hero = HeroSelectData.FindHero(rec.HeroId);
				bool missing = hero == null || !HeroSelectData.IsSelectable(hero);

				HeroProfileRegistry.Profile profile = HeroProfileRegistry.GetProfile(rec.HeroId);
				string name = hero != null
					? HeroSelectData.GetDisplayName(hero)
					: new TextObject("{=LWN_hero_select_not_arrived}Not yet in this world").ToString();

				string ageText = string.Empty;
				if (profile != null && profile.HasLifespan)
				{
					// 年龄 = 时代年份 − 生年（史实口径；**不是** CampaignTime.Now.GetYear，
					// 那个是"开局以来经过的年数"，见 EraCatalog.SelectedEraYear 注释）
					int eraYear = EraCatalog.SelectedEraYear;
					int age = eraYear > 0 ? eraYear - profile.Birth : (int)(hero?.Age ?? 0f);
					ageText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
						.SetTextVariable("AGE", age.ToString()).ToString();
				}
				else if (hero != null)
				{
					ageText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
						.SetTextVariable("AGE", ((int)hero.Age).ToString()).ToString();
				}

				var item = new RecommendedItemVM(
					rec.HeroId, hero, name, ageText,
					new TextObject(rec.StoryTypeRaw).ToString(),
					new TextObject(rec.StoryGoalRaw).ToString(),
					missing, this);
				RecommendedItems.Add(item);
			}
		}

		/// <summary>点王国 → 刷新家族列（并自动选第一个家族，省一次点击）。</summary>
		internal void SelectKingdom(KingdomItemVM item)
		{
			if (item == null)
			{
				return;
			}
			foreach (KingdomItemVM k in Kingdoms)
			{
				k.IsSelected = k == item;
			}
			Clans.Clear();
			foreach (Clan c in HeroSelectData.GetSelectableClans(item.Kingdom))
			{
				Clans.Add(new ClanItemVM(c, this));
			}
			SelectClan(Clans.Count > 0 ? Clans[0] : null);
		}

		/// <summary>点家族 → 刷新英雄列（同样自动选第一个，让行高亮直接落在人身上）。</summary>
		internal void SelectClan(ClanItemVM item)
		{
			foreach (ClanItemVM c in Clans)
			{
				c.IsSelected = c == item;
			}
			Heroes.Clear();
			if (item != null)
			{
				foreach (Hero h in HeroSelectData.GetSelectableHeroes(item.Clan))
				{
					Heroes.Add(new HeroItemVM(h, this));
				}
			}
			// 只做高亮，**不开详情页**（换列不该把人推进下一步）
			foreach (HeroItemVM h in Heroes)
			{
				h.IsSelected = h == (Heroes.Count > 0 ? Heroes[0] : null);
			}
		}

		/// <summary>「自定义英雄」→ 走现有建号流程。</summary>
		public void ExecuteCustomHero()
		{
			_onCustomHero?.Invoke();
		}

		/// <summary>返回上一层（回选剧本）。</summary>
		public void ExecuteBack()
		{
			_onBack?.Invoke();
		}

		public override void RefreshValues()
		{
			base.RefreshValues();
			OnPropertyChanged(nameof(TitleText));
			OnPropertyChanged(nameof(BackText));
			OnPropertyChanged(nameof(CustomHeroText));
			OnPropertyChanged(nameof(KingdomHeader));
			OnPropertyChanged(nameof(ClanHeader));
			OnPropertyChanged(nameof(HeroHeader));
			OnPropertyChanged(nameof(IsRecommendedMode));
			OnPropertyChanged(nameof(IsTreeMode));
		}
	}
}
