using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using \u200E\u206E\u200D\u206A\u200B\u202D\u206C\u200C\u202A\u206F\u202A\u202D\u206F\u202C\u200D\u200B\u200C\u200E\u200F\u200C\u202A\u202C\u200E\u206F\u202E\u206B\u200E\u206E\u202E\u206E\u202D\u202C\u206D\u202E\u202D\u200E\u206C\u206A\u202D\u202C\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.UI.Widgets.CombatPhrases
{
	// Token: 0x020000FB RID: 251
	public class PopUpConversationScreenWidget : Widget
	{
		// Token: 0x06000826 RID: 2086 RVA: 0x000A7DA4 File Offset: 0x000A5FA4
		public PopUpConversationScreenWidget(UIContext context) : base(context)
		{
		}

		// Token: 0x170001A4 RID: 420
		// (get) Token: 0x06000827 RID: 2087 RVA: 0x000A7DD8 File Offset: 0x000A5FD8
		// (set) Token: 0x06000828 RID: 2088 RVA: 0x000A7DEC File Offset: 0x000A5FEC
		public bool IsMarkersEnabled
		{
			get
			{
				return this._isMarkersEnabled;
			}
			set
			{
				bool flag = this._isMarkersEnabled != value;
				for (;;)
				{
					IL_0E:
					int num = 480256227;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(~-1691669209) - (~(~(-848756365 ^ -2097913075)) - num2))) % 4U)
						{
						case 1U:
							num = (int)((flag ? 2040119955U : 2650687881U) ^ num3 * 3999287899U);
							continue;
						case 2U:
							this._isMarkersEnabled = value;
							this.UpdateMarkersVisibility();
							num = (int)(num3 * 464490013U ^ 553560200U);
							continue;
						case 3U:
							goto IL_0E;
						}
						return;
					}
				}
			}
		}

		// Token: 0x170001A5 RID: 421
		// (get) Token: 0x06000829 RID: 2089 RVA: 0x000A7E7C File Offset: 0x000A607C
		// (set) Token: 0x0600082A RID: 2090 RVA: 0x000A7E90 File Offset: 0x000A6090
		public float TargetAlphaValue
		{
			get
			{
				return this._targetAlphaValue;
			}
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._targetAlphaValue - value) > 0.001f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1460401873;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(~(uint)((~num2 ^ ~(-599026872 - -203279311)) + ~1414354825))) % 3U)
							{
							case 1U:
								this._targetAlphaValue = value;
								num = (int)(num3 * 3207965675U ^ 1842553562U);
								continue;
							case 2U:
								goto IL_1E;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170001A6 RID: 422
		// (get) Token: 0x0600082B RID: 2091 RVA: 0x000A7F08 File Offset: 0x000A6108
		// (set) Token: 0x0600082C RID: 2092 RVA: 0x000A7F1C File Offset: 0x000A611C
		public Widget MarkersContainer
		{
			get
			{
				return this._markersContainer;
			}
			set
			{
				bool flag = this._markersContainer == value;
				if (flag)
				{
					goto IL_0E;
				}
				goto IL_61;
				int num2;
				for (;;)
				{
					IL_13:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(~(-(num ^ 1946918089 * ~1227029654 - --1110080603 * 1654097461)) ^ -1940805442)) % 4U)
					{
					case 0U:
						goto IL_0E;
					case 1U:
						num2 = (int)(num3 * 3282643370U ^ 4124330845U);
						continue;
					case 3U:
						goto IL_61;
					}
					break;
				}
				return;
				IL_0E:
				num2 = 1807816494;
				goto IL_13;
				IL_61:
				this._markersContainer = value;
				this.RefreshRegisteredMarkers();
				num2 = 358770943;
				goto IL_13;
			}
		}

		// Token: 0x0600082D RID: 2093 RVA: 0x000A7FA0 File Offset: 0x000A61A0
		protected override void OnLateUpdate(float dt)
		{
			\u206D\u202E\u206B\u206D\u202C\u202C\u200C\u200F\u206C\u206A\u200C\u206D\u200B\u206E\u206D\u206A\u202C\u200F\u206B\u206E\u200C\u200F\u200D\u200D\u206B\u202E\u200F\u206B\u202D\u200F\u206A\u206D\u200B\u200F\u200E\u206D\u206C\u206E\u202D\u206E\u202E.È\u00B0F@\u00BE(this, dt);
			this.RefreshRegisteredMarkers();
			bool flag = this._registeredMarkers.Count > 0;
			float num = (this._isMarkersEnabled && flag) ? this._targetAlphaValue : 0f;
			float num2 = \u200F\u200E\u202B\u206B\u200C\u202B\u200D\u200D\u202A\u200D\u202B\u202B\u202B\u200B\u206A\u200E\u206F\u202E\u200B\u206C\u200B\u206A\u202C\u202C\u206C\u200D\u202E\u206D\u206A\u200F\u206A\u206E\u206C\u200C\u200C\u206A\u200E\u206B\u206F\u206D\u202E.ï@Èc\u0086(dt * 10f, 0f, 1f);
			\u206D\u202E\u206B\u206D\u202C\u202C\u200C\u200F\u206C\u206A\u200C\u206D\u200B\u206E\u206D\u206A\u202C\u200F\u206B\u206E\u200C\u200F\u200D\u200D\u206B\u202E\u200F\u206B\u202D\u200F\u206A\u206D\u200B\u200F\u200E\u206D\u206C\u206E\u202D\u206E\u202E.\u00BDÎ\u00B9(\u0010(this, \u206F\u206A\u200F\u200E\u200C\u200D\u206E\u202C\u202D\u206E\u200E\u206E\u206D\u202A\u202E\u206F\u202A\u200F\u200F\u206B\u202B\u202B\u202C\u206E\u200E\u202A\u202A\u200C\u206C\u200F\u202E\u206D\u206B\u202E\u202B\u206B\u202D\u206B\u206B\u206B\u202E.\u001Ai\u00B3\u001D\u0089(\u202B\u206C\u200C\u206B\u200C\u206D\u206F\u200D\u206E\u200E\u206E\u200E\u202C\u202D\u200B\u202A\u206E\u200C\u206B\u202E\u206E\u202C\u202C\u202E\u202D\u206A\u202C\u200E\u206A\u200B\u202D\u206F\u200D\u202D\u200D\u206A\u200F\u206D\u200C\u206A\u202E.\u001F/Âà3(this), num, num2, 1E-05f));
			using (List<PopUpConversationListPanel>.Enumerator enumerator = this._registeredMarkers.GetEnumerator())
			{
				for (;;)
				{
					IL_F9:
					int num3 = (!enumerator.MoveNext()) ? 1562885039 : -107210695;
					for (;;)
					{
						int num4 = num3;
						switch (((~(-1487971984 ^ -1680714295) - ~num4 ^ (-114087200 ^ -1761947782)) - -803429199) % 4)
						{
						case 0:
							num3 = -107210695;
							continue;
						case 1:
						{
							PopUpConversationListPanel popUpConversationListPanel = enumerator.Current;
							\u206F\u202B\u200E\u200F\u202D\u200D\u202A\u206B\u206E\u202A\u200D\u206A\u202E\u206B\u206B\u206A\u202B\u206C\u202B\u206F\u202B\u200E\u206B\u200E\u200C\u202E\u202C\u206F\u206C\u206B\u200B\u206F\u206C\u200B\u200F\u202D\u202E\u202A\u206F\u202E.\u009FâwR\u0098(popUpConversationListPanel, this._isMarkersEnabled);
							num3 = 1747276450;
							continue;
						}
						case 2:
							goto IL_F9;
						}
						goto Block_4;
					}
				}
				Block_4:;
			}
		}

		// Token: 0x0600082E RID: 2094 RVA: 0x000A80E4 File Offset: 0x000A62E4
		private void RefreshRegisteredMarkers()
		{
			bool flag = this._markersContainer == null;
			if (flag)
			{
				goto IL_0E;
			}
			goto IL_42;
			int num2;
			for (;;)
			{
				IL_13:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)(~(uint)(~(uint)num)))) % 4U)
				{
				case 1U:
					goto IL_42;
				case 2U:
					num2 = (int)(num3 * 3737806988U ^ 133071196U);
					continue;
				case 3U:
					goto IL_0E;
				}
				break;
			}
			return;
			IL_0E:
			num2 = -36548986;
			goto IL_13;
			IL_42:
			this._registeredMarkers.Clear();
			this._registeredMarkers.AddRange(Enumerable.OfType<PopUpConversationListPanel>(\u200D\u200E\u200B\u200D\u206C\u206D\u200E\u202B\u200B\u202A\u200E\u206C\u202B\u206F\u206F\u200D\u200D\u200C\u202E\u202E\u200C\u206C\u206D\u202A\u202E\u200B\u202E\u202A\u206C\u206E\u202E\u202E\u202B\u206E\u206E\u206C\u202E\u206A\u200D\u206E\u202E.\u0013ñ\u0015ÓQ(this._markersContainer)));
			this._targetAlphaValue = ((this._registeredMarkers.Count > 0) ? 1f : 0f);
			num2 = -1867243548;
			goto IL_13;
		}

		// Token: 0x0600082F RID: 2095 RVA: 0x000A818C File Offset: 0x000A638C
		private void UpdateMarkersVisibility()
		{
			using (List<PopUpConversationListPanel>.Enumerator enumerator = this._registeredMarkers.GetEnumerator())
			{
				for (;;)
				{
					IL_81:
					int num = (!enumerator.MoveNext()) ? 1822441730 : 1737723452;
					for (;;)
					{
						int num2 = num;
						switch ((472499881 * -829540810 * 1019808987 + ~(2135478358 - 1263690315) - num2 ^ ~(873765777 + -716811522)) % 4)
						{
						case 1:
							goto IL_81;
						case 2:
						{
							PopUpConversationListPanel popUpConversationListPanel = enumerator.Current;
							\u206F\u202B\u200E\u200F\u202D\u200D\u202A\u206B\u206E\u202A\u200D\u206A\u202E\u206B\u206B\u206A\u202B\u206C\u202B\u206F\u202B\u200E\u206B\u200E\u200C\u202E\u202C\u206F\u206C\u206B\u200B\u206F\u206C\u200B\u200F\u202D\u202E\u202A\u206F\u202E.\u009FâwR\u0098(popUpConversationListPanel, this._isMarkersEnabled);
							num = 595726669;
							continue;
						}
						case 3:
							num = 1737723452;
							continue;
						}
						goto Block_3;
					}
				}
				Block_3:;
			}
		}

		// Token: 0x040004FB RID: 1275
		private readonly List<PopUpConversationListPanel> _registeredMarkers = new List<PopUpConversationListPanel>();

		// Token: 0x040004FC RID: 1276
		private Widget _markersContainer;

		// Token: 0x040004FD RID: 1277
		private bool _isMarkersEnabled = true;

		// Token: 0x040004FE RID: 1278
		private float _targetAlphaValue = 0f;
	}
}
