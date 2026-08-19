using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using BannerlordTalk.Prompts;
using BannerlordTalk.Settings;
using MCM.Abstractions.Base.Global;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace BannerlordTalk.UI;

internal sealed class ChatterManagerVM : ViewModel
{
	private readonly IChatterManagerDataSource _dataSource;

	private readonly Action _closed;

	private ChatterManagerPage _page;

	private string _selectedHeroId = "";

	private string _selectedRecordId = "";

	private string _selectedPromptId = "";

	private string _statusText = "";

	private string _editorText = "";

	private string _secondaryText = "";

	private string _knowledgeSummaryText = "";

	private string _numberText = "0.50";

	private bool _flagValue;

	private string _personaGenerationRequestId = "";

	private ManagerPersonaData _personaPreview;

	private string _personaPreviewText = "";

	private bool _personaGenerationBusy;

	private ManagerImportKind _importKind;

	private string _pendingImportContent = "";

	private string _importPreviewTitle = "";

	private string _importPreviewText = "";

	private bool _canConfirmImport;

	private int _managerWidth;

	private int _managerHeight;

	private int _managerHorizontalMargin;

	private int _managerContentTop;

	private int _managerContentHeight;

	private int _managerLeftWidth;

	private int _managerRightWidth;

	[DataSourceProperty]
	public string TitleText => "闲聊资料管理";

	[DataSourceProperty]
	public string PageTitle
	{
		get
		{
			if (_page != 0)
			{
				if (_page != ChatterManagerPage.Memory)
				{
					if (_page != ChatterManagerPage.Thought)
					{
						if (_page != ChatterManagerPage.Knowledge)
						{
							return "提示词预设";
						}
						return "独立常识库";
					}
					return "私密思绪";
				}
				return "个人记忆";
			}
			return "人格卡";
		}
	}

	[DataSourceProperty]
	public string EditorLabel
	{
		get
		{
			if (_page != 0)
			{
				if (_page != ChatterManagerPage.Memory)
				{
					if (_page != ChatterManagerPage.Thought)
					{
						if (_page != ChatterManagerPage.Knowledge)
						{
							return "当前提示词正文（可直接编辑保存；剪贴板导入走独立预览）";
						}
						return "当前整库抽样预览（只读；全文不会装入游戏控件）";
					}
					return "思绪正文";
				}
				return "记忆正文";
			}
			return "人格正文（说话风格/长期目标/价值/禁忌可在下方补充）";
		}
	}

	[DataSourceProperty]
	public string SecondaryLabel
	{
		get
		{
			if (_page != 0)
			{
				if (_page != ChatterManagerPage.Memory)
				{
					if (_page != ChatterManagerPage.Thought)
					{
						if (_page != ChatterManagerPage.Knowledge)
						{
							return "最终展开预览（只读；生成过该类型后显示最后一次实际 system + user）";
						}
						return "整库统计；格式：[关键词|权重|Any/All|可提取|后续可匹配]正文";
					}
					return "层级：Mid / Long / Belief；情感可写 positive / neutral / negative";
				}
				return "层级：Recent / Situational / EventLog / Archive";
			}
			return "说话风格 | 长期目标 | 价值 | 禁忌（每项一行）";
		}
	}

	[DataSourceProperty]
	public string NumberLabel
	{
		get
		{
			if (_page != 0)
			{
				if (_page != ChatterManagerPage.Thought)
				{
					if (_page != ChatterManagerPage.Prompt)
					{
						return "重要度 0..1";
					}
					return "";
				}
				return "强度 0..1";
			}
			return "话痨度 0..1";
		}
	}

	[DataSourceProperty]
	public string FlagLabel
	{
		get
		{
			if (_page != 0)
			{
				if (_page != ChatterManagerPage.Knowledge)
				{
					if (_page != ChatterManagerPage.Prompt)
					{
						return "置顶";
					}
					return "";
				}
				return "规则启用";
			}
			return "允许自动发起";
		}
	}

	[DataSourceProperty]
	public bool IsPersonaPage => _page == ChatterManagerPage.Persona;

	[DataSourceProperty]
	public bool IsMemoryPage => _page == ChatterManagerPage.Memory;

	[DataSourceProperty]
	public bool IsThoughtPage => _page == ChatterManagerPage.Thought;

	[DataSourceProperty]
	public bool IsKnowledgePage => _page == ChatterManagerPage.Knowledge;

	[DataSourceProperty]
	public bool IsPromptPage => _page == ChatterManagerPage.Prompt;

	[DataSourceProperty]
	public bool IsKnowledgeOrPromptPage
	{
		get
		{
			if (!IsKnowledgePage)
			{
				return IsPromptPage;
			}
			return true;
		}
	}

	[DataSourceProperty]
	public bool IsLibraryPage
	{
		get
		{
			if (!IsMemoryPage)
			{
				return IsKnowledgePage;
			}
			return true;
		}
	}

	[DataSourceProperty]
	public bool ShowValueControls
	{
		get
		{
			if (!IsPromptPage)
			{
				return !IsKnowledgePage;
			}
			return false;
		}
	}

	[DataSourceProperty]
	public bool ShowCrudButtons => !IsKnowledgePage;

	[DataSourceProperty]
	public bool ShowStandardEditors => !IsKnowledgePage;

	[DataSourceProperty]
	public bool CanEditPrimary => !IsKnowledgePage;

	[DataSourceProperty]
	public bool CanEditSecondary
	{
		get
		{
			if (!IsPromptPage)
			{
				return !IsKnowledgePage;
			}
			return false;
		}
	}

	[DataSourceProperty]
	public bool PersonaGenerationBusy => _personaGenerationBusy;

	[DataSourceProperty]
	public bool HasPersonaPreview => _personaPreview != null;

	[DataSourceProperty]
	public bool CanGeneratePersona
	{
		get
		{
			if (IsPersonaPage && !_personaGenerationBusy)
			{
				return !string.IsNullOrWhiteSpace(_selectedHeroId);
			}
			return false;
		}
	}

	[DataSourceProperty]
	public string PersonaGenerateButtonText
	{
		get
		{
			if (!_personaGenerationBusy)
			{
				return "智能生成";
			}
			return "生成中……";
		}
	}

	[DataSourceProperty]
	public string PersonaPreviewText => _personaPreviewText;

	[DataSourceProperty]
	public bool HasImportPreview => _importKind != ManagerImportKind.None;

	[DataSourceProperty]
	public bool CanConfirmImport => _canConfirmImport;

