using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AIInfluence.SettlementCombat.PhrasesDisplay.Popup;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;
using \u206F\u202A\u206F\u202C\u206D\u202C\u200B\u200D\u206E\u206B\u202A\u206B\u206D\u200F\u206B\u202B\u200F\u200E\u202B\u206B\u202B\u200B\u206D\u202B\u202D\u202B\u202A\u200F\u202C\u202E\u200D\u200B\u206B\u206E\u202A\u206A\u202D\u200B\u200F\u206C\u202E;

namespace AIInfluence.SettlementCombat.PhrasesDisplay
{
	// Token: 0x0200015D RID: 349
	public class CombatPhrasesDisplay : MissionLogic
	{
		// Token: 0x06000B16 RID: 2838 RVA: 0x000C67F8 File Offset: 0x000C49F8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public CombatPhrasesDisplay(CombatSituationAnalysis analysis)
		{
			this._analysis = analysis;
			this._logger = SettlementCombatLogger.Instance;
			this._initialized = true;
			CombatSituationAnalysis analysis2 = this._analysis;
			this._malePanicPhrases = ((((analysis2 != null) ? analysis2.CivilianPhrasesMalePanic : null) != null) ? new List<string>(this._analysis.CivilianPhrasesMalePanic) : new List<string>());
			CombatSituationAnalysis analysis3 = this._analysis;
			this._maleFightPhrases = ((((analysis3 != null) ? analysis3.CivilianPhrasesMaleFight : null) != null) ? new List<string>(this._analysis.CivilianPhrasesMaleFight) : new List<string>());
			CombatSituationAnalysis analysis4 = this._analysis;
			this._femalePhrases = ((((analysis4 != null) ? analysis4.CivilianPhrasesFemale : null) != null) ? new List<string>(this._analysis.CivilianPhrasesFemale) : new List<string>());
			CombatSituationAnalysis analysis5 = this._analysis;
			this._childPhrases = ((((analysis5 != null) ? analysis5.CivilianPhrasesChild : null) != null) ? new List<string>(this._analysis.CivilianPhrasesChild) : new List<string>());
			this._logger.Log(\u202D\u200E\u206D\u206A\u200C\u200C\u206C\u202B\u206F\u202E\u202D\u202B\u206A\u200D\u206D\u206E\u202A\u206B\u202D\u200E\u206F\u206E\u200C\u206D\u202D\u206F\u200F\u206E\u206C\u202B\u206E\u206F\u206B\u202B\u202A\u206F\u206F\u200C\u200B\u202C\u202E.\u200C\u206A\u202D\u202E\u200B\u202C\u202D\u202D\u206F\u202A\u206D\u202D\u200C\u202A\u202A\u202A\u202B\u200D\u206B\u202D\u202C\u206C\u202D\u202D\u206B\u200F\u202E\u200D\u200E\u206A\u206F\u202D\u206A\u206A\u206B\u200E\u202A\u206F\u200F\u206B\u202E(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1968858044), new object[]
			{
				this._malePanicPhrases.Count,
				this._maleFightPhrases.Count,
				this._femalePhrases.Count,
				this._childPhrases.Count
			}));
		}

		// Token: 0x06000B17 RID: 2839 RVA: 0x000C6994 File Offset: 0x000C4B94
		public override void AfterStart()
		{
			\u200C\u206B\u202D\u206D\u202A\u200E\u200B\u206F\u202A\u206F\u202C\u206D\u200E\u206F\u202E\u200E\u206F\u206D\u202A\u202B\u200C\u202E\u202E\u200C\u200D\u206C\u206B\u202D\u206E\u200F\u200E\u202B\u200B\u206C\u202A\u202E\u202C\u202C\u206D\u206A\u202E.\u00BE\u0090\u00B3/\u0095(this);
			CombatPhrasePopupView.EnsureCreated();
		}

