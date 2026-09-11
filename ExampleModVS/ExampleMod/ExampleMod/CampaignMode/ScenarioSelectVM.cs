using System;
using System.Collections.Generic;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace LivingWorldNpcs.CampaignMode
{
	/// <summary>
	/// 选剧本界面的 ViewModel（配 <c>GUI/Prefabs/ScenarioSelect.xml</c>）。
	/// 布局对标太阁5「开始新游戏」：左侧剧本按钮列 + 右侧大图 + 右下返回。
	/// 🔴 选时代必须在主菜单层做（引擎在 new 出战役类时才按类名过滤 XML 段）——
	///   本界面就是主菜单入口点开的那个界面。
	/// </summary>
	public class ScenarioSelectVM : ViewModel
	{
		private readonly Action _onBack;
		private readonly Action<EraCatalog.Era> _onPick;
		private readonly Action _onRecommended;
		private readonly List<ScenarioItemVM> _items = new List<ScenarioItemVM>();
		private ScenarioItemVM _selected;

		public ScenarioSelectVM(Action onBack, Action<EraCatalog.Era> onPick, Action onRecommended = null)
		{
			_onBack = onBack;
			_onPick = onPick;
			_onRecommended = onRecommended;
			Items = new MBBindingList<ScenarioItemVM>();
			foreach (EraCatalog.Era era in EraCatalog.All)
			{
				var item = new ScenarioItemVM(era, this);
				_items.Add(item);
				Items.Add(item);
			}
			_selected = _items.Count > 0 ? _items[0] : null;
			if (_selected != null)
			{
				_selected.IsSelected = true;
			}
			RefreshValues();
		}

		/// <summary>界面标题。</summary>
		public string TitleText => new TextObject("{=LWN_scenario_select_title}Choose a Scenario").ToString();

		/// <summary>返回按钮文字。</summary>
		public string BackText => new TextObject("{=LWN_scenario_select_back}Back").ToString();

		/// <summary>「推荐」按钮文字（太阁5 原版就在剧本页底部居中）。</summary>
		public string RecommendedText => new TextObject("{=LWN_scenario_select_recommended}Recommended").ToString();

		/// <summary>剧本按钮列表（prefab 里 DataSource="{Items}"）。
		/// 🔴 必须是**同一个实例**——每次 get 新建列表会割断 Gauntlet 绑定。</summary>
		[DataSourceProperty]
		public MBBindingList<ScenarioItemVM> Items { get; }

		/// <summary>是否显示右侧大图（没配图 = 显示占位块）。</summary>
		[DataSourceProperty]
		public bool HasPreview => _selected != null && !string.IsNullOrEmpty(_selected.Era.PreviewSprite);

		/// <summary>右侧大图的 sprite 名。</summary>
		[DataSourceProperty]
		public string PreviewSprite => _selected?.Era.PreviewSprite ?? string.Empty;

		/// <summary>悬停/点击某个剧本 → 切换右侧大图。</summary>
		internal void OnItemHover(ScenarioItemVM item)
		{
			if (item == null)
			{
				return;
			}
			foreach (ScenarioItemVM other in _items)
			{
				other.IsSelected = other == item;
			}
			_selected = item;
			OnPropertyChanged(nameof(HasPreview));
			OnPropertyChanged(nameof(PreviewSprite));
		}

		/// <summary>点击剧本 → 开这一局。</summary>
		internal void OnItemClick(ScenarioItemVM item)
		{
			if (item != null && _onPick != null)
			{
				_onPick(item.Era);
			}
		}

		/// <summary>返回主菜单。</summary>
		public void ExecuteBack()
		{
			_onBack?.Invoke();
		}

		/// <summary>「推荐」→ 进该时代的推荐人物列表（固定 1560，不跟随当前选中项）。</summary>
		public void ExecuteRecommended()
		{
			_onRecommended?.Invoke();
		}

		public override void RefreshValues()
		{
			base.RefreshValues();
			OnPropertyChanged(nameof(TitleText));
			OnPropertyChanged(nameof(BackText));
			OnPropertyChanged(nameof(RecommendedText));
		}

		/// <summary>一个剧本按钮。</summary>
		public class ScenarioItemVM : ViewModel
		{
			private readonly ScenarioSelectVM _owner;
			private bool _isSelected;

			internal EraCatalog.Era Era { get; }

			public ScenarioItemVM(EraCatalog.Era era, ScenarioSelectVM owner)
			{
				Era = era;
				_owner = owner;
			}

			/// <summary>年份（按钮上的大字，如 "1560"）。</summary>
			[DataSourceProperty]
			public string Year => Era.Year;

			/// <summary>卷名（按钮上的小字，如 "日轮之卷"）。</summary>
			[DataSourceProperty]
			public string Name => Era.VolumeName.ToString();

			/// <summary>按钮文字色：选中/悬停 = 金字，其余 = 白字（prefab 绑 Brush.FontColor）。</summary>
			[DataSourceProperty]
			public string TextColor => IsSelected ? "#F0CE7AFF" : "#F2F2F2FF";

			/// <summary>当前选中/悬停项（界面据此高亮 + 换右图）。</summary>
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

			public void ExecuteHover()
			{
				_owner.OnItemHover(this);
			}

			public void ExecuteClick()
			{
				_owner.OnItemClick(this);
			}

			public override void RefreshValues()
			{
				base.RefreshValues();
				OnPropertyChanged(nameof(Year));
				OnPropertyChanged(nameof(Name));
			}
		}
	}
}
