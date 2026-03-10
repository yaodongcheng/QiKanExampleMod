using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using \u200E\u206E\u200D\u206A\u200B\u202D\u206C\u200C\u202A\u206F\u202A\u202D\u206F\u202C\u200D\u200B\u200C\u200E\u200F\u200C\u202A\u202C\u200E\u206F\u202E\u206B\u200E\u206E\u202E\u206E\u202D\u202C\u206D\u202E\u202D\u200E\u206C\u206A\u202D\u202C\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.UI.Widgets.CombatPhrases
{
	// Token: 0x020000FA RID: 250
	public class PopUpConversationListPanel : ListPanel
	{
		// Token: 0x06000815 RID: 2069 RVA: 0x000A78BC File Offset: 0x000A5ABC
		public PopUpConversationListPanel(UIContext context) : base(context)
		{
		}

		// Token: 0x1700019E RID: 414
		// (get) Token: 0x06000816 RID: 2070 RVA: 0x000A78E8 File Offset: 0x000A5AE8
		// (set) Token: 0x06000817 RID: 2071 RVA: 0x000A78FC File Offset: 0x000A5AFC
		public Widget TypeVisualWidget { get; set; }

		// Token: 0x1700019F RID: 415
		// (get) Token: 0x06000818 RID: 2072 RVA: 0x000A7910 File Offset: 0x000A5B10
		// (set) Token: 0x06000819 RID: 2073 RVA: 0x000A7924 File Offset: 0x000A5B24
		public TextWidget NameTextWidget { get; set; }

		// Token: 0x170001A0 RID: 416
		// (get) Token: 0x0600081A RID: 2074 RVA: 0x000A7938 File Offset: 0x000A5B38
		// (set) Token: 0x0600081B RID: 2075 RVA: 0x000A794C File Offset: 0x000A5B4C
		public float FontSize
		{
			get
			{
				return this._fontSize;
			}
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fontSize - value) > 0.001f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = 727550777;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-num2 - (-1855470948 + (1405257619 - 1635657384)) - 1402974054 + 451766261)) % 3U)
							{
							case 0U:
								goto IL_1E;
							case 2U:
								this._fontSize = value;
								this.UpdateFontSize();
								num = (int)(num3 * 2572362903U ^ 348641714U);
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170001A1 RID: 417
		// (get) Token: 0x0600081C RID: 2076 RVA: 0x000A79D4 File Offset: 0x000A5BD4
		// (set) Token: 0x0600081D RID: 2077 RVA: 0x000A79E8 File Offset: 0x000A5BE8
		public Vec2 ScreenPosition
		{
			get
			{
				return this._screenPosition;
			}
			set
			{
				bool flag = \u206E\u200C\u202A\u202A\u200E\u206E\u206B\u206E\u202B\u206F\u200E\u200C\u206F\u206C\u202C\u206A\u202B\u200E\u200C\u200B\u202B\u202E\u202C\u202C\u202E\u200B\u202C\u200B\u206F\u202C\u202D\u202C\u202E\u206B\u206C\u206C\u200E\u206D\u206B\u202B\u202E.\u00B4Ñïpç(this._screenPosition, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = 1833512493;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)((num2 - (-1259333879 * 201464514 - (-1751005487 * -1434775088 ^ -1237176285 + -1794488770)) + (~-1966941546 - 1339334514)) * -1794655241 * -457877017)) % 3U)
							{
							case 0U:
								goto IL_16;
							case 1U:
								this._screenPosition = value;
								this.UpdatePosition();
								num = (int)(num3 * 3725355693U ^ 3404501763U);
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170001A2 RID: 418
		// (get) Token: 0x0600081E RID: 2078 RVA: 0x000A7A88 File Offset: 0x000A5C88
		// (set) Token: 0x0600081F RID: 2079 RVA: 0x000A7A9C File Offset: 0x000A5C9C
		public float Distance
		{
			get
			{
				return this._distance;
			}
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._distance - value) > 0.001f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1391428443;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-(uint)(~(uint)((num2 ^ -169706685 * (-925867735 * 1215713461 + ~-527783352)) - (1811595251 + -1684235911))))) % 3U)
							{
							case 0U:
								goto IL_1E;
							case 1U:
								this._distance = value;
								this.UpdateScaleWithDistance();
								num = (int)(num3 * 4147678117U ^ 4084262905U);
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170001A3 RID: 419
		// (get) Token: 0x06000820 RID: 2080 RVA: 0x000A7B2C File Offset: 0x000A5D2C
		// (set) Token: 0x06000821 RID: 2081 RVA: 0x000A7B54 File Offset: 0x000A5D54
		[Editor(false)]
		public Vec2 Position
		{
			get
			{
				return new Vec2(\u202B\u206C\u200C\u206B\u200C\u206D\u206F\u200D\u206E\u200E\u206E\u200E\u202C\u202D\u200B\u202A\u206E\u200C\u206B\u202E\u206E\u202C\u202C\u202E\u202D\u206A\u202C\u200E\u206A\u200B\u202D\u206F\u200D\u202D\u200D\u206A\u200F\u206D\u200C\u206A\u202E.\u00B0zC\u0086\u00AF(this), \u202B\u206C\u200C\u206B\u200C\u206D\u206F\u200D\u206E\u200E\u206E\u200E\u202C\u202D\u200B\u202A\u206E\u200C\u206B\u202E\u206E\u202C\u202C\u202E\u202D\u206A\u202C\u200E\u206A\u200B\u202D\u206F\u200D\u202D\u200D\u206A\u200F\u206D\u200C\u206A\u202E.\u0080î\u00BC[\u0012(this));
			}
			set
			{
				this.ScreenPosition = value;
			}
		}

		// Token: 0x06000822 RID: 2082 RVA: 0x000A7B6C File Offset: 0x000A5D6C
		protected override void OnLateUpdate(float dt)
		{
			\u206D\u202E\u206B\u206D\u202C\u202C\u200C\u200F\u206C\u206A\u200C\u206D\u200B\u206E\u206D\u206A\u202C\u200F\u206B\u206E\u200C\u200F\u200D\u200D\u206B\u202E\u200F\u206B\u202D\u200F\u206A\u206D\u200B\u200F\u200E\u206D\u206C\u206E\u202D\u206E\u202E.È\u00B0F@\u00BE(this, dt);
			this.UpdateFontSize();
			this.UpdatePosition();
		}

		// Token: 0x06000823 RID: 2083 RVA: 0x000A7B98 File Offset: 0x000A5D98
		private void UpdateFontSize()
		{
			bool flag = this.NameTextWidget != null;
			if (flag)
			{
				for (;;)
				{
					IL_11:
					int num = -1539201020;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(1120057159 * 897709457 + (1310713667 + -1959497482) + (-2050455890 + (-347369757 ^ 316738838)) - num2 - (1596420109 * -850295040 + ~-519644874))) % 3U)
						{
						case 0U:
							goto IL_11;
						case 1U:
							\u206C\u202E\u206D\u200E\u206D\u202D\u202E\u206F\u206A\u202D\u200C\u200F\u200C\u200C\u202C\u200B\u206D\u200B\u202B\u202E\u206D\u202D\u200B\u202D\u206A\u202B\u202E\u200B\u202A\u200C\u202E\u200F\u200B\u200C\u200F\u202E\u206B\u206F\u206F\u200E\u202E.ýáj\u00AFù(\u206B\u206E\u200C\u202D\u200C\u206A\u206A\u200E\u200B\u206D\u202E\u200F\u200F\u200E\u206A\u200D\u200B\u206F\u206C\u200F\u200E\u202A\u206E\u206D\u200D\u206D\u202B\u200F\u202C\u206D\u200E\u202D\u202E\u200B\u200B\u202E\u206D\u202E\u200C\u206B\u202E.u\u008DKLÉ(this.NameTextWidget), (int)(this.FontSize * this._currentScale));
							num = (int)(num3 * 78190792U ^ 2453356459U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06000824 RID: 2084 RVA: 0x000A7C50 File Offset: 0x000A5E50
		private void UpdateScaleWithDistance()
		{
			bool flag = this.Distance <= 0f;
			if (flag)
			{
				goto IL_15;
			}
			goto IL_78;
			int num2;
			for (;;)
			{
				IL_1A:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((-691734272 + -153843274) * -951973277 - -num - 2015722787 * 619243428)) % 4U)
				{
				case 0U:
					goto IL_15;
				case 2U:
					this._currentScale = 0f;
					this.UpdateFontSize();
					num2 = (int)(num3 * 735570069U ^ 3897850549U);
					continue;
				case 3U:
					goto IL_78;
				}
				break;
			}
			return;
			IL_15:
			num2 = -623011664;
			goto IL_1A;
			IL_78:
			float num4 = \u200F\u200E\u202B\u206B\u200C\u202B\u200D\u200D\u202A\u200D\u202B\u202B\u202B\u200B\u206A\u200E\u206F\u202E\u200B\u206C\u200B\u206A\u202C\u202C\u206C\u200D\u202E\u206D\u206A\u200F\u206A\u206E\u206C\u200C\u200C\u206A\u200E\u206B\u206F\u206D\u202E.ï@Èc\u0086(1f - this.Distance / 80f, 0f, 1f);
			this._currentScale = 0.75f + 0.45000005f * num4;
			this.UpdateFontSize();
			num2 = -1052860629;
			goto IL_1A;
		}

		// Token: 0x06000825 RID: 2085 RVA: 0x000A7D20 File Offset: 0x000A5F20
		private void UpdatePosition()
		{
			TextWidget nameTextWidget = this.NameTextWidget;
			float num = (nameTextWidget != null) ? \u200C\u206A\u202C\u206E\u200C\u200E\u200C\u206F\u200C\u206E\u200E\u206E\u206C\u200B\u206E\u200B\u206E\u206A\u206F\u206D\u206A\u200C\u200C\u200B\u200E\u202C\u200F\u200B\u202C\u206C\u202D\u206E\u202D\u202C\u200F\u206D\u200E\u202D\u202B\u202E.\u206A\u206F\u206E\u200B\u200C\u202C\u202C\u206B\u206C\u206A\u206C\u202E\u202A\u202A\u202A\u200B\u202C\u206F\u202B\u206E\u206E\u202A\u206A\u202E\u202D\u206B\u206F\u200E\u202A\u206E\u206D\u200E\u200B\u202A\u202A\u200B\u206B\u200F\u206A\u206F\u202E(nameTextWidget).X : 0f;
			TextWidget nameTextWidget2 = this.NameTextWidget;
			float num2 = (nameTextWidget2 != null) ? \u200C\u206A\u202C\u206E\u200C\u200E\u200C\u206F\u200C\u206E\u200E\u206E\u206C\u200B\u206E\u200B\u206E\u206A\u206F\u206D\u206A\u200C\u200C\u200B\u200E\u202C\u200F\u200B\u202C\u206C\u202D\u206E\u202D\u202C\u200F\u206D\u200E\u202D\u202B\u202E.\u206A\u206F\u206E\u200B\u200C\u202C\u202C\u206B\u206C\u206A\u206C\u202E\u202A\u202A\u202A\u200B\u202C\u206F\u202B\u206E\u206E\u202A\u206A\u202E\u202D\u206B\u206F\u200E\u202A\u206E\u206D\u200E\u200B\u202A\u202A\u200B\u206B\u200F\u206A\u206F\u202E(nameTextWidget2).Y : 0f;
			float num3 = num * 0.5f;
			float num4 = num2;
			\u206D\u202E\u206B\u206D\u202C\u202C\u200C\u200F\u206C\u206A\u200C\u206D\u200B\u206E\u206D\u206A\u202C\u200F\u206B\u206E\u200C\u200F\u200D\u200D\u206B\u202E\u200F\u206B\u202D\u200F\u206A\u206D\u200B\u200F\u200E\u206D\u206C\u206E\u202D\u206E\u202E.Ñéîof(this, this._screenPosition.x - num3);
			\u206D\u202E\u206B\u206D\u202C\u202C\u200C\u200F\u206C\u206A\u200C\u206D\u200B\u206E\u206D\u206A\u202C\u200F\u206B\u206E\u200C\u200F\u200D\u200D\u206B\u202E\u200F\u206B\u202D\u200F\u206A\u206D\u200B\u200F\u200E\u206D\u206C\u206E\u202D\u206E\u202E.Q\u007F\u0009\u000D\u00D7(this, this._screenPosition.y - num4);
		}

		// Token: 0x040004F5 RID: 1269
		private Vec2 _screenPosition;

		// Token: 0x040004F6 RID: 1270
		private float _fontSize = 25f;

		// Token: 0x040004F7 RID: 1271
		private float _distance;

		// Token: 0x040004F8 RID: 1272
		private float _currentScale = 1f;
	}
}
