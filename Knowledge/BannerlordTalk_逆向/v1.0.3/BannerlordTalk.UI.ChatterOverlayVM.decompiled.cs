using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BannerlordTalk.Runtime;
using BannerlordTalk.Settings;
using MCM.Abstractions;
using MCM.Abstractions.Base;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordTalk.UI;

internal sealed class ChatterOverlayVM : ViewModel
{
	private const int CollapsedWindowHeight = 68;

	private const int HeaderControlReservedWidth = 232;

	private int _windowX;

	private int _windowY;

	private int _windowWidth;

	private int _windowHeight;

	private int _renderedWindowHeight;

	private int _headerTextWidth;

	private float _windowOpacity;

	private bool _isVisible;

	private bool _manualCollapsed;

	private UiCoordinateSpace _coordinateSpace;

	private long _lastResizeAtTicks;

	[DataSourceProperty]
	public string TitleText => "旅途闲聊";

	[DataSourceProperty]
	public string StatusText => "现实时间自动生成 · 单人单句";

	[DataSourceProperty]
	public MBBindingList<ChatterLineVM> Lines { get; }

	[DataSourceProperty]
	public int WindowX
	{
		get
		{
			return _windowX;
		}
		private set
		{
			if (value != _windowX)
			{
				_windowX = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "WindowX");
			}
		}
	}

	[DataSourceProperty]
	public int WindowY
	{
		get
		{
			return _windowY;
		}
		private set
		{
			if (value != _windowY)
			{
				_windowY = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "WindowY");
			}
		}
	}

	[DataSourceProperty]
	public int WindowWidth
	{
		get
		{
			return _windowWidth;
		}
		private set
		{
			if (value != _windowWidth)
			{
				_windowWidth = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "WindowWidth");
			}
		}
	}

	[DataSourceProperty]
	public int WindowHeight
	{
		get
		{
			return _windowHeight;
		}
		private set
		{
			if (value != _windowHeight)
			{
				_windowHeight = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "WindowHeight");
			}
		}
	}

	[DataSourceProperty]
	public int RenderedWindowHeight
	{
		get
		{
			return _renderedWindowHeight;
		}
		private set
		{
			if (value != _renderedWindowHeight)
			{
				_renderedWindowHeight = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "RenderedWindowHeight");
			}
		}
	}

	[DataSourceProperty]
	public int HeaderTextWidth
	{
		get
		{
			return _headerTextWidth;
		}
		private set
		{
			if (value != _headerTextWidth)
			{
				_headerTextWidth = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "HeaderTextWidth");
			}
		}
	}

	[DataSourceProperty]
	public float WindowOpacity
	{
		get
		{
			return _windowOpacity;
		}
		private set
		{
			if (Math.Abs(value - _windowOpacity) > 0.001f)
			{
				_windowOpacity = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "WindowOpacity");
			}
		}
	}

	[DataSourceProperty]
	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		internal set
		{
			if (value != _isVisible)
			{
				_isVisible = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "IsVisible");
			}
		}
	}

	[DataSourceProperty]
	public bool IsCollapsed => _manualCollapsed;

	[DataSourceProperty]
	public bool IsContentVisible => !IsCollapsed;

	[DataSourceProperty]
	public string CollapseButtonText
	{
		get
		{
			if (!IsCollapsed)
			{
				return "收起";
			}
			return "展开";
		}
	}

	internal ChatterOverlayVM()
	{
		Lines = new MBBindingList<ChatterLineVM>();
		_coordinateSpace = new UiCoordinateSpace(Screen.RealScreenResolutionWidth, Screen.RealScreenResolutionHeight, 1f);
		RefreshSettings(_coordinateSpace);
	}

	public void ExecuteOpenManager()
	{
		ChatterManagerPanel.Show();
	}

	public void ExecuteShrinkWindow()
	{
		ResizeWindow(0.9f);
	}

	public void ExecuteGrowWindow()
	{
		ResizeWindow(1.1f);
	}

	public void ExecuteToggleCollapsed()
	{
		_manualCollapsed = !_manualCollapsed;
		RefreshCollapseState();
	}

	internal void ReplaceAll(IEnumerable<ChatterLineState> lines)
	{
		((Collection<ChatterLineVM>)(object)Lines).Clear();
		if (lines == null)
		{
			return;
		}
		foreach (ChatterLineState line in lines)
		{
			((Collection<ChatterLineVM>)(object)Lines).Add(new ChatterLineVM(line));
		}
	}

	internal void Publish(ChatterLineState line)
	{
		if (line != null)
		{
			((Collection<ChatterLineVM>)(object)Lines).Add(new ChatterLineVM(line));
		}
		int num = Math.Max(5, GlobalSettings<ChatterMcmSettings>.Instance?.HistoryLineLimit ?? 60);
		while (((Collection<ChatterLineVM>)(object)Lines).Count > num)
		{
			((Collection<ChatterLineVM>)(object)Lines).RemoveAt(0);
		}
	}

	internal void RefreshSettings(UiCoordinateSpace coordinateSpace)
	{
		_coordinateSpace = coordinateSpace;
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if (instance != null)
		{
			WindowWidth = coordinateSpace.ResponsiveWidth(instance.WindowWidth, 420);
			WindowHeight = coordinateSpace.ResponsiveHeight(instance.WindowHeight, 260);
			RenderedWindowHeight = (IsCollapsed ? Math.Min(68, WindowHeight) : WindowHeight);
			HeaderTextWidth = Math.Max(1, WindowWidth - 232);
			float x = (((instance.WindowAnchor?.SelectedIndex ?? 0) == 1) ? (coordinateSpace.LogicalWidth - (float)WindowWidth - 12f) : 12f);
			WindowX = (int)Math.Round(coordinateSpace.ClampLogicalXWithSafeMargin(x, WindowWidth));
			WindowY = (int)Math.Round(coordinateSpace.ClampLogicalYWithSafeMargin(12f, RenderedWindowHeight));
			WindowOpacity = instance.WindowOpacity;
			((ViewModel)this).OnPropertyChanged("StatusText");
		}
	}

	private void RefreshCollapseState()
	{
		((ViewModel)this).OnPropertyChanged("IsCollapsed");
		((ViewModel)this).OnPropertyChanged("IsContentVisible");
		((ViewModel)this).OnPropertyChanged("CollapseButtonText");
		RefreshSettings(_coordinateSpace);
	}

	private void ResizeWindow(float factor)
	{
		long ticks = DateTime.UtcNow.Ticks;
		if (ticks - _lastResizeAtTicks < TimeSpan.FromMilliseconds(120.0).Ticks)
		{
			return;
		}
		_lastResizeAtTicks = ticks;
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if (instance == null || float.IsNaN(factor) || float.IsInfinity(factor))
		{
			return;
		}
		int num = Math.Max(480, Math.Min(1200, (int)Math.Round((float)instance.WindowWidth * factor)));
		int num2 = Math.Max(280, Math.Min(900, (int)Math.Round((float)instance.WindowHeight * factor)));
		if (num == instance.WindowWidth && num2 == instance.WindowHeight)
		{
			return;
		}
		instance.WindowWidth = num;
		instance.WindowHeight = num2;
		try
		{
			BaseSettingsProvider instance2 = BaseSettingsProvider.Instance;
			if (instance2 != null)
			{
				instance2.SaveSettings((BaseSettings)(object)instance);
			}
		}
		catch
		{
		}
		RefreshSettings(_coordinateSpace);
	}

	internal bool HitTestWindowPhysical(float mouseX, float mouseY)
	{
		if (IsVisible)
		{
			return _coordinateSpace.ContainsPhysical(mouseX, mouseY, WindowX, WindowY, WindowWidth, RenderedWindowHeight);
		}
		return false;
	}

	internal string DescribeCoordinateDiagnostics(float mouseX, float mouseY)
	{
		return DescribeCoordinateMetrics() + " mousePhysical=" + Math.Round(mouseX) + "," + Math.Round(mouseY) + " mouseLogical=" + Math.Round(_coordinateSpace.ToLogical(mouseX)) + "," + Math.Round(_coordinateSpace.ToLogical(mouseY));
	}

	internal string DescribeCoordinateMetrics()
	{
		return "physical=" + Math.Round(_coordinateSpace.PhysicalWidth) + "x" + Math.Round(_coordinateSpace.PhysicalHeight) + " logical=" + Math.Round(_coordinateSpace.LogicalWidth) + "x" + Math.Round(_coordinateSpace.LogicalHeight) + " scale=" + _coordinateSpace.Scale.ToString("0.###") + " rect=" + WindowX + "," + WindowY + "," + WindowWidth + "," + RenderedWindowHeight + " expandedHeight=" + WindowHeight + " collapsed=" + IsCollapsed.ToString().ToLowerInvariant();
	}
}