	[DataSourceProperty]
	public string ImportPreviewTitle => _importPreviewTitle;

	[DataSourceProperty]
	public string ImportPreviewText => _importPreviewText;

	[DataSourceProperty]
	public string ImportConfirmButtonText
	{
		get
		{
			if (_importKind != ManagerImportKind.KnowledgeReplacement)
			{
				return "确认导入";
			}
			return "替换整库";
		}
	}

	[DataSourceProperty]
	public int ManagerWidth => _managerWidth;

	[DataSourceProperty]
	public int ManagerHeight => _managerHeight;

	[DataSourceProperty]
	public int ManagerHorizontalMargin => _managerHorizontalMargin;

	[DataSourceProperty]
	public int ManagerContentTop => _managerContentTop;

	[DataSourceProperty]
	public int ManagerContentHeight => _managerContentHeight;

	[DataSourceProperty]
	public int ManagerLeftWidth => _managerLeftWidth;

	[DataSourceProperty]
	public int ManagerRightWidth => _managerRightWidth;

	[DataSourceProperty]
	public int PrimaryEditorTop => 28;

	[DataSourceProperty]
	public int StatusTop => Math.Max(0, ManagerContentHeight - (IsKnowledgeOrPromptPage ? 94 : 44) - 30);

	[DataSourceProperty]
	public int ValueControlTop => Math.Max(PrimaryEditorTop + 80, StatusTop - 44);

	private int EditorBottomBoundary
	{
		get
		{
			if (!ShowValueControls)
			{
				return StatusTop;
			}
			return ValueControlTop;
		}
	}

	[DataSourceProperty]
	public int PrimaryEditorHeight
	{
		get
		{
			int num = Math.Max(100, EditorBottomBoundary - PrimaryEditorTop);
			int val = Math.Max(64, num - 80);
			int val2 = (int)Math.Round((double)num * 0.66);
			return Math.Max(64, Math.Min(val2, val));
		}
	}

	[DataSourceProperty]
	public int SecondaryLabelTop => PrimaryEditorTop + PrimaryEditorHeight + 6;

	[DataSourceProperty]
	public int SecondaryEditorTop => SecondaryLabelTop + 28;

	[DataSourceProperty]
	public int SecondaryEditorHeight => Math.Max(36, EditorBottomBoundary - SecondaryEditorTop - 6);

	[DataSourceProperty]
	public int PersonaPreviewHeight => Math.Max(220, ManagerContentHeight - 54);

	[DataSourceProperty]
	public int ImportPreviewHeight => Math.Max(220, ManagerContentHeight - 54);

	[DataSourceProperty]
	public string DeleteButtonText
	{
		get
		{
			if (!IsPromptPage)
			{
				return "删除/清空";
			}
			return "恢复默认";
		}
	}

	[DataSourceProperty]
	public MBBindingList<ManagerSelectionItemVM> Items { get; }

	[DataSourceProperty]
	public int TitleFontSize => ScaleFont(25);

	[DataSourceProperty]
	public int PageTitleFontSize => ScaleFont(20);

	[DataSourceProperty]
	public int TabFontSize => ScaleFont(14);

	[DataSourceProperty]
	public int LabelFontSize => ScaleFont(13);

	[DataSourceProperty]
	public int EditorFontSize => ScaleFont(13);

	[DataSourceProperty]
	public int ListTitleFontSize => ScaleFont(14);

	[DataSourceProperty]
	public int ListSubtitleFontSize => ScaleFont(11);

	[DataSourceProperty]
	public int ValueFontSize => ScaleFont(13);

	[DataSourceProperty]
	public int ButtonFontSize => ScaleFont(13);

	[DataSourceProperty]
	public int StatusFontSize => ScaleFont(12);

	[DataSourceProperty]
	public string StatusText
	{
		get
		{
			return _statusText;
		}
		private set
		{
			_statusText = value ?? "";
			((ViewModel)this).OnPropertyChangedWithValue<string>(_statusText, "StatusText");
		}
	}

	[DataSourceProperty]
	public string EditorText
	{
		get
		{
			return _editorText;
		}
		set
		{
			string text = BoundRaw(value, 16777216);
			if (text != _editorText)
			{
				_editorText = text;
				((ViewModel)this).OnPropertyChangedWithValue<string>(text, "EditorText");
			}
		}
	}

	[DataSourceProperty]
	public string SecondaryText
	{
		get
		{
			return _secondaryText;
		}
		set
		{
			string text = Bound(value, 120000);
			if (text != _secondaryText)
			{
				_secondaryText = text;
				((ViewModel)this).OnPropertyChangedWithValue<string>(text, "SecondaryText");
			}
		}
	}

	[DataSourceProperty]
	public string KnowledgeSummaryText
	{
		get
		{
			return _knowledgeSummaryText;
		}
		private set
		{
			string text = Bound(value, 512);
			if (text != _knowledgeSummaryText)
			{
				_knowledgeSummaryText = text;
				((ViewModel)this).OnPropertyChangedWithValue<string>(text, "KnowledgeSummaryText");
			}
		}
	}

	[DataSourceProperty]
	public string NumberText
	{
		get
		{
			return _numberText;
		}
		set
		{
			string text = Bound(value, 16);
			if (text != _numberText)
			{
				_numberText = text;
				((ViewModel)this).OnPropertyChangedWithValue<string>(text, "NumberText");
			}
		}
	}

	[DataSourceProperty]
	public bool FlagValue
	{
		get
		{
			return _flagValue;
		}
		set
		{
			if (value != _flagValue)
			{
				_flagValue = value;
				((ViewModel)this).OnPropertyChangedWithValue(value, "FlagValue");
			}
		}
	}

	internal ChatterManagerVM(IChatterManagerDataSource dataSource, Action closed)
	{
		_dataSource = dataSource;
		_closed = closed;
		Items = new MBBindingList<ManagerSelectionItemVM>();
		RefreshResponsiveLayout(new UiCoordinateSpace(Screen.RealScreenResolutionWidth, Screen.RealScreenResolutionHeight, 1f));
		SwitchPage(ChatterManagerPage.Persona);
	}

	public void ExecuteShowPersona()
	{
		SwitchPage(ChatterManagerPage.Persona);
	}

	public void ExecuteShowLibrary()
	{
		SwitchPage(ChatterManagerPage.Memory);
	}

	public void ExecuteShowMemory()
	{
		SwitchPage(ChatterManagerPage.Memory);
	}

	public void ExecuteShowThought()
	{
		SwitchPage(ChatterManagerPage.Thought);
	}

