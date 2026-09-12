using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面 ViewModel（配 <c>GUI/Prefabs/HeroSelect.xml</c>）。两种罗列模式：
	///   · **树模式**（默认）：选王国 → 刷新家族列 → 选家族 → 刷新领主列
	///   · **推荐模式**：只列「推荐」五人（内容包配的名单，见 HeroProfileRegistry.Recommendations）
	///
	/// 🔴 **数据源 = 选人目录（静态）**，不是活世界（2026-09-11 改造）——
	///   所以本界面可以在**建世界之前**（长 loading 之前）显示。见 <see cref="HeroSelectData"/>。
	/// 🔴 **点人名不再直接开局**：点行 → 开**角色详情页**（HeroDetailVM）→ 详情页 [决定] 才真正魂穿。
	///   故本 VM 没有「确认」按钮——它的职责移交给详情页的 [决定]。
	/// </summary>
	public class HeroSelectVM : ViewModel
	{
		/// <summary>最左列的一个身份筛档（第一档永远是「全部」，之后是内容包配的各档）。</summary>
		public class FilterItemVM : ViewModel
		{
			/// <summary>筛档 id（空串 = 「全部」）；与目录里 Realm/House 的 <c>type</c> 同一套取值。</summary>
			internal string TypeKey { get; }

			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			internal FilterItemVM(string typeKey, string name, HeroSelectVM owner)
			{
				TypeKey = typeKey;
				Name = name;
				_owner = owner;
			}

			[DataSourceProperty] public string Name { get; }

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

			[DataSourceProperty] public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.SelectFilter(this);
		}

		/// <summary>左侧王国项（含「无所属」那一档）。</summary>
		public class RealmItemVM : ViewModel
		{
			internal HeroCatalogRegistry.Realm Realm { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public RealmItemVM(HeroCatalogRegistry.Realm realm, HeroSelectVM owner)
			{
				Realm = realm;
				_owner = owner;
			}

			[DataSourceProperty] public string Name => HeroSelectData.Resolve(Realm?.NameRaw);

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

			[DataSourceProperty] public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.SelectRealm(this);
		}

		/// <summary>中间家族项。</summary>
		public class HouseItemVM : ViewModel
		{
			internal HeroCatalogRegistry.House House { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public HouseItemVM(HeroCatalogRegistry.House house, HeroSelectVM owner)
			{
				House = house;
				_owner = owner;
			}

			[DataSourceProperty] public string Name => HeroSelectData.Resolve(House?.NameRaw);

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

			[DataSourceProperty] public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.SelectHouse(this);
		}

		/// <summary>右侧领主项。点它 = 开详情页，不直接开局。</summary>
		public class LordItemVM : ViewModel
		{
			internal HeroCatalogRegistry.Lord Lord { get; }
			private bool _isSelected;
			private readonly HeroSelectVM _owner;

			public LordItemVM(HeroCatalogRegistry.Lord lord, HeroSelectVM owner)
			{
				Lord = lord;
				_owner = owner;
				// 🔴 显示点：名字 → Sprite 对象（顺带按需加载纹理；名字串绑给 Sprite= 是静默无效的）
				string miniName = !string.IsNullOrEmpty(lord?.MiniSprite)
					? lord.MiniSprite
					: HeroProfileRegistry.GetMiniheadSpriteName(lord?.Id);
				MiniSprite = HeroProfileRegistry.LoadPortraitSprite(miniName);
			}

			[DataSourceProperty] public string Name => HeroSelectData.Resolve(Lord?.NameRaw);

			/// <summary>身份（太阁5 原文，如「足轻组头」）；数据没有 = 空串。</summary>
			[DataSourceProperty] public string Role => HeroSelectData.Resolve(Lord?.IdentityRaw);

			/// <summary>
			/// 行内小头像（**目录按时代挑好的那张卡**；目录没给 = 回落立绘表首张）。
			/// 🔴 类型必须是 <see cref="Sprite"/> **对象**、不能是名字串——绑名字给 `Sprite=` 在 VM 绑定
			/// 路径下静默无效（引擎只对 prefab 字面量做名字解析）→ 立绘/头像永远空白。
			/// 构造时算一次（GetOrLoad 顺手把纹理按需加载进显存）。
			/// </summary>
			[DataSourceProperty] public Sprite MiniSprite { get; }

			[DataSourceProperty] public bool HasMini => MiniSprite != null;

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

			[DataSourceProperty] public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			public void ExecutePick() => _owner.OnLordClicked(Lord);
		}

		/// <summary>推荐人列表项（推荐模式）——头像 + 姓名/年龄 + 型别 + 目标描述。</summary>
		public class RecommendedItemVM : ViewModel
		{
			internal string HeroId { get; }
			private readonly HeroSelectVM _owner;
			private bool _isSelected;

			internal RecommendedItemVM(string heroId, string name, string ageText, string storyType,
				string storyGoal, bool missing, string miniSprite, HeroSelectVM owner)
			{
				HeroId = heroId;
				_miniSprite = miniSprite;
				Name = name;
				AgeText = ageText;
				StoryType = storyType;
				StoryGoal = storyGoal;
				IsMissing = missing;
				_owner = owner;
				// 🔴 显示点：名字 → Sprite 对象（顺带按需加载纹理）
				MiniSprite = HeroProfileRegistry.LoadPortraitSprite(
					!string.IsNullOrEmpty(_miniSprite) ? _miniSprite : HeroProfileRegistry.GetMiniheadSpriteName(heroId));
			}

			[DataSourceProperty] public string Name { get; }

			/// <summary>「25 years old」式年龄小字（数据没有 = 空串）。</summary>
			[DataSourceProperty] public string AgeText { get; }

			/// <summary>型别标签（如「武士型故事」）——推荐人专属。</summary>
			[DataSourceProperty] public string StoryType { get; }

			/// <summary>目标描述（两行小字）。</summary>
			[DataSourceProperty] public string StoryGoal { get; }

			/// <summary>true = 该时代目录里没有这个人（占位期/未进全量数据）→ 灰显且点了没反应。</summary>
			[DataSourceProperty] public bool IsMissing { get; }

			private readonly string _miniSprite;

			/// <summary>头像（`Sprite` 对象——见 <see cref="LordItemVM.MiniSprite"/> 的类型说明）。</summary>
			[DataSourceProperty] public Sprite MiniSprite { get; }

			[DataSourceProperty] public bool HasMini => MiniSprite != null;

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
		private readonly Action<string> _onHeroChosen;    // → 开详情页（不是开局）
		private readonly int _year;
		/// <summary>当前身份筛档（空串 = 「全部」）。</summary>
		private string _activeType = string.Empty;

		public HeroSelectVM(int year, Action onBack, Action<string> onHeroChosen, bool recommended = false)
		{
			_year = year;
			_onBack = onBack;
			_onHeroChosen = onHeroChosen;
			IsRecommendedMode = recommended;

			Realms = new MBBindingList<RealmItemVM>();
			Houses = new MBBindingList<HouseItemVM>();
			Lords = new MBBindingList<LordItemVM>();
			Filters = new MBBindingList<FilterItemVM>();
			RecommendedItems = new MBBindingList<RecommendedItemVM>();

			if (recommended)
			{
				BuildRecommended();
			}
			else
			{
				BuildFilters();
				RefreshRealms();     // 默认「全部」档；内部会自动选第一个王国/家族
			}
			RefreshValues();
		}

		// ── 列表属性（🔴 单实例，构造里 new 一次）──
		[DataSourceProperty] public MBBindingList<RealmItemVM> Realms { get; }
		[DataSourceProperty] public MBBindingList<HouseItemVM> Houses { get; }
		[DataSourceProperty] public MBBindingList<LordItemVM> Lords { get; }

		/// <summary>最左列的筛档（第一档 = 「全部」）。</summary>
		[DataSourceProperty] public MBBindingList<FilterItemVM> Filters { get; }

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
		[DataSourceProperty] public string KingdomHeader => new TextObject("{=LWN_hero_select_kingdom}Realm").ToString();
		[DataSourceProperty] public string ClanHeader => new TextObject("{=LWN_hero_select_clan}House").ToString();
		[DataSourceProperty] public string HeroHeader => new TextObject("{=LWN_hero_select_hero}Lords").ToString();

		/// <summary>最左列（身份筛档）的标题。</summary>
		[DataSourceProperty] public string IdentityHeader => new TextObject("{=LWN_hero_select_identity}Station").ToString();

		/// <summary>该时代没有任何可选的人（内容包没配目录）——界面显示提示而不是空列表。</summary>
		[DataSourceProperty] public bool IsEmpty => IsRecommendedMode
			? RecommendedItems.Count == 0
			: Realms.Count == 0;

		/// <summary>空列表文案：筛档筛空的 ≠ 该时代本来就没人的（反馈要说清是哪一种）。</summary>
		[DataSourceProperty] public string EmptyText => string.IsNullOrEmpty(_activeType)
			? new TextObject("{=LWN_hero_select_empty}No playable lord in this era.").ToString()
			: new TextObject("{=LWN_hero_select_empty_identity}No playable lord with this station.").ToString();

		/// <summary>
		/// 左列筛档：「全部」打头，之后按目录 <c>&lt;RealmType&gt;</c> 的 order（内容包没配 = 只有「全部」）。
		/// </summary>
		private void BuildFilters()
		{
			Filters.Add(new FilterItemVM(string.Empty,
				new TextObject("{=LWN_hero_select_identity_all}All").ToString(), this));
			foreach (HeroCatalogRegistry.RealmType t in HeroCatalogRegistry.GetRealmTypes())
			{
				Filters.Add(new FilterItemVM(t.Id, HeroSelectData.Resolve(t.NameRaw), this));
			}
			Filters[0].IsSelected = true;
		}

		/// <summary>按当前筛档重铺王国列，并自动选第一个王国（→ 家族 → 领主），让界面一打开就有内容。</summary>
		private void RefreshRealms()
		{
			Realms.Clear();
			foreach (HeroCatalogRegistry.Realm r in HeroSelectData.GetRealms(_year))
			{
				if (PassRealm(r))
				{
					Realms.Add(new RealmItemVM(r, this));
				}
			}
			SelectRealm(Realms.Count > 0 ? Realms[0] : null);
			OnPropertyChanged(nameof(IsEmpty));
			OnPropertyChanged(nameof(EmptyText));
		}

		/// <summary>该王国在当前筛档下留不留。王国自己的 type 命中就留；否则看它旗下有没有该档的家族
		/// （「无所属」那一档自己不带 type，全靠这条：商人/浪人众都在里面）。</summary>
		private bool PassRealm(HeroCatalogRegistry.Realm realm)
		{
			if (string.IsNullOrEmpty(_activeType) || realm == null)
			{
				return true;
			}
			if (realm.Type == _activeType)
			{
				return true;
			}
			foreach (HeroCatalogRegistry.House h in HeroSelectData.GetHouses(_year, realm.Id))
			{
				if (PassHouse(h))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>该家族在当前筛档下留不留（家族没标 type = 只在「全部」档出现）。</summary>
		private bool PassHouse(HeroCatalogRegistry.House house)
		{
			return string.IsNullOrEmpty(_activeType) || (house != null && house.Type == _activeType);
		}

		/// <summary>点左列筛档 → 重铺王国列（级联仍自动落到第一个人）。</summary>
		internal void SelectFilter(FilterItemVM item)
		{
			if (item == null)
			{
				return;
			}
			foreach (FilterItemVM f in Filters)
			{
				f.IsSelected = f == item;
			}
			_activeType = item.TypeKey ?? string.Empty;
			RefreshRealms();
		}

		/// <summary>点领主行 → 交给外层开详情页（**不在这里落地**）。</summary>
		internal void OnLordClicked(HeroCatalogRegistry.Lord lord)
		{
			if (lord == null)
			{
				return;
			}
			foreach (LordItemVM l in Lords)
			{
				l.IsSelected = l.Lord == lord;
			}
			_onHeroChosen?.Invoke(lord.Id);
		}

		/// <summary>点推荐人行 → 同上；该时代没有这个人则点了没反应。</summary>
		internal void OnRecommendedClicked(RecommendedItemVM item)
		{
			if (item == null || item.IsMissing)
			{
				return;
			}
			foreach (RecommendedItemVM r in RecommendedItems)
			{
				r.IsSelected = r == item;
			}
			_onHeroChosen?.Invoke(item.HeroId);
		}

		/// <summary>
		/// 用内容包的「推荐」名单铺列表：名单里的 id 要在**该时代的目录**里找得到人。
		/// 找不到就灰显「未登场」（占位期/该时代还没这个人，属正常）。
		/// 年龄按 **时代年份 − 生年** 现算（史实口径）。
		/// </summary>
		private void BuildRecommended()
		{
			foreach (HeroProfileRegistry.Recommendation rec in HeroProfileRegistry.Recommendations)
			{
				HeroCatalogRegistry.Lord lord = HeroSelectData.FindLord(_year, rec.HeroId);
				bool missing = lord == null;

				string name = missing
					? new TextObject("{=LWN_hero_select_not_arrived}Not yet in this world").ToString()
					: HeroSelectData.Resolve(lord.NameRaw);

				string ageText = string.Empty;
				HeroProfileRegistry.Profile profile = HeroProfileRegistry.GetProfile(rec.HeroId);
				if (profile != null && profile.HasLifespan)
				{
					int age = _year > 0 ? _year - profile.Birth : 0;
					ageText = new TextObject("{=LWN_hero_detail_age}{AGE} years old")
						.SetTextVariable("AGE", age.ToString()).ToString();
				}

				RecommendedItems.Add(new RecommendedItemVM(
					rec.HeroId, name, ageText,
					new TextObject(rec.StoryTypeRaw).ToString(),
					new TextObject(rec.StoryGoalRaw).ToString(),
					missing, lord?.MiniSprite, this));
			}
		}

		/// <summary>点王国 → 刷新家族列（按当前筛档过滤；并自动选第一个家族，省一次点击）。</summary>
		internal void SelectRealm(RealmItemVM item)
		{
			foreach (RealmItemVM r in Realms)
			{
				r.IsSelected = r == item;
			}
			Houses.Clear();
			if (item != null)
			{
				foreach (HeroCatalogRegistry.House h in HeroSelectData.GetHouses(_year, item.Realm?.Id))
				{
					if (PassHouse(h))
					{
						Houses.Add(new HouseItemVM(h, this));
					}
				}
			}
			SelectHouse(Houses.Count > 0 ? Houses[0] : null);
		}

		/// <summary>点家族 → 刷新领主列（同样自动选第一个，让行高亮直接落在人身上）。</summary>
		internal void SelectHouse(HouseItemVM item)
		{
			foreach (HouseItemVM h in Houses)
			{
				h.IsSelected = h == item;
			}
			Lords.Clear();
			if (item != null)
			{
				foreach (HeroCatalogRegistry.Lord l in HeroSelectData.GetLords(_year, item.House?.Id))
				{
					Lords.Add(new LordItemVM(l, this));
				}
			}
			// 只做高亮，**不开详情页**（换列不该把人推进下一步）
			foreach (LordItemVM l in Lords)
			{
				l.IsSelected = l == (Lords.Count > 0 ? Lords[0] : null);
			}
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
			OnPropertyChanged(nameof(KingdomHeader));
			OnPropertyChanged(nameof(ClanHeader));
			OnPropertyChanged(nameof(HeroHeader));
			OnPropertyChanged(nameof(IdentityHeader));
			OnPropertyChanged(nameof(IsRecommendedMode));
			OnPropertyChanged(nameof(IsTreeMode));
			OnPropertyChanged(nameof(IsEmpty));
			OnPropertyChanged(nameof(EmptyText));
		}
	}
}
