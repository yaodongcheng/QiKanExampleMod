using System;
using System.Collections.Generic;
using System.Linq;
using BannerlordTalk.Runtime;
using BannerlordTalk.Settings;
using MCM.Abstractions.Base.Global;
using SandBox.View.Map;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace BannerlordTalk.UI;

internal static class ChatterOverlay
{
	private const int OverlayLayerOrder = 2100;

	private static GauntletLayer _layer;

	private static GauntletMovieIdentifier _movie;

	private static ChatterOverlayVM _vm;

	private static ScreenBase _screen;

	private static List<ChatterLineState> _pendingLines = new List<ChatterLineState>();

	private static string _lastCoordinateDiagnostics = "";

	private static bool? _lastHitInside;

	private static OverlayHotkeyBinding _visibilityBinding;

	private static string _visibilityBindingText = "";

	private static bool _sessionHidden;

	private static bool _settlementOverrideVisible;

	private static bool _settlementAutoHideContextActive;

	internal static void Show()
	{
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_004c: Expected O, but got Unknown
		if (_layer != null)
		{
			return;
		}
		ScreenBase topScreen = ScreenManager.TopScreen;
		MapScreen val = (MapScreen)(object)((topScreen is MapScreen) ? topScreen : null);
		if (val == null)
		{
			return;
		}
		try
		{
			_screen = (ScreenBase)(object)val;
			_vm = new ChatterOverlayVM();
			_vm.ReplaceAll(_pendingLines);
			_layer = new GauntletLayer("BannerlordTalkOverlay", 2100, false);
			_movie = _layer.LoadMovie("BannerlordTalkOverlay", (ViewModel)(object)_vm);
			_vm.RefreshSettings(CaptureCoordinateSpace());
			((ScreenLayer)_layer).InputRestrictions.ResetInputRestrictions();
			((ScreenLayer)_layer).IsFocusLayer = false;
			((ScreenBase)val).AddLayer((ScreenLayer)(object)_layer);
			LogUiDiagnostics("ui_overlay_open");
		}
		catch (Exception exception)
		{
			Log.Error("overlay_open_failed", exception);
			Hide();
		}
	}

	internal static void Tick(bool onMap, bool paused, ChatterMcmSettings settings)
	{
		Tick(onMap, paused, inSettlementMenu: false, settings);
	}

	internal static void Tick(bool onMap, bool paused, bool inSettlementMenu, ChatterMcmSettings settings)
	{
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_017e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		bool flag = onMap && inSettlementMenu && (settings?.AutoCollapseInSettlementMenu ?? false);
		UpdateSettlementAutoHideContext(flag);
		ChatterManagerPanel.TickHotkey(onMap, settings);
		TickVisibilityHotkey(onMap, paused, flag, settings);
		bool flag2 = (flag ? _settlementOverrideVisible : (!_sessionHidden));
		if (_layer == null && onMap && (settings?.WindowEnabled ?? false) && flag2)
		{
			Show();
		}
		if (_layer == null)
		{
			return;
		}
		UiCoordinateSpace coordinateSpace = CaptureCoordinateSpace();
		_vm.RefreshSettings(coordinateSpace);
		bool flag3 = onMap && (settings?.WindowEnabled ?? false) && flag2 && (!paused || !settings.HideWhilePaused);
		_vm.IsVisible = flag3;
		if (!flag3)
		{
			((ScreenLayer)_layer).InputRestrictions.ResetInputRestrictions();
			((ScreenLayer)_layer).IsFocusLayer = false;
			ScreenManager.TryLoseFocus((ScreenLayer)(object)_layer);
		}
		else
		{
			Vec2 mousePositionPixel = Input.MousePositionPixel;
			bool flag4 = _vm.HitTestWindowPhysical(mousePositionPixel.x, mousePositionPixel.y);
			if (settings != null && settings.EnableDiagnosticLogging && _lastHitInside != flag4)
			{
				_lastHitInside = flag4;
				Log.Info("ui_overlay_hit inside=" + flag4.ToString().ToLowerInvariant() + " layer=" + 2100 + " " + _vm.DescribeCoordinateDiagnostics(mousePositionPixel.x, mousePositionPixel.y));
			}
			if (flag4)
			{
				((ScreenLayer)_layer).InputRestrictions.SetInputRestrictions(true, (InputUsageMask)3);
			}
			else
			{
				((ScreenLayer)_layer).InputRestrictions.ResetInputRestrictions();
				((ScreenLayer)_layer).IsFocusLayer = false;
				ScreenManager.TryLoseFocus((ScreenLayer)(object)_layer);
			}
		}
		LogUiDiagnostics("ui_overlay_metrics");
	}

