using System;

namespace BannerlordTalk.UI;

internal static class ManagerTextPreviewPolicy
{
	internal const int KnowledgePreviewCharacterLimit = 6000;

	internal const int KnowledgeSummaryCharacterLimit = 512;

	internal static string CreateKnowledgeSummary(int ruleCount, int characterCount)
	{
		string text = "当前多行常识库\n\n规则数：" + Math.Max(0, ruleCount) + "\n字符数：" + Math.Max(0, characterCount) + "\n\n为保持界面流畅，常识正文不在本页渲染。\n需要完整内容请点击“复制当前整库”。";
		if (text.Length > 512)
		{
			return text.Substring(0, 512);
		}
		return text;
	}

	internal static string CreateKnowledgeLibraryPreview(string value)
	{
		return CreateBoundedPreview(value, "[界面预览已截断；完整常识库仍保存在当前战役中。需要全文请点击“复制当前整库”。]");
	}

	internal static string CreateKnowledgeImportPreview(string value)
	{
		return CreateBoundedPreview(value, "[界面预览已截断；确认替换时仍会使用完整剪贴板内容，不会只导入上面的片段。]");
	}

	internal static string CreatePromptImportPreview(string value)
	{
		return CreateBoundedPreview(value, "[界面预览已截断；确认导入时仍会使用完整提示词内容，不会只保存上面的片段。]");
	}

	private static string CreateBoundedPreview(string value, string notice)
	{
		string text = value ?? "";
		if (text.Length <= 6000)
		{
			return text;
		}
		string text2 = "\n\n" + notice + "\n完整内容字符数：" + text.Length + "。";
		int num = Math.Max(0, 6000 - text2.Length);
		if (num > 0 && num < text.Length && char.IsHighSurrogate(text[num - 1]) && char.IsLowSurrogate(text[num]))
		{
			num--;
		}
		int num2 = text.LastIndexOfAny(new char[2] { '\r', '\n' }, num - 1);
		if (num2 >= num / 2)
		{
			num = num2;
		}
		return text.Substring(0, num).TrimEnd() + text2;
	}
}