	public void ExecuteShowKnowledge()
	{
		SwitchPage(ChatterManagerPage.Knowledge);
	}

	public void ExecuteShowPrompt()
	{
		SwitchPage(ChatterManagerPage.Prompt);
	}

	public void ExecuteClose()
	{
		_closed?.Invoke();
	}

	public void ExecuteGeneratePersona()
	{
		if (!IsPersonaPage || _personaGenerationBusy || string.IsNullOrWhiteSpace(_selectedHeroId))
		{
			StatusText = "请先选择要生成人格卡的角色。";
			return;
		}
		DiscardPersonaPreview(updateStatus: false);
		string text = Guid.NewGuid().ToString("N");
		string[] values = PersonaSecondaryLines();
		ManagerPersonaGenerationRequestData request = new ManagerPersonaGenerationRequestData
		{
			RequestId = text,
			HeroId = _selectedHeroId,
			ExistingPersona = EditorText,
			ExistingSpeakingStyle = Element(values, 0),
			ExistingLongTermGoal = Element(values, 1),
			ExistingValues = Element(values, 2),
			ExistingTaboos = Element(values, 3)
		};
		_personaGenerationRequestId = text;
		SetPersonaGenerationBusy(value: true);
		ManagerPersonaGenerationStartData managerPersonaGenerationStartData = _dataSource?.BeginPersonaGeneration(request, OnPersonaGenerationCompleted);
		if (managerPersonaGenerationStartData == null || !managerPersonaGenerationStartData.Started)
		{
			if (string.Equals(_personaGenerationRequestId, text, StringComparison.Ordinal))
			{
				_personaGenerationRequestId = string.Empty;
				SetPersonaGenerationBusy(value: false);
			}
			StatusText = managerPersonaGenerationStartData?.Status ?? "人格智能生成没有启动，原草稿未修改。";
		}
		else if (string.Equals(_personaGenerationRequestId, text, StringComparison.Ordinal))
		{
			StatusText = (string.IsNullOrWhiteSpace(managerPersonaGenerationStartData.Status) ? "正在用主模型生成人格草稿；不会自动保存。" : managerPersonaGenerationStartData.Status);
		}
	}

	public void ExecuteApplyPersonaPreview()
	{
		if (IsPersonaPage && _personaPreview != null)
		{
			EditorText = _personaPreview.Persona;
			SecondaryText = string.Join("\n", _personaPreview.SpeakingStyle, _personaPreview.LongTermGoal, _personaPreview.Values, _personaPreview.Taboos);
			DiscardPersonaPreview(updateStatus: false);
			StatusText = "已采用生成草稿，但尚未保存；你仍可继续编辑。";
		}
	}

	public void ExecuteDiscardPersonaPreview()
	{
		DiscardPersonaPreview(updateStatus: true);
	}

	public void ExecuteNew()
	{
		if (_page == ChatterManagerPage.Knowledge)
		{
			StatusText = "常识库不提供游戏内逐条编辑；请在游戏外编辑整库文本后粘贴替换。";
		}
		else if (_page == ChatterManagerPage.Prompt)
		{
			EditorText = "";
			StatusText = "当前提示词正文已清空为草稿；点击保存才会写入预设文件。";
		}
		else
		{
			ClearEditor();
			StatusText = "新建草稿；只有点击保存且数据源确认成功才会落入当前存档。";
		}
	}

	public void ExecuteDelete()
	{
		if (_page == ChatterManagerPage.Knowledge)
		{
			StatusText = "常识库只支持经过预览的整库替换，不提供游戏内清空或逐条删除。";
		}
		else if (_page == ChatterManagerPage.Prompt)
		{
			string status = "";
			bool flag = _dataSource?.ResetPromptTemplate(_selectedPromptId, out status) ?? false;
			StatusText = ((!string.IsNullOrWhiteSpace(status)) ? status : (flag ? "已恢复默认提示词。" : "恢复默认失败。"));
			if (flag)
			{
				RefreshPromptSelection();
			}
		}
		else
		{
			bool flag2 = ((_page != 0) ? (_dataSource?.DeleteMemory(_selectedRecordId) ?? false) : (_dataSource?.ClearPersona(_selectedHeroId) ?? false));
			StatusText = (flag2 ? "已删除并由存档数据源确认。" : "删除失败或当前存档数据源未连接。");
			if (flag2)
			{
				RefreshPage();
			}
		}
	}

	public void ExecuteSave()
	{
		if (_page == ChatterManagerPage.Knowledge)
		{
			StatusText = "当前整库预览为只读；请点击“读取整库剪贴板”，检查后确认替换。";
			return;
		}
		if (_page == ChatterManagerPage.Prompt)
		{
			if (SavePrompt())
			{
				RefreshPromptSelection();
			}
			return;
		}
		bool flag = ((_page == ChatterManagerPage.Persona) ? SavePersona() : ((_page == ChatterManagerPage.Memory) ? SaveMemory(thought: false) : ((_page == ChatterManagerPage.Thought) ? SaveMemory(thought: true) : SavePrompt())));
		StatusText = (flag ? "已保存并由存档数据源确认。" : "未保存：请检查选择、正文、数值，或确认当前存档数据源已连接。");
		if (flag)
		{
			RefreshPage();
		}
	}

	public void ExecuteToggleFlag()
	{
		FlagValue = !FlagValue;
		if (_page == ChatterManagerPage.Memory || _page == ChatterManagerPage.Thought)
		{
			bool flag = _dataSource?.SetMemoryPinned(_selectedRecordId, FlagValue) ?? false;
			StatusText = (flag ? "置顶状态已保存。" : "当前仅修改草稿；保存后才会落库。");
		}
	}

	public void ExecutePreviewKnowledgeClipboard()
	{
		if (IsKnowledgePage)
		{
			ClipboardTextTransferResult clipboardTextTransferResult = ReadClipboard();
			if (!clipboardTextTransferResult.Success)
			{
				DiscardImportPreview(updateStatus: false);
				StatusText = clipboardTextTransferResult.Diagnostic;
				return;
			}
			string text = clipboardTextTransferResult.Text;
			ManagerKnowledgeImportPreviewData managerKnowledgeImportPreviewData = _dataSource?.PreviewKnowledgeReplacement(text);
			_pendingImportContent = text;
			_importKind = ManagerImportKind.KnowledgeReplacement;
			_canConfirmImport = managerKnowledgeImportPreviewData?.CanCommit ?? false;
			_importPreviewTitle = "整库替换预览（尚未写入存档）";
			_importPreviewText = FormatKnowledgePreview(managerKnowledgeImportPreviewData) + FormatClipboardTransport(clipboardTextTransferResult);
			NotifyImportPreviewChanged();
			StatusText = ((!_canConfirmImport) ? "剪贴板内容未通过整库校验；旧常识库没有改动。" : (clipboardTextTransferResult.UsedFallback ? "已通过经校验的 TaleWorlds 回退读取常识库；确认后会一次性替换旧库。" : "已通过 Windows Unicode 剪贴板读取完整常识库；确认后会一次性替换旧库。"));
		}
	}

