using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AIInfluence.API;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence
{
	// Token: 0x02000061 RID: 97
	public class ModSettings : AttributeGlobalSettings<ModSettings>
	{
		// Token: 0x17000039 RID: 57
		// (get) Token: 0x06000249 RID: 585 RVA: 0x00051394 File Offset: 0x0004F594
		public override string Id
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(680526853);
			}
		}

		// Token: 0x1700003A RID: 58
		// (get) Token: 0x0600024A RID: 586 RVA: 0x000513B0 File Offset: 0x0004F5B0
		public override string DisplayName
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(\u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-214828113), null));
			}
		}

		// Token: 0x1700003B RID: 59
		// (get) Token: 0x0600024B RID: 587 RVA: 0x000513E0 File Offset: 0x0004F5E0
		public override string FolderName
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1143808045);
			}
		}

		// Token: 0x1700003C RID: 60
		// (get) Token: 0x0600024C RID: 588 RVA: 0x000513FC File Offset: 0x0004F5FC
		public override string FormatType
		{
			[MethodImpl(MethodImplOptions.NoInlining)]
			get
			{
				return <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(2117696962);
			}
		}

		// Token: 0x14000002 RID: 2
		// (add) Token: 0x0600024D RID: 589 RVA: 0x00051418 File Offset: 0x0004F618
		// (remove) Token: 0x0600024E RID: 590 RVA: 0x0005149C File Offset: 0x0004F69C
		public event Action<string, object> OnSettingChanged
		{
			[CompilerGenerated]
			add
			{
				Action<string, object> action = this.OnSettingChanged;
				for (;;)
				{
					IL_07:
					int num = 1329680334;
					for (;;)
					{
						int num2 = num;
						switch ((num2 + ~(-(--494023372)) ^ ~(--821042627)) * 595943105 % 3)
						{
						case 0:
							goto IL_07;
						case 1:
						{
							Action<string, object> action2 = action;
							Action<string, object> value2 = (Action<string, object>)\u206D\u202E\u206A\u202A\u200C\u202B\u200D\u206A\u202A\u202E\u202D\u202B\u206E\u202D\u206D\u206D\u206E\u200F\u202E\u200B\u200D\u202E\u206E\u202E\u206C\u206E\u206C\u202D\u200C\u200F\u200B\u202A\u200C\u206C\u202C\u200E\u206E\u206A\u206F\u206A\u202E.\u0018k]\u0095ü(action2, value);
							action = Interlocked.CompareExchange<Action<string, object>>(ref this.OnSettingChanged, value2, action2);
							num = ((action != action2) ? 1329680334 : 437820053);
							continue;
						}
						}
						return;
					}
				}
			}
			[CompilerGenerated]
			remove
			{
				Action<string, object> action = this.OnSettingChanged;
				for (;;)
				{
					IL_07:
					int num = 1560060613;
					for (;;)
					{
						int num2 = num;
						switch ((-239815170 - ~(~(1491357125 ^ (-1509556978 ^ 1489683860)) - num2 - 837811323 * (-1558290199 - -1558941626))) % 3)
						{
						case 1:
						{
							Action<string, object> action2 = action;
							Action<string, object> value2 = (Action<string, object>)\u206D\u202E\u206A\u202A\u200C\u202B\u200D\u206A\u202A\u202E\u202D\u202B\u206E\u202D\u206D\u206D\u206E\u200F\u202E\u200B\u200D\u202E\u206E\u202E\u206C\u206E\u206C\u202D\u200C\u200F\u200B\u202A\u200C\u206C\u202C\u200E\u206E\u206A\u206F\u206A\u202E.2\u00A4}\u0017ã(action2, value);
							action = Interlocked.CompareExchange<Action<string, object>>(ref this.OnSettingChanged, value2, action2);
							num = ((action != action2) ? 1560060613 : 2003448398);
							continue;
						}
						case 2:
							goto IL_07;
						}
						return;
					}
				}
			}
		}

		// Token: 0x1700003D RID: 61
		// (get) Token: 0x0600024F RID: 591 RVA: 0x00051534 File Offset: 0x0004F734
		// (set) Token: 0x06000250 RID: 592 RVA: 0x00051548 File Offset: 0x0004F748
		[SettingPropertyGroup("{=AIInfluence_Group_General}General Settings", GroupOrder = 0)]
		[SettingPropertyBool("{=AIInfluence_EnableModification}Enable Modification", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_EnableModification_Hint}Enables or disables the AIInfluence mod.")]
		public bool EnableModification
		{
			get
			{
				return this._enableModification;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableModification != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -2063311033;
						for (;;)
						{
							int num2 = num;
							switch (-((num2 ^ -1700667429 * (1689580038 + 2066500839 ^ ~-1328050696)) * 92148997 * 1007879609) % 4)
							{
							case 0:
								goto IL_8F;
							case 2:
								goto IL_14;
							case 3:
							{
								this._enableModification = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8F;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-2093519342), value);
								num = 2014784122;
								continue;
							}
							}
							goto Block_1;
							IL_8F:
							num = 798585821;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700003E RID: 62
		// (get) Token: 0x06000251 RID: 593 RVA: 0x000515EC File Offset: 0x0004F7EC
		// (set) Token: 0x06000252 RID: 594 RVA: 0x00051600 File Offset: 0x0004F800
		[SettingPropertyGroup("{=AIInfluence_Group_General}General Settings", GroupOrder = 0)]
		[SettingPropertyBool("{=AIInfluence_EnableNearbyNPCInitialization}Enable Nearby NPC Initialization", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_EnableNearbyNPCInitialization_Hint}Automatically initialize NPCs near the player every 2 hours.")]
		public bool EnableNearbyNPCInitialization
		{
			get
			{
				return this._enableNearbyNPCInitialization;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableNearbyNPCInitialization != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 477986637;
						for (;;)
						{
							int num2 = num;
							switch (-(num2 ^ -(-(-727097314 - -63415433))) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
							{
								this._enableNearbyNPCInitialization = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_75;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-676565000), value);
								num = 101953558;
								continue;
							}
							case 3:
								goto IL_75;
							}
							goto Block_1;
							IL_75:
							num = 1165999956;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700003F RID: 63
		// (get) Token: 0x06000253 RID: 595 RVA: 0x0005168C File Offset: 0x0004F88C
		// (set) Token: 0x06000254 RID: 596 RVA: 0x000516A0 File Offset: 0x0004F8A0
		[SettingPropertyGroup("{=AIInfluence_Group_General}General Settings", GroupOrder = 0)]
		[SettingPropertyBool("{=AIInfluence_EnableDebugLogging}Enable Debug Logging", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_EnableDebugLogging_Hint}Enables logging of debug messages to a file.")]
		public bool EnableDebugLogging
		{
			get
			{
				return this._enableDebugLogging;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableDebugLogging != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1268589675;
						for (;;)
						{
							int num2 = num;
							switch ((610063265 - ((num2 + -(-322608321)) * -319800611 - 687525196 * -892274377)) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._enableDebugLogging = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(744799769), value);
								num = -577069394;
								continue;
							}
							case 2:
								goto IL_86;
							}
							goto Block_1;
							IL_86:
							num = -1224971683;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000040 RID: 64
		// (get) Token: 0x06000255 RID: 597 RVA: 0x0005173C File Offset: 0x0004F93C
		// (set) Token: 0x06000256 RID: 598 RVA: 0x00051750 File Offset: 0x0004F950
		[SettingPropertyGroup("{=AIInfluence_Group_Community}Community & Support", GroupOrder = -2)]
		[SettingPropertyButton("{=AIInfluence_JoinDiscord}Join Discord - Support & Community", -1, true, "{=AIInfluence_JoinDiscord_Button}Open Discord", Content = "{=AIInfluence_JoinDiscord_Content}Join Discord Server", RequireRestart = false, HintText = "{=AIInfluence_JoinDiscord_Hint}Join our Discord server for support, updates, and community discussions")]
		public Action OpenDiscordServer { get; set; } = new Action(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u206E\u200C\u202B\u202E\u206B\u200C\u202B\u200F\u202C\u206C\u206C\u202C\u206B\u200B\u206B\u202B\u200F\u200B\u206B\u206A\u200F\u202B\u206B\u200F\u206F\u206D\u200B\u202A\u206D\u202B\u200D\u202A\u202B\u202A\u200C\u200E\u200E\u202B\u206C\u202E);

		// Token: 0x17000041 RID: 65
		// (get) Token: 0x06000257 RID: 599 RVA: 0x00051764 File Offset: 0x0004F964
		// (set) Token: 0x06000258 RID: 600 RVA: 0x00051778 File Offset: 0x0004F978
		[SettingPropertyGroup("{=AIInfluence_Group_Community}Community & Support", GroupOrder = -2)]
		[SettingPropertyButton("{=AIInfluence_OpenBoosty}Support on Boosty", -1, true, "{=AIInfluence_OpenBoosty_Button}Open Boosty", Content = "{=AIInfluence_OpenBoosty_Content}Open Boosty page", RequireRestart = false, HintText = "{=AIInfluence_OpenBoosty_Hint}Open the AI Influence Boosty support page in your browser")]
		public Action OpenBoostyPage { get; set; } = new Action(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u200B\u200B\u206D\u202D\u206C\u200C\u206E\u202A\u200C\u202E\u206F\u206A\u206C\u206C\u206C\u200C\u200D\u206A\u200D\u202E\u202B\u206D\u200B\u206B\u200F\u206A\u206B\u206C\u202A\u206C\u200B\u200E\u206C\u202A\u202D\u206F\u206A\u200B\u200B\u206B\u202E);

		// Token: 0x17000042 RID: 66
		// (get) Token: 0x06000259 RID: 601 RVA: 0x0005178C File Offset: 0x0004F98C
		// (set) Token: 0x0600025A RID: 602 RVA: 0x000517A0 File Offset: 0x0004F9A0
		[SettingPropertyGroup("{=AIInfluence_Group_Community}Community & Support", GroupOrder = -2)]
		[SettingPropertyButton("{=AIInfluence_OpenAfdian}Support on Afdian", -1, true, "{=AIInfluence_OpenAfdian_Button}Open Afdian", Content = "{=AIInfluence_OpenAfdian_Content}Open Afdian page", RequireRestart = false, HintText = "{=AIInfluence_OpenAfdian_Hint}Open the AI Influence Afdian support page in your browser")]
		public Action OpenAfdianPage { get; set; } = new Action(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u200D\u200C\u200B\u206C\u206F\u206F\u202D\u206B\u206A\u200B\u206A\u202D\u200B\u200D\u200D\u206A\u202E\u200E\u206B\u202C\u202E\u200D\u200E\u202E\u200D\u200D\u202E\u202A\u206A\u200C\u206D\u200C\u200D\u200E\u200F\u202D\u206D\u202E\u202D\u200F\u202E);

		// Token: 0x17000043 RID: 67
		// (get) Token: 0x0600025B RID: 603 RVA: 0x000517B4 File Offset: 0x0004F9B4
		// (set) Token: 0x0600025C RID: 604 RVA: 0x000517C8 File Offset: 0x0004F9C8
		[SettingPropertyDropdown("AI Backend", RequireRestart = false, HintText = "Select the backend to use for AI responses.")]
		[SettingPropertyGroup("API Settings/Main Settings", GroupOrder = 1)]
		public Dropdown<string> AIBackend
		{
			get
			{
				return this._aiBackend;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._aiBackend == null;
				if (flag)
				{
					goto IL_0E;
				}
				goto IL_67;
				int num2;
				for (;;)
				{
					IL_13:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)((num ^ -(~183562619) + (869818609 - -641265096 + -69583132)) * 2028718105)) % 4U)
					{
					case 0U:
						goto IL_0E;
					case 1U:
						goto IL_67;
					case 3U:
						this._aiBackend = value;
						num2 = (int)(num3 * 338359898U ^ 3278394709U);
						continue;
					}
					break;
				}
				return;
				IL_0E:
				num2 = 1361675098;
				goto IL_13;
				IL_67:
				this._aiBackend = value;
				Action<string, object> onSettingChanged = this.OnSettingChanged;
				if (onSettingChanged != null)
				{
					onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-695868282), value);
					num2 = 1399059339;
					goto IL_13;
				}
			}
		}

		// Token: 0x17000044 RID: 68
		// (get) Token: 0x0600025D RID: 605 RVA: 0x0005186C File Offset: 0x0004FA6C
		// (set) Token: 0x0600025E RID: 606 RVA: 0x00051880 File Offset: 0x0004FA80
		[SettingPropertyText("OpenRouter AI Model", -1, true, "", RequireRestart = false, HintText = "Enter the AI model to use for conversations (e.g., gpt-3.5-turbo, gpt-4).")]
		[SettingPropertyGroup("API Settings/OpenRouter Settings", GroupOrder = 1)]
		public string AIModel
		{
			get
			{
				return this._aiModel;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.I\u001D\u00BF\u000B\u0085(this._aiModel, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = -1583293399;
						for (;;)
						{
							int num2 = num;
							switch ((num2 * -582231923 - (1629813593 + 58550342 + -1312375459 * -1428579815)) % 4)
							{
							case 1:
							{
								this._aiModel = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_84;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(753620623), value);
								num = -2133592317;
								continue;
							}
							case 2:
								goto IL_16;
							case 3:
								goto IL_84;
							}
							goto Block_1;
							IL_84:
							num = -1756921064;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000045 RID: 69
		// (get) Token: 0x0600025F RID: 607 RVA: 0x0005191C File Offset: 0x0004FB1C
		// (set) Token: 0x06000260 RID: 608 RVA: 0x00051930 File Offset: 0x0004FB30
		[SettingPropertyText("OpenRouter API Key", -1, true, "", RequireRestart = false, HintText = "Enter your OpenRouter API key (only needed for OpenRouter provider).")]
		[SettingPropertyGroup("API Settings/OpenRouter Settings", GroupOrder = 1)]
		public string ApiKey
		{
			get
			{
				return this._apiKey;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.I\u001D\u00BF\u000B\u0085(this._apiKey, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = -1089489828;
						for (;;)
						{
							int num2 = num;
							switch ((-(~(num2 ^ ~(1580433376 - 530813216 + (1041126765 - 374799605)))) ^ 1032138809) % 4)
							{
							case 1:
							{
								this._apiKey = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_87;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1480751657), value);
								num = -1735173086;
								continue;
							}
							case 2:
								goto IL_16;
							case 3:
								goto IL_87;
							}
							goto Block_1;
							IL_87:
							num = -465612773;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000046 RID: 70
		// (get) Token: 0x06000261 RID: 609 RVA: 0x000519CC File Offset: 0x0004FBCC
		// (set) Token: 0x06000262 RID: 610 RVA: 0x000519E0 File Offset: 0x0004FBE0
		[SettingPropertyText("DeepSeek Model", -1, true, "", RequireRestart = false, HintText = "Enter the DeepSeek model to use (e.g., deepseek-chat).")]
		[SettingPropertyGroup("API Settings/DeepSeek Settings", GroupOrder = 5)]
		public string DeepSeekModel
		{
			get
			{
				return this._deepSeekModel;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.I\u001D\u00BF\u000B\u0085(this._deepSeekModel, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = 616999720;
						for (;;)
						{
							int num2 = num;
							switch (-(-(num2 - (~2134141707 - 1262977762 - ((1573079848 ^ 616736274) + -1372161259)))) % 4)
							{
							case 1:
							{
								this._deepSeekModel = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_87;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1065892491), value);
								num = 1216727026;
								continue;
							}
							case 2:
								goto IL_16;
							case 3:
								goto IL_87;
							}
							goto Block_1;
							IL_87:
							num = 365163471;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000047 RID: 71
		// (get) Token: 0x06000263 RID: 611 RVA: 0x00051A7C File Offset: 0x0004FC7C
		// (set) Token: 0x06000264 RID: 612 RVA: 0x00051A90 File Offset: 0x0004FC90
		[SettingPropertyText("DeepSeek API Key", -1, true, "", RequireRestart = false, HintText = "Enter your DeepSeek API key (only needed for DeepSeek provider).")]
		[SettingPropertyGroup("API Settings/DeepSeek Settings", GroupOrder = 5)]
		public string DeepSeekApiKey
		{
			get
			{
				return this._deepSeekApiKey;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.I\u001D\u00BF\u000B\u0085(this._deepSeekApiKey, value);
				if (flag)
				{
					for (;;)
					{
						IL_16:
						int num = -1582174826;
						for (;;)
						{
							int num2 = num;
							switch ((~(~(-(~-136946086)) - num2 ^ 1920599069) ^ -87146010) % 4)
							{
							case 0:
								goto IL_16;
							case 2:
							{
								this._deepSeekApiKey = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7C;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(722747634), value);
								num = -1160958055;
								continue;
							}
							case 3:
								goto IL_7C;
							}
							goto Block_1;
							IL_7C:
							num = -776591617;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000048 RID: 72
		// (get) Token: 0x06000265 RID: 613 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000266 RID: 614 RVA: 0x00051B24 File Offset: 0x0004FD24
		[SettingPropertyBool("Test DeepSeek Connection", Order = 0, RequireRestart = false, HintText = "Test connection to DeepSeek backend. Results will be displayed in game messages.")]
		[SettingPropertyGroup("API Settings/DeepSeek Settings", GroupOrder = 5)]
		public bool TestDeepSeekConnection
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					try
					{
						\u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E.\u202B\u200B\u202E\u202C\u206E\u200E\u206F\u200D\u202D\u202E\u202C\u206E\u206B\u200B\u202C\u202B\u202C\u206E\u206B\u200C\u200B\u200F\u202D\u200D\u200E\u200B\u202E\u200E\u200B\u202A\u206D\u202D\u202C\u206B\u206B\u202D\u206B\u202C\u206F\u202B\u202E(new Func<Task>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u206E\u202A\u202D\u206A\u206D\u200D\u206F\u200C\u206C\u200E\u206B\u206A\u206B\u206A\u206C\u200E\u200E\u200B\u206E\u206E\u202C\u200E\u206A\u206D\u200E\u202C\u200D\u200B\u206D\u200C\u206E\u200C\u200E\u200C\u202C\u200E\u202B\u206C\u206E\u200F\u202E));
					}
					catch (Exception ex)
					{
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-405694881), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
					}
				}
			}
		}

		// Token: 0x17000049 RID: 73
		// (get) Token: 0x06000267 RID: 615 RVA: 0x00051BB4 File Offset: 0x0004FDB4
		// (set) Token: 0x06000268 RID: 616 RVA: 0x00051BC8 File Offset: 0x0004FDC8
		[SettingPropertyText("Player2 API URL", -1, true, "", RequireRestart = false, HintText = "The URL for the Player2 API. Default is http://localhost:4315")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public string Player2ApiUrl { get; set; } = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1803638435);

		// Token: 0x1700004A RID: 74
		// (get) Token: 0x06000269 RID: 617 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600026A RID: 618 RVA: 0x00051BDC File Offset: 0x0004FDDC
		[SettingPropertyBool("Test OpenRouter Connection", Order = 0, RequireRestart = false, HintText = "Test connection to OpenRouter backend. Results will be displayed in game messages.")]
		[SettingPropertyGroup("API Settings/OpenRouter Settings", GroupOrder = 1)]
		public bool TestOpenRouterConnection
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					try
					{
						\u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E.\u202B\u200B\u202E\u202C\u206E\u200E\u206F\u200D\u202D\u202E\u202C\u206E\u206B\u200B\u202C\u202B\u202C\u206E\u206B\u200C\u200B\u200F\u202D\u200D\u200E\u200B\u202E\u200E\u200B\u202A\u206D\u202D\u202C\u206B\u206B\u202D\u206B\u202C\u206F\u202B\u202E(new Func<Task>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u202B\u202B\u206C\u206D\u206B\u202D\u200F\u206E\u200C\u202A\u202C\u206F\u202C\u200D\u200D\u200C\u202E\u202E\u206A\u206B\u206E\u200C\u206E\u200B\u200B\u202D\u202A\u200D\u202D\u202E\u200D\u206B\u200C\u206E\u202A\u200B\u200C\u206A\u200D\u202E\u202E));
					}
					catch (Exception ex)
					{
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-733900551), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
					}
				}
			}
		}

		// Token: 0x1700004B RID: 75
		// (get) Token: 0x0600026B RID: 619 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600026C RID: 620 RVA: 0x00051C6C File Offset: 0x0004FE6C
		[SettingPropertyBool("Test Player2 Connection", Order = 0, RequireRestart = false, HintText = "Test connection to Player2 backend. Results will be displayed in game messages.")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public bool TestPlayer2Connection
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					try
					{
						\u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E.\u202B\u200B\u202E\u202C\u206E\u200E\u206F\u200D\u202D\u202E\u202C\u206E\u206B\u200B\u202C\u202B\u202C\u206E\u206B\u200C\u200B\u200F\u202D\u200D\u200E\u200B\u202E\u200E\u200B\u202A\u206D\u202D\u202C\u206B\u206B\u202D\u206B\u202C\u206F\u202B\u202E(new Func<Task>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u202A\u202D\u200B\u206E\u202C\u202C\u202A\u202A\u206E\u202B\u200E\u200B\u206A\u200C\u206F\u200D\u200F\u200C\u206E\u202C\u200D\u206A\u200B\u206A\u202A\u200F\u202B\u202A\u206B\u206E\u200B\u200C\u202C\u200D\u200F\u200F\u206C\u202B\u202E\u206E\u202E));
					}
					catch (Exception ex)
					{
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-868386207), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
					}
				}
			}
		}

		// Token: 0x1700004C RID: 76
		// (get) Token: 0x0600026D RID: 621 RVA: 0x00051CFC File Offset: 0x0004FEFC
		// (set) Token: 0x0600026E RID: 622 RVA: 0x00051D10 File Offset: 0x0004FF10
		[SettingPropertyButton("{=DownloadPlayer2}API Settings/Player2 Settings", 0, true, "{=DownloadPlayer2_Button}Open Player2 Web", Content = "{=DownloadPlayer2_Button_2}Open Player2 Web", RequireRestart = false, HintText = "{=DynamicClanSettings_CheckAIInfluence_Hint}Download Player2 (free AI API)")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public Action OpenAIInfluenceWebsite { get; set; } = new Action(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u202C\u200F\u206E\u206C\u202B\u200F\u200D\u206A\u202C\u206E\u206C\u202C\u200C\u202E\u202B\u200D\u202E\u200F\u206F\u200F\u200E\u206F\u200D\u206B\u200D\u202D\u200C\u206E\u200E\u200F\u202B\u202B\u200D\u206C\u206D\u206A\u202E\u206B\u200B\u206E\u202E);

		// Token: 0x1700004D RID: 77
		// (get) Token: 0x0600026F RID: 623 RVA: 0x00051D24 File Offset: 0x0004FF24
		// (set) Token: 0x06000270 RID: 624 RVA: 0x00051D38 File Offset: 0x0004FF38
		[SettingPropertyText("Ollama Model Name", -1, true, "", RequireRestart = false, HintText = "Enter the Ollama model name (e.g., llama2, mistral, codellama).")]
		[SettingPropertyGroup("API Settings/Ollama Settings", GroupOrder = 3)]
		public string OllamaModel { get; set; } = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1293776018);

		// Token: 0x1700004E RID: 78
		// (get) Token: 0x06000271 RID: 625 RVA: 0x00051D4C File Offset: 0x0004FF4C
		// (set) Token: 0x06000272 RID: 626 RVA: 0x00051D60 File Offset: 0x0004FF60
		[SettingPropertyText("Ollama API URL", -1, true, "", RequireRestart = false, HintText = "Enter the Ollama API URL (default: http://localhost:11434).")]
		[SettingPropertyGroup("API Settings/Ollama Settings", GroupOrder = 3)]
		public string OllamaApiUrl { get; set; } = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1812459289);

		// Token: 0x1700004F RID: 79
		// (get) Token: 0x06000273 RID: 627 RVA: 0x00051D74 File Offset: 0x0004FF74
		// (set) Token: 0x06000274 RID: 628 RVA: 0x00051D88 File Offset: 0x0004FF88
		[SettingPropertyText("KoboldCpp API URL", -1, true, "", RequireRestart = false, HintText = "Enter the KoboldCpp API URL (default: http://localhost:5001).")]
		[SettingPropertyGroup("API Settings/KoboldCpp Settings", GroupOrder = 4)]
		public string KoboldCppApiUrl { get; set; } = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1133228963);

		// Token: 0x17000050 RID: 80
		// (get) Token: 0x06000275 RID: 629 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000276 RID: 630 RVA: 0x00051D9C File Offset: 0x0004FF9C
		[SettingPropertyBool("Test Ollama Connection", Order = 0, RequireRestart = false, HintText = "Test connection to Ollama backend. Results will be displayed in game messages.")]
		[SettingPropertyGroup("API Settings/Ollama Settings", GroupOrder = 3)]
		public bool TestOllamaConnection
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					try
					{
						\u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E.\u202B\u200B\u202E\u202C\u206E\u200E\u206F\u200D\u202D\u202E\u202C\u206E\u206B\u200B\u202C\u202B\u202C\u206E\u206B\u200C\u200B\u200F\u202D\u200D\u200E\u200B\u202E\u200E\u200B\u202A\u206D\u202D\u202C\u206B\u206B\u202D\u206B\u202C\u206F\u202B\u202E(new Func<Task>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u200B\u202B\u206F\u206D\u206A\u202B\u206E\u202D\u202E\u202C\u200B\u206E\u206F\u202B\u202A\u206D\u200B\u200C\u200E\u202D\u206C\u202D\u206C\u206D\u202E\u202C\u202D\u200D\u206D\u206A\u200E\u202B\u202E\u206F\u200B\u200F\u202D\u200E\u200F\u202C\u202E));
					}
					catch (Exception ex)
					{
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1289641192), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
					}
				}
			}
		}

		// Token: 0x17000051 RID: 81
		// (get) Token: 0x06000277 RID: 631 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000278 RID: 632 RVA: 0x00051E2C File Offset: 0x0005002C
		[SettingPropertyBool("Test KoboldCpp Connection", Order = 0, RequireRestart = false, HintText = "Test connection to KoboldCpp backend. Results will be displayed in game messages.")]
		[SettingPropertyGroup("API Settings/KoboldCpp Settings", GroupOrder = 4)]
		public bool TestKoboldCppConnection
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					try
					{
						\u200E\u202A\u200D\u202B\u206E\u200E\u200C\u200D\u200E\u200F\u206B\u206B\u200C\u200D\u206D\u200C\u202A\u202A\u202A\u206E\u202C\u200C\u206C\u200E\u200B\u206A\u202E\u202D\u200E\u200E\u206B\u206B\u200F\u206B\u206E\u200F\u202E\u206B\u206C\u200C\u202E.\u202B\u200B\u202E\u202C\u206E\u200E\u206F\u200D\u202D\u202E\u202C\u206E\u206B\u200B\u202C\u202B\u202C\u206E\u206B\u200C\u200B\u200F\u202D\u200D\u200E\u200B\u202E\u200E\u200B\u202A\u206D\u202D\u202C\u206B\u206B\u202D\u206B\u202C\u206F\u202B\u202E(new Func<Task>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u202E\u200D\u206D\u202E\u200F\u202C\u206E\u200F\u206E\u206D\u202E\u202C\u200B\u202E\u202D\u200C\u202B\u200F\u206E\u206E\u206C\u206C\u206B\u202B\u206E\u206B\u206E\u206B\u200C\u202E\u202C\u206D\u202A\u202A\u206F\u202A\u206F\u200E\u202B\u202D\u202E));
					}
					catch (Exception ex)
					{
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-782415248), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
					}
				}
			}
		}

		// Token: 0x17000052 RID: 82
		// (get) Token: 0x06000279 RID: 633 RVA: 0x00051EBC File Offset: 0x000500BC
		// (set) Token: 0x0600027A RID: 634 RVA: 0x00051ED0 File Offset: 0x000500D0
		[SettingPropertyBool("Enable TTS (Text-to-Speech)", Order = 1, RequireRestart = false, HintText = "Enable text-to-speech for NPC dialogue responses. Voices are assigned automatically based on gender.")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public bool EnableTTS
		{
			get
			{
				return this._enableTTS;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableTTS != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -703236541;
						for (;;)
						{
							int num2 = num;
							switch ((20017509 - -146962194 - (num2 ^ -(-651035086 ^ -2128866959) - (936908920 * -1887395903 - 1159768431 * 1088709371) ^ -(-202365541 ^ 1861067382))) % 4)
							{
							case 1:
							{
								this._enableTTS = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_AA;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(591967196), value);
								num = -1920483768;
								continue;
							}
							case 2:
								goto IL_AA;
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_AA:
							num = -2050837278;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000053 RID: 83
		// (get) Token: 0x0600027B RID: 635 RVA: 0x00051F94 File Offset: 0x00050194
		// (set) Token: 0x0600027C RID: 636 RVA: 0x00051FA8 File Offset: 0x000501A8
		[SettingPropertyFloatingInteger("TTS Speed", 0.25f, 4f, "0.00", Order = 2, RequireRestart = false, HintText = "Speed of text-to-speech (0.25 to 4.0)")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public float TTSSpeed
		{
			get
			{
				return this._ttsSpeed;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._ttsSpeed - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = 125767799;
						for (;;)
						{
							int num2 = num;
							switch (((num2 - (--1123605735 ^ -1582708458 - -10203637 ^ (-1848852804 ^ 1092369159 + 975881405))) * 1533295347 - (1986848408 - 355252736)) % 4)
							{
							case 0:
								goto IL_B1;
							case 1:
							{
								this._ttsSpeed = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_B1;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(151835456), value);
								num = -2006559144;
								continue;
							}
							case 3:
								goto IL_21;
							}
							goto Block_1;
							IL_B1:
							num = 375251282;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000054 RID: 84
		// (get) Token: 0x0600027D RID: 637 RVA: 0x00052074 File Offset: 0x00050274
		// (set) Token: 0x0600027E RID: 638 RVA: 0x00052088 File Offset: 0x00050288
		[SettingPropertyButton("Export Available Voices", 0, true, "Export Voices", Content = "Export Available Voices", RequireRestart = false, HintText = "Export available TTS voices to a text file in the mod folder. You can use this list to manually edit NPC .json files and assign custom voices.")]
		[SettingPropertyGroup("API Settings/Player2 Settings", GroupOrder = 2)]
		public Action ExportTTSVoices { get; set; } = new Action(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u200F\u200F\u202E\u200D\u202E\u206B\u200F\u200E\u206F\u200D\u202D\u200D\u202A\u206C\u200D\u200D\u200C\u202B\u200F\u200F\u202A\u206C\u206B\u200E\u206F\u202A\u202E\u202A\u206C\u202E\u200F\u206D\u200F\u206D\u202B\u200F\u206D\u200D\u206E\u200E\u202E);

		// Token: 0x17000055 RID: 85
		// (get) Token: 0x0600027F RID: 639 RVA: 0x0005209C File Offset: 0x0005029C
		// (set) Token: 0x06000280 RID: 640 RVA: 0x000520B0 File Offset: 0x000502B0
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_PromptMaxHistory}Maximum Conversation History Length", 1, 200, "0 Messages", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_PromptMaxHistory_Hint}Sets how many recent conversation messages to include in the AI prompt (1–10). Lower is faster, higher is more contextual.")]
		public int PromptMaxHistory
		{
			get
			{
				return this._promptMaxHistory;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptMaxHistory != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -1157319449;
						for (;;)
						{
							int num2 = num;
							switch (-(~(-(num2 ^ ~(~-2103270752) - (1447699807 * 935101039 ^ -259570311 * -1033492800)))) % 4)
							{
							case 1:
							{
								this._promptMaxHistory = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8C;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1802329481), value);
								num = -1963627972;
								continue;
							}
							case 2:
								goto IL_8C;
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_8C:
							num = -576051726;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000056 RID: 86
		// (get) Token: 0x06000281 RID: 641 RVA: 0x00052154 File Offset: 0x00050354
		// (set) Token: 0x06000282 RID: 642 RVA: 0x00052168 File Offset: 0x00050368
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyBool("{=AIInfluence_PromptIncludeEvents}Include Recent Events", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_PromptIncludeEvents_Hint}Includes recent events (wars, tournaments) from the last 7 days in the prompt. Disabling reduces prompt size.")]
		public bool PromptIncludeEvents
		{
			get
			{
				return this._promptIncludeEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptIncludeEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 545925678;
						for (;;)
						{
							int num2 = num;
							switch (-(~(num2 + ~(-(-153339997 + -1772000119)) - -908279751 * (1719557847 ^ -215134496))) % 4)
							{
							case 0:
								goto IL_88;
							case 2:
								goto IL_11;
							case 3:
							{
								this._promptIncludeEvents = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_88;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(634539094), value);
								num = 1423644199;
								continue;
							}
							}
							goto Block_1;
							IL_88:
							num = 1917401180;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000057 RID: 87
		// (get) Token: 0x06000283 RID: 643 RVA: 0x00052208 File Offset: 0x00050408
		// (set) Token: 0x06000284 RID: 644 RVA: 0x0005221C File Offset: 0x0005041C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_PromptSensitiveTrust}Sensitive Information Trust Threshold", 0f, 1f, "#0.0", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_PromptSensitiveTrust_Hint}Minimum trust level (0–1) to discuss sensitive topics (troops, plans). Higher makes NPCs more cautious.")]
		public float PromptSensitiveTrust
		{
			get
			{
				return this._promptSensitiveTrust;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptSensitiveTrust != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 365685324;
						for (;;)
						{
							int num2 = num;
							switch (-(~num2) % 4)
							{
							case 0:
								goto IL_68;
							case 1:
							{
								this._promptSensitiveTrust = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_68;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1358514910), value);
								num = 1671800719;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_68:
							num = 866899577;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000058 RID: 88
		// (get) Token: 0x06000285 RID: 645 RVA: 0x0005229C File Offset: 0x0005049C
		// (set) Token: 0x06000286 RID: 646 RVA: 0x000522B0 File Offset: 0x000504B0
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_PromptSecretTrust}Secret Information Trust Threshold", 0f, 1f, "#0.0", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_PromptSecretTrust_Hint}Minimum trust level (0–1) to reveal secrets (weaknesses, alliances). Higher makes NPCs more secretive.")]
		public float PromptSecretTrust
		{
			get
			{
				return this._promptSecretTrust;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptSecretTrust != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1980326264;
						for (;;)
						{
							int num2 = num;
							switch ((-(num2 - (~(1990693170 * 1093601597) - -994733093 * (1211400194 - -510293521))) - (-372810239 + 735647748) ^ 1085827678) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
							{
								this._promptSecretTrust = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_9E;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1393496833), value);
								num = 2122438626;
								continue;
							}
							case 3:
								goto IL_9E;
							}
							goto Block_1;
							IL_9E:
							num = 2022835931;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000059 RID: 89
		// (get) Token: 0x06000287 RID: 647 RVA: 0x00052368 File Offset: 0x00050568
		// (set) Token: 0x06000288 RID: 648 RVA: 0x0005237C File Offset: 0x0005057C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyBool("{=AIInfluence_PromptIncludeQuirks}Include Speech Quirks", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_PromptIncludeQuirks_Hint}Includes NPC speech quirks (e.g., *sighs*) in the prompt. Disabling makes responses more neutral.")]
		public bool PromptIncludeQuirks
		{
			get
			{
				return this._promptIncludeQuirks;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptIncludeQuirks != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 66862440;
						for (;;)
						{
							int num2 = num;
							switch (-((num2 ^ -(~-2029898474 + 1799467099 * -782295657)) - 1289956577 * -323942769 * 757465977 - (326799068 ^ 939271873)) % 4)
							{
							case 1:
								goto IL_9F;
							case 2:
							{
								this._promptIncludeQuirks = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_9F;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1731496048), value);
								num = 280834693;
								continue;
							}
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_9F:
							num = 480311878;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700005A RID: 90
		// (get) Token: 0x06000289 RID: 649 RVA: 0x00052434 File Offset: 0x00050634
		// (set) Token: 0x0600028A RID: 650 RVA: 0x00052448 File Offset: 0x00050648
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_PromptQuirksFrequency}Speech Quirks Frequency", 0f, 1f, "0.0", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_PromptQuirksFrequency_Hint}How often NPCs use their speech quirks (0–1). 0 = never, 1 = always, 0.5 = half the time.")]
		public float PromptQuirksFrequency
		{
			get
			{
				return this._promptQuirksFrequency;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				for (;;)
				{
					IL_01:
					int num = 113842863;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(-num2 - (-2114009432 * -612502773 + -1529418918)) - -874433276)) % 6U)
						{
						case 1U:
						{
							bool flag = this._promptQuirksFrequency != value;
							num = (int)(num3 * 3437517498U ^ 125112269U);
							continue;
						}
						case 2U:
						{
							this._promptQuirksFrequency = value;
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_45;
							}
							onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-622077920), value);
							num = 447350736;
							continue;
						}
						case 3U:
						{
							bool flag;
							num = (int)((flag ? 3763037111U : 2251062359U) ^ num3 * 1436804443U);
							continue;
						}
						case 4U:
							goto IL_45;
						case 5U:
							goto IL_01;
						}
						return;
						IL_45:
						num = 590551976;
					}
				}
			}
		}

		// Token: 0x1700005B RID: 91
		// (get) Token: 0x0600028B RID: 651 RVA: 0x00052514 File Offset: 0x00050714
		// (set) Token: 0x0600028C RID: 652 RVA: 0x00052528 File Offset: 0x00050728
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyBool("{=AIInfluence_PromptUseAsterisks}Use Asterisks for Actions", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_PromptUseAsterisks_Hint}Enables NPCs to use **asterisks** for describing actions and emotions in their responses. Disabling makes responses more text-only.")]
		public bool PromptUseAsterisks
		{
			get
			{
				return this._promptUseAsterisks;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptUseAsterisks != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -7813256;
						for (;;)
						{
							int num2 = num;
							switch (((num2 ^ ~(--534888496)) * -574063829 - (-105964583 ^ 2018564898) - -1978312315) % 4)
							{
							case 1:
							{
								this._promptUseAsterisks = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(586024397), value);
								num = 2065866126;
								continue;
							}
							case 2:
								goto IL_11;
							case 3:
								goto IL_86;
							}
							goto Block_1;
							IL_86:
							num = -1446407997;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700005C RID: 92
		// (get) Token: 0x0600028D RID: 653 RVA: 0x000525C4 File Offset: 0x000507C4
		// (set) Token: 0x0600028E RID: 654 RVA: 0x000525D8 File Offset: 0x000507D8
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_PromptLieStrictness}Lie Detection Strictness", 0f, 1f, "#0.0", Order = 7, RequireRestart = false, HintText = "{=AIInfluence_PromptLieStrictness_Hint}Sets how strictly NPCs detect lies about the player's identity (0–1). Higher makes them more suspicious.")]
		public float PromptLieStrictness
		{
			get
			{
				return this._promptLieStrictness;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptLieStrictness != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -223840638;
						for (;;)
						{
							int num2 = num;
							switch ((-(num2 * 255529537 - -1018282481 * (1607607982 ^ -1303962159)) ^ -830280192) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_85;
							case 3:
							{
								this._promptLieStrictness = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_85;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-284613453), value);
								num = -1027870804;
								continue;
							}
							}
							goto Block_1;
							IL_85:
							num = -949836397;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700005D RID: 93
		// (get) Token: 0x0600028F RID: 655 RVA: 0x00052674 File Offset: 0x00050874
		// (set) Token: 0x06000290 RID: 656 RVA: 0x00052688 File Offset: 0x00050888
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_PromptMinResponseLength}Minimum AI Response Length", 10, 300, "0 Characters", Order = 8, RequireRestart = false, HintText = "{=AIInfluence_PromptMinResponseLength_Hint}Minimum number of characters in AI responses (10–100). Shorter responses will be expanded.")]
		public int PromptMinResponseLength
		{
			get
			{
				return this._promptMinResponseLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptMinResponseLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 387904118;
						for (;;)
						{
							int num2 = num;
							switch ((-884518625 - ((num2 ^ (-(-2017306374 - 1471528113) ^ (1203287724 ^ ~-1505780537))) + ~(1842717008 ^ 1827817104))) % 4)
							{
							case 0:
								goto IL_97;
							case 2:
								goto IL_14;
							case 3:
							{
								this._promptMinResponseLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_97;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(815715897), value);
								num = 1682934207;
								continue;
							}
							}
							goto Block_1;
							IL_97:
							num = 1719317888;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700005E RID: 94
		// (get) Token: 0x06000291 RID: 657 RVA: 0x00052738 File Offset: 0x00050938
		// (set) Token: 0x06000292 RID: 658 RVA: 0x0005274C File Offset: 0x0005094C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_PromptMaxResponseLength}Maximum AI Response Length", 100, 1000, "0 Characters", Order = 9, RequireRestart = false, HintText = "{=AIInfluence_PromptMaxResponseLength_Hint}Maximum number of characters in AI responses (100–1000). Longer responses will be truncated.")]
		public int PromptMaxResponseLength
		{
			get
			{
				return this._promptMaxResponseLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptMaxResponseLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1523405211;
						for (;;)
						{
							int num2 = num;
							switch (~(~((num2 ^ ~-1674644468 * -713162087 - 146276344) - -1462033632)) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
							{
								this._promptMaxResponseLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_81;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(242280749), value);
								num = -527765712;
								continue;
							}
							case 3:
								goto IL_81;
							}
							goto Block_1;
							IL_81:
							num = -535607106;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700005F RID: 95
		// (get) Token: 0x06000293 RID: 659 RVA: 0x000527E4 File Offset: 0x000509E4
		// (set) Token: 0x06000294 RID: 660 RVA: 0x000527F8 File Offset: 0x000509F8
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyBool("{=AIInfluence_PromptIncludeFriends}Include NPC Friends", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_PromptIncludeFriends_Hint}Includes the list of NPC friends in the prompt. Disabling reduces prompt size.")]
		public bool PromptIncludeFriends
		{
			get
			{
				return this._promptIncludeFriends;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptIncludeFriends != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -67056823;
						for (;;)
						{
							int num2 = num;
							switch (((-153255259 * (-1713886016 * 1759788839 - (985565022 + -2059259200)) - num2 ^ -264085434) - 879294256 - -2035072964) % 4)
							{
							case 0:
								goto IL_99;
							case 2:
								goto IL_14;
							case 3:
							{
								this._promptIncludeFriends = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_99;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-575608042), value);
								num = 162399744;
								continue;
							}
							}
							goto Block_1;
							IL_99:
							num = -543175841;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000060 RID: 96
		// (get) Token: 0x06000295 RID: 661 RVA: 0x000528AC File Offset: 0x00050AAC
		// (set) Token: 0x06000296 RID: 662 RVA: 0x000528C0 File Offset: 0x00050AC0
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_PromptMaxFriends}Maximum Number of Friends", 1, 50, "0 Friends", Order = 11, RequireRestart = false, HintText = "{=AIInfluence_PromptMaxFriends_Hint}Maximum number of NPC friends included in the prompt (1–20). Limits the size of the friend list.")]
		public int PromptMaxFriends
		{
			get
			{
				return this._promptMaxFriends;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptMaxFriends != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1870544546;
						for (;;)
						{
							int num2 = num;
							switch (-(-(num2 * 1924169309) ^ 1870980015 + -1484066527) % 4)
							{
							case 1:
								goto IL_7A;
							case 2:
							{
								this._promptMaxFriends = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7A;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1384942803), value);
								num = 802406493;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_7A:
							num = 1325261400;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000061 RID: 97
		// (get) Token: 0x06000297 RID: 663 RVA: 0x00052950 File Offset: 0x00050B50
		// (set) Token: 0x06000298 RID: 664 RVA: 0x00052964 File Offset: 0x00050B64
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_PromptMaxEnemies}Maximum Number of Enemies", 1, 50, "0 Enemies", Order = 12, RequireRestart = false, HintText = "{=AIInfluence_PromptMaxEnemies_Hint}Maximum number of NPC enemies included in the prompt (1–50). Limits the size of the enemy list.")]
		public int PromptMaxEnemies
		{
			get
			{
				return this._promptMaxEnemies;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._promptMaxEnemies != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1068182265;
						for (;;)
						{
							int num2 = num;
							switch (-(num2 * -856615063 ^ -1664561872) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_73;
							case 3:
							{
								this._promptMaxEnemies = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_73;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1954464042), value);
								num = 1708724330;
								continue;
							}
							}
							goto Block_1;
							IL_73:
							num = 798417627;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000062 RID: 98
		// (get) Token: 0x06000299 RID: 665 RVA: 0x000529EC File Offset: 0x00050BEC
		// (set) Token: 0x0600029A RID: 666 RVA: 0x00052A00 File Offset: 0x00050C00
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyBool("{=AIInfluence_UseAdvancedTrust}Use Advanced Trust Mode", Order = 13, RequireRestart = false, HintText = "{=AIInfluence_UseAdvancedTrust_Hint}Enables advanced trust calculation (based on relations, interactions, familiarity, and lies). If disabled, trust equals relations.")]
		public bool UseAdvancedTrust
		{
			get
			{
				return this._useAdvancedTrust;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._useAdvancedTrust != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -701932733;
						for (;;)
						{
							int num2 = num;
							switch (-(~(num2 ^ -591833849 + (-724034445 ^ -692549301 ^ -439889900))) % 4)
							{
							case 1:
							{
								this._useAdvancedTrust = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_80;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1053744037), value);
								num = -1160530043;
								continue;
							}
							case 2:
								goto IL_11;
							case 3:
								goto IL_80;
							}
							goto Block_1;
							IL_80:
							num = -73006996;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000063 RID: 99
		// (get) Token: 0x0600029B RID: 667 RVA: 0x00052A98 File Offset: 0x00050C98
		// (set) Token: 0x0600029C RID: 668 RVA: 0x00052AAC File Offset: 0x00050CAC
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_InteractionTrustBonus}Interaction Trust Bonus", 0.01f, 0.03f, "#0.00", Order = 14, RequireRestart = false, HintText = "{=AIInfluence_InteractionTrustBonus_Hint}Trust gained per interaction with an NPC in advanced trust mode (0.01–0.03).")]
		public float InteractionTrustBonus
		{
			get
			{
				return this._interactionTrustBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._interactionTrustBonus != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 810414158;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-(num2 - (-227952544 ^ ~(-1234408737 * -536033097))) + (328385689 ^ 1709131655) - 713888574)) % 5U)
							{
							case 0U:
								num = (int)(num3 * 3061868013U ^ 2969260220U);
								continue;
							case 2U:
								goto IL_14;
							case 3U:
								goto IL_92;
							case 4U:
							{
								this._interactionTrustBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_92;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1688517092), value);
								num = 1777887422;
								continue;
							}
							}
							goto Block_1;
							IL_92:
							num = 392454376;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000064 RID: 100
		// (get) Token: 0x0600029D RID: 669 RVA: 0x00052B6C File Offset: 0x00050D6C
		// (set) Token: 0x0600029E RID: 670 RVA: 0x00052B80 File Offset: 0x00050D80
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FamiliarityTrustBonus}Familiarity Trust Bonus", 0f, 0.2f, "#0.0", Order = 15, RequireRestart = false, HintText = "{=AIInfluence_FamiliarityTrustBonus_Hint}Trust bonus when the NPC knows the player's identity in advanced trust mode (0.0–0.2).")]
		public float FamiliarityTrustBonus
		{
			get
			{
				return this._familiarityTrustBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._familiarityTrustBonus != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1129678519;
						for (;;)
						{
							int num2 = num;
							switch ((~1351483546 - ~num2 ^ -558465295) * 1993910245 % 4)
							{
							case 1:
								goto IL_7A;
							case 2:
							{
								this._familiarityTrustBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7A;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1932411907), value);
								num = 1011893962;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_7A:
							num = -1840358677;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000065 RID: 101
		// (get) Token: 0x0600029F RID: 671 RVA: 0x00052C10 File Offset: 0x00050E10
		// (set) Token: 0x060002A0 RID: 672 RVA: 0x00052C24 File Offset: 0x00050E24
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_MinLieTrustPenalty}Minimum Lie Trust Penalty", 0.05f, 0.1f, "#0.00", Order = 16, RequireRestart = false, HintText = "{=AIInfluence_MinLieTrustPenalty_Hint}Minimum trust penalty when a lie is detected in advanced trust mode (0.05–0.1).")]
		public float MinLieTrustPenalty
		{
			get
			{
				return this._minLieTrustPenalty;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minLieTrustPenalty != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1095962947;
						for (;;)
						{
							int num2 = num;
							switch ((-226564013 - ~(~num2) * -1875557263) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_74;
							case 2:
							{
								this._minLieTrustPenalty = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_74;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2048823244), value);
								num = 703482994;
								continue;
							}
							}
							goto Block_1;
							IL_74:
							num = -1221736320;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000066 RID: 102
		// (get) Token: 0x060002A1 RID: 673 RVA: 0x00052CB0 File Offset: 0x00050EB0
		// (set) Token: 0x060002A2 RID: 674 RVA: 0x00052CC4 File Offset: 0x00050EC4
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyFloatingInteger("{=AIInfluence_MaxLieTrustPenalty}Maximum Lie Trust Penalty", 0.1f, 0.2f, "#0.00", Order = 17, RequireRestart = false, HintText = "{=AIInfluence_MaxLieTrustPenalty_Hint}Maximum trust penalty when a lie is detected in advanced trust mode (0.1–0.2).")]
		public float MaxLieTrustPenalty
		{
			get
			{
				return this._maxLieTrustPenalty;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxLieTrustPenalty != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1913753073;
						for (;;)
						{
							int num2 = num;
							switch (-(~(num2 ^ (148428257 ^ -2050616453) - -1249307444 - 1835696067 * 162142357 ^ (34423939 + -680988793 ^ -957880437 * 1002916148))) % 4)
							{
							case 1:
							{
								this._maxLieTrustPenalty = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_A4;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1373246234), value);
								num = 1088166303;
								continue;
							}
							case 2:
								goto IL_14;
							case 3:
								goto IL_A4;
							}
							goto Block_1;
							IL_A4:
							num = 1018243658;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000067 RID: 103
		// (get) Token: 0x060002A3 RID: 675 RVA: 0x00052D80 File Offset: 0x00050F80
		// (set) Token: 0x060002A4 RID: 676 RVA: 0x00052D94 File Offset: 0x00050F94
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MinLieRelationPenalty}Minimum Lie Relation Penalty", 1, 10, "0 Points", Order = 18, RequireRestart = false, HintText = "{=AIInfluence_MinLieRelationPenalty_Hint}Minimum relation penalty when a lie is detected (1–5).")]
		public int MinLieRelationPenalty
		{
			get
			{
				return this._minLieRelationPenalty;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minLieRelationPenalty != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 872567227;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - (~-1418018243 * -1082769143 - (626427117 - -1100560457) * 129083385) + (-1911688094 * 922448969 ^ -28105397) - --721910029) * -1858545675 % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_AB;
							case 3:
							{
								this._minLieRelationPenalty = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_AB;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1159209170), value);
								num = -1945878575;
								continue;
							}
							}
							goto Block_1;
							IL_AB:
							num = 445258966;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000068 RID: 104
		// (get) Token: 0x060002A5 RID: 677 RVA: 0x00052E58 File Offset: 0x00051058
		// (set) Token: 0x060002A6 RID: 678 RVA: 0x00052E6C File Offset: 0x0005106C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MaxLieRelationPenalty}Maximum Lie Relation Penalty", 1, 10, "0 Points", Order = 19, RequireRestart = false, HintText = "{=AIInfluence_MaxLieRelationPenalty_Hint}Maximum relation penalty when a lie is detected (5–10).")]
		public int MaxLieRelationPenalty
		{
			get
			{
				return this._maxLieRelationPenalty;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxLieRelationPenalty != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 506816584;
						for (;;)
						{
							int num2 = num;
							switch (((num2 * -1631582579 ^ 72745883) - (-121808520 + 1233007842) - 587232023) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_84;
							case 2:
							{
								this._maxLieRelationPenalty = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_84;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1100718668), value);
								num = 876762453;
								continue;
							}
							}
							goto Block_1;
							IL_84:
							num = 446538327;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000069 RID: 105
		// (get) Token: 0x060002A7 RID: 679 RVA: 0x00052F08 File Offset: 0x00051108
		// (set) Token: 0x060002A8 RID: 680 RVA: 0x00052F1C File Offset: 0x0005111C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MinPositiveRelationChange}Minimum Positive Relation Change", 1, 10, "0 Points", Order = 20, RequireRestart = false, HintText = "{=AIInfluence_MinPositiveRelationChange_Hint}Minimum relation increase when the player communicates positively (1–5).")]
		public int MinPositiveRelationChange
		{
			get
			{
				return this._minPositiveRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minPositiveRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 742732285;
						for (;;)
						{
							int num2 = num;
							switch (~(num2 + (-(538667991 * -1181350587) + -(~-1317769502)) + 987280364) * -432123327 % 4)
							{
							case 1:
								goto IL_88;
							case 2:
							{
								this._minPositiveRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_88;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1358393572), value);
								num = -1874253530;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_88:
							num = -418197493;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006A RID: 106
		// (get) Token: 0x060002A9 RID: 681 RVA: 0x00052FBC File Offset: 0x000511BC
		// (set) Token: 0x060002AA RID: 682 RVA: 0x00052FD0 File Offset: 0x000511D0
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MaxPositiveRelationChange}Maximum Positive Relation Change", 1, 10, "0 Points", Order = 21, RequireRestart = false, HintText = "{=AIInfluence_MaxPositiveRelationChange_Hint}Maximum relation increase when the player communicates positively (5–10).")]
		public int MaxPositiveRelationChange
		{
			get
			{
				return this._maxPositiveRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxPositiveRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -642040946;
						for (;;)
						{
							int num2 = num;
							switch ((num2 + (1922763411 * ~-706338370 - ~-947206121 * -1858264165) - -(-621373195 ^ 1832140053)) % 4)
							{
							case 1:
							{
								this._maxPositiveRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_90;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(52851014), value);
								num = -1374860297;
								continue;
							}
							case 2:
								goto IL_90;
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_90:
							num = -1709294671;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006B RID: 107
		// (get) Token: 0x060002AB RID: 683 RVA: 0x00053078 File Offset: 0x00051278
		// (set) Token: 0x060002AC RID: 684 RVA: 0x0005308C File Offset: 0x0005128C
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MinNegativeRelationChange}Minimum Negative Relation Change", 1, 10, "0 Points", Order = 22, RequireRestart = false, HintText = "{=AIInfluence_MinNegativeRelationChange_Hint}Minimum relation decrease when the player communicates aggressively (1–5).")]
		public int MinNegativeRelationChange
		{
			get
			{
				return this._minNegativeRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minNegativeRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -319455857;
						for (;;)
						{
							int num2 = num;
							switch (~(-num2 - -(-949028706 - -2084595585) - -2014718940) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_7C;
							case 3:
							{
								this._minNegativeRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7C;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(84589393), value);
								num = 949966326;
								continue;
							}
							}
							goto Block_1;
							IL_7C:
							num = 415974041;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006C RID: 108
		// (get) Token: 0x060002AD RID: 685 RVA: 0x00053120 File Offset: 0x00051320
		// (set) Token: 0x060002AE RID: 686 RVA: 0x00053134 File Offset: 0x00051334
		[SettingPropertyGroup("{=AIInfluence_Group_Prompt}Prompt Settings", GroupOrder = 5)]
		[SettingPropertyInteger("{=AIInfluence_MaxNegativeRelationChange}Maximum Negative Relation Change", 1, 10, "0 Points", Order = 23, RequireRestart = false, HintText = "{=AIInfluence_MaxNegativeRelationChange_Hint}Maximum relation decrease when the player communicates aggressively (5–10).")]
		public int MaxNegativeRelationChange
		{
			get
			{
				return this._maxNegativeRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxNegativeRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -864368150;
						for (;;)
						{
							int num2 = num;
							switch (((-1647160977 ^ 1663750131) - (-1805881154 + 681301496) - ~num2) * 2007263911 * 241554513 % 4)
							{
							case 0:
								goto IL_14;
							case 2:
								goto IL_8E;
							case 3:
							{
								this._maxNegativeRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8E;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1011757452), value);
								num = -1778650993;
								continue;
							}
							}
							goto Block_1;
							IL_8E:
							num = -707260696;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006D RID: 109
		// (get) Token: 0x060002AF RID: 687 RVA: 0x000531D8 File Offset: 0x000513D8
		// (set) Token: 0x060002B0 RID: 688 RVA: 0x000531EC File Offset: 0x000513EC
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FemaleNPCRomanceInitiative}Female NPC Romance Initiative Chance", 0f, 1f, "#0.0", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_FemaleNPCRomanceInitiative_Hint}Chance (0–1) for female NPCs to initiate romantic interactions with a male player.")]
		public float FemaleNPCRomanceInitiative
		{
			get
			{
				return this._femaleNPCRomanceInitiative;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._femaleNPCRomanceInitiative != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1177926602;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - ((1951286335 ^ -1534942546) * 773094991 - (-329438316 ^ ~-2022295105))) * -1103670763 % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_8E;
							case 3:
							{
								this._femaleNPCRomanceInitiative = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8E;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1839792940), value);
								num = 1407688268;
								continue;
							}
							}
							goto Block_1;
							IL_8E:
							num = -1325554235;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006E RID: 110
		// (get) Token: 0x060002B1 RID: 689 RVA: 0x00053290 File Offset: 0x00051490
		// (set) Token: 0x060002B2 RID: 690 RVA: 0x000532A4 File Offset: 0x000514A4
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyFloatingInteger("{=AIInfluence_MaleNPCRomanceInitiative}Male NPC Romance Initiative Chance", 0f, 1f, "#0.0", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_MaleNPCRomanceInitiative_Hint}Chance (0–1) for male NPCs to initiate romantic interactions with a female player.")]
		public float MaleNPCRomanceInitiative
		{
			get
			{
				return this._maleNPCRomanceInitiative;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maleNPCRomanceInitiative != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1134888698;
						for (;;)
						{
							int num2 = num;
							switch ((-834188585 + -1265901186 - ~num2 * 172529025 ^ -503195651) % 4)
							{
							case 0:
								goto IL_7F;
							case 1:
							{
								this._maleNPCRomanceInitiative = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7F;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(396376036), value);
								num = -664951973;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_7F:
							num = -192189480;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700006F RID: 111
		// (get) Token: 0x060002B3 RID: 691 RVA: 0x00053338 File Offset: 0x00051538
		// (set) Token: 0x060002B4 RID: 692 RVA: 0x0005334C File Offset: 0x0005154C
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyInteger("{=AIInfluence_MinRomanceChange}Minimum Romance Level Change", 1, 10, "0 Points", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_MinRomanceChange_Hint}Minimum change in RomanceLevel for successful romantic interactions.")]
		public int MinRomanceChange
		{
			get
			{
				return this._minRomanceChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minRomanceChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 2044379056;
						for (;;)
						{
							int num2 = num;
							switch ((-1647531363 + -922518945 - ((num2 ^ ~(1155751065 - 846134842) - -1999844663) - ~(1889441068 - -1787811030))) % 4)
							{
							case 0:
								goto IL_14;
							case 2:
							{
								this._minRomanceChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_95;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(383144755), value);
								num = 419681177;
								continue;
							}
							case 3:
								goto IL_95;
							}
							goto Block_1;
							IL_95:
							num = 1225624879;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000070 RID: 112
		// (get) Token: 0x060002B5 RID: 693 RVA: 0x000533FC File Offset: 0x000515FC
		// (set) Token: 0x060002B6 RID: 694 RVA: 0x00053410 File Offset: 0x00051610
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyInteger("{=AIInfluence_MaxRomanceChange}Maximum Romance Level Change", 1, 10, "0 Points", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_MaxRomanceChange_Hint}Maximum change in RomanceLevel for successful romantic interactions.")]
		public int MaxRomanceChange
		{
			get
			{
				return this._maxRomanceChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxRomanceChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 904423314;
						for (;;)
						{
							int num2 = num;
							switch (~(-1216292659 - 381426900 - (1259169673 + 1372956074) - num2 * -768459103) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_85;
							case 3:
							{
								this._maxRomanceChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_85;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-493630830), value);
								num = -1208422151;
								continue;
							}
							}
							goto Block_1;
							IL_85:
							num = -2096397912;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000071 RID: 113
		// (get) Token: 0x060002B7 RID: 695 RVA: 0x000534AC File Offset: 0x000516AC
		// (set) Token: 0x060002B8 RID: 696 RVA: 0x000534C0 File Offset: 0x000516C0
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyInteger("{=AIInfluence_RomanceDecayDays}Romance Decay Days", 1, 60, "0 Days", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_RomanceDecayDays_Hint}Number of days without romantic interaction before RomanceLevel starts to decay.")]
		public int RomanceDecayDays
		{
			get
			{
				return this._romanceDecayDays;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._romanceDecayDays != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1289695501;
						for (;;)
						{
							int num2 = num;
							switch ((~-979176482 - (~num2 ^ ~759961677 - 26084773) ^ -498338871) % 4)
							{
							case 1:
							{
								this._romanceDecayDays = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_81;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(374323901), value);
								num = -1331623799;
								continue;
							}
							case 2:
								goto IL_11;
							case 3:
								goto IL_81;
							}
							goto Block_1;
							IL_81:
							num = 1549118406;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000072 RID: 114
		// (get) Token: 0x060002B9 RID: 697 RVA: 0x00053558 File Offset: 0x00051758
		// (set) Token: 0x060002BA RID: 698 RVA: 0x0005356C File Offset: 0x0005176C
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyInteger("{=AIInfluence_MinRomanceToAcceptMarriage}Minimum Romance to Accept Marriage", 0, 100, "0", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_MinRomanceToAcceptMarriage_Hint}Minimum RomanceLevel (0-100) required for NPC to accept marriage proposal from player.")]
		public int MinRomanceToAcceptMarriage
		{
			get
			{
				return this._minRomanceToAcceptMarriage;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minRomanceToAcceptMarriage != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1234717835;
						for (;;)
						{
							int num2 = num;
							switch ((~(num2 * -1121954277 ^ 677015175 + -1240385846 * 2098732861) ^ 1217764324) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._minRomanceToAcceptMarriage = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_85;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1147463306), value);
								num = 675844728;
								continue;
							}
							case 2:
								goto IL_85;
							}
							goto Block_1;
							IL_85:
							num = 206214783;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000073 RID: 115
		// (get) Token: 0x060002BB RID: 699 RVA: 0x00053608 File Offset: 0x00051808
		// (set) Token: 0x060002BC RID: 700 RVA: 0x0005361C File Offset: 0x0005181C
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyInteger("{=AIInfluence_RomanceDecayAmount}Romance Decay Amount", 1, 100, "0 Points", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_RomanceDecayAmount_Hint}Amount by which RomanceLevel decreases per day after decay period.")]
		public int RomanceDecayAmount
		{
			get
			{
				return this._romanceDecayAmount;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._romanceDecayAmount != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 299479674;
						for (;;)
						{
							int num2 = num;
							switch ((((-883525348 ^ 104339007) - (1708714979 - -93453151)) * -1266960027 - num2 - -(1434093715 + 634427640)) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_94;
							case 2:
							{
								this._romanceDecayAmount = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_94;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-816349649), value);
								num = -312310809;
								continue;
							}
							}
							goto Block_1;
							IL_94:
							num = 516562133;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000074 RID: 116
		// (get) Token: 0x060002BD RID: 701 RVA: 0x000536C8 File Offset: 0x000518C8
		// (set) Token: 0x060002BE RID: 702 RVA: 0x000536DC File Offset: 0x000518DC
		[SettingPropertyGroup("{=AIInfluence_Group_Romance}Romance Settings", GroupOrder = 6)]
		[SettingPropertyBool("{=AIInfluence_AllowRomanceWithMarried}Allow Romance with Married NPCs", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_AllowRomanceWithMarried_Hint}Allow romantic interactions with married NPCs (player or NPC can be married). Warning: This may lead to affairs and divorces.")]
		public bool AllowRomanceWithMarried
		{
			get
			{
				return this._allowRomanceWithMarried;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._allowRomanceWithMarried != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -434637564;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - (-(-433675189 * -1206016461) + ~(-599565291)) ^ -(~1231310403)) % 4)
							{
							case 0:
								goto IL_83;
							case 2:
								goto IL_11;
							case 3:
							{
								this._allowRomanceWithMarried = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_83;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(40961981), value);
								num = -687988923;
								continue;
							}
							}
							goto Block_1;
							IL_83:
							num = 190909770;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000075 RID: 117
		// (get) Token: 0x060002BF RID: 703 RVA: 0x00053774 File Offset: 0x00051974
		// (set) Token: 0x060002C0 RID: 704 RVA: 0x00053788 File Offset: 0x00051988
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_CompanionAutoRecognize}Companions Auto-Recognize Player", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_CompanionAutoRecognize_Hint}NPCs automatically recognize you if they are your companions.")]
		public bool CompanionAutoRecognize
		{
			get
			{
				return this._companionAutoRecognize;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._companionAutoRecognize != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1031044019;
						for (;;)
						{
							int num2 = num;
							switch ((~(~(num2 + --914293576)) ^ 1345938974) % 4)
							{
							case 0:
								goto IL_75;
							case 1:
							{
								this._companionAutoRecognize = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_75;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1773636535), value);
								num = -317784030;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_75:
							num = 623881976;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000076 RID: 118
		// (get) Token: 0x060002C1 RID: 705 RVA: 0x00053814 File Offset: 0x00051A14
		// (set) Token: 0x060002C2 RID: 706 RVA: 0x00053828 File Offset: 0x00051A28
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_KingdomAutoRecognize}Kingdom Members Auto-Recognize Player", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_KingdomAutoRecognize_Hint}All NPCs in your kingdom automatically recognize you if you are the kingdom leader.")]
		public bool KingdomAutoRecognize
		{
			get
			{
				return this._kingdomAutoRecognize;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._kingdomAutoRecognize != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1686753414;
						for (;;)
						{
							int num2 = num;
							switch ((--432289977 - num2) * 967144199 * -1445352427 % 4)
							{
							case 0:
								goto IL_79;
							case 1:
							{
								this._kingdomAutoRecognize = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_79;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1760405254), value);
								num = 1696297973;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_79:
							num = -1920603696;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000077 RID: 119
		// (get) Token: 0x060002C3 RID: 707 RVA: 0x000538B8 File Offset: 0x00051AB8
		// (set) Token: 0x060002C4 RID: 708 RVA: 0x000538CC File Offset: 0x00051ACC
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_ClanTierAutoRecognize}Clan Tier Auto-Recognition", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_ClanTierAutoRecognize_Hint}NPCs have a chance to recognize you based on your clan tier level.")]
		public bool ClanTierAutoRecognize
		{
			get
			{
				return this._clanTierAutoRecognize;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._clanTierAutoRecognize != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -51737759;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - ~(-(--1947472981)) ^ -1772691006 + -158868827 + (687585281 + 396460928)) % 4)
							{
							case 0:
								goto IL_87;
							case 1:
							{
								this._clanTierAutoRecognize = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_87;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1991855341), value);
								num = -75255508;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_87:
							num = 1839447834;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000078 RID: 120
		// (get) Token: 0x060002C5 RID: 709 RVA: 0x00053968 File Offset: 0x00051B68
		// (set) Token: 0x060002C6 RID: 710 RVA: 0x0005397C File Offset: 0x00051B7C
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyInteger("{=AIInfluence_ClanTierThreshold}Clan Tier Recognition Threshold", 1, 6, "0 Tier", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_ClanTierThreshold_Hint}Minimum clan tier level for NPCs to have a chance to recognize you. Default: 3.")]
		public int ClanTierThreshold
		{
			get
			{
				return this._clanTierThreshold;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._clanTierThreshold != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1067268501;
						for (;;)
						{
							int num2 = num;
							switch (((-1347174239 ^ -720890709) - (-num2 ^ -(~607761413))) * -39024347 % 4)
							{
							case 0:
								goto IL_81;
							case 1:
							{
								this._clanTierThreshold = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_81;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-227547937), value);
								num = -894519948;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_81:
							num = -1033358366;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000079 RID: 121
		// (get) Token: 0x060002C7 RID: 711 RVA: 0x00053A14 File Offset: 0x00051C14
		// (set) Token: 0x060002C8 RID: 712 RVA: 0x00053A28 File Offset: 0x00051C28
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_ClanTierRecognitionChance}Clan Tier Recognition Chance", 1f, 100f, "0%", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_ClanTierRecognitionChance_Hint}Chance percentage for NPCs to recognize you based on clan tier. Default: 25%.")]
		public float ClanTierRecognitionChance
		{
			get
			{
				return this._clanTierRecognitionChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._clanTierRecognitionChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1407976561;
						for (;;)
						{
							int num2 = num;
							switch ((~(~num2) * -2105367071 ^ -534721632) % 4)
							{
							case 0:
								goto IL_74;
							case 1:
							{
								this._clanTierRecognitionChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_74;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1288541121), value);
								num = -1848562544;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_74:
							num = -390012069;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007A RID: 122
		// (get) Token: 0x060002C9 RID: 713 RVA: 0x00053AB4 File Offset: 0x00051CB4
		// (set) Token: 0x060002CA RID: 714 RVA: 0x00053AC8 File Offset: 0x00051CC8
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_RealisticInfoSpread}Realistic Information Spread", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_RealisticInfoSpread_Hint}Enable realistic information spreading: NPCs only learn about events based on distance, social status, and relationships. Disable for classic behavior where all NPCs know everything.")]
		public bool RealisticInformationSpread
		{
			get
			{
				return this._realisticInformationSpread;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._realisticInformationSpread != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 595610128;
						for (;;)
						{
							int num2 = num;
							switch ((-(num2 ^ (589108249 * -1959226493 - (-186360271 + 1576690692)) * 858393705) + 1506263890 ^ -1190336365) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_94;
							case 3:
							{
								this._realisticInformationSpread = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_94;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1009761575), value);
								num = 1710171454;
								continue;
							}
							}
							goto Block_1;
							IL_94:
							num = -1415301469;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007B RID: 123
		// (get) Token: 0x060002CB RID: 715 RVA: 0x00053B74 File Offset: 0x00051D74
		// (set) Token: 0x060002CC RID: 716 RVA: 0x00053B88 File Offset: 0x00051D88
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_LocalNewsDistance}Local News Distance", 5f, 50f, "0 Units", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_LocalNewsDistance_Hint}Maximum distance for local events (tournaments, marriages, deaths). Default: 15 units.")]
		public float LocalNewsDistance
		{
			get
			{
				return this._localNewsDistance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._localNewsDistance != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1685858726;
						for (;;)
						{
							int num2 = num;
							switch (~((2038419478 ^ (-1517091858 ^ -795367197)) - num2 * -1932159985 ^ -877371798 - 1439390943) % 4)
							{
							case 1:
							{
								this._localNewsDistance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8E;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-919175747), value);
								num = 198713015;
								continue;
							}
							case 2:
								goto IL_8E;
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_8E:
							num = 2076153581;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007C RID: 124
		// (get) Token: 0x060002CD RID: 717 RVA: 0x00053C2C File Offset: 0x00051E2C
		// (set) Token: 0x060002CE RID: 718 RVA: 0x00053C40 File Offset: 0x00051E40
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_RegionalNewsDistance}Regional News Distance", 20f, 150f, "0 Units", Order = 7, RequireRestart = false, HintText = "{=AIInfluence_RegionalNewsDistance_Hint}Maximum distance for regional events (wars, battles, settlements). Default: 50 units.")]
		public float RegionalNewsDistance
		{
			get
			{
				return this._regionalNewsDistance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._regionalNewsDistance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1445839999;
						for (;;)
						{
							int num2 = num;
							switch ((~(~num2) * -1591902103 - -394186140) % 4)
							{
							case 0:
								goto IL_74;
							case 2:
								goto IL_11;
							case 3:
							{
								this._regionalNewsDistance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_74;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1132183449), value);
								num = 775942044;
								continue;
							}
							}
							goto Block_1;
							IL_74:
							num = 650424557;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007D RID: 125
		// (get) Token: 0x060002CF RID: 719 RVA: 0x00053CCC File Offset: 0x00051ECC
		// (set) Token: 0x060002D0 RID: 720 RVA: 0x00053CE0 File Offset: 0x00051EE0
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_KingdomNewsDistance}Kingdom News Distance", 50f, 300f, "0 Units", Order = 8, RequireRestart = false, HintText = "{=AIInfluence_KingdomNewsDistance_Hint}Maximum distance for kingdom events (laws, political decisions). Default: 150 units.")]
		public float KingdomNewsDistance
		{
			get
			{
				return this._kingdomNewsDistance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._kingdomNewsDistance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 920598140;
						for (;;)
						{
							int num2 = num;
							switch (-(~(~-212781610 * 101635603 + -(~-730946911) - num2)) * -1733351131 % 4)
							{
							case 0:
								goto IL_11;
							case 2:
							{
								this._kingdomNewsDistance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_83;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1852336217), value);
								num = 1677578411;
								continue;
							}
							case 3:
								goto IL_83;
							}
							goto Block_1;
							IL_83:
							num = 1412176661;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007E RID: 126
		// (get) Token: 0x060002D1 RID: 721 RVA: 0x00053D78 File Offset: 0x00051F78
		// (set) Token: 0x060002D2 RID: 722 RVA: 0x00053D8C File Offset: 0x00051F8C
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NobleDistanceMultiplier}Noble Distance Multiplier", 1f, 3f, "0.0x", Order = 9, RequireRestart = false, HintText = "{=AIInfluence_NobleDistanceMultiplier_Hint}Distance multiplier for nobles (medium access level). Default: 1.5x.")]
		public float NobleDistanceMultiplier
		{
			get
			{
				return this._nobleDistanceMultiplier;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._nobleDistanceMultiplier != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -463976228;
						for (;;)
						{
							int num2 = num;
							switch ((1974755848 - (-num2 + -1582781690 - -1226419101)) % 4)
							{
							case 0:
								goto IL_79;
							case 1:
							{
								this._nobleDistanceMultiplier = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_79;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(294561875), value);
								num = -205659569;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_79:
							num = -1560623010;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700007F RID: 127
		// (get) Token: 0x060002D3 RID: 723 RVA: 0x00053E1C File Offset: 0x0005201C
		// (set) Token: 0x060002D4 RID: 724 RVA: 0x00053E30 File Offset: 0x00052030
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyFloatingInteger("{=AIInfluence_RoyalDistanceMultiplier}Royal Distance Multiplier", 1f, 5f, "0.0x", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_RoyalDistanceMultiplier_Hint}Distance multiplier for kings and clan leaders (high access level). Default: 2.0x.")]
		public float RoyalDistanceMultiplier
		{
			get
			{
				return this._royalDistanceMultiplier;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._royalDistanceMultiplier != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 496743802;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)((num2 ^ (971039596 + -1837586787 - -1650364667 ^ (-290559044 ^ --514287268))) * 1455929791 + -1326067583 * -2093392329 - 543243805)) % 5U)
							{
							case 0U:
								goto IL_14;
							case 1U:
								goto IL_A8;
							case 2U:
							{
								this._royalDistanceMultiplier = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_A8;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-262913214), value);
								num = -678248499;
								continue;
							}
							case 4U:
								num = (int)(num3 * 663560476U ^ 2773568119U);
								continue;
							}
							goto Block_1;
							IL_A8:
							num = -607429561;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000080 RID: 128
		// (get) Token: 0x060002D5 RID: 725 RVA: 0x00053F04 File Offset: 0x00052104
		// (set) Token: 0x060002D6 RID: 726 RVA: 0x00053F18 File Offset: 0x00052118
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyInteger("{=AIInfluence_RelationshipThreshold}Relationship Threshold", 5, 50, "0 Points", Order = 11, RequireRestart = false, HintText = "{=AIInfluence_RelationshipThreshold_Hint}Minimum relationship level for NPCs to learn about personal events regardless of distance. Default: 20 points.")]
		public int RelationshipThreshold
		{
			get
			{
				return this._relationshipThreshold;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				for (;;)
				{
					IL_01:
					int num = 917265487;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)((-1799712974 ^ -1660882392) - -num2 * 2112210861 - -514695075)) % 5U)
						{
						case 0U:
							goto IL_40;
						case 1U:
						{
							bool flag = this._relationshipThreshold != value;
							num = (int)(((!flag) ? 1163060911U : 3494970463U) ^ num3 * 1855796464U);
							continue;
						}
						case 3U:
						{
							this._relationshipThreshold = value;
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_40;
							}
							onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1245899215), value);
							num = 1495901785;
							continue;
						}
						case 4U:
							goto IL_01;
						}
						return;
						IL_40:
						num = 1255786671;
					}
				}
			}
		}

		// Token: 0x17000081 RID: 129
		// (get) Token: 0x060002D7 RID: 727 RVA: 0x00053FCC File Offset: 0x000521CC
		// (set) Token: 0x060002D8 RID: 728 RVA: 0x00053FE0 File Offset: 0x000521E0
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_EnableSocialFiltering}Enable Social Filtering", Order = 12, RequireRestart = false, HintText = "{=AIInfluence_EnableSocialFiltering_Hint}Enable social class filtering: commoners won't learn about political events, nobles get priority access to information.")]
		public bool EnableSocialFiltering
		{
			get
			{
				return this._enableSocialFiltering;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableSocialFiltering != value;
				for (;;)
				{
					IL_0E:
					int num = 284729633;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(~num2 + 1084383747 - -333082496 - 890209533)) % 6U)
						{
						case 0U:
							goto IL_0E;
						case 2U:
							num = (int)(((!flag) ? 741057796U : 2539775612U) ^ num3 * 396836690U);
							continue;
						case 3U:
							goto IL_4B;
						case 4U:
						{
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_4B;
							}
							onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(349857216), value);
							num = -496808456;
							continue;
						}
						case 5U:
							this._enableSocialFiltering = value;
							num = (int)(num3 * 1808652593U ^ 3822171334U);
							continue;
						}
						return;
						IL_4B:
						num = 232990476;
					}
				}
			}
		}

		// Token: 0x17000082 RID: 130
		// (get) Token: 0x060002D9 RID: 729 RVA: 0x000540A4 File Offset: 0x000522A4
		// (set) Token: 0x060002DA RID: 730 RVA: 0x000540B8 File Offset: 0x000522B8
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_EnableRelationshipOverride}Enable Relationship Override", Order = 13, RequireRestart = false, HintText = "{=AIInfluence_EnableRelationshipOverride_Hint}Allow close friends/enemies to learn about events regardless of distance and social status.")]
		public bool EnableRelationshipOverride
		{
			get
			{
				return this._enableRelationshipOverride;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableRelationshipOverride != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1341828297;
						for (;;)
						{
							int num2 = num;
							switch ((~num2 + ~(828742319 * 367592397) + (1668370612 - 100843220) - -351838639) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._enableRelationshipOverride = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1140096472), value);
								num = 569279416;
								continue;
							}
							case 2:
								goto IL_86;
							}
							goto Block_1;
							IL_86:
							num = 1143736087;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000083 RID: 131
		// (get) Token: 0x060002DB RID: 731 RVA: 0x00054154 File Offset: 0x00052354
		// (set) Token: 0x060002DC RID: 732 RVA: 0x00054168 File Offset: 0x00052368
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_EnableFactionOverride}Enable Faction Override", Order = 14, RequireRestart = false, HintText = "{=AIInfluence_EnableFactionOverride_Hint}Members of the same faction always know about faction-related events (wars, battles).")]
		public bool EnableFactionOverride
		{
			get
			{
				return this._enableFactionOverride;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableFactionOverride != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 2137066484;
						for (;;)
						{
							int num2 = num;
							switch (~(~(~(-num2))) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_6A;
							case 3:
							{
								this._enableFactionOverride = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_6A;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1780042839), value);
								num = 1445929378;
								continue;
							}
							}
							goto Block_1;
							IL_6A:
							num = 733556691;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000084 RID: 132
		// (get) Token: 0x060002DD RID: 733 RVA: 0x000541E8 File Offset: 0x000523E8
		// (set) Token: 0x060002DE RID: 734 RVA: 0x000541FC File Offset: 0x000523FC
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyBool("{=AIInfluence_EnableDetailedLogging}Enable Detailed Info Logging", Order = 15, RequireRestart = false, HintText = "{=AIInfluence_EnableDetailedLogging_Hint}Log detailed information about who learns what events and why. Useful for debugging information spread.")]
		public bool EnableDetailedInfoLogging
		{
			get
			{
				return this._enableDetailedInfoLogging;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableDetailedInfoLogging != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 996625507;
						for (;;)
						{
							int num2 = num;
							switch ((~(~num2 * 380550207) - -871956924) % 4)
							{
							case 0:
								goto IL_74;
							case 2:
								goto IL_11;
							case 3:
							{
								this._enableDetailedInfoLogging = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_74;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1766811558), value);
								num = -837610174;
								continue;
							}
							}
							goto Block_1;
							IL_74:
							num = -1802906567;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000085 RID: 133
		// (get) Token: 0x060002DF RID: 735 RVA: 0x00054288 File Offset: 0x00052488
		// (set) Token: 0x060002E0 RID: 736 RVA: 0x0005429C File Offset: 0x0005249C
		[SettingPropertyGroup("{=AIInfluence_Group_InfoLogic}Information Logic", GroupOrder = 7)]
		[SettingPropertyInteger("{=AIInfluence_NewsDelayHours}News Delay Hours per Distance", 0, 24, "0 Hours", Order = 16, RequireRestart = false, HintText = "{=AIInfluence_NewsDelayHours_Hint}Hours of delay per unit of distance for news to travel. Higher values make news spread more slowly. Default: 2 hours per distance unit.")]
		public int NewsDelayHoursPerDistance
		{
			get
			{
				return this._newsDelayHoursPerDistance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._newsDelayHoursPerDistance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -702339709;
						for (;;)
						{
							int num2 = num;
							switch (~(num2 ^ -(1012751842 ^ -1266858895) - -2121711149 * (1898578437 * 656023214)) % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_86;
							case 3:
							{
								this._newsDelayHoursPerDistance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2018788290), value);
								num = -367654950;
								continue;
							}
							}
							goto Block_1;
							IL_86:
							num = -1323602443;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000086 RID: 134
		// (get) Token: 0x060002E1 RID: 737 RVA: 0x00054338 File Offset: 0x00052538
		// (set) Token: 0x060002E2 RID: 738 RVA: 0x0005434C File Offset: 0x0005254C
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnableDynamicEvents}Enable Dynamic Events", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_EnableDynamicEvents_Hint}Enables AI-generated dynamic world events that NPCs know about and can discuss.")]
		public bool EnableDynamicEvents
		{
			get
			{
				return this._enableDynamicEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableDynamicEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 251233923;
						for (;;)
						{
							int num2 = num;
							uint num3;
							bool flag2;
							bool flag3;
							switch ((num3 = (uint)((-(318407906 * -396591869 + (-635135258 + 1547382657)) - num2 - (-173168632 ^ 779534759 ^ 1507774632) ^ -501494980) + 1692919820)) % 10U)
							{
							case 1U:
								this._enableDynamicEvents = value;
								if (!value)
								{
									num = (int)(num3 * 12473429U ^ 1046703794U);
									continue;
								}
								goto IL_15F;
							case 2U:
								goto IL_14;
							case 3U:
								num = (int)((flag2 ? 2185002092U : 1665366230U) ^ num3 * 2389136857U);
								continue;
							case 4U:
								goto IL_16B;
							case 5U:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_CB;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1789850040), value);
								num = -222044900;
								continue;
							}
							case 6U:
								flag3 = !this.CanEnableDiplomacy();
								goto IL_160;
							case 7U:
								if (this._enableDiplomacy)
								{
									num = (int)(num3 * 2234966340U ^ 4254752622U);
									continue;
								}
								goto IL_15F;
							case 8U:
								goto IL_CB;
							case 9U:
							{
								this._enableDiplomacy = false;
								Action<string, object> onSettingChanged2 = this.OnSettingChanged;
								if (onSettingChanged2 == null)
								{
									goto IL_16B;
								}
								onSettingChanged2(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(946004719), false);
								num = -846756524;
								continue;
							}
							}
							goto Block_1;
							IL_CB:
							num = -813194076;
							continue;
							IL_160:
							flag2 = flag3;
							num = -1420340063;
							continue;
							IL_15F:
							flag3 = false;
							goto IL_160;
							IL_16B:
							TextObject textObject = \u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1120022250), null);
							\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(textObject), Colors.Yellow));
							num = -631106851;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000087 RID: 135
		// (get) Token: 0x060002E3 RID: 739 RVA: 0x0005450C File Offset: 0x0005270C
		// (set) Token: 0x060002E4 RID: 740 RVA: 0x00054520 File Offset: 0x00052720
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_DynamicEventsDialogueOnly}Dialogue Analysis Only (No Global Events)", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_DynamicEventsDialogueOnly_Hint}When enabled, dynamic events will only analyze player-NPC dialogues and update NPC knowledge. No global events will be generated and no map notifications will be shown. Useful when you want dialogue analysis without diplomacy system.")]
		public bool DynamicEventsDialogueOnly
		{
			get
			{
				return this._dynamicEventsDialogueOnly;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._dynamicEventsDialogueOnly != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1181527700;
						for (;;)
						{
							int num2 = num;
							uint num3;
							bool flag2;
							switch ((num3 = (uint)(~(~num2) ^ ~-573616173)) % 9U)
							{
							case 0U:
								goto IL_73;
							case 1U:
							{
								this._enableDiplomacy = false;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_73;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1541662403), false);
								num = 1113176049;
								continue;
							}
							case 2U:
								goto IL_14;
							case 3U:
							{
								Action<string, object> onSettingChanged2 = this.OnSettingChanged;
								if (onSettingChanged2 == null)
								{
									goto IL_F9;
								}
								onSettingChanged2(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1089686631), value);
								num = 994880573;
								continue;
							}
							case 4U:
								this._dynamicEventsDialogueOnly = value;
								if (value)
								{
									num = (int)(num3 * 214796647U ^ 3272535776U);
									continue;
								}
								goto IL_C6;
							case 5U:
								goto IL_F9;
							case 7U:
								if (this._enableDiplomacy)
								{
									num = (int)(num3 * 665294940U ^ 303109272U);
									continue;
								}
								goto IL_C6;
							case 8U:
								flag2 = !this.CanEnableDiplomacy();
								goto IL_C7;
							}
							goto Block_1;
							IL_73:
							TextObject textObject = \u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1028122374), null);
							\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(textObject), Colors.Yellow));
							num = 1524062264;
							continue;
							IL_C7:
							num = (flag2 ? 1796645138 : 1524062264);
							continue;
							IL_C6:
							flag2 = false;
							goto IL_C7;
							IL_F9:
							num = 965254872;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000088 RID: 136
		// (get) Token: 0x060002E5 RID: 741 RVA: 0x00054698 File Offset: 0x00052898
		// (set) Token: 0x060002E6 RID: 742 RVA: 0x000546AC File Offset: 0x000528AC
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyDropdown("{=AIInfluence_DynamicEventsAIBackend}AI Backend for Events", RequireRestart = false, Order = 2, HintText = "{=AIInfluence_DynamicEventsAIBackend_Hint}Select which AI backend to use for dynamic event generation. Can be different from dialogue AI.")]
		public Dropdown<string> DynamicEventsAIBackend { get; set; } = new Dropdown<string>(new List<string>
		{
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-850742799),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1338752304),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(502993515),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(360400838),
			<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1777175873)
		}, 1);

		// Token: 0x17000089 RID: 137
		// (get) Token: 0x060002E7 RID: 743 RVA: 0x000546C0 File Offset: 0x000528C0
		// (set) Token: 0x060002E8 RID: 744 RVA: 0x000546D4 File Offset: 0x000528D4
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_MaxSimultaneousDynamicEvents}Max Simultaneous Events", 1, 3, "0 Events", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_MaxSimultaneousDynamicEvents_Hint}Maximum number of active dynamic events allowed at the same time. Default: 1.")]
		public int MaxSimultaneousDynamicEvents
		{
			get
			{
				return this._maxSimultaneousDynamicEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxSimultaneousDynamicEvents != value;
				for (;;)
				{
					IL_0E:
					int num = 1142545889;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(~(uint)(1685517369 - -823587903 + -1247545113 * 1679410101 - ((2107164853 ^ -1197208197) - (84251258 - 1684849868)) - num2 + -307079257))) % 5U)
						{
						case 1U:
						{
							this._maxSimultaneousDynamicEvents = value;
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_6C;
							}
							onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1158456401), value);
							num = 1300080446;
							continue;
						}
						case 2U:
							num = (int)((flag ? 2396601136U : 2114178544U) ^ num3 * 995659674U);
							continue;
						case 3U:
							goto IL_0E;
						case 4U:
							goto IL_6C;
						}
						return;
						IL_6C:
						num = -2103434084;
					}
				}
			}
		}

		// Token: 0x1700008A RID: 138
		// (get) Token: 0x060002E9 RID: 745 RVA: 0x000547AC File Offset: 0x000529AC
		// (set) Token: 0x060002EA RID: 746 RVA: 0x000547C0 File Offset: 0x000529C0
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_DynamicEventsInterval}Event Generation Interval (Days)", 1, 100, "0 Days", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_DynamicEventsInterval_Hint}How often AI generates new events (in game days). Default: 14 days.")]
		public int DynamicEventsInterval
		{
			get
			{
				return this._dynamicEventsInterval;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._dynamicEventsInterval != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 12120921;
						for (;;)
						{
							int num2 = num;
							switch (-(-num2 * 1538458115 * 994395607) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._dynamicEventsInterval = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_74;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-354587242), value);
								num = 1692770714;
								continue;
							}
							case 2:
								goto IL_74;
							}
							goto Block_1;
							IL_74:
							num = 777800591;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700008B RID: 139
		// (get) Token: 0x060002EB RID: 747 RVA: 0x0005484C File Offset: 0x00052A4C
		// (set) Token: 0x060002EC RID: 748 RVA: 0x00054860 File Offset: 0x00052A60
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_DynamicEventsMinLength}Minimum Event Description Length", 50, 1000, "0 Characters", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_DynamicEventsMinLength_Hint}Minimum characters for event descriptions. Default: 50.")]
		public int DynamicEventsMinLength
		{
			get
			{
				return this._dynamicEventsMinLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._dynamicEventsMinLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1334761465;
						for (;;)
						{
							int num2 = num;
							switch ((-num2 - ~(-1442224765 * -1787937704) ^ -643628107 - 1535365052) * -1839612915 % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_86;
							case 3:
							{
								this._dynamicEventsMinLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(177850563), value);
								num = -10798122;
								continue;
							}
							}
							goto Block_1;
							IL_86:
							num = -818270339;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700008C RID: 140
		// (get) Token: 0x060002ED RID: 749 RVA: 0x000548FC File Offset: 0x00052AFC
		// (set) Token: 0x060002EE RID: 750 RVA: 0x00054910 File Offset: 0x00052B10
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_DynamicEventsMaxLength}Maximum Event Description Length", 100, 1000, "0 Characters", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_DynamicEventsMaxLength_Hint}Maximum characters for event descriptions. Default: 500.")]
		public int DynamicEventsMaxLength
		{
			get
			{
				return this._dynamicEventsMaxLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._dynamicEventsMaxLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1479189202;
						for (;;)
						{
							int num2 = num;
							switch (~((num2 ^ ~(-762466336 * 313271859) + (~2065765654 + (1482638977 + -1706731222))) - ~891891583) % 4)
							{
							case 1:
								goto IL_91;
							case 2:
							{
								this._dynamicEventsMaxLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_91;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-5655171), value);
								num = -147294463;
								continue;
							}
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_91:
							num = 827502876;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700008D RID: 141
		// (get) Token: 0x060002EF RID: 751 RVA: 0x000549B8 File Offset: 0x00052BB8
		// (set) Token: 0x060002F0 RID: 752 RVA: 0x000549CC File Offset: 0x00052BCC
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_DynamicEventsLifespan}Event Lifespan (Days)", 1, 300, "0 Days", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_DynamicEventsLifespan_Hint}How long events exist before expiring. Default: 100 days.")]
		public int DynamicEventsLifespan
		{
			get
			{
				return this._dynamicEventsLifespan;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._dynamicEventsLifespan != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1283471547;
						for (;;)
						{
							int num2 = num;
							switch (-num2 * -1917596589 % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_6D;
							case 3:
							{
								this._dynamicEventsLifespan = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_6D;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1637054081), value);
								num = -1878448430;
								continue;
							}
							}
							goto Block_1;
							IL_6D:
							num = 1749309185;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700008E RID: 142
		// (get) Token: 0x060002F1 RID: 753 RVA: 0x00054A50 File Offset: 0x00052C50
		// (set) Token: 0x060002F2 RID: 754 RVA: 0x00054A64 File Offset: 0x00052C64
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnableMilitaryEvents}Enable Military Events", Order = 7, RequireRestart = false, HintText = "{=AIInfluence_EnableMilitaryEvents_Hint}Enable generation of military-related events (battles, wars, sieges).")]
		public bool EnableMilitaryEvents
		{
			get
			{
				return this._enableMilitaryEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableMilitaryEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1007191766;
						for (;;)
						{
							int num2 = num;
							switch (~(num2 * -121300729) * 1012539787 % 4)
							{
							case 0:
								goto IL_11;
							case 2:
								goto IL_73;
							case 3:
							{
								this._enableMilitaryEvents = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_73;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1887682670), value);
								num = -205829933;
								continue;
							}
							}
							goto Block_1;
							IL_73:
							num = 845418208;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700008F RID: 143
		// (get) Token: 0x060002F3 RID: 755 RVA: 0x00054AEC File Offset: 0x00052CEC
		// (set) Token: 0x060002F4 RID: 756 RVA: 0x00054B00 File Offset: 0x00052D00
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_MilitaryEventsChance}Military Events Chance (%)", 0, 100, "0", Order = 8, RequireRestart = false, HintText = "{=AIInfluence_MilitaryEventsChance_Hint}Chance for military events to be generated when enabled. Default: 20%.")]
		public int MilitaryEventsChance
		{
			get
			{
				return this._militaryEventsChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._militaryEventsChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -22351206;
						for (;;)
						{
							int num2 = num;
							switch (-(((num2 ^ (--200339632 ^ -339355023 * -1831455003 ^ --806840496 - (1282208131 - 254512854))) + (701882231 - -1122144970 - 1681054 * 110770025)) * 2105031905) % 4)
							{
							case 1:
							{
								this._militaryEventsChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_B1;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(146977574), value);
								num = 1684638405;
								continue;
							}
							case 2:
								goto IL_B1;
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_B1:
							num = -1926453749;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000090 RID: 144
		// (get) Token: 0x060002F5 RID: 757 RVA: 0x00054BCC File Offset: 0x00052DCC
		// (set) Token: 0x060002F6 RID: 758 RVA: 0x00054BE0 File Offset: 0x00052DE0
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnablePoliticalEvents}Enable Political Events", Order = 9, RequireRestart = false, HintText = "{=AIInfluence_EnablePoliticalEvents_Hint}Enable generation of political events (diplomacy, succession, intrigues).")]
		public bool EnablePoliticalEvents
		{
			get
			{
				return this._enablePoliticalEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enablePoliticalEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1969950740;
						for (;;)
						{
							int num2 = num;
							switch (-(~num2 * -1405115683) % 4)
							{
							case 0:
								goto IL_6E;
							case 1:
							{
								this._enablePoliticalEvents = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_6E;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1600409252), value);
								num = -1515049877;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_6E:
							num = -1927450891;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000091 RID: 145
		// (get) Token: 0x060002F7 RID: 759 RVA: 0x00054C64 File Offset: 0x00052E64
		// (set) Token: 0x060002F8 RID: 760 RVA: 0x00054C78 File Offset: 0x00052E78
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_PoliticalEventsChance}Political Events Chance (%)", 0, 100, "0", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_PoliticalEventsChance_Hint}Chance for political events to be generated when enabled. Default: 20%.")]
		public int PoliticalEventsChance
		{
			get
			{
				return this._politicalEventsChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._politicalEventsChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1262980024;
						for (;;)
						{
							int num2 = num;
							switch (((-num2 ^ 719420421 ^ ~886955742) - 604937083) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._politicalEventsChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7A;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(536724731), value);
								num = -1966755081;
								continue;
							}
							case 2:
								goto IL_7A;
							}
							goto Block_1;
							IL_7A:
							num = 1409837950;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000092 RID: 146
		// (get) Token: 0x060002F9 RID: 761 RVA: 0x00054D08 File Offset: 0x00052F08
		// (set) Token: 0x060002FA RID: 762 RVA: 0x00054D1C File Offset: 0x00052F1C
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnableEconomicEvents}Enable Economic Events", Order = 11, RequireRestart = false, HintText = "{=AIInfluence_EnableEconomicEvents_Hint}Enable generation of economic events (trade, resources, prosperity).")]
		public bool EnableEconomicEvents
		{
			get
			{
				return this._enableEconomicEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableEconomicEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 2094287359;
						for (;;)
						{
							int num2 = num;
							switch (~(~(-(num2 - (-63978477 + -1803311910 + (-396305574 - -712983367) ^ (~-1508710314 ^ --437178983))))) % 4)
							{
							case 0:
								goto IL_92;
							case 1:
							{
								this._enableEconomicEvents = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_92;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1789306905), value);
								num = -1220537780;
								continue;
							}
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_92:
							num = -1191338522;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000093 RID: 147
		// (get) Token: 0x060002FB RID: 763 RVA: 0x00054DC8 File Offset: 0x00052FC8
		// (set) Token: 0x060002FC RID: 764 RVA: 0x00054DDC File Offset: 0x00052FDC
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_EconomicEventsChance}Economic Events Chance (%)", 0, 100, "0", Order = 12, RequireRestart = false, HintText = "{=AIInfluence_EconomicEventsChance_Hint}Chance for economic events to be generated when enabled. Default: 20%.")]
		public int EconomicEventsChance
		{
			get
			{
				return this._economicEventsChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._economicEventsChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -765648597;
						for (;;)
						{
							int num2 = num;
							switch ((~(num2 - ((1803470423 - 1513020230) * 2033753111 ^ ~-879983248)) + (766678370 - -1511220704) ^ 1162176692) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_95;
							case 2:
							{
								this._economicEventsChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_95;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1403522456), value);
								num = -16570144;
								continue;
							}
							}
							goto Block_1;
							IL_95:
							num = -1770542190;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000094 RID: 148
		// (get) Token: 0x060002FD RID: 765 RVA: 0x00054E8C File Offset: 0x0005308C
		// (set) Token: 0x060002FE RID: 766 RVA: 0x00054EA0 File Offset: 0x000530A0
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnableSocialEvents}Enable Social Events", Order = 13, RequireRestart = false, HintText = "{=AIInfluence_EnableSocialEvents_Hint}Enable generation of social events (marriages, festivals, cultural events).")]
		public bool EnableSocialEvents
		{
			get
			{
				return this._enableSocialEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableSocialEvents != value;
				for (;;)
				{
					IL_0E:
					int num = 1385939816;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(~(~num2) * -1156902659 ^ -1837110931)) % 5U)
						{
						case 1U:
							goto IL_42;
						case 2U:
							goto IL_0E;
						case 3U:
							num = (int)((flag ? 192577994U : 3040675854U) ^ num3 * 2561939637U);
							continue;
						case 4U:
						{
							this._enableSocialEvents = value;
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_42;
							}
							onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1331722746), value);
							num = -1317336221;
							continue;
						}
						}
						return;
						IL_42:
						num = 1281882279;
					}
				}
			}
		}

		// Token: 0x17000095 RID: 149
		// (get) Token: 0x060002FF RID: 767 RVA: 0x00054F4C File Offset: 0x0005314C
		// (set) Token: 0x06000300 RID: 768 RVA: 0x00054F60 File Offset: 0x00053160
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_SocialEventsChance}Social Events Chance (%)", 0, 100, "0", Order = 14, RequireRestart = false, HintText = "{=AIInfluence_SocialEventsChance_Hint}Chance for social events to be generated when enabled. Default: 20%.")]
		public int SocialEventsChance
		{
			get
			{
				return this._socialEventsChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._socialEventsChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1838439714;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - -(-2038721674 - -1208615467) * 254921499 - -(~86656805)) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_81;
							case 3:
							{
								this._socialEventsChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_81;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(85231596), value);
								num = 1762545096;
								continue;
							}
							}
							goto Block_1;
							IL_81:
							num = 1035078969;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000096 RID: 150
		// (get) Token: 0x06000301 RID: 769 RVA: 0x00054FF8 File Offset: 0x000531F8
		// (set) Token: 0x06000302 RID: 770 RVA: 0x0005500C File Offset: 0x0005320C
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_EnableMysteriousEvents}Enable Mysterious Events", Order = 15, RequireRestart = false, HintText = "{=AIInfluence_EnableMysteriousEvents_Hint}Enable generation of mysterious events (supernatural, unexplained phenomena).")]
		public bool EnableMysteriousEvents
		{
			get
			{
				return this._enableMysteriousEvents;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableMysteriousEvents != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1719253005;
						for (;;)
						{
							int num2 = num;
							switch (~((num2 ^ -647984407 * 1566789865) - (-457248007 - 306826375) * -1490002295) % 4)
							{
							case 0:
								goto IL_85;
							case 1:
							{
								this._enableMysteriousEvents = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_85;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(606261842), value);
								num = -1425496320;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_85:
							num = -1125093417;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000097 RID: 151
		// (get) Token: 0x06000303 RID: 771 RVA: 0x000550A8 File Offset: 0x000532A8
		// (set) Token: 0x06000304 RID: 772 RVA: 0x000550BC File Offset: 0x000532BC
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyInteger("{=AIInfluence_MysteriousEventsChance}Mysterious Events Chance (%)", 0, 100, "0", Order = 16, RequireRestart = false, HintText = "{=AIInfluence_MysteriousEventsChance_Hint}Chance for mysterious events to be generated when enabled. Default: 20%.")]
		public int MysteriousEventsChance
		{
			get
			{
				return this._mysteriousEventsChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._mysteriousEventsChance != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1700470041;
						for (;;)
						{
							int num2 = num;
							switch (~(-(~(num2 * -2083913889))) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
							{
								this._mysteriousEventsChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_6F;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(199806198), value);
								num = -887038024;
								continue;
							}
							case 2:
								goto IL_6F;
							}
							goto Block_1;
							IL_6F:
							num = -2069720327;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000098 RID: 152
		// (get) Token: 0x06000305 RID: 773 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000306 RID: 774 RVA: 0x00055140 File Offset: 0x00053340
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_ForceGenerateEvent}Force Generate Event Now", Order = 17, RequireRestart = false, HintText = "{=AIInfluence_ForceGenerateEvent_Hint}Manually trigger event generation immediately (for testing).")]
		public bool ForceGenerateEvent
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_09:
						int num = 1657471108;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(~(uint)(-(uint)((num2 ^ -809656246 + -916104510 - ~632720324 - (--1425621940 + --1912833653)) * 118814937)))) % 6U)
							{
							case 0U:
								num = (int)(num3 * 2760358506U ^ 1090314526U);
								continue;
							case 1U:
							{
								DateTime dateTime;
								this._lastForceGenerateEventTime = dateTime;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_C7;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1808516279), value);
								num = -617293291;
								continue;
							}
							case 2U:
								goto IL_09;
							case 4U:
								goto IL_C7;
							case 5U:
							{
								DateTime dateTime = \u200B\u202A\u206E\u202A\u202D\u206D\u202B\u200E\u202D\u200F\u206D\u206A\u202B\u206D\u202C\u200F\u206F\u202C\u202C\u200C\u202C\u200F\u200F\u202E\u202E\u206B\u206C\u206D\u206C\u200D\u202A\u200C\u206E\u202E\u200E\u200E\u206D\u202B\u200D\u200D\u202E.Y\u000C8FÛ();
								bool flag = \u206E\u206C\u206B\u206F\u200D\u202D\u202D\u200B\u206B\u200F\u202C\u206F\u206F\u206A\u206F\u206A\u200B\u200C\u206D\u202E\u200E\u202A\u200D\u202E\u200D\u206D\u206E\u202A\u200E\u200B\u200F\u200E\u202C\u200C\u200F\u202D\u206F\u206E\u202E\u202C\u202E.À\u008C\u001Ar[(dateTime, this._lastForceGenerateEventTime).TotalSeconds < 2.0;
								num = (int)(((!flag) ? 790456700U : 2945695305U) ^ num3 * 3988066284U);
								continue;
							}
							}
							goto Block_1;
							IL_C7:
							num = 618026310;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x17000099 RID: 153
		// (get) Token: 0x06000307 RID: 775 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000308 RID: 776 RVA: 0x00055254 File Offset: 0x00053454
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_ViewActiveEvents}View Active Events", Order = 18, RequireRestart = false, HintText = "{=AIInfluence_ViewActiveEvents_Hint}Display all currently active dynamic events in game log")]
		public bool ViewActiveEvents
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = -1970075964;
						for (;;)
						{
							int num2 = num;
							switch (-(-num2 - 474208042 - ~1642457792) % 4)
							{
							case 0:
								goto IL_63;
							case 1:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_63;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-684819789), value);
								num = -1094107989;
								continue;
							}
							case 3:
								goto IL_06;
							}
							goto Block_1;
							IL_63:
							num = -1760502235;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700009A RID: 154
		// (get) Token: 0x06000309 RID: 777 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600030A RID: 778 RVA: 0x000552CC File Offset: 0x000534CC
		[SettingPropertyGroup("{=AIInfluence_Group_DynamicEvents}Dynamic Events", GroupOrder = 8)]
		[SettingPropertyBool("{=AIInfluence_ClearAllDynamicEvents}Clear All Dynamic Events", Order = 19, RequireRestart = false, HintText = "{=AIInfluence_ClearAllDynamicEvents_Hint}Removes all active dynamic events. This action cannot be undone!")]
		public bool ClearAllDynamicEvents
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_09:
						int num = 695356983;
						for (;;)
						{
							int num2 = num;
							switch (~((num2 ^ -147901509 * (665640178 - -231286888 ^ (-154975816 ^ 1965383652)) ^ 1771337825) + 1816125133 * -157331738) % 4)
							{
							case 1:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_88;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(15814017), value);
								num = 940501261;
								continue;
							}
							case 2:
								goto IL_09;
							case 3:
								goto IL_88;
							}
							goto Block_1;
							IL_88:
							num = 547699710;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700009B RID: 155
		// (get) Token: 0x0600030B RID: 779 RVA: 0x0005536C File Offset: 0x0005356C
		// (set) Token: 0x0600030C RID: 780 RVA: 0x00055380 File Offset: 0x00053580
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyBool("{=AIInfluence_EnableDiplomacy}Enable Diplomacy System", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_EnableDiplomacy_Hint}Enables AI-driven diplomacy system. When enabled, vanilla diplomacy (war declarations, peace treaties) will be disabled and replaced with AI-controlled diplomatic situations. Requires Dynamic Events to be enabled and not in dialogue-only mode.")]
		public bool EnableDiplomacy
		{
			get
			{
				return this._enableDiplomacy;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableDiplomacy != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 1118381703;
						for (;;)
						{
							int num2 = num;
							uint num3;
							bool flag2;
							switch ((num3 = (uint)((1345164858 ^ 1208354254) - (num2 * 257858183 ^ ~(-745383839 + -227956376)))) % 12U)
							{
							case 0U:
							{
								string text = \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(\u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-339855918), null));
								num = -1320722627;
								continue;
							}
							case 1U:
							{
								string text = \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(\u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(795241704), null));
								num = -1320722627;
								continue;
							}
							case 2U:
							{
								this._enableDiplomacy = false;
								string text = "";
								num = (int)(((!this.EnableDynamicEvents) ? 4056947032U : 672263382U) ^ num3 * 2174921649U);
								continue;
							}
							case 3U:
							{
								this._enableDiplomacy = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_22A;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1541662403), value);
								num = 1860625088;
								continue;
							}
							case 6U:
								goto IL_22A;
							case 7U:
							{
								string text;
								TextObject textObject = \u206C\u200E\u200F\u206D\u200F\u206B\u200F\u202B\u200D\u202A\u206D\u202A\u202B\u206C\u200C\u200B\u200C\u206E\u200C\u200F\u200D\u206A\u202E\u206F\u206A\u206B\u206C\u200E\u202D\u206C\u206F\u200E\u200C\u202A\u202A\u200E\u206B\u200C\u206D\u202C\u202E.\u00BE\u00A4\u00A0(\u00B1(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1871843438), text), null);
								\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.Â^\u0084\u0005ð(textObject), Colors.Yellow));
								Action<string, object> onSettingChanged2 = this.OnSettingChanged;
								if (onSettingChanged2 != null)
								{
									onSettingChanged2(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1541662403), false);
									num = -1385678133;
									continue;
								}
								break;
							}
							case 8U:
								flag2 = !this.CanEnableDiplomacy();
								goto IL_1C1;
							case 9U:
								if (value)
								{
									num = (int)(num3 * 1374211355U ^ 2647250081U);
									continue;
								}
								flag2 = false;
								goto IL_1C1;
							case 10U:
							{
								bool dynamicEventsDialogueOnly = this.DynamicEventsDialogueOnly;
								num = ((!dynamicEventsDialogueOnly) ? -1320722627 : 1443516347);
								continue;
							}
							case 11U:
								goto IL_14;
							}
							goto Block_1;
							IL_1C1:
							num = (flag2 ? -301657688 : -1758906427);
							continue;
							IL_22A:
							num = 776659586;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x0600030D RID: 781 RVA: 0x000555C4 File Offset: 0x000537C4
		public bool CanEnableDiplomacy()
		{
			bool flag;
			if (!this.EnableDynamicEvents)
			{
				flag = false;
				goto IL_35;
			}
			IL_09:
			int num = 2099995682;
			IL_0E:
			int num2 = num;
			switch (~(~num2) % 3)
			{
			case 0:
				goto IL_09;
			case 2:
				flag = !this.DynamicEventsDialogueOnly;
				goto IL_35;
			}
			bool result;
			return result;
			IL_35:
			result = flag;
			num = 2076355291;
			goto IL_0E;
		}

		// Token: 0x1700009C RID: 156
		// (get) Token: 0x0600030E RID: 782 RVA: 0x00055610 File Offset: 0x00053810
		// (set) Token: 0x0600030F RID: 783 RVA: 0x00055624 File Offset: 0x00053824
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyDropdown("{=AIInfluence_DiplomacyAIBackend}AI Backend for Diplomacy", RequireRestart = false, Order = 1, HintText = "{=AIInfluence_DiplomacyAIBackend_Hint}Select which AI backend to use for diplomatic statement generation. Can be different from dialogue AI.")]
		public Dropdown<string> DiplomacyAIBackend { get; set; } = new Dropdown<string>(new List<string>
		{
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1093806124),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1338752304),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(502993515),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(520863513),
			<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-2120288909)
		}, 1);

		// Token: 0x1700009D RID: 157
		// (get) Token: 0x06000310 RID: 784 RVA: 0x00055638 File Offset: 0x00053838
		// (set) Token: 0x06000311 RID: 785 RVA: 0x0005564C File Offset: 0x0005384C
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyBool("{=AIInfluence_StartInPeace}Start Campaign in Peace", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_StartInPeace_Hint}When enabled, all kingdoms start the campaign at peace with each other. Wars will only be declared through the AI diplomacy system.")]
		public bool StartInPeace
		{
			get
			{
				return this._startInPeace;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._startInPeace != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 103136777;
						for (;;)
						{
							int num2 = num;
							switch ((1611214463 - (-num2 - (351615877 - (834016569 ^ -555232330)) ^ 1526846254 + -800930581)) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_8E;
							case 3:
							{
								this._startInPeace = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8E;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1681807501), value);
								num = -1105339369;
								continue;
							}
							}
							goto Block_1;
							IL_8E:
							num = 2044476422;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700009E RID: 158
		// (get) Token: 0x06000312 RID: 786 RVA: 0x000556F0 File Offset: 0x000538F0
		// (set) Token: 0x06000313 RID: 787 RVA: 0x00055704 File Offset: 0x00053904
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_MaxParticipatingKingdoms}Maximum Participating Kingdoms", 2, 8, "0 Kingdoms", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_MaxParticipatingKingdoms_Hint}Maximum number of kingdoms that can participate in a single diplomatic event (excluding player kingdom). Default: 4.")]
		public int MaxParticipatingKingdoms
		{
			get
			{
				return this._maxParticipatingKingdoms;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxParticipatingKingdoms != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 129353702;
						for (;;)
						{
							int num2 = num;
							switch (-(-num2 * -491866407 - (-1645951121 + -461976131)) % 4)
							{
							case 0:
								goto IL_11;
							case 1:
								goto IL_7A;
							case 2:
							{
								this._maxParticipatingKingdoms = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7A;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1980549765), value);
								num = 595717557;
								continue;
							}
							}
							goto Block_1;
							IL_7A:
							num = -766022585;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x1700009F RID: 159
		// (get) Token: 0x06000314 RID: 788 RVA: 0x00055794 File Offset: 0x00053994
		// (set) Token: 0x06000315 RID: 789 RVA: 0x000557A8 File Offset: 0x000539A8
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_StatementGenerationInterval}Statement Generation Interval (Days)", 1, 10, "0 Days", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_StatementGenerationInterval_Hint}Maximum days between kingdom statement generations in diplomatic events. Actual interval is random from 1 to this value. Default: 3 days.")]
		public int StatementGenerationIntervalDays
		{
			get
			{
				return this._statementGenerationIntervalDays;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._statementGenerationIntervalDays != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 1002165858;
						for (;;)
						{
							int num2 = num;
							switch (~(~(-num2) - (-1254528249 + -449767440)) % 4)
							{
							case 0:
								goto IL_75;
							case 1:
							{
								this._statementGenerationIntervalDays = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_75;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(17998664), value);
								num = 1905086727;
								continue;
							}
							case 3:
								goto IL_11;
							}
							goto Block_1;
							IL_75:
							num = 453153401;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A0 RID: 160
		// (get) Token: 0x06000316 RID: 790 RVA: 0x00055834 File Offset: 0x00053A34
		// (set) Token: 0x06000317 RID: 791 RVA: 0x00055848 File Offset: 0x00053A48
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_DiplomacyMinLength}Minimum Statement Length", 50, 1000, "0 Characters", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_DiplomacyMinLength_Hint}Minimum characters for diplomatic statements. Default: 150.")]
		public int DiplomacyMinStatementLength
		{
			get
			{
				return this._diplomacyMinStatementLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._diplomacyMinStatementLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -33279349;
						for (;;)
						{
							int num2 = num;
							switch ((-(329761878 ^ -1749290996) - num2 * -480771169) % 4)
							{
							case 0:
								goto IL_79;
							case 1:
							{
								this._diplomacyMinStatementLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_79;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1385514472), value);
								num = -1645798498;
								continue;
							}
							case 2:
								goto IL_11;
							}
							goto Block_1;
							IL_79:
							num = -1129274187;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A1 RID: 161
		// (get) Token: 0x06000318 RID: 792 RVA: 0x000558D8 File Offset: 0x00053AD8
		// (set) Token: 0x06000319 RID: 793 RVA: 0x000558EC File Offset: 0x00053AEC
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_DiplomacyMaxLength}Maximum Statement Length", 100, 1000, "0 Characters", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_DiplomacyMaxLength_Hint}Maximum characters for diplomatic statements. Default: 600.")]
		public int DiplomacyMaxStatementLength
		{
			get
			{
				return this._diplomacyMaxStatementLength;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._diplomacyMaxStatementLength != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = -1587864417;
						for (;;)
						{
							int num2 = num;
							switch (-(~(num2 - ~(-(-707329664 * -1453996977)))) % 4)
							{
							case 1:
							{
								this._diplomacyMaxStatementLength = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_76;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1824525171), value);
								num = 39574837;
								continue;
							}
							case 2:
								goto IL_11;
							case 3:
								goto IL_76;
							}
							goto Block_1;
							IL_76:
							num = -621280918;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A2 RID: 162
		// (get) Token: 0x0600031A RID: 794 RVA: 0x00055978 File Offset: 0x00053B78
		// (set) Token: 0x0600031B RID: 795 RVA: 0x0005598C File Offset: 0x00053B8C
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_MinRelationChange}Minimum Kingdom Relation Change", -20, 0, "0", Order = 7, RequireRestart = false, HintText = "{=AIInfluence_MinRelationChange_Hint}Minimum relation change between kingdom leaders during diplomatic events. Negative values decrease relations. Default: -10.")]
		public int MinKingdomRelationChange
		{
			get
			{
				return this._minKingdomRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._minKingdomRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 995547939;
						for (;;)
						{
							int num2 = num;
							switch ((num2 * 1393634135 ^ 2102274162 + (1678309456 - -220389473) ^ -106010510 + -801083521) * -2145531305 % 4)
							{
							case 0:
								goto IL_93;
							case 1:
							{
								this._minKingdomRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_93;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1326469502), value);
								num = -217743446;
								continue;
							}
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_93:
							num = 1800251200;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A3 RID: 163
		// (get) Token: 0x0600031C RID: 796 RVA: 0x00055A38 File Offset: 0x00053C38
		// (set) Token: 0x0600031D RID: 797 RVA: 0x00055A4C File Offset: 0x00053C4C
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyInteger("{=AIInfluence_MaxRelationChange}Maximum Kingdom Relation Change", 0, 20, "0", Order = 8, RequireRestart = false, HintText = "{=AIInfluence_MaxRelationChange_Hint}Maximum relation change between kingdom leaders during diplomatic events. Positive values increase relations. Default: 10.")]
		public int MaxKingdomRelationChange
		{
			get
			{
				return this._maxKingdomRelationChange;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._maxKingdomRelationChange != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = -494680701;
						for (;;)
						{
							int num2 = num;
							switch ((1608985658 - ~(num2 + -(1814504515 * -29576102 + (-455509781 ^ -636588105)) ^ -992454322 - (33424815 - -433656864))) % 4)
							{
							case 0:
								goto IL_9E;
							case 1:
							{
								this._maxKingdomRelationChange = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_9E;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1534441581), value);
								num = 642103784;
								continue;
							}
							case 3:
								goto IL_14;
							}
							goto Block_1;
							IL_9E:
							num = 1982852070;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A4 RID: 164
		// (get) Token: 0x0600031E RID: 798 RVA: 0x00055B04 File Offset: 0x00053D04
		// (set) Token: 0x0600031F RID: 799 RVA: 0x00055B18 File Offset: 0x00053D18
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatiguePerTroop}Fatigue per Troop Lost", 0.001f, 0.1f, "0.000", Order = 9, RequireRestart = false, HintText = "{=AIInfluence_FatiguePerTroop_Hint}Fatigue points added per troop casualty. With 1000 troops lost and value 0.005, adds 5 points. Default: 0.005")]
		public float FatiguePerTroopLost
		{
			get
			{
				return this._fatiguePerTroopLost;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatiguePerTroopLost - value) > 0.0001f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1666893036;
						for (;;)
						{
							int num2 = num;
							switch (-(~((num2 + -1732934795 * 1248479126) * 1022346649)) % 4)
							{
							case 0:
								goto IL_1E;
							case 2:
								goto IL_87;
							case 3:
							{
								this._fatiguePerTroopLost = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_87;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2065364831), value);
								num = -1775557305;
								continue;
							}
							}
							goto Block_1;
							IL_87:
							num = 1240138658;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A5 RID: 165
		// (get) Token: 0x06000320 RID: 800 RVA: 0x00055BB4 File Offset: 0x00053DB4
		// (set) Token: 0x06000321 RID: 801 RVA: 0x00055BC8 File Offset: 0x00053DC8
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatiguePerLordKilled}Fatigue per Lord Killed", 0.5f, 10f, "0.0", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_FatiguePerLordKilled_Hint}Fatigue points added when a lord is killed in war. Default: 5.0")]
		public float FatiguePerLordKilled
		{
			get
			{
				return this._fatiguePerLordKilled;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatiguePerLordKilled - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = 951581377;
						for (;;)
						{
							int num2 = num;
							switch (((-746100391 + -1584961765 + --1099221058 - ~num2) * -730585043 + 1722468313) % 4)
							{
							case 0:
								goto IL_1E;
							case 1:
							{
								this._fatiguePerLordKilled = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_93;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1979301450), value);
								num = 2034323366;
								continue;
							}
							case 2:
								goto IL_93;
							}
							goto Block_1;
							IL_93:
							num = 42059231;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A6 RID: 166
		// (get) Token: 0x06000322 RID: 802 RVA: 0x00055C70 File Offset: 0x00053E70
		// (set) Token: 0x06000323 RID: 803 RVA: 0x00055C84 File Offset: 0x00053E84
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatiguePerLordCaptured}Fatigue per Lord Captured", 0.5f, 10f, "0.0", Order = 11, RequireRestart = false, HintText = "{=AIInfluence_FatiguePerLordCaptured_Hint}Fatigue points added when a lord is captured by enemy. Default: 3.0")]
		public float FatiguePerLordCaptured
		{
			get
			{
				return this._fatiguePerLordCaptured;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatiguePerLordCaptured - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = 1974234414;
						for (;;)
						{
							int num2 = num;
							switch ((-1649807585 * (-12307872 * -1886079885) - (num2 ^ (-33519887 * -1326450391 ^ -1663045253 * 1849483581) + (697575038 + -660648811 + -713014686)) - (-834229972 + 837334226)) % 4)
							{
							case 1:
							{
								this._fatiguePerLordCaptured = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_C2;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-105058724), value);
								num = 660223784;
								continue;
							}
							case 2:
								goto IL_21;
							case 3:
								goto IL_C2;
							}
							goto Block_1;
							IL_C2:
							num = 1688614913;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A7 RID: 167
		// (get) Token: 0x06000324 RID: 804 RVA: 0x00055D60 File Offset: 0x00053F60
		// (set) Token: 0x06000325 RID: 805 RVA: 0x00055D74 File Offset: 0x00053F74
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatiguePerSettlement}Fatigue per Settlement Lost", 1f, 20f, "0.0", Order = 12, RequireRestart = false, HintText = "{=AIInfluence_FatiguePerSettlement_Hint}Fatigue points added when a settlement is captured by enemy. Default: 10.0")]
		public float FatiguePerSettlementLost
		{
			get
			{
				return this._fatiguePerSettlementLost;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatiguePerSettlementLost - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = -797499186;
						for (;;)
						{
							int num2 = num;
							switch (~(((-1589966033 * (-1608817173 ^ -1705991689) ^ -(-863340958 ^ 1284376845)) - num2 - (1172793970 + 1348998531)) * 788574061) % 4)
							{
							case 0:
								goto IL_21;
							case 1:
							{
								this._fatiguePerSettlementLost = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_AB;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-166162743), value);
								num = -1005210248;
								continue;
							}
							case 3:
								goto IL_AB;
							}
							goto Block_1;
							IL_AB:
							num = -1958382693;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A8 RID: 168
		// (get) Token: 0x06000326 RID: 806 RVA: 0x00055E38 File Offset: 0x00054038
		// (set) Token: 0x06000327 RID: 807 RVA: 0x00055E4C File Offset: 0x0005404C
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatiguePerCaravan}Fatigue per Caravan Destroyed", 0.5f, 10f, "0.0", Order = 13, RequireRestart = false, HintText = "{=AIInfluence_FatiguePerCaravan_Hint}Fatigue points added when a caravan is destroyed by enemy. Default: 2.0")]
		public float FatiguePerCaravanDestroyed
		{
			get
			{
				return this._fatiguePerCaravanDestroyed;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatiguePerCaravanDestroyed - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = 1052842692;
						for (;;)
						{
							int num2 = num;
							switch (-(-(~(num2 + (-1934226441 * 469216507 - -107520471) * 365192513))) % 4)
							{
							case 0:
								goto IL_1E;
							case 1:
								goto IL_8E;
							case 3:
							{
								this._fatiguePerCaravanDestroyed = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8E;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-2031739865), value);
								num = -1560180826;
								continue;
							}
							}
							goto Block_1;
							IL_8E:
							num = 1737885421;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000A9 RID: 169
		// (get) Token: 0x06000328 RID: 808 RVA: 0x00055EF0 File Offset: 0x000540F0
		// (set) Token: 0x06000329 RID: 809 RVA: 0x00055F04 File Offset: 0x00054104
		[SettingPropertyGroup("{=AIInfluence_Group_Diplomacy}Diplomacy", GroupOrder = 9)]
		[SettingPropertyFloatingInteger("{=AIInfluence_FatigueRecoveryPerDay}Fatigue Recovery per Day (at Peace)", 1f, 10f, "0.0", Order = 14, RequireRestart = false, HintText = "{=AIInfluence_FatigueRecoveryPerDay_Hint}Fatigue points recovered each day when kingdom is at peace with all factions. Default: 5.0")]
		public float FatigueRecoveryPerDay
		{
			get
			{
				return this._fatigueRecoveryPerDay;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._fatigueRecoveryPerDay - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1359933723;
						for (;;)
						{
							int num2 = num;
							switch (((~num2 - -(~-1377628195)) * -1209522825 ^ 1367891065) % 4)
							{
							case 0:
								goto IL_88;
							case 1:
							{
								this._fatigueRecoveryPerDay = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_88;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1622810928), value);
								num = 477370750;
								continue;
							}
							case 2:
								goto IL_1E;
							}
							goto Block_1;
							IL_88:
							num = -57799061;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AA RID: 170
		// (get) Token: 0x0600032A RID: 810 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600032B RID: 811 RVA: 0x00055FA4 File Offset: 0x000541A4
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_ClearAllNPCs}Clear Current Campaign NPC Data", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_ClearAllNPCs_Hint}WARNING: This will permanently delete all NPC conversation data and contexts for the CURRENT CAMPAIGN only. Other campaigns will not be affected. This action cannot be undone! Set to true and save settings to execute.")]
		public bool ClearAllNPCs
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = -672290594;
						for (;;)
						{
							int num2 = num;
							switch (-(~(num2 + -(~781258234 + (-813382333 ^ 1506032474))) * 1107052601) % 4)
							{
							case 0:
								goto IL_06;
							case 1:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_70;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1564336775), value);
								num = 1424851328;
								continue;
							}
							case 3:
								goto IL_70;
							}
							goto Block_1;
							IL_70:
							num = -1336658069;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AB RID: 171
		// (get) Token: 0x0600032C RID: 812 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600032D RID: 813 RVA: 0x0005602C File Offset: 0x0005422C
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_ResetUserEdits}Reset User-Edited Knowledge", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_ResetUserEdits_Hint}Resets all user-edited KnownSecrets and KnownInfo flags for the current campaign, allowing the system to auto-generate them again. This will not delete existing knowledge, but will allow it to be modified by the system.")]
		public bool ResetUserEditedKnowledge
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = 1515641933;
						for (;;)
						{
							int num2 = num;
							switch ((-(num2 * 1168708169) + --782000514) % 4)
							{
							case 1:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_62;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1755597343), value);
								num = -684869140;
								continue;
							}
							case 2:
								goto IL_62;
							case 3:
								goto IL_06;
							}
							goto Block_1;
							IL_62:
							num = 39353066;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AC RID: 172
		// (get) Token: 0x0600032E RID: 814 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x0600032F RID: 815 RVA: 0x000560A4 File Offset: 0x000542A4
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_ClearAllNPCEvents}Clear All NPC Events", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_ClearAllNPCEvents_Hint}Clears all events (RecentEvents) for all NPCs in the current campaign. This will remove all battle, marriage, death, and other world events from NPC memory. This action cannot be undone!")]
		public bool ClearAllNPCEvents
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = 1411937827;
						for (;;)
						{
							int num2 = num;
							switch (~(~((num2 ^ ~(-60529003 * -278227151)) * 280204277)) % 4)
							{
							case 0:
								goto IL_69;
							case 1:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_69;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-761045559), value);
								num = -1941255830;
								continue;
							}
							case 3:
								goto IL_06;
							}
							goto Block_1;
							IL_69:
							num = -98228520;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AD RID: 173
		// (get) Token: 0x06000330 RID: 816 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000331 RID: 817 RVA: 0x00056124 File Offset: 0x00054324
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_LoadAllNPCs}Load All NPCs Into Memory", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_LoadAllNPCs_Hint}Loads all NPC contexts from JSON files into game memory. This ensures all NPCs can receive world events. Use this button after loading a save game to activate all NPCs for event processing.")]
		public bool LoadAllNPCs
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = -971598343;
						for (;;)
						{
							int num2 = num;
							switch ((-(~((1214022445 * ~-706675372 ^ (-878522959 - -762046727 ^ -325435481)) - num2)) - 867538813) % 4)
							{
							case 0:
								goto IL_06;
							case 1:
								goto IL_7B;
							case 3:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7B;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-889329598), value);
								num = -1220151933;
								continue;
							}
							}
							goto Block_1;
							IL_7B:
							num = -1454164178;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AE RID: 174
		// (get) Token: 0x06000332 RID: 818 RVA: 0x00046720 File Offset: 0x00044920
		// (set) Token: 0x06000333 RID: 819 RVA: 0x000561B4 File Offset: 0x000543B4
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_EraseSpecificNPC}Erase Specific NPC", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_EraseSpecificNPC_Hint}Opens a window to select and permanently delete a specific NPC's data from memory and saved files. This action cannot be undone!")]
		public bool EraseSpecificNPC
		{
			get
			{
				return false;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				if (value)
				{
					for (;;)
					{
						IL_06:
						int num = 383005755;
						for (;;)
						{
							int num2 = num;
							switch (((~(-num2) ^ ~1285484150) + -2130649389) % 4)
							{
							case 0:
								goto IL_06;
							case 1:
								goto IL_63;
							case 2:
							{
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_63;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1054996288), value);
								num = 550169344;
								continue;
							}
							}
							goto Block_1;
							IL_63:
							num = 1837612550;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000AF RID: 175
		// (get) Token: 0x06000334 RID: 820 RVA: 0x0005622C File Offset: 0x0005442C
		// (set) Token: 0x06000335 RID: 821 RVA: 0x00056240 File Offset: 0x00054440
		[SettingPropertyGroup("{=AIInfluence_Group_NPCManagement}NPC Management", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_EnableDeadNPCCleanup}Enable Dead NPC Cleanup", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_EnableDeadNPCCleanup_Hint}Automatically removes dead NPC data from memory and deletes their JSON files. When disabled, dead NPC data will be preserved. This helps keep the system clean and prevents memory bloat.")]
		public bool EnableDeadNPCCleanup
		{
			get
			{
				return this._enableDeadNPCCleanup;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableDeadNPCCleanup != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 112518320;
						for (;;)
						{
							int num2 = num;
							switch (-(num2 - ~(-(47622513 + -323824477)) - (--1271470296 - (-769469815 - 49075300)) ^ (1355197887 ^ 341184496)) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
								goto IL_97;
							case 3:
							{
								this._enableDeadNPCCleanup = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_97;
								}
								onSettingChanged(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1431719535), value);
								num = 1359297002;
								continue;
							}
							}
							goto Block_1;
							IL_97:
							num = 1213579787;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x06000336 RID: 822 RVA: 0x000562F0 File Offset: 0x000544F0
		[MethodImpl(MethodImplOptions.NoInlining)]
		[return: TupleElementNames(new string[]
		{
			"enabled",
			"chance"
		})]
		public ValueTuple<bool, int> GetEventTypeConfig(string eventType)
		{
			string text = \u202A\u200F\u200E\u200F\u202E\u202B\u200B\u206F\u206E\u206D\u200D\u202A\u202A\u200E\u206F\u206A\u200C\u206D\u202B\u200E\u202B\u200C\u202E\u200B\u200B\u200E\u206E\u202D\u206F\u206F\u202D\u206A\u200B\u202A\u200B\u202B\u202D\u202B\u200C\u206E\u202E.\u000FØóCæ(eventType);
			if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-580991166)))
			{
				goto IL_29;
			}
			goto IL_127;
			int num2;
			ValueTuple<bool, int> valueTuple;
			ValueTuple<bool, int> result;
			for (;;)
			{
				IL_2E:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)((num - ~(117866319 + 325970142 ^ 563047706 * -1609619613) ^ --1984269409) * -1532367851))) % 15U)
				{
				case 0U:
					goto IL_127;
				case 1U:
					num2 = (int)(num3 * 3402672689U ^ 213582174U);
					continue;
				case 2U:
					valueTuple = new ValueTuple<bool, int>(false, 0);
					num2 = 508084136;
					continue;
				case 3U:
					valueTuple = new ValueTuple<bool, int>(this.EnableEconomicEvents, this.EconomicEventsChance);
					num2 = 508084136;
					continue;
				case 4U:
					num2 = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1640452636))) ? -157755784 : 1618420702);
					continue;
				case 5U:
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1449982906)) ? 1518354374 : 265432797);
					continue;
				case 6U:
					goto IL_29;
				case 8U:
					valueTuple = new ValueTuple<bool, int>(this.EnableMysteriousEvents, this.MysteriousEventsChance);
					num2 = -794611576;
					continue;
				case 9U:
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(221357250)) ? -1938847761 : 51697238);
					continue;
				case 10U:
					result = valueTuple;
					num2 = -1341763354;
					continue;
				case 11U:
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-328807425)) ? 990180720 : -2006683652);
					continue;
				case 12U:
					valueTuple = new ValueTuple<bool, int>(this.EnablePoliticalEvents, this.PoliticalEventsChance);
					num2 = 508084136;
					continue;
				case 13U:
					valueTuple = new ValueTuple<bool, int>(this.EnableSocialEvents, this.SocialEventsChance);
					num2 = 508084136;
					continue;
				case 14U:
					num2 = (int)(num3 * 3301455578U ^ 4219337860U);
					continue;
				}
				break;
			}
			return result;
			IL_29:
			num2 = -504434287;
			goto IL_2E;
			IL_127:
			valueTuple = new ValueTuple<bool, int>(this.EnableMilitaryEvents, this.MilitaryEventsChance);
			num2 = 508084136;
			goto IL_2E;
		}

		// Token: 0x06000337 RID: 823 RVA: 0x00056524 File Offset: 0x00054724
		[MethodImpl(MethodImplOptions.NoInlining)]
		public Dictionary<string, int> GetEnabledEventTypes()
		{
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			if (this.EnableMilitaryEvents)
			{
				goto IL_12;
			}
			bool flag = false;
			goto IL_95;
			int num2;
			Dictionary<string, int> result;
			for (;;)
			{
				IL_17:
				int num = num2;
				bool flag2;
				bool flag3;
				bool flag4;
				bool flag5;
				switch ((~num - (~-1721442253 + (-867794028 - -1965097889))) * 1166410025 % 17)
				{
				case 0:
					flag2 = (this.PoliticalEventsChance > 0);
					goto IL_18B;
				case 1:
					dictionary[<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1091672308)] = this.MysteriousEventsChance;
					num2 = -514722062;
					continue;
				case 2:
					flag3 = (this.MysteriousEventsChance > 0);
					goto IL_231;
				case 3:
					if (this.EnableMysteriousEvents)
					{
						num2 = -2091916681;
						continue;
					}
					flag3 = false;
					goto IL_231;
				case 4:
					result = dictionary;
					num2 = 653656229;
					continue;
				case 5:
					goto IL_89;
				case 6:
					if (this.EnablePoliticalEvents)
					{
						num2 = -129583127;
						continue;
					}
					flag2 = false;
					goto IL_18B;
				case 7:
					dictionary[<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1816307365)] = this.EconomicEventsChance;
					num2 = 54275831;
					continue;
				case 8:
					flag4 = (this.EconomicEventsChance > 0);
					goto IL_15A;
				case 9:
					dictionary[<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-580991166)] = this.MilitaryEventsChance;
					num2 = -232372201;
					continue;
				case 11:
					if (this.EnableEconomicEvents)
					{
						num2 = -1480597324;
						continue;
					}
					flag4 = false;
					goto IL_15A;
				case 12:
					dictionary[<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1999281069)] = this.SocialEventsChance;
					num2 = 2054266084;
					continue;
				case 13:
					goto IL_12;
				case 14:
					flag5 = (this.SocialEventsChance > 0);
					goto IL_1AF;
				case 15:
					if (this.EnableSocialEvents)
					{
						num2 = 1914414726;
						continue;
					}
					flag5 = false;
					goto IL_1AF;
				case 16:
					dictionary[<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(221357250)] = this.PoliticalEventsChance;
					num2 = -704413859;
					continue;
				}
				break;
				IL_15A:
				bool flag6 = flag4;
				num2 = ((!flag6) ? 54275831 : -2071167473);
				continue;
				IL_18B:
				num2 = (flag2 ? -1620973195 : -704413859);
				continue;
				IL_1AF:
				num2 = (flag5 ? -918635847 : 2054266084);
				continue;
				IL_231:
				bool flag7 = flag3;
				num2 = ((!flag7) ? -514722062 : 1811362515);
			}
			return result;
			IL_89:
			flag = (this.MilitaryEventsChance > 0);
			goto IL_95;
			IL_12:
			num2 = -1087168526;
			goto IL_17;
			IL_95:
			bool flag8 = flag;
			num2 = ((!flag8) ? -232372201 : 852683863);
			goto IL_17;
		}

		// Token: 0x06000338 RID: 824 RVA: 0x00056780 File Offset: 0x00054980
		public bool HasEnabledEventTypes()
		{
			return Enumerable.Any<KeyValuePair<string, int>>(this.GetEnabledEventTypes());
		}

		// Token: 0x170000B0 RID: 176
		// (get) Token: 0x06000339 RID: 825 RVA: 0x000567A0 File Offset: 0x000549A0
		// (set) Token: 0x0600033A RID: 826 RVA: 0x000567B4 File Offset: 0x000549B4
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_EnableNPCInitiative}Enable NPC Initiative System", Order = 0, RequireRestart = false, HintText = "{=AIInfluence_EnableNPCInitiative_Hint}Allows NPCs to initiate conversations with the player. NPCs in your party will ask to talk, while others will send messengers.")]
		public bool EnableNPCInitiative
		{
			get
			{
				return this._enableNPCInitiative;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableNPCInitiative != value;
				if (flag)
				{
					for (;;)
					{
						IL_11:
						int num = 168581243;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-(uint)(~(num2 + --900358306) - -533857824))) % 5U)
							{
							case 1U:
							{
								this._enableNPCInitiative = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_78;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1782682399), value);
								num = 1373096950;
								continue;
							}
							case 2U:
								goto IL_11;
							case 3U:
								num = (int)(num3 * 3388672498U ^ 3485580406U);
								continue;
							case 4U:
								goto IL_78;
							}
							goto Block_1;
							IL_78:
							num = 198026567;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B1 RID: 177
		// (get) Token: 0x0600033B RID: 827 RVA: 0x00056854 File Offset: 0x00054A54
		// (set) Token: 0x0600033C RID: 828 RVA: 0x00056868 File Offset: 0x00054A68
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeBaseChance}Base Daily Initiative Chance (%)", 0f, 50f, "#0.0", Order = 1, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeBaseChance_Hint}Base chance for an NPC to initiate conversation each day (0-50%). Default: 5%")]
		public float NPCInitiativeBaseChance
		{
			get
			{
				return this._npcInitiativeBaseChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeBaseChance - value) > 0.01f;
				for (;;)
				{
					IL_1B:
					int num = 368552690;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(((-583589365 ^ --835440318) * 2004324235 - num2 + (-1551870683 - 781853656 ^ ~-485880846)) * 2121271725 ^ 306785553)) % 5U)
						{
						case 0U:
							goto IL_1B;
						case 1U:
							num = (int)(((!flag) ? 20828589U : 2753147362U) ^ num3 * 401591685U);
							continue;
						case 2U:
							goto IL_73;
						case 3U:
						{
							this._npcInitiativeBaseChance = value;
							Action<string, object> onSettingChanged = this.OnSettingChanged;
							if (onSettingChanged == null)
							{
								goto IL_73;
							}
							onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-232319148), value);
							num = 671523379;
							continue;
						}
						}
						return;
						IL_73:
						num = 1664025997;
					}
				}
			}
		}

		// Token: 0x170000B2 RID: 178
		// (get) Token: 0x0600033D RID: 829 RVA: 0x00056948 File Offset: 0x00054B48
		// (set) Token: 0x0600033E RID: 830 RVA: 0x0005695C File Offset: 0x00054B5C
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeFriendlyBonus}Friendly Relation Bonus (%)", 0f, 30f, "#0.0", Order = 2, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeFriendlyBonus_Hint}Additional chance for NPCs with good relations (20+). Default: 10%")]
		public float NPCInitiativeFriendlyBonus
		{
			get
			{
				return this._npcInitiativeFriendlyBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeFriendlyBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = 2087615329;
						for (;;)
						{
							int num2 = num;
							switch (num2 * -646226807 * -301699009 % 4)
							{
							case 0:
								goto IL_1E;
							case 2:
								goto IL_7F;
							case 3:
							{
								this._npcInitiativeFriendlyBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7F;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1907211132), value);
								num = 1283397258;
								continue;
							}
							}
							goto Block_1;
							IL_7F:
							num = 516273703;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B3 RID: 179
		// (get) Token: 0x0600033F RID: 831 RVA: 0x000569F0 File Offset: 0x00054BF0
		// (set) Token: 0x06000340 RID: 832 RVA: 0x00056A04 File Offset: 0x00054C04
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeHostileBonus}Hostile Relation Bonus (%)", 0f, 20f, "#0.0", Order = 3, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeHostileBonus_Hint}Additional chance for NPCs with bad relations (-20 or worse). Default: 5%")]
		public float NPCInitiativeHostileBonus
		{
			get
			{
				return this._npcInitiativeHostileBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeHostileBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = -963202349;
						for (;;)
						{
							int num2 = num;
							switch (((num2 ^ ~786635021) + (270199557 - 755776065 - (1672214535 ^ -1219798788))) * 2053297733 % 4)
							{
							case 1:
								goto IL_9B;
							case 2:
							{
								this._npcInitiativeHostileBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_9B;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(812478732), value);
								num = 2075160926;
								continue;
							}
							case 3:
								goto IL_21;
							}
							goto Block_1;
							IL_9B:
							num = 309899773;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B4 RID: 180
		// (get) Token: 0x06000341 RID: 833 RVA: 0x00056AB4 File Offset: 0x00054CB4
		// (set) Token: 0x06000342 RID: 834 RVA: 0x00056AC8 File Offset: 0x00054CC8
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_EnableNPCMapInitiative}Enable Map Initiative", Order = 9, RequireRestart = false, HintText = "{=AIInfluence_EnableNPCMapInitiative_Hint}Allow lords on the global map to approach the player (friendly or hostile).")]
		public bool EnableNPCMapInitiative
		{
			get
			{
				return this._enableNPCMapInitiative;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = this._enableNPCMapInitiative != value;
				if (flag)
				{
					for (;;)
					{
						IL_14:
						int num = 2033467016;
						for (;;)
						{
							int num2 = num;
							switch ((num2 - (~(-1798224139 * 562732329) + (481111910 + -809991671 + (1321583220 - 1764254633))) ^ (1003457755 ^ -279956712) + (1512148653 - 960493313) ^ -129332929 + -1512615017 ^ -1449927932) % 4)
							{
							case 0:
								goto IL_14;
							case 1:
							{
								this._enableNPCMapInitiative = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_BB;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1526026164), value);
								num = 1551808263;
								continue;
							}
							case 2:
								goto IL_BB;
							}
							goto Block_1;
							IL_BB:
							num = -1227580750;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B5 RID: 181
		// (get) Token: 0x06000343 RID: 835 RVA: 0x00056B9C File Offset: 0x00054D9C
		// (set) Token: 0x06000344 RID: 836 RVA: 0x00056BB0 File Offset: 0x00054DB0
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyBool("{=AIInfluence_EnableHostileInitiative}Enable Hostile Initiative", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_EnableHostileInitiative_Hint}Allow hostile lords to initiate conversation when they catch the player on the map.")]
		public bool EnableHostileInitiative { get; set; } = true;

		// Token: 0x170000B6 RID: 182
		// (get) Token: 0x06000345 RID: 837 RVA: 0x00056BC4 File Offset: 0x00054DC4
		// (set) Token: 0x06000346 RID: 838 RVA: 0x00056BD8 File Offset: 0x00054DD8
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeMapBaseChance}Map Approach Base Chance (%)", 0f, 50f, "#0.0", Order = 10, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeMapBaseChance_Hint}Base chance for a visible lord on the map to approach the player (checked every 5 hours). Default: 2%")]
		public float NPCInitiativeMapBaseChance
		{
			get
			{
				return this._npcInitiativeMapBaseChance;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeMapBaseChance - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -1661476190;
						for (;;)
						{
							int num2 = num;
							switch (~(num2 * 1242377181 - -(-226184207 - 986832609)) * -1221545537 % 4)
							{
							case 0:
								goto IL_8D;
							case 2:
								goto IL_1E;
							case 3:
							{
								this._npcInitiativeMapBaseChance = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_8D;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(290709965), value);
								num = -1977393437;
								continue;
							}
							}
							goto Block_1;
							IL_8D:
							num = 1845984504;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B7 RID: 183
		// (get) Token: 0x06000347 RID: 839 RVA: 0x00056C7C File Offset: 0x00054E7C
		// (set) Token: 0x06000348 RID: 840 RVA: 0x00056C90 File Offset: 0x00054E90
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyInteger("{=AIInfluence_HostileInitiativeCooldown}Hostile Initiative Cooldown (Days)", 1, 30, "0 days", Order = 11, RequireRestart = false, HintText = "{=AIInfluence_HostileInitiativeCooldown_Hint}Number of days before ANY hostile lord can initiate conversation again after one has successfully caught the player.")]
		public int HostileInitiativeCooldown { get; set; } = 3;

		// Token: 0x170000B8 RID: 184
		// (get) Token: 0x06000349 RID: 841 RVA: 0x00056CA4 File Offset: 0x00054EA4
		// (set) Token: 0x0600034A RID: 842 RVA: 0x00056CB8 File Offset: 0x00054EB8
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeRomanceBonus}Romance Bonus (%)", 0f, 40f, "#0.0", Order = 4, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeRomanceBonus_Hint}Additional chance for NPCs with romance level 50+. Default: 20%")]
		public float NPCInitiativeRomanceBonus
		{
			get
			{
				return this._npcInitiativeRomanceBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeRomanceBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = 1314457872;
						for (;;)
						{
							int num2 = num;
							switch ((-(~(-num2)) - -1727500596) % 4)
							{
							case 0:
								goto IL_7C;
							case 1:
							{
								this._npcInitiativeRomanceBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_7C;
								}
								onSettingChanged(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-294065126), value);
								num = 1104003101;
								continue;
							}
							case 3:
								goto IL_1E;
							}
							goto Block_1;
							IL_7C:
							num = 278439359;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000B9 RID: 185
		// (get) Token: 0x0600034B RID: 843 RVA: 0x00056D4C File Offset: 0x00054F4C
		// (set) Token: 0x0600034C RID: 844 RVA: 0x00056D60 File Offset: 0x00054F60
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeFamiliarityBonus}Familiarity Bonus (%)", 0f, 20f, "#0.0", Order = 5, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeFamiliarityBonus_Hint}Additional chance for NPCs with 10+ interactions. Default: 5%")]
		public float NPCInitiativeFamiliarityBonus
		{
			get
			{
				return this._npcInitiativeFamiliarityBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeFamiliarityBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_1E:
						int num = -174684395;
						for (;;)
						{
							int num2 = num;
							switch ((~(num2 * -439149695 ^ -1696215368) ^ -1059999133) % 4)
							{
							case 0:
								goto IL_1E;
							case 1:
							{
								this._npcInitiativeFamiliarityBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_86;
								}
								onSettingChanged(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1829347502), value);
								num = -550230006;
								continue;
							}
							case 2:
								goto IL_86;
							}
							goto Block_1;
							IL_86:
							num = 540764811;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000BA RID: 186
		// (get) Token: 0x0600034D RID: 845 RVA: 0x00056DFC File Offset: 0x00054FFC
		// (set) Token: 0x0600034E RID: 846 RVA: 0x00056E10 File Offset: 0x00055010
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativePartyBonus}Party Member Bonus (%)", 0f, 30f, "#0.0", Order = 6, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativePartyBonus_Hint}Additional chance for NPCs in player's party. Default: 15%")]
		public float NPCInitiativePartyBonus
		{
			get
			{
				return this._npcInitiativePartyBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativePartyBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = -287119317;
						for (;;)
						{
							int num2 = num;
							switch (~((-499472297 ^ -930431899) - ((num2 ^ ~(233279983 - -80186823) + 88258530) - ((512683496 ^ -166975178) + (-914582693 - -444206206)))) % 4)
							{
							case 1:
								goto IL_B1;
							case 2:
							{
								this._npcInitiativePartyBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_B1;
								}
								onSettingChanged(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1610845996), value);
								num = -928898560;
								continue;
							}
							case 3:
								goto IL_21;
							}
							goto Block_1;
							IL_B1:
							num = -1017864139;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x170000BB RID: 187
		// (get) Token: 0x0600034F RID: 847 RVA: 0x00056EDC File Offset: 0x000550DC
		// (set) Token: 0x06000350 RID: 848 RVA: 0x00056EF0 File Offset: 0x000550F0
		[SettingPropertyGroup("{=AIInfluence_Group_NPCInitiative}NPC Initiative", GroupOrder = 10)]
		[SettingPropertyFloatingInteger("{=AIInfluence_NPCInitiativeLongTimeSinceContactBonus}Long Time Since Contact Bonus (%)", 0f, 25f, "#0.0", Order = 7, RequireRestart = false, HintText = "{=AIInfluence_NPCInitiativeLongTimeSinceContactBonus_Hint}Additional chance for NPCs you haven't talked to in 20+ days. Default: 10%")]
		public float NPCInitiativeLongTimeSinceContactBonus
		{
			get
			{
				return this._npcInitiativeLongTimeSinceContactBonus;
			}
			[MethodImpl(MethodImplOptions.NoInlining)]
			set
			{
				bool flag = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(this._npcInitiativeLongTimeSinceContactBonus - value) > 0.01f;
				if (flag)
				{
					for (;;)
					{
						IL_21:
						int num = -104907669;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(~(uint)(num2 - ((1850015585 + -1885925776) * 396089657 + -807265588)))) % 5U)
							{
							case 1U:
								num = (int)(num3 * 1967674428U ^ 529099354U);
								continue;
							case 2U:
							{
								this._npcInitiativeLongTimeSinceContactBonus = value;
								Action<string, object> onSettingChanged = this.OnSettingChanged;
								if (onSettingChanged == null)
								{
									goto IL_70;
								}
								onSettingChanged(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(888282783), value);
								num = -1868963677;
								continue;
							}
							case 3U:
								goto IL_21;
							case 4U:
								goto IL_70;
							}
							goto Block_1;
							IL_70:
							num = -6507748;
						}
					}
					Block_1:;
				}
			}
		}

		// Token: 0x06000351 RID: 849 RVA: 0x00056FAC File Offset: 0x000551AC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public ModSettings()
		{
		}

		// Token: 0x04000197 RID: 407
		private DateTime _lastForceGenerateEventTime = DateTime.MinValue;

		// Token: 0x04000198 RID: 408
		private bool _enableModification = true;

		// Token: 0x04000199 RID: 409
		private bool _enableNearbyNPCInitialization = true;

		// Token: 0x0400019A RID: 410
		private bool _enableDebugLogging = true;

		// Token: 0x0400019E RID: 414
		private Dropdown<string> _aiBackend = new Dropdown<string>(new List<string>
		{
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-409829707),
			<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1338752304),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(502993515),
			<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-375529797),
			<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(250367903)
		}, 2);

		// Token: 0x0400019F RID: 415
		private string _aiModel = <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1202020455);

		// Token: 0x040001A0 RID: 416
		private string _apiKey = "";

		// Token: 0x040001A1 RID: 417
		private string _deepSeekModel = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1052322384);

		// Token: 0x040001A2 RID: 418
		private string _deepSeekApiKey = "";

		// Token: 0x040001A8 RID: 424
		private bool _enableTTS = false;

		// Token: 0x040001A9 RID: 425
		private float _ttsSpeed = 1f;

		// Token: 0x040001AB RID: 427
		private int _promptMaxHistory = 100;

		// Token: 0x040001AC RID: 428
		private bool _promptIncludeEvents = true;

		// Token: 0x040001AD RID: 429
		private float _promptSensitiveTrust = 0.5f;

		// Token: 0x040001AE RID: 430
		private float _promptSecretTrust = 0.8f;

		// Token: 0x040001AF RID: 431
		private bool _promptIncludeQuirks = true;

		// Token: 0x040001B0 RID: 432
		private float _promptQuirksFrequency = 0.5f;

		// Token: 0x040001B1 RID: 433
		private bool _promptUseAsterisks = true;

		// Token: 0x040001B2 RID: 434
		private float _promptLieStrictness = 0.5f;

		// Token: 0x040001B3 RID: 435
		private int _promptMinResponseLength = 20;

		// Token: 0x040001B4 RID: 436
		private int _promptMaxResponseLength = 500;

		// Token: 0x040001B5 RID: 437
		private bool _promptIncludeFriends = true;

		// Token: 0x040001B6 RID: 438
		private int _promptMaxFriends = 5;

		// Token: 0x040001B7 RID: 439
		private int _promptMaxEnemies = 5;

		// Token: 0x040001B8 RID: 440
		private bool _useAdvancedTrust = false;

		// Token: 0x040001B9 RID: 441
		private float _interactionTrustBonus = 0.01f;

		// Token: 0x040001BA RID: 442
		private float _familiarityTrustBonus = 0.1f;

		// Token: 0x040001BB RID: 443
		private float _minLieTrustPenalty = 0.05f;

		// Token: 0x040001BC RID: 444
		private float _maxLieTrustPenalty = 0.1f;

		// Token: 0x040001BD RID: 445
		private int _minLieRelationPenalty = 1;

		// Token: 0x040001BE RID: 446
		private int _maxLieRelationPenalty = 5;

		// Token: 0x040001BF RID: 447
		private int _minPositiveRelationChange = 1;

		// Token: 0x040001C0 RID: 448
		private int _maxPositiveRelationChange = 5;

		// Token: 0x040001C1 RID: 449
		private int _minNegativeRelationChange = 1;

		// Token: 0x040001C2 RID: 450
		private int _maxNegativeRelationChange = 5;

		// Token: 0x040001C3 RID: 451
		private float _femaleNPCRomanceInitiative = 0.2f;

		// Token: 0x040001C4 RID: 452
		private float _maleNPCRomanceInitiative = 0.6f;

		// Token: 0x040001C5 RID: 453
		private int _minRomanceChange = 2;

		// Token: 0x040001C6 RID: 454
		private int _maxRomanceChange = 10;

		// Token: 0x040001C7 RID: 455
		private int _romanceDecayDays = 7;

		// Token: 0x040001C8 RID: 456
		private int _minRomanceToAcceptMarriage = 50;

		// Token: 0x040001C9 RID: 457
		private int _romanceDecayAmount = 2;

		// Token: 0x040001CA RID: 458
		private bool _allowRomanceWithMarried = false;

		// Token: 0x040001CB RID: 459
		private bool _companionAutoRecognize = true;

		// Token: 0x040001CC RID: 460
		private bool _kingdomAutoRecognize = true;

		// Token: 0x040001CD RID: 461
		private bool _clanTierAutoRecognize = false;

		// Token: 0x040001CE RID: 462
		private int _clanTierThreshold = 3;

		// Token: 0x040001CF RID: 463
		private float _clanTierRecognitionChance = 25f;

		// Token: 0x040001D0 RID: 464
		private bool _realisticInformationSpread = true;

		// Token: 0x040001D1 RID: 465
		private float _localNewsDistance = 15f;

		// Token: 0x040001D2 RID: 466
		private float _regionalNewsDistance = 50f;

		// Token: 0x040001D3 RID: 467
		private float _kingdomNewsDistance = 150f;

		// Token: 0x040001D4 RID: 468
		private float _nobleDistanceMultiplier = 1.5f;

		// Token: 0x040001D5 RID: 469
		private float _royalDistanceMultiplier = 2f;

		// Token: 0x040001D6 RID: 470
		private int _relationshipThreshold = 20;

		// Token: 0x040001D7 RID: 471
		private bool _enableSocialFiltering = true;

		// Token: 0x040001D8 RID: 472
		private bool _enableRelationshipOverride = true;

		// Token: 0x040001D9 RID: 473
		private bool _enableFactionOverride = true;

		// Token: 0x040001DA RID: 474
		private bool _enableDetailedInfoLogging = false;

		// Token: 0x040001DB RID: 475
		private int _newsDelayHoursPerDistance = 2;

		// Token: 0x040001DC RID: 476
		private bool _enableDynamicEvents = true;

		// Token: 0x040001DD RID: 477
		private bool _dynamicEventsDialogueOnly = false;

		// Token: 0x040001DF RID: 479
		private int _maxSimultaneousDynamicEvents = 1;

		// Token: 0x040001E0 RID: 480
		private int _dynamicEventsInterval = 3;

		// Token: 0x040001E1 RID: 481
		private int _dynamicEventsMinLength = 100;

		// Token: 0x040001E2 RID: 482
		private int _dynamicEventsMaxLength = 600;

		// Token: 0x040001E3 RID: 483
		private int _dynamicEventsLifespan = 100;

		// Token: 0x040001E4 RID: 484
		private bool _enableMilitaryEvents = true;

		// Token: 0x040001E5 RID: 485
		private int _militaryEventsChance = 20;

		// Token: 0x040001E6 RID: 486
		private bool _enablePoliticalEvents = true;

		// Token: 0x040001E7 RID: 487
		private int _politicalEventsChance = 20;

		// Token: 0x040001E8 RID: 488
		private bool _enableEconomicEvents = true;

		// Token: 0x040001E9 RID: 489
		private int _economicEventsChance = 20;

		// Token: 0x040001EA RID: 490
		private bool _enableSocialEvents = true;

		// Token: 0x040001EB RID: 491
		private int _socialEventsChance = 20;

		// Token: 0x040001EC RID: 492
		private bool _enableMysteriousEvents = true;

		// Token: 0x040001ED RID: 493
		private int _mysteriousEventsChance = 20;

		// Token: 0x040001EE RID: 494
		private bool _enableDiplomacy = true;

		// Token: 0x040001F0 RID: 496
		private bool _startInPeace = false;

		// Token: 0x040001F1 RID: 497
		private int _maxParticipatingKingdoms = 4;

		// Token: 0x040001F2 RID: 498
		private int _statementGenerationIntervalDays = 2;

		// Token: 0x040001F3 RID: 499
		private int _diplomacyMinStatementLength = 100;

		// Token: 0x040001F4 RID: 500
		private int _diplomacyMaxStatementLength = 500;

		// Token: 0x040001F5 RID: 501
		private int _minKingdomRelationChange = -10;

		// Token: 0x040001F6 RID: 502
		private int _maxKingdomRelationChange = 10;

		// Token: 0x040001F7 RID: 503
		private float _fatiguePerTroopLost = 0.002f;

		// Token: 0x040001F8 RID: 504
		private float _fatiguePerLordKilled = 5f;

		// Token: 0x040001F9 RID: 505
		private float _fatiguePerLordCaptured = 2f;

		// Token: 0x040001FA RID: 506
		private float _fatiguePerSettlementLost = 10f;

		// Token: 0x040001FB RID: 507
		private float _fatiguePerCaravanDestroyed = 2f;

		// Token: 0x040001FC RID: 508
		private float _fatigueRecoveryPerDay = 3f;

		// Token: 0x040001FD RID: 509
		private bool _enableDeadNPCCleanup = false;

		// Token: 0x040001FE RID: 510
		private bool _enableNPCInitiative = true;

		// Token: 0x040001FF RID: 511
		private float _npcInitiativeBaseChance = 2.5f;

		// Token: 0x04000200 RID: 512
		private float _npcInitiativeFriendlyBonus = 1.5f;

		// Token: 0x04000201 RID: 513
		private float _npcInitiativeHostileBonus = 1f;

		// Token: 0x04000202 RID: 514
		private bool _enableNPCMapInitiative = true;

		// Token: 0x04000204 RID: 516
		private float _npcInitiativeMapBaseChance = 2f;

		// Token: 0x04000206 RID: 518
		private float _npcInitiativeRomanceBonus = 2f;

		// Token: 0x04000207 RID: 519
		private float _npcInitiativeFamiliarityBonus = 3f;

		// Token: 0x04000208 RID: 520
		private float _npcInitiativePartyBonus = 2f;

		// Token: 0x04000209 RID: 521
		private float _npcInitiativeLongTimeSinceContactBonus = 5f;

		// Token: 0x02000062 RID: 98
		[CompilerGenerated]
		[Serializable]
		private sealed class J<,|5Pzbsc-D0i_=z$g0Eadc)
		{
			// Token: 0x06000354 RID: 852 RVA: 0x000575B4 File Offset: 0x000557B4
			[DebuggerStepThrough]
			internal Task \u206E\u202A\u202D\u206A\u206D\u200D\u206F\u200C\u206C\u200E\u206B\u206A\u206B\u206A\u206C\u200E\u200E\u200B\u206E\u206E\u202C\u200E\u206A\u206D\u200E\u202C\u200D\u200B\u206D\u200C\u206E\u200C\u200E\u200C\u202C\u200E\u202B\u206C\u206E\u200F\u202E()
			{
				ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).jZ/J\)>:7;\/EJyMz9ZovG?t% jZ/J\)>:7;\/EJyMz9ZovG?t% = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).jZ/J\)>:7;\/EJyMz9ZovG?t%();
				jZ/J\)>:7;\/EJyMz9ZovG?t%.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E = \u206F\u200F\u200C\u202C\u206C\u206D\u206D\u206D\u206C\u200D\u202D\u200D\u206C\u206D\u206F\u202E\u206A\u202A\u206F\u202D\u200E\u202A\u206D\u202A\u202B\u206A\u200F\u202E\u200B\u206A\u202E\u206F\u206F\u200F\u200D\u206C\u200E\u202D\u200B\u200C\u202E.l\u0017LQ\u0082();
				jZ/J\)>:7;\/EJyMz9ZovG?t%.\u202C\u206F\u206D\u202C\u206E\u200D\u206A\u202A\u202B\u206F\u200D\u206E\u200F\u202E\u206F\u206E\u202E\u206B\u200F\u200E\u206D\u200F\u202D\u202C\u206E\u206F\u202E\u202B\u206E\u202B\u206C\u206E\u200F\u206D\u206C\u206D\u200E\u206E\u206B\u206D\u202E = this;
				jZ/J\)>:7;\/EJyMz9ZovG?t%.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E = -1;
				jZ/J\)>:7;\/EJyMz9ZovG?t%.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E.Start<ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).jZ/J\)>:7;\/EJyMz9ZovG?t%>(ref jZ/J\)>:7;\/EJyMz9ZovG?t%);
				return jZ/J\)>:7;\/EJyMz9ZovG?t%.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E.Task;
			}

			// Token: 0x06000355 RID: 853 RVA: 0x00057600 File Offset: 0x00055800
			[DebuggerStepThrough]
			internal Task \u202B\u202B\u206C\u206D\u206B\u202D\u200F\u206E\u200C\u202A\u202C\u206F\u202C\u200D\u200D\u200C\u202E\u202E\u206A\u206B\u206E\u200C\u206E\u200B\u200B\u202D\u202A\u200D\u202D\u202E\u200D\u206B\u200C\u206E\u202A\u200B\u200C\u206A\u200D\u202E\u202E()
			{
				ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).aH)*NckE%<1=Xn aH)*NckE%<1=Xn = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).aH)*NckE%<1=Xn();
				aH)*NckE%<1=Xn.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E = \u206F\u200F\u200C\u202C\u206C\u206D\u206D\u206D\u206C\u200D\u202D\u200D\u206C\u206D\u206F\u202E\u206A\u202A\u206F\u202D\u200E\u202A\u206D\u202A\u202B\u206A\u200F\u202E\u200B\u206A\u202E\u206F\u206F\u200F\u200D\u206C\u200E\u202D\u200B\u200C\u202E.l\u0017LQ\u0082();
				aH)*NckE%<1=Xn.\u206D\u206A\u200B\u206F\u206C\u200C\u202A\u206A\u202E\u200F\u200C\u206C\u206F\u200E\u206C\u202E\u206F\u206B\u206D\u202D\u206E\u202B\u206F\u200D\u200E\u200D\u206C\u200E\u202B\u206B\u202E\u200C\u202C\u202C\u206C\u206D\u200C\u206A\u206D\u206E\u202E = this;
				aH)*NckE%<1=Xn.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E = -1;
				aH)*NckE%<1=Xn.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E.Start<ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).aH)*NckE%<1=Xn>(ref aH)*NckE%<1=Xn);
				return aH)*NckE%<1=Xn.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E.Task;
			}

			// Token: 0x06000356 RID: 854 RVA: 0x0005764C File Offset: 0x0005584C
			[DebuggerStepThrough]
			internal Task \u202A\u202D\u200B\u206E\u202C\u202C\u202A\u202A\u206E\u202B\u200E\u200B\u206A\u200C\u206F\u200D\u200F\u200C\u206E\u202C\u200D\u206A\u200B\u206A\u202A\u200F\u202B\u202A\u206B\u206E\u200B\u200C\u202C\u200D\u200F\u200F\u206C\u202B\u202E\u206E\u202E()
			{
				ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).HphMPq"fF9WTqkVuP{AGYb)e hphMPq"fF9WTqkVuP{AGYb)e = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).HphMPq"fF9WTqkVuP{AGYb)e();
				hphMPq"fF9WTqkVuP{AGYb)e.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E = \u206F\u200F\u200C\u202C\u206C\u206D\u206D\u206D\u206C\u200D\u202D\u200D\u206C\u206D\u206F\u202E\u206A\u202A\u206F\u202D\u200E\u202A\u206D\u202A\u202B\u206A\u200F\u202E\u200B\u206A\u202E\u206F\u206F\u200F\u200D\u206C\u200E\u202D\u200B\u200C\u202E.l\u0017LQ\u0082();
				hphMPq"fF9WTqkVuP{AGYb)e.\u200F\u200B\u202A\u206F\u206B\u202D\u206B\u206B\u200D\u202C\u206A\u206C\u202B\u200E\u200F\u200C\u200C\u200E\u206B\u202E\u200B\u200C\u206A\u206D\u206F\u206B\u206F\u200B\u202D\u200C\u200C\u202B\u202E\u200F\u206C\u206B\u200B\u200E\u202E\u206A\u202E = this;
				hphMPq"fF9WTqkVuP{AGYb)e.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E = -1;
				hphMPq"fF9WTqkVuP{AGYb)e.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E.Start<ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).HphMPq"fF9WTqkVuP{AGYb)e>(ref hphMPq"fF9WTqkVuP{AGYb)e);
				return hphMPq"fF9WTqkVuP{AGYb)e.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E.Task;
			}

			// Token: 0x06000357 RID: 855 RVA: 0x00057698 File Offset: 0x00055898
			[DebuggerStepThrough]
			internal Task \u200B\u202B\u206F\u206D\u206A\u202B\u206E\u202D\u202E\u202C\u200B\u206E\u206F\u202B\u202A\u206D\u200B\u200C\u200E\u202D\u206C\u202D\u206C\u206D\u202E\u202C\u202D\u200D\u206D\u206A\u200E\u202B\u202E\u206F\u200B\u200F\u202D\u200E\u200F\u202C\u202E()
			{
				ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).,S_Fe}Kvy;c2%l$0X"eO<e0U" ,S_Fe}Kvy;c2%l$0X"eO<e0U" = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).,S_Fe}Kvy;c2%l$0X"eO<e0U"();
				,S_Fe}Kvy;c2%l$0X"eO<e0U".\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E = \u206F\u200F\u200C\u202C\u206C\u206D\u206D\u206D\u206C\u200D\u202D\u200D\u206C\u206D\u206F\u202E\u206A\u202A\u206F\u202D\u200E\u202A\u206D\u202A\u202B\u206A\u200F\u202E\u200B\u206A\u202E\u206F\u206F\u200F\u200D\u206C\u200E\u202D\u200B\u200C\u202E.l\u0017LQ\u0082();
				,S_Fe}Kvy;c2%l$0X"eO<e0U".\u202D\u202E\u200C\u202C\u200E\u202C\u202C\u202B\u200F\u202C\u202B\u202A\u200C\u200B\u202B\u202B\u202D\u206F\u202A\u200D\u200B\u200E\u200F\u206E\u206D\u206F\u206A\u206F\u202B\u200F\u202E\u206B\u202E\u200C\u200C\u202E\u200D\u200F\u202C\u206A\u202E = this;
				,S_Fe}Kvy;c2%l$0X"eO<e0U".\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E = -1;
				,S_Fe}Kvy;c2%l$0X"eO<e0U".\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E.Start<ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).,S_Fe}Kvy;c2%l$0X"eO<e0U">(ref ,S_Fe}Kvy;c2%l$0X"eO<e0U");
				return ,S_Fe}Kvy;c2%l$0X"eO<e0U".\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E.Task;
			}

			// Token: 0x06000358 RID: 856 RVA: 0x000576E4 File Offset: 0x000558E4
			[DebuggerStepThrough]
			internal Task \u202E\u200D\u206D\u202E\u200F\u202C\u206E\u200F\u206E\u206D\u202E\u202C\u200B\u202E\u202D\u200C\u202B\u200F\u206E\u206E\u206C\u206C\u206B\u202B\u206E\u206B\u206E\u206B\u200C\u202E\u202C\u206D\u202A\u202A\u206F\u202A\u206F\u200E\u202B\u202D\u202E()
			{
				ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).#t0^sCIx<uXWnJsJaC@yUP2@' #t0^sCIx<uXWnJsJaC@yUP2@' = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).#t0^sCIx<uXWnJsJaC@yUP2@'();
				#t0^sCIx<uXWnJsJaC@yUP2@'.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E = \u206F\u200F\u200C\u202C\u206C\u206D\u206D\u206D\u206C\u200D\u202D\u200D\u206C\u206D\u206F\u202E\u206A\u202A\u206F\u202D\u200E\u202A\u206D\u202A\u202B\u206A\u200F\u202E\u200B\u206A\u202E\u206F\u206F\u200F\u200D\u206C\u200E\u202D\u200B\u200C\u202E.l\u0017LQ\u0082();
				#t0^sCIx<uXWnJsJaC@yUP2@'.\u206B\u202B\u202A\u202B\u206E\u202B\u202C\u206F\u202C\u206A\u206F\u206F\u206C\u206B\u202E\u206A\u200F\u200C\u202C\u206D\u202C\u206D\u202B\u206E\u200C\u206F\u206B\u200B\u202C\u206B\u202A\u206A\u202B\u200B\u206B\u206D\u206B\u200B\u200F\u206A\u202E = this;
				#t0^sCIx<uXWnJsJaC@yUP2@'.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E = -1;
				#t0^sCIx<uXWnJsJaC@yUP2@'.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E.Start<ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).#t0^sCIx<uXWnJsJaC@yUP2@'>(ref #t0^sCIx<uXWnJsJaC@yUP2@');
				return #t0^sCIx<uXWnJsJaC@yUP2@'.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E.Task;
			}

			// Token: 0x06000359 RID: 857 RVA: 0x00057730 File Offset: 0x00055930
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal void \u206E\u200C\u202B\u202E\u206B\u200C\u202B\u200F\u202C\u206C\u206C\u202C\u206B\u200B\u206B\u202B\u200F\u200B\u206B\u206A\u200F\u202B\u206B\u200F\u206F\u206D\u200B\u202A\u206D\u202B\u200D\u202A\u202B\u202A\u200C\u200E\u200E\u202B\u206C\u202E()
			{
				try
				{
					ProcessStartInfo processStartInfo = \u202D\u206B\u206A\u200D\u206C\u206D\u206E\u206E\u206B\u200E\u200B\u206D\u200F\u202D\u206E\u202A\u206F\u202E\u206A\u206A\u200B\u200E\u200B\u200E\u206A\u202C\u206E\u206D\u206F\u200C\u206D\u206E\u206A\u206B\u206A\u206A\u206F\u200B\u200F\u202E.\u001A\u0018ÌpÍ();
					\u200E\u202C\u202B\u200B\u202B\u200B\u200D\u202B\u200E\u206B\u206A\u202B\u206D\u200F\u200B\u206B\u202A\u202B\u202A\u206B\u202C\u206F\u206F\u206E\u200D\u202D\u200C\u202C\u206B\u206F\u206F\u200F\u202A\u206D\u206D\u206B\u206B\u200C\u200B\u206A\u202E.\u202A\u200C\u200F\u202D\u202B\u202A\u200D\u200C\u206C\u206E\u200F\u206A\u202E\u206F\u206B\u206C\u200F\u206C\u206A\u202E\u206D\u202B\u202D\u206E\u202E\u202A\u202D\u200D\u206B\u200C\u200D\u206D\u202A\u206B\u202C\u206B\u202E\u206E\u206D\u200E\u202E(processStartInfo, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1295124892));
					\u206A\u200F\u200C\u200D\u206F\u200D\u206B\u202C\u206D\u200C\u202E\u200F\u202B\u206D\u202D\u202D\u200C\u200C\u206A\u202A\u202A\u206D\u202A\u206D\u202B\u202A\u206F\u200D\u200D\u200E\u200C\u200E\u206C\u200E\u206A\u200E\u200D\u202B\u200F\u202C\u202E.\u202D\u206A\u206A\u200C\u206B\u202D\u206C\u202B\u200F\u206A\u206C\u200B\u206F\u200F\u206E\u206D\u200C\u200B\u206D\u202C\u206E\u202B\u202E\u200E\u206A\u206B\u206E\u200B\u200F\u200E\u202B\u200F\u206B\u200D\u206F\u206C\u202A\u200C\u202E\u206B\u202E(processStartInfo, true);
					\u200B\u206D\u206A\u202C\u206B\u200C\u200B\u202B\u200C\u206F\u200F\u202C\u202C\u200F\u202A\u206E\u206C\u206F\u200E\u200E\u206F\u200C\u206F\u206F\u200C\u200C\u206A\u202B\u200E\u206C\u200F\u202C\u200F\u206C\u202E\u206F\u202E\u206B\u202C\u206C\u202E.\u200B\u206A\u200C\u202B\u202C\u202A\u202A\u200E\u202C\u206F\u206A\u202E\u206D\u200B\u206E\u206D\u200C\u206C\u200B\u206E\u200C\u202C\u200B\u200C\u206D\u200E\u202B\u206C\u202C\u206C\u200B\u206C\u202E\u200C\u206C\u206A\u206D\u206D\u200C\u202A\u202E(processStartInfo);
				}
				catch (Exception ex)
				{
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-426900010), Colors.Yellow));
				}
			}

			// Token: 0x0600035A RID: 858 RVA: 0x000577A8 File Offset: 0x000559A8
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal void \u200B\u200B\u206D\u202D\u206C\u200C\u206E\u202A\u200C\u202E\u206F\u206A\u206C\u206C\u206C\u200C\u200D\u206A\u200D\u202E\u202B\u206D\u200B\u206B\u200F\u206A\u206B\u206C\u202A\u206C\u200B\u200E\u206C\u202A\u202D\u206F\u206A\u200B\u200B\u206B\u202E()
			{
				try
				{
					ProcessStartInfo processStartInfo = \u202D\u206B\u206A\u200D\u206C\u206D\u206E\u206E\u206B\u200E\u200B\u206D\u200F\u202D\u206E\u202A\u206F\u202E\u206A\u206A\u200B\u200E\u200B\u200E\u206A\u202C\u206E\u206D\u206F\u200C\u206D\u206E\u206A\u206B\u206A\u206A\u206F\u200B\u200F\u202E.\u001A\u0018ÌpÍ();
					\u200E\u202C\u202B\u200B\u202B\u200B\u200D\u202B\u200E\u206B\u206A\u202B\u206D\u200F\u200B\u206B\u202A\u202B\u202A\u206B\u202C\u206F\u206F\u206E\u200D\u202D\u200C\u202C\u206B\u206F\u206F\u200F\u202A\u206D\u206D\u206B\u206B\u200C\u200B\u206A\u202E.\u202A\u200C\u200F\u202D\u202B\u202A\u200D\u200C\u206C\u206E\u200F\u206A\u202E\u206F\u206B\u206C\u200F\u206C\u206A\u202E\u206D\u202B\u202D\u206E\u202E\u202A\u202D\u200D\u206B\u200C\u200D\u206D\u202A\u206B\u202C\u206B\u202E\u206E\u206D\u200E\u202E(processStartInfo, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(181689951));
					\u206A\u200F\u200C\u200D\u206F\u200D\u206B\u202C\u206D\u200C\u202E\u200F\u202B\u206D\u202D\u202D\u200C\u200C\u206A\u202A\u202A\u206D\u202A\u206D\u202B\u202A\u206F\u200D\u200D\u200E\u200C\u200E\u206C\u200E\u206A\u200E\u200D\u202B\u200F\u202C\u202E.\u202D\u206A\u206A\u200C\u206B\u202D\u206C\u202B\u200F\u206A\u206C\u200B\u206F\u200F\u206E\u206D\u200C\u200B\u206D\u202C\u206E\u202B\u202E\u200E\u206A\u206B\u206E\u200B\u200F\u200E\u202B\u200F\u206B\u200D\u206F\u206C\u202A\u200C\u202E\u206B\u202E(processStartInfo, true);
					\u200B\u206D\u206A\u202C\u206B\u200C\u200B\u202B\u200C\u206F\u200F\u202C\u202C\u200F\u202A\u206E\u206C\u206F\u200E\u200E\u206F\u200C\u206F\u206F\u200C\u200C\u206A\u202B\u200E\u206C\u200F\u202C\u200F\u206C\u202E\u206F\u202E\u206B\u202C\u206C\u202E.\u200B\u206A\u200C\u202B\u202C\u202A\u202A\u200E\u202C\u206F\u206A\u202E\u206D\u200B\u206E\u206D\u200C\u206C\u200B\u206E\u200C\u202C\u200B\u200C\u206D\u200E\u202B\u206C\u202C\u206C\u200B\u206C\u202E\u200C\u206C\u206A\u206D\u206D\u200C\u202A\u202E(processStartInfo);
				}
				catch (Exception ex)
				{
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1798194655), Colors.Yellow));
				}
			}

			// Token: 0x0600035B RID: 859 RVA: 0x00057820 File Offset: 0x00055A20
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal void \u200D\u200C\u200B\u206C\u206F\u206F\u202D\u206B\u206A\u200B\u206A\u202D\u200B\u200D\u200D\u206A\u202E\u200E\u206B\u202C\u202E\u200D\u200E\u202E\u200D\u200D\u202E\u202A\u206A\u200C\u206D\u200C\u200D\u200E\u200F\u202D\u206D\u202E\u202D\u200F\u202E()
			{
				try
				{
					ProcessStartInfo processStartInfo = \u202D\u206B\u206A\u200D\u206C\u206D\u206E\u206E\u206B\u200E\u200B\u206D\u200F\u202D\u206E\u202A\u206F\u202E\u206A\u206A\u200B\u200E\u200B\u200E\u206A\u202C\u206E\u206D\u206F\u200C\u206D\u206E\u206A\u206B\u206A\u206A\u206F\u200B\u200F\u202E.\u001A\u0018ÌpÍ();
					\u200E\u202C\u202B\u200B\u202B\u200B\u200D\u202B\u200E\u206B\u206A\u202B\u206D\u200F\u200B\u206B\u202A\u202B\u202A\u206B\u202C\u206F\u206F\u206E\u200D\u202D\u200C\u202C\u206B\u206F\u206F\u200F\u202A\u206D\u206D\u206B\u206B\u200C\u200B\u206A\u202E.\u202A\u200C\u200F\u202D\u202B\u202A\u200D\u200C\u206C\u206E\u200F\u206A\u202E\u206F\u206B\u206C\u200F\u206C\u206A\u202E\u206D\u202B\u202D\u206E\u202E\u202A\u202D\u200D\u206B\u200C\u200D\u206D\u202A\u206B\u202C\u206B\u202E\u206E\u206D\u200E\u202E(processStartInfo, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(836132567));
					\u206A\u200F\u200C\u200D\u206F\u200D\u206B\u202C\u206D\u200C\u202E\u200F\u202B\u206D\u202D\u202D\u200C\u200C\u206A\u202A\u202A\u206D\u202A\u206D\u202B\u202A\u206F\u200D\u200D\u200E\u200C\u200E\u206C\u200E\u206A\u200E\u200D\u202B\u200F\u202C\u202E.\u202D\u206A\u206A\u200C\u206B\u202D\u206C\u202B\u200F\u206A\u206C\u200B\u206F\u200F\u206E\u206D\u200C\u200B\u206D\u202C\u206E\u202B\u202E\u200E\u206A\u206B\u206E\u200B\u200F\u200E\u202B\u200F\u206B\u200D\u206F\u206C\u202A\u200C\u202E\u206B\u202E(processStartInfo, true);
					\u200B\u206D\u206A\u202C\u206B\u200C\u200B\u202B\u200C\u206F\u200F\u202C\u202C\u200F\u202A\u206E\u206C\u206F\u200E\u200E\u206F\u200C\u206F\u206F\u200C\u200C\u206A\u202B\u200E\u206C\u200F\u202C\u200F\u206C\u202E\u206F\u202E\u206B\u202C\u206C\u202E.\u200B\u206A\u200C\u202B\u202C\u202A\u202A\u200E\u202C\u206F\u206A\u202E\u206D\u200B\u206E\u206D\u200C\u206C\u200B\u206E\u200C\u202C\u200B\u200C\u206D\u200E\u202B\u206C\u202C\u206C\u200B\u206C\u202E\u200C\u206C\u206A\u206D\u206D\u200C\u202A\u202E(processStartInfo);
				}
				catch (Exception ex)
				{
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-496944768), Colors.Yellow));
				}
			}

			// Token: 0x0600035C RID: 860 RVA: 0x00057898 File Offset: 0x00055A98
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal void \u202C\u200F\u206E\u206C\u202B\u200F\u200D\u206A\u202C\u206E\u206C\u202C\u200C\u202E\u202B\u200D\u202E\u200F\u206F\u200F\u200E\u206F\u200D\u206B\u200D\u202D\u200C\u206E\u200E\u200F\u202B\u202B\u200D\u206C\u206D\u206A\u202E\u206B\u200B\u206E\u202E()
			{
				try
				{
					ProcessStartInfo processStartInfo = \u202D\u206B\u206A\u200D\u206C\u206D\u206E\u206E\u206B\u200E\u200B\u206D\u200F\u202D\u206E\u202A\u206F\u202E\u206A\u206A\u200B\u200E\u200B\u200E\u206A\u202C\u206E\u206D\u206F\u200C\u206D\u206E\u206A\u206B\u206A\u206A\u206F\u200B\u200F\u202E.\u001A\u0018ÌpÍ();
					\u200E\u202C\u202B\u200B\u202B\u200B\u200D\u202B\u200E\u206B\u206A\u202B\u206D\u200F\u200B\u206B\u202A\u202B\u202A\u206B\u202C\u206F\u206F\u206E\u200D\u202D\u200C\u202C\u206B\u206F\u206F\u200F\u202A\u206D\u206D\u206B\u206B\u200C\u200B\u206A\u202E.\u202A\u200C\u200F\u202D\u202B\u202A\u200D\u200C\u206C\u206E\u200F\u206A\u202E\u206F\u206B\u206C\u200F\u206C\u206A\u202E\u206D\u202B\u202D\u206E\u202E\u202A\u202D\u200D\u206B\u200C\u200D\u206D\u202A\u206B\u202C\u206B\u202E\u206E\u206D\u200E\u202E(processStartInfo, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1361745746));
					\u206A\u200F\u200C\u200D\u206F\u200D\u206B\u202C\u206D\u200C\u202E\u200F\u202B\u206D\u202D\u202D\u200C\u200C\u206A\u202A\u202A\u206D\u202A\u206D\u202B\u202A\u206F\u200D\u200D\u200E\u200C\u200E\u206C\u200E\u206A\u200E\u200D\u202B\u200F\u202C\u202E.\u202D\u206A\u206A\u200C\u206B\u202D\u206C\u202B\u200F\u206A\u206C\u200B\u206F\u200F\u206E\u206D\u200C\u200B\u206D\u202C\u206E\u202B\u202E\u200E\u206A\u206B\u206E\u200B\u200F\u200E\u202B\u200F\u206B\u200D\u206F\u206C\u202A\u200C\u202E\u206B\u202E(processStartInfo, true);
					\u200B\u206D\u206A\u202C\u206B\u200C\u200B\u202B\u200C\u206F\u200F\u202C\u202C\u200F\u202A\u206E\u206C\u206F\u200E\u200E\u206F\u200C\u206F\u206F\u200C\u200C\u206A\u202B\u200E\u206C\u200F\u202C\u200F\u206C\u202E\u206F\u202E\u206B\u202C\u206C\u202E.\u200B\u206A\u200C\u202B\u202C\u202A\u202A\u200E\u202C\u206F\u206A\u202E\u206D\u200B\u206E\u206D\u200C\u206C\u200B\u206E\u200C\u202C\u200B\u200C\u206D\u200E\u202B\u206C\u202C\u206C\u200B\u206C\u202E\u200C\u206C\u206A\u206D\u206D\u200C\u202A\u202E(processStartInfo);
				}
				catch (Exception ex)
				{
				}
			}

			// Token: 0x0600035D RID: 861 RVA: 0x000578EC File Offset: 0x00055AEC
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal void \u200F\u200F\u202E\u200D\u202E\u206B\u200F\u200E\u206F\u200D\u202D\u200D\u202A\u206C\u200D\u200D\u200C\u202B\u200F\u200F\u202A\u206C\u206B\u200E\u206F\u202A\u202E\u202A\u206C\u202E\u200F\u206D\u200F\u206D\u202B\u200F\u206D\u200D\u206E\u200E\u202E()
			{
				try
				{
					Dictionary<string, VoiceInfo> availableVoices = Player2Client.GetAvailableVoices();
					bool flag;
					if (availableVoices == null)
					{
						flag = true;
						goto IL_51;
					}
					IL_0B:
					int num = -653603819;
					IL_10:
					int num2 = num;
					switch (~(-(num2 * -116723307 - -(2059321667 ^ 640339294))) % 4)
					{
					case 1:
						flag = (availableVoices.Count == 0);
						goto IL_51;
					case 2:
						goto IL_0B;
					case 3:
						\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1018694109), Colors.Yellow));
						return;
					}
					string text = \u202A\u200F\u200E\u200F\u202E\u202B\u200B\u206F\u206E\u206D\u200D\u202A\u202A\u200E\u206F\u206A\u200C\u206D\u202B\u200E\u202B\u200C\u202E\u200B\u200B\u200E\u206E\u202D\u206F\u206F\u202D\u206A\u200B\u202A\u200B\u202B\u202D\u202B\u200C\u206E\u202E.\u0020\u009CóVg(\u202C\u200E\u200B\u206A\u206B\u206C\u206B\u206B\u200D\u206E\u202E\u206D\u200C\u206B\u200E\u206E\u206A\u202B\u200D\u206A\u206D\u206C\u200C\u206A\u206C\u200E\u206C\u206C\u202C\u200E\u200D\u200E\u206B\u202A\u202C\u200B\u200B\u206A\u206B\u202B\u202E.ùd\u009F\u0015\u00A9(\u206B\u200B\u200D\u200E\u200D\u206D\u202E\u200F\u202E\u200F\u206F\u200D\u202A\u206B\u200C\u206D\u202C\u206E\u200B\u206A\u202C\u200C\u200E\u206D\u202E\u206D\u200E\u206B\u200B\u202C\u202D\u206A\u206A\u202C\u200F\u202C\u202B\u202C\u200F\u202C\u202E.\u206F\u202E\u200F\u200B\u206E\u202A\u200D\u206F\u206B\u202E\u206F\u202A\u202D\u202C\u200B\u202B\u206B\u206E\u200E\u206A\u200B\u202C\u200E\u200B\u202A\u202C\u206A\u202E\u206A\u200F\u202E\u200F\u202D\u202C\u206D\u206A\u200F\u206D\u202A\u206D\u202E()));
					string text2 = \u200C\u200F\u206B\u206A\u202B\u202D\u206C\u200D\u200B\u200D\u200C\u200D\u202A\u206B\u206A\u202C\u206A\u200F\u200B\u206F\u206B\u202A\u202E\u200C\u206C\u202B\u206F\u202C\u206A\u202A\u206D\u206A\u200B\u202D\u206B\u206F\u200D\u202D\u200B\u206E\u202E.\u008D\u000D\u00A4îÈ(\u200D\u200D\u202E\u202B\u206C\u200B\u206C\u206E\u206B\u202B\u206B\u202D\u200E\u200F\u200C\u200B\u202C\u200B\u206B\u200C\u206C\u206C\u202C\u206C\u206C\u200E\u206D\u206D\u200B\u206F\u206A\u200F\u202C\u202E\u206E\u202B\u200E\u202C\u206D\u202E\u202E.òyyO\u000A(\u200C\u200F\u206B\u206A\u202B\u202D\u206C\u200D\u200B\u200D\u200C\u200D\u202A\u206B\u206A\u202C\u206A\u200F\u200B\u206F\u206B\u202A\u202E\u200C\u206C\u202B\u206F\u202C\u206A\u202A\u206D\u206A\u200B\u202D\u206B\u206F\u200D\u202D\u200B\u206E\u202E.\u008D\u000D\u00A4îÈ(\u200D\u200D\u202E\u202B\u206C\u200B\u206C\u206E\u206B\u202B\u206B\u202D\u200E\u200F\u200C\u200B\u202C\u200B\u206B\u200C\u206C\u206C\u202C\u206C\u206C\u200E\u206D\u206D\u200B\u206F\u206A\u200F\u202C\u202E\u206E\u202B\u200E\u202C\u206D\u202E\u202E.òyyO\u000A(text))));
					string text3 = \u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E.hÌL<\u000C(text2, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-87297364));
					List<VoiceInfo> list = Enumerable.ToList<VoiceInfo>(Enumerable.OrderBy<VoiceInfo, string>(Enumerable.Where<VoiceInfo>(availableVoices.Values, new Func<VoiceInfo, bool>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u202E\u206B\u206A\u202A\u206E\u200B\u202D\u200C\u200B\u202C\u202E\u206F\u200C\u202A\u206B\u206C\u200F\u200C\u202E\u200B\u200E\u200F\u206C\u206F\u202D\u206F\u200E\u206D\u200B\u200E\u206C\u200B\u206E\u202C\u202D\u200D\u202D\u200E\u200E\u206B\u202E)), new Func<VoiceInfo, string>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u206C\u206B\u200B\u206C\u206E\u200D\u200C\u206B\u206C\u206C\u206E\u200C\u202E\u200F\u206C\u206A\u206A\u202D\u206D\u202B\u202B\u206C\u202A\u206E\u206F\u206B\u206D\u202D\u200E\u206D\u202E\u206F\u202C\u202B\u200B\u200F\u202B\u200E\u206B\u202A\u202E)));
					List<VoiceInfo> list2 = Enumerable.ToList<VoiceInfo>(Enumerable.OrderBy<VoiceInfo, string>(Enumerable.Where<VoiceInfo>(availableVoices.Values, new Func<VoiceInfo, bool>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u206F\u202A\u200D\u206D\u202E\u206E\u200B\u202B\u206E\u200C\u206D\u202E\u200B\u200E\u200F\u206B\u206B\u206A\u206C\u202A\u206C\u202A\u202B\u202C\u206D\u200E\u200E\u206E\u206D\u206D\u206B\u200B\u202E\u200B\u202D\u200C\u206A\u202B\u206E\u202D\u202E)), new Func<VoiceInfo, string>(ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).<>9.\u206D\u202B\u206E\u202B\u202D\u202B\u206D\u202B\u206F\u202C\u200C\u206B\u206D\u206B\u206C\u206B\u200B\u200E\u202C\u200D\u206B\u206A\u202B\u206F\u200C\u206C\u206D\u202A\u202D\u200C\u206E\u202A\u202D\u200C\u206A\u202A\u206D\u200B\u206F\u206A\u202E)));
					List<string> list3 = new List<string>
					{
						<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2051071377),
						<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1132497703),
						"",
						<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1316232837),
						"",
						<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-580742881),
						<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-598384589)
					};
					using (List<VoiceInfo>.Enumerator enumerator = list.GetEnumerator())
					{
						for (;;)
						{
							IL_2E1:
							int num3 = enumerator.MoveNext() ? 1082480146 : 1286418459;
							for (;;)
							{
								num2 = num3;
								switch (~(-(num2 * -116723307 - -(2059321667 ^ 640339294))) % 4)
								{
								case 0:
									num3 = 1082480146;
									continue;
								case 1:
									goto IL_2E1;
								case 2:
								{
									VoiceInfo voiceInfo = enumerator.Current;
									list3.Add(\u202E\u200F\u206C\u202A\u206D\u200F\u206A\u200E\u206C\u202C\u202E\u206B\u206B\u206D\u206F\u202B\u200B\u200C\u202D\u206D\u202E\u202C\u206B\u202C\u206E\u202A\u200C\u206B\u200C\u200C\u206B\u200B\u200D\u202C\u200D\u202E\u206A\u206F\u202A\u206F\u202E.\u200B\u200B\u206B\u200E\u202C\u202D\u202E\u202A\u206F\u202D\u200F\u206E\u202B\u202A\u206E\u200D\u206F\u200C\u202D\u200F\u200E\u206B\u206C\u202C\u206F\u202E\u200F\u202B\u206D\u200D\u206E\u206F\u206B\u200D\u202D\u202E\u200D\u202B\u202B\u206A\u202E(new string[]
									{
										voiceInfo.Id,
										<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-467896709),
										voiceInfo.Name,
										<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(371362249),
										voiceInfo.Language,
										<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1827987126)
									}));
									num3 = 138873021;
									continue;
								}
								}
								goto Block_12;
							}
						}
						Block_12:;
					}
					list3.Add("");
					list3.Add(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(358967584));
					list3.Add(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(931910195));
					using (List<VoiceInfo>.Enumerator enumerator2 = list2.GetEnumerator())
					{
						for (;;)
						{
							IL_403:
							int num4 = enumerator2.MoveNext() ? 2085798701 : 494942323;
							for (;;)
							{
								num2 = num4;
								switch (~(-(num2 * -116723307 - -(2059321667 ^ 640339294))) % 4)
								{
								case 0:
									goto IL_403;
								case 1:
								{
									VoiceInfo voiceInfo2 = enumerator2.Current;
									list3.Add(\u202E\u200F\u206C\u202A\u206D\u200F\u206A\u200E\u206C\u202C\u202E\u206B\u206B\u206D\u206F\u202B\u200B\u200C\u202D\u206D\u202E\u202C\u206B\u202C\u206E\u202A\u200C\u206B\u200C\u200C\u206B\u200B\u200D\u202C\u200D\u202E\u206A\u206F\u202A\u206F\u202E.\u200B\u200B\u206B\u200E\u202C\u202D\u202E\u202A\u206F\u202D\u200F\u206E\u202B\u202A\u206E\u200D\u206F\u200C\u202D\u200F\u200E\u206B\u206C\u202C\u206F\u202E\u200F\u202B\u206D\u200D\u206E\u206F\u206B\u200D\u202D\u202E\u200D\u202B\u202B\u206A\u202E(new string[]
									{
										voiceInfo2.Id,
										<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1504852806),
										voiceInfo2.Name,
										<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-432756618),
										voiceInfo2.Language,
										<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-52838832)
									}));
									num4 = 1077897964;
									continue;
								}
								case 2:
									num4 = 2085798701;
									continue;
								}
								goto Block_16;
							}
						}
						Block_16:;
					}
					\u206F\u200C\u202A\u200E\u202D\u206E\u200F\u206A\u202A\u200D\u206A\u206F\u202B\u200E\u202E\u200F\u200C\u200E\u200F\u202C\u202A\u200C\u200D\u206C\u206E\u206B\u206D\u206C\u206A\u206D\u206B\u206C\u202A\u206C\u206C\u206C\u200E\u206B\u206A\u200C\u202E.Ow\u00A2\u00B3\u00B8(text3, list3, \u200E\u206C\u206C\u200D\u200F\u206A\u202E\u200F\u206B\u206F\u202B\u206C\u206E\u200D\u206A\u200D\u202A\u206E\u206F\u202A\u200B\u200C\u200F\u202E\u206B\u200E\u200F\u200E\u200E\u202D\u206E\u206F\u200B\u200C\u200F\u206B\u206A\u200B\u200B\u202D\u202E.Oëo#\u009B());
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1991352041), Colors.Green));
					bool flag2 = AIInfluenceBehavior.Instance != null;
					if (flag2)
					{
						for (;;)
						{
							IL_47D:
							int num5 = 1520316227;
							for (;;)
							{
								num2 = num5;
								switch (~(-(num2 * -116723307 - -(2059321667 ^ 640339294))) % 3)
								{
								case 0:
									goto IL_47D;
								case 2:
									AIInfluenceBehavior.Instance.LogMessage(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.y=\u001E/\u0091(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-826340477), list.Count, list2.Count, text3));
									num5 = 1280837878;
									continue;
								}
								goto Block_10;
							}
						}
						Block_10:;
					}
					return;
					IL_51:
					num = (flag ? -7204637 : -858633804);
					goto IL_10;
				}
				catch (Exception ex)
				{
					\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u206F\u206F\u206B\u202A\u200E\u200D\u206E\u206E\u206F\u206F\u202D\u206B\u206B\u200B\u202E\u206B\u206F\u202E\u202A\u202D\u200E\u200C\u206C\u200C\u200E\u200E\u200C\u200B\u200D\u206B\u202B\u206B\u200B\u206C\u206E\u200B\u206F\u200D\u202E._fØ\u00BBÜ(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-165915237), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex)), Colors.Red));
				}
			}

			// Token: 0x0600035E RID: 862 RVA: 0x00057E80 File Offset: 0x00056080
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal bool \u202E\u206B\u206A\u202A\u206E\u200B\u202D\u200C\u200B\u202C\u202E\u206F\u200C\u202A\u206B\u206C\u200F\u200C\u202E\u200B\u200E\u200F\u206C\u206F\u202D\u206F\u200E\u206D\u200B\u200E\u206C\u200B\u206E\u202C\u202D\u200D\u202D\u200E\u200E\u206B\u202E(VoiceInfo A_1)
			{
				string gender = A_1.Gender;
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u200E\u206D\u206F\u202D\u200F\u202A\u206A\u206F\u200F\u200F\u200C\u200B\u202C\u206C\u206C\u206F\u200E\u200C\u206A\u200E\u202B\u200C\u200D\u200F\u200F\u202B\u202C\u202E\u206E\u202A\u202C\u202A\u200D\u202E\u202E\u202E\u206D\u206D\u200F\u202B\u202E((gender != null) ? \u202A\u200F\u200E\u200F\u202E\u202B\u200B\u206F\u206E\u206D\u200D\u202A\u202A\u200E\u206F\u206A\u200C\u206D\u202B\u200E\u202B\u200C\u202E\u200B\u200B\u200E\u206E\u202D\u206F\u206F\u202D\u206A\u200B\u202A\u200B\u202B\u202D\u202B\u200C\u206E\u202E.\u200E\u202A\u206B\u202D\u202B\u202C\u202E\u202C\u202B\u206E\u202C\u200C\u206D\u200F\u200C\u206F\u206E\u200F\u200C\u200E\u200E\u200B\u206A\u202D\u200B\u200F\u202C\u206C\u200D\u202E\u206A\u200F\u206F\u202D\u206F\u200D\u206A\u202C\u206A\u206E\u202E(gender) : null, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-331255999));
			}

			// Token: 0x0600035F RID: 863 RVA: 0x00057EB0 File Offset: 0x000560B0
			internal string \u206C\u206B\u200B\u206C\u206E\u200D\u200C\u206B\u206C\u206C\u206E\u200C\u202E\u200F\u206C\u206A\u206A\u202D\u206D\u202B\u202B\u206C\u202A\u206E\u206F\u206B\u206D\u202D\u200E\u206D\u202E\u206F\u202C\u202B\u200B\u200F\u202B\u200E\u206B\u202A\u202E(VoiceInfo A_1)
			{
				return A_1.Name;
			}

			// Token: 0x06000360 RID: 864 RVA: 0x00057EC4 File Offset: 0x000560C4
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal bool \u206F\u202A\u200D\u206D\u202E\u206E\u200B\u202B\u206E\u200C\u206D\u202E\u200B\u200E\u200F\u206B\u206B\u206A\u206C\u202A\u206C\u202A\u202B\u202C\u206D\u200E\u200E\u206E\u206D\u206D\u206B\u200B\u202E\u200B\u202D\u200C\u206A\u202B\u206E\u202D\u202E(VoiceInfo A_1)
			{
				string gender = A_1.Gender;
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u200E\u206D\u206F\u202D\u200F\u202A\u206A\u206F\u200F\u200F\u200C\u200B\u202C\u206C\u206C\u206F\u200E\u200C\u206A\u200E\u202B\u200C\u200D\u200F\u200F\u202B\u202C\u202E\u206E\u202A\u202C\u202A\u200D\u202E\u202E\u202E\u206D\u206D\u200F\u202B\u202E((gender != null) ? \u202A\u200F\u200E\u200F\u202E\u202B\u200B\u206F\u206E\u206D\u200D\u202A\u202A\u200E\u206F\u206A\u200C\u206D\u202B\u200E\u202B\u200C\u202E\u200B\u200B\u200E\u206E\u202D\u206F\u206F\u202D\u206A\u200B\u202A\u200B\u202B\u202D\u202B\u200C\u206E\u202E.\u200E\u202A\u206B\u202D\u202B\u202C\u202E\u202C\u202B\u206E\u202C\u200C\u206D\u200F\u200C\u206F\u206E\u200F\u200C\u200E\u200E\u200B\u206A\u202D\u200B\u200F\u202C\u206C\u200D\u202E\u206A\u200F\u206F\u202D\u206F\u200D\u206A\u202C\u206A\u206E\u202E(gender) : null, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1391526647));
			}

			// Token: 0x06000361 RID: 865 RVA: 0x00057EB0 File Offset: 0x000560B0
			internal string \u206D\u202B\u206E\u202B\u202D\u202B\u206D\u202B\u206F\u202C\u200C\u206B\u206D\u206B\u206C\u206B\u200B\u200E\u202C\u200D\u206B\u206A\u202B\u206F\u200C\u206C\u206D\u202A\u202D\u200C\u206E\u202A\u202D\u200C\u206A\u202A\u206D\u200B\u206F\u206A\u202E(VoiceInfo A_1)
			{
				return A_1.Name;
			}

			// Token: 0x0400020A RID: 522
			public static readonly ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) <>9 = new ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc)();

			// Token: 0x0400020B RID: 523
			public static Func<Task> <>9__58_0;

			// Token: 0x0400020C RID: 524
			public static Func<Task> <>9__65_0;

			// Token: 0x0400020D RID: 525
			public static Func<Task> <>9__68_0;

			// Token: 0x0400020E RID: 526
			public static Func<Task> <>9__87_0;

			// Token: 0x0400020F RID: 527
			public static Func<Task> <>9__90_0;

			// Token: 0x04000210 RID: 528
			public static Action <>9__511_0;

			// Token: 0x04000211 RID: 529
			public static Action <>9__511_1;

			// Token: 0x04000212 RID: 530
			public static Action <>9__511_2;

			// Token: 0x04000213 RID: 531
			public static Action <>9__511_3;

			// Token: 0x04000214 RID: 532
			public static Func<VoiceInfo, bool> <>9__511_5;

			// Token: 0x04000215 RID: 533
			public static Func<VoiceInfo, string> <>9__511_6;

			// Token: 0x04000216 RID: 534
			public static Func<VoiceInfo, bool> <>9__511_7;

			// Token: 0x04000217 RID: 535
			public static Func<VoiceInfo, string> <>9__511_8;

			// Token: 0x04000218 RID: 536
			public static Action <>9__511_4;

			// Token: 0x02000063 RID: 99
			private sealed class jZ/J\)>:7;\/EJyMz9ZovG?t% : IAsyncStateMachine
			{
				// Token: 0x06000363 RID: 867 RVA: 0x00057EF4 File Offset: 0x000560F4
				void IAsyncStateMachine.MoveNext()
				{
					int u200E_u202B_u206D_u206E_u200B_u202D_u200E_u206E_u206F_u206A_u200F_u206D_u202B_u206C_u206D_u206B_u202C_u200B_u200C_u206E_u202E_u206C_u200C_u206C_u202A_u200B_u206E_u200E_u206E_u202C_u200C_u206B_u202D_u200C_u200D_u200F_u206F_u202B_u206C_u202A_u202E = this.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E;
					try
					{
						if (u200E_u202B_u206D_u206E_u200B_u202D_u200E_u206E_u206F_u206A_u200F_u206D_u202B_u206C_u206D_u206B_u202C_u200B_u200C_u206E_u202E_u206C_u200C_u206C_u202A_u200B_u206E_u200E_u206E_u202C_u200C_u206B_u202D_u200C_u200D_u200F_u206F_u202B_u206C_u202A_u202E != 0)
						{
							goto IL_0A;
						}
						goto IL_86;
						int num2;
						TaskAwaiter<bool> u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E;
						for (;;)
						{
							IL_0F:
							int num = num2;
							uint num3;
							switch ((num3 = (uint)(-1428699057 + -580504475 - ~(uint)(num ^ -1033629325 - (-1232040638 + (892124866 - -629667975))))) % 7U)
							{
							case 1U:
								goto IL_A2;
							case 2U:
								num2 = (int)(num3 * 2262647451U ^ 2400814908U);
								continue;
							case 3U:
								goto IL_63;
							case 4U:
								u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E = AIClient.TestDeepSeekConnection().GetAwaiter();
								num2 = (u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E.IsCompleted ? -928654598 : 1890093815);
								continue;
							case 5U:
								goto IL_86;
							case 6U:
								goto IL_0A;
							}
							break;
						}
						goto IL_102;
						IL_A2:
						this.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E = 0;
						this.\u206C\u202A\u206D\u206F\u202C\u200F\u202C\u200F\u200E\u206F\u202C\u202B\u200D\u206B\u202B\u200C\u206E\u200B\u206E\u200F\u202D\u206D\u206F\u200D\u206C\u202D\u200B\u202A\u202C\u200E\u206B\u206D\u200C\u200C\u206C\u206A\u202C\u206D\u206F\u200F\u202E = u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E;
						ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).jZ/J\)>:7;\/EJyMz9ZovG?t% jZ/J\)>:7;\/EJyMz9ZovG?t% = this;
						this.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).jZ/J\)>:7;\/EJyMz9ZovG?t%>(ref u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E, ref jZ/J\)>:7;\/EJyMz9ZovG?t%);
						return;
						IL_102:
						u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E.GetResult();
						goto IL_124;
						IL_0A:
						num2 = 1345798601;
						goto IL_0F;
						IL_63:
						u206C_u202A_u206D_u206F_u202C_u200F_u202C_u200F_u200E_u206F_u202C_u202B_u200D_u206B_u202B_u200C_u206E_u200B_u206E_u200F_u202D_u206D_u206F_u200D_u206C_u202D_u200B_u202A_u202C_u200E_u206B_u206D_u200C_u200C_u206C_u206A_u202C_u206D_u206F_u200F_u202E = this.\u206C\u202A\u206D\u206F\u202C\u200F\u202C\u200F\u200E\u206F\u202C\u202B\u200D\u206B\u202B\u200C\u206E\u200B\u206E\u200F\u202D\u206D\u206F\u200D\u206C\u202D\u200B\u202A\u202C\u200E\u206B\u206D\u200C\u200C\u206C\u206A\u202C\u206D\u206F\u200F\u202E;
						this.\u206C\u202A\u206D\u206F\u202C\u200F\u202C\u200F\u200E\u206F\u202C\u202B\u200D\u206B\u202B\u200C\u206E\u200B\u206E\u200F\u202D\u206D\u206F\u200D\u206C\u202D\u200B\u202A\u202C\u200E\u206B\u206D\u200C\u200C\u206C\u206A\u202C\u206D\u206F\u200F\u202E = default(TaskAwaiter<bool>);
						this.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E = -1;
						num2 = -928654598;
						goto IL_0F;
						IL_86:
						goto IL_63;
					}
					catch (Exception exception)
					{
						this.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E = -2;
						this.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E.SetException(exception);
						return;
					}
					IL_124:
					this.\u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E = -2;
					this.\u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E.SetResult();
				}

				// Token: 0x06000364 RID: 868 RVA: 0x0003953C File Offset: 0x0003773C
				[DebuggerHidden]
				void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
				{
				}

				// Token: 0x04000219 RID: 537
				public int \u200E\u202B\u206D\u206E\u200B\u202D\u200E\u206E\u206F\u206A\u200F\u206D\u202B\u206C\u206D\u206B\u202C\u200B\u200C\u206E\u202E\u206C\u200C\u206C\u202A\u200B\u206E\u200E\u206E\u202C\u200C\u206B\u202D\u200C\u200D\u200F\u206F\u202B\u206C\u202A\u202E;

				// Token: 0x0400021A RID: 538
				public AsyncTaskMethodBuilder \u202A\u206D\u200D\u202D\u200C\u206D\u200B\u200C\u206B\u202A\u200B\u206B\u200D\u200C\u202C\u202A\u202C\u200E\u206E\u200F\u200C\u206E\u202E\u206A\u202C\u206D\u206B\u202A\u200C\u206E\u200D\u200E\u200F\u202D\u202A\u206C\u202E\u202B\u206E\u206A\u202E;

				// Token: 0x0400021B RID: 539
				public ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) \u202C\u206F\u206D\u202C\u206E\u200D\u206A\u202A\u202B\u206F\u200D\u206E\u200F\u202E\u206F\u206E\u202E\u206B\u200F\u200E\u206D\u200F\u202D\u202C\u206E\u206F\u202E\u202B\u206E\u202B\u206C\u206E\u200F\u206D\u206C\u206D\u200E\u206E\u206B\u206D\u202E;

				// Token: 0x0400021C RID: 540
				private TaskAwaiter<bool> \u206C\u202A\u206D\u206F\u202C\u200F\u202C\u200F\u200E\u206F\u202C\u202B\u200D\u206B\u202B\u200C\u206E\u200B\u206E\u200F\u202D\u206D\u206F\u200D\u206C\u202D\u200B\u202A\u202C\u200E\u206B\u206D\u200C\u200C\u206C\u206A\u202C\u206D\u206F\u200F\u202E;
			}

			// Token: 0x02000064 RID: 100
			private sealed class #t0^sCIx<uXWnJsJaC@yUP2@' : IAsyncStateMachine
			{
				// Token: 0x06000366 RID: 870 RVA: 0x00058058 File Offset: 0x00056258
				void IAsyncStateMachine.MoveNext()
				{
					int u206A_u200F_u200E_u200D_u206E_u200F_u200F_u200B_u200F_u206E_u202D_u206B_u206C_u206D_u206C_u202D_u206F_u206E_u206E_u202A_u206C_u200D_u206F_u206D_u206C_u202A_u202D_u200F_u206F_u202C_u200B_u206F_u202A_u202D_u206B_u200C_u200F_u200D_u202A_u202A_u202E = this.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E;
					try
					{
						if (u206A_u200F_u200E_u200D_u206E_u200F_u200F_u200B_u200F_u206E_u202D_u206B_u206C_u206D_u206C_u202D_u206F_u206E_u206E_u202A_u206C_u200D_u206F_u206D_u206C_u202A_u202D_u200F_u206F_u202C_u200B_u206F_u202A_u202D_u206B_u200C_u200F_u200D_u202A_u202A_u202E != 0)
						{
							goto IL_0D;
						}
						goto IL_F3;
						int num2;
						TaskAwaiter<bool> u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E;
						for (;;)
						{
							IL_12:
							int num = num2;
							uint num3;
							switch ((num3 = (uint)(-555722918 - (~num + -2044371009 ^ -616597181 * -102527048))) % 7U)
							{
							case 0U:
								u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E = AIClient.TestKoboldCppConnection().GetAwaiter();
								num2 = ((!u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E.IsCompleted) ? 2021469340 : 398779848);
								continue;
							case 1U:
								goto IL_5A;
							case 2U:
								goto IL_F3;
							case 4U:
								num2 = (int)(num3 * 4159529608U ^ 3173639058U);
								continue;
							case 5U:
								goto IL_0D;
							case 6U:
								goto IL_91;
							}
							break;
						}
						goto IL_FF;
						IL_5A:
						this.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E = 0;
						this.\u202D\u200E\u202B\u200D\u200C\u206C\u200B\u206D\u200C\u202D\u200F\u202B\u202D\u200F\u200C\u206A\u202D\u200D\u202E\u206E\u206B\u202C\u202B\u202A\u200F\u206B\u206B\u200B\u200D\u202E\u202A\u206C\u206C\u206E\u200D\u200D\u202D\u206C\u206F\u202B\u202E = u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E;
						ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).#t0^sCIx<uXWnJsJaC@yUP2@' #t0^sCIx<uXWnJsJaC@yUP2@' = this;
						this.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).#t0^sCIx<uXWnJsJaC@yUP2@'>(ref u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E, ref #t0^sCIx<uXWnJsJaC@yUP2@');
						return;
						IL_FF:
						u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E.GetResult();
						goto IL_121;
						IL_0D:
						num2 = 114854164;
						goto IL_12;
						IL_91:
						u202D_u200E_u202B_u200D_u200C_u206C_u200B_u206D_u200C_u202D_u200F_u202B_u202D_u200F_u200C_u206A_u202D_u200D_u202E_u206E_u206B_u202C_u202B_u202A_u200F_u206B_u206B_u200B_u200D_u202E_u202A_u206C_u206C_u206E_u200D_u200D_u202D_u206C_u206F_u202B_u202E = this.\u202D\u200E\u202B\u200D\u200C\u206C\u200B\u206D\u200C\u202D\u200F\u202B\u202D\u200F\u200C\u206A\u202D\u200D\u202E\u206E\u206B\u202C\u202B\u202A\u200F\u206B\u206B\u200B\u200D\u202E\u202A\u206C\u206C\u206E\u200D\u200D\u202D\u206C\u206F\u202B\u202E;
						this.\u202D\u200E\u202B\u200D\u200C\u206C\u200B\u206D\u200C\u202D\u200F\u202B\u202D\u200F\u200C\u206A\u202D\u200D\u202E\u206E\u206B\u202C\u202B\u202A\u200F\u206B\u206B\u200B\u200D\u202E\u202A\u206C\u206C\u206E\u200D\u200D\u202D\u206C\u206F\u202B\u202E = default(TaskAwaiter<bool>);
						this.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E = -1;
						num2 = 398779848;
						goto IL_12;
						IL_F3:
						goto IL_91;
					}
					catch (Exception exception)
					{
						this.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E = -2;
						this.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E.SetException(exception);
						return;
					}
					IL_121:
					this.\u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E = -2;
					this.\u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E.SetResult();
				}

				// Token: 0x06000367 RID: 871 RVA: 0x0003953C File Offset: 0x0003773C
				[DebuggerHidden]
				void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
				{
				}

				// Token: 0x0400021D RID: 541
				public int \u206A\u200F\u200E\u200D\u206E\u200F\u200F\u200B\u200F\u206E\u202D\u206B\u206C\u206D\u206C\u202D\u206F\u206E\u206E\u202A\u206C\u200D\u206F\u206D\u206C\u202A\u202D\u200F\u206F\u202C\u200B\u206F\u202A\u202D\u206B\u200C\u200F\u200D\u202A\u202A\u202E;

				// Token: 0x0400021E RID: 542
				public AsyncTaskMethodBuilder \u202A\u202E\u202D\u206E\u206E\u200C\u202B\u200F\u200C\u202E\u202A\u200D\u202E\u206A\u202E\u202B\u206F\u206F\u206B\u202A\u206A\u200C\u200D\u202E\u206F\u200C\u200D\u206C\u200D\u200F\u206A\u206D\u206D\u200E\u202E\u200F\u206E\u200D\u200C\u206F\u202E;

				// Token: 0x0400021F RID: 543
				public ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) \u206B\u202B\u202A\u202B\u206E\u202B\u202C\u206F\u202C\u206A\u206F\u206F\u206C\u206B\u202E\u206A\u200F\u200C\u202C\u206D\u202C\u206D\u202B\u206E\u200C\u206F\u206B\u200B\u202C\u206B\u202A\u206A\u202B\u200B\u206B\u206D\u206B\u200B\u200F\u206A\u202E;

				// Token: 0x04000220 RID: 544
				private TaskAwaiter<bool> \u202D\u200E\u202B\u200D\u200C\u206C\u200B\u206D\u200C\u202D\u200F\u202B\u202D\u200F\u200C\u206A\u202D\u200D\u202E\u206E\u206B\u202C\u202B\u202A\u200F\u206B\u206B\u200B\u200D\u202E\u202A\u206C\u206C\u206E\u200D\u200D\u202D\u206C\u206F\u202B\u202E;
			}

			// Token: 0x02000065 RID: 101
			private sealed class ,S_Fe}Kvy;c2%l$0X"eO<e0U" : IAsyncStateMachine
			{
				// Token: 0x06000369 RID: 873 RVA: 0x000581B8 File Offset: 0x000563B8
				void IAsyncStateMachine.MoveNext()
				{
					int u200F_u202C_u206B_u202D_u200F_u200F_u202E_u200E_u202C_u200C_u206B_u206E_u206B_u200B_u200E_u200F_u200F_u206E_u206A_u202D_u202E_u206C_u206C_u206B_u200F_u206C_u202A_u200D_u200C_u202C_u202E_u200C_u202E_u206A_u202D_u200B_u202C_u206D_u200E_u202D_u202E = this.\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E;
					try
					{
						if (u200F_u202C_u206B_u202D_u200F_u200F_u202E_u200E_u202C_u200C_u206B_u206E_u206B_u200B_u200E_u200F_u200F_u206E_u206A_u202D_u202E_u206C_u206C_u206B_u200F_u206C_u202A_u200D_u200C_u202C_u202E_u200C_u202E_u206A_u202D_u200B_u202C_u206D_u200E_u202D_u202E != 0)
						{
							goto IL_0A;
						}
						goto IL_70;
						int num2;
						TaskAwaiter<bool> u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E;
						for (;;)
						{
							IL_0F:
							int num = num2;
							uint num3;
							switch ((num3 = (uint)(~(uint)(~(-num) - (-1671031195 + 679683447)))) % 7U)
							{
							case 0U:
								goto IL_4D;
							case 2U:
								goto IL_A2;
							case 3U:
								goto IL_0A;
							case 4U:
								u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E = AIClient.TestOllamaConnection().GetAwaiter();
								num2 = (u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E.IsCompleted ? 1996491284 : -1813347166);
								continue;
							case 5U:
								goto IL_70;
							case 6U:
								num2 = (int)(num3 * 3163190561U ^ 27917296U);
								continue;
							}
							break;
						}
						goto IL_EC;
						IL_A2:
						this.\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E = 0;
						this.\u200B\u202E\u206B\u206D\u202B\u200E\u206F\u200E\u202D\u200B\u202B\u206E\u206A\u206E\u200E\u206F\u206D\u202E\u202A\u200C\u202D\u206E\u206D\u206D\u200D\u206A\u202A\u202B\u200B\u206D\u206D\u202A\u206C\u200E\u200D\u200B\u202E\u200B\u200C\u202E\u202E = u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E;
						ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).,S_Fe}Kvy;c2%l$0X"eO<e0U" ,S_Fe}Kvy;c2%l$0X"eO<e0U" = this;
						this.\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).,S_Fe}Kvy;c2%l$0X"eO<e0U">(ref u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E, ref ,S_Fe}Kvy;c2%l$0X"eO<e0U");
						return;
						IL_EC:
						u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E.GetResult();
						goto IL_10E;
						IL_0A:
						num2 = 1853121976;
						goto IL_0F;
						IL_4D:
						u200B_u202E_u206B_u206D_u202B_u200E_u206F_u200E_u202D_u200B_u202B_u206E_u206A_u206E_u200E_u206F_u206D_u202E_u202A_u200C_u202D_u206E_u206D_u206D_u200D_u206A_u202A_u202B_u200B_u206D_u206D_u202A_u206C_u200E_u200D_u200B_u202E_u200B_u200C_u202E_u202E = this.\u200B\u202E\u206B\u206D\u202B\u200E\u206F\u200E\u202D\u200B\u202B\u206E\u206A\u206E\u200E\u206F\u206D\u202E\u202A\u200C\u202D\u206E\u206D\u206D\u200D\u206A\u202A\u202B\u200B\u206D\u206D\u202A\u206C\u200E\u200D\u200B\u202E\u200B\u200C\u202E\u202E;
						this.\u200B\u202E\u206B\u206D\u202B\u200E\u206F\u200E\u202D\u200B\u202B\u206E\u206A\u206E\u200E\u206F\u206D\u202E\u202A\u200C\u202D\u206E\u206D\u206D\u200D\u206A\u202A\u202B\u200B\u206D\u206D\u202A\u206C\u200E\u200D\u200B\u202E\u200B\u200C\u202E\u202E = default(TaskAwaiter<bool>);
						this.\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E = -1;
						num2 = 1996491284;
						goto IL_0F;
						IL_70:
						goto IL_4D;
					}
					catch (Exception exception)
					{
						this.\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E = -2;
						this.\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E.SetException(exception);
						return;
					}
					IL_10E:
					this.\u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E = -2;
					this.\u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E.SetResult();
				}

				// Token: 0x0600036A RID: 874 RVA: 0x0003953C File Offset: 0x0003773C
				[DebuggerHidden]
				void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
				{
				}

				// Token: 0x04000221 RID: 545
				public int \u200F\u202C\u206B\u202D\u200F\u200F\u202E\u200E\u202C\u200C\u206B\u206E\u206B\u200B\u200E\u200F\u200F\u206E\u206A\u202D\u202E\u206C\u206C\u206B\u200F\u206C\u202A\u200D\u200C\u202C\u202E\u200C\u202E\u206A\u202D\u200B\u202C\u206D\u200E\u202D\u202E;

				// Token: 0x04000222 RID: 546
				public AsyncTaskMethodBuilder \u200F\u206D\u202E\u202C\u206F\u200D\u200D\u200C\u200E\u200D\u202D\u206F\u200F\u202C\u206D\u200B\u202E\u206F\u200D\u202B\u200C\u206C\u200B\u206A\u206F\u200E\u206F\u200D\u206F\u206C\u206B\u206C\u202A\u206B\u206F\u206D\u202C\u200D\u202E\u202E\u202E;

				// Token: 0x04000223 RID: 547
				public ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) \u202D\u202E\u200C\u202C\u200E\u202C\u202C\u202B\u200F\u202C\u202B\u202A\u200C\u200B\u202B\u202B\u202D\u206F\u202A\u200D\u200B\u200E\u200F\u206E\u206D\u206F\u206A\u206F\u202B\u200F\u202E\u206B\u202E\u200C\u200C\u202E\u200D\u200F\u202C\u206A\u202E;

				// Token: 0x04000224 RID: 548
				private TaskAwaiter<bool> \u200B\u202E\u206B\u206D\u202B\u200E\u206F\u200E\u202D\u200B\u202B\u206E\u206A\u206E\u200E\u206F\u206D\u202E\u202A\u200C\u202D\u206E\u206D\u206D\u200D\u206A\u202A\u202B\u200B\u206D\u206D\u202A\u206C\u200E\u200D\u200B\u202E\u200B\u200C\u202E\u202E;
			}

			// Token: 0x02000066 RID: 102
			private sealed class aH)*NckE%<1=Xn : IAsyncStateMachine
			{
				// Token: 0x0600036C RID: 876 RVA: 0x000582F8 File Offset: 0x000564F8
				void IAsyncStateMachine.MoveNext()
				{
					int u202D_u206F_u202B_u202D_u200F_u206B_u206C_u206C_u200F_u206C_u206E_u206F_u202D_u202B_u200F_u206A_u200C_u202B_u202B_u206B_u200E_u206F_u202E_u200B_u202B_u206D_u200B_u200F_u206B_u200E_u202E_u206E_u202A_u206D_u206B_u202B_u200C_u200C_u200E_u206A_u202E = this.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E;
					try
					{
						if (u202D_u206F_u202B_u202D_u200F_u206B_u206C_u206C_u200F_u206C_u206E_u206F_u202D_u202B_u200F_u206A_u200C_u202B_u202B_u206B_u200E_u206F_u202E_u200B_u202B_u206D_u200B_u200F_u206B_u200E_u202E_u206E_u202A_u206D_u206B_u202B_u200C_u200C_u200E_u206A_u202E != 0)
						{
							goto IL_0D;
						}
						goto IL_9B;
						int num2;
						TaskAwaiter<bool> u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E;
						for (;;)
						{
							IL_12:
							int num = num2;
							uint num3;
							switch ((num3 = (uint)(~(uint)(-747294762 * -1027583521 - num * 451785161 ^ 1588309795 + -640407258))) % 7U)
							{
							case 0U:
								goto IL_9B;
							case 1U:
								num2 = (int)(num3 * 2515813156U ^ 252109721U);
								continue;
							case 2U:
								goto IL_0D;
							case 3U:
								goto IL_A7;
							case 4U:
								goto IL_61;
							case 5U:
								u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E = AIClient.TestOpenRouterConnection().GetAwaiter();
								num2 = (u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E.IsCompleted ? 1954227613 : 1768546354);
								continue;
							}
							break;
						}
						goto IL_109;
						IL_61:
						this.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E = 0;
						this.\u200B\u200E\u200D\u202D\u206C\u206D\u206D\u206C\u202C\u200B\u200E\u200C\u202D\u206B\u206B\u202C\u206A\u206C\u202B\u206C\u206B\u206E\u200D\u200C\u200C\u206C\u202D\u200F\u206A\u202C\u202B\u200D\u202E\u202B\u200D\u200F\u202B\u202A\u206A\u206F\u202E = u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E;
						ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).aH)*NckE%<1=Xn aH)*NckE%<1=Xn = this;
						this.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).aH)*NckE%<1=Xn>(ref u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E, ref aH)*NckE%<1=Xn);
						return;
						IL_109:
						u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E.GetResult();
						goto IL_12B;
						IL_0D:
						num2 = 1941260563;
						goto IL_12;
						IL_9B:
						IL_A7:
						u200B_u200E_u200D_u202D_u206C_u206D_u206D_u206C_u202C_u200B_u200E_u200C_u202D_u206B_u206B_u202C_u206A_u206C_u202B_u206C_u206B_u206E_u200D_u200C_u200C_u206C_u202D_u200F_u206A_u202C_u202B_u200D_u202E_u202B_u200D_u200F_u202B_u202A_u206A_u206F_u202E = this.\u200B\u200E\u200D\u202D\u206C\u206D\u206D\u206C\u202C\u200B\u200E\u200C\u202D\u206B\u206B\u202C\u206A\u206C\u202B\u206C\u206B\u206E\u200D\u200C\u200C\u206C\u202D\u200F\u206A\u202C\u202B\u200D\u202E\u202B\u200D\u200F\u202B\u202A\u206A\u206F\u202E;
						this.\u200B\u200E\u200D\u202D\u206C\u206D\u206D\u206C\u202C\u200B\u200E\u200C\u202D\u206B\u206B\u202C\u206A\u206C\u202B\u206C\u206B\u206E\u200D\u200C\u200C\u206C\u202D\u200F\u206A\u202C\u202B\u200D\u202E\u202B\u200D\u200F\u202B\u202A\u206A\u206F\u202E = default(TaskAwaiter<bool>);
						this.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E = -1;
						num2 = 1954227613;
						goto IL_12;
					}
					catch (Exception exception)
					{
						this.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E = -2;
						this.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E.SetException(exception);
						return;
					}
					IL_12B:
					this.\u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E = -2;
					this.\u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E.SetResult();
					for (;;)
					{
						IL_13E:
						int num4 = -584514744;
						for (;;)
						{
							int num = num4;
							uint num3;
							switch ((num3 = (uint)(~(uint)(-747294762 * -1027583521 - num * 451785161 ^ 1588309795 + -640407258))) % 3U)
							{
							case 0U:
								goto IL_13E;
							case 2U:
								num4 = (int)(num3 * 2280872675U ^ 93337944U);
								continue;
							}
							return;
						}
					}
				}

				// Token: 0x0600036D RID: 877 RVA: 0x0003953C File Offset: 0x0003773C
				[DebuggerHidden]
				void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
				{
				}

				// Token: 0x04000225 RID: 549
				public int \u202D\u206F\u202B\u202D\u200F\u206B\u206C\u206C\u200F\u206C\u206E\u206F\u202D\u202B\u200F\u206A\u200C\u202B\u202B\u206B\u200E\u206F\u202E\u200B\u202B\u206D\u200B\u200F\u206B\u200E\u202E\u206E\u202A\u206D\u206B\u202B\u200C\u200C\u200E\u206A\u202E;

				// Token: 0x04000226 RID: 550
				public AsyncTaskMethodBuilder \u202C\u200B\u206E\u200E\u202B\u202B\u202B\u202B\u202B\u200E\u206E\u200D\u206B\u206F\u200B\u206A\u200B\u206D\u206A\u200D\u202B\u202C\u206B\u200E\u202E\u200E\u206B\u206F\u202E\u206B\u206C\u202D\u202C\u202E\u200E\u202A\u202D\u206C\u200C\u206C\u202E;

				// Token: 0x04000227 RID: 551
				public ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) \u206D\u206A\u200B\u206F\u206C\u200C\u202A\u206A\u202E\u200F\u200C\u206C\u206F\u200E\u206C\u202E\u206F\u206B\u206D\u202D\u206E\u202B\u206F\u200D\u200E\u200D\u206C\u200E\u202B\u206B\u202E\u200C\u202C\u202C\u206C\u206D\u200C\u206A\u206D\u206E\u202E;

				// Token: 0x04000228 RID: 552
				private TaskAwaiter<bool> \u200B\u200E\u200D\u202D\u206C\u206D\u206D\u206C\u202C\u200B\u200E\u200C\u202D\u206B\u206B\u202C\u206A\u206C\u202B\u206C\u206B\u206E\u200D\u200C\u200C\u206C\u202D\u200F\u206A\u202C\u202B\u200D\u202E\u202B\u200D\u200F\u202B\u202A\u206A\u206F\u202E;
			}

			// Token: 0x02000067 RID: 103
			private sealed class HphMPq"fF9WTqkVuP{AGYb)e : IAsyncStateMachine
			{
				// Token: 0x0600036F RID: 879 RVA: 0x000584B4 File Offset: 0x000566B4
				void IAsyncStateMachine.MoveNext()
				{
					int u206D_u206C_u206D_u202C_u202D_u206E_u200C_u202E_u206E_u202D_u206C_u200D_u200D_u202E_u202B_u206B_u206E_u206A_u206F_u202B_u200C_u206E_u206F_u200C_u206B_u200D_u200C_u206A_u202E_u200E_u206A_u206A_u202E_u206F_u200D_u202B_u200F_u202D_u202B_u200C_u202E = this.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E;
					try
					{
						if (u206D_u206C_u206D_u202C_u202D_u206E_u200C_u202E_u206E_u202D_u206C_u200D_u200D_u202E_u202B_u206B_u206E_u206A_u206F_u202B_u200C_u206E_u206F_u200C_u206B_u200D_u200C_u206A_u202E_u200E_u206A_u206A_u202E_u206F_u200D_u202B_u200F_u202D_u202B_u200C_u202E != 0)
						{
							goto IL_0D;
						}
						goto IL_E1;
						int num2;
						TaskAwaiter<bool> u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E;
						for (;;)
						{
							IL_12:
							int num = num2;
							uint num3;
							switch ((num3 = (uint)((num - (~1385460376 + 1917329267 * 941497533 - ~(~1610554696)) - (--2061847584 + 634340352 * 759353849) ^ (874592132 ^ 1504518641)) - -1237716638)) % 7U)
							{
							case 0U:
								goto IL_E1;
							case 1U:
								u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E = AIClient.TestPlayer2Connection().GetAwaiter();
								num2 = (u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E.IsCompleted ? 43027693 : -31915555);
								continue;
							case 2U:
								goto IL_A7;
							case 4U:
								goto IL_0D;
							case 5U:
								goto IL_81;
							case 6U:
								num2 = (int)(num3 * 677417933U ^ 618457793U);
								continue;
							}
							break;
						}
						goto IL_129;
						IL_A7:
						this.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E = 0;
						this.\u200E\u206B\u206C\u206F\u206D\u206D\u206B\u206E\u202C\u202A\u202E\u206D\u200F\u200B\u206C\u206F\u206A\u206B\u200B\u200B\u206A\u206D\u202A\u206F\u200B\u206D\u200F\u200D\u200B\u202D\u206A\u206F\u206D\u206B\u200F\u202B\u200B\u200B\u202A\u206D\u202E = u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E;
						ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).HphMPq"fF9WTqkVuP{AGYb)e hphMPq"fF9WTqkVuP{AGYb)e = this;
						this.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E.AwaitUnsafeOnCompleted<TaskAwaiter<bool>, ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc).HphMPq"fF9WTqkVuP{AGYb)e>(ref u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E, ref hphMPq"fF9WTqkVuP{AGYb)e);
						return;
						IL_129:
						u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E.GetResult();
						goto IL_14B;
						IL_0D:
						num2 = 1066275178;
						goto IL_12;
						IL_81:
						u200E_u206B_u206C_u206F_u206D_u206D_u206B_u206E_u202C_u202A_u202E_u206D_u200F_u200B_u206C_u206F_u206A_u206B_u200B_u200B_u206A_u206D_u202A_u206F_u200B_u206D_u200F_u200D_u200B_u202D_u206A_u206F_u206D_u206B_u200F_u202B_u200B_u200B_u202A_u206D_u202E = this.\u200E\u206B\u206C\u206F\u206D\u206D\u206B\u206E\u202C\u202A\u202E\u206D\u200F\u200B\u206C\u206F\u206A\u206B\u200B\u200B\u206A\u206D\u202A\u206F\u200B\u206D\u200F\u200D\u200B\u202D\u206A\u206F\u206D\u206B\u200F\u202B\u200B\u200B\u202A\u206D\u202E;
						this.\u200E\u206B\u206C\u206F\u206D\u206D\u206B\u206E\u202C\u202A\u202E\u206D\u200F\u200B\u206C\u206F\u206A\u206B\u200B\u200B\u206A\u206D\u202A\u206F\u200B\u206D\u200F\u200D\u200B\u202D\u206A\u206F\u206D\u206B\u200F\u202B\u200B\u200B\u202A\u206D\u202E = default(TaskAwaiter<bool>);
						this.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E = -1;
						num2 = 43027693;
						goto IL_12;
						IL_E1:
						goto IL_81;
					}
					catch (Exception exception)
					{
						this.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E = -2;
						this.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E.SetException(exception);
						return;
					}
					IL_14B:
					this.\u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E = -2;
					this.\u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E.SetResult();
				}

				// Token: 0x06000370 RID: 880 RVA: 0x0003953C File Offset: 0x0003773C
				[DebuggerHidden]
				void IAsyncStateMachine.SetStateMachine(IAsyncStateMachine stateMachine)
				{
				}

				// Token: 0x04000229 RID: 553
				public int \u206D\u206C\u206D\u202C\u202D\u206E\u200C\u202E\u206E\u202D\u206C\u200D\u200D\u202E\u202B\u206B\u206E\u206A\u206F\u202B\u200C\u206E\u206F\u200C\u206B\u200D\u200C\u206A\u202E\u200E\u206A\u206A\u202E\u206F\u200D\u202B\u200F\u202D\u202B\u200C\u202E;

				// Token: 0x0400022A RID: 554
				public AsyncTaskMethodBuilder \u200B\u206B\u202C\u202E\u202B\u206D\u206C\u202B\u206C\u206A\u206C\u200E\u206C\u202C\u200F\u200E\u206C\u200F\u202C\u206C\u202A\u202A\u202B\u202B\u202A\u202D\u202A\u206E\u200F\u206E\u200C\u206C\u200B\u206E\u200E\u202D\u202E\u202A\u200C\u202B\u202E;

				// Token: 0x0400022B RID: 555
				public ModSettings.J<,|5Pzbsc-D0i_=z$g0Eadc) \u200F\u200B\u202A\u206F\u206B\u202D\u206B\u206B\u200D\u202C\u206A\u206C\u202B\u200E\u200F\u200C\u200C\u200E\u206B\u202E\u200B\u200C\u206A\u206D\u206F\u206B\u206F\u200B\u202D\u200C\u200C\u202B\u202E\u200F\u206C\u206B\u200B\u200E\u202E\u206A\u202E;

				// Token: 0x0400022C RID: 556
				private TaskAwaiter<bool> \u200E\u206B\u206C\u206F\u206D\u206D\u206B\u206E\u202C\u202A\u202E\u206D\u200F\u200B\u206C\u206F\u206A\u206B\u200B\u200B\u206A\u206D\u202A\u206F\u200B\u206D\u200F\u200D\u200B\u202D\u206A\u206F\u206D\u206B\u200F\u202B\u200B\u200B\u202A\u206D\u202E;
			}
		}
	}
}
