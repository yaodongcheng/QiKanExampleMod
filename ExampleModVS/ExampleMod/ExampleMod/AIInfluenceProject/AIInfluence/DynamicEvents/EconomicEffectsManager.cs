using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using \u200F\u202A\u206C\u202C\u202B\u206B\u206C\u202A\u200B\u200D\u202D\u202A\u200F\u200E\u202D\u200F\u202E\u200E\u206F\u202A\u200C\u206A\u206F\u206F\u202A\u202D\u206A\u200F\u202C\u206A\u206A\u206B\u200B\u206D\u206A\u200D\u206D\u200F\u206B\u202B\u202E;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;

namespace AIInfluence.DynamicEvents
{
	// Token: 0x020001B8 RID: 440
	public class EconomicEffectsManager : CampaignBehaviorBase
	{
		// Token: 0x170002D3 RID: 723
		// (get) Token: 0x06000E1B RID: 3611 RVA: 0x000EEEF4 File Offset: 0x000ED0F4
		// (set) Token: 0x06000E1C RID: 3612 RVA: 0x000EEF08 File Offset: 0x000ED108
		public static EconomicEffectsManager Instance { get; private set; }

		// Token: 0x06000E1D RID: 3613 RVA: 0x000EEF1C File Offset: 0x000ED11C
		public EconomicEffectsManager()
		{
			this._storage = new EconomicEffectsStorage();
			EconomicEffectsManager.Instance = this;
		}

		// Token: 0x06000E1E RID: 3614 RVA: 0x000EEF50 File Offset: 0x000ED150
		public override void RegisterEvents()
		{
			\u202D\u202D\u202A\u206B\u206A\u200F\u206B\u200E\u202E\u206A\u200D\u206B\u202C\u206D\u200B\u202E\u202A\u200B\u206D\u202B\u206E\u200F\u206B\u202B\u200F\u200D\u206D\u206E\u206D\u202E\u206B\u206D\u202A\u206B\u206F\u200B\u200F\u206C\u206C\u200F\u202E.\u0009F\u0087\u00D7z().AddNonSerializedListener(this, new Action<CampaignGameStarter>(this.OnSessionLaunched));
			\u200E\u200E\u200B\u200B\u202B\u206B\u206F\u200C\u202A\u206A\u206A\u200B\u202B\u200C\u200F\u200F\u206C\u202D\u206D\u202A\u200B\u202A\u200F\u202C\u206D\u206B\u202D\u200F\u202D\u206F\u202A\u200D\u202A\u202A\u200C\u206F\u206C\u200B\u200E\u202B\u202E.\u00B4\u008Aòúr(\u206F\u202E\u200E\u206D\u202A\u202D\u200B\u200C\u200B\u206E\u202D\u206C\u200E\u200C\u200F\u206B\u206E\u202E\u202D\u206A\u202A\u200F\u200F\u206B\u202A\u202C\u206C\u206D\u206A\u202E\u206C\u202D\u200B\u200B\u202D\u206F\u206D\u206C\u200F\u202E.Äf\u008EÐU(), this, new Action(this.OnDailyTick));
		}

		// Token: 0x06000E1F RID: 3615 RVA: 0x000417F4 File Offset: 0x0003F9F4
		public override void SyncData(IDataStore dataStore)
		{
		}

