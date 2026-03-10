using System;
using System.Runtime.CompilerServices;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using \u200E\u206E\u200D\u206A\u200B\u202D\u206C\u200C\u202A\u206F\u202A\u202D\u206F\u202C\u200D\u200B\u200C\u200E\u200F\u200C\u202A\u202C\u200E\u206F\u202E\u206B\u200E\u206E\u202E\u206E\u202D\u202C\u206D\u202E\u202D\u200E\u206C\u206A\u202D\u202C\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;
using \u202E\u206D\u206C\u200F\u200C\u200D\u202D\u200E\u202C\u206D\u200E\u206E\u206B\u206D\u206D\u202E\u206D\u200D\u200B\u206E\u202A\u202D\u200D\u206A\u200F\u206E\u206C\u200D\u206F\u206A\u206A\u206A\u202A\u206D\u200C\u200F\u206F\u200B\u206C\u200F\u202E;
using \u206F\u202A\u206F\u202C\u206D\u202C\u200B\u200D\u206E\u206B\u202A\u206B\u206D\u200F\u206B\u202B\u200F\u200E\u202B\u206B\u202B\u200B\u206D\u202B\u202D\u202B\u202A\u200F\u202C\u202E\u200D\u200B\u206B\u206E\u202A\u206A\u202D\u200B\u200F\u206C\u202E;

namespace AIInfluence.SettlementCombat.PhrasesDisplay.Popup
{
	// Token: 0x0200015E RID: 350
	public class CombatPhrasePopupItemVM : ViewModel
	{
		// Token: 0x06000B1D RID: 2845 RVA: 0x000C735C File Offset: 0x000C555C
		public CombatPhrasePopupItemVM(Agent agent, string text, bool isEnemy, float fontSize = 16f)
		{
			this._agent = agent;
			this._text = text;
			this._isEnemy = isEnemy;
			this._fontSize = fontSize;
		}