		// Token: 0x06000B18 RID: 2840 RVA: 0x000C69B4 File Offset: 0x000C4BB4
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnMissionTick(float dt)
		{
			\u202B\u202C\u206A\u206E\u202B\u206D\u200C\u200F\u206D\u202E\u206A\u200E\u202C\u202A\u202D\u206B\u200B\u206F\u200F\u200D\u206C\u202E\u202A\u202D\u202B\u200D\u202B\u200B\u206A\u202E\u206D\u200D\u206B\u202C\u206B\u202A\u206E\u202B\u200E\u206F\u202E.\u00D7ýæ3\u00A9(this, dt);
			if (this._initialized)
			{
				goto IL_16;
			}
			goto IL_88;
			int num2;
			for (;;)
			{
				IL_1B:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(-(uint)(-(uint)(~-1994843250 * -1734520869 - num))))) % 5U)
				{
				case 1U:
					goto IL_65;
				case 2U:
					goto IL_79;
				case 3U:
					if (this._analysis != null)
					{
						num2 = (int)(num3 * 998920777U ^ 3355645693U);
						continue;
					}
					goto IL_88;
				case 4U:
					goto IL_16;
				}
				break;
			}
			goto IL_A1;
			IL_65:
			return;
			IL_79:
			bool flag = \u202D\u206F\u200E\u202E\u202B\u200D\u206F\u200F\u200D\u202C\u206A\u202D\u206A\u202D\u202D\u202C\u206C\u202D\u206D\u202B\u206D\u200E\u200D\u206D\u200D\u202C\u206E\u200C\u206B\u206A\u206E\u206A\u200D\u200D\u202A\u202B\u202B\u206D\u200B\u200D\u202E.3,\u0005\u00AFö() == null;
			goto IL_89;
			IL_A1:
			try
			{
				this._tickTimer += dt;
				bool flag2 = this._tickTimer < 0.5f;
				if (flag2)
				{
					for (;;)
					{
						int num = 1180388023;
						switch (~(-(-(~-1994843250 * -1734520869 - num))) % 3)
						{
						case 0:
							continue;
						case 1:
							goto IL_F2;
						}
						break;
					}
					goto IL_103;
					IL_F2:
					return;
				}
				IL_103:
				this._tickTimer = 0f;
				this.CheckAgentsForPhraseActivation();
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1953418669), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex), ex);
			}
			return;
			IL_16:
			num2 = -1572629689;
			goto IL_1B;
			IL_88:
			flag = true;
			IL_89:
			num2 = (flag ? 1248323900 : -1811153137);
			goto IL_1B;
		}

		// Token: 0x06000B19 RID: 2841 RVA: 0x000C6B14 File Offset: 0x000C4D14
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CheckAgentsForPhraseActivation()
		{
			bool flag = \u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5() == null;
			if (!flag)
			{
				goto IL_81;
			}
			IL_12:
			int num = -567184988;
			IL_17:
			int num2 = num;
			switch (~(-(-1890649765 - 1285642629) - num2 * -1109824569 - --1386488254) % 5)
			{
			case 0:
				goto IL_12;
			case 2:
				return;
			case 3:
				return;
			case 4:
				IL_81:
				num = (\u202E\u206A\u206A\u206F\u202C\u200D\u200F\u206E\u202C\u200D\u202E\u202A\u200C\u206B\u202B\u200F\u206B\u200D\u200D\u206D\u200E\u206C\u200B\u200B\u200E\u206D\u206A\u206F\u206C\u200D\u206E\u202A\u200C\u200E\u200D\u202B\u206A\u202D\u200D\u202E\u202E.\u00B4\u00B7nµ?(\u206C\u206F\u206F\u206F\u206C\u202C\u206B\u206A\u202B\u206F\u206C\u200D\u206F\u206E\u200B\u206E\u202A\u202C\u200F\u202C\u200E\u206A\u206F\u206F\u206B\u206A\u206D\u200C\u206E\u206E\u200C\u202C\u202A\u206C\u200C\u202C\u200F\u200E\u202D\u202E.\u202D\u200D\u202B\u202E\u206B\u206A\u202A\u202E\u206B\u200C\u200C\u200D\u206D\u206F\u206A\u206B\u200C\u206A\u202A\u200D\u200B\u206C\u200D\u206F\u202A\u206F\u200F\u202D\u206D\u206A\u206B\u202D\u206F\u202A\u202A\u202D\u206D\u206F\u206B\u206F\u202E(), this._nextPhraseAllowedTime) ? -1059439204 : -1383398702);
				goto IL_17;
			}
			PlayerReinforcementMissionLogic missionBehavior = \u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u206A\u206A\u206C\u206D\u206A\u206C\u200B\u206A\u200C\u202B\u200C\u206D\u200C\u202C\u202D\u206E\u206D\u206A\u200D\u206F\u200B\u200E\u200C\u206A\u202E\u206E\u206D\u200F\u200D\u206B\u200D\u200F\u206A\u202D\u202A\u202A\u200F\u200E\u200F\u206B\u202E().GetMissionBehavior<PlayerReinforcementMissionLogic>();
			Vec3 v = \u202C\u206A\u202B\u206A\u200E\u202A\u206E\u206E\u206D\u206F\u200C\u202C\u202B\u206A\u200F\u202D\u200D\u202B\u202A\u202E\u200E\u206F\u202A\u200D\u206D\u202A\u200B\u206A\u202C\u200D\u202B\u206B\u200C\u206D\u206E\u200F\u206D\u202B\u200F\u206A\u202E.\u0085\u00B4vüy(\u202D\u206F\u200E\u202E\u202B\u200D\u206F\u200F\u200D\u202C\u206A\u202D\u206A\u202D\u202D\u202C\u206C\u202D\u206D\u202B\u206D\u200E\u200D\u206D\u200D\u202C\u206E\u200C\u206B\u206A\u206E\u206A\u200D\u200D\u202A\u202B\u202B\u206D\u200B\u200D\u202E.3,\u0005\u00AFö());
			using (List<Agent>.Enumerator enumerator = \u202B\u200F\u206F\u206E\u206E\u206B\u206D\u202A\u202C\u200B\u200D\u200D\u206B\u206F\u200B\u200D\u206F\u206A\u206E\u202A\u200C\u206D\u202A\u206D\u206B\u206E\u202A\u202B\u200F\u202E\u202A\u206C\u202E\u200F\u200B\u200D\u206F\u206D\u206F\u202E\u202E.X\u00A1+:\u0091(\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5()).GetEnumerator())
			{
				Agent agent;
				string phraseForAgent;
				float num5;
				for (;;)
				{
					IL_2C8:
					int num3 = enumerator.MoveNext() ? 1430623731 : -632000775;
					for (;;)
					{
						num2 = num3;
						uint num4;
						bool flag2;
						bool flag3;
						switch ((num4 = (uint)(~(uint)(-(-1890649765 - 1285642629) - num2 * -1109824569 - --1386488254))) % 23U)
						{
						case 0U:
							if (\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.HD\u0014F!(agent))
							{
								num3 = (int)(num4 * 3521339457U ^ 1244956575U);
								continue;
							}
							goto IL_2F6;
						case 1U:
							goto IL_220;
						case 2U:
							\u200D\u202B\u206F\u202D\u200C\u206E\u200D\u206B\u200B\u200B\u206D\u200C\u200D\u206A\u200E\u200F\u202D\u206B\u202B\u200F\u206D\u200F\u206C\u200E\u206C\u206D\u202C\u202A\u202B\u200B\u200E\u202A\u202A\u200F\u202B\u200F\u206E\u202E\u206A\u200E\u202E.\u00A1\u0005d\u00B2\u00BC(\u206E\u202C\u202C\u200F\u202C\u206A\u200D\u206D\u206A\u202B\u206F\u206B\u200B\u200D\u202C\u206F\u206F\u206F\u206B\u206C\u206C\u206C\u202C\u200D\u202B\u206A\u200B\u206E\u200E\u206A\u202E\u206A\u202C\u200E\u206A\u200E\u206B\u202A\u206E\u206C\u202E.{\u0001\u009CG\u0085(\u200C\u200C\u206A\u206D\u206E\u202D\u200D\u200D\u206A\u200E\u200D\u206A\u202E\u206E\u202B\u200C\u200B\u202B\u206D\u206C\u202A\u200F\u206B\u206D\u206C\u206E\u200F\u206C\u206A\u206F\u206F\u206C\u202B\u206A\u206D\u200E\u202A\u200C\u206F\u200E\u202E.\u000BÃ>Ø\u00A4(\u202B\u206E\u200F\u202B\u200F\u200C\u200B\u200E\u202D\u202A\u202A\u200C\u206D\u200B\u206B\u206E\u200E\u202E\u206D\u202D\u206D\u202A\u202C\u206F\u200B\u200D\u202E\u202A\u202B\u206E\u202A\u202A\u206C\u202D\u202D\u202A\u206C\u206F\u206C\u206C\u202E.\u0002\u0096\u008DÌ\u0001(agent), <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(970143417), phraseForAgent, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1036563585)), Colors.Gray));
							num3 = -15484994;
							continue;
						case 3U:
							num3 = (int)(num4 * 87689647U ^ 2590422497U);
							continue;
						case 4U:
							phraseForAgent = this.GetPhraseForAgent(agent);
							num3 = (int)(((!\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(phraseForAgent)) ? 296972696U : 429499427U) ^ num4 * 3172001094U);
							continue;
						case 5U:
							num3 = (int)(num4 * 3794178854U ^ 3955255631U);
							continue;
						case 6U:
							num3 = (int)(num4 * 2344416206U ^ 3849991323U);
							continue;
						case 7U:
							goto IL_2C8;
						case 8U:
							flag2 = !\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.}\u0008\u00A6*\u0001(agent);
							goto IL_2F7;
						case 9U:
							flag3 = missionBehavior.IsSummonedTroop(agent);
							goto IL_3DB;
						case 10U:
						{
							bool flag4 = agent == \u202D\u206F\u200E\u202E\u202B\u200D\u206F\u200F\u200D\u202C\u206A\u202D\u206A\u202D\u202D\u202C\u206C\u202D\u206D\u202B\u206D\u200E\u200D\u206D\u200D\u202C\u206E\u200C\u206B\u206A\u206E\u206A\u200D\u200D\u202A\u202B\u202B\u206D\u200B\u200D\u202E.3,\u0005\u00AFö();
							num3 = ((!flag4) ? -1650232 : -373698193);
							continue;
						}
						case 11U:
							agent = enumerator.Current;
							if (agent != null)
							{
								num3 = -1110465734;
								continue;
							}
							goto IL_2F6;
						case 12U:
							num3 = ((\u202A\u206D\u206E\u200D\u202C\u200D\u200B\u206B\u200E\u200C\u200E\u200E\u206D\u202D\u200D\u206D\u200B\u202C\u200C\u206D\u206F\u206D\u206D\u206A\u202A\u202D\u206D\u202A\u200F\u200F\u202B\u206B\u202C\u206A\u200E\u202B\u200D\u206D\u202E\u202C\u202E.\u008A!:)c(agent) == \u202A\u200B\u202D\u206D\u206B\u200F\u200D\u202A\u200C\u206D\u200E\u202D\u206D\u200B\u202A\u206E\u206D\u200E\u202D\u200F\u200F\u206C\u206A\u206C\u202E\u200F\u202B\u206A\u200D\u202B\u202E\u206D\u202E\u202D\u200D\u200F\u202D\u206F\u206F\u200D\u202E.Ø<\u00B8\u0087\u00A9(\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5())) ? 2092780993 : -1377350039);
							continue;
						case 13U:
							num3 = -587056237;
							continue;
						case 14U:
							num3 = (int)(num4 * 270319233U ^ 2422383553U);
							continue;
						case 16U:
							num3 = 2047940467;
							continue;
						case 17U:
							num3 = 1430623731;
							continue;
						case 18U:
							num3 = (this._agentsWhoSpoke.Contains(agent) ? 1491441549 : -343865142);
							continue;
						case 19U:
						{
							CombatPhrasePopupView instance = CombatPhrasePopupView.Instance;
							if (instance == null)
							{
								goto Block_13;
							}
							instance.ShowPhrase(agent, phraseForAgent, 5f);
							num3 = -2009895022;
							continue;
						}
						case 20U:
							num3 = (int)(num4 * 3483623561U ^ 1446831636U);
							continue;
						case 21U:
							if (missionBehavior != null)
							{
								num3 = 1250039557;
								continue;
							}
							flag3 = false;
							goto IL_3DB;
						case 22U:
						{
							num5 = \u202C\u206A\u202B\u206A\u200E\u202A\u206E\u206E\u206D\u206F\u200C\u202C\u202B\u206A\u200F\u202D\u200D\u202B\u202A\u202E\u200E\u206F\u202A\u200D\u206D\u202A\u200B\u206A\u202C\u200D\u202B\u206B\u200C\u206D\u206E\u200F\u206D\u202B\u200F\u206A\u202E.\u0085\u00B4vüy(agent).Distance(v);
							bool flag5 = num5 <= 15f;
							num3 = ((!flag5) ? 2047940467 : 916616904);
							continue;
						}
						}
						goto Block_5;
						IL_2F7:
						bool flag6 = flag2;
						num3 = ((!flag6) ? -121659792 : 660126281);
						continue;
						IL_2F6:
						flag2 = true;
						goto IL_2F7;
						IL_3DB:
						num3 = (flag3 ? 655114276 : 2128362650);
					}
				}
				Block_5:
				goto IL_4AF;
				IL_220:
				this._agentsWhoSpoke.Add(agent);
				this._nextPhraseAllowedTime = \u206A\u200B\u200D\u206E\u200B\u206A\u200F\u200E\u206C\u200D\u200B\u200B\u200D\u206F\u202D\u206A\u202A\u206F\u202C\u200B\u202C\u200D\u206E\u200E\u200C\u206F\u206D\u202B\u200E\u202E\u200F\u206A\u206F\u206A\u206E\u200F\u202D\u206B\u206A\u202A\u202E.;hIMÅ(5f);
				this._logger.Log(\u202D\u200E\u206D\u206A\u200C\u200C\u206C\u202B\u206F\u202E\u202D\u202B\u206A\u200D\u206D\u206E\u202A\u206B\u202D\u200E\u206F\u206E\u200C\u206D\u202D\u206F\u200F\u206E\u206C\u202B\u206E\u206F\u206B\u202B\u202A\u206F\u206F\u200C\u200B\u202C\u202E.\u200C\u206A\u202D\u202E\u200B\u202C\u202D\u202D\u206F\u202A\u206D\u202D\u200C\u202A\u202A\u202A\u202B\u200D\u206B\u202D\u202C\u206C\u202D\u202D\u206B\u200F\u202E\u200D\u200E\u206A\u206F\u202D\u206A\u206A\u206B\u200E\u202A\u206F\u200F\u206B\u202E(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(505205739), new object[]
				{
					\u202B\u206E\u200F\u202B\u200F\u200C\u200B\u200E\u202D\u202A\u202A\u200C\u206D\u200B\u206B\u206E\u200E\u202E\u206D\u202D\u206D\u202A\u202C\u206F\u200B\u200D\u202E\u202A\u202B\u206E\u202A\u202A\u206C\u202D\u202D\u202A\u206C\u206F\u206C\u206C\u202E.\u0002\u0096\u008DÌ\u0001(agent),
					num5,
					phraseForAgent,
					5f
				}));
				return;
				Block_13:
				goto IL_220;
				IL_4AF:;
			}
		}

		// Token: 0x06000B1A RID: 2842 RVA: 0x000C7000 File Offset: 0x000C5200
		private string GetPhraseForAgent(Agent agent)
		{
			CharacterObject characterObject = \u200B\u206A\u200E\u202A\u206B\u202D\u206A\u200B\u206C\u206C\u206E\u206C\u206D\u206A\u200F\u206F\u206A\u202C\u206A\u200F\u206C\u200F\u202E\u202E\u206A\u200C\u202D\u206C\u200C\u206C\u202A\u202D\u206D\u202A\u200C\u200D\u200E\u202D\u206C\u202C\u202E.\u0081\u001B9S\u00B0(agent) as CharacterObject;
			bool flag = characterObject == null;
			if (flag)
			{
				goto IL_1F;
			}
			goto IL_107;
			int num2;
			List<string> list;
			string result;
			for (;;)
			{
				IL_24:
				int num = num2;
				uint num3;
				bool flag2;
				List<string> list2;
				bool flag4;
				switch ((num3 = (uint)((num + (~(-2063239195 * 2043068217) ^ ~-43134043)) * -1226664135)) % 17U)
				{
				case 0U:
					flag2 = !Enumerable.Any<string>(list);
					goto IL_278;
				case 1U:
					result = null;
					num2 = (int)(num3 * 238335833U ^ 1533792413U);
					continue;
				case 3U:
					if (list != null)
					{
						num2 = 453946353;
						continue;
					}
					flag2 = true;
					goto IL_278;
				case 4U:
					if (\u202D\u200D\u202B\u202E\u202D\u202B\u206C\u206F\u202E\u200D\u202A\u202B\u202E\u200D\u202C\u202E\u200C\u206D\u202B\u206E\u202E\u202D\u202D\u206E\u200F\u206D\u200B\u206D\u202B\u206B\u202B\u206F\u202E\u206C\u206C\u200D\u206C\u200B\u206C\u200F\u202E.\u001E\u0083J\u009Fn(characterObject) != Occupation.Mercenary)
					{
						num2 = (int)(num3 * 3872430317U ^ 710869915U);
						continue;
					}
					goto IL_1E2;
				case 5U:
					list = this._femalePhrases;
					num2 = (int)(num3 * 3924020178U ^ 1784710770U);
					continue;
				case 6U:
					if (\u202D\u200D\u202B\u202E\u202D\u202B\u206C\u206F\u202E\u200D\u202A\u202B\u202E\u200D\u202C\u202E\u200C\u206D\u202B\u206E\u202E\u202D\u202D\u206E\u200F\u206D\u200B\u206D\u202B\u206B\u202B\u206F\u202E\u206C\u206C\u200D\u206C\u200B\u206C\u200F\u202E.\u001E\u0083J\u009Fn(characterObject) != Occupation.Soldier)
					{
						num2 = 1040266195;
						continue;
					}
					goto IL_1E2;
				case 7U:
				{
					bool flag3 = \u202C\u206A\u200C\u202B\u206A\u202B\u206C\u200D\u200E\u200E\u200E\u206F\u202A\u202A\u206D\u202E\u206B\u206F\u206E\u202E\u206A\u200D\u200F\u206F\u206D\u200E\u200C\u206B\u206B\u206D\u206C\u200E\u200D\u202B\u202A\u202D\u200B\u200F\u206B\u202E.Óñ\u001FxÌ(characterObject);
					num2 = ((!flag3) ? 1109901693 : -76889257);
					continue;
				}
				case 8U:
					list2 = this._malePanicPhrases;
					goto IL_A4;
				case 9U:
				{
					List<string> list3 = Enumerable.ToList<string>(Enumerable.Where<string>(list, (string p) => !this._usedPhrases.Contains(p)));
					num2 = ((!Enumerable.Any<string>(list3)) ? 778053963 : -783868860);
					continue;
				}
				case 10U:
				{
					Random random = \u202B\u206A\u206C\u206A\u200E\u200B\u206F\u200D\u202E\u206F\u206B\u206B\u206B\u202E\u206B\u202D\u202C\u202A\u202C\u206B\u200C\u200E\u200E\u206C\u202E\u202D\u200B\u200F\u206F\u206A\u200D\u206C\u202B\u202B\u200D\u202A\u206D\u206A\u202B\u206D\u202E.þ\u001Dâ`O(\u202A\u206D\u202D\u200C\u202D\u200D\u206F\u206E\u200E\u200D\u202C\u202D\u206C\u200F\u202C\u202A\u202A\u202B\u202D\u202A\u206E\u206B\u200E\u206C\u206C\u200F\u202E\u206E\u206A\u206E\u202A\u206D\u200F\u202C\u202A\u206D\u206C\u202D\u206F\u202A\u202E.ã\u0009ß)ã(agent) + this._usedPhrases.Count);
					List<string> list3;
					int index = \u200C\u200B\u200F\u206B\u200B\u206A\u200B\u202A\u202E\u206D\u206E\u202D\u206B\u200F\u206E\u200B\u206C\u202B\u202B\u202B\u202D\u206B\u200E\u200B\u202E\u200C\u200F\u206C\u206F\u200F\u200E\u200D\u206C\u200B\u202B\u202B\u206F\u202D\u206B\u206D\u202E.b\u0082ôÄ\u00B4(random, list3.Count);
					string text = list3[index];
					this._usedPhrases.Add(text);
					result = text;
					num2 = -182198494;
					continue;
				}
				case 11U:
					list = this._childPhrases;
					num2 = (int)(num3 * 448244806U ^ 3708815952U);
					continue;
				case 12U:
					flag4 = (\u202D\u200D\u202B\u202E\u202D\u202B\u206C\u206F\u202E\u200D\u202A\u202B\u202E\u200D\u202C\u202E\u200C\u206D\u202B\u206E\u202E\u202D\u202D\u206E\u200F\u206D\u200B\u206D\u202B\u206B\u202B\u206F\u202E\u206C\u206C\u200D\u206C\u200B\u206C\u200F\u202E.\u001E\u0083J\u009Fn(characterObject) == Occupation.Bandit);
					goto IL_1E3;
				case 13U:
					result = null;
					num2 = (int)(num3 * 1572270164U ^ 362030014U);
					continue;
				case 14U:
					result = null;
					num2 = (int)(num3 * 305258194U ^ 3894041462U);
					continue;
				case 15U:
					goto IL_107;
				case 16U:
					goto IL_1F;
				}
				break;
				IL_A4:
				list = list2;
				num2 = -1451434004;
				continue;
				IL_1E3:
				if (!flag4)
				{
					num2 = 148864414;
					continue;
				}
				list2 = this._maleFightPhrases;
				goto IL_A4;
				IL_1E2:
				flag4 = true;
				goto IL_1E3;
				IL_278:
				bool flag5 = flag2;
				num2 = ((!flag5) ? -109013536 : 1113537847);
			}
			return result;
			IL_1F:
			num2 = 1974073618;
			goto IL_24;
			IL_107:
			list = null;
			num2 = ((\u202D\u200B\u206C\u202C\u202C\u202B\u200C\u206C\u200F\u206F\u200C\u202E\u206F\u200D\u202C\u206F\u200B\u200D\u202E\u202C\u202A\u206F\u200F\u206C\u202C\u200F\u206F\u200C\u206D\u200B\u200C\u206B\u206C\u202D\u200B\u200E\u202D\u200D\u200C\u200F\u202E.ùP\u0093ðë(characterObject) < 18f) ? -1140187262 : 1745057856);
			goto IL_24;
		}

		// Token: 0x06000B1B RID: 2843 RVA: 0x000C72B8 File Offset: 0x000C54B8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void OnRemoveBehavior()
		{
			\u200C\u206B\u202D\u206D\u202A\u200E\u200B\u206F\u202A\u206F\u202C\u206D\u200E\u206F\u202E\u200E\u206F\u206D\u202A\u202B\u200C\u202E\u202E\u200C\u200D\u206C\u206B\u202D\u206E\u200F\u200E\u202B\u200B\u206C\u202A\u202E\u202C\u202C\u206D\u206A\u202E.M<ÕÀà(this);
			try
			{
				this._agentsWhoSpoke.Clear();
				this._usedPhrases.Clear();
				this._logger.Log(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1340548218));
			}
			catch (Exception ex)
			{
				this._logger.LogError(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-343892112), \u200D\u202C\u200D\u200B\u200B\u206F\u202A\u202B\u200D\u206E\u206C\u206E\u206A\u200E\u206E\u206F\u202C\u200E\u200E\u200F\u206B\u200F\u206F\u202C\u200E\u200C\u200F\u200D\u200D\u200B\u200D\u202B\u206A\u206B\u206F\u202A\u200C\u206D\u206F\u206C\u202E.Wý\u00B9\u009Bh(ex), ex);
			}
		}

		// Token: 0x040006CA RID: 1738
		private readonly CombatSituationAnalysis _analysis;

		// Token: 0x040006CB RID: 1739
		private List<string> _malePanicPhrases;

		// Token: 0x040006CC RID: 1740
		private List<string> _maleFightPhrases;

		// Token: 0x040006CD RID: 1741
		private List<string> _femalePhrases;

		// Token: 0x040006CE RID: 1742
		private List<string> _childPhrases;

		// Token: 0x040006CF RID: 1743
		private readonly SettlementCombatLogger _logger;

		// Token: 0x040006D0 RID: 1744
		private HashSet<Agent> _agentsWhoSpoke = new HashSet<Agent>();

		// Token: 0x040006D1 RID: 1745
		private HashSet<string> _usedPhrases = new HashSet<string>();

		// Token: 0x040006D2 RID: 1746
		private const float ACTIVATION_DISTANCE = 15f;

		// Token: 0x040006D3 RID: 1747
		private const float PHRASE_COOLDOWN = 5f;

		// Token: 0x040006D4 RID: 1748
		private MissionTime _nextPhraseAllowedTime = \u206C\u206F\u206F\u206F\u206C\u202C\u206B\u206A\u202B\u206F\u206C\u200D\u206F\u206E\u200B\u206E\u202A\u202C\u200F\u202C\u200E\u206A\u206F\u206F\u206B\u206A\u206D\u200C\u206E\u206E\u200C\u202C\u202A\u206C\u200C\u202C\u200F\u200E\u202D\u202E.ÎÖÑQ\u0099();

		// Token: 0x040006D5 RID: 1749
		private bool _initialized = false;

		// Token: 0x040006D6 RID: 1750
		private float _tickTimer = 0f;
	}
}
