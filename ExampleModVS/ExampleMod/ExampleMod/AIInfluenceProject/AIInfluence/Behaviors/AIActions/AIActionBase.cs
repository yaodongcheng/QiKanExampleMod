using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;
using \u202C\u200B\u200B\u200D\u206B\u202A\u202D\u200E\u202E\u200E\u200D\u206A\u206C\u206A\u202C\u206A\u206D\u206B\u206C\u200E\u206C\u200F\u200F\u206E\u206E\u206C\u206F\u206E\u202E\u206E\u202C\u200D\u202A\u202B\u200B\u200C\u206A\u202D\u202B\u202D\u202E;
using \u202E\u202B\u200D\u202E\u206A\u206A\u206C\u200B\u202C\u202C\u200C\u202B\u202C\u200F\u206C\u202E\u200D\u200F\u200F\u200F\u206B\u206F\u202C\u206D\u206C\u206C\u206F\u200B\u200D\u206A\u202A\u202E\u200B\u206A\u202E\u202A\u206D\u200C\u200E\u206E\u202E;
using \u206F\u202A\u206F\u202C\u206D\u202C\u200B\u200D\u206E\u206B\u202A\u206B\u206D\u200F\u206B\u202B\u200F\u200E\u202B\u206B\u202B\u200B\u206D\u202B\u202D\u202B\u202A\u200F\u202C\u202E\u200D\u200B\u206B\u206E\u202A\u206A\u202D\u200B\u200F\u206C\u202E;

namespace AIInfluence.Behaviors.AIActions
{
	// Token: 0x020002DC RID: 732
	public abstract class AIActionBase
	{
		// Token: 0x170003D4 RID: 980
		// (get) Token: 0x06001465 RID: 5221
		public abstract string ActionName { get; }

		// Token: 0x170003D5 RID: 981
		// (get) Token: 0x06001466 RID: 5222
		public abstract string Description { get; }

		// Token: 0x170003D6 RID: 982
		// (get) Token: 0x06001467 RID: 5223 RVA: 0x001282D0 File Offset: 0x001264D0
		// (set) Token: 0x06001468 RID: 5224 RVA: 0x001282E4 File Offset: 0x001264E4
		private protected Hero TargetHero { protected get; private set; }

		// Token: 0x170003D7 RID: 983
		// (get) Token: 0x06001469 RID: 5225 RVA: 0x001282F8 File Offset: 0x001264F8
		// (set) Token: 0x0600146A RID: 5226 RVA: 0x0012830C File Offset: 0x0012650C
		protected Agent TargetAgent { get; set; }

		// Token: 0x170003D8 RID: 984
		// (get) Token: 0x0600146B RID: 5227 RVA: 0x00128320 File Offset: 0x00126520
		// (set) Token: 0x0600146C RID: 5228 RVA: 0x00128334 File Offset: 0x00126534
		public bool IsActive { get; protected set; }

		// Token: 0x170003D9 RID: 985
		// (get) Token: 0x0600146D RID: 5229 RVA: 0x00128348 File Offset: 0x00126548
		// (set) Token: 0x0600146E RID: 5230 RVA: 0x0012835C File Offset: 0x0012655C
		private protected DateTime StartTime { protected get; private set; }