	public void ExecutePreviewPromptTextClipboard()
	{
		if (IsPromptPage)
		{
			ClipboardTextTransferResult clipboardTextTransferResult = ReadClipboard();
			if (!clipboardTextTransferResult.Success)
			{
				DiscardImportPreview(updateStatus: false);
				StatusText = clipboardTextTransferResult.Diagnostic;
			}
			else
			{
				string text = clipboardTextTransferResult.Text;
				ManagerPromptImportPreviewData preview = _dataSource?.PreviewPromptText(_selectedPromptId, text);
				BeginPromptPreview(ManagerImportKind.PromptText, "纯文本提示词预览（只替换当前模板）", text, preview, clipboardTextTransferResult);
			}
		}
	}

	public void ExecutePreviewPromptJsonClipboard()
	{
		if (IsPromptPage)
		{
			ClipboardTextTransferResult clipboardTextTransferResult = ReadClipboard();
			if (!clipboardTextTransferResult.Success)
			{
				DiscardImportPreview(updateStatus: false);
				StatusText = clipboardTextTransferResult.Diagnostic;
			}
			else
			{
				string text = clipboardTextTransferResult.Text;
				ManagerPromptImportPreviewData preview = _dataSource?.PreviewPromptJsonImport(_selectedPromptId, text);
				BeginPromptPreview(ManagerImportKind.PromptJson, "完整 JSON 导入预览（可能包含多个模板）", text, preview, clipboardTextTransferResult);
			}
		}
	}

	public void ExecuteImport()
	{
		if (IsKnowledgePage)
		{
			ExecutePreviewKnowledgeClipboard();
		}
		else if (IsPromptPage)
		{
			ExecutePreviewPromptTextClipboard();
		}
	}

	public void ExecuteConfirmImport()
	{
		if (!_canConfirmImport || _importKind == ManagerImportKind.None)
		{
			StatusText = "当前预览不能确认；旧内容未改动。";
			return;
		}
		string status = "";
		bool flag = ((_importKind != ManagerImportKind.KnowledgeReplacement) ? ((_importKind != ManagerImportKind.PromptJson) ? (_dataSource?.SavePromptTemplateText(_selectedPromptId, _pendingImportContent, out status) ?? false) : (_dataSource?.CommitPromptJsonImport(_selectedPromptId, _pendingImportContent, out status) ?? false)) : (_dataSource?.ReplaceKnowledgeLibrary(_pendingImportContent, out status) ?? false));
		StatusText = ((!string.IsNullOrWhiteSpace(status)) ? status : (flag ? "导入已确认。" : "确认失败；原内容未改动。"));
		if (flag)
		{
			ManagerImportKind importKind = _importKind;
			DiscardImportPreview(updateStatus: false);
			if (importKind == ManagerImportKind.KnowledgeReplacement)
			{
				RefreshPage();
			}
			else
			{
				RefreshPromptSelection();
			}
		}
	}

	public void ExecuteCancelImport()
	{
		DiscardImportPreview(updateStatus: true);
	}

	public void ExecuteExport()
	{
		string content = "";
		string status = "";
		bool flag;
		if (_page == ChatterManagerPage.Prompt)
		{
			flag = _dataSource != null && _dataSource.ExportPromptPreset(out content, out status);
		}
		else
		{
			ManagerKnowledgeLibraryData obj = _dataSource?.GetKnowledgeLibrary();
			content = obj?.KnowledgeText ?? "";
			flag = obj != null;
			status = (flag ? "已复制当前多行常识库到系统剪贴板。" : "无法读取当前常识库。");
		}
		if (flag && !string.IsNullOrWhiteSpace(content))
		{
			ClipboardTextTransferResult clipboardTextTransferResult = WindowsUnicodeClipboard.WriteTextOnCurrentUiThread(content);
			flag = clipboardTextTransferResult.Success;
			status = ((!flag) ? clipboardTextTransferResult.Diagnostic : (((_page == ChatterManagerPage.Prompt) ? "已复制提示词 JSON。" : "已复制当前多行常识库。") + (clipboardTextTransferResult.UsedFallback ? " 使用了 TaleWorlds 回退，并已逐字回读校验。" : " 使用 Windows CF_UNICODETEXT 写入。")));
		}
		StatusText = ((!string.IsNullOrWhiteSpace(status)) ? status : ((!flag) ? ((_page == ChatterManagerPage.Prompt) ? "提示词预设导出失败。" : "常识库导出失败。") : ((_page == ChatterManagerPage.Prompt) ? "已复制提示词 JSON 到剪贴板。" : "已复制多行常识库到剪贴板。")));
	}

	public void ExecuteImportFile()
	{
		if (_page != ChatterManagerPage.Prompt)
		{
			return;
		}
		StatusText = "正在等待玩家选择 .json 或 .txt 文件……";
		WindowsFileDialogService.OpenTextAsync(PromptTemplateStore.PresetDirectory, delegate(FileDialogResult result)
		{
			if (result == null)
			{
				StatusText = "文件操作没有返回结果。";
			}
			else if (!result.Succeeded)
			{
				StatusText = result.Status;
			}
			else
			{
				bool flag = string.Equals(Path.GetExtension(result.Path), ".json", StringComparison.OrdinalIgnoreCase);
				ManagerPromptImportPreviewData preview = ((!flag) ? _dataSource?.PreviewPromptText(_selectedPromptId, result.Content) : _dataSource?.PreviewPromptJsonImport(_selectedPromptId, result.Content));
				BeginPromptPreview(flag ? ManagerImportKind.PromptJson : ManagerImportKind.PromptText, (flag ? "完整 JSON" : "纯文本") + "文件导入预览：" + result.Path, result.Content, preview, null);
			}
		});
	}

