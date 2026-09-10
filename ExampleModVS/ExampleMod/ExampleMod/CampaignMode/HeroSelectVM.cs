using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选人界面 ViewModel（配 <c>GUI/Prefabs/HeroSelect.xml</c>）。
	/// 三级联动：选王国 → 刷新家族列 → 选家族 → 刷新英雄列 → 点英雄 = 用他开局。
	/// 另有一条并行路：「自定义英雄」按钮 → 走现有建号流程（本界面不参与）。
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

		/// <summary>右侧英雄项。</summary>
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

			public void ExecutePick() => _owner.SelectHero(this);
		}

		private readonly Action _onBack;
		private readonly Action<Hero> _onPickHero;
		private readonly Action _onCustomHero;

		private KingdomItemVM _selectedKingdom;
		private ClanItemVM _selectedClan;
		private string _selectedHeroText = string.Empty;
		private bool _canConfirm;

		public HeroSelectVM(Action onBack, Action<Hero> onPickHero, Action onCustomHero)
		{
			_onBack = onBack;
			_onPickHero = onPickHero;
			_onCustomHero = onCustomHero;

			Kingdoms = new MBBindingList<KingdomItemVM>();
			Clans = new MBBindingList<ClanItemVM>();
			Heroes = new MBBindingList<HeroItemVM>();

			foreach (Kingdom k in HeroSelectData.GetSelectableKingdoms())
			{
				Kingdoms.Add(new KingdomItemVM(k, this));
			}
			if (Kingdoms.Count > 0)
			{
				SelectKingdom(Kingdoms[0]);      // 默认选第一个王国，界面一打开就有内容
			}
			RefreshValues();
		}

		// ── 列表属性（🔴 单实例，构造里 new 一次）──
		[DataSourceProperty] public MBBindingList<KingdomItemVM> Kingdoms { get; }
		[DataSourceProperty] public MBBindingList<ClanItemVM> Clans { get; }
		[DataSourceProperty] public MBBindingList<HeroItemVM> Heroes { get; }

		[DataSourceProperty] public string TitleText => new TextObject("{=LWN_hero_select_title}Choose Your Lord").ToString();
		[DataSourceProperty] public string BackText => new TextObject("{=LWN_hero_select_back}Back").ToString();
		[DataSourceProperty] public string CustomHeroText => new TextObject("{=LWN_hero_select_custom}Custom Hero").ToString();
		[DataSourceProperty] public string ConfirmText => new TextObject("{=LWN_hero_select_confirm}Start as This Lord").ToString();
		[DataSourceProperty] public string KingdomHeader => new TextObject("{=LWN_hero_select_kingdom}Realm").ToString();
		[DataSourceProperty] public string ClanHeader => new TextObject("{=LWN_hero_select_clan}House").ToString();
		[DataSourceProperty] public string HeroHeader => new TextObject("{=LWN_hero_select_hero}Lords").ToString();

		/// <summary>当前选中的英雄（未选 = 空串；界面据此显示"将扮演：X"并决定确认键可用）。</summary>
		[DataSourceProperty]
		public string SelectedHeroText
		{
			get => _selectedHeroText;
			private set
			{
				if (_selectedHeroText != value)
				{
					_selectedHeroText = value;
					OnPropertyChangedWithValue(value, nameof(SelectedHeroText));
				}
			}
		}

		/// <summary>是否已选好英雄（未选 = 确认键禁用）。</summary>
		[DataSourceProperty]
		public bool CanConfirm
		{
			get => _canConfirm;
			private set
			{
				if (_canConfirm != value)
				{
					_canConfirm = value;
					OnPropertyChangedWithValue(value, nameof(CanConfirm));
				}
			}
		}

		/// <summary>点王国 → 刷新家族列（并自动选第一个家族，省一次点击）。</summary>
		internal void SelectKingdom(KingdomItemVM item)
		{
			if (item == null)
			{
				return;
			}
			_selectedKingdom = item;
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

		/// <summary>点家族 → 刷新英雄列（同样自动选第一个，让"确认"按钮立刻可用）。</summary>
		internal void SelectClan(ClanItemVM item)
		{
			_selectedClan = item;
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
			SelectHero(Heroes.Count > 0 ? Heroes[0] : null);
		}

		/// <summary>点英雄 → 选它（不直接开局；由「确认」键或双击决定，防误触）。</summary>
		internal void SelectHero(HeroItemVM item)
		{
			foreach (HeroItemVM h in Heroes)
			{
				h.IsSelected = h == item;
			}
			if (item?.Hero != null)
			{
				SelectedHeroText = new TextObject("{=LWN_hero_select_will_be}You will play as: {NAME}")
					.SetTextVariable("NAME", HeroSelectData.GetDisplayName(item.Hero)).ToString();
				CanConfirm = true;
			}
			else
			{
				SelectedHeroText = new TextObject("{=LWN_hero_select_none}No selectable lord in this house").ToString();
				CanConfirm = false;
			}
		}

		/// <summary>确认键 → 用当前选中的英雄开局。</summary>
		public void ExecuteConfirm()
		{
			Hero hero = GetSelectedHero();
			if (hero != null)
			{
				_onPickHero?.Invoke(hero);
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

		private Hero GetSelectedHero()
		{
			foreach (HeroItemVM h in Heroes)
			{
				if (h.IsSelected)
				{
					return h.Hero;
				}
			}
			return null;
		}

		public override void RefreshValues()
		{
			base.RefreshValues();
			OnPropertyChanged(nameof(TitleText));
			OnPropertyChanged(nameof(BackText));
			OnPropertyChanged(nameof(CustomHeroText));
			OnPropertyChanged(nameof(ConfirmText));
			OnPropertyChanged(nameof(KingdomHeader));
			OnPropertyChanged(nameof(ClanHeader));
			OnPropertyChanged(nameof(HeroHeader));
		}
	}
}
