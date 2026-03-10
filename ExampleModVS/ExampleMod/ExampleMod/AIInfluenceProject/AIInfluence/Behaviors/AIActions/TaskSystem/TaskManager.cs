using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;
using \u202A\u202C\u202A\u202E\u206A\u206D\u202C\u200D\u206D\u200B\u200C\u206A\u206A\u202D\u202A\u206A\u202D\u206F\u206A\u206A\u202D\u206A\u206B\u206E\u206A\u200E\u200C\u202E\u200B\u202C\u206A\u202A\u206C\u202D\u206C\u202A\u200D\u202A\u206F\u200D\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.Behaviors.AIActions.TaskSystem
{
	// Token: 0x02000317 RID: 791
	public class TaskManager : CampaignBehaviorBase
	{
		// Token: 0x17000405 RID: 1029
		// (get) Token: 0x06001635 RID: 5685 RVA: 0x0015090C File Offset: 0x0014EB0C
		public static TaskManager Instance
		{
			get
			{
				bool flag = \u200E\u202D\u202C\u206F\u206A\u202A\u202A\u200F\u202D\u206B\u206C\u200E\u202B\u200C\u202E\u202B\u200E\u202E\u206E\u200C\u206C\u202E\u200F\u206C\u200D\u206B\u200C\u206D\u206B\u206D\u200B\u200B\u200B\u202A\u202E\u200B\u202C\u202C\u206D\u206C\u202E.ã`ÿ\u00ADÇ() != null;
				if (flag)
				{
					goto IL_12;
				}
				goto IL_48;
				int num2;
				TaskManager instance;
				for (;;)
				{
					IL_17:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(-(-(-num)) ^ -40828275)) % 6U)
					{
					case 0U:
					{
						TaskManager campaignBehavior;
						TaskManager._instance = campaignBehavior;
						instance = TaskManager._instance;
						num2 = (int)(num3 * 1776472444U ^ 3564746880U);
						continue;
					}
					case 1U:
					{
						TaskManager campaignBehavior = \u200E\u202D\u202C\u206F\u206A\u202A\u202A\u200F\u202D\u206B\u206C\u200E\u202B\u200C\u202E\u202B\u200E\u202E\u206E\u200C\u206C\u202E\u200F\u206C\u200D\u206B\u200C\u206D\u206B\u206D\u200B\u200B\u200B\u202A\u202E\u200B\u202C\u202C\u206D\u206C\u202E.ã`ÿ\u00ADÇ().GetCampaignBehavior<TaskManager>();
						num2 = (int)(((campaignBehavior != null) ? 4179239649U : 2609660849U) ^ num3 * 3153061896U);
						continue;
					}
					case 2U:
						goto IL_12;
					case 3U:
						goto IL_48;
					case 4U:
						num2 = 656573098;
						continue;
					}
					break;
				}
				return instance;
				IL_12:
				num2 = 299552620;
				goto IL_17;
				IL_48:
				instance = TaskManager._instance;
				num2 = 551629080;
				goto IL_17;
			}
		}

		// Token: 0x06001636 RID: 5686 RVA: 0x001509CC File Offset: 0x0014EBCC
		public TaskManager()
		{
			this._activeTasks = new Dictionary<string, HeroTask>();
			this._completedTasks = new Dictionary<string, List<HeroTask>>();
		}

		// Token: 0x06001637 RID: 5687 RVA: 0x001509F8 File Offset: 0x0014EBF8
		public override void RegisterEvents()
		{
			\u200E\u200E\u200B\u200B\u202B\u206B\u206F\u200C\u202A\u206A\u206A\u200B\u202B\u200C\u200F\u200F\u206C\u202D\u206D\u202A\u200B\u202A\u200F\u202C\u206D\u206B\u202D\u200F\u202D\u206F\u202A\u200D\u202A\u202A\u200C\u206F\u206C\u200B\u200E\u202B\u202E.\u00B4\u008Aòúr(\u206F\u202E\u200E\u206D\u202A\u202D\u200B\u200C\u200B\u206E\u202D\u206C\u200E\u200C\u200F\u206B\u206E\u202E\u202D\u206A\u202A\u200F\u200F\u206B\u202A\u202C\u206C\u206D\u206A\u202E\u206C\u202D\u200B\u200B\u202D\u206F\u206D\u206C\u200F\u202E.bØy\u000DU(), this, new Action(this.OnHourlyTick));
			\u200E\u200E\u200B\u200B\u202B\u206B\u206F\u200C\u202A\u206A\u206A\u200B\u202B\u200C\u200F\u200F\u206C\u202D\u206D\u202A\u200B\u202A\u200F\u202C\u206D\u206B\u202D\u200F\u202D\u206F\u202A\u200D\u202A\u202A\u200C\u206F\u206C\u200B\u200E\u202B\u202E.\u00B4\u008Aòúr(\u206F\u202E\u200E\u206D\u202A\u202D\u200B\u200C\u200B\u206E\u202D\u206C\u200E\u200C\u200F\u206B\u206E\u202E\u202D\u206A\u202A\u200F\u200F\u206B\u202A\u202C\u206C\u206D\u206A\u202E\u206C\u202D\u200B\u200B\u202D\u206F\u206D\u206C\u200F\u202E.Äf\u008EÐU(), this, new Action(this.OnDailyTick));
		}

		// Token: 0x06001638 RID: 5688 RVA: 0x00150A4C File Offset: 0x0014EC4C
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override void SyncData(IDataStore dataStore)
		{
			dataStore.SyncData<Dictionary<string, HeroTask>>(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1081901359), ref this._activeTasks);
			dataStore.SyncData<Dictionary<string, List<HeroTask>>>(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-687926793), ref this._completedTasks);
			bool flag = \u206D\u200B\u206D\u202A\u206A\u206A\u206D\u202E\u202B\u200E\u206D\u200E\u202A\u206B\u206E\u206E\u202E\u202C\u200E\u200E\u202C\u206D\u206F\u200C\u206A\u202B\u200C\u200E\u206C\u202D\u202E\u206D\u200E\u200D\u202E\u202E\u200C\u202B\u206A\u200D\u202E._&\\u0086n(dataStore);
			if (flag)
			{
				for (;;)
				{
					IL_42:
					int num = -251335953;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-(uint)(~(-num2) * 232260261))) % 3U)
						{
						case 1U:
							this.RestoreTasksAfterLoad();
							num = (int)(num3 * 2921199646U ^ 3518617221U);
							continue;
						case 2U:
							goto IL_42;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06001639 RID: 5689 RVA: 0x00150ADC File Offset: 0x0014ECDC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public HeroTask CreateTask(Hero hero, string description = null)
		{
			HeroTask result;
			for (;;)
			{
				IL_01:
				int num = 414954790;
				for (;;)
				{
					int num2 = num;
					uint num3;
					bool flag3;
					bool flag4;
					HeroTask heroTask;
					switch ((num3 = (uint)(-(uint)(~(num2 ^ ~(-1645896943) + ~1487212508) + -817746839))) % 16U)
					{
					case 0U:
					{
						bool flag2;
						bool flag = flag2;
						num = ((!flag) ? -348075878 : 76293330);
						continue;
					}
					case 1U:
						result = null;
						num = (int)(num3 * 2756359176U ^ 3220821461U);
						continue;
					case 2U:
						flag3 = (\u202D\u202B\u206F\u206D\u206E\u206E\u206F\u200B\u200F\u206D\u200C\u206F\u202D\u206C\u200C\u206E\u206D\u202C\u200F\u202D\u202E\u206F\u206E\u202B\u202D\u200D\u206A\u200D\u206C\u202B\u200C\u202E\u206B\u206D\u206B\u206A\u200E\u202C\u202D\u202E.ÂTeÕ\u00AD(\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero)) != null);
						goto IL_2F8;
					case 3U:
						num = 497375685;
						continue;
					case 5U:
						flag4 = (\u202D\u206D\u200B\u202E\u206D\u202C\u206D\u206F\u202A\u202B\u202E\u202E\u202E\u200B\u206C\u200D\u200B\u202A\u206E\u202E\u200F\u200B\u202D\u200E\u202D\u206C\u206B\u200F\u200F\u206D\u200E\u202E\u200E\u200D\u206F\u202A\u206F\u200E\u202A\u206D\u202E.É}Â\u00AE\u0009(\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero)) != null);
						goto IL_2BA;
					case 6U:
						goto IL_F4;
					case 7U:
						goto IL_312;
					case 8U:
						if (\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero) != null)
						{
							num = -349611492;
							continue;
						}
						flag4 = false;
						goto IL_2BA;
					case 9U:
						goto IL_180;
					case 10U:
					{
						Army army;
						MobileParty mobileParty = \u200B\u206D\u206D\u206F\u206E\u200D\u200F\u202A\u206A\u200F\u206C\u206D\u206E\u206C\u206E\u206A\u206C\u200B\u206D\u202B\u200E\u200E\u202E\u206B\u206C\u202E\u206A\u206A\u202D\u200B\u206A\u206E\u202D\u202C\u206C\u200C\u206E\u206C\u202D\u200E\u202E.\u00BD\u0088å8&(army);
						string text;
						if (mobileParty == null)
						{
							text = null;
						}
						else
						{
							Hero hero2 = \u200E\u200C\u206E\u200D\u206F\u200B\u200C\u200B\u206F\u202A\u206C\u206A\u202E\u206F\u202A\u200D\u202A\u206C\u206B\u200B\u206C\u200E\u206E\u202D\u202C\u202D\u200E\u200C\u200B\u200E\u206D\u206C\u202B\u202B\u206A\u206B\u206C\u202A\u200B\u206A\u202E.\u202D\u206E\u200C\u206E\u202C\u202B\u200E\u202E\u206B\u206A\u206E\u206C\u202E\u200D\u206F\u202C\u200C\u202D\u202A\u200D\u200E\u200F\u202E\u202B\u200E\u202A\u202C\u206F\u202B\u200B\u200D\u206B\u206E\u206B\u202E\u202A\u200C\u200F\u200C\u200F\u202E(mobileParty);
							if (hero2 == null)
							{
								text = null;
							}
							else
							{
								TextObject textObject = \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.\u206E\u202B\u200F\u202D\u206C\u206E\u202C\u206B\u206D\u202D\u202E\u200E\u206D\u200E\u200F\u206C\u206D\u206A\u200D\u206F\u202D\u202E\u200C\u206D\u206A\u202E\u206D\u206C\u200D\u200C\u200E\u202B\u202D\u206D\u200B\u202D\u206D\u200B\u206A\u206B\u202E(hero2);
								text = ((textObject != null) ? \u206F\u200D\u202B\u206A\u200F\u200D\u202B\u200B\u206C\u206B\u202E\u206E\u200F\u200D\u200F\u206D\u200B\u200C\u202C\u206D\u202A\u206C\u200C\u200B\u202A\u202E\u200E\u202B\u202C\u202E\u202E\u200E\u206D\u202B\u206E\u206A\u200D\u202D\u206E\u200E\u202E.\u200B\u206D\u206E\u200E\u200C\u200B\u202C\u200E\u200C\u206A\u200F\u202B\u206D\u206E\u202E\u202E\u200C\u202D\u202E\u202B\u206D\u206B\u200F\u202B\u200B\u202C\u202E\u200F\u202C\u206A\u200F\u200E\u202C\u206F\u202E\u200E\u206D\u202C\u206B\u206C\u202E(textObject) : null);
							}
						}
						string text2 = text ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1085054637);
						AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
						if (instance == null)
						{
							goto IL_180;
						}
						instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-128460926), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), text2));
						num = -242314304;
						continue;
					}
					case 11U:
					{
						AIInfluenceBehavior instance2 = AIInfluenceBehavior.Instance;
						if (instance2 == null)
						{
							goto IL_F4;
						}
						instance2.LogMessage(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-732867944), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
						num = -23337217;
						continue;
					}
					case 12U:
					{
						this.CancelTask(hero);
						heroTask = new HeroTask(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), description);
						this._activeTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)] = heroTask;
						AIInfluenceBehavior instance3 = AIInfluenceBehavior.Instance;
						if (instance3 == null)
						{
							goto IL_312;
						}
						instance3.LogMessage(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.\u200D\u206D\u206F\u202A\u200B\u202B\u206B\u200F\u202B\u202E\u202B\u202E\u206E\u200F\u202C\u200F\u202A\u206D\u200D\u206C\u202A\u200F\u202D\u206F\u206E\u206C\u206B\u206D\u202E\u200E\u206D\u206E\u206C\u206B\u200F\u200D\u202A\u202A\u202C\u200F\u202E(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1132416717), heroTask.TaskId, \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), description ?? <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1891432372)));
						num = 133013134;
						continue;
					}
					case 13U:
						goto IL_01;
					case 14U:
					{
						Army army = \u202D\u206D\u200B\u202E\u206D\u202C\u206D\u206F\u202A\u202B\u202E\u202E\u202E\u200B\u206C\u200D\u200B\u202A\u206E\u202E\u200F\u200B\u202D\u200E\u202D\u206C\u206B\u200F\u200F\u206D\u200E\u202E\u200E\u200D\u206F\u202A\u206F\u200E\u202A\u206D\u202E.É}Â\u00AE\u0009(\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero));
						bool flag2 = \u200B\u206D\u206D\u206F\u206E\u200D\u200F\u202A\u206A\u200F\u206C\u206D\u206E\u206C\u206E\u206A\u206C\u200B\u206D\u202B\u200E\u200E\u202E\u206B\u206C\u202E\u206A\u206A\u202D\u200B\u206A\u206E\u202D\u202C\u206C\u200C\u206E\u206C\u202D\u200E\u202E.\u00BD\u0088å8&(army) == \u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero);
						if (!flag2)
						{
							num = (int)(num3 * 1503766533U ^ 182555917U);
							continue;
						}
						flag3 = false;
						goto IL_2F8;
					}
					case 15U:
						num = (int)(((hero == null) ? 806584947U : 120437546U) ^ num3 * 3931871045U);
						continue;
					}
					return result;
					IL_F4:
					num = -348075878;
					continue;
					IL_180:
					\u200C\u206C\u200F\u206E\u200C\u206E\u206B\u206D\u200D\u200D\u206C\u202B\u206E\u200C\u202E\u202D\u206B\u200F\u200F\u202C\u200B\u200C\u200C\u202E\u206B\u200D\u206C\u206F\u200B\u202B\u206A\u200F\u202C\u206B\u200C\u206D\u202D\u206E\u200D\u206F\u202E.ÑE\u00F7\u00D7ö(\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(hero), null);
					num = -348075878;
					continue;
					IL_2BA:
					num = (flag4 ? -529829385 : 497375685);
					continue;
					IL_2F8:
					bool flag5 = flag3;
					num = ((!flag5) ? 1071490105 : 465309715);
					continue;
					IL_312:
					result = heroTask;
					num = -596589603;
				}
			}
			return result;
		}

		// Token: 0x0600163A RID: 5690 RVA: 0x00150E08 File Offset: 0x0014F008
		public HeroTask GetActiveTask(Hero hero)
		{
			bool flag = hero == null;
			if (flag)
			{
				goto IL_09;
			}
			goto IL_7D;
			int num2;
			HeroTask result;
			HeroTask heroTask;
			for (;;)
			{
				IL_0E:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((~(-1457969365 + -1852360859) - (num ^ 17000266 - 2127332541 + --39169870 + (-820603248 + ~827989008))) * 1960638121 + -76857827)) % 7U)
				{
				case 0U:
					goto IL_CC;
				case 1U:
					result = null;
					num2 = (int)(num3 * 2104666143U ^ 2829057149U);
					continue;
				case 2U:
					goto IL_7D;
				case 3U:
					result = null;
					num2 = 1736142687;
					continue;
				case 4U:
					goto IL_09;
				case 6U:
					result = heroTask;
					num2 = (int)(num3 * 4060881278U ^ 2759098707U);
					continue;
				}
				break;
			}
			return result;
			IL_CC:
			bool flag2 = heroTask.IsActive();
			goto IL_D5;
			IL_09:
			num2 = 1609181452;
			goto IL_0E;
			IL_7D:
			if (this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask))
			{
				num2 = -1772008092;
				goto IL_0E;
			}
			flag2 = false;
			IL_D5:
			num2 = (flag2 ? 1091589760 : -17000556);
			goto IL_0E;
		}

		// Token: 0x0600163B RID: 5691 RVA: 0x00150F04 File Offset: 0x0014F104
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool CancelTask(Hero hero)
		{
			bool flag = hero == null;
			if (flag)
			{
				goto IL_0C;
			}
			goto IL_15D;
			int num2;
			HeroTask heroTask;
			bool result;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(2030945635 - (num - (-1508114511 + 297811717 - -1235305613 - (~-35378337 ^ -1127570313 + 1279874569))))) % 10U)
				{
				case 0U:
				{
					heroTask.Cancel();
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_78;
					}
					instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-2099165676), heroTask.TaskId, \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
					num2 = 313615715;
					continue;
				}
				case 1U:
					goto IL_15D;
				case 2U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)] = new List<HeroTask>();
					num2 = (int)(num3 * 1752092036U ^ 253224834U);
					continue;
				case 4U:
					this._activeTasks.Remove(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
					result = true;
					num2 = (int)(num3 * 3619705340U ^ 1055946869U);
					continue;
				case 5U:
					result = false;
					num2 = (int)(num3 * 1028979251U ^ 1651331938U);
					continue;
				case 6U:
					result = false;
					num2 = -193332931;
					continue;
				case 7U:
					goto IL_78;
				case 8U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)].Add(heroTask);
					num2 = 278432728;
					continue;
				case 9U:
					goto IL_0C;
				}
				break;
				IL_78:
				bool flag2 = !this._completedTasks.ContainsKey(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
				num2 = ((!flag2) ? 1032120954 : 1554024520);
			}
			return result;
			IL_0C:
			num2 = -261054623;
			goto IL_11;
			IL_15D:
			bool flag3 = this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask);
			num2 = ((!flag3) ? 80125266 : 801268012);
			goto IL_11;
		}

		// Token: 0x0600163C RID: 5692 RVA: 0x001510DC File Offset: 0x0014F2DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void CompleteTask(Hero hero)
		{
			bool flag = hero == null;
			if (flag)
			{
				goto IL_0C;
			}
			goto IL_1C2;
			int num2;
			HeroTask heroTask;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((943101068 - 210947230) * -2121435613 + (-1053186406 * 1746774745 + --1072708029) - num + ~(~-1202929215) - -477428302 ^ -321203581)) % 10U)
				{
				case 0U:
					heroTask.Status = TaskStatus.Completed;
					num2 = (int)(num3 * 2759375430U ^ 4285260566U);
					continue;
				case 1U:
					num2 = (int)(num3 * 3407858980U ^ 2283762229U);
					continue;
				case 3U:
				{
					heroTask.CompletedTime = new CampaignTime?(\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ());
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_160;
					}
					instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1503694177), heroTask.TaskId, \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
					num2 = -2119671583;
					continue;
				}
				case 4U:
					goto IL_160;
				case 5U:
					goto IL_0C;
				case 6U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)] = new List<HeroTask>();
					num2 = (int)(num3 * 3975101814U ^ 1464636488U);
					continue;
				case 7U:
					goto IL_1C2;
				case 8U:
					num2 = (int)(num3 * 1139552579U ^ 255107199U);
					continue;
				case 9U:
					this._completedTasks[\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero)].Add(heroTask);
					this._activeTasks.Remove(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
					num2 = 919546399;
					continue;
				}
				break;
				IL_160:
				bool flag2 = !this._completedTasks.ContainsKey(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero));
				num2 = ((!flag2) ? 2025821064 : -1867461443);
			}
			return;
			IL_0C:
			num2 = 1464634616;
			goto IL_11;
			IL_1C2:
			num2 = (this._activeTasks.TryGetValue(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(hero), out heroTask) ? 446930147 : 508845113);
			goto IL_11;
		}

		// Token: 0x0600163D RID: 5693 RVA: 0x001512DC File Offset: 0x0014F4DC
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool MoveToNextStep(Hero hero)
		{
			HeroTask activeTask = this.GetActiveTask(hero);
			bool flag = activeTask == null;
			if (flag)
			{
				goto IL_14;
			}
			goto IL_96;
			int num2;
			bool flag2;
			bool result;
			for (;;)
			{
				IL_19:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)((num ^ -(~1569368227 - ~1189540525)) - 328755922))) % 8U)
				{
				case 0U:
					goto IL_96;
				case 1U:
				{
					AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
					if (instance == null)
					{
						goto IL_8E;
					}
					instance.LogMessage(\u202D\u200E\u206D\u206A\u200C\u200C\u206C\u202B\u206F\u202E\u202D\u202B\u206A\u200D\u206D\u206E\u202A\u206B\u202D\u200E\u206F\u206E\u200C\u206D\u202D\u206F\u200F\u206E\u206C\u202B\u206E\u206F\u206B\u202B\u202A\u206F\u206F\u200C\u200B\u202C\u202E.\u200C\u206A\u202D\u202E\u200B\u202C\u202D\u202D\u206F\u202A\u206D\u202D\u200C\u202A\u202A\u202A\u202B\u200D\u206B\u202D\u202C\u206C\u202D\u202D\u206B\u200F\u202E\u200D\u200E\u206A\u206F\u202D\u206A\u206A\u206B\u200E\u202A\u206F\u200F\u206B\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(216369203), new object[]
					{
						activeTask.TaskId,
						\u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero),
						activeTask.CurrentStepIndex + 1,
						activeTask.Steps.Count
					}));
					num2 = 412743186;
					continue;
				}
				case 2U:
					goto IL_14;
				case 3U:
					result = flag2;
					num2 = -243763;
					continue;
				case 4U:
					this.CompleteTask(hero);
					num2 = (int)(num3 * 4001743069U ^ 1051980141U);
					continue;
				case 5U:
					result = false;
					num2 = (int)(num3 * 1766010365U ^ 3998121332U);
					continue;
				case 6U:
					goto IL_8E;
				}
				break;
				IL_8E:
				num2 = 130636441;
			}
			return result;
			IL_14:
			num2 = -579339885;
			goto IL_19;
			IL_96:
			flag2 = activeTask.MoveToNextStep();
			bool flag3 = !flag2;
			num2 = ((!flag3) ? -1150278985 : 499867064);
			goto IL_19;
		}

		// Token: 0x0600163E RID: 5694 RVA: 0x00151420 File Offset: 0x0014F620
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void OnHourlyTick()
		{
			List<HeroTask> list = new List<HeroTask>(this._activeTasks.Values);
			using (List<HeroTask>.Enumerator enumerator = list.GetEnumerator())
			{
				for (;;)
				{
					IL_164:
					int num = (!enumerator.MoveNext()) ? -1805074128 : 951327626;
					for (;;)
					{
						int num2 = num;
						uint num3;
						Hero hero;
						bool flag2;
						switch ((num3 = (uint)(-(uint)((-629697965 - (num2 ^ -(-853790590 + -413209183 - (-162461911 - -291654494)))) * 552261363))) % 14U)
						{
						case 0U:
						{
							HeroTask heroTask;
							hero = heroTask.GetHero();
							bool flag = hero != null;
							num = (int)(((!flag) ? 2888476044U : 3834563643U) ^ num3 * 2297961954U);
							continue;
						}
						case 1U:
							num = 1081029378;
							continue;
						case 2U:
							num = (int)(num3 * 2186744059U ^ 1061983590U);
							continue;
						case 4U:
							num = 951327626;
							continue;
						case 5U:
							goto IL_164;
						case 6U:
						{
							TaskStep currentStep;
							flag2 = (currentStep.StepType == TaskStepType.WaitInSettlement);
							goto IL_1A0;
						}
						case 7U:
						{
							TaskStep currentStep;
							num = (int)((currentStep.IsWaitCompleted() ? 1885717424U : 3297782639U) ^ num3 * 2918882937U);
							continue;
						}
						case 8U:
						{
							AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
							if (instance == null)
							{
								goto IL_1F6;
							}
							HeroTask heroTask;
							instance.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(-1639095247), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), heroTask.CurrentStepIndex + 1));
							num = -961653909;
							continue;
						}
						case 9U:
							num = 183554909;
							continue;
						case 10U:
							goto IL_1F6;
						case 11U:
						{
							HeroTask heroTask = enumerator.Current;
							bool flag3 = !heroTask.IsActive();
							num = ((!flag3) ? 729452868 : -1617797857);
							continue;
						}
						case 12U:
							num = 7517262;
							continue;
						case 13U:
						{
							HeroTask heroTask;
							TaskStep currentStep = heroTask.GetCurrentStep();
							if (currentStep != null)
							{
								num = 2124953197;
								continue;
							}
							flag2 = false;
							goto IL_1A0;
						}
						}
						goto Block_3;
						IL_1A0:
						bool flag4 = flag2;
						num = ((!flag4) ? 183554909 : -726460722);
						continue;
						IL_1F6:
						this.MoveToNextStep(hero);
						num = -758358808;
					}
				}
				Block_3:;
			}
		}

		// Token: 0x0600163F RID: 5695 RVA: 0x000417F4 File Offset: 0x0003F9F4
		private void OnDailyTick()
		{
		}

		// Token: 0x06001640 RID: 5696 RVA: 0x00151664 File Offset: 0x0014F864
		[MethodImpl(MethodImplOptions.NoInlining)]
		public void RestoreTasksAfterLoad()
		{
			AIInfluenceBehavior instance = AIInfluenceBehavior.Instance;
			if (instance != null)
			{
				instance.LogMessage(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1769757270), this._activeTasks.Count));
			}
			using (List<KeyValuePair<string, HeroTask>>.Enumerator enumerator = Enumerable.ToList<KeyValuePair<string, HeroTask>>(this._activeTasks).GetEnumerator())
			{
				for (;;)
				{
					IL_2F5:
					int num = (!enumerator.MoveNext()) ? 674791548 : -1286868238;
					for (;;)
					{
						int num2 = num;
						uint num3;
						bool flag;
						switch ((num3 = (uint)(~(uint)(~(num2 - (2076157337 + -1200971951 + 773334045 * -1322237388 - (-1269533353 * 200006524 - (739991023 - -1855878059)))) - (-1806837018 - 456546845)))) % 17U)
						{
						case 0U:
							goto IL_1F7;
						case 1U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E;
							this._activeTasks.Remove(u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Key);
							num = (int)(num3 * 3393279642U ^ 3453498071U);
							continue;
						}
						case 2U:
						{
							TaskStep currentStep;
							if (currentStep.StepType == TaskStepType.WaitNearSettlement)
							{
								num = (int)(num3 * 3718061482U ^ 2586367486U);
								continue;
							}
							goto IL_17A;
						}
						case 3U:
						{
							HeroTask value;
							num = (int)(((!value.IsActive()) ? 314138310U : 4184945060U) ^ num3 * 2551845174U);
							continue;
						}
						case 4U:
						{
							TaskStep currentStep;
							flag = (currentStep.WaitUntilTime == null);
							goto IL_17B;
						}
						case 5U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E = new TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E();
							u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E = enumerator.Current;
							Hero hero = Enumerable.FirstOrDefault<Hero>(\u206C\u206C\u206D\u200C\u206B\u200D\u202E\u206E\u206B\u200D\u202E\u206C\u200E\u206F\u202A\u202D\u200B\u200F\u206A\u202D\u200D\u202A\u206B\u206B\u206C\u202A\u206E\u200B\u202D\u206C\u206E\u200C\u202E\u206C\u206E\u200F\u206D\u200E\u200E\u202C\u202E.\u0081\u0009\u0017T}(), new Func<Hero, bool>(u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206C\u206F\u200E\u200D\u202D\u206D\u206F\u200D\u200D\u202D\u206F\u206B\u206F\u206E\u206F\u200D\u200B\u202E\u200E\u202C\u206C\u202C\u206A\u206E\u206D\u202B\u202B\u200C\u202B\u206E\u202D\u206E\u206B\u200F\u200E\u206B\u202E\u206A\u200C\u206F\u202E));
							num = ((hero == null) ? 470055450 : 599111888);
							continue;
						}
						case 6U:
							num = -1286868238;
							continue;
						case 8U:
							num = -1112961987;
							continue;
						case 9U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E;
							this._activeTasks.Remove(u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Key);
							num = (int)(num3 * 2015828369U ^ 2073325034U);
							continue;
						}
						case 10U:
						{
							TaskStep currentStep;
							num = (int)(((currentStep.StepType != TaskStepType.WaitInSettlement) ? 3388770616U : 3472173708U) ^ num3 * 1506726422U);
							continue;
						}
						case 11U:
						{
							HeroTask value;
							TaskStep currentStep = value.GetCurrentStep();
							num = ((currentStep != null) ? -1104975917 : -1112961987);
							continue;
						}
						case 12U:
						{
							TaskManager.\u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E;
							HeroTask value = u206F_u200E_u200E_u200B_u200D_u202E_u200C_u200F_u206A_u206B_u200B_u202E_u206E_u206D_u200B_u206A_u200F_u206C_u202D_u200B_u206D_u206C_u202A_u206D_u200B_u200C_u202B_u206D_u202B_u206C_u206F_u206F_u200B_u206E_u206D_u202B_u206B_u200F_u206A_u202E_u202E.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Value;
							num = -488783928;
							continue;
						}
						case 13U:
							num = 799360485;
							continue;
						case 14U:
						{
							TaskStep currentStep;
							if (currentStep.Status == TaskStepStatus.InProgress)
							{
								num = -1124378091;
								continue;
							}
							goto IL_17A;
						}
						case 15U:
						{
							TaskStep currentStep;
							currentStep.StartWaiting();
							AIInfluenceBehavior instance2 = AIInfluenceBehavior.Instance;
							if (instance2 == null)
							{
								goto IL_1F7;
							}
							Hero hero;
							instance2.LogMessage(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-2098005263), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), currentStep.WaitDays));
							num = -965162827;
							continue;
						}
						case 16U:
							goto IL_2F5;
						}
						goto Block_4;
						IL_17B:
						num = (flag ? -416662181 : -471065826);
						continue;
						IL_17A:
						flag = false;
						goto IL_17B;
						IL_1F7:
						num = -471065826;
					}
				}
				Block_4:;
			}
		}

		// Token: 0x06001641 RID: 5697 RVA: 0x001519B0 File Offset: 0x0014FBB0
		public static void ResetInstance()
		{
			TaskManager._instance = null;
		}

		// Token: 0x06001642 RID: 5698 RVA: 0x001519C4 File Offset: 0x0014FBC4
		public static void SetInstance(TaskManager instance)
		{
			TaskManager._instance = instance;
		}

		// Token: 0x04000F98 RID: 3992
		private static TaskManager _instance;

		// Token: 0x04000F99 RID: 3993
		[SaveableField(1)]
		private Dictionary<string, HeroTask> _activeTasks;

		// Token: 0x04000F9A RID: 3994
		[SaveableField(2)]
		private Dictionary<string, List<HeroTask>> _completedTasks;

		// Token: 0x02000318 RID: 792
		[CompilerGenerated]
		private sealed class \u206F\u200E\u200E\u200B\u200D\u202E\u200C\u200F\u206A\u206B\u200B\u202E\u206E\u206D\u200B\u206A\u200F\u206C\u202D\u200B\u206D\u206C\u202A\u206D\u200B\u200C\u202B\u206D\u202B\u206C\u206F\u206F\u200B\u206E\u206D\u202B\u206B\u200F\u206A\u202E\u202E
		{
			// Token: 0x06001644 RID: 5700 RVA: 0x001519D8 File Offset: 0x0014FBD8
			internal bool \u206C\u206F\u200E\u200D\u202D\u206D\u206F\u200D\u200D\u202D\u206F\u206B\u206F\u206E\u206F\u200D\u200B\u202E\u200E\u202C\u206C\u202C\u206A\u206E\u206D\u202B\u202B\u200C\u202B\u206E\u202D\u206E\u206B\u200F\u200E\u206B\u202E\u206A\u200C\u206F\u202E(Hero A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E.Key);
			}

			// Token: 0x04000F9B RID: 3995
			public KeyValuePair<string, HeroTask> \u206F\u202A\u206D\u200D\u206E\u200F\u202B\u200B\u206D\u202A\u206C\u202D\u206B\u202B\u200E\u206E\u206E\u202B\u202B\u202A\u206E\u202E\u200D\u202D\u206F\u206C\u206D\u206D\u202E\u202C\u200E\u206B\u200C\u200C\u206C\u206E\u202C\u200D\u206A\u206E\u202E;
		}
	}
}
