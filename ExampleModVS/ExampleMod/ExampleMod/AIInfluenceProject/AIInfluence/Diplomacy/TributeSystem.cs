A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-20946194) + ex.Message, \u200D\u206E\u200B\u202E\u206A\u202A\u200E\u202E\u200E\u202E\u202A\u202B\u200C\u206F\u202A\u206E\u206E\u200F\u206B\u202D\u200C\u200F\u206E\u200C\u206E\u202E\u200B\u200D\u202D\u202E\u206D\u206E\u202B\u200F\u200C\u206A\u202A\u202C\u206D\u202E.\u202C\u200D\u202E\u200D\u206F\u206E\u206C\u202E\u206A\u206C\u206F\u200F\u206F\u200C\u206A\u206D\u206B\u200B\u206B\u202C\u202A\u202E\u200B\u200B\u206B\u200D\u200D\u202B\u206F\u202E\u202E\u202E\u200F\u200C\u202A\u202D\u206A\u202D\u200F\u200C\u202E));
			}
		}

		// Token: 0x060000F0 RID: 240 RVA: 0x0003761C File Offset: 0x0003581C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnGameLoadFinished()
		{
			try
			{
				this.LogMessage(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1216503438));
				this.TryShowWelcomePopupForCurrentCampaign();
			}
			catch (Exception ex)
			{
				this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(517939611) + ex.Message);
			}
		}

		// Token: 0x060000F1 RID: 241 RVA: 0x0003767C File Offset: 0x0003587C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void TryShowWelcomePopupForCurrentCampaign()
		{
			try
			{
				bool welcomeCheckedThisSession = this._welcomeCheckedThisSession;
				if (!welcomeCheckedThisSession)
				{
					this._welcomeCheckedThisSession = true;
					this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1068173026));
					string activeSaveDirectory = this.GetActiveSaveDirectory();
					string text = Path.Combine(activeSaveDirectory, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-836146139));
					bool flag = File.Exists(text);
					if (flag)
					{
						this.LogMessage(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-545540185));
					}
					else
					{
						try
						{
							File.WriteAllText(text, DateTime.Now.ToString(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-179883606)));
							this.LogMessage(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1008252193) + text);
						}
						catch (Exception ex)
						{
							this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1830293549) + ex.Message);
						}
						this.ShowWelcomePopup();
					}
				}
			}
			catch (Exception ex2)
			{
				this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(98832806) + ex2.Message);
			}
		}

		// Token: 0x060000F2 RID: 242 RVA: 0x000377A0 File Offset: 0x000359A0
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ShowWelcomePopup()
		{
			string titleText = new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-602818986), null).ToString();
			string descriptionText = new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(636628350), null).ToString();
			List<InquiryElement> inquiryElements = new List<InquiryElement>
			{
				new InquiryElement(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-170203241), new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1925327379), null).ToString(), null, true, new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2049342110), null).ToString()),
				new InquiryElement(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1666545269), new TextObject(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(526768979), null).ToString(), null, true, new TextObject(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1361678875), null).ToString()),
				new InquiryElement(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1827918435), new TextObject(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1211779947), null).ToString(), null, true, new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-203355341), null).ToString())
			};
			MultiSelectionInquiryData data = new MultiSelectionInquiryData(titleText, descriptionText, inquiryElements, true, 1, 1, GameTexts.FindText(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1809176277), null).ToString(), string.Empty, new Action<List<InquiryElement>>(this.OnWelcomePopupSelection), null, string.Empty, false);
			MBInformationManager.ShowMultiSelectionInquiry(data, false, false);
		}

		// Token: 0x060000F3 RID: 243 RVA: 0x00037900 File Offset: 0x00035B00
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnWelcomePopupSelection(List<InquiryElement> elements)
		{
			bool flag = elements == null || elements.Count == 0;
			if (!flag)
			{
				InquiryElement inquiryElement = elements[0];
				string text = ((inquiryElement != null) ? inquiryElement.Identifier : null) as string;
				bool flag2 = string.IsNullOrEmpty(text);
				if (!flag2)
				{
					try
					{
						string text2 = text;
						string a = text2;
						if (!(a == <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1878177542)))
						{
							if (!(a == <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1172761461)))
							{
								if (!(a == <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-947112674)))
								{
								}
							}
							else
							{
								try
								{
									Process.Start(new ProcessStartInfo
									{
										FileName = <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1275428734),
										UseShellExecute = true
									});
								}
								catch (Exception ex)
								{
									InformationManager.DisplayMessage(new InformationMessage(new TextObject(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1154523795), null).ToString(), Colors.Yellow));
									this.LogMessage(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-33421774) + ex.Message);
								}
							}
						}
						else
						{
							GlobalSettings<ModSettings>.Instance.OpenDiscordServer();
						}
					}
					catch (Exception ex2)
					{
						this.LogMessage(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1026229751) + ex2.Message);
					}
				}
			}
		}

		// Token: 0x04000049 RID: 73
		private readonly string _logFilePath;

		// Token: 0x0400004A RID: 74
		private readonly string _saveDataPath;

		// Token: 0x0400004B RID: 75
		private string _currentSaveFolder;

		// Token: 0x0400004C RID: 76
		private Dictionary<string, NPCContext> _npcContexts = new Dictionary<string, NPCContext>();

		// Token: 0x0400004D RID: 77
		private Dictionary<string, string> _stringIdToContextKey = new Dictionary<string, string>();

		// Token: 0x0400004E RID: 78
		private readonly Random _random = new Random();

		// Token: 0x0400004F RID: 79
		private readonly AIDecisionHandler _decisionHandler;

		// Token: 0x04000050 RID: 80
		private readonly SettlementCombatHandler _settlementCombatHandler;

		// Token: 0x04000051 RID: 81
		private SettlementCombatManager _settlementCombatManager;

		// Token: 0x04000052 RID: 82
		private bool _isInitialized = false;

		// Token: 0x04000053 RID: 83
		private CampaignTime _lastDialogueAnalysisTime = CampaignTime.Never;

		// Token: 0x04000054 RID: 84
		private static AIInfluenceBehavior _instance;

		// Token: 0x04000055 RID: 85
		private static bool _settingsSubscribed;

		// Token: 0x04000056 RID: 86
		private readonly DelayedTaskManager _delayedTaskManager;

		// Token: 0x04000057 RID: 87
		private readonly SaveQueueManager _saveQueueManager;

		// Token: 0x04000058 RID: 88
		private WorldInfoManager _worldInfoManager;

		// Token: 0x04000059 RID: 89
		private static int _hourlyTickCounter;

		// Token: 0x0400005A RID: 90
		private NPCInitiativeSystem _npcInitiativeSystem;

		// Token: 0x0400005B RID: 91
		private List<string> _followingHeroIds = new List<string>();

		// Token: 0x0400005C RID: 92
		private string _serializedActionState = string.Empty;

		// Token: 0x0400005D RID: 93
		private bool _playerReinforcementAdded = false;

		// Token: 0x0400005E RID: 94
		private const string WelcomeMarkerFileName = "welcome_popup_shown.txt";

		// Token: 0x0400005F RID: 95
		private bool _welcomeCheckedThisSession = false;

		// Token: 0x02000019 RID: 25
		[CompilerGenerated]
		[Serializable]
		private sealed class \u200E\u202B\u206B\u202D\u200D\u200D\u202B\u200B\u200F\u202E\u200B\u200B\u206E\u206E\u200E\u200C\u200C\u200E\u202B\u206A\u200D\u202C\u202E\u200F\u202A\u202D\u202C\u202C\u206D\u200D\u206D\u206A\u200B\u200F\u202A\u202D\u200E\u202C\u206B\u202E\u202E
		{
			// Token: 0x060000F9 RID: 249 RVA: 0x00037BC4 File Offset: 0x00035DC4
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal string \u200B\u200C\u200B\u206A\u200D\u200B\u202C\u206E\u200E\u202C\u202D\u200D\u200B\u202A\u206D\u202E\u206A\u206C\u202E\u202E\u200D\u200C\u206F\u206F\u200C\u200F\u202B\u202A\u206B\u202C\u200E\u202B\u206F\u206B\u206D\u206F\u206A\u202C\u206D\u202A\u202E(Workshop A_1)
			{
				string text = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(660274106);
				object obj = \u200B\u206D\u200D\u202D\u200C\u206A\u202C\u200F\u206A\u206E\u202A\u206B\u206F\u206C\u202D\u206C\u202E\u200F\u202C\u206F\u202C\u206D\u206D\u202E\u202E\u202A\u200D\u202C\u202B\u202A\u202D\u200F\u200D\u202C\u206C\u200F\u202A\u200C\u200F\u206B\u202E.\u001C\u0093Åâ'(A_1);
				WorkshopType workshopType = \u202D\u206E\u200E\u200C\u200E\u206D\u202A\u206F\u200D\u206B\u202D\u202A\u202E\u206A\u202C\u202A\u206F\u200F\u206D\u202B\u200F\u202C\u200C\u206A\u202A\u200E\u200B\u200E\u202E\u206C\u200E\u202C\u206A\u202A\u206D\u206F\u200F\u200F\u206E\u202A\u202E.&ã\u0092Ð\u0015(A_1);
				return \u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(text, obj, (workshopType != null) ? \u202A\u206B\u202A\u206C\u202A\u206A\u202A\u200D\u206B\u200E\u206E\u202A\u202B\u206B\u202E\u200D\u200D\u202E\u206B\u206F\u200C\u202D\u202A\u206D\u206B\u200B\u206B\u200B\u200B\u206E\u202E\u200B\u206F\u206A\u206F\u206A\u206C\u206A\u200D\u206B\u202E.\u202D\u206D\u206F\u202E\u202A\u200E\u206F\u206E\u200F\u206F\u206E\u206C\u202E\u206F\u202C\u200E\u200B\u206D\u206F\u202E\u202B\u200D\u202D\u206A\u206F\u200B\u200C\u200B\u202D\u206B\u206E\u206B\u206B\u206C\u206C\u200B\u200D\u206E\u202C\u206E\u202E(workshopType) : null);
			}

			// Token: 0x060000FA RID: 250 RVA: 0x00037C04 File Offset: 0x00035E04
			internal bool \u202D\u206D\u202E\u202D\u202B\u200E\u202D\u200E\u202D\u200B\u202D\u200B\u206D\u202B\u206F\u202B\u200F\u206B\u206F\u202C\u202B\u200F\u200E\u206D\u200E\u200B\u206D\u206D\u200F\u202B\u200B\u200B\u200F\u206C\u206B\u200D\u206C\u206B\u200F\u206F\u202E(MapEventParty A_1)
			{
				return \u206D\u206A\u202C\u206D\u202D\u206C\u200B\u200E\u200E\u206F\u200C\u202B\u206D\u206B\u206B\u206A\u202D\u206D\u206B\u206F\u202D\u206F\u200B\u206F\u202D\u206D\u200C\u202E\u202B\u202A\u200B\u200F\u206D\u206A\u206D\u202D\u200F\u206D\u200F\u202C\u202E.wÕÑN\u001B(\u202C\u200C\u206A\u200B\u202E\u202E\u206B\u200B\u206E\u202C\u202D\u206E\u202E\u206C\u206B\u202A\u206F\u206F\u202B\u206F\u206E\u200E\u206F\u202D\u206C\u200F\u200D\u202D\u202C\u202D\u202E\u206D\u206B\u202D\u202B\u206B\u206B\u206F\u200B\u206C\u202E.#ñÁa\u00B8(A_1)) > 0;
			}

			// Token: 0x060000FB RID: 251 RVA: 0x00037C2C File Offset: 0x00035E2C
			internal int \u206C\u202B\u202C\u202A\u200F\u202A\u200D\u200C\u200C\u202A\u202B\u202B\u200D\u206D\u200D\u202E\u206E\u206A\u202D\u200B\u202C\u202C\u206B\u206F\u202C\u206A\u202C\u200B\u202A\u206F\u206E\u206B\u206E\u206F\u206C\u206C\u202C\u206D\u200D\u200E\u202E(DynamicEvent A_1)
			{
				return A_1.Importance;
			}

			// Token: 0x060000FC RID: 252 RVA: 0x00037C40 File Offset: 0x00035E40
			internal int \u202B\u200D\u202A\u202E\u206B\u206C\u206F\u206F\u206C\u206A\u202C\u200E\u202C\u206E\u206B\u202A\u200E\u206F\u200F\u200C\u202A\u202C\u200E\u200D\u202D\u200E\u202C\u202B\u202A\u206A\u202E\u200C\u200F\u202E\u206B\u202C\u202C\u202B\u206E\u202E\u202E(DynamicEvent A_1)
			{
				return A_1.DaysSinceCreation;
			}

			// Token: 0x060000FD RID: 253 RVA: 0x00037C54 File Offset: 0x00035E54
			internal double \u200D\u206E\u206E\u206B\u206D\u206B\u202C\u206B\u206B\u202C\u200C\u202A\u202B\u202E\u202A\u206C\u206D\u206F\u206F\u206F\u206D\u206C\u202B\u200C\u202A\u200E\u206A\u202A\u200F\u200E\u202E\u200B\u202D\u200D\u200E\u200D\u200D\u202E\u200C\u202A\u202E(SettlementVisit A_1)
			{
				return A_1.VisitTime.ToDays;
			}

			// Token: 0x060000FE RID: 254 RVA: 0x00037C70 File Offset: 0x00035E70
			internal string \u200C\u202B\u206A\u200B\u202A\u200F\u202E\u206D\u206B\u206E\u200C\u206D\u206D\u200C\u206A\u206B\u206E\u206E\u202E\u206C\u200F\u206C\u200D\u202E\u206A\u206B\u206A\u206F\u200F\u202D\u202C\u206F\u200F\u200F\u202A\u200F\u202E\u200E\u200F\u202C\u202E(NPCEraseInfo A_1)
			{
				return A_1.Name;
			}

			// Token: 0x04000060 RID: 96
			public static readonly AIInfluenceBehavior.\u200E\u202B\u206B\u202D\u200D\u200D\u202B\u200B\u200F\u202E\u200B\u200B\u206E\u206E\u200E\u200C\u200C\u200E\u202B\u206A\u200D\u202C\u202E\u200F\u202A\u202D\u202C\u202C\u206D\u200D\u206D\u206A\u200B\u200F\u202A\u202D\u200E\u202C\u206B\u202E\u202E <>9 = new AIInfluenceBehavior.\u200E\u202B\u206B\u202D\u200D\u200D\u202B\u200B\u200F\u202E\u200B\u200B\u206E\u206E\u200E\u200C\u200C\u200E\u202B\u206A\u200D\u202C\u202E\u200F\u202A\u202D\u202C\u202C\u206D\u200D\u206D\u206A\u200B\u200F\u202A\u202D\u200E\u202C\u206B\u202E\u202E();

			// Token: 0x04000061 RID: 97
			public static Func<Workshop, string> <>9__48_1;

			// Token: 0x04000062 RID: 98
			public static Func<MapEventParty, bool> <>9__57_0;

			// Token: 0x04000063 RID: 99
			public static Func<DynamicEvent, int> <>9__75_0;

			// Token: 0x04000064 RID: 100
			public static Func<DynamicEvent, int> <>9__75_1;

			// Token: 0x04000065 RID: 101
			public static Func<SettlementVisit, double> <>9__85_1;

			// Token: 0x04000066 RID: 102
			public static Func<NPCEraseInfo, string> <>9__95_0;
		}

		// Token: 0x0200001A RID: 26
		[CompilerGenerated]
		private sealed class \u202D\u206D\u202C\u202B\u206E\u200B\u206D\u206C\u206C\u202C\u200D\u206A\u206B\u206B\u200F\u206E\u202A\u200F\u206D\u200C\u200B\u206C\u206E\u206E\u202A\u200B\u202E\u200D\u202D\u202C\u200F\u206D\u200D\u200E\u200F\u206D\u206B\u202A\u206D\u206A\u202E
		{
			// Token: 0x06000100 RID: 256 RVA: 0x00037C84 File Offset: 0x00035E84
			internal bool \u206D\u200B\u202B\u206A\u206A\u202C\u200E\u200F\u206E\u206E\u206F\u206E\u200C\u206F\u200E\u206A\u200B\u206F\u206C\u206B\u206C\u200E\u206F\u200B\u202D\u200C\u206B\u206A\u200C\u202A\u200C\u200C\u202A\u206A\u206C\u206D\u206D\u206C\u206F\u202A\u202E(Agent A_1)
			{
				if (A_1 != null)
				{
					for (;;)
					{
						IL_06:
						int num = -413123079;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)((~194215096 - (~num2 + -510056932)) * 1772677403)) % 5U)
							{
							case 0U:
								goto IL_06;
							case 1U:
								if (\u200B\u206A\u200E\u202A\u206B\u202D\u206A\u200B\u206C\u206C\u206E\u206C\u206D\u206A\u200F\u206F\u206A\u202C\u206A\u200F\u206C\u200F\u202E\u202E\u206A\u200C\u202D\u206C\u200C\u206C\u202A\u202D\u206D\u202A\u200C\u200D\u200E\u202D\u206C\u202C\u202E.\u0081\u001B9S\u00B0(A_1) != null)
								{
									num = (int)(num3 * 1902733740U ^ 3414147273U);
									continue;
								}
								goto IL_B4;
							case 3U:
								if (\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.HD\u0014F!(A_1))
								{
									num = (int)(num3 * 307276381U ^ 3016004356U);
									continue;
								}
								goto IL_B4;
							case 4U:
								if (\u200B\u206A\u200E\u202A\u206B\u202D\u206A\u200B\u206C\u206C\u206E\u206C\u206D\u206A\u200F\u206F\u206A\u202C\u206A\u200F\u206C\u200F\u202E\u202E\u206A\u200C\u202D\u206C\u200C\u206C\u202A\u202D\u206D\u202A\u200C\u200D\u200E\u202D\u206C\u202C\u202E.\u0081\u001B9S\u00B0(A_1) == \u202C\u200F\u200E\u206B\u202E\u206E\u200D\u202D\u206F\u206A\u200C\u200F\u200E\u200B\u200C\u200B\u200E\u200E\u206F\u202A\u206C\u206B\u206B\u206C\u200B\u200F\u206F\u200E\u202C\u200C\u202A\u206B\u206D\u206B\u206C\u202B\u200B\u200C\u202D\u206B\u202E.Æ\u0097\u00B1\u008BÍ(this.\u200E\u206A\u206E\u202E\u206A\u206E\u200F\u200B\u202D\u200F\u200B\u206E\u200F\u200F\u200F\u200B\u206B\u202A\u200E\u200E\u202C\u206D\u200E\u206C\u202A\u202A\u202B\u202C\u200E\u206C\u206C\u206C\u200B\u200C\u200F\u200E\u206C\u206C\u202C\u202D\u202E))
								{
									num = (int)(num3 * 176497743U ^ 955372126U);
									continue;
								}
								goto IL_B4;
							}
							goto Block_1;
						}
					}
					Block_1:
					return \u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.}\u0008\u00A6*\u0001(A_1);
				}
				IL_B4:
				return false;
			}

			// Token: 0x06000101 RID: 257 RVA: 0x00037D48 File Offset: 0x00035F48
			internal bool \u202D\u202D\u200D\u200E\u200D\u206D\u200B\u200B\u202E\u200C\u206E\u200E\u202D\u206E\u200E\u202C\u206C\u202B\u206F\u206B\u206C\u206A\u200E\u202E\u202D\u200F\u206C\u202D\u202E\u200D\u200E\u200D\u202C\u200C\u202D\u202D\u200D\u200B\u202B\u206A\u202E(Agent A_1)
			{
				if (A_1 != null)
				{
					for (;;)
					{
						IL_06:
						int num = 748231681;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-(uint)(~(num2 - ((-648962793