		// Token: 0x0600146F RID: 5231 RVA: 0x00128370 File Offset: 0x00126570
		public virtual void Initialize(Hero hero)
		{
			this.TargetHero = hero;
			this.StartTime = \u200B\u202A\u206E\u202A\u202D\u206D\u202B\u200E\u202D\u200F\u206D\u206A\u202B\u206D\u202C\u200F\u206F\u202C\u202C\u200C\u202C\u200F\u200F\u202E\u202E\u206B\u206C\u206D\u206C\u200D\u202A\u200C\u206E\u202E\u200E\u200E\u206D\u202B\u200D\u200D\u202E.Y\u000C8FÛ();
			bool flag = \u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5() != null;
			if (flag)
			{
				for (;;)
				{
					IL_2B:
					int num = -2037657266;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(~(661810199 + -438883260) + (-1962309706 - 1501895675 + --330731123) - num2 ^ ~(-1096352265) ^ (-1557218756 ^ 549789935))) % 3U)
						{
						case 0U:
							goto IL_2B;
						case 1U:
							this.TargetAgent = this.FindAgentForHero(hero);
							num = (int)(num3 * 4271397471U ^ 2391651633U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
			this.IsActive = false;
		}

		// Token: 0x06001470 RID: 5232 RVA: 0x00128424 File Offset: 0x00126624
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual void Start()
		{
			bool isActive = this.IsActive;
			if (isActive)
			{
				goto IL_0B;
			}
			goto IL_60;
			int num2;
			for (;;)
			{
				IL_10:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(uint)(~(num - (-1138291497 ^ ~(-949397003 ^ 1778616335))) * 1860105145))) % 6U)
				{
				case 0U:
					this.IsActive = true;
					this.StartTime = \u200B\u202A\u206E\u202A\u202D\u206D\u202B\u200E\u202D\u200F\u206D\u206A\u202B\u206D\u202C\u200F\u206F\u202C\u202C\u200C\u202C\u200F\u200F\u202E\u202E\u206B\u206C\u206D\u206C\u200D\u202A\u200C\u206E\u202E\u200E\u200E\u206D\u202B\u200D\u200D\u202E.Y\u000C8FÛ();
					this.OnStart();
					num2 = -2123333033;
					continue;
				case 1U:
					num2 = (int)(num3 * 2912923432U ^ 773712319U);
					continue;
				case 3U:
					this.LogError(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1992626020));
					num2 = -2123333033;
					continue;
				case 4U:
					goto IL_0B;
				case 5U:
					goto IL_60;
				}
				break;
			}
			return;
			IL_0B:
			num2 = 1088548562;
			goto IL_10;
			IL_60:
			num2 = ((this.TargetHero == null) ? -2066189950 : -759849257);
			goto IL_10;
		}

		// Token: 0x06001471 RID: 5233 RVA: 0x001284F8 File Offset: 0x001266F8
		public virtual void Stop()
		{
			bool flag = !this.IsActive;
			if (flag)
			{
				goto IL_0E;
			}
			goto IL_56;
			int num2;
			for (;;)
			{
				IL_13:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(num - ~(-(1670037058 - -1242067414))) + ~-1362807135)) % 4U)
				{
				case 1U:
					goto IL_56;
				case 2U:
					num2 = (int)(num3 * 3060496250U ^ 3468478809U);
					continue;
				case 3U:
					goto IL_0E;
				}
				break;
			}
			return;
			IL_0E:
			num2 = -329942437;
			goto IL_13;
			IL_56:
			this.IsActive = false;
			this.OnStop();
			num2 = -1562156739;
			goto IL_13;
		}

		// Token: 0x06001472 RID: 5234 RVA: 0x00128574 File Offset: 0x00126774
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual void Update(float deltaTime)
		{
			bool flag = !this.IsActive;
			if (flag)
			{
				goto IL_0E;
			}
			goto IL_5C;
			int num2;
			for (;;)
			{
				IL_13:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-327962204 - -(uint)(~(uint)(num * 198915401)))) % 7U)
				{
				case 1U:
					this.OnUpdate(deltaTime);
					num2 = -1307720043;
					continue;
				case 2U:
					goto IL_6A;
				case 3U:
				{
					string text = <Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-721369255);
					Hero targetHero = this.TargetHero;
					this.LogAction(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E.\u206B\u206E\u200F\u200B\u202C\u202E\u202A\u202B\u206F\u206F\u202D\u200F\u202B\u202B\u202E\u200B\u206D\u200C\u206B\u206F\u200F\u200D\u206D\u202B\u200B\u206F\u202C\u200B\u200D\u206C\u200F\u202C\u200E\u200D\u202A\u206F\u206D\u200E\u200F\u206A\u202E(text, (targetHero != null) ? \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.\u206E\u202B\u200F\u202D\u206C\u206E\u202C\u206B\u206D\u202D\u202E\u200E\u206D\u200E\u200F\u206C\u206D\u206A\u200D\u206F\u202D\u202E\u200C\u206D\u206A\u202E\u206D\u206C\u200D\u200C\u200E\u202B\u202D\u206D\u200B\u202D\u206D\u200B\u206A\u206B\u202E(targetHero) : null));
					this.TargetAgent = this.FindAgentForHero(this.TargetHero);
					this.LogAction(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(764193634), this.TargetAgent != null));
					num2 = -609909660;
					continue;
				}
				case 4U:
					goto IL_0E;
				case 5U:
					num2 = (int)(num3 * 3490538128U ^ 4129378021U);
					continue;
				case 6U:
					goto IL_5C;
				}
				break;
			}
			return;
			IL_6A:
			bool flag2 = this.TargetAgent == null;
			goto IL_76;
			IL_0E:
			num2 = 700801748;
			goto IL_13;
			IL_5C:
			if (\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u206A\u206A\u206C\u206D\u206A\u206C\u200B\u206A\u200C\u202B\u200C\u206D\u200C\u202C\u202D\u206E\u206D\u206A\u200D\u206F\u200B\u200E\u200C\u206A\u202E\u206E\u206D\u200F\u200D\u206B\u200D\u200F\u206A\u202D\u202A\u202A\u200F\u200E\u200F\u206B\u202E() != null)
			{
				num2 = -1601878344;
				goto IL_13;
			}
			flag2 = false;
			IL_76:
			bool flag3 = flag2;
			num2 = ((!flag3) ? -609909660 : 370403713);
			goto IL_13;
		}

		// Token: 0x06001473 RID: 5235
		public abstract bool CanExecute();

		// Token: 0x06001474 RID: 5236
		protected abstract void OnStart();

		// Token: 0x06001475 RID: 5237
		protected abstract void OnStop();

		// Token: 0x06001476 RID: 5238
		protected abstract void OnUpdate(float deltaTime);

		// Token: 0x06001477 RID: 5239 RVA: 0x00128694 File Offset: 0x00126894
		[MethodImpl(MethodImplOptions.NoInlining)]
		protected Agent FindAgentForHero(Hero hero)
		{
			bool flag;
			if (\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5() == null)
			{
				flag = true;
				goto IL_43;
			}
			IL_0D:
			int num = 1430733524;
			IL_12:
			int num2 = num;
			switch (-(-(~(num2 ^ ~348673045))) % 4)
			{
			case 0:
				this.LogAction(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(750962353));
				return null;
			case 1:
				flag = (hero == null);
				goto IL_43;
			case 2:
				goto IL_0D;
			}
			this.LogAction(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.jèCÕ4(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(62882952), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero), \u202B\u200F\u206F\u206E\u206E\u206B\u206D\u202A\u202C\u200B\u200D\u200D\u206B\u206F\u200B\u200D\u206F\u206A\u206E\u202A\u200C\u206D\u202A\u206D\u206B\u206E\u202A\u202B\u200F\u202E\u202A\u206C\u202E\u200F\u200B\u200D\u206F\u206D\u206F\u202E\u202E.X\u00A1+:\u0091(\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5()).Count));
			using (List<Agent>.Enumerator enumerator = \u202B\u200F\u206F\u206E\u206E\u206B\u206D\u202A\u202C\u200B\u200D\u200D\u206B\u206F\u200B\u200D\u206F\u206A\u206E\u202A\u200C\u206D\u202A\u206D\u206B\u206E\u202A\u202B\u200F\u202E\u202A\u206C\u202E\u200F\u200B\u200D\u206F\u206D\u206F\u202E\u202E.X\u00A1+:\u0091(\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5()).GetEnumerator())
			{
				Agent agent;
				for (;;)
				{
					IL_38C:
					int num3 = enumerator.MoveNext() ? 752275116 : 2014440044;
					for (;;)
					{
						num2 = num3;
						uint num4;
						bool flag2;
						bool flag4;
						switch ((num4 = (uint)(-(uint)(-(uint)(~(uint)(num2 ^ ~348673045))))) % 15U)
						{
						case 0U:
							num3 = 752275116;
							continue;
						case 1U:
							num3 = 1628622218;
							continue;
						case 2U:
							num3 = 1447614868;
							continue;
						case 3U:
						{
							CharacterObject characterObject = \u200B\u206A\u200E\u202A\u206B\u202D\u206A\u200B\u206C\u206C\u206E\u206C\u206D\u206A\u200F\u206F\u206A\u202C\u206A\u200F\u206C\u200F\u202E\u202E\u206A\u200C\u202D\u206C\u200C\u206C\u202A\u202D\u206D\u202A\u200C\u200D\u200E\u202D\u206C\u202C\u202E.\u0081\u001B9S\u00B0(agent) as CharacterObject;
							flag2 = (characterObject != null);
							goto IL_159;
						}
						case 4U:
							num3 = 1623082581;
							continue;
						case 5U:
							agent = enumerator.Current;
							if (agent != null)
							{
								num3 = 1356492517;
								continue;
							}
							goto IL_18E;
						case 7U:
						{
							string text = <Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2119401998);
							CharacterObject characterObject;
							object obj = \u206A\u200D\u206F\u202D\u206B\u200E\u206D\u206D\u206E\u206F\u206B\u202B\u206C\u206A\u206B\u206E\u206C\u202A\u206C\u200E\u206C\u200B\u200B\u202E\u206C\u206C\u200E\u200B\u206C\u202D\u200F\u206C\u202D\u200B\u206F\u206F\u202D\u200F\u202E\u200D\u202E.-\u0080w\u00938(characterObject);
							Hero hero2 = \u206E\u206F\u200D\u206A\u206E\u200E\u202B\u206C\u200D\u202A\u200C\u202C\u200F\u202E\u206F\u206A\u206F\u202C\u202A\u206F\u202A\u200D\u202A\u206C\u202D\u202A\u206E\u200B\u206F\u200D\u202C\u202C\u200B\u206B\u202C\u200F\u206A\u202C\u206D\u202E\u202E.\u009D-ÙHI(characterObject);
							this.LogAction(\u200B\u202D\u206E\u206D\u200D\u200B\u206B\u200B\u200E\u206C\u202B\u200D\u200D\u206C\u206B\u206E\u206C\u200E\u202A\u200F\u202C\u206F\u200D\u202E\u202C\u200F\u202D\u206A\u202A\u200C\u200F\u200F\u206C\u206D\u206C\u202E\u206C\u200F\u200D\u206B\u202E.\u206E\u200E\u200C\u202C\u206A\u206D\u200F\u202A\u202E\u206C\u206B\u206A\u206A\u206E\u202B\u206D\u202E\u202B\u200E\u200B\u200F\u206B\u200F\u200C\u202E\u206F\u200F\u200F\u200F\u202C\u200C\u206A\u206F\u206E\u200E\u200C\u206A\u206B\u200B\u206C\u202E(text, obj, (hero2 != null) ? \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.\u206E\u202B\u200F\u202D\u206C\u206E\u202C\u206B\u206D\u202D\u202E\u200E\u206D\u200E\u200F\u206C\u206D\u206A\u200D\u206F\u202D\u202E\u200C\u206D\u206A\u202E\u206D\u206C\u200D\u200C\u200E\u202B\u202D\u206D\u200B\u202D\u206D\u200B\u206A\u206B\u202E(hero2) : null));
							bool flag3 = \u206E\u206F\u200D\u206A\u206E\u200E\u202B\u206C\u200D\u202A\u200C\u202C\u200F\u202E\u206F\u206A\u206F\u202C\u202A\u206F\u202A\u200D\u202A\u206C\u202D\u202A\u206E\u200B\u206F\u200D\u202C\u202C\u200B\u206B\u202C\u200F\u206A\u202C\u206D\u202E\u202E.\u009D-ÙHI(characterObject) == hero;
							num3 = ((!flag3) ? 2107358861 : 943952077);
							continue;
						}
						case 8U:
							this.LogAction(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.\u200D\u206D\u206F\u202A\u200B\u202B\u206B\u200F\u202B\u202E\u202B\u202E\u206E\u200F\u202C\u200F\u202A\u206D\u200D\u206C\u202A\u200F\u202D\u206F\u206E\u206C\u206B\u206D\u202E\u200E\u206D\u206E\u206C\u206B\u200F\u200D\u202A\u202A\u202C\u200F\u202E(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1887537358), agent == null, (agent != null) ? new bool?(\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.+Üh\u00A0\u0099(agent)) : null, (agent != null) ? new bool?(\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.1{=æ|(agent)) : null));
							num3 = 1628622218;
							continue;
						case 9U:
							if (\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.}\u0008\u00A6*\u0001(agent))
							{
								num3 = (int)(num4 * 1428605110U ^ 1101499669U);
								continue;
							}
							goto IL_18E;
						case 10U:
							goto IL_34A;
						case 11U:
							this.LogAction(<Module>.\u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<string>(16495303));
							num3 = 1623082581;
							continue;
						case 12U:
							this.LogAction(\u206B\u202E\u206F\u206A\u200D\u206B\u202D\u202E\u206F\u200D\u206F\u200B\u206A\u206B\u206C\u200E\u202C\u202E\u206F\u200B\u206B\u206B\u200B\u200F\u200D\u200B\u202B\u200D\u202B\u202D\u206A\u206B\u200F\u202E\u206F\u206B\u206D\u202C\u206E\u200D\u202E.y=\u001E/\u0091(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(2137043706), \u202B\u206E\u200F\u202B\u200F\u200C\u200B\u200E\u202D\u202A\u202A\u200C\u206D\u200B\u206B\u206E\u200E\u202E\u206D\u202D\u206D\u202A\u202C\u206F\u200B\u200D\u202E\u202A\u202B\u206E\u202A\u202A\u206C\u202D\u202D\u202A\u206C\u206F\u206C\u206C\u202E.\u0002\u0096\u008DÌ\u0001(agent), \u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.}\u0008\u00A6*\u0001(agent), \u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.sµ(Ò\u00B0(agent)));
							if (\u200B\u206A\u200E\u202A\u206B\u202D\u206A\u200B\u206C\u206C\u206E\u206C\u206D\u206A\u200F\u206F\u206A\u202C\u206A\u200F\u206C\u200F\u202E\u202E\u206A\u200C\u202D\u206C\u200C\u206C\u202A\u202D\u206D\u202A\u200C\u200D\u200E\u202D\u206C\u202C\u202E.\u0081\u001B9S\u00B0(agent) != null)
							{
								num3 = 80551694;
								continue;
							}
							flag2 = false;
							goto IL_159;
						case 13U:
							flag4 = !\u200D\u206A\u202B\u200B\u206E\u206F\u202D\u206A\u202A\u206C\u206F\u202C\u202B\u206A\u202B\u200B\u202B\u202D\u202C\u200C\u202D\u206A\u206B\u206C\u202E\u206A\u200F\u202B\u202C\u206F\u202B\u200D\u200C\u206B\u206C\u206E\u206E\u206D\u202C\u202E.sµ(Ò\u00B0(agent);
							goto IL_18F;
						case 14U:
							goto IL_38C;
						}
						goto Block_5;
						IL_159:
						num3 = (flag2 ? 153510780 : 1888461300);
						continue;
						IL_18F:
						bool flag5 = flag4;
						num3 = ((!flag5) ? 1598599744 : 462019578);
						continue;
						IL_18E:
						flag4 = false;
						goto IL_18F;
					}
				}
				Block_5:
				goto IL_3A9;
				IL_34A:
				this.LogAction(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1900891061), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
				return agent;
				IL_3A9:;
			}
			this.LogAction(\u200B\u200E\u202C\u206F\u202C\u202E\u206E\u200D\u202B\u202A\u202E\u202E\u200E\u202D\u202E\u200F\u202D\u202B\u202B\u202C\u202A\u206A\u200B\u202D\u202E\u202B\u200F\u202B\u206F\u202C\u200B\u206A\u202C\u202E\u200D\u202B\u200F\u200D\u202A\u200C\u202E./\u000FÙü\u0008(<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(-1454626113), \u206B\u206B\u200D\u206C\u200C\u200D\u206F\u200B\u206C\u202D\u202C\u202A\u200E\u206B\u200C\u200C\u206A\u206A\u200E\u206A\u200D\u202D\u206F\u200C\u202B\u206D\u202C\u200D\u200F\u202C\u202D\u206B\u202E\u206B\u202C\u202D\u202D\u206A\u200F\u206D\u202E.Yð\u00D7\u00A1\u009B(hero)));
			return null;
			IL_43:
			num = (flag ? 2051212133 : 685771066);
			goto IL_12;
		}

		// Token: 0x06001478 RID: 5240 RVA: 0x00128AA4 File Offset: 0x00126CA4
		protected Agent GetPlayerAgent()
		{
			Mission mission = \u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5();
			return (mission != null) ? \u206F\u206C\u200D\u202B\u200B\u202D\u202A\u202D\u206B\u200E\u202D\u200E\u206B\u206C\u206D\u206F\u206D\u206E\u206A\u206F\u206B\u202A\u200F\u200C\u200F\u206A\u202C\u202B\u206C\u202C\u206D\u206A\u200E\u206E\u200D\u200F\u200C\u206E\u206B\u200E\u202E.\u206C\u200B\u200B\u202D\u202A\u202E\u202C\u206E\u206A\u200F\u202B\u200B\u200E\u200E\u206E\u200B\u200B\u200B\u200E\u206A\u202A\u206F\u206D\u206E\u206D\u202C\u206B\u206F\u206C\u202B\u206B\u200F\u206E\u200B\u200C\u202D\u200D\u200B\u200F\u202A\u202E(mission) : null;
		}

		// Token: 0x06001479 RID: 5241 RVA: 0x00128ACC File Offset: 0x00126CCC
		[MethodImpl(MethodImplOptions.NoInlining)]
		protected void LogAction(string message)
		{
			AIActionsLogger.Instance.Log(\u200C\u200C\u206A\u206D\u206E\u202D\u200D\u200D\u206A\u200E\u200D\u206A\u202E\u206E\u202B\u200C\u200B\u202B\u206D\u206C\u202A\u200F\u206B\u206D\u206C\u206E\u200F\u206C\u206A\u206F\u206F\u206C\u202B\u206A\u206D\u200E\u202A\u200C\u206F\u200E\u202E.\u000BÃ>Ø\u00A4(<Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(-1026921846), this.ActionName, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(176393030), message));
		}

		// Token: 0x0600147A RID: 5242 RVA: 0x00128B10 File Offset: 0x00126D10
		[MethodImpl(MethodImplOptions.NoInlining)]
		protected void LogError(string message)
		{
			AIActionsLogger.Instance.Log(\u200C\u200C\u206A\u206D\u206E\u202D\u200D\u200D\u206A\u200E\u200D\u206A\u202E\u206E\u202B\u200C\u200B\u202B\u206D\u206C\u202A\u200F\u206B\u206D\u206C\u206E\u200F\u206C\u206A\u206F\u206F\u206C\u202B\u206A\u206D\u200E\u202A\u200C\u206F\u200E\u202E.\u000BÃ>Ø\u00A4(<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1517608072), this.ActionName, <Module>.\u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<string>(1223656917), message));
		}

		// Token: 0x0600147B RID: 5243 RVA: 0x00128B54 File Offset: 0x00126D54
		protected bool IsInMission()
		{
			bool flag;
			if (\u202D\u206F\u200D\u200D\u200F\u202D\u202B\u200D\u200C\u206C\u206E\u202B\u202D\u206A\u202D\u202B\u202C\u202D\u200C\u200D\u202C\u200E\u206C\u200E\u206C\u202C\u200E\u200C\u206D\u200E\u200C\u202B\u206F\u206E\u206B\u202C\u202C\u202C\u206A\u200E\u202E.\u009A\u0088áø5() == null)
			{
				flag = false;
				goto IL_6F;
			}
			IL_0D:
			int num = -492704560;
			IL_12:
			int num2 = num;
			switch (~((num2 + (~-1157376489 * -2094199523 ^ (-311859482 * 1514028797 ^ (-1854533385 ^ -1180463894))) ^ -1428839870) + (442572956 ^ -544583520)) % 3)
			{
			case 1:
				flag = (this.TargetAgent != null);
				goto IL_6F;
			case 2:
				goto IL_0D;
			}
			bool result;
			return result;
			IL_6F:
			result = flag;
			num = 1114991936;
			goto IL_12;
		}

		// Token: 0x0600147C RID: 5244 RVA: 0x00128BDC File Offset: 0x00126DDC
		protected bool IsInPlayerParty()
		{
			if (this.TargetHero != null)
			{
				goto IL_09;
			}
			bool flag = true;
			goto IL_65;
			int num2;
			bool result;
			for (;;)
			{
				IL_0E:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(-(num + (1830926385 - (926407590 - 723694663 - ~1242498541))) + (2136396808 - -696353837))) % 5U)
				{
				case 0U:
					result = false;
					num2 = (int)(num3 * 16520631U ^ 266036494U);
					continue;
				case 2U:
					goto IL_09;
				case 3U:
					goto IL_55;
				case 4U:
					result = (\u202A\u206A\u206E\u200D\u206E\u206C\u206A\u200B\u202B\u202A\u202B\u200F\u200E\u202B\u202D\u202C\u206C\u200C\u206E\u200C\u202D\u206C\u206B\u202A\u206D\u200E\u206B\u200B\u206D\u206B\u206B\u202C\u202A\u200E\u200D\u206D\u200E\u206C\u206C\u200E\u202E.\u001Fúæ}Ç(this.TargetHero) == \u200D\u202D\u206D\u206F\u202E\u200B\u206D\u202C\u200E\u202C\u206F\u202C\u206A\u206F\u206B\u206E\u200E\u200F\u206C\u206C\u206D\u206C\u202C\u206A\u206A\u202A\u200F\u200B\u200D\u200F\u202C\u200B\u202C\u202A\u200C\u200F\u200E\u206F\u206E\u200D\u202E.ÜV\u00177\u0083());
					num2 = 1613427133;
					continue;
				}
				break;
			}
			return result;
			IL_55:
			flag = (\u200D\u202D\u206D\u206F\u202E\u200B\u206D\u202C\u200E\u202C\u206F\u202C\u206A\u206F\u206B\u206E\u200E\u200F\u206C\u206C\u206D\u206C\u202C\u206A\u206A\u202A\u200F\u200B\u200D\u200F\u202C\u200B\u202C\u202A\u200C\u200F\u200E\u206F\u206E\u200D\u202E.ÜV\u00177\u0083() == null);
			goto IL_65;
			IL_09:
			num2 = 678675246;
			goto IL_0E;
			IL_65:
			bool flag2 = flag;
			num2 = ((!flag2) ? -2071725286 : -2097433492);
			goto IL_0E;
		}

		// Token: 0x0600147D RID: 5245 RVA: 0x00128CA0 File Offset: 0x00126EA0
		public virtual Dictionary<string, string> GetStateDataForSave()
		{
			return null;
		}

		// Token: 0x04000EBF RID: 3775
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Hero \u206F\u206A\u200F\u200D\u206E\u206B\u206E\u206E\u206D\u206B\u200B\u206D\u202E\u200D\u206B\u206B\u206C\u206F\u202B\u206C\u206A\u206F\u200D\u200C\u202A\u202A\u202E\u206B\u200C\u206D\u202D\u206F\u206C\u202A\u202B\u200D\u200C\u206B\u206E\u206E\u202E;

		// Token: 0x04000EC0 RID: 3776
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private Agent \u200B\u206D\u202B\u206C\u206E\u200B\u200F\u206D\u206F\u200D\u202B\u206B\u206F\u202D\u206C\u202D\u206C\u202A\u202C\u206B\u206C\u206A\u202B\u202C\u200D\u200F\u200C\u206A\u206F\u200B\u200C\u202B\u200F\u200E\u202A\u202C\u206C\u200F\u206A\u202E;

		// Token: 0x04000EC1 RID: 3777
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private bool \u206D\u200D\u202A\u200E\u200F\u206D\u202B\u206D\u200E\u206F\u200F\u202B\u202B\u200C\u200D\u200D\u202B\u206D\u206C\u200C\u200D\u206A\u200F\u206D\u202B\u200F\u206F\u202D\u206F\u206F\u200F\u200B\u200C\u206F\u206B\u200E\u200E\u200E\u202A\u200F\u202E;

		// Token: 0x04000EC2 RID: 3778
		[CompilerGenerated]
		[DebuggerBrowsable(DebuggerBrowsableState.Never)]
		private DateTime \u200B\u200C\u202A\u202E\u206A\u200B\u200D\u202B\u200C\u200F\u200D\u206D\u200E\u202C\u200B\u206C\u202C\u206A\u206A\u200F\u202A\u202A\u206D\u200C\u206B\u206E\u206D\u202A\u202B\u206E\u202D\u202D\u200B\u206D\u202E\u206E\u206A\u200B\u206D\u200D\u202E;
	}
}
