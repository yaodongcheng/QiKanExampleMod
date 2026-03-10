5U);
						continue;
					case 6U:
						num = (int)((flag ? 221056762U : 3622201319U) ^ num3 * 2332238629U);
						continue;
					}
					return result;
				}
			}
			return result;
		}

		// Token: 0x0600166B RID: 5739 RVA: 0x0015201C File Offset: 0x0015021C
		public MobileParty GetTargetParty()
		{
			bool flag = \u206B\u200F\u202A\u202D\u206E\u202E\u200D\u200D\u206F\u202E\u202C\u206B\u202B\u202E\u206D\u206A\u200B\u200F\u202A\u200D\u206B\u206E\u202A\u200D\u202B\u206B\u206A\u206A\u202E\u202A\u202E\u206E\u206E\u200F\u206E\u200E\u202C\u200B\u200B\u206C\u202E.<\u0098R\u00AB\u0019(this.TargetPartyId);
			if (flag)
			{
				goto IL_15;
			}
			goto IL_73;
			int num2;
			MobileParty result;
			for (;;)
			{
				IL_1A:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)(~(uint)(num * -1337040955 + ((-232827183 ^ -1542369145) + (-1290429809 ^ -1700409742)) - -773369251 * -1048356490))) % 4U)
				{
				case 0U:
					goto IL_15;
				case 2U:
					result = null;
					num2 = (int)(num3 * 455156019U ^ 4168599819U);
					continue;
				case 3U:
					goto IL_73;
				}
				break;
			}
			return result;
			IL_15:
			num2 = -18852584;
			goto IL_1A;
			IL_73:
			result = Enumerable.FirstOrDefault<MobileParty>(\u200D\u206B\u200C\u200D\u206B\u202C\u200F\u206C\u206D\u202A\u202A\u202B\u202D\u202C\u206C\u206A\u202E\u206B\u206C\u206A\u202E\u202C\u200D\u200E\u206A\u206F\u202B\u206E\u206D\u200B\u202B\u206D\u202D\u200B\u202A\u200D\u202B\u202E\u206A\u200F\u202E.\u200E\u202D\u200F\u200E\u206D\u206D\u206A\u202A\u202A\u206E\u206D\u200C\u202C\u206E\u202B\u202D\u206C\u200B\u202A\u202E\u202B\u200E\u206D\u202C\u206F\u206D\u200D\u200C\u202A\u206F\u202B\u200B\u200F\u200F\u202E\u206F\u202B\u202D\u206E\u206A\u202E(), new Func<MobileParty, bool>(this.\u206B\u206C\u206A\u200C\u206C\u206D\u202E\u202B\u206F\u202E\u202E\u200D\u202E\u200B\u202B\u200E\u200F\u202C\u200F\u206C\u206D\u202D\u202C\u202D\u200B\u202A\u202B\u200E\u200D\u206E\u206E\u206F\u200B\u206E\u206E\u206F\u206B\u200D\u206E\u206C\u202E));
			num2 = 1597744541;
			goto IL_1A;
		}

		// Token: 0x0600166C RID: 5740 RVA: 0x001520BC File Offset: 0x001502BC
		public bool IsWaitCompleted()
		{
			if (this.StepType == TaskStepType.WaitInSettlement)
			{
				goto IL_0D;
			}
			bool flag = true;
			goto IL_98;
			int num2;
			bool result;
			for (;;)
			{
				IL_12:
				int num = num2;
				uint num3;
				switch ((num3 = (uint)((~num * 1209115649 ^ -1389442152 - -525499966) - -621909689))