	internal static void Publish(ChatterLineState line)
	{
		if (line != null)
		{
			_pendingLines.Add(line);
			int num = Math.Max(5, GlobalSettings<ChatterMcmSettings>.Instance?.HistoryLineLimit ?? 60);
			if (_pendingLines.Count > num)
			{
				_pendingLines.RemoveRange(0, _pendingLines.Count - num);
			}
			_vm?.Publish(line);
		}
	}

	internal static void ReplaceAll(IEnumerable<ChatterLineState> lines)
	{
		_pendingLines = lines?.Where((ChatterLineState line) => line != null).ToList() ?? new List<ChatterLineState>();
		_vm?.ReplaceAll(_pendingLines);
	}

	internal static void Hide()
	{
		try
		{
			if (_layer != null)
			{
				((ScreenLayer)_layer).IsFocusLayer = false;
				ScreenManager.TryLoseFocus((ScreenLayer)(object)_layer);
				if (_movie != null)
				{
					_layer.ReleaseMovie(_movie);
				}
				ScreenBase screen = _screen;
				if (screen != null)
				{
					screen.RemoveLayer((ScreenLayer)(object)_layer);
				}
			}
		}
		catch
		{
		}
		ChatterOverlayVM vm = _vm;
		if (vm != null)
		{
			((ViewModel)vm).OnFinalize();
		}
		_vm = null;
		_movie = null;
		_layer = null;
		_screen = null;
		_lastCoordinateDiagnostics = "";
		_lastHitInside = null;
	}

	internal static void Reset()
	{
		Hide();
		ChatterManagerPanel.Reset();
		_pendingLines.Clear();
		_visibilityBinding = null;
		_visibilityBindingText = "";
		_sessionHidden = false;
		_settlementOverrideVisible = false;
		_settlementAutoHideContextActive = false;
	}

	private static void TickVisibilityHotkey(bool onMap, bool paused, bool settlementAutoHideContext, ChatterMcmSettings settings)
	{
		if (!onMap || !(ScreenManager.TopScreen is MapScreen) || settings == null || !settings.WindowEnabled || (paused && settings.HideWhilePaused) || ChatterManagerPanel.IsOpen)
		{
			return;
		}
		string text = settings.WindowVisibilityHotkey ?? "";
		if (_visibilityBinding == null || !string.Equals(_visibilityBindingText, text, StringComparison.Ordinal))
		{
			_visibilityBindingText = text;
			_visibilityBinding = OverlayHotkeyPolicy.ParseOrDefault(text);
		}
		if (OverlayHotkeyPolicy.IsPressed(_visibilityBinding))
		{
			if (settlementAutoHideContext)
			{
				_settlementOverrideVisible = !_settlementOverrideVisible;
			}
			else
			{
				_sessionHidden = !_sessionHidden;
			}
			_lastHitInside = null;
			_lastCoordinateDiagnostics = "";
			if (settings.EnableDiagnosticLogging)
			{
				bool flag = (settlementAutoHideContext ? (!_settlementOverrideVisible) : _sessionHidden);
				Log.Info("ui_overlay_session_visibility hidden=" + flag.ToString().ToLowerInvariant() + " hotkey=" + _visibilityBinding.CanonicalText + " scope=" + (settlementAutoHideContext ? "settlement_override" : "session"));
			}
		}
	}

	private static void UpdateSettlementAutoHideContext(bool active)
	{
		if (_settlementAutoHideContextActive != active)
		{
			_settlementAutoHideContextActive = active;
			_settlementOverrideVisible = false;
			_lastHitInside = null;
			_lastCoordinateDiagnostics = "";
		}
	}

	private static UiCoordinateSpace CaptureCoordinateSpace()
	{
		GauntletLayer layer = _layer;
		float? obj;
		if (layer == null)
		{
			obj = null;
		}
		else
		{
			UIContext uIContext = layer.UIContext;
			obj = ((uIContext != null) ? new float?(uIContext.CustomScale) : null);
		}
		float? num = obj;
		float valueOrDefault = num.GetValueOrDefault(1f);
		return new UiCoordinateSpace(Screen.RealScreenResolutionWidth, Screen.RealScreenResolutionHeight, valueOrDefault);
	}

	private static void LogUiDiagnostics(string stage)
	{
		ChatterMcmSettings instance = GlobalSettings<ChatterMcmSettings>.Instance;
		if (instance != null && instance.EnableDiagnosticLogging && _vm != null)
		{
			string text = _vm.DescribeCoordinateMetrics();
			if (!(stage != "ui_overlay_open") || !string.Equals(text, _lastCoordinateDiagnostics, StringComparison.Ordinal))
			{
				_lastCoordinateDiagnostics = text;
				Log.Info(stage + " layer=" + 2100 + " space=physical_to_logical " + text);
			}
		}
	}
}