	public void ExecuteExportFile()
	{
		if (_page != ChatterManagerPage.Prompt)
		{
			return;
		}
		string content = "";
		string status = "";
		if (_dataSource == null || !_dataSource.ExportPromptPreset(out content, out status))
		{
			StatusText = (string.IsNullOrWhiteSpace(status) ? "无法生成提示词预设。" : status);
			return;
		}
		StatusText = "正在等待玩家选择导出位置……";
		WindowsFileDialogService.SaveTextAsync(PromptTemplateStore.PresetDirectory, "prompt-preset.json", content, delegate(FileDialogResult result)
		{
			StatusText = result?.Status ?? "文件操作没有返回结果。";
		});
	}

	public void StartTyping()
	{
	}

	public void StopTyping()
	{
	}

	private void SwitchPage(ChatterManagerPage page)
	{
		CancelPersonaGeneration();
		DiscardPersonaPreview(updateStatus: false);
		DiscardImportPreview(updateStatus: false);
		_page = page;
		ClearEditor();
		NotifyPageChanged();
		RefreshPage();
	}

	private void NotifyPageChanged()
	{
		((ViewModel)this).OnPropertyChanged("PageTitle");
		((ViewModel)this).OnPropertyChanged("EditorLabel");
		((ViewModel)this).OnPropertyChanged("SecondaryLabel");
		((ViewModel)this).OnPropertyChanged("NumberLabel");
		((ViewModel)this).OnPropertyChanged("FlagLabel");
		((ViewModel)this).OnPropertyChanged("IsPersonaPage");
		((ViewModel)this).OnPropertyChanged("IsMemoryPage");
		((ViewModel)this).OnPropertyChanged("IsThoughtPage");
		((ViewModel)this).OnPropertyChanged("IsKnowledgePage");
		((ViewModel)this).OnPropertyChanged("IsPromptPage");
		((ViewModel)this).OnPropertyChanged("IsKnowledgeOrPromptPage");
		((ViewModel)this).OnPropertyChanged("IsLibraryPage");
		((ViewModel)this).OnPropertyChanged("ShowValueControls");
		((ViewModel)this).OnPropertyChanged("ShowCrudButtons");
		((ViewModel)this).OnPropertyChanged("ShowStandardEditors");
		((ViewModel)this).OnPropertyChanged("CanEditPrimary");
		((ViewModel)this).OnPropertyChanged("CanEditSecondary");
		((ViewModel)this).OnPropertyChanged("PrimaryEditorHeight");
		((ViewModel)this).OnPropertyChanged("PrimaryEditorTop");
		((ViewModel)this).OnPropertyChanged("SecondaryLabelTop");
		((ViewModel)this).OnPropertyChanged("SecondaryEditorTop");
		((ViewModel)this).OnPropertyChanged("SecondaryEditorHeight");
		((ViewModel)this).OnPropertyChanged("ValueControlTop");
		((ViewModel)this).OnPropertyChanged("StatusTop");
		((ViewModel)this).OnPropertyChanged("DeleteButtonText");
		NotifyPersonaGenerationChanged();
	}

	private void RefreshPage()
	{
		((Collection<ManagerSelectionItemVM>)(object)Items).Clear();
		if (_dataSource == null)
		{
			StatusText = "当前存档数据源未连接：可以查看四个页面，但不会伪造保存。";
			return;
		}
		if (_page == ChatterManagerPage.Prompt)
		{
			foreach (ManagerPromptTemplateData item in _dataSource.GetPromptTemplates() ?? Array.Empty<ManagerPromptTemplateData>())
			{
				((Collection<ManagerSelectionItemVM>)(object)Items).Add(new ManagerSelectionItemVM(item.TemplateId, item.DisplayName, item.HasActualPreview ? "已有实际请求预览" : "结构预览", SelectPrompt));
			}
			ManagerSelectionItemVM managerSelectionItemVM = ((IEnumerable<ManagerSelectionItemVM>)Items).FirstOrDefault((ManagerSelectionItemVM item) => string.Equals(item.Id, _selectedPromptId, StringComparison.Ordinal)) ?? ((IEnumerable<ManagerSelectionItemVM>)Items).FirstOrDefault();
			if (managerSelectionItemVM != null)
			{
				SelectPrompt(managerSelectionItemVM);
			}
			else
			{
				StatusText = "没有可管理的提示词模板。";
			}
			return;
		}
		if (_page == ChatterManagerPage.Knowledge)
		{
			ManagerKnowledgeLibraryData managerKnowledgeLibraryData = _dataSource.GetKnowledgeLibrary() ?? new ManagerKnowledgeLibraryData();
			EditorText = "";
			SecondaryText = "";
			KnowledgeSummaryText = ManagerTextPreviewPolicy.CreateKnowledgeSummary(managerKnowledgeLibraryData.RuleCount, managerKnowledgeLibraryData.CharacterCount);
			((Collection<ManagerSelectionItemVM>)(object)Items).Add(new ManagerSelectionItemVM("knowledge-library", "当前多行常识库", managerKnowledgeLibraryData.RuleCount + " 条 · " + managerKnowledgeLibraryData.CharacterCount + " 字符", SelectKnowledgeLibrary));
			StatusText = ((managerKnowledgeLibraryData.RuleCount == 0) ? "当前战役常识库为空；请从游戏外复制完整文本后读取剪贴板并确认。" : "当前战役唯一常识库为只读预览；不提供游戏内逐条增删改。");
			return;
		}
		foreach (ManagerHeroData item2 in _dataSource.GetHeroes() ?? Array.Empty<ManagerHeroData>())
		{
			((Collection<ManagerSelectionItemVM>)(object)Items).Add(new ManagerSelectionItemVM(item2.HeroId, item2.DisplayName, item2.IsMainHero ? "玩家" : "主队同伴", SelectHero));
		}
		StatusText = ((((Collection<ManagerSelectionItemVM>)(object)Items).Count == 0) ? "当前没有可管理的玩家/同伴。" : "请选择左侧角色。");
	}

	private void SelectHero(ManagerSelectionItemVM item)
	{
		if (!string.Equals(_selectedHeroId, item.Id, StringComparison.Ordinal))
		{
			CancelPersonaGeneration();
			DiscardPersonaPreview(updateStatus: false);
		}
		_selectedHeroId = item.Id;
		_selectedRecordId = "";
		if (_page == ChatterManagerPage.Persona)
		{
			ManagerPersonaData managerPersonaData = _dataSource?.GetPersona(item.Id) ?? new ManagerPersonaData
			{
				HeroId = item.Id
			};
			EditorText = managerPersonaData.Persona;
			SecondaryText = string.Join("\n", managerPersonaData.SpeakingStyle, managerPersonaData.LongTermGoal, managerPersonaData.Values, managerPersonaData.Taboos);
			NumberText = managerPersonaData.Chattiness.ToString("0.00", CultureInfo.InvariantCulture);
			FlagValue = managerPersonaData.AutoInitiate;
			StatusText = "正在编辑：" + item.TitleText;
			NotifyPersonaGenerationChanged();
		}
		else
		{
			LoadMemoryItems(item.Id, _page == ChatterManagerPage.Thought);
		}
	}

