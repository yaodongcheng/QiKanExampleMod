using System;
using System.Runtime.CompilerServices;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using \u200B\u202C\u200E\u206A\u206C\u200E\u202A\u202D\u206C\u200E\u202C\u200E\u202A\u202C\u202E\u200C\u202A\u200E\u206B\u202B\u200C\u200F\u206B\u200D\u200F\u200B\u206D\u200C\u202B\u202C\u206E\u206E\u206E\u202E\u202B\u200E\u202D\u200B\u200B\u200E\u202E;

namespace AIInfluence.ViewModelMixin
{
	// Token: 0x020000F3 RID: 243
	[PrefabExtension("EncyclopediaHeroPage", "descendant::RichTextWidget[@Text='@InformationText']")]
	internal sealed class EncyclopediaHeroPagePrefabExtension : PrefabExtensionInsertPatch
	{
		// Token: 0x1700019D RID: 413
		// (get) Token: 0x060007D1 RID: 2001 RVA: 0x000A1438 File Offset: 0x0009F638
		public override InsertType Type
		{
			get
			{
				return 4;
			}
		}

		// Token: 0x060007D2 RID: 2002 RVA: 0x000A1448 File Offset: 0x0009F648
		[MethodImpl(MethodImplOptions.NoInlining)]
		public EncyclopediaHeroPagePrefabExtension()
		{
			this._document = \u206B\u206D\u200F\u200D\u202A\u200D\u200E\u202A\u202B\u200D\u202B\u200B\u200D\u206B\u206F\u200F\u200E\u206A\u200C\u202D\u200F\u202A\u206C\u206E\u202A\u206C\u202E\u206E\u200E\u206D\u200C\u206C\u206F\u200C\u206F\u200C\u206C\u202D\u200C\u202E.UÂ\u0097\u00ABB();
			\u202D\u200D\u200D\u206C\u200E\u202E\u206B\u200C\u206B\u206A\u200B\u202A\u206A\u200F\u200E\u206D\u200C\u202E\u200B\u200E\u206C\u200C\u206F\u206A\u206E\u200E\u206C\u202A\u200F\u206F\u206F\u206F\u206E\u202C\u202D\u200B\u202A\u202E\u202D\u202A\u202E.w\u001BR\u0009~(this._document, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-103701732));
		}

		// Token: 0x060007D3 RID: 2003 RVA: 0x000A148C File Offset: 0x0009F68C
		[PrefabExtensionInsertPatch.PrefabExtensionXmlDocumentAttribute(false)]
		public XmlDocument GetPrefabExtension()
		{
			return this._document;
		}

		// Token: 0x040004E2 RID: 1250
		private readonly XmlDocument _document;
	}
}