		// Token: 0x1700021E RID: 542
		// (get) Token: 0x06000B1E RID: 2846 RVA: 0x000C73B0 File Offset: 0x000C55B0
		// (set) Token: 0x06000B1F RID: 2847 RVA: 0x000C73C4 File Offset: 0x000C55C4
		[DataSourceProperty]
		public Vec2 ScreenPosition
		{
			get
			{
				return this._screenPosition;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206E\u200C\u202A\u202A\u200E\u206E\u206B\u206E\u202B\u206F\u200E\u200C\u206F\u206C\u202C\u206A\u202B\u200E\u200C\u200B\u202B\u202E\u202C\u202C\u202E\u200B\u202C\u200B\u206F\u202C\u202D\u202C\u202E\u206B\u206C\u206C\u200E\u206D\u206B\u202B\u202E.\u00B4Ñïpç(this._screenPosition, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = 1031343914;
						for (;;)
						{
							int num2 = num;
							switch (~(num2 - -(-(53466659 + 1795411221))) % 3)
							{
							case 0:
								goto IL_16;
							case 2:
								this._screenPosition = value;
								\u202B\u206B\u200B\u200E\u200D\u206B\u202E\u200C\u202C\u206C\u200E\u206D\u200E\u200D\u202E\u202A\u202E\u206C\u200F\u206C\u206D\u200B\u206A\u200C\u206C\u200B\u202E\u206B\u202C\u200C\u206F\u200C\u200C\u206B\u206F\u200C\u200D\u206E\u200F\u206A\u202E.qÍÑë\u000F(this, value, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-767848214));
								num = 522503049;
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700021F RID: 543
		// (get) Token: 0x06000B20 RID: 2848 RVA: 0x000C7440 File Offset: 0x000C5640
		// (set) Token: 0x06000B21 RID: 2849 RVA: 0x000C7454 File Offset: 0x000C5654
		[DataSourceProperty]
		public string Name
		{
			get
			{
				return this._text;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.I\u001D\u00BF\u000B\u0085(this._text, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = -1271825296;
						for (;;)
						{
							int num2 = num;
							switch (~(-((num2 ^ ~1264514332 - (-1500426253 * 1773951940 ^ -2032696357 - 518220029)) * 1280977539)) % 3)
							{
							case 1:
								this._text = value;
								base.OnPropertyChangedWithValue<string>(value, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1451899856));
								num = 1793137458;
								continue;
							case 2:
								goto IL_16;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000220 RID: 544
		// (get) Token: 0x06000B22 RID: 2850 RVA: 0x000C74E0 File Offset: 0x000C56E0
		// (set) Token: 0x06000B23 RID: 2851 RVA: 0x000C74F4 File Offset: 0x000C56F4
		[DataSourceProperty]
		public bool IsEnemy
		{
			get
			{
				return this._isEnemy;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._isEnemy != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -660630047;
						for (;;)
						{
							int num2 = num;
							switch ((-(num2 ^ -(-44889726 - 738161172 * -1517739109)) - (-517091589 + -1860646040)) % 3)
							{
							case 1:
								this._isEnemy = value;
								\u200B\u200B\u206B\u206E\u206A\u202D\u202B\u206E\u206E\u206D\u202D\u202E\u200E\u206B\u200B\u206C\u200F\u200F\u200D\u200C\u206F\u206F\u202D\u202E\u206D\u200D\u206A\u206A\u202B\u206D\u202B\u206F\u206E\u206E\u202E\u202B\u202E\u200E\u206C\u206E\u202E.]Þvgæ(this, value, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1402621269));
								num = -153496454;
								continue;
							case 2:
								goto IL_11;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000221 RID: 545
		// (get) Token: 0x06000B24 RID: 2852 RVA: 0x000C757C File Offset: 0x000C577C
		// (set) Token: 0x06000B25 RID: 2853 RVA: 0x000C7590 File Offset: 0x000C5790
		[DataSourceProperty]
		public int Distance
		{
			get
			{
				return this._distance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._distance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1566559083;
						for (;;)
						{
							int num2 = num;
							switch (-(~(1097988539 * (1401654175 ^ -273854681)) - num2) % 3)
							{
							case 0:
								goto IL_11;
							case 1:
								this._distance = value;
								\u200C\u200C\u206D\u200B\u202A\u206F\u206E\u206D\u202D\u206C\u202D\u202E\u206A\u202D\u202C\u200D\u200B\u206C\u202B\u200D\u200D\u206C\u206B\u202B\u200D\u200D\u200E\u200D\u206E\u206B\u202D\u200E\u200D\u200F\u206D\u206E\u206D\u206D\u202E\u202C\u202E.bH\u0005Ïp(this, value, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1864824530));
								num = -1654720965;
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000222 RID: 546
		// (get) Token: 0x06000B26 RID: 2854 RVA: 0x000C760C File Offset: 0x000C580C
		// (set) Token: 0x06000B27 RID: 2855 RVA: 0x000C7620 File Offset: 0x000C5820
		[DataSourceProperty]
		public float FontSize
		{
			get
			{
				return this._fontSize;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fontSize - value) > 0.001f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1563466413;
						for (;;)
						{
							int num2 = num;
							switch (~(-(num2 ^ 1806374431 * (1740486850 ^ 1337844493)) + --96785754) % 3)
							{
							case 1:
								this._fontSize = value;
								\u206C\u206B\u206F\u202D\u200F\u206E\u206C\u202C\u200B\u200F\u200B\u202B\u206C\u206A\u200E\u200C\u200F\u202D\u202B\u202B\u200F\u200C\u206F\u202D\u200D\u206D\u200C\u202C\u200B\u202D\u206E\u200B\u202C\u206C\u206A\u206D\u202C\u206C\u206A\u202D\u202E.+\u0080\u008B\u00A0\u00F7(this, value, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1869234957));
								\u200F\u202E\u206A\u202B\u200E\u202C\u206F\u206C\u200C\u206C\u200D\u206B\u202B\u206D\u206C\u200E\u202A\u206D\u206E\u202A\u200D\u206C\u202A\u200B\u206B\u200B\u206C\u200B\u200E\u200C\u200D\u202C\u202E\u200B\u206D\u202D\u200B\u200D\u202C\u200C\u202E.õ\u0019\u008AÏ\u008C(this, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1160728392));
								num = -39490472;
								continue;
							case 2:
								goto IL_1E;
							}
							goto Block_1;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000223 RID: 547
		// (get) Token: 0x06000B28 RID: 2856 RVA: 0x000C76C8 File Offset: 0x000C58C8
		[DataSourceProperty]
		public int FontSizeInt
		{
			get
			{
				return (int)\u200F\u206F\u200E\u206A\u206B\u206D\u206F\u202B\u206B\u200D\u206F\u202C\u202C\u200F\u206B\u200C\u200C\u200D\u206F\u200C\u206A\u206E\u202E\u200C\u200C\u202E\u200F\u206E\u202B\u202A\u206E\u206D\u206A\u202E\u200F\u202A\u206C\u200E\u206B\u206C\u202E.6NÖwW((double)this._fontSize);
			}
		}

		// Token: 0x17000224 RID: 548
		// (get) Token: 0x06000B29 RID: 2857 RVA: 0x000C76E8 File Offset: 0x000C58E8
		public Agent Agent
		{
			get
			{
				return this._agent;
			}
		}

		// Token: 0x06000B2A RID: 2858 RVA: 0x000C76FC File Offset: 0x000C58FC
		public void Update(Camera camera)
		{
			bool flag = this._agent == null;
			if (flag)
			{
				goto IL_13;
			}
			goto IL_96;
			int num2;
			float a;
			float b;
			Vec3 vec;
			for (;;)
			{
				IL_18:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((num ^ ~(-2048126365 - -781546781 * 1559927397)) * 1009241989 - (895350954 - 2011733006) + -859305333)) % 6U)
				{
				case 0U:
					this.ScreenPosition = new Vec2(-500f, -500f);
					this.Distance = -1;
					num2 = 143142232;
					continue;
				case 1U:
					this.ScreenPosition = new Vec2(a, b);
					this.Distance = (int)\u206F\u202D\u206F\u206B\u200F\u200B\u202C\u200B\u200B\u206C\u206E\u202C\u202C\u200D\u202C\u200D\u206F\u200C\u200D\u206E\u200C\u206A\u206D\u202C\u206E\u200B\u206F\u200E\u202E\u200E\u202B\u206E\u202C\u202B\u202D\u200F\u200B\u202D\u202C\u206D\u202E.ë"\u00A8\u0017\u0093(vec, \u206F\u206E\u202D\u200B\u206D\u206C\u206D\u206F\u206C\u206D\u200E\u200C\u206C\u206E\u206A\u200B\u206B\u202A\u206D\u200C\u202B\u202D\u200E\u200C\u202D\u202D\u206F\u200F\u206C\u202D\u202B\u200C\u200E\u202B\u206C\u206C\u206D\u202C\u206B\u206D\u202E.é\u00D7í\u00815(camera)).Length;
					num2 = (int)(num3 * 3120386855U ^ 2969465819U);
					continue;
				case 3U:
					this.ScreenPosition = new Vec2(-500f, -500f);
					num2 = (int)(num3 * 548352802U ^ 603484890U);
					continue;
				case 4U:
					goto IL_96;
				case 5U:
					goto IL_13;
				}
				break;
			}
			return;
			IL_13:
			num2 = -767460879;
			goto IL_18;
			IL_96:
			vec = \u206F\u202D\u206F\u206B\u200F\u200B\u202C\u200B\u200B\u206C\u206E\u202C\u202C\u200D\u202C\u200D\u206F\u200C\u200D\u206E\u200C\u206A\u206D\u202C\u206E\u200B\u206F\u200E\u202E\u200E\u202B\u206E\u202C\u202B\u202D\u200F\u200B\u202D\u202C\u206D\u202E.\u00A6~\u0009Ê\u0007(\u202C\u206A\u202B\u206A\u200E\u202A\u206E\u206E\u206D\u206F\u200C\u202C\u202B\u206A\u200F\u202D\u200D\u202B\u202A\u202E\u200E\u206F\u202A\u200D\u206D\u202A\u200B\u206A\u202C\u200D\u202B\u206B\u200C\u206D\u206E\u200F\u206D\u202B\u200F\u206A\u202E.ü8ð)º(this._agent), this._offset);
			a = 0f;
			b = 0f;
			float num4 = 0f;
			\u206F\u202D\u206D\u200C\u200E\u206D\u206E\u200D\u202D\u206A\u206E\u200F\u202A\u200E\u200E\u206F\u202D\u206C\u206E\u202B\u206E\u202A\u200F\u206B\u206F\u202D\u206F\u206B\u206A\u202D\u206F\u200D\u202C\u206E\u206B\u200F\u200C\u202D\u206F\u202B\u202E.\u008B\u0099\u000EJU(camera, vec, ref a, ref b, ref num4);
			num2 = ((num4 > 0f) ? -1262278667 : 1493366974);
			goto IL_18;
		}

		// Token: 0x040006D7 RID: 1751
		private Vec2 _screenPosition;

		// Token: 0x040006D8 RID: 1752
		private string _text;

		// Token: 0x040006D9 RID: 1753
		private bool _isEnemy;

		// Token: 0x040006DA RID: 1754
		private int _distance;

		// Token: 0x040006DB RID: 1755
		private float _fontSize;

		// Token: 0x040006DC RID: 1756
		private readonly Agent _agent;

		// Token: 0x040006DD RID: 1757
		private readonly Vec3 _offset = new Vec3(0f, 0f, 0.6f, -1f);
	}
}