	private void LoadMemoryItems(string heroId, bool thoughtsOnly)
	{
		((Collection<ManagerSelectionItemVM>)(object)Items).Clear();
		foreach (ManagerMemoryData item in _dataSource?.GetMemories(heroId, "", thoughtsOnly) ?? Array.Empty<ManagerMemoryData>())
		{
			string text = (thoughtsOnly ? item.ThoughtTier : item.Layer);
			((Collection<ManagerSelectionItemVM>)(object)Items).Add(new ManagerSelectionItemVM(item.RecordId, (item.Pinned ? "★ " : "") + (string.IsNullOrWhiteSpace(item.About) ? item.Kind : item.About), text + " · " + Bound(item.Text, 50), SelectMemory));
		}
		StatusText = ((((Collection<ManagerSelectionItemVM>)(object)Items).Count == 0) ? "此角色当前没有该类记录；可点击新建。" : "请选择记录，或点击新建。");
	}

	private void SelectMemory(ManagerSelectionItemVM item)
	{
		_selectedRecordId = item.Id;
		ManagerMemoryData managerMemoryData = (_dataSource?.GetMemories(_selectedHeroId, "", _page == ChatterManagerPage.Thought) ?? Array.Empty<ManagerMemoryData>()).FirstOrDefault((ManagerMemoryData record) => record.RecordId == item.Id);
		if (managerMemoryData != null)
		{
			EditorText = managerMemoryData.Text;
			SecondaryText = ((_page == ChatterManagerPage.Thought) ? (managerMemoryData.ThoughtTier + "\n" + managerMemoryData.Sentiment) : managerMemoryData.Layer);
			NumberText = ((_page == ChatterManagerPage.Thought) ? managerMemoryData.Strength : managerMemoryData.Importance).ToString("0.00", CultureInfo.InvariantCulture);
			FlagValue = managerMemoryData.Pinned;
		}
	}

	private void SelectPrompt(ManagerSelectionItemVM item)
	{
		DiscardImportPreview(updateStatus: false);
		_selectedPromptId = item.Id;
		ManagerPromptTemplateData managerPromptTemplateData = _dataSource?.GetPromptTemplate(item.Id);
		if (managerPromptTemplateData != null)
		{
			EditorText = managerPromptTemplateData.Template;
			SecondaryText = managerPromptTemplateData.Preview;
			StatusText = (managerPromptTemplateData.HasActualPreview ? ("正在编辑：" + managerPromptTemplateData.DisplayName + "；下方为最后一次实际发送内容。") : ("正在编辑：" + managerPromptTemplateData.DisplayName + "；生成一次对应请求后可查看实际发送内容。"));
		}
	}

	private void SelectKnowledgeLibrary(ManagerSelectionItemVM item)
	{
		ManagerKnowledgeLibraryData managerKnowledgeLibraryData = _dataSource?.GetKnowledgeLibrary();
		if (managerKnowledgeLibraryData == null)
		{
			StatusText = "无法重新读取当前战役常识库。";
			return;
		}
		EditorText = "";
		SecondaryText = "";
		KnowledgeSummaryText = ManagerTextPreviewPolicy.CreateKnowledgeSummary(managerKnowledgeLibraryData.RuleCount, managerKnowledgeLibraryData.CharacterCount);
		StatusText = "已重新读取当前整库，只读预览没有修改存档。";
	}

	private void RefreshPromptSelection()
	{
		string selected = _selectedPromptId;
		RefreshPage();
		ManagerSelectionItemVM managerSelectionItemVM = ((IEnumerable<ManagerSelectionItemVM>)Items).FirstOrDefault((ManagerSelectionItemVM value) => value.Id == selected) ?? ((IEnumerable<ManagerSelectionItemVM>)Items).FirstOrDefault();
		if (managerSelectionItemVM != null)
		{
			SelectPrompt(managerSelectionItemVM);
		}
	}