		// Token: 0x06000E20 RID: 3616 RVA: 0x000EEFA0 File Offset: 0x000ED1A0
		private void OnSessionLaunched(CampaignGameStarter starter)
		{
			try
			{
				this._activeEffects.Clear();
				List<ActiveEconomicEffect> list = this._storage.LoadEffects();
				if (list != null)
				{
					goto IL_1D;
				}
				bool flag = false;
				goto IL_83;
				int num2;
				for (;;)
				{
					IL_22:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(num - (--195812633 + (-1894129218 - -822557458) + (-7797107 + -1619458889 + --339255784)) + (-1935739369 ^ 194537992 ^ 1329574662))) % 4U)
					{
					case 0U:
						this._activeEffects.AddRange(list);
						num2 = (int)(num3 * 1831559724U ^ 2387900031U);
						continue;
					case 1U:
						goto IL_77;
					case 2U:
						goto IL_1D;
					}
					break;
				}
				goto IL_B9;
				IL_77:
				flag = (list.Count > 0);
				goto IL_83;
				IL_B9:
				return;
				IL_1D:
				num2 = 763929829;
				goto IL_22;
				IL_83:
				num2 = (flag ? 417972040 : 770387823);
				goto IL_22;
			}
			catch
			{
			}
		}

		// Token: 0x06000E21 RID: 3617 RVA: 0x000EF080 File Offset: 0x000ED280
		public List<ActiveEconomicEffect> GetActiveEffects()
		{
			List<ActiveEconomicEffect> result;
			try
			{
				EconomicEffectsManager.\u200F\u202D\u202B\u206C\u200D\u206F\u200C\u200B\u206A\u202B\u206F\u206B\u202B\u206D\u200B\u200B\u202A\u200E\u202C\u206A\u202C\u202D\u206D\u202B\u202B\u200C\u200B\u200F\u200B\u200F\u202A\u200B\u200D\u200C\u202A\u206F\u206C\u200F\u200E\u202E u200F_u202D_u202B_u206C_u200D_u206F_u200C_u200B_u206A_u202B_u206F_u206B_u202B_u206D_u200B_u200B_u202A_u200E_u202C_u206A_u202C_u202D_u206D_u202B_u202B_u200C_u200B_u200F_u200B_u200F_u202A_u200B_u200D_u200C_u202A_u206F_u206C_u200F_u200E_u202E = new EconomicEffectsManager.\u200F\u202D\u202B\u206C\u200D\u206F\u200C\u200B\u206A\u202B\u206F\u206B\u202B\u206D\u200B\u200B\u202A\u200E\u202C\u206A\u202C\u202D\u206D\u202B\u202B\u200C\u200B\u200F\u200B\u200F\u202A\u200B\u200D\u200C\u202A\u206F\u206C\u200F\u200E\u202E();
				u200F_u202D_u202B_u206C_u200D_u206F_u200C_u200B_u206A_u202B_u206F_u206B_u202B_u206D_u200B_u200B_u202A_u200E_u202C_u206A_u202C_u202D_u206D_u202B_u202B_u200C_u200B_u200F_u200B_u200F_u202A_u200B_u200D_u200C_u202A_u206F_u206C_u200F_u200E_u202E.\u202C\u200F\u206F\u202E\u206A\u200D\u206C\u200E\u202C\u206D\u206A\u206C\u202C\u200D\u200D\u206E\u202C\u206B\u202E\u206E\u200D\u202B\u202B\u206A\u206F\u206B\u200C\u200B\u202B\u202A\u202E\u206C\u202E\u206C\u200F\u202E\u200E\u200E\u202B\u200E\u202E = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
				result = Enumerable.ToList<ActiveEconomicEffect>(Enumerable.Where<ActiveEconomicEffect>(this._activeEffects, new Func<ActiveEconomicEffect, bool>(u200F_u202D_u202B_u206C_u200D_u206F_u200C_u200B_u206A_u202B_u206F_u206B_u202B_u206D_u200B_u200B_u202A_u200E_u202C_u206A_u202C_u202D_u206D_u202B_u202B_u200C_u200B_u200F_u200B_u200F_u202A_u200B_u200D_u200C_u202A_u206F_u206C_u200F_u200E_u202E.\u200B\u202A\u206D\u206F\u206D\u206B\u200D\u206D\u202C\u206D\u206D\u202E\u200E\u206F\u206F\u206B\u206A\u206F\u206F\u200D\u200E\u206D\u200F\u206D\u200B\u200F\u206A\u200B\u206B\u200F\u202B\u200E\u202D\u206C\u200E\u200B\u206E\u202C\u206B\u206A\u202E)));
			}
			catch
			{
				result = new List<ActiveEconomicEffect>();
			}
			return result;
		}

		// Token: 0x06000E22 RID: 3618 RVA: 0x000EF0E8 File Offset: 0x000ED2E8
		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool TryGetSettlementDailyEffect(Settlement settlement, out float prosperityPerDay, out float foodPerDay, out string reason)
		{
			prosperityPerDay = 0f;
			foodPerDay = 0f;
			reason = null;
			bool flag = settlement == null;
			if (flag)
			{
				for (;;)
				{
					int num = -1507916833;
					switch ((-527617531 - ((num ^ -(-(~-763215283))) - 345378462)) * -859948929 % 3)
					{
					case 0:
						continue;
					case 1:
						goto IL_58;
					}
					break;
				}
				goto IL_6F;
				IL_58:
				return false;
			}
			IL_6F:
			bool result;
			try
			{
				EconomicEffectsManager.\u206C\u206C\u202C\u206D\u202D\u202A\u200D\u202B\u206C\u202B\u206D\u200C\u202A\u206B\u202E\u206F\u200D\u200C\u200B\u206C\u206F\u200B\u206D\u200C\u206C\u200C\u202A\u200D\u202D\u206B\u200D\u206D\u206D\u202B\u206C\u200C\u202A\u206F\u202B\u206C\u202E u206C_u206C_u202C_u206D_u202D_u202A_u200D_u202B_u206C_u202B_u206D_u200C_u202A_u206B_u202E_u206F_u200D_u200C_u200B_u206C_u206F_u200B_u206D_u200C_u206C_u200C_u202A_u200D_u202D_u206B_u200D_u206D_u206D_u202B_u206C_u200C_u202A_u206F_u202B_u206C_u202E = new EconomicEffectsManager.\u206C\u206C\u202C\u206D\u202D\u202A\u200D\u202B\u206C\u202B\u206D\u200C\u202A\u206B\u202E\u206F\u200D\u200C\u200B\u206C\u206F\u200B\u206D\u200C\u206C\u200C\u202A\u200D\u202D\u206B\u200D\u206D\u206D\u202B\u206C\u200C\u202A\u206F\u202B\u206C\u202E();
				u206C_u206C_u202C_u206D_u202D_u202A_u200D_u202B_u206C_u202B_u206D_u200C_u202A_u206B_u202E_u206F_u200D_u200C_u200B_u206C_u206F_u200B_u206D_u200C_u206C_u200C_u202A_u200D_u202D_u206B_u200D_u206D_u206D_u202B_u206C_u200C_u202A_u206F_u202B_u206C_u202E.\u200D\u206B\u206D\u202D\u206F\u202A\u202A\u206D\u206F\u206E\u200B\u206C\u206D\u200F\u206C\u202C\u202B\u206B\u206D\u206C\u206F\u202D\u200C\u200B\u206F\u206B\u202E\u206C\u206E\u202A\u202E\u200E\u206B\u202C\u206B\u206E\u206E\u206A\u202B\u200F\u202E = \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(settlement);
				u206C_u206C_u202C_u206D_u202D_u202A_u200D_u202B_u206C_u202B_u206D_u200C_u202A_u206B_u202E_u206F_u200D_u200C_u200B_u206C_u206F_u200B_u206D_u200C_u206C_u200C_u202A_u200D_u202D_u206B_u200D_u206D_u206D_u202B_u206C_u200C_u202A_u206F_u202B_u206C_u202E.\u202D\u200F\u200B\u202A\u206E\u206D\u200E\u206A\u202B\u202D\u200B\u206B\u200E\u200E\u200E\u202D\u202C\u202A\u206D\u202A\u206B\u202E\u206E\u200D\u206A\u206C\u200F\u206F\u200C\u200F\u206E\u200F\u200B\u202A\u200E\u202A\u202D\u200C\u206A\u202B\u202E = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays;
				List<ActiveEconomicEffect> list = Enumerable.ToList<ActiveEconomicEffect>(Enumerable.Where<ActiveEconomicEffect>(this._activeEffects, new Func<ActiveEconomicEffect, bool>(u206C_u206C_u202C_u206D_u202D_u202A_u200D_u202B_u206C_u202B_u206D_u200C_u202A_u206B_u202E_u206F_u200D_u200C_u200B_u206C_u206F_u200B_u206D_u200C_u206C_u200C_u202A_u200D_u202D_u206B_u200D_u206D_u206D_u202B_u206C_u200C_u202A_u206F_u202B_u206C_u202E.\u206B\u202C\u202C\u202C\u202B\u202E\u206A\u200C\u202E\u206D\u206F\u206A\u206D\u202D\u206C\u202B\u206A\u202D\u200E\u206D\u202C\u206E\u206D\u200C\u202A\u202D\u202E\u202C\u200C\u206E\u206B\u206F\u200B\u202A\u206C\u202A\u206B\u200D\u206C\u206C\u202E)));
				bool flag2 = !Enumerable.Any<ActiveEconomicEffect>(list);
				if (flag2)
				{
					for (;;)
					{
						int num = 325112413;
						switch ((-527617531 - ((num ^ -(-(~-763215283))) - 345378462)) * -859948929 % 3)
						{
						case 0:
							continue;
						case 1:
							goto IL_10B;
						}
						break;
					}
					goto IL_122;
					IL_10B:
					return false;
				}
				IL_122:
				using (List<ActiveEconomicEffect>.Enumerator enumerator = list.GetEnumerator())
				{
					for (;;)
					{
						IL_29F:
						int num2 = enumerator.MoveNext() ? 1766170511 : 1493774507;
						for (;;)
						{
							int num = num2;
							uint num3;
							switch ((num3 = (uint)((-527617531 - ((num ^ -(-(~-763215283))) - 345378462)) * -859948929)) % 10U)
							{
							case 0U:
							{
								ActiveEconomicEffect activeEconomicEffect;
								reason = \u202B\u202E\u202C\u200D\u206F\u206D\u206F\u202A\u206A\u206C\u206C\u206B\u206C\u202A\u200C\u202B\u200D\u206F\u202D\u206F\u206A\u202B\u206A\u206C\u206B\u200F\u202C\u200C\u206D\u202B\u206A\u202D\u200D\u202B\u200D\u206C\u206B\u206A\u206D\u200C\u202E.Þ\u00A4\u00A4F'(reason, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-434705534), activeEconomicEffect.Reason);
								num2 = -514017064;
								continue;
							}
							case 1U:
							{
								ActiveEconomicEffect activeEconomicEffect;
								num2 = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.mcò\u00A9^(reason, activeEconomicEffect.Reason)) ? 125375941 : -514017064);
								continue;
							}
							case 2U:
								goto IL_29F;
							case 3U:
							{
								ActiveEconomicEffect activeEconomicEffect;
								reason = activeEconomicEffect.Reason;
								num2 = (int)(num3 * 2728474076U ^ 844316620U);
								continue;
							}
							case 4U:
							{
								bool flag3 = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(reason);
								num2 = (int)(((!flag3) ? 1485929176U : 731501226U) ^ num3 * 2895401267U);
								continue;
							}
							case 5U:
								num2 = 981517413;
								continue;
							case 7U:
								num2 = 1766170511;
								continue;
							case 8U:
							{
								ActiveEconomicEffect activeEconomicEffect = enumerator.Current;
								prosperityPerDay += activeEconomicEffect.ProsperityDeltaPerDay;
								foodPerDay += activeEconomicEffect.FoodDeltaPerDay;
								bool flag4 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(activeEconomicEffect.Reason);
								num2 = ((!flag4) ? -2144872212 : 791371007);
								continue;
							}
							case 9U:
								num2 = -2144872212;
								continue;
							}
							goto Block_9;
						}
					}
					Block_9:;
				}
				bool flag5 = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(reason);
				if (flag5)
				{
					goto IL_2E0;
				}
				goto IL_337;
				int num4;
				for (;;)
				{
					IL_2E5:
					int num = num4;
					switch ((-527617531 - ((num ^ -(-(~-763215283))) - 345378462)) * -859948929 % 4)
					{
					case 1:
						reason = <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1872646393);
						num4 = -1296221620;
						continue;
					case 2:
						goto IL_2E0;
					case 3:
						goto IL_337;
					}
					break;
				}
				bool flag6 = \u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(foodPerDay) > 0.001f;
				goto IL_367;
				IL_2E0:
				num4 = -355802574;
				goto IL_2E5;
				IL_337:
				if (\u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(prosperityPerDay) <= 0.001f)
				{
					num4 = 17147561;
					goto IL_2E5;
				}
				flag6 = true;
				IL_367:
				result = flag6;
			}
			catch
			{
				result = false;
			}
			return result;
		}

		// Token: 0x06000E23 RID: 3619 RVA: 0x000EF49C File Offset: 0x000ED69C
		public void AddEconomicEffects(IEnumerable<EconomicEffect> effects)
		{
			bool flag = effects == null;
			if (flag)
			{
				for (;;)
				{
					int num = -90041261;
					switch ((-num * 1235007241 - (-658030745 + -291241041)) * -1019206481 % 3)
					{
					case 0:
						continue;
					case 2:
						goto IL_43;
					}
					break;
				}
				goto IL_58;
				IL_43:
				return;
			}
			IL_58:
			IEnumerator<EconomicEffect> enumerator = effects.GetEnumerator();
			try
			{
				for (;;)
				{
					IL_1AB:
					int num2 = (!\u200C\u200D\u206F\u200E\u202B\u206E\u206A\u200E\u202E\u202A\u206F\u202C\u206F\u206F\u200D\u200B\u206E\u206F\u202D\u202C\u206B\u202C\u202E\u206F\u200E\u200B\u206D\u200C\u200E\u200B\u202C\u206F\u206D\u206A\u200D\u206D\u206D\u202E\u206B\u202B\u202E.f\u00A5úÎ~(enumerator)) ? -1241978722 : -1852145256;
					for (;;)
					{
						int num = num2;
						uint num3;
						bool flag2;
						bool flag3;
						bool flag4;
						bool flag6;
						bool flag5;
						switch ((num3 = (uint)((-num * 1235007241 - (-658030745 + -291241041)) * -1019206481)) % 11U)
						{
						case 0U:
							num2 = (int)(num3 * 3094705053U ^ 2302329962U);
							continue;
						case 1U:
						{
							EconomicEffect economicEffect = enumerator.Current;
							if (economicEffect != null)
							{
								num2 = -1543106625;
								continue;
							}
							flag2 = true;
							goto IL_15E;
						}
						case 2U:
							num2 = (int)(num3 * 1061357071U ^ 14079740U);
							continue;
						case 3U:
						{
							EconomicEffect economicEffect;
							flag3 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(economicEffect.TargetId);
							if (economicEffect.TargetIds != null)
							{
								num2 = -1959343504;
								continue;
							}
							flag4 = false;
							goto IL_189;
						}
						case 4U:
						{
							EconomicEffect economicEffect;
							flag2 = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(economicEffect.TargetType);
							goto IL_15E;
						}
						case 5U:
							flag5 = !flag6;
							goto IL_131;
						case 7U:
						{
							EconomicEffect economicEffect;
							this.ApplyInstantEffect(economicEffect);
							this.CreateActiveEffects(economicEffect);
							num2 = -1501023336;
							continue;
						}
						case 8U:
							goto IL_1AB;
						case 9U:
							num2 = -1852145256;
							continue;
						case 10U:
						{
							EconomicEffect economicEffect;
							flag4 = (economicEffect.TargetIds.Count > 0);
							goto IL_189;
						}
						}
						goto Block_4;
						IL_131:
						num2 = (flag5 ? 1433329860 : 30979607);
						continue;
						IL_189:
						flag6 = flag4;
						if (!flag3)
						{
							num2 = -1092235947;
							continue;
						}
						flag5 = false;
						goto IL_131;
						IL_15E:
						bool flag7 = flag2;
						num2 = ((!flag7) ? 1161771659 : -785762346);
					}
				}
				Block_4:;
			}
			finally
			{
				if (enumerator != null)
				{
					for (;;)
					{
						IL_1D1:
						int num4 = -1910207430;
						for (;;)
						{
							int num = num4;
							uint num3;
							switch ((num3 = (uint)((-num * 1235007241 - (-658030745 + -291241041)) * -1019206481)) % 3U)
							{
							case 0U:
								goto IL_1D1;
							case 2U:
								\u200E\u206D\u202C\u200F\u202A\u206C\u202C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E.ôgÍ@\u000E(enumerator);
								num4 = (int)(num3 * 1746361154U ^ 1674004875U);
								continue;
							}
							goto Block_12;
						}
					}
					Block_12:;
				}
			}
			this._storage.SaveEffects(this._activeEffects);
		}

		// Token: 0x06000E24 RID: 3620 RVA: 0x000EF700 File Offset: 0x000ED900
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void CreateActiveEffects(EconomicEffect effect)
		{
			if (effect.DurationDays > 0)
			{
				goto IL_0D;
			}
			bool flag = false;
			goto IL_92;
			int num2;
			for (;;)
			{
				IL_12:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(num - -((464102594 ^ 1047453379) - -687815026 * -2059473691) + 151894187 * (1972226882 - -274950779)) ^ 1415885030)) % 6U)
				{
				case 1U:
					if (effect.ProsperityDeltaPerDay == 0f)
					{
						num2 = (int)(num3 * 3955801086U ^ 3833742301U);
						continue;
					}
					goto IL_8E;
				case 2U:
					if (effect.FoodDeltaPerDay == 0f)
					{
						num2 = (int)(num3 * 3518490352U ^ 175340472U);
						continue;
					}
					goto IL_8E;
				case 3U:
					goto IL_6F;
				case 4U:
					goto IL_0D;
				case 5U:
					goto IL_CF;
				}
				break;
			}
			goto IL_107;
			IL_6F:
			flag = (\u206B\u200F\u206B\u202C\u206D\u206F\u206D\u202A\u202B\u200D\u206E\u206B\u200E\u200E\u206C\u202C\u202E\u202D\u206C\u200C\u200B\u206B\u202D\u200E\u202A\u200D\u200D\u206F\u206E\u206A\u200B\u200F\u206B\u202C\u206C\u206C\u200E\u206E\u202C\u206A\u202E.ûfí)ý(effect.IncomeMultiplier - 1f) > 0.001f);
			goto IL_8F;
			IL_8E:
			flag = true;
			IL_8F:
			goto IL_92;
			IL_CF:
			return;
			IL_107:
			try
			{
				string targetType = effect.TargetType;
				string text = targetType;
				if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1839406465)))
				{
					goto IL_12B;
				}
				goto IL_191;
				int num4;
				for (;;)
				{
					IL_130:
					int num = num4;
					switch ((~(num - -((464102594 ^ 1047453379) - -687815026 * -2059473691) + 151894187 * (1972226882 - -274950779)) ^ 1415885030) % 7)
					{
					case 0:
						if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1810365924)))
						{
							num4 = 988168003;
							continue;
						}
						goto IL_3BF;
					case 2:
						goto IL_12B;
					case 3:
						goto IL_191;
					case 4:
						if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(900492205)))
						{
							num4 = 1259076671;
							continue;
						}
						goto IL_38D;
					case 5:
						goto IL_1A0;
					case 6:
						goto IL_204;
					}
					break;
				}
				goto IL_228;
				IL_1A0:
				goto IL_40A;
				IL_204:
				bool flag2 = effect.TargetIds.Count > 0;
				goto IL_215;
				IL_228:
				using (List<string>.Enumerator enumerator = effect.TargetIds.GetEnumerator())
				{
					for (;;)
					{
						IL_2D9:
						int num5 = enumerator.MoveNext() ? 687247152 : 943821718;
						for (;;)
						{
							int num = num5;
							uint num3;
							switch ((num3 = (uint)(~(num - -((464102594 ^ 1047453379) - -687815026 * -2059473691) + 151894187 * (1972226882 - -274950779)) ^ 1415885030)) % 6U)
							{
							case 0U:
							{
								string text2;
								this.AddActiveEffectForSettlement(effect, text2);
								num5 = (int)(num3 * 4234416611U ^ 71025196U);
								continue;
							}
							case 2U:
								goto IL_2D9;
							case 3U:
								num5 = 945274833;
								continue;
							case 4U:
								num5 = 687247152;
								continue;
							case 5U:
							{
								string text2 = enumerator.Current;
								bool flag3 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(text2);
								num5 = ((!flag3) ? 207525112 : 898713291);
								continue;
							}
							}
							goto Block_16;
						}
					}
					Block_16:;
				}
				goto IL_3FE;
				int num6;
				for (;;)
				{
					IL_32F:
					int num = num6;
					uint num3;
					switch ((num3 = (uint)(~(num - -((464102594 ^ 1047453379) - -687815026 * -2059473691) + 151894187 * (1972226882 - -274950779)) ^ 1415885030)) % 7U)
					{
					case 0U:
						num6 = 1988375076;
						continue;
					case 1U:
						goto IL_3D1;
					case 3U:
						this.AddActiveEffectForSettlement(effect, effect.TargetId);
						num6 = (int)(num3 * 3179409994U ^ 171547822U);
						continue;
					case 5U:
						goto IL_3BF;
					case 6U:
						goto IL_38D;
					}
					break;
				}
				goto IL_40A;
				IL_12B:
				num4 = 187305443;
				goto IL_130;
				IL_191:
				if (effect.TargetIds != null)
				{
					num4 = 1912400645;
					goto IL_130;
				}
				flag2 = false;
				IL_215:
				bool flag4 = flag2;
				if (flag4)
				{
					num4 = 584390610;
					goto IL_130;
				}
				goto IL_3D1;
				IL_38D:
				this.AddActiveEffectsForKingdom(effect);
				num6 = -2045602992;
				goto IL_32F;
				IL_3BF:
				this.AddActiveEffectsForClan(effect);
				num6 = -2045602992;
				goto IL_32F;
				IL_3D1:
				bool flag5 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(effect.TargetId);
				num6 = ((!flag5) ? 889383140 : 699690974);
				goto IL_32F;
				IL_3FE:
				IL_40A:;
			}
			catch
			{
			}
			return;
			IL_0D:
			num2 = -2031402098;
			goto IL_12;
			IL_92:
			bool flag6 = flag;
			bool flag7 = !flag6;
			num2 = ((!flag7) ? 209138089 : 1220047116);
			goto IL_12;
		}

		// Token: 0x06000E25 RID: 3621 RVA: 0x000EFB54 File Offset: 0x000EDD54
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void AddActiveEffectForSettlement(EconomicEffect effect, string settlementId)
		{
			ActiveEconomicEffect item = new ActiveEconomicEffect
			{
				TargetType = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-75144644),
				TargetId = settlementId,
				ProsperityDeltaPerDay = effect.ProsperityDeltaPerDay,
				FoodDeltaPerDay = effect.FoodDeltaPerDay,
				SecurityDeltaPerDay = 0f,
				LoyaltyDeltaPerDay = 0f,
				IncomeMultiplier = effect.IncomeMultiplier,
				StartDay = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u00B0\u000FYDÞ().ToDays,
				DurationDays = effect.DurationDays,
				Reason = effect.Reason
			};
			this._activeEffects.Add(item);
		}

		// Token: 0x06000E26 RID: 3622 RVA: 0x000EFC04 File Offset: 0x000EDE04
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void AddActiveEffectsForKingdom(EconomicEffect effect)
		{
			EconomicEffectsManager.\u200D\u202A\u200D\u200F\u206A\u206A\u206B\u202E\u202E\u200E\u206A\u202B\u200F\u202A\u206A\u206F\u200C\u202D\u200E\u200C\u200D\u206E\u200B\u206F\u206B\u200D\u202B\u200B\u202C\u200E\u200D\u200C\u206B\u200F\u206E\u206F\u202D\u200D\u200B\u200C\u202E u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E = new EconomicEffectsManager.\u200D\u202A\u200D\u200F\u206A\u206A\u206B\u202E\u202E\u200E\u206A\u202B\u200F\u202A\u206A\u206F\u200C\u202D\u200E\u200C\u200D\u206E\u200B\u206F\u206B\u200D\u202B\u200B\u202C\u200E\u200D\u200C\u206B\u200F\u206E\u206F\u202D\u200D\u200B\u200C\u202E();
			u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u200E\u202B\u206E\u202A\u206A\u206D\u206A\u202B\u202D\u202A\u206D\u206E\u200D\u202C\u202D\u200B\u206B\u206B\u200B\u206E\u202B\u206A\u200C\u206F\u206C\u206C\u202D\u200F\u200F\u200E\u202E\u206A\u206F\u206B\u202C\u200E\u200B\u200D\u200E\u202B\u202E = effect;
			u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u202C\u206E\u206F\u202B\u206F\u206F\u206D\u206F\u202D\u202D\u206E\u202D\u202C\u206F\u206E\u200F\u200E\u200B\u200B\u202D\u200E\u200B\u200D\u200D\u206B\u200F\u202D\u206F\u200B\u206C\u202D\u206E\u202B\u202E\u206E\u200B\u206F\u206E\u200E\u200C\u202E = Enumerable.FirstOrDefault<Kingdom>(\u200B\u200F\u202A\u202D\u200E\u206D\u200F\u206F\u202B\u200E\u206E\u206B\u202D\u200C\u206F\u206F\u206D\u200E\u202A\u206D\u200B\u206C\u200C\u206A\u206B\u206A\u206D\u200B\u206F\u200E\u200C\u206B\u206B\u206B\u202B\u200D\u202E\u200B\u202D\u206A\u202E.ºã\u009A\u00A7ü(), new Func<Kingdom, bool>(u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u206E\u200B\u200D\u200D\u200B\u202B\u200C\u200D\u206A\u206D\u206E\u200B\u202B\u200F\u202B\u206B\u202A\u202E\u206E\u202E\u200E\u206B\u200C\u206A\u200D\u202A\u200F\u206F\u200C\u202C\u206A\u202C\u202B\u206F\u206F\u200E\u202C\u200D\u202E\u206A\u202E));
			bool flag = u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u202C\u206E\u206F\u202B\u206F\u206F\u206D\u206F\u202D\u202D\u206E\u202D\u202C\u206F\u206E\u200F\u200E\u200B\u200B\u202D\u200E\u200B\u200D\u200D\u206B\u200F\u202D\u206F\u200B\u206C\u202D\u206E\u202B\u202E\u206E\u200B\u206F\u206E\u200E\u200C\u202E == null;
			IEnumerable<Settlement> enumerable;
			for (;;)
			{
				IL_39:
				int num = -1488760346;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)((-(num2 - ~(229099828 + -1552954565 - (101294553 ^ -288608982))) ^ ~-1125066492) - 1549818935)) % 15U)
					{
					case 0U:
						num = (int)(num3 * 2007946519U ^ 561395635U);
						continue;
					case 1U:
					{
						string text = u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u200E\u202B\u206E\u202A\u206A\u206D\u206A\u202B\u202D\u202A\u206D\u206E\u200D\u202C\u202D\u200B\u206B\u206B\u200B\u206E\u202B\u206A\u200C\u206F\u206C\u206C\u202D\u200F\u200F\u200E\u202E\u206A\u206F\u206B\u202C\u200E\u200B\u200D\u200E\u202B\u202E.TargetScope ?? <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1958883061);
						num = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-869387792)) ? 1405430698 : 1401814840);
						continue;
					}
					case 2U:
						goto IL_15A;
					case 3U:
						enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u202C\u206B\u200C\u202E\u202B\u206D\u202D\u202D\u206C\u200D\u200E\u206F\u200C\u206C\u202D\u200D\u206F\u206C\u206C\u206C\u206D\u200E\u202E\u206D\u200D\u202B\u200C\u206E\u206A\u206E\u206B\u200C\u206E\u200C\u206C\u202B\u206C\u202A\u200B\u206D\u202E));
						num = -1345171249;
						continue;
					case 4U:
						enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u200D\u200C\u200B\u206B\u200C\u200B\u202E\u200E\u202D\u206C\u206F\u206B\u206E\u202A\u206B\u206D\u200D\u200D\u206D\u200D\u202D\u206F\u202B\u202C\u200E\u202A\u200D\u206B\u202C\u200C\u202B\u202B\u202D\u202D\u206D\u206A\u200C\u206E\u202E\u206B\u202E));
						num = -1345171249;
						continue;
					case 5U:
						enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u206F\u206C\u206C\u202E\u206E\u200C\u202E\u200F\u200F\u200D\u200E\u206D\u202A\u206E\u200D\u200C\u202C\u206C\u202E\u200B\u206C\u200F\u202E\u200D\u202C\u200E\u206E\u206E\u202B\u206E\u200E\u202B\u206A\u200D\u206E\u202A\u202C\u200C\u206F\u202A\u202E));
						num = -1345171249;
						continue;
					case 7U:
					{
						enumerable = Enumerable.Where<Settlement>(\u200F\u200D\u200D\u200C\u200F\u200B\u206A\u202E\u202A\u200F\u202A\u206F\u206D\u202D\u200F\u200C\u202C\u200F\u200B\u206B\u202A\u206B\u202D\u200D\u202B\u202A\u206D\u200C\u200C\u202A\u202A\u202C\u202A\u206C\u206A\u202D\u206C\u202B\u202D\u206E\u202E.\u202A\u206F\u206B\u206D\u200D\u202A\u206C\u200C\u200C\u202E\u206F\u206E\u202A\u200C\u202E\u206C\u206C\u200D\u206B\u206A\u202E\u200D\u200B\u200B\u206C\u206E\u206A\u206F\u202D\u206E\u202D\u202D\u206E\u206A\u202B\u206A\u206D\u200C\u206F\u202B\u202E(), new Func<Settlement, bool>(u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u202D\u206C\u202A\u206C\u200F\u202A\u206C\u202C\u202B\u202E\u206A\u200E\u206E\u202A\u206F\u202B\u200B\u200B\u206E\u200C\u202C\u206B\u200B\u202D\u206C\u202D\u200F\u200F\u202D\u200E\u202B\u200D\u206C\u206E\u206C\u200B\u200F\u206C\u202A\u206F\u202E));
						string text;
						string text2 = text;
						string text3 = text2;
						num = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text3, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-864977365))) ? -81150451 : 1714401595);
						continue;
					}
					case 8U:
					{
						string text3;
						num = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text3, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-43504431))) ? 125469248 : -103616573);
						continue;
					}
					case 9U:
						goto IL_B5;
					case 10U:
					{
						string text3;
						num = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text3, <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(1266714518))) ? -1239441245 : 937642058);
						continue;
					}
					case 12U:
						num = (int)(((!flag) ? 4265702578U : 3445515130U) ^ num3 * 103691811U);
						continue;
					case 13U:
						goto IL_39;
					case 14U:
					{
						string text3;
						num = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text3, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(556387404))) ? -1262413010 : 1997889794);
						continue;
					}
					}
					goto Block_1;
				}
			}
			Block_1:
			goto IL_2DB;
			IL_B5:
			IL_15A:
			return;
			IL_2DB:
			IEnumerator<Settlement> enumerator = enumerable.GetEnumerator();
			try
			{
				for (;;)
				{
					IL_35D:
					int num4 = \u200C\u200D\u206F\u200E\u202B\u206E\u206A\u200E\u202E\u202A\u206F\u202C\u206F\u206F\u200D\u200B\u206E\u206F\u202D\u202C\u206B\u202C\u202E\u206F\u200E\u200B\u206D\u200C\u200E\u200B\u202C\u206F\u206D\u206A\u200D\u206D\u206D\u202E\u206B\u202B\u202E.f\u00A5úÎ~(enumerator) ? 359549508 : -1409620050;
					for (;;)
					{
						int num2 = num4;
						switch (((-(num2 - ~(229099828 + -1552954565 - (101294553 ^ -288608982))) ^ ~-1125066492) - 1549818935) % 4)
						{
						case 0:
							goto IL_35D;
						case 1:
						{
							Settlement settlement = enumerator.Current;
							this.AddActiveEffectForSettlement(u200D_u202A_u200D_u200F_u206A_u206A_u206B_u202E_u202E_u200E_u206A_u202B_u200F_u202A_u206A_u206F_u200C_u202D_u200E_u200C_u200D_u206E_u200B_u206F_u206B_u200D_u202B_u200B_u202C_u200E_u200D_u200C_u206B_u200F_u206E_u206F_u202D_u200D_u200B_u200C_u202E.\u200E\u202B\u206E\u202A\u206A\u206D\u206A\u202B\u202D\u202A\u206D\u206E\u200D\u202C\u202D\u200B\u206B\u206B\u200B\u206E\u202B\u206A\u200C\u206F\u206C\u206C\u202D\u200F\u200F\u200E\u202E\u206A\u206F\u206B\u202C\u200E\u200B\u200D\u200E\u202B\u202E, \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(settlement));
							num4 = 1842784111;
							continue;
						}
						case 2:
							num4 = 359549508;
							continue;
						}
						goto Block_14;
					}
				}
				Block_14:;
			}
			finally
			{
				if (enumerator != null)
				{
					for (;;)
					{
						IL_385:
						int num5 = 243715973;
						for (;;)
						{
							int num2 = num5;
							uint num3;
							switch ((num3 = (uint)((-(num2 - ~(229099828 + -1552954565 - (101294553 ^ -288608982))) ^ ~-1125066492) - 1549818935)) % 3U)
							{
							case 1U:
								\u200E\u206D\u202C\u200F\u202A\u206C\u202C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E.ôgÍ@\u000E(enumerator);
								num5 = (int)(num3 * 2790957807U ^ 666082723U);
								continue;
							case 2U:
								goto IL_385;
							}
							goto Block_17;
						}
					}
					Block_17:;
				}
			}
		}

		// Token: 0x06000E27 RID: 3623 RVA: 0x000F000C File Offset: 0x000EE20C
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void AddActiveEffectsForClan(EconomicEffect effect)
		{
			EconomicEffectsManager.\u202A\u206A\u200F\u200C\u206A\u200D\u202B\u202C\u202E\u206C\u200F\u206C\u202D\u206C\u206E\u200E\u206A\u206D\u206D\u200D\u200E\u200B\u206F\u202A\u206E\u206E\u202A\u200F\u206D\u202D\u206C\u200C\u202C\u206B\u202A\u202B\u202A\u206F\u200F\u200C\u202E u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E = new EconomicEffectsManager.\u202A\u206A\u200F\u200C\u206A\u200D\u202B\u202C\u202E\u206C\u200F\u206C\u202D\u206C\u206E\u200E\u206A\u206D\u206D\u200D\u200E\u200B\u206F\u202A\u206E\u206E\u202A\u200F\u206D\u202D\u206C\u200C\u202C\u206B\u202A\u202B\u202A\u206F\u200F\u200C\u202E();
			u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200B\u206D\u206B\u200F\u202E\u206A\u206A\u200C\u200D\u200D\u206E\u206A\u202C\u206D\u202E\u200B\u200E\u202E\u200B\u206B\u206E\u202B\u206F\u202B\u200C\u202A\u200B\u200D\u202A\u200B\u200B\u206C\u200D\u202C\u202D\u206A\u200D\u202E\u200C\u200D\u202E = effect;
			u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200E\u202A\u206D\u206D\u202D\u200C\u202D\u202A\u202C\u206E\u206B\u200B\u206F\u206A\u206B\u200C\u202D\u206B\u200F\u200B\u206E\u202D\u202E\u202B\u202A\u202A\u200B\u200D\u200E\u200E\u202D\u200C\u200E\u202C\u202D\u206B\u206A\u202C\u202C\u200E\u202E = Enumerable.FirstOrDefault<Clan>(\u202B\u200F\u206E\u202C\u202B\u200D\u206C\u200F\u206A\u206C\u202B\u202A\u202D\u200E\u206E\u202C\u202C\u202D\u200E\u202E\u206B\u202C\u200D\u200B\u200C\u200D\u206D\u200B\u206E\u202E\u200C\u202A\u200E\u202C\u202B\u202C\u206E\u200E\u202A\u202B\u202E.A\u007F\u0089WÌ(), new Func<Clan, bool>(u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u202A\u206C\u200D\u202C\u200D\u206B\u200B\u206B\u200E\u206F\u202B\u206C\u200C\u206F\u200E\u200E\u206E\u206A\u206D\u200C\u206F\u200D\u206D\u202A\u202D\u206B\u206C\u202A\u200C\u206C\u206E\u202E\u206B\u200D\u206E\u202A\u202A\u200C\u206C\u202E\u202E));
			bool flag = u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200E\u202A\u206D\u206D\u202D\u200C\u202D\u202A\u202C\u206E\u206B\u200B\u206F\u206A\u206B\u200C\u202D\u206B\u200F\u200B\u206E\u202D\u202E\u202B\u202A\u202A\u200B\u200D\u200E\u200E\u202D\u200C\u200E\u202C\u202D\u206B\u206A\u202C\u202C\u200E\u202E == null;
			if (flag)
			{
				goto IL_3F;
			}
			goto IL_110;
			int num2;
			IEnumerable<Settlement> enumerable;
			string text3;
			for (;;)
			{
				IL_44:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)((num ^ -2093853045) * -946455223 + 1888701626))) % 15U)
				{
				case 1U:
				{
					string text;
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-119813335)) ? 2001690795 : 1953652680);
					continue;
				}
				case 2U:
					goto IL_110;
				case 3U:
				{
					string text;
					num2 = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(773575000))) ? -1693390787 : 151355228);
					continue;
				}
				case 4U:
				{
					enumerable = Enumerable.Where<Settlement>(\u200F\u200D\u200D\u200C\u200F\u200B\u206A\u202E\u202A\u200F\u202A\u206F\u206D\u202D\u200F\u200C\u202C\u200F\u200B\u206B\u202A\u206B\u202D\u200D\u202B\u202A\u206D\u200C\u200C\u202A\u202A\u202C\u202A\u206C\u206A\u202D\u206C\u202B\u202D\u206E\u202E.\u202A\u206F\u206B\u206D\u200D\u202A\u206C\u200C\u200C\u202E\u206F\u206E\u202A\u200C\u202E\u206C\u206C\u200D\u206B\u206A\u202E\u200D\u200B\u200B\u206C\u206E\u206A\u206F\u202D\u206E\u202D\u202D\u206E\u206A\u202B\u206A\u206D\u200C\u206F\u202B\u202E(), new Func<Settlement, bool>(u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200B\u206C\u206E\u200E\u200D\u206D\u202A\u200C\u202E\u200B\u202C\u200D\u200B\u200B\u202C\u200C\u206C\u202A\u206C\u206F\u206E\u200D\u206F\u200D\u202D\u206F\u200F\u202E\u200C\u200B\u200C\u200E\u206A\u200B\u206E\u200C\u200C\u206A\u200D\u206B\u202E));
					string text2 = text3;
					string text = text2;
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-864977365)) ? -194889532 : -573757198);
					continue;
				}
				case 5U:
					enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u200E\u206F\u200C\u202A\u200D\u202E\u202D\u202E\u206C\u200C\u202A\u200D\u202B\u202C\u200F\u206D\u206B\u206C\u202E\u202E\u202A\u202B\u202C\u200F\u206E\u202A\u200F\u206A\u206B\u202D\u200E\u200D\u200E\u202E\u202E\u206E\u206E\u206E\u202A\u200C\u202E));
					num2 = 453188847;
					continue;
				case 6U:
					goto IL_3F;
				case 8U:
					goto IL_1B9;
				case 9U:
					num2 = (int)(num3 * 2519160164U ^ 182859413U);
					continue;
				case 10U:
					goto IL_1D2;
				case 11U:
					enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u202D\u206F\u200C\u200B\u200D\u202D\u206F\u200C\u200F\u202D\u206C\u200C\u200D\u206E\u200B\u200F\u202B\u202E\u202A\u200D\u200C\u200F\u202B\u202D\u202B\u202B\u200B\u200D\u202C\u206C\u206F\u206F\u200E\u200D\u202E\u206E\u206A\u202C\u202E\u200D\u202E));
					num2 = 453188847;
					continue;
				case 12U:
					num2 = (int)(num3 * 2547447521U ^ 2312151209U);
					continue;
				case 13U:
				{
					string text;
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-593669106)) ? 165513013 : -1620897218);
					continue;
				}
				case 14U:
					enumerable = Enumerable.Where<Settlement>(enumerable, new Func<Settlement, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u200B\u200C\u206D\u206E\u200D\u200B\u202B\u202A\u206C\u200B\u200D\u200E\u202D\u206C\u200B\u206F\u202A\u206F\u202D\u200C\u200B\u206D\u202C\u206A\u200E\u206B\u202A\u206C\u206D\u206F\u206D\u206A\u200D\u200C\u202D\u200C\u200B\u202B\u202D\u206E\u202E));
					num2 = 956698572;
					continue;
				}
				break;
			}
			goto IL_2BB;
			IL_1B9:
			IL_1D2:
			return;
			IL_2BB:
			IEnumerator<Settlement> enumerator = enumerable.GetEnumerator();
			try
			{
				for (;;)
				{
					IL_32A:
					int num4 = \u200C\u200D\u206F\u200E\u202B\u206E\u206A\u200E\u202E\u202A\u206F\u202C\u206F\u206F\u200D\u200B\u206E\u206F\u202D\u202C\u206B\u202C\u202E\u206F\u200E\u200B\u206D\u200C\u200E\u200B\u202C\u206F\u206D\u206A\u200D\u206D\u206D\u202E\u206B\u202B\u202E.f\u00A5úÎ~(enumerator) ? -271068700 : 1283181847;
					for (;;)
					{
						int num = num4;
						switch (~((num ^ -2093853045) * -946455223 + 1888701626) % 4)
						{
						case 0:
							num4 = -271068700;
							continue;
						case 2:
						{
							Settlement settlement = enumerator.Current;
							this.AddActiveEffectForSettlement(u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200B\u206D\u206B\u200F\u202E\u206A\u206A\u200C\u200D\u200D\u206E\u206A\u202C\u206D\u202E\u200B\u200E\u202E\u200B\u206B\u206E\u202B\u206F\u202B\u200C\u202A\u200B\u200D\u202A\u200B\u200B\u206C\u200D\u202C\u202D\u206A\u200D\u202E\u200C\u200D\u202E, \u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(settlement));
							num4 = -7653459;
							continue;
						}
						case 3:
							goto IL_32A;
						}
						goto Block_13;
					}
				}
				Block_13:;
			}
			finally
			{
				if (enumerator != null)
				{
					for (;;)
					{
						IL_34F:
						int num5 = -497573290;
						for (;;)
						{
							int num = num5;
							uint num3;
							switch ((num3 = (uint)(~(uint)((num ^ -2093853045) * -946455223 + 1888701626))) % 3U)
							{
							case 0U:
								goto IL_34F;
							case 1U:
								\u200E\u206D\u202C\u200F\u202A\u206C\u202C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E.ôgÍ@\u000E(enumerator);
								num5 = (int)(num3 * 3972817793U ^ 854664440U);
								continue;
							}
							goto Block_16;
						}
					}
					Block_16:;
				}
			}
			return;
			IL_3F:
			num2 = 391414821;
			goto IL_44;
			IL_110:
			text3 = (u202A_u206A_u200F_u200C_u206A_u200D_u202B_u202C_u202E_u206C_u200F_u206C_u202D_u206C_u206E_u200E_u206A_u206D_u206D_u200D_u200E_u200B_u206F_u202A_u206E_u206E_u202A_u200F_u206D_u202D_u206C_u200C_u202C_u206B_u202A_u202B_u202A_u206F_u200F_u200C_u202E.\u200B\u206D\u206B\u200F\u202E\u206A\u206A\u200C\u200D\u200D\u206E\u206A\u202C\u206D\u202E\u200B\u200E\u202E\u200B\u206B\u206E\u202B\u206F\u202B\u200C\u202A\u200B\u200D\u202A\u200B\u200B\u206C\u200D\u202C\u202D\u206A\u200D\u202E\u200C\u200D\u202E.TargetScope ?? <Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(2028442184));
			bool flag2 = \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text3, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(1958883061));
			num2 = ((!flag2) ? 28560237 : -1295011760);
			goto IL_44;
		}

		// Token: 0x06000E28 RID: 3624 RVA: 0x000F03CC File Offset: 0x000EE5CC
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplyInstantEffect(EconomicEffect effect)
		{
			bool flag;
			if (effect == null)
			{
				flag = true;
				goto IL_4F;
			}
			IL_04:
			int num = -623059130;
			IL_09:
			int num2 = num;
			switch (-(-num2 * -1567528279 - (2087750752 + 420734883)) % 4)
			{
			case 1:
				flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(effect.TargetType);
				goto IL_4F;
			case 2:
				return;
			case 3:
				goto IL_04;
			}
			try
			{
				string targetType = effect.TargetType;
				string text = targetType;
				if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1206744059)))
				{
					goto IL_9C;
				}
				goto IL_114;
				int num3;
				for (;;)
				{
					IL_A1:
					num2 = num3;
					uint num4;
					switch ((num4 = (uint)(-(uint)(-num2 * -1567528279 - (2087750752 + 420734883)))) % 8U)
					{
					case 0U:
						goto IL_9C;
					case 1U:
						goto IL_114;
					case 2U:
						num3 = (int)(num4 * 3603737047U ^ 3867429850U);
						continue;
					case 3U:
						this.ApplyInstantClanEffect(effect);
						num3 = 1903113692;
						continue;
					case 4U:
						num3 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-1248334973)) ? 1944151344 : 668974479);
						continue;
					case 5U:
						this.ApplyInstantKingdomEffect(effect);
						num3 = 1903113692;
						continue;
					case 6U:
						num3 = ((!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-228451274))) ? 1592667313 : -345021822);
						continue;
					}
					break;
				}
				return;
				IL_9C:
				num3 = -1063000253;
				goto IL_A1;
				IL_114:
				this.ApplyInstantSettlementEffect(effect);
				num3 = 1903113692;
				goto IL_A1;
			}
			catch
			{
			}
			return;
			IL_4F:
			num = (flag ? -1267380001 : -1160584751);
			goto IL_09;
		}

		// Token: 0x06000E29 RID: 3625 RVA: 0x000F0580 File Offset: 0x000EE780
		private void ApplyInstantSettlementEffect(EconomicEffect effect)
		{
			if (effect.TargetIds != null)
			{
				goto IL_0C;
			}
			bool flag = false;
			goto IL_149;
			int num2;
			IEnumerable<string> enumerable;
			for (;;)
			{
				IL_11:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((1678905366 ^ 277980893) - (-168852839 - -1514897821) - ((-(1346163790 ^ -1165460360) ^ ~-163912428 + (1221295403 + -1300012480)) - num) + (-1535642601 + -1651602397) ^ 1468950399)) % 8U)
				{
				case 0U:
				{
					bool flag2;
					num2 = (int)((flag2 ? 2386831747U : 1227507361U) ^ num3 * 2961501783U);
					continue;
				}
				case 1U:
					enumerable = new string[]
					{
						effect.TargetId
					};
					num2 = (int)(num3 * 1350711226U ^ 2684085060U);
					continue;
				case 2U:
					goto IL_0C;
				case 3U:
					goto IL_8E;
				case 4U:
				{
					bool flag2 = !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(effect.TargetId);
					num2 = -280653140;
					continue;
				}
				case 5U:
					goto IL_138;
				case 7U:
					enumerable = Enumerable.Where<string>(effect.TargetIds, new Func<string, bool>(EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E.<>9.\u200E\u206F\u206C\u202D\u202A\u200B\u206D\u206D\u202D\u202C\u206F\u202D\u202B\u206B\u200E\u200F\u200E\u200D\u206D\u206A\u206D\u200F\u200D\u200C\u202C\u200E\u206C\u206A\u206D\u200C\u206B\u206F\u206D\u202B\u202C\u200C\u202D\u200D\u200B\u206C\u202E));
					num2 = -993358834;
					continue;
				}
				break;
			}
			goto IL_161;
			IL_8E:
			return;
			IL_138:
			flag = (effect.TargetIds.Count > 0);
			goto IL_149;
			IL_161:
			IEnumerator<string> enumerator = enumerable.GetEnumerator();
			try
			{
				for (;;)
				{
					IL_2ED:
					int num4 = \u200C\u200D\u206F\u200E\u202B\u206E\u206A\u200E\u202E\u202A\u206F\u202C\u206F\u206F\u200D\u200B\u206E\u206F\u202D\u202C\u206B\u202C\u202E\u206F\u200E\u200B\u206D\u200C\u200E\u200B\u202C\u206F\u206D\u206A\u200D\u206D\u206D\u202E\u206B\u202B\u202E.f\u00A5úÎ~(enumerator) ? 357267333 : -132081145;
					for (;;)
					{
						int num = num4;
						uint num3;
						bool flag3;
						bool flag4;
						switch ((num3 = (uint)((1678905366 ^ 277980893) - (-168852839 - -1514897821) - ((-(1346163790 ^ -1165460360) ^ ~-163912428 + (1221295403 + -1300012480)) - num) + (-1535642601 + -1651602397) ^ 1468950399)) % 23U)
						{
						case 0U:
							num4 = 357267333;
							continue;
						case 1U:
							num4 = ((effect.LoyaltyDelta != 0f) ? -23983192 : -1184725494);
							continue;
						case 2U:
						{
							Settlement settlement;
							if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.&\u0094\u0091\u00B7\u00BE(settlement))
							{
								num4 = (int)(num3 * 1661885159U ^ 2147729125U);
								continue;
							}
							flag3 = false;
							goto IL_5D5;
						}
						case 3U:
						{
							Settlement settlement;
							if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.[{\u0083\u0010_(settlement))
							{
								num4 = -331136053;
								continue;
							}
							flag4 = false;
							goto IL_461;
						}
						case 4U:
						{
							Settlement settlement;
							flag3 = (settlement.Town != null);
							goto IL_5D5;
						}
						case 5U:
						{
							Settlement settlement;
							\u202A\u202D\u206B\u206B\u206D\u202E\u200D\u202B\u200B\u200E\u200F\u202C\u202E\u202B\u200B\u206F\u206E\u206E\u206D\u206F\u200F\u206C\u202E\u202C\u200C\u206B\u202E\u206B\u202E\u200E\u200C\u206A\u206A\u206C\u206A\u200E\u200C\u200B\u202C\u200D\u202E.Î\u00B2\u00A5\u001B\u00B7(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u202B\u202A\u202C\u206E\u206E\u206F\u206E\u200C\u200D\u202A\u200B\u202C\u206F\u200E\u202E\u206B\u200F\u202B\u200B\u206F\u200F\u206C\u200F\u200D\u202B\u202D\u206D\u202E\u206C\u206C\u202A\u206C\u200F\u200F\u206B\u202B\u206D\u202A\u200D\u202E\u202E.s\u0012X>J(settlement.Town) + effect.LoyaltyDelta));
							num4 = (int)(num3 * 1524094152U ^ 2082721322U);
							continue;
						}
						case 6U:
							goto IL_2ED;
						case 7U:
						{
							EconomicEffectsManager.\u200F\u202A\u206B\u200F\u206F\u206A\u200E\u200E\u206B\u202D\u202E\u200D\u202B\u206E\u200D\u202A\u206A\u206B\u206E\u202E\u206C\u200B\u206F\u200D\u206A\u202C\u200E\u202B\u200B\u206C\u202D\u206B\u206D\u206D\u206C\u202C\u200E\u202A\u200B\u206C\u202E u200F_u202A_u206B_u200F_u206F_u206A_u200E_u200E_u206B_u202D_u202E_u200D_u202B_u206E_u200D_u202A_u206A_u206B_u206E_u202E_u206C_u200B_u206F_u200D_u206A_u202C_u200E_u202B_u200B_u206C_u202D_u206B_u206D_u206D_u206C_u202C_u200E_u202A_u200B_u206C_u202E = new EconomicEffectsManager.\u200F\u202A\u206B\u200F\u206F\u206A\u200E\u200E\u206B\u202D\u202E\u200D\u202B\u206E\u200D\u202A\u206A\u206B\u206E\u202E\u206C\u200B\u206F\u200D\u206A\u202C\u200E\u202B\u200B\u206C\u202D\u206B\u206D\u206D\u206C\u202C\u200E\u202A\u200B\u206C\u202E();
							u200F_u202A_u206B_u200F_u206F_u206A_u200E_u200E_u206B_u202D_u202E_u200D_u202B_u206E_u200D_u202A_u206A_u206B_u206E_u202E_u206C_u200B_u206F_u200D_u206A_u202C_u200E_u202B_u200B_u206C_u202D_u206B_u206D_u206D_u206C_u202C_u200E_u202A_u200B_u206C_u202E.\u202A\u206F\u202B\u206D\u202E\u202B\u202E\u202A\u206C\u206C\u206D\u202A\u202C\u200D\u206A\u206C\u206E\u206F\u200C\u206C\u202C\u202A\u202D\u202D\u206F\u206B\u202E\u200F\u202B\u202B\u200C\u206F\u200B\u202B\u206D\u206E\u206A\u200B\u206E\u202B\u202E = enumerator.Current;
							Settlement settlement = Enumerable.FirstOrDefault<Settlement>(\u200F\u200D\u200D\u200C\u200F\u200B\u206A\u202E\u202A\u200F\u202A\u206F\u206D\u202D\u200F\u200C\u202C\u200F\u200B\u206B\u202A\u206B\u202D\u200D\u202B\u202A\u206D\u200C\u200C\u202A\u202A\u202C\u202A\u206C\u206A\u202D\u206C\u202B\u202D\u206E\u202E.(hD\u0010ñ(), new Func<Settlement, bool>(u200F_u202A_u206B_u200F_u206F_u206A_u200E_u200E_u206B_u202D_u202E_u200D_u202B_u206E_u200D_u202A_u206A_u206B_u206E_u202E_u206C_u200B_u206F_u200D_u206A_u202C_u200E_u202B_u200B_u206C_u202D_u206B_u206D_u206D_u206C_u202C_u200E_u202A_u200B_u206C_u202E.\u206D\u200F\u206E\u206B\u206E\u200F\u206A\u200C\u200E\u206D\u206A\u200C\u200E\u206B\u206C\u202C\u200F\u200E\u206D\u200D\u200B\u202A\u200F\u200B\u206E\u206E\u200B\u200E\u202E\u206A\u206F\u206F\u206B\u206F\u200F\u202B\u206D\u206A\u202D\u206E\u202E));
							bool flag5 = settlement == null;
							num4 = ((!flag5) ? -26007542 : -784624129);
							continue;
						}
						case 8U:
						{
							bool flag6 = effect.FoodDelta != 0f;
							num4 = ((!flag6) ? -660081693 : -322902507);
							continue;
						}
						case 9U:
							num4 = 68385861;
							continue;
						case 10U:
						{
							Settlement settlement;
							\u202A\u202D\u206B\u206B\u206D\u202E\u200D\u202B\u200B\u200E\u200F\u202C\u202E\u202B\u200B\u206F\u206E\u206E\u206D\u206F\u200F\u206C\u202E\u202C\u200C\u206B\u202E\u206B\u202E\u200E\u200C\u206A\u206A\u206C\u206A\u200E\u200C\u200B\u202C\u200D\u202E.\u00B76ÕCd(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u202B\u202A\u202C\u206E\u206E\u206F\u206E\u200C\u200D\u202A\u200B\u202C\u206F\u200E\u202E\u206B\u200F\u202B\u200B\u206F\u200F\u206C\u200F\u200D\u202B\u202D\u206D\u202E\u206C\u206C\u202A\u206C\u200F\u200F\u206B\u202B\u206D\u202A\u200D\u202E\u202E.6b\u000C\u0006H(settlement.Town) + effect.ProsperityDelta));
							num4 = (int)(num3 * 2370819383U ^ 3427247261U);
							continue;
						}
						case 11U:
							num4 = 68385861;
							continue;
						case 12U:
							num4 = -205348162;
							continue;
						case 13U:
							num4 = (int)(num3 * 1746428388U ^ 837632810U);
							continue;
						case 14U:
						{
							Settlement settlement;
							\u202A\u202D\u206B\u206B\u206D\u202E\u200D\u202B\u200B\u200E\u200F\u202C\u202E\u202B\u200B\u206F\u206E\u206E\u206D\u206F\u200F\u206C\u202E\u202C\u200C\u206B\u202E\u206B\u202E\u200E\u200C\u206A\u206A\u206C\u206A\u200E\u200C\u200B\u202C\u200D\u202E.-{Ó\Ë(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u202B\u202A\u202C\u206E\u206E\u206F\u206E\u200C\u200D\u202A\u200B\u202C\u206F\u200E\u202E\u206B\u200F\u202B\u200B\u206F\u200F\u206C\u200F\u200D\u202B\u202D\u206D\u202E\u206C\u206C\u202A\u206C\u200F\u200F\u206B\u202B\u206D\u202A\u200D\u202E\u202E.d\u0017+\u0006\u0090(settlement.Town) + effect.SecurityDelta));
							num4 = (int)(num3 * 1107136945U ^ 3449303469U);
							continue;
						}
						case 15U:
						{
							Settlement settlement;
							num4 = (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.Óß;\u0004å(settlement) ? -259383782 : 142472227);
							continue;
						}
						case 16U:
						{
							Settlement settlement;
							flag4 = (settlement.Village != null);
							goto IL_461;
						}
						case 17U:
						{
							bool flag7 = effect.ProsperityDelta != 0f;
							num4 = (int)(((!flag7) ? 3521695884U : 2187660925U) ^ num3 * 3234824651U);
							continue;
						}
						case 18U:
						{
							bool flag8 = effect.SecurityDelta != 0f;
							num4 = ((!flag8) ? 161186956 : 282565595);
							continue;
						}
						case 19U:
						{
							Settlement settlement;
							\u206B\u206B\u202C\u200E\u200E\u200C\u206A\u200F\u206D\u202D\u202C\u200D\u200E\u200F\u200B\u206A\u200B\u206B\u206A\u206C\u200E\u200C\u200E\u202A\u202E\u206D\u202E\u202C\u202D\u200F\u202A\u200E\u206D\u200E\u202B\u206A\u206D\u206C\u206C\u206A\u202E.12\u0017\u00A32(settlement.Village, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(50f, \u200B\u206A\u202E\u200E\u200D\u206F\u206A\u206B\u206D\u206B\u206A\u206E\u206D\u206A\u206A\u202B\u200D\u206C\u202B\u200E\u202B\u206E\u206E\u202D\u206E\u200F\u202E\u200C\u202B\u202A\u200F\u202E\u206E\u206E\u202D\u206D\u202E\u200F\u200D\u200F\u202E.àÉX\u0084ï(settlement.Village) + effect.ProsperityDelta));
							num4 = (int)(num3 * 922661937U ^ 2816368225U);
							continue;
						}
						case 21U:
						{
							Settlement settlement;
							\u206E\u200B\u206E\u206F\u200C\u206C\u200E\u206B\u200C\u202A\u206A\u202D\u206E\u206B\u206A\u202B\u202E\u202E\u200B\u200B\u206B\u200E\u206B\u206F\u206C\u206C\u200E\u200B\u200C\u206C\u202B\u206D\u206B\u200E\u200B\u202B\u202A\u206B\u206A\u202C\u202E.I\u00B3é_6(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u200B\u202E\u200E\u206E\u206E\u202C\u200B\u202C\u200F\u200D\u200E\u202C\u200D\u202C\u200E\u202C\u202B\u206C\u202C\u206D\u206F\u206F\u206C\u206F\u200E\u200B\u202A\u202B\u202C\u202C\u206E\u206A\u200F\u202B\u206B\u206C\u202B\u200E\u202A\u200E\u202E.ItÚÂõ(settlement.Town) + effect.FoodDelta));
							num4 = (int)(num3 * 3048266287U ^ 437774330U);
							continue;
						}
						case 22U:
						{
							bool flag9 = effect.ProsperityDelta != 0f;
							num4 = (int)(((!flag9) ? 3855322018U : 454695131U) ^ num3 * 1936598659U);
							continue;
						}
						}
						goto Block_7;
						IL_461:
						bool flag10 = flag4;
						num4 = ((!flag10) ? -1416710106 : 235163699);
						continue;
						IL_5D5:
						bool flag11 = flag3;
						num4 = ((!flag11) ? 68385861 : -932500535);
					}
				}
				Block_7:;
			}
			finally
			{
				if (enumerator != null)
				{
					for (;;)
					{
						IL_5FA:
						int num5 = 633948948;
						for (;;)
						{
							int num = num5;
							uint num3;
							switch ((num3 = (uint)((1678905366 ^ 277980893) - (-168852839 - -1514897821) - ((-(1346163790 ^ -1165460360) ^ ~-163912428 + (1221295403 + -1300012480)) - num) + (-1535642601 + -1651602397) ^ 1468950399)) % 3U)
							{
							case 0U:
								goto IL_5FA;
							case 2U:
								\u200E\u206D\u202C\u200F\u202A\u206C\u202C\u206E\u206D\u206F\u206A\u200D\u206D\u206E\u206C\u202E\u206D\u206D\u200E\u206A\u200D\u206B\u206E\u206B\u206D\u206C\u206F\u206F\u206C\u200D\u206C\u206C\u202D\u206F\u200F\u206A\u206C\u202C\u206D\u206A\u202E.ôgÍ@\u000E(enumerator);
								num5 = (int)(num3 * 1366033441U ^ 2154600897U);
								continue;
							}
							goto Block_21;
						}
					}
					Block_21:;
				}
			}
			return;
			IL_0C:
			num2 = -296839825;
			goto IL_11;
			IL_149:
			num2 = (flag ? -900261571 : -459264456);
			goto IL_11;
		}

		// Token: 0x06000E2A RID: 3626 RVA: 0x000417F4 File Offset: 0x0003F9F4
		private void ApplyInstantKingdomEffect(EconomicEffect effect)
		{
		}

		// Token: 0x06000E2B RID: 3627 RVA: 0x000417F4 File Offset: 0x0003F9F4
		private void ApplyInstantClanEffect(EconomicEffect effect)
		{
		}

		// Token: 0x06000E2C RID: 3628 RVA: 0x000F0C30 File Offset: 0x000EEE30
		private void OnDailyTick()
		{
			try
			{
				bool flag = this._activeEffects.Count == 0;
				if (flag)
				{
					for (;;)
					{
						int num = -1362089718;
						switch (-(~((num ^ (-1663116743 ^ -1631252692 ^ 1088544203 * 241657259 + (1795637852 + -1955482245))) - -(-1964826744 ^ 335058993))) % 3)
						{
						case 1:
							goto IL_69;
						case 2:
							continue;
						}
						break;
					}
					goto IL_7E;
					IL_69:
					return;
				}
				IL_7E:
				float num2 = (float)\u206D\u202A\u200E\u200B\u206C\u200B\u200E\u200C\u206F\u200B\u206A\u200B\u202A\u206A\u200B\u206D\u200F\u206D\u206A\u206A\u206C\u206A\u200C\u206A\u202E\u206A\u200F\u206C\u206F\u206A\u206E\u206F\u200E\u200F\u206C\u200F\u206D\u206B\u202B\u202C\u202E.\u200D\u200D\u200C\u202E\u200B\u206F\u202C\u206B\u206A\u202A\u206D\u202E\u202A\u202B\u202B\u206F\u206D\u206E\u200B\u202C\u200D\u200F\u206C\u200C\u200C\u200D\u200F\u200C\u202E\u206A\u200B\u200B\u202C\u206E\u202A\u200C\u206B\u200B\u202E\u206B\u202E().ToDays;
				List<ActiveEconomicEffect> list = new List<ActiveEconomicEffect>();
				using (List<ActiveEconomicEffect>.Enumerator enumerator = this._activeEffects.GetEnumerator())
				{
					for (;;)
					{
						IL_138:
						int num3 = enumerator.MoveNext() ? 36677658 : 217867188;
						for (;;)
						{
							int num = num3;
							uint num4;
							switch ((num4 = (uint)(-(uint)(~(uint)((num ^ (-1663116743 ^ -1631252692 ^ 1088544203 * 241657259 + (1795637852 + -1955482245))) - -(-1964826744 ^ 335058993))))) % 6U)
							{
							case 0U:
								num3 = 36677658;
								continue;
							case 1U:
							{
								ActiveEconomicEffect activeEconomicEffect = enumerator.Current;
								bool flag2 = num2 >= activeEconomicEffect.StartDay + (float)activeEconomicEffect.DurationDays;
								num3 = ((!flag2) ? -1249146612 : 1263063643);
								continue;
							}
							case 2U:
							{
								ActiveEconomicEffect activeEconomicEffect;
								list.Add(activeEconomicEffect);
								num3 = (int)(num4 * 1112526708U ^ 3464010309U);
								continue;
							}
							case 4U:
								goto IL_138;
							case 5U:
							{
								ActiveEconomicEffect activeEconomicEffect;
								this.ApplyDailyEffect(activeEconomicEffect);
								num3 = 8607269;
								continue;
							}
							}
							goto Block_7;
						}
					}
					Block_7:;
				}
				bool flag3 = list.Count > 0;
				if (flag3)
				{
					using (List<ActiveEconomicEffect>.Enumerator enumerator2 = list.GetEnumerator())
					{
						for (;;)
						{
							IL_235:
							int num5 = (!enumerator2.MoveNext()) ? -1216510759 : 2003201268;
							for (;;)
							{
								int num = num5;
								switch (-(~((num ^ (-1663116743 ^ -1631252692 ^ 1088544203 * 241657259 + (1795637852 + -1955482245))) - -(-1964826744 ^ 335058993))) % 4)
								{
								case 0:
									num5 = 2003201268;
									continue;
								case 1:
									goto IL_235;
								case 3:
								{
									ActiveEconomicEffect item = enumerator2.Current;
									this._activeEffects.Remove(item);
									num5 = 1795844310;
									continue;
								}
								}
								goto Block_12;
							}
						}
						Block_12:;
					}
					this._storage.SaveEffects(this._activeEffects);
				}
			}
			catch
			{
			}
		}

		// Token: 0x06000E2D RID: 3629 RVA: 0x000F0F08 File Offset: 0x000EF108
		[MethodImpl(MethodImplOptions.NoInlining)]
		private void ApplyDailyEffect(ActiveEconomicEffect active)
		{
			string targetType = active.TargetType;
			string text = targetType;
			if (!\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1206744059)))
			{
				goto IL_26;
			}
			goto IL_AB;
			int num2;
			for (;;)
			{
				IL_2B:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-1839585691 - ~(-num) * 851012361)) % 8U)
				{
				case 0U:
					goto IL_26;
				case 1U:
					goto IL_AB;
				case 2U:
					num2 = (int)(num3 * 3281877004U ^ 3632549130U);
					continue;
				case 5U:
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(-1810365924)) ? 726266999 : -1883143460);
					continue;
				case 6U:
					num2 = (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(text, <Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(-228451274)) ? 959263067 : -681301071);
					continue;
				}
				break;
			}
			return;
			IL_26:
			num2 = 406267008;
			goto IL_2B;
			IL_AB:
			this.ApplyDailySettlementEffect(active);
			num2 = 1803669842;
			goto IL_2B;
		}

		// Token: 0x06000E2E RID: 3630 RVA: 0x000F1014 File Offset: 0x000EF214
		private void ApplyDailySettlementEffect(ActiveEconomicEffect active)
		{
			EconomicEffectsManager.\u206A\u200B\u202A\u200D\u206B\u200F\u202E\u206F\u202B\u202A\u202A\u202A\u200F\u200B\u202A\u206C\u202C\u200E\u206B\u202B\u202E\u202E\u206B\u202D\u200C\u200C\u206A\u206B\u200D\u206E\u206A\u202A\u206A\u202D\u206F\u206C\u202E\u206D\u206C\u206F\u202E u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E = new EconomicEffectsManager.\u206A\u200B\u202A\u200D\u206B\u200F\u202E\u206F\u202B\u202A\u202A\u202A\u200F\u200B\u202A\u206C\u202C\u200E\u206B\u202B\u202E\u202E\u206B\u202D\u200C\u200C\u206A\u206B\u200D\u206E\u206A\u202A\u206A\u202D\u206F\u206C\u202E\u206D\u206C\u206F\u202E();
			u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E = active;
			Settlement settlement = Enumerable.FirstOrDefault<Settlement>(\u200F\u200D\u200D\u200C\u200F\u200B\u206A\u202E\u202A\u200F\u202A\u206F\u206D\u202D\u200F\u200C\u202C\u200F\u200B\u206B\u202A\u206B\u202D\u200D\u202B\u202A\u206D\u200C\u200C\u202A\u202A\u202C\u202A\u206C\u206A\u202D\u206C\u202B\u202D\u206E\u202E.(hD\u0010ñ(), new Func<Settlement, bool>(u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u202A\u200B\u206B\u206D\u200C\u200D\u206D\u200B\u206D\u206C\u206E\u202E\u200F\u202A\u200D\u200D\u202E\u206A\u200D\u202D\u202B\u206A\u206B\u202E\u202C\u202B\u202B\u200D\u202B\u206C\u202E\u202E\u206F\u206B\u200C\u206A\u200C\u202D\u202E\u206E\u202E));
			bool flag = settlement == null;
			if (flag)
			{
				goto IL_35;
			}
			goto IL_2D8;
			int num2;
			for (;;)
			{
				IL_3A:
				int num = num2;
				uint num3;
				bool flag2;
				switch ((num3 = (uint)((num * -409722671 + (~931173352 - ~-1091767789)) * -504806729 + -1133618353)) % 16U)
				{
				case 0U:
					if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.&\u0094\u0091\u00B7\u00BE(settlement))
					{
						num2 = (int)(num3 * 455273593U ^ 4098368082U);
						continue;
					}
					flag2 = false;
					goto IL_29E;
				case 2U:
					num2 = 1530854323;
					continue;
				case 3U:
					num2 = 1530854323;
					continue;
				case 4U:
					\u206B\u206B\u202C\u200E\u200E\u200C\u206A\u200F\u206D\u202D\u202C\u200D\u200E\u200F\u200B\u206A\u200B\u206B\u206A\u206C\u200E\u200C\u200E\u202A\u202E\u206D\u202E\u202C\u202D\u200F\u202A\u200E\u206D\u200E\u202B\u206A\u206D\u206C\u206C\u206A\u202E.12\u0017\u00A32(settlement.Village, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(50f, \u200B\u206A\u202E\u200E\u200D\u206F\u206A\u206B\u206D\u206B\u206A\u206E\u206D\u206A\u206A\u202B\u200D\u206C\u202B\u200E\u202B\u206E\u206E\u202D\u206E\u200F\u202E\u200C\u202B\u202A\u200F\u202E\u206E\u206E\u202D\u206D\u202E\u200F\u200D\u200F\u202E.àÉX\u0084ï(settlement.Village) + u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.ProsperityDeltaPerDay));
					num2 = (int)(num3 * 2166765174U ^ 3974915602U);
					continue;
				case 5U:
					\u206E\u200B\u206E\u206F\u200C\u206C\u200E\u206B\u200C\u202A\u206A\u202D\u206E\u206B\u206A\u202B\u202E\u202E\u200B\u200B\u206B\u200E\u206B\u206F\u206C\u206C\u200E\u200B\u200C\u206C\u202B\u206D\u206B\u200E\u200B\u202B\u202A\u206B\u206A\u202C\u202E.I\u00B3é_6(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u200B\u202E\u200E\u206E\u206E\u202C\u200B\u202C\u200F\u200D\u200E\u202C\u200D\u202C\u200E\u202C\u202B\u206C\u202C\u206D\u206F\u206F\u206C\u206F\u200E\u200B\u202A\u202B\u202C\u202C\u206E\u206A\u200F\u202B\u206B\u206C\u202B\u200E\u202A\u200E\u202E.ItÚÂõ(settlement.Town) + u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.FoodDeltaPerDay));
					num2 = (int)(num3 * 1370169906U ^ 3446336491U);
					continue;
				case 6U:
					goto IL_1FC;
				case 7U:
					num2 = (int)(((u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.ProsperityDeltaPerDay != 0f) ? 2672604936U : 4133415562U) ^ num3 * 3614507712U);
					continue;
				case 8U:
				{
					bool flag3 = u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.FoodDeltaPerDay != 0f;
					num2 = ((!flag3) ? 1389676241 : 1587400831);
					continue;
				}
				case 9U:
					goto IL_35;
				case 10U:
					flag2 = (settlement.Town != null);
					goto IL_29E;
				case 11U:
				{
					bool flag4 = u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.ProsperityDeltaPerDay != 0f;
					num2 = (int)(((!flag4) ? 1808549874U : 1206809928U) ^ num3 * 3321762450U);
					continue;
				}
				case 12U:
					goto IL_2D8;
				case 13U:
					num2 = (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.Óß;\u0004å(settlement) ? 1410378034 : 1981169804);
					continue;
				case 14U:
					\u202A\u202D\u206B\u206B\u206D\u202E\u200D\u202B\u200B\u200E\u200F\u202C\u202E\u202B\u200B\u206F\u206E\u206E\u206D\u206F\u200F\u206C\u202E\u202C\u200C\u206B\u202E\u206B\u202E\u200E\u200C\u206A\u206A\u206C\u206A\u200E\u200C\u200B\u202C\u200D\u202E.\u00B76ÕCd(settlement.Town, \u200B\u200B\u202A\u200F\u202A\u206F\u206A\u200C\u200B\u206D\u200E\u206B\u200E\u206D\u206A\u202D\u206A\u200C\u202D\u200B\u206E\u200D\u206A\u202E\u200F\u206E\u202E\u206A\u200B\u202E\u202C\u200D\u202D\u206F\u206A\u206B\u206B\u202A\u206C\u200D\u202E.&\u0088Ü4s(0f, \u202B\u202A\u202C\u206E\u206E\u206F\u206E\u200C\u200D\u202A\u200B\u202C\u206F\u200E\u202E\u206B\u200F\u202B\u200B\u206F\u200F\u206C\u200F\u200D\u202B\u202D\u206D\u202E\u206C\u206C\u202A\u206C\u200F\u200F\u206B\u202B\u206D\u202A\u200D\u202E\u202E.6b\u000C\u0006H(settlement.Town) + u206A_u200B_u202A_u200D_u206B_u200F_u202E_u206F_u202B_u202A_u202A_u202A_u200F_u200B_u202A_u206C_u202C_u200E_u206B_u202B_u202E_u202E_u206B_u202D_u200C_u200C_u206A_u206B_u200D_u206E_u206A_u202A_u206A_u202D_u206F_u206C_u202E_u206D_u206C_u206F_u202E.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.ProsperityDeltaPerDay));
					num2 = (int)(num3 * 3189692816U ^ 818406164U);
					continue;
				case 15U:
					num2 = (int)(num3 * 4293465487U ^ 306603202U);
					continue;
				}
				break;
				IL_29E:
				bool flag5 = flag2;
				num2 = ((!flag5) ? 1530854323 : 637003849);
			}
			return;
			IL_1FC:
			bool flag6 = settlement.Village != null;
			goto IL_208;
			IL_35:
			num2 = -1431497067;
			goto IL_3A;
			IL_208:
			bool flag7 = flag6;
			num2 = ((!flag7) ? -990009385 : 1052976509);
			goto IL_3A;
			IL_2D8:
			if (\u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.[{\u0083\u0010_(settlement))
			{
				num2 = 1860127478;
				goto IL_3A;
			}
			flag6 = false;
			goto IL_208;
		}

		// Token: 0x040008FB RID: 2299
		private readonly EconomicEffectsStorage _storage;

		// Token: 0x040008FC RID: 2300
		private readonly List<ActiveEconomicEffect> _activeEffects = new List<ActiveEconomicEffect>();

		// Token: 0x020001B9 RID: 441
		[CompilerGenerated]
		[Serializable]
		private sealed class \u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E
		{
			// Token: 0x06000E31 RID: 3633 RVA: 0x000E4BB0 File Offset: 0x000E2DB0
			internal bool \u206F\u206C\u206C\u202E\u206E\u200C\u202E\u200F\u200F\u200D\u200E\u206D\u202A\u206E\u200D\u200C\u202C\u206C\u202E\u200B\u206C\u200F\u202E\u200D\u202C\u200E\u206E\u206E\u202B\u206E\u200E\u202B\u206A\u200D\u206E\u202A\u202C\u200C\u206F\u202A\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.Óß;\u0004å(A_1);
			}

			// Token: 0x06000E32 RID: 3634 RVA: 0x000F134C File Offset: 0x000EF54C
			internal bool \u200D\u200C\u200B\u206B\u200C\u200B\u202E\u200E\u202D\u206C\u206F\u206B\u206E\u202A\u206B\u206D\u200D\u200D\u206D\u200D\u202D\u206F\u202B\u202C\u200E\u202A\u200D\u206B\u202C\u200C\u202B\u202B\u202D\u202D\u206D\u206A\u200C\u206E\u202E\u206B\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.&\u0094\u0091\u00B7\u00BE(A_1);
			}

			// Token: 0x06000E33 RID: 3635 RVA: 0x000F1364 File Offset: 0x000EF564
			internal bool \u202C\u206B\u200C\u202E\u202B\u206D\u202D\u202D\u206C\u200D\u200E\u206F\u200C\u206C\u202D\u200D\u206F\u206C\u206C\u206C\u206D\u200E\u202E\u206D\u200D\u202B\u200C\u206E\u206A\u206E\u206B\u200C\u206E\u200C\u206C\u202B\u206C\u202A\u200B\u206D\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.[{\u0083\u0010_(A_1);
			}

			// Token: 0x06000E34 RID: 3636 RVA: 0x000E4BB0 File Offset: 0x000E2DB0
			internal bool \u200B\u200C\u206D\u206E\u200D\u200B\u202B\u202A\u206C\u200B\u200D\u200E\u202D\u206C\u200B\u206F\u202A\u206F\u202D\u200C\u200B\u206D\u202C\u206A\u200E\u206B\u202A\u206C\u206D\u206F\u206D\u206A\u200D\u200C\u202D\u200C\u200B\u202B\u202D\u206E\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.Óß;\u0004å(A_1);
			}

			// Token: 0x06000E35 RID: 3637 RVA: 0x000F134C File Offset: 0x000EF54C
			internal bool \u202D\u206F\u200C\u200B\u200D\u202D\u206F\u200C\u200F\u202D\u206C\u200C\u200D\u206E\u200B\u200F\u202B\u202E\u202A\u200D\u200C\u200F\u202B\u202D\u202B\u202B\u200B\u200D\u202C\u206C\u206F\u206F\u200E\u200D\u202E\u206E\u206A\u202C\u202E\u200D\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.&\u0094\u0091\u00B7\u00BE(A_1);
			}

			// Token: 0x06000E36 RID: 3638 RVA: 0x000F1364 File Offset: 0x000EF564
			internal bool \u200E\u206F\u200C\u202A\u200D\u202E\u202D\u202E\u206C\u200C\u202A\u200D\u202B\u202C\u200F\u206D\u206B\u206C\u202E\u202E\u202A\u202B\u202C\u200F\u206E\u202A\u200F\u206A\u206B\u202D\u200E\u200D\u200E\u202E\u202E\u206E\u206E\u206E\u202A\u200C\u202E(Settlement A_1)
			{
				return \u206C\u202B\u206E\u206D\u202D\u206A\u202A\u202C\u206A\u200B\u202C\u200B\u202E\u202C\u206F\u200D\u206A\u206E\u206D\u206C\u200C\u200E\u206F\u206F\u202B\u206A\u206F\u200B\u206D\u200B\u202B\u200B\u200E\u202C\u206D\u200B\u200D\u202C\u202E\u206E\u202E.[{\u0083\u0010_(A_1);
			}

			// Token: 0x06000E37 RID: 3639 RVA: 0x0005A8D0 File Offset: 0x00058AD0
			internal bool \u200E\u206F\u206C\u202D\u202A\u200B\u206D\u206D\u202D\u202C\u206F\u202D\u202B\u206B\u200E\u200F\u200E\u200D\u206D\u206A\u206D\u200F\u200D\u200C\u202C\u200E\u206C\u206A\u206D\u200C\u206B\u206F\u206D\u202B\u202C\u200C\u202D\u200D\u200B\u206C\u202E(string A_1)
			{
				return !\u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(A_1);
			}

			// Token: 0x040008FE RID: 2302
			public static readonly EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E <>9 = new EconomicEffectsManager.\u206B\u202B\u206B\u206D\u206C\u206A\u206F\u202A\u202A\u206C\u200F\u206D\u202E\u206A\u202E\u206C\u206C\u200D\u202A\u206D\u200C\u206D\u202B\u202B\u200F\u206C\u202B\u206D\u200C\u202D\u202E\u206E\u206A\u200F\u200B\u200C\u202A\u206C\u200E\u202C\u202E();

			// Token: 0x040008FF RID: 2303
			public static Func<Settlement, bool> <>9__15_2;

			// Token: 0x04000900 RID: 2304
			public static Func<Settlement, bool> <>9__15_3;

			// Token: 0x04000901 RID: 2305
			public static Func<Settlement, bool> <>9__15_4;

			// Token: 0x04000902 RID: 2306
			public static Func<Settlement, bool> <>9__16_2;

			// Token: 0x04000903 RID: 2307
			public static Func<Settlement, bool> <>9__16_3;

			// Token: 0x04000904 RID: 2308
			public static Func<Settlement, bool> <>9__16_4;

			// Token: 0x04000905 RID: 2309
			public static Func<string, bool> <>9__18_0;
		}

		// Token: 0x020001BA RID: 442
		[CompilerGenerated]
		private sealed class \u200F\u202D\u202B\u206C\u200D\u206F\u200C\u200B\u206A\u202B\u206F\u206B\u202B\u206D\u200B\u200B\u202A\u200E\u202C\u206A\u202C\u202D\u206D\u202B\u202B\u200C\u200B\u200F\u200B\u200F\u202A\u200B\u200D\u200C\u202A\u206F\u206C\u200F\u200E\u202E
		{
			// Token: 0x06000E39 RID: 3641 RVA: 0x000F137C File Offset: 0x000EF57C
			internal bool \u200B\u202A\u206D\u206F\u206D\u206B\u200D\u206D\u202C\u206D\u206D\u202E\u200E\u206F\u206F\u206B\u206A\u206F\u206F\u200D\u200E\u206D\u200F\u206D\u200B\u200F\u206A\u200B\u206B\u200F\u202B\u200E\u202D\u206C\u200E\u200B\u206E\u202C\u206B\u206A\u202E(ActiveEconomicEffect A_1)
			{
				return this.\u202C\u200F\u206F\u202E\u206A\u200D\u206C\u200E\u202C\u206D\u206A\u206C\u202C\u200D\u200D\u206E\u202C\u206B\u202E\u206E\u200D\u202B\u202B\u206A\u206F\u206B\u200C\u200B\u202B\u202A\u202E\u206C\u202E\u206C\u200F\u202E\u200E\u200E\u202B\u200E\u202E < A_1.StartDay + (float)A_1.DurationDays;
			}

			// Token: 0x04000906 RID: 2310
			public float \u202C\u200F\u206F\u202E\u206A\u200D\u206C\u200E\u202C\u206D\u206A\u206C\u202C\u200D\u200D\u206E\u202C\u206B\u202E\u206E\u200D\u202B\u202B\u206A\u206F\u206B\u200C\u200B\u202B\u202A\u202E\u206C\u202E\u206C\u200F\u202E\u200E\u200E\u202B\u200E\u202E;
		}

		// Token: 0x020001BB RID: 443
		[CompilerGenerated]
		private sealed class \u206C\u206C\u202C\u206D\u202D\u202A\u200D\u202B\u206C\u202B\u206D\u200C\u202A\u206B\u202E\u206F\u200D\u200C\u200B\u206C\u206F\u200B\u206D\u200C\u206C\u200C\u202A\u200D\u202D\u206B\u200D\u206D\u206D\u202B\u206C\u200C\u202A\u206F\u202B\u206C\u202E
		{
			// Token: 0x06000E3B RID: 3643 RVA: 0x000F13A0 File Offset: 0x000EF5A0
			[MethodImpl(MethodImplOptions.NoInlining)]
			internal bool \u206B\u202C\u202C\u202C\u202B\u202E\u206A\u200C\u202E\u206D\u206F\u206A\u206D\u202D\u206C\u202B\u206A\u202D\u200E\u206D\u202C\u206E\u206D\u200C\u202A\u202D\u202E\u202C\u200C\u206E\u206B\u206F\u200B\u202A\u206C\u202A\u206B\u200D\u206C\u206C\u202E(ActiveEconomicEffect A_1)
			{
				if (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(A_1.TargetType, <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-75144644)))
				{
					for (;;)
					{
						IL_21:
						int num = -856958021;
						for (;;)
						{
							int num2 = num;
							uint num3;
							switch ((num3 = (uint)(-(uint)((num2 + (-2081409215 - ~(-1240176491 ^ 458917395)) ^ -1916936823 * -1145945856) * -499614795))) % 3U)
							{
							case 0U:
								goto IL_21;
							case 1U:
								if (\u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(A_1.TargetId, this.\u200D\u206B\u206D\u202D\u206F\u202A\u202A\u206D\u206F\u206E\u200B\u206C\u206D\u200F\u206C\u202C\u202B\u206B\u206D\u206C\u206F\u202D\u200C\u200B\u206F\u206B\u202E\u206C\u206E\u202A\u202E\u200E\u206B\u202C\u206B\u206E\u206E\u206A\u202B\u200F\u202E))
								{
									num = (int)(num3 * 1820254258U ^ 229518917U);
									continue;
								}
								goto IL_A5;
							}
							goto Block_1;
						}
					}
					Block_1:
					return this.\u202D\u200F\u200B\u202A\u206E\u206D\u200E\u206A\u202B\u202D\u200B\u206B\u200E\u200E\u200E\u202D\u202C\u202A\u206D\u202A\u206B\u202E\u206E\u200D\u206A\u206C\u200F\u206F\u200C\u200F\u206E\u200F\u200B\u202A\u200E\u202A\u202D\u200C\u206A\u202B\u202E < A_1.StartDay + (float)A_1.DurationDays;
				}
				IL_A5:
				return false;
			}

			// Token: 0x04000907 RID: 2311
			public string \u200D\u206B\u206D\u202D\u206F\u202A\u202A\u206D\u206F\u206E\u200B\u206C\u206D\u200F\u206C\u202C\u202B\u206B\u206D\u206C\u206F\u202D\u200C\u200B\u206F\u206B\u202E\u206C\u206E\u202A\u202E\u200E\u206B\u202C\u206B\u206E\u206E\u206A\u202B\u200F\u202E;

			// Token: 0x04000908 RID: 2312
			public float \u202D\u200F\u200B\u202A\u206E\u206D\u200E\u206A\u202B\u202D\u200B\u206B\u200E\u200E\u200E\u202D\u202C\u202A\u206D\u202A\u206B\u202E\u206E\u200D\u206A\u206C\u200F\u206F\u200C\u200F\u206E\u200F\u200B\u202A\u200E\u202A\u202D\u200C\u206A\u202B\u202E;
		}

		// Token: 0x020001BC RID: 444
		[CompilerGenerated]
		private sealed class \u200D\u202A\u200D\u200F\u206A\u206A\u206B\u202E\u202E\u200E\u206A\u202B\u200F\u202A\u206A\u206F\u200C\u202D\u200E\u200C\u200D\u206E\u200B\u206F\u206B\u200D\u202B\u200B\u202C\u200E\u200D\u200C\u206B\u200F\u206E\u206F\u202D\u200D\u200B\u200C\u202E
		{
			// Token: 0x06000E3D RID: 3645 RVA: 0x000F1454 File Offset: 0x000EF654
			internal bool \u206E\u200B\u200D\u200D\u200B\u202B\u200C\u200D\u206A\u206D\u206E\u200B\u202B\u200F\u202B\u206B\u202A\u202E\u206E\u202E\u200E\u206B\u200C\u206A\u200D\u202A\u200F\u206F\u200C\u202C\u206A\u202C\u202B\u206F\u206F\u200E\u202C\u200D\u202E\u206A\u202E(Kingdom A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u200E\u202B\u206E\u202A\u206A\u206D\u206A\u202B\u202D\u202A\u206D\u206E\u200D\u202C\u202D\u200B\u206B\u206B\u200B\u206E\u202B\u206A\u200C\u206F\u206C\u206C\u202D\u200F\u200F\u200E\u202E\u206A\u206F\u206B\u202C\u200E\u200B\u200D\u200E\u202B\u202E.TargetId);
			}

			// Token: 0x06000E3E RID: 3646 RVA: 0x000F1484 File Offset: 0x000EF684
			internal bool \u202D\u206C\u202A\u206C\u200F\u202A\u206C\u202C\u202B\u202E\u206A\u200E\u206E\u202A\u206F\u202B\u200B\u200B\u206E\u200C\u202C\u206B\u200B\u202D\u206C\u202D\u200F\u200F\u202D\u200E\u202B\u200D\u206C\u206E\u206C\u200B\u200F\u206C\u202A\u206F\u202E(Settlement A_1)
			{
				return \u206D\u206F\u200C\u202A\u200E\u200D\u202D\u200C\u200B\u200C\u200B\u202B\u202D\u202B\u200F\u206C\u200D\u206A\u202E\u206E\u202A\u202C\u206C\u200B\u206B\u206B\u200C\u206D\u202C\u206D\u206F\u202D\u206A\u202C\u202A\u202B\u206F\u206C\u202A\u202A\u202E.\u0081íè\u0004A(A_1) != null && \u200D\u206C\u200F\u206C\u202B\u202A\u200B\u202D\u202B\u200F\u206D\u200C\u202B\u200E\u206E\u200C\u202E\u200E\u202D\u202C\u202A\u206C\u200F\u200C\u202C\u202E\u200B\u202D\u206C\u202A\u202C\u200F\u200B\u206A\u200C\u206A\u202A\u202A\u206D\u202A\u202E.\u001F&\u009DUÈ(\u206D\u206F\u200C\u202A\u200E\u200D\u202D\u200C\u200B\u200C\u200B\u202B\u202D\u202B\u200F\u206C\u200D\u206A\u202E\u206E\u202A\u202C\u206C\u200B\u206B\u206B\u200C\u206D\u202C\u206D\u206F\u202D\u206A\u202C\u202A\u202B\u206F\u206C\u202A\u202A\u202E.\u0081íè\u0004A(A_1)) == this.\u202C\u206E\u206F\u202B\u206F\u206F\u206D\u206F\u202D\u202D\u206E\u202D\u202C\u206F\u206E\u200F\u200E\u200B\u200B\u202D\u200E\u200B\u200D\u200D\u206B\u200F\u202D\u206F\u200B\u206C\u202D\u206E\u202B\u202E\u206E\u200B\u206F\u206E\u200E\u200C\u202E;
			}

			// Token: 0x04000909 RID: 2313
			public EconomicEffect \u200E\u202B\u206E\u202A\u206A\u206D\u206A\u202B\u202D\u202A\u206D\u206E\u200D\u202C\u202D\u200B\u206B\u206B\u200B\u206E\u202B\u206A\u200C\u206F\u206C\u206C\u202D\u200F\u200F\u200E\u202E\u206A\u206F\u206B\u202C\u200E\u200B\u200D\u200E\u202B\u202E;

			// Token: 0x0400090A RID: 2314
			public Kingdom \u202C\u206E\u206F\u202B\u206F\u206F\u206D\u206F\u202D\u202D\u206E\u202D\u202C\u206F\u206E\u200F\u200E\u200B\u200B\u202D\u200E\u200B\u200D\u200D\u206B\u200F\u202D\u206F\u200B\u206C\u202D\u206E\u202B\u202E\u206E\u200B\u206F\u206E\u200E\u200C\u202E;
		}

		// Token: 0x020001BD RID: 445
		[CompilerGenerated]
		private sealed class \u202A\u206A\u200F\u200C\u206A\u200D\u202B\u202C\u202E\u206C\u200F\u206C\u202D\u206C\u206E\u200E\u206A\u206D\u206D\u200D\u200E\u200B\u206F\u202A\u206E\u206E\u202A\u200F\u206D\u202D\u206C\u200C\u202C\u206B\u202A\u202B\u202A\u206F\u200F\u200C\u202E
		{
			// Token: 0x06000E40 RID: 3648 RVA: 0x000F14C0 File Offset: 0x000EF6C0
			internal bool \u202A\u206C\u200D\u202C\u200D\u206B\u200B\u206B\u200E\u206F\u202B\u206C\u200C\u206F\u200E\u200E\u206E\u206A\u206D\u200C\u206F\u200D\u206D\u202A\u202D\u206B\u206C\u202A\u200C\u206C\u206E\u202E\u206B\u200D\u206E\u202A\u202A\u200C\u206C\u202E\u202E(Clan A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u200B\u206D\u206B\u200F\u202E\u206A\u206A\u200C\u200D\u200D\u206E\u206A\u202C\u206D\u202E\u200B\u200E\u202E\u200B\u206B\u206E\u202B\u206F\u202B\u200C\u202A\u200B\u200D\u202A\u200B\u200B\u206C\u200D\u202C\u202D\u206A\u200D\u202E\u200C\u200D\u202E.TargetId);
			}

			// Token: 0x06000E41 RID: 3649 RVA: 0x000F14F0 File Offset: 0x000EF6F0
			internal bool \u200B\u206C\u206E\u200E\u200D\u206D\u202A\u200C\u202E\u200B\u202C\u200D\u200B\u200B\u202C\u200C\u206C\u202A\u206C\u206F\u206E\u200D\u206F\u200D\u202D\u206F\u200F\u202E\u200C\u200B\u200C\u200E\u206A\u200B\u206E\u200C\u200C\u206A\u200D\u206B\u202E(Settlement A_1)
			{
				return \u206D\u206F\u200C\u202A\u200E\u200D\u202D\u200C\u200B\u200C\u200B\u202B\u202D\u202B\u200F\u206C\u200D\u206A\u202E\u206E\u202A\u202C\u206C\u200B\u206B\u206B\u200C\u206D\u202C\u206D\u206F\u202D\u206A\u202C\u202A\u202B\u206F\u206C\u202A\u202A\u202E.\u0081íè\u0004A(A_1) == this.\u200E\u202A\u206D\u206D\u202D\u200C\u202D\u202A\u202C\u206E\u206B\u200B\u206F\u206A\u206B\u200C\u202D\u206B\u200F\u200B\u206E\u202D\u202E\u202B\u202A\u202A\u200B\u200D\u200E\u200E\u202D\u200C\u200E\u202C\u202D\u206B\u206A\u202C\u202C\u200E\u202E;
			}

			// Token: 0x0400090B RID: 2315
			public EconomicEffect \u200B\u206D\u206B\u200F\u202E\u206A\u206A\u200C\u200D\u200D\u206E\u206A\u202C\u206D\u202E\u200B\u200E\u202E\u200B\u206B\u206E\u202B\u206F\u202B\u200C\u202A\u200B\u200D\u202A\u200B\u200B\u206C\u200D\u202C\u202D\u206A\u200D\u202E\u200C\u200D\u202E;

			// Token: 0x0400090C RID: 2316
			public Clan \u200E\u202A\u206D\u206D\u202D\u200C\u202D\u202A\u202C\u206E\u206B\u200B\u206F\u206A\u206B\u200C\u202D\u206B\u200F\u200B\u206E\u202D\u202E\u202B\u202A\u202A\u200B\u200D\u200E\u200E\u202D\u200C\u200E\u202C\u202D\u206B\u206A\u202C\u202C\u200E\u202E;
		}

		// Token: 0x020001BE RID: 446
		[CompilerGenerated]
		private sealed class \u200F\u202A\u206B\u200F\u206F\u206A\u200E\u200E\u206B\u202D\u202E\u200D\u202B\u206E\u200D\u202A\u206A\u206B\u206E\u202E\u206C\u200B\u206F\u200D\u206A\u202C\u200E\u202B\u200B\u206C\u202D\u206B\u206D\u206D\u206C\u202C\u200E\u202A\u200B\u206C\u202E
		{
			// Token: 0x06000E43 RID: 3651 RVA: 0x000F1510 File Offset: 0x000EF710
			internal bool \u206D\u200F\u206E\u206B\u206E\u200F\u206A\u200C\u200E\u206D\u206A\u200C\u200E\u206B\u206C\u202C\u200F\u200E\u206D\u200D\u200B\u202A\u200F\u200B\u206E\u206E\u200B\u200E\u202E\u206A\u206F\u206F\u206B\u206F\u200F\u202B\u206D\u206A\u202D\u206E\u202E(Settlement A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u202A\u206F\u202B\u206D\u202E\u202B\u202E\u202A\u206C\u206C\u206D\u202A\u202C\u200D\u206A\u206C\u206E\u206F\u200C\u206C\u202C\u202A\u202D\u202D\u206F\u206B\u202E\u200F\u202B\u202B\u200C\u206F\u200B\u202B\u206D\u206E\u206A\u200B\u206E\u202B\u202E);
			}

			// Token: 0x0400090D RID: 2317
			public string \u202A\u206F\u202B\u206D\u202E\u202B\u202E\u202A\u206C\u206C\u206D\u202A\u202C\u200D\u206A\u206C\u206E\u206F\u200C\u206C\u202C\u202A\u202D\u202D\u206F\u206B\u202E\u200F\u202B\u202B\u200C\u206F\u200B\u202B\u206D\u206E\u206A\u200B\u206E\u202B\u202E;
		}

		// Token: 0x020001BF RID: 447
		[CompilerGenerated]
		private sealed class \u206A\u200B\u202A\u200D\u206B\u200F\u202E\u206F\u202B\u202A\u202A\u202A\u200F\u200B\u202A\u206C\u202C\u200E\u206B\u202B\u202E\u202E\u206B\u202D\u200C\u200C\u206A\u206B\u200D\u206E\u206A\u202A\u206A\u202D\u206F\u206C\u202E\u206D\u206C\u206F\u202E
		{
			// Token: 0x06000E45 RID: 3653 RVA: 0x000F1538 File Offset: 0x000EF738
			internal bool \u202A\u200B\u206B\u206D\u200C\u200D\u206D\u200B\u206D\u206C\u206E\u202E\u200F\u202A\u200D\u200D\u202E\u206A\u200D\u202D\u202B\u206A\u206B\u202E\u202C\u202B\u202B\u200D\u202B\u206C\u202E\u202E\u206F\u206B\u200C\u206A\u200C\u202D\u202E\u206E\u202E(Settlement A_1)
			{
				return \u200E\u206F\u200C\u206D\u202D\u206C\u200E\u202E\u200B\u200D\u200C\u200E\u206E\u202A\u202C\u206A\u206C\u206A\u202E\u202B\u200B\u202B\u202B\u202A\u206A\u200E\u206C\u206B\u202B\u200E\u200C\u206E\u202A\u202B\u206E\u200C\u206A\u206A\u206F\u206A\u202E.\u008A\u0016\u00B1ùô(\u206E\u206C\u200F\u202D\u200E\u206C\u206C\u206F\u200D\u206D\u202E\u206F\u202D\u200C\u206D\u206D\u206D\u206F\u200F\u206E\u202A\u200F\u206C\u206D\u202C\u206F\u206D\u206D\u202B\u202A\u206F\u206C\u206C\u200F\u206D\u200B\u206D\u202A\u206F\u200C\u202E.Ôés[}(A_1), this.\u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E.TargetId);
			}

			// Token: 0x0400090E RID: 2318
			public ActiveEconomicEffect \u206E\u206E\u206E\u202B\u206C\u200F\u200F\u206A\u206F\u202A\u206E\u202A\u206A\u206C\u206A\u206E\u206C\u202B\u200F\u200B\u206A\u200B\u206E\u200C\u202C\u202B\u200B\u206F\u206A\u206C\u206A\u206F\u206D\u202A\u202A\u200C\u200C\u200E\u206C\u206A\u202E;
		}
	}
}