	private bool SavePersona()
	{
		if (string.IsNullOrWhiteSpace(_selectedHeroId) || !TryParse01(NumberText, out var number))
		{
			return false;
		}
		string[] values = (SecondaryText ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		return _dataSource?.SavePersona(new ManagerPersonaData
		{
			HeroId = _selectedHeroId,
			Persona = EditorText,
			SpeakingStyle = Element(values, 0),
			LongTermGoal = Element(values, 1),
			Values = Element(values, 2),
			Taboos = Element(values, 3),
			Chattiness = number,
			AutoInitiate = FlagValue
		}) ?? false;
	}

	private void OnPersonaGenerationCompleted(ManagerPersonaGenerationResultData result)
	{
		if (result != null && string.Equals(result.RequestId, _personaGenerationRequestId, StringComparison.Ordinal) && string.Equals(result.HeroId, _selectedHeroId, StringComparison.Ordinal) && IsPersonaPage)
		{
			_personaGenerationRequestId = "";
			SetPersonaGenerationBusy(value: false);
			if (!result.Succeeded || result.Preview == null || string.IsNullOrWhiteSpace(result.Preview.Persona))
			{
				StatusText = "人格生成失败（" + Bound(result.DiagnosticCode, 80) + "），原草稿未修改。";
				return;
			}
			_personaPreview = result.Preview;
			_personaPreviewText = FormatPersonaPreview(result.Preview);
			StatusText = "人格草稿已生成：先预览，点“采用草稿”后仍需手动保存。";
			NotifyPersonaGenerationChanged();
		}
	}

	private void CancelPersonaGeneration()
	{
		string personaGenerationRequestId = _personaGenerationRequestId;
		_personaGenerationRequestId = "";
		if (!string.IsNullOrWhiteSpace(personaGenerationRequestId))
		{
			_dataSource?.CancelPersonaGeneration(personaGenerationRequestId);
		}
		SetPersonaGenerationBusy(value: false);
	}

	private void DiscardPersonaPreview(bool updateStatus)
	{
		bool flag = _personaPreview != null;
		_personaPreview = null;
		_personaPreviewText = "";
		NotifyPersonaGenerationChanged();
		if (updateStatus && flag)
		{
			StatusText = "已放弃生成草稿，原编辑内容未修改。";
		}
	}

	private void SetPersonaGenerationBusy(bool value)
	{
		if (_personaGenerationBusy != value)
		{
			_personaGenerationBusy = value;
			NotifyPersonaGenerationChanged();
		}
	}

	private void NotifyPersonaGenerationChanged()
	{
		((ViewModel)this).OnPropertyChanged("PersonaGenerationBusy");
		((ViewModel)this).OnPropertyChanged("HasPersonaPreview");
		((ViewModel)this).OnPropertyChanged("CanGeneratePersona");
		((ViewModel)this).OnPropertyChanged("PersonaGenerateButtonText");
		((ViewModel)this).OnPropertyChanged("PersonaPreviewText");
	}

	private string[] PersonaSecondaryLines()
	{
		return (SecondaryText ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
	}

	private static string FormatPersonaPreview(ManagerPersonaData value)
	{
		return "人格正文：" + value?.Persona + "\n说话风格：" + value?.SpeakingStyle + "\n长期目标：" + value?.LongTermGoal + "\n价值：" + value?.Values + "\n禁忌：" + value?.Taboos;
	}

	private void BeginPromptPreview(ManagerImportKind kind, string title, string content, ManagerPromptImportPreviewData preview, ClipboardTextTransferResult clipboard)
	{
		_pendingImportContent = content ?? "";
		_importKind = kind;
		_canConfirmImport = preview?.CanCommit ?? false;
		_importPreviewTitle = title ?? "提示词导入预览";
		_importPreviewText = ManagerTextPreviewPolicy.CreatePromptImportPreview(FormatPromptPreview(preview) + FormatClipboardTransport(clipboard));
		NotifyImportPreviewChanged();
		StatusText = (_canConfirmImport ? "已从系统剪贴板读取并解析；确认前不会覆盖现有提示词。" : "导入内容未通过校验；现有提示词没有改动。");
	}

	private void DiscardImportPreview(bool updateStatus)
	{
		bool flag = _importKind != ManagerImportKind.None;
		_importKind = ManagerImportKind.None;
		_pendingImportContent = "";
		_importPreviewTitle = "";
		_importPreviewText = "";
		_canConfirmImport = false;
		NotifyImportPreviewChanged();
		if (updateStatus && flag)
		{
			StatusText = "已取消导入；原内容未改动。";
		}
	}

	private void NotifyImportPreviewChanged()
	{
		((ViewModel)this).OnPropertyChanged("HasImportPreview");
		((ViewModel)this).OnPropertyChanged("CanConfirmImport");
		((ViewModel)this).OnPropertyChanged("ImportPreviewTitle");
		((ViewModel)this).OnPropertyChanged("ImportPreviewText");
		((ViewModel)this).OnPropertyChanged("ImportConfirmButtonText");
	}

	private static ClipboardTextTransferResult ReadClipboard()
	{
		return WindowsUnicodeClipboard.ReadTextOnCurrentUiThread();
	}

	private static string FormatClipboardTransport(ClipboardTextTransferResult value)
	{
		if (value == null || string.IsNullOrWhiteSpace(value.Diagnostic))
		{
			return "";
		}
		return "\n\n[剪贴板通道]\n" + value.Diagnostic;
	}

	private static string FormatKnowledgePreview(ManagerKnowledgeImportPreviewData value)
	{
		if (value == null)
		{
			return "无法连接常识库解析服务；旧库未改动。";
		}
		List<string> list = new List<string>();
		list.Add("旧库：" + value.ExistingCount + " 条");
		list.Add("剪贴板：" + value.SourceCharacterCount + " 字符，" + value.SourceLineCount + " 个源物理行");
		list.Add("完整五段式规则头：" + value.SourceRuleHeaderCount + " 个");
		list.Add("识别：" + value.ParsedCount + " 条");
		list.Add(value.CanCommit ? "状态：可以整体替换" : "状态：不可确认，旧库将保留");
		List<string> list2 = list;
		if (!string.IsNullOrWhiteSpace(value.Errors))
		{
			list2.Add("\n[错误]\n" + value.Errors);
		}
		if (!string.IsNullOrWhiteSpace(value.Warnings))
		{
			list2.Add("\n[警告]\n" + value.Warnings);
		}
		if (!string.IsNullOrWhiteSpace(value.NormalizedPreview))
		{
			list2.Add("\n[规范化整库预览]\n" + value.NormalizedPreview);
		}
		return ManagerTextPreviewPolicy.CreateKnowledgeImportPreview(string.Join("\n", list2));
	}

	private static string FormatPromptPreview(ManagerPromptImportPreviewData value)
	{
		if (value == null)
		{
			return "无法连接提示词解析服务；现有模板未改动。";
		}
		List<string> list = new List<string>
		{
			"剪贴板：" + value.SourceCharacterCount + " 字符",
			"识别模板：" + value.TemplateCount + (string.IsNullOrWhiteSpace(value.TemplateIds) ? "" : ("（" + value.TemplateIds + "）")),
			value.CanCommit ? "状态：可以确认" : "状态：不可确认，现有模板将保留"
		};
		if (!string.IsNullOrWhiteSpace(value.Errors))
		{
			list.Add("\n[错误]\n" + value.Errors);
		}
		if (!string.IsNullOrWhiteSpace(value.Warnings))
		{
			list.Add("\n[警告]\n" + value.Warnings);
		}
		if (!string.IsNullOrWhiteSpace(value.PreviewText))
		{
			list.Add("\n[导入内容预览]\n" + value.PreviewText);
		}
		return string.Join("\n", list);
	}

	internal void RefreshResponsiveLayout(UiCoordinateSpace coordinates)
	{
		int num = coordinates.ResponsiveWidth(1180, 760);
		int num2 = coordinates.ResponsiveHeight(760, 560);
		int num3 = Math.Max(12, (int)Math.Round((double)num * 22.0 / 1180.0));
		int num4 = Math.Max(160, (int)Math.Round((double)num2 * 162.0 / 760.0));
		int num5 = Math.Max(48, (int)Math.Round((double)num2 * 72.0 / 760.0));
		int num6 = Math.Max(300, num2 - num4 - num5);
		int num7 = Math.Max(210, (int)Math.Round((double)num * 330.0 / 1180.0));
		int num8 = Math.Max(460, num - num7 - num3 * 3);
		if (_managerWidth != num || _managerHeight != num2 || _managerHorizontalMargin != num3 || _managerContentTop != num4 || _managerContentHeight != num6 || _managerLeftWidth != num7 || _managerRightWidth != num8)
		{
			_managerWidth = num;
			_managerHeight = num2;
			_managerHorizontalMargin = num3;
			_managerContentTop = num4;
			_managerContentHeight = num6;
			_managerLeftWidth = num7;
			_managerRightWidth = num8;
			((ViewModel)this).OnPropertyChangedWithValue(_managerWidth, "ManagerWidth");
			((ViewModel)this).OnPropertyChangedWithValue(_managerHeight, "ManagerHeight");
			((ViewModel)this).OnPropertyChangedWithValue(_managerHorizontalMargin, "ManagerHorizontalMargin");
			((ViewModel)this).OnPropertyChangedWithValue(_managerContentTop, "ManagerContentTop");
			((ViewModel)this).OnPropertyChangedWithValue(_managerContentHeight, "ManagerContentHeight");
			((ViewModel)this).OnPropertyChangedWithValue(_managerLeftWidth, "ManagerLeftWidth");
			((ViewModel)this).OnPropertyChangedWithValue(_managerRightWidth, "ManagerRightWidth");
			((ViewModel)this).OnPropertyChanged("PrimaryEditorHeight");
			((ViewModel)this).OnPropertyChanged("PrimaryEditorTop");
			((ViewModel)this).OnPropertyChanged("SecondaryLabelTop");
			((ViewModel)this).OnPropertyChanged("SecondaryEditorTop");
			((ViewModel)this).OnPropertyChanged("SecondaryEditorHeight");
			((ViewModel)this).OnPropertyChanged("ValueControlTop");
			((ViewModel)this).OnPropertyChanged("StatusTop");
			((ViewModel)this).OnPropertyChanged("PersonaPreviewHeight");
			((ViewModel)this).OnPropertyChanged("ImportPreviewHeight");
		}
	}

	private int ScaleContent(int reference, int minimum)
	{
		return Math.Max(minimum, (int)Math.Round((double)(reference * ManagerContentHeight) / 526.0));
	}

	public override void OnFinalize()
	{
		CancelPersonaGeneration();
		DiscardPersonaPreview(updateStatus: false);
		DiscardImportPreview(updateStatus: false);
		((ViewModel)this).OnFinalize();
	}

	private bool SaveMemory(bool thought)
	{
		if (string.IsNullOrWhiteSpace(_selectedHeroId) || string.IsNullOrWhiteSpace(EditorText) || !TryParse01(NumberText, out var number))
		{
			return false;
		}
		string[] values = (SecondaryText ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		return _dataSource?.SaveMemory(new ManagerMemoryData
		{
			RecordId = _selectedRecordId,
			OwnerHeroId = _selectedHeroId,
			Text = EditorText,
			About = "玩家手工记录",
			Kind = (thought ? "思绪" : "Event"),
			Layer = (thought ? TierToLayer(Element(values, 0)) : NormalizeLayer(Element(values, 0))),
			ThoughtTier = (thought ? NormalizeTier(Element(values, 0)) : "None"),
			Sentiment = (thought ? NormalizeSentiment(Element(values, 1)) : "neutral"),
			Strength = (thought ? number : 0.5f),
			Importance = (thought ? number : number),
			Pinned = FlagValue
		}) ?? false;
	}

	private bool SavePrompt()
	{
		if (string.IsNullOrWhiteSpace(_selectedPromptId))
		{
			return false;
		}
		string status = "";
		bool flag = _dataSource?.SavePromptTemplateText(_selectedPromptId, EditorText, out status) ?? false;
		StatusText = ((!string.IsNullOrWhiteSpace(status)) ? status : (flag ? "提示词已保存。" : "提示词未保存。"));
		if (flag)
		{
			ManagerPromptTemplateData promptTemplate = _dataSource.GetPromptTemplate(_selectedPromptId);
			if (promptTemplate != null)
			{
				SecondaryText = promptTemplate.Preview;
			}
		}
		return flag;
	}

	private void ClearEditor()
	{
		_selectedRecordId = "";
		if (_page != ChatterManagerPage.Prompt)
		{
			_selectedPromptId = "";
		}
		EditorText = "";
		SecondaryText = "";
		KnowledgeSummaryText = "";
		NumberText = "0.50";
		FlagValue = _page == ChatterManagerPage.Persona;
	}

	private static bool TryParse01(string value, out float number)
	{
		if ((float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || float.TryParse(value, out number)) && number >= 0f)
		{
			return number <= 1f;
		}
		return false;
	}

	private static string Element(string[] values, int index)
	{
		if (values == null || index >= values.Length)
		{
			return "";
		}
		return values[index].Trim();
	}

	private static string Bound(string value, int maximum)
	{
		string text = (value ?? "").Replace('\0', ' ');
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum);
	}

	private static string BoundRaw(string value, int maximum)
	{
		string text = value ?? "";
		if (text.Length <= maximum)
		{
			return text;
		}
		return text.Substring(0, maximum);
	}

	private static string NormalizeLayer(string value)
	{
		switch ((value ?? "").Trim().ToLowerInvariant())
		{
		default:
			return "Situational";
		case "archive":
		case "long":
			return "Archive";
		case "eventlog":
		case "event":
			return "EventLog";
		case "recent":
			return "Recent";
		}
	}

	private static string NormalizeTier(string value)
	{
		string text = (value ?? "").Trim().ToLowerInvariant();
		if (!(text == "long"))
		{
			if (!(text == "belief"))
			{
				return "Mid";
			}
			return "Belief";
		}
		return "Long";
	}

	private static string TierToLayer(string value)
	{
		string text = NormalizeTier(value);
		if (!(text == "Long"))
		{
			if (!(text == "Belief"))
			{
				return "Situational";
			}
			return "Archive";
		}
		return "EventLog";
	}

	private static string NormalizeSentiment(string value)
	{
		string text = (value ?? "").Trim().ToLowerInvariant();
		if (!(text == "positive") && !(text == "negative"))
		{
			return "neutral";
		}
		return text;
	}

	private static int ScaleFont(int basis)
	{
		float val = GlobalSettings<ChatterMcmSettings>.Instance?.ManagerFontScale ?? 1f;
		val = Math.Max(0.7f, Math.Min(1.3f, val));
		return Math.Max(9, (int)Math.Round((float)basis * val));
	}
}
