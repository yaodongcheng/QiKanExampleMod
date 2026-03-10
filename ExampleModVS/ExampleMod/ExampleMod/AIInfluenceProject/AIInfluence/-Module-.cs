using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

// Token: 0x02000001 RID: 1
internal class <Module>
{
	// Token: 0x06000001 RID: 1 RVA: 0x000204B4 File Offset: 0x0001E6B4
	static <Module>()
	{
		<Module>.\u200F\u206F\u202D\u202A\u202E\u202C\u202B\u206C\u200B\u200D\u202A\u200E\u200D\u202C\u200E\u200E\u206F\u202E\u200F\u206C\u202E\u202A\u206A\u206D\u202D\u206C\u200E\u202A\u202A\u202C\u202E\u206F\u206E\u202D\u206D\u200C\u202B\u200E\u206A\u202E\u202E();
		<Module>.\u206A\u202B\u200C\u202E\u206F\u202B\u206B\u202C\u206B\u200E\u206C\u206C\u200B\u206F\u206E\u206A\u206B\u206C\u202C\u206D\u200C\u202C\u202B\u202D\u200C\u200D\u202C\u206B\u200C\u202D\u200B\u206E\u200E\u206C\u200E\u200B\u206D\u206E\u206E\u206D\u202E();
		<Module>.\u200B\u200E\u202D\u206F\u206B\u202D\u202A\u206E\u206C\u200E\u202A\u200D\u206E\u206A\u206A\u200E\u200B\u206F\u202D\u200D\u202D\u200C\u200B\u206B\u206B\u206F\u202C\u200C\u200E\u202B\u206E\u202A\u206F\u202C\u206E\u206E\u202D\u202A\u206A\u202C\u202E();
	}

	// Token: 0x06000002 RID: 2 RVA: 0x000204D0 File Offset: 0x0001E6D0
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void \u200B\u200E\u202D\u206F\u206B\u202D\u202A\u206E\u206C\u200E\u202A\u200D\u206E\u206A\u206A\u200E\u200B\u206F\u202D\u200D\u202D\u200C\u200B\u206B\u206B\u206F\u202C\u200C\u200E\u202B\u206E\u202A\u206F\u202C\u206E\u206E\u202D\u202A\u206A\u202C\u202E()
	{
		MethodInfo method = typeof(Environment).GetMethod(<Module>.\u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<string>(1250707335), new Type[]
		{
			typeof(string)
		});
		if (method != null)
		{
			for (;;)
			{
				IL_35:
				int num = -2026890131;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(~(-num2 ^ -2097052921 * (-262440309 - -1891834392)) * -1404987015)) % 4U)
					{
					case 0U:
						goto IL_35;
					case 1U:
						num = (<Module>.\u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<string>(1549025943).Equals(method.Invoke(null, new object[]
						{
							<Module>.\u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<string>(88873440)
						})) ? 1199386195 : -759312320);
						continue;
					case 3U:
						Environment.FailFast(null);
						num = (int)(num3 * 3204137324U ^ 3080097172U);
						continue;
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		new Thread(new ParameterizedThreadStart(<Module>.\u202C\u202A\u206C\u200F\u206C\u202A\u200F\u202D\u206A\u206D\u202B\u202B\u206A\u206F\u202C\u206D\u200E\u206F\u202C\u202D\u206F\u200E\u200B\u200F\u206C\u206C\u202E\u200C\u202E\u206D\u202E\u206A\u206D\u206D\u202D\u206E\u202B\u202B\u202B\u202E\u202E))
		{
			IsBackground = true
		}.Start(null);
	}

	// Token: 0x06000003 RID: 3 RVA: 0x000205C4 File Offset: 0x0001E7C4
	private static void \u202C\u202A\u206C\u200F\u206C\u202A\u200F\u202D\u206A\u206D\u202B\u202B\u206A\u206F\u202C\u206D\u200E\u206F\u202C\u202D\u206F\u200E\u200B\u200F\u206C\u206C\u202E\u200C\u202E\u206D\u202E\u206A\u206D\u206D\u202D\u206E\u202B\u202B\u202B\u202E\u202E(object thread)
	{
		Thread thread2 = thread as Thread;
		if (thread2 == null)
		{
			goto IL_0A;
		}
		goto IL_62;
		int num2;
		for (;;)
		{
			IL_0F:
			int num = num2;
			uint num3;
			switch ((num3 = (uint)(-(uint)(-(uint)(~(uint)num)))) % 8U)
			{
			case 0U:
				goto IL_62;
			case 2U:
				num2 = (thread2.IsAlive ? -92682554 : -453704788);
				continue;
			case 3U:
				Environment.FailFast(null);
				num2 = (int)(num3 * 3604386159U ^ 3093933115U);
				continue;
			case 4U:
				thread2 = new Thread(new ParameterizedThreadStart(<Module>.\u202C\u202A\u206C\u200F\u206C\u202A\u200F\u202D\u206A\u206D\u202B\u202B\u206A\u206F\u202C\u206D\u200E\u206F\u202C\u202D\u206F\u200E\u200B\u200F\u206C\u206C\u202E\u200C\u202E\u206D\u202E\u206A\u206D\u206D\u202D\u206E\u202B\u202B\u202B\u202E\u202E));
				thread2.IsBackground = true;
				thread2.Start(Thread.CurrentThread);
				Thread.Sleep(500);
				num2 = (int)(num3 * 3338414992U ^ 2372162991U);
				continue;
			case 5U:
				goto IL_0A;
			case 6U:
				Environment.FailFast(null);
				num2 = -677705619;
				continue;
			case 7U:
				num2 = (int)((Debugger.IsLogging() ? 1310298361U : 995180813U) ^ num3 * 386957216U);
				continue;
			}
			break;
		}
		Thread.Sleep(1000);
		goto IL_62;
		IL_0A:
		num2 = -786278525;
		goto IL_0F;
		IL_62:
		num2 = ((!Debugger.IsAttached) ? -544140776 : -1563174503);
		goto IL_0F;
	}

	// Token: 0x06000004 RID: 4
	[DllImport("kernel32.dll", EntryPoint = "VirtualProtect")]
	internal unsafe static extern bool \u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(byte* lpAddress, int dwSize, uint flNewProtect, out uint lpflOldProtect);

	// Token: 0x06000005 RID: 5 RVA: 0x000206DC File Offset: 0x0001E8DC
	internal unsafe static void \u206A\u202B\u200C\u202E\u206F\u202B\u206B\u202C\u206B\u200E\u206C\u206C\u200B\u206F\u206E\u206A\u206B\u206C\u202C\u206D\u200C\u202C\u202B\u202D\u200C\u200D\u202C\u206B\u200C\u202D\u200B\u206E\u200E\u206C\u200E\u200B\u206D\u206E\u206E\u206D\u202E()
	{
		Module module = typeof(<Module>).Module;
		byte* ptr = (byte*)((void*)Marshal.GetHINSTANCE(module));
		byte* ptr2 = ptr + 60;
		ptr2 = ptr + *(uint*)ptr2;
		ptr2 += 6;
		ushort num = *(ushort*)ptr2;
		ptr2 += 14;
		ushort num2 = *(ushort*)ptr2;
		ptr2 = ptr2 + 4 + num2;
		byte* ptr3 = stackalloc byte[(UIntPtr)11];
		if (module.FullyQualifiedName[0] != '<')
		{
			goto IL_59;
		}
		goto IL_889;
		int num4;
		uint[] array;
		uint num9;
		int num11;
		uint[] array2;
		uint[] array3;
		uint num24;
		for (;;)
		{
			IL_5E:
			int num3 = num4;
			uint num5;
			switch ((num5 = (uint)(-(~num3 - ~800959860 * -1355725967) ^ -1456296614)) % 85U)
			{
			case 0U:
			{
				int num6;
				uint num7;
				num4 = ((array[num6] <= num7) ? -450159768 : 2063845694);
				continue;
			}
			case 1U:
			{
				byte* ptr4;
				*ptr4 = 0;
				ptr4++;
				num4 = ((*ptr4 == 0) ? -712811142 : -1188738358);
				continue;
			}
			case 2U:
			{
				int num8;
				num4 = ((array[num8] <= num9) ? -2030718315 : -779767319);
				continue;
			}
			case 3U:
			{
				byte* ptr5;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr5, 8, 64U, out num10);
				ptr5 += 4;
				ptr5 += 4;
				num4 = -587161839;
				continue;
			}
			case 4U:
				array[num11] = *(uint*)(ptr2 + 12);
				array2[num11] = *(uint*)(ptr2 + 8);
				array3[num11] = *(uint*)(ptr2 + 20);
				ptr2 += 40;
				num11++;
				num4 = (int)(num5 * 3568533352U ^ 2797673726U);
				continue;
			case 5U:
			{
				uint num10;
				uint num12;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr + num12, 11, 64U, out num10);
				*(int*)ptr3 = 1818522734;
				*(int*)(ptr3 + 4) = 1818504812;
				*(short*)(ptr3 + (IntPtr)4 * 2) = 108;
				ptr3[10] = 0;
				int num13 = 0;
				num4 = -1921420876;
				continue;
			}
			case 6U:
			{
				int num8;
				num9 = num9 - array[num8] + array3[num8];
				num4 = (int)(num5 * 974232814U ^ 499226924U);
				continue;
			}
			case 7U:
			{
				int num14;
				num4 = ((num14 < 11) ? -1122054456 : -1865738480);
				continue;
			}
			case 8U:
			{
				uint num15;
				int num16;
				(ptr + num15)[num16] = ptr3[num16];
				num16++;
				num4 = -1269669524;
				continue;
			}
			case 9U:
			{
				byte* ptr5;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr5, 4, 64U, out num10);
				*ptr5 = 0;
				ptr5++;
				num4 = ((*ptr5 != 0) ? -1909301619 : -1755749692);
				continue;
			}
			case 10U:
			{
				uint num12;
				int num17;
				num4 = ((array[num17] <= num12) ? -1246673209 : -1000793679);
				continue;
			}
			case 11U:
			{
				uint num10;
				byte* ptr6;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr6, 11, 64U, out num10);
				*(int*)ptr3 = 1866691662;
				*(int*)(ptr3 + 4) = 1852404846;
				*(short*)(ptr3 + (IntPtr)4 * 2) = 25973;
				ptr3[10] = 0;
				int num14 = 0;
				num4 = (int)(num5 * 1395547954U ^ 3368181037U);
				continue;
			}
			case 12U:
			{
				int num18;
				num4 = ((num18 >= (int)num) ? -824301945 : -462349946);
				continue;
			}
			case 13U:
			{
				byte* ptr7;
				uint num15 = *(uint*)ptr7 + 2U;
				int num19 = 0;
				num4 = 2097555284;
				continue;
			}
			case 14U:
			{
				int num6;
				uint num7 = num7 - array[num6] + array3[num6];
				num4 = (int)(num5 * 235926221U ^ 653088853U);
				continue;
			}
			case 15U:
				num4 = ((num11 < (int)num) ? -1479319280 : -1042132590);
				continue;
			case 16U:
			{
				int num13;
				num4 = ((num13 < 11) ? -2088754288 : -587400983);
				continue;
			}
			case 17U:
			{
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr2, 8, 64U, out num10);
				Marshal.Copy(new byte[8], 0, (IntPtr)((void*)ptr2), 8);
				num4 = -568483382;
				continue;
			}
			case 18U:
			{
				int num17;
				uint num12 = num12 - array[num17] + array3[num17];
				num4 = (int)(num5 * 1163231665U ^ 2925749898U);
				continue;
			}
			case 19U:
			{
				uint num12;
				int num13;
				(ptr + num12)[num13] = ptr3[num13];
				num13++;
				num4 = -1921420876;
				continue;
			}
			case 20U:
			{
				byte* ptr8;
				*(int*)ptr8 = 0;
				*(int*)(ptr8 + 4) = 0;
				*(int*)(ptr8 + (IntPtr)2 * 4) = 0;
				*(int*)(ptr8 + (IntPtr)3 * 4) = 0;
				uint num20;
				byte* ptr5 = ptr + num20;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr5, 4, 64U, out num10);
				*(int*)ptr5 = 0;
				ptr5 += 12;
				ptr5 += *(uint*)ptr5;
				ptr5 = (ptr5 + 7L & -4L);
				ptr5 += 2;
				ushort num21 = (ushort)(*ptr5);
				ptr5 += 2;
				int num22 = 0;
				num4 = -1420999042;
				continue;
			}
			case 21U:
			{
				uint num20;
				int num23;
				num4 = ((array[num23] > num20) ? -831281108 : 1833381909);
				continue;
			}
			case 22U:
			{
				byte* ptr8 = ptr + num24;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr8, 72, 64U, out num10);
				uint num20 = *(uint*)(ptr8 + 8);
				int num23 = 0;
				num4 = -1522189731;
				continue;
			}
			case 23U:
			{
				byte* ptr4;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr4, 4, 64U, out num10);
				*ptr4 = 0;
				ptr4++;
				num4 = ((*ptr4 == 0) ? -2091388721 : -480049114);
				continue;
			}
			case 24U:
			{
				uint num15;
				int num19;
				num4 = (int)(((num15 < array[num19] + array2[num19]) ? 350991132U : 329875305U) ^ num5 * 2954967451U);
				continue;
			}
			case 25U:
			{
				byte* ptr5;
				ptr5 += 3;
				num4 = (int)(num5 * 4162116032U ^ 1126498582U);
				continue;
			}
			case 26U:
			{
				int num25;
				ushort num26;
				num4 = ((num25 >= (int)num26) ? -604752706 : 1843441373);
				continue;
			}
			case 27U:
			{
				byte* ptr4;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr4, 8, 64U, out num10);
				ptr4 += 4;
				ptr4 += 4;
				int num27 = 0;
				num4 = -1406333895;
				continue;
			}
			case 28U:
			{
				int num8;
				num8++;
				num4 = -470837039;
				continue;
			}
			case 29U:
			{
				int num23;
				num23++;
				num4 = -1522189731;
				continue;
			}
			case 30U:
			{
				int num28;
				num4 = ((num28 >= 11) ? -1025244932 : 2114658275);
				continue;
			}
			case 31U:
			{
				byte* ptr9 = ptr + *(uint*)(ptr2 - 120);
				byte* ptr10 = ptr + *(uint*)ptr9;
				byte* ptr11 = ptr + *(uint*)(ptr9 + 12);
				byte* ptr6 = ptr + *(uint*)ptr10 + 2;
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr11, 11, 64U, out num10);
				*(int*)ptr3 = 1818522734;
				*(int*)(ptr3 + 4) = 1818504812;
				*(short*)(ptr3 + (IntPtr)4 * 2) = 108;
				ptr3[10] = 0;
				int num28 = 0;
				num4 = (int)(num5 * 3752949836U ^ 1438269472U);
				continue;
			}
			case 32U:
			{
				byte* ptr5;
				*ptr5 = 0;
				ptr5++;
				int num29;
				num29++;
				num4 = -610053335;
				continue;
			}
			case 33U:
			{
				byte* ptr4;
				ptr4++;
				num4 = (int)(num5 * 1380683527U ^ 3784249667U);
				continue;
			}
			case 34U:
			{
				uint num12;
				int num17;
				num4 = (int)(((num12 < array[num17] + array2[num17]) ? 3825017321U : 4188856830U) ^ num5 * 2437879559U);
				continue;
			}
			case 35U:
				num4 = (int)(((num9 == 0U) ? 4102017346U : 1351304086U) ^ num5 * 1547065396U);
				continue;
			case 36U:
			{
				int num19;
				num19++;
				num4 = 2097555284;
				continue;
			}
			case 37U:
			{
				int num6;
				uint num7;
				num4 = (int)(((num7 < array[num6] + array2[num6]) ? 4252910425U : 1081984128U) ^ num5 * 1444626289U);
				continue;
			}
			case 38U:
			{
				int num8;
				num4 = (int)(((num9 < array[num8] + array2[num8]) ? 779780897U : 1316475223U) ^ num5 * 3844652090U);
				continue;
			}
			case 39U:
			{
				int num19;
				uint num15 = num15 - array[num19] + array3[num19];
				num4 = (int)(num5 * 4099093954U ^ 2296014877U);
				continue;
			}
			case 40U:
				goto IL_889;
			case 41U:
			{
				int num6;
				num6++;
				num4 = -1945643737;
				continue;
			}
			case 42U:
			{
				byte* ptr5;
				*ptr5 = 0;
				ptr5++;
				num4 = ((*ptr5 != 0) ? -812567661 : -1871996604);
				continue;
			}
			case 43U:
			{
				int num25;
				num25++;
				num4 = 1821230800;
				continue;
			}
			case 44U:
			{
				uint num10;
				uint num15;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr + num15, 11, 64U, out num10);
				*(int*)ptr3 = 1866691662;
				*(int*)(ptr3 + 4) = 1852404846;
				*(short*)(ptr3 + (IntPtr)4 * 2) = 25973;
				ptr3[10] = 0;
				int num16 = 0;
				num4 = (int)(num5 * 855500810U ^ 2920636506U);
				continue;
			}
			case 45U:
			{
				int num14;
				byte* ptr6;
				ptr6[num14] = ptr3[num14];
				num14++;
				num4 = -2080330935;
				continue;
			}
			case 46U:
			{
				byte* ptr12 = ptr + num9;
				uint num7 = *(uint*)ptr12;
				int num6 = 0;
				num4 = -1945643737;
				continue;
			}
			case 47U:
			{
				int num18;
				num24 = num24 - array[num18] + array3[num18];
				num4 = (int)(num5 * 447177506U ^ 1920864083U);
				continue;
			}
			case 48U:
			{
				uint num10;
				byte* ptr13;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr13, 72, 64U, out num10);
				byte* ptr4 = ptr + *(uint*)(ptr13 + 8);
				*(int*)ptr13 = 0;
				*(int*)(ptr13 + 4) = 0;
				*(int*)(ptr13 + (IntPtr)2 * 4) = 0;
				*(int*)(ptr13 + (IntPtr)3 * 4) = 0;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr4, 4, 64U, out num10);
				*(int*)ptr4 = 0;
				ptr4 += 12;
				ptr4 += *(uint*)ptr4;
				ptr4 = (ptr4 + 7L & -4L);
				ptr4 += 2;
				ushort num26 = (ushort)(*ptr4);
				ptr4 += 2;
				int num25 = 0;
				num4 = (int)(num5 * 3483708412U ^ 3435182732U);
				continue;
			}
			case 49U:
			{
				int num30;
				num4 = ((num30 < (int)num) ? -625637279 : -915112361);
				continue;
			}
			case 50U:
			{
				int num22;
				num22++;
				num4 = -1420999042;
				continue;
			}
			case 51U:
			{
				int num18;
				num4 = ((array[num18] <= num24) ? -1480460139 : -1943133419);
				continue;
			}
			case 52U:
			{
				int num18;
				num18++;
				num4 = 1947907057;
				continue;
			}
			case 53U:
				return;
			case 54U:
			{
				int num18;
				num4 = (int)(((num24 < array[num18] + array2[num18]) ? 4284512614U : 2851770823U) ^ num5 * 1884390006U);
				continue;
			}
			case 55U:
			{
				int num19;
				num4 = ((num19 >= (int)num) ? -769601673 : -550048031);
				continue;
			}
			case 56U:
			{
				uint num7;
				byte* ptr7 = ptr + num7;
				byte* ptr12;
				uint num12 = *(uint*)(ptr12 + 12);
				int num17 = 0;
				num4 = -463521623;
				continue;
			}
			case 58U:
			{
				byte* ptr4;
				*ptr4 = 0;
				ptr4++;
				num4 = ((*ptr4 == 0) ? -611889923 : -2134051906);
				continue;
			}
			case 59U:
			{
				int num18 = 0;
				num4 = 1947907057;
				continue;
			}
			case 60U:
			{
				byte* ptr5;
				*ptr5 = 0;
				ptr5++;
				num4 = ((*ptr5 == 0) ? 2004753918 : -1268383876);
				continue;
			}
			case 61U:
			{
				uint num10;
				<Module>.\u200C\u206D\u206E\u202C\u200E\u206B\u202A\u202B\u206F\u200B\u206D\u200E\u206A\u200D\u202D\u206B\u202E\u206D\u206C\u206F\u206D\u206F\u200C\u202B\u200B\u206F\u200D\u206A\u206E\u206D\u206A\u206B\u202E\u206C\u206E\u206A\u200B\u206F\u200F\u202C\u202E(ptr2, 8, 64U, out num10);
				Marshal.Copy(new byte[8], 0, (IntPtr)((void*)ptr2), 8);
				ptr2 += 40;
				int num30;
				num30++;
				num4 = -1756933519;
				continue;
			}
			case 62U:
			{
				int num16;
				num4 = ((num16 >= 11) ? 2082317778 : 1829448727);
				continue;
			}
			case 63U:
			{
				int num8 = 0;
				num4 = (int)(num5 * 3808964991U ^ 1899868441U);
				continue;
			}
			case 64U:
			{
				int num23;
				num4 = ((num23 >= (int)num) ? -1749309129 : -678412149);
				continue;
			}
			case 65U:
			{
				byte* ptr4;
				*ptr4 = 0;
				ptr4++;
				int num27;
				num27++;
				num4 = -1406333895;
				continue;
			}
			case 66U:
			{
				int num8;
				num4 = ((num8 >= (int)num) ? -2006315902 : 1983745439);
				continue;
			}
			case 67U:
			{
				byte* ptr5;
				ptr5 += 2;
				num4 = (int)(num5 * 3328257271U ^ 3093774752U);
				continue;
			}
			case 68U:
			{
				byte* ptr4;
				ptr4 += 2;
				num4 = (int)(num5 * 207028195U ^ 4137396542U);
				continue;
			}
			case 69U:
			{
				int num17;
				num4 = ((num17 >= (int)num) ? -828043950 : -874649502);
				continue;
			}
			case 70U:
			{
				byte* ptr13 = ptr + *(uint*)(ptr2 - 16);
				num4 = (int)(((*(uint*)(ptr2 - 120) != 0U) ? 2604885194U : 3074020474U) ^ num5 * 3842946963U);
				continue;
			}
			case 71U:
			{
				byte* ptr5;
				ptr5++;
				num4 = (int)(num5 * 2400238411U ^ 2597119382U);
				continue;
			}
			case 72U:
			{
				int num23;
				uint num20 = num20 - array[num23] + array3[num23];
				num4 = (int)(num5 * 3088847816U ^ 3983671535U);
				continue;
			}
			case 73U:
			{
				byte* ptr4;
				ptr4 += 3;
				num4 = (int)(num5 * 784711660U ^ 1783816635U);
				continue;
			}
			case 74U:
			{
				uint num15;
				int num19;
				num4 = ((array[num19] <= num15) ? -1841872181 : -1531161146);
				continue;
			}
			case 75U:
			{
				int num27;
				num4 = ((num27 < 8) ? -2128773227 : -620779881);
				continue;
			}
			case 76U:
			{
				int num30 = 0;
				num4 = -1756933519;
				continue;
			}
			case 77U:
			{
				int num6;
				num4 = ((num6 >= (int)num) ? -1019602864 : -1071710594);
				continue;
			}
			case 78U:
			{
				uint num20;
				int num23;
				num4 = (int)(((num20 >= array[num23] + array2[num23]) ? 3879988757U : 3962434196U) ^ num5 * 1200656427U);
				continue;
			}
			case 79U:
			{
				ushort num21;
				int num22;
				num4 = ((num22 >= (int)num21) ? 1959434521 : -675761599);
				continue;
			}
			case 80U:
			{
				int num29;
				num4 = ((num29 < 8) ? -1576176222 : -676248426);
				continue;
			}
			case 81U:
			{
				int num29 = 0;
				num4 = (int)(num5 * 134419294U ^ 2891527707U);
				continue;
			}
			case 82U:
			{
				int num17;
				num17++;
				num4 = -463521623;
				continue;
			}
			case 83U:
			{
				int num28;
				byte* ptr11;
				ptr11[num28] = ptr3[num28];
				num28++;
				num4 = 1929574760;
				continue;
			}
			case 84U:
				goto IL_59;
			}
			break;
		}
		return;
		IL_59:
		num4 = -1254096872;
		goto IL_5E;
		IL_889:
		num24 = *(uint*)(ptr2 - 16);
		num9 = *(uint*)(ptr2 - 120);
		array = new uint[(int)num];
		array2 = new uint[(int)num];
		array3 = new uint[(int)num];
		num11 = 0;
		num4 = -1012641122;
		goto IL_5E;
	}

	// Token: 0x06000006 RID: 6 RVA: 0x00021408 File Offset: 0x0001F608
	static void \u206E\u206C\u206F\u200F\u206D\u200E\u206A\u202C\u206C\u200F\u200F\u200F\u202B\u202B\u206E\u206F\u202C\u200D\u202A\u202D\u206B\u202D\u200B\u206A\u200D\u200C\u202B\u202C\u202C\u206D\u206D\u202C\u200D\u206D\u200B\u200B\u206E\u202D\u202D\u200E\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = -1344441877;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 0);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[num - 1]) << (8 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_254;
		int num6;
		MethodInfo[] methods;
		int num9;
		byte[] array2;
		int num10;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		Type[] array4;
		for (;;)
		{
			IL_D7:
			int num5 = num6;
			uint num7;
			int num8;
			Type type;
			Type type2;
			int num12;
			switch ((num7 = (uint)(-((num5 ^ (132027307 * 273622445 * 1867076693 ^ -(~764952907))) * 1187506051) - 2113470858)) % 22U)
			{
			case 0U:
			{
				ParameterInfo[] parameters;
				type = parameters[num8].ParameterType;
				goto IL_2BF;
			}
			case 1U:
			{
				MethodInfo methodInfo = methods[num9];
				num6 = ((methodInfo.DeclaringType != fieldType) ? 1918666450 : -82719552);
				continue;
			}
			case 2U:
				num6 = (int)(((!type2.IsByRef) ? 169704041U : 1962501416U) ^ num7 * 3749007333U);
				continue;
			case 3U:
				num6 = ((num9 >= methods.Length) ? -1747865394 : 313182194);
				continue;
			case 4U:
			{
				array2[num10++] = 14;
				int num11;
				array2[num10++] = (byte)num11;
				if (num8 != -1)
				{
					num6 = 1784272625;
					continue;
				}
				type = methodBase.DeclaringType;
				goto IL_2BF;
			}
			case 5U:
				goto IL_2DE;
			case 6U:
				num12 = -1;
				goto IL_35B;
			case 7U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array3 = new byte[2];
				array3[0] = 7;
				dynamicILInfo2.SetLocalSignature(array3);
				array2 = new byte[7 * array4.Length + 6];
				num10 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num6 = -518167903;
					continue;
				}
				num12 = 0;
				goto IL_35B;
			}
			case 8U:
				goto IL_D2;
			case 9U:
				goto IL_254;
			case 10U:
			{
				int num11 = 0;
				num6 = (int)(num7 * 4250245539U ^ 621388383U);
				continue;
			}
			case 11U:
			{
				int num13;
				ParameterInfo[] parameters2;
				array4[num13] = parameters2[num13].ParameterType;
				num13++;
				num6 = -342860912;
				continue;
			}
			case 12U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array4, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num6 = -1747865394;
				continue;
			}
			case 13U:
			{
				int num13;
				num6 = ((num13 >= array4.Length) ? -657931411 : -1145647010);
				continue;
			}
			case 14U:
			{
				num8++;
				int num11;
				num11++;
				num6 = -811781323;
				continue;
			}
			case 15U:
				num10 += 5;
				num6 = 1468115393;
				continue;
			case 16U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type2.TypeHandle);
				array2[num10++] = 116;
				array2[num10++] = (byte)tokenFor;
				array2[num10++] = (byte)(tokenFor >> 8);
				array2[num10++] = (byte)(tokenFor >> 16);
				array2[num10++] = (byte)(tokenFor >> 24);
				num6 = (int)(num7 * 3240662960U ^ 295201U);
				continue;
			}
			case 17U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array4 = new Type[parameters2.Length];
				int num13 = 0;
				num6 = (int)(num7 * 1152690692U ^ 2678698452U);
				continue;
			}
			case 18U:
			{
				int num11;
				num6 = ((num11 >= array4.Length) ? 235055641 : -336672917);
				continue;
			}
			case 19U:
				num9++;
				num6 = -119767436;
				continue;
			case 21U:
				num6 = (int)(((!type2.IsPointer) ? 4093450215U : 2177462574U) ^ num7 * 2153012876U);
				continue;
			}
			break;
			IL_2BF:
			type2 = type;
			num6 = (type2.IsClass ? -1034800532 : 1690275346);
			continue;
			IL_35B:
			num8 = num12;
			num6 = 1254358479;
		}
		goto IL_474;
		IL_2DE:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_474:
		array2[num10++] = ((byte)fieldFromHandle.Name[1] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array2[num10++] = (byte)tokenFor2;
		array2[num10++] = (byte)(tokenFor2 >> 8);
		array2[num10++] = (byte)(tokenFor2 >> 16);
		array2[num10++] = (byte)(tokenFor2 >> 24);
		array2[num10] = 42;
		dynamicILInfo.SetCode(array2, array4.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num6 = -1492093190;
		goto IL_D7;
		IL_254:
		dynamicMethod = null;
		array4 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num9 = 0;
		num6 = -119767436;
		goto IL_D7;
	}

	// Token: 0x06000007 RID: 7 RVA: 0x00021918 File Offset: 0x0001FB18
	static void \u206A\u206F\u202E\u200E\u206D\u202A\u206D\u200F\u206F\u206D\u202D\u202A\u200F\u200F\u200F\u200C\u200E\u206E\u200C\u202D\u206B\u200B\u200B\u206E\u200D\u200B\u202B\u200B\u202D\u202D\u202E\u200B\u202B\u200D\u200F\u200F\u202E\u202D\u202C\u206C\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = 132661535;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 16);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (8 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_221;
		int num6;
		Type[] array2;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		byte[] array4;
		int num9;
		MethodInfo[] methods;
		int num11;
		for (;;)
		{
			IL_D7:
			int num5 = num6;
			uint num7;
			int num10;
			Type type;
			int num12;
			int num13;
			Type type2;
			switch ((num7 = (uint)(-((num5 + (-(-1302964360) ^ ~1801182732 - (4219509 ^ -1130374481))) * -1049955411) * 1601818961)) % 22U)
			{
			case 0U:
				goto IL_D2;
			case 1U:
				goto IL_38A;
			case 2U:
			{
				int num8;
				num6 = ((num8 >= array2.Length) ? -1373798923 : -1619273423);
				continue;
			}
			case 3U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array3 = new byte[2];
				array3[0] = 7;
				dynamicILInfo2.SetLocalSignature(array3);
				array4 = new byte[7 * array2.Length + 6];
				num9 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num6 = -779894661;
					continue;
				}
				num10 = 0;
				goto IL_466;
			}
			case 4U:
				num6 = (int)(((!type.IsPointer) ? 4178102226U : 2070976355U) ^ num7 * 960061604U);
				continue;
			case 5U:
			{
				MethodInfo methodInfo = methods[num11];
				num6 = ((methodInfo.DeclaringType != fieldType) ? 60463349 : 1781454641);
				continue;
			}
			case 6U:
				num10 = -1;
				goto IL_466;
			case 7U:
				num6 = (int)(num7 * 4218889985U ^ 1566269987U);
				continue;
			case 8U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num6 = 857458130;
				continue;
			}
			case 9U:
				array4[num9++] = 14;
				array4[num9++] = (byte)num12;
				if (num13 != -1)
				{
					num6 = -20264733;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_2DF;
			case 10U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array2 = new Type[parameters2.Length];
				int num8 = 0;
				num6 = (int)(num7 * 1497960310U ^ 1571643221U);
				continue;
			}
			case 11U:
				num6 = ((num11 < methods.Length) ? 1242042212 : 857458130);
				continue;
			case 12U:
				num9 += 5;
				num6 = -84553574;
				continue;
			case 13U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array4[num9++] = 116;
				array4[num9++] = (byte)tokenFor;
				array4[num9++] = (byte)(tokenFor >> 8);
				array4[num9++] = (byte)(tokenFor >> 16);
				array4[num9++] = (byte)(tokenFor >> 24);
				num6 = (int)(num7 * 3723014656U ^ 1382367898U);
				continue;
			}
			case 14U:
				goto IL_221;
			case 16U:
				num11++;
				num6 = -549585128;
				continue;
			case 17U:
				num13++;
				num12++;
				num6 = -978031228;
				continue;
			case 18U:
			{
				int num8;
				ParameterInfo[] parameters2;
				array2[num8] = parameters2[num8].ParameterType;
				num8++;
				num6 = 31072785;
				continue;
			}
			case 19U:
				num6 = (int)(((!type.IsByRef) ? 2493305451U : 3160229330U) ^ num7 * 2833294761U);
				continue;
			case 20U:
			{
				ParameterInfo[] parameters;
				type2 = parameters[num13].ParameterType;
				goto IL_2DF;
			}
			case 21U:
				num6 = ((num12 >= array2.Length) ? -1047447554 : 1759124954);
				continue;
			}
			break;
			IL_2DF:
			type = type2;
			num6 = ((!type.IsClass) ? 755832851 : 510809667);
			continue;
			IL_466:
			num13 = num10;
			num12 = 0;
			num6 = -978031228;
		}
		goto IL_475;
		IL_38A:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_475:
		array4[num9++] = ((byte)fieldFromHandle.Name[3] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array4[num9++] = (byte)tokenFor2;
		array4[num9++] = (byte)(tokenFor2 >> 8);
		array4[num9++] = (byte)(tokenFor2 >> 16);
		array4[num9++] = (byte)(tokenFor2 >> 24);
		array4[num9] = 42;
		dynamicILInfo.SetCode(array4, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num6 = -609862444;
		goto IL_D7;
		IL_221:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num11 = 0;
		num6 = 1647910936;
		goto IL_D7;
	}

	// Token: 0x06000008 RID: 8 RVA: 0x00021E28 File Offset: 0x00020028
	static void \u206A\u206A\u202E\u206C\u202A\u206A\u202B\u202E\u206B\u202A\u206E\u200D\u202B\u200E\u206A\u200D\u206C\u200F\u202E\u200D\u202A\u200B\u202B\u200B\u202C\u202D\u202B\u206F\u200F\u202B\u202E\u206B\u202C\u202D\u206D\u200C\u200E\u200E\u206A\u202D\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 8) + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 0);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[num - 1]) << (16 & 31))) * 1340600837;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_455;
		int num5;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		Type[] array4;
		byte[] array3;
		int num7;
		int num9;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num8;
			int num10;
			Type type;
			int num12;
			Type type2;
			switch ((num6 = (uint)(-num4 * 1493510179 - (1141892460 - 284063469) - 1077848019)) % 23U)
			{
			case 0U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array2 = new byte[2];
				array2[0] = 7;
				dynamicILInfo2.SetLocalSignature(array2);
				array3 = new byte[7 * array4.Length + 6];
				num7 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = 1547555514;
					continue;
				}
				num8 = 0;
				goto IL_314;
			}
			case 1U:
				num9++;
				num5 = 142943501;
				continue;
			case 2U:
				goto IL_455;
			case 3U:
				num5 = ((num10 < array4.Length) ? 684378037 : 1721142930);
				continue;
			case 4U:
				num5 = (int)((type.IsByRef ? 3184936235U : 1073873731U) ^ num6 * 1640250552U);
				continue;
			case 5U:
				goto IL_36F;
			case 6U:
			{
				int tokenFor;
				array3[num7++] = (byte)tokenFor;
				array3[num7++] = (byte)(tokenFor >> 8);
				array3[num7++] = (byte)(tokenFor >> 16);
				array3[num7++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 2525251930U ^ 875715621U);
				continue;
			}
			case 7U:
			{
				int num11;
				num5 = ((num11 >= array4.Length) ? -1311433472 : 1579572922);
				continue;
			}
			case 8U:
			{
				ParameterInfo[] parameters;
				type2 = parameters[num12].ParameterType;
				goto IL_20C;
			}
			case 9U:
			{
				int num11;
				ParameterInfo[] parameters2;
				array4[num11] = parameters2[num11].ParameterType;
				num11++;
				num5 = -333021180;
				continue;
			}
			case 10U:
				array3[num7++] = 116;
				num5 = (int)(num6 * 1573695098U ^ 2430531668U);
				continue;
			case 11U:
			{
				MethodInfo methodInfo = methods[num9];
				num5 = ((methodInfo.DeclaringType != fieldType) ? 1743322112 : 1482153771);
				continue;
			}
			case 12U:
				num7 += 5;
				num5 = -863136583;
				continue;
			case 14U:
				num8 = -1;
				goto IL_314;
			case 15U:
				goto IL_D2;
			case 16U:
				num5 = ((num9 >= methods.Length) ? 307593380 : 103586326);
				continue;
			case 17U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array4 = new Type[parameters2.Length];
				int num11 = 0;
				num5 = (int)(num6 * 1537067480U ^ 683536892U);
				continue;
			}
			case 18U:
				array3[num7++] = 14;
				array3[num7++] = (byte)num10;
				if (num12 != -1)
				{
					num5 = 601271263;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_20C;
			case 19U:
				num12++;
				num10++;
				num5 = 804925687;
				continue;
			case 20U:
				num5 = (int)(((!type.IsPointer) ? 3792103086U : 2597383081U) ^ num6 * 3018256446U);
				continue;
			case 21U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				num5 = (int)(num6 * 1500690746U ^ 2804996898U);
				continue;
			}
			case 22U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array4, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 307593380;
				continue;
			}
			}
			break;
			IL_20C:
			type = type2;
			num5 = (type.IsClass ? 1559969645 : 135888283);
			continue;
			IL_314:
			num12 = num8;
			num10 = 0;
			num5 = 804925687;
		}
		goto IL_477;
		IL_36F:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_477:
		array3[num7++] = ((byte)fieldFromHandle.Name[2] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num7++] = (byte)tokenFor2;
		array3[num7++] = (byte)(tokenFor2 >> 8);
		array3[num7++] = (byte)(tokenFor2 >> 16);
		array3[num7++] = (byte)(tokenFor2 >> 24);
		array3[num7] = 42;
		dynamicILInfo.SetCode(array3, array4.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 796809234;
		goto IL_D7;
		IL_455:
		dynamicMethod = null;
		array4 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num9 = 0;
		num5 = 142943501;
		goto IL_D7;
	}

	// Token: 0x06000009 RID: 9 RVA: 0x00022338 File Offset: 0x00020538
	static void \u200B\u206E\u206D\u202B\u206E\u206A\u202B\u206D\u200E\u206F\u200D\u200C\u200E\u206E\u202D\u202D\u206A\u200E\u206F\u206F\u206B\u202D\u206C\u202D\u206B\u206F\u206B\u202D\u206D\u200D\u200D\u202E\u206C\u206C\u206F\u200F\u206B\u200F\u200F\u200E\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 16);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[num - 1]) << (8 & 31))) * 1387427777;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_203;
		int num5;
		int num7;
		Type[] array2;
		DynamicMethod dynamicMethod;
		byte[] array3;
		int num10;
		DynamicILInfo dynamicILInfo;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num8;
			Type type;
			int num9;
			int num12;
			Type type2;
			switch ((num6 = (uint)(~(uint)(-132755164 * -2072567185 - ~-956780301 + -(--1679298933) - num4))) % 22U)
			{
			case 0U:
				num7 += 5;
				num5 = 1727554376;
				continue;
			case 2U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = -1326169755;
				continue;
			}
			case 3U:
			{
				ParameterInfo[] parameters;
				type = parameters[num8].ParameterType;
				goto IL_444;
			}
			case 4U:
				num5 = ((num9 >= array2.Length) ? -1520034577 : -1755916688);
				continue;
			case 5U:
				goto IL_D2;
			case 6U:
				array3[num7++] = (byte)num9;
				if (num8 != -1)
				{
					num5 = (int)(num6 * 432631242U ^ 2123633949U);
					continue;
				}
				type = methodBase.DeclaringType;
				goto IL_444;
			case 7U:
				num10++;
				num5 = 2051028537;
				continue;
			case 8U:
			{
				int num11;
				ParameterInfo[] parameters2;
				array2[num11] = parameters2[num11].ParameterType;
				num11++;
				num5 = -1789789341;
				continue;
			}
			case 9U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array3 = new byte[7 * array2.Length + 6];
				num7 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = 1176825430;
					continue;
				}
				num12 = 0;
				goto IL_26E;
			}
			case 10U:
				num5 = (int)((type2.IsPointer ? 3027796298U : 1721187879U) ^ num6 * 2207410223U);
				continue;
			case 11U:
				num5 = ((num10 < methods.Length) ? -2041781967 : -1326169755);
				continue;
			case 12U:
				num8++;
				num9++;
				num5 = -1376922594;
				continue;
			case 13U:
			{
				MethodInfo methodInfo = methods[num10];
				num5 = ((methodInfo.DeclaringType == fieldType) ? 1196764429 : 1581466935);
				continue;
			}
			case 14U:
				num12 = -1;
				goto IL_26E;
			case 15U:
				goto IL_408;
			case 16U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type2.TypeHandle);
				array3[num7++] = 116;
				array3[num7++] = (byte)tokenFor;
				array3[num7++] = (byte)(tokenFor >> 8);
				array3[num7++] = (byte)(tokenFor >> 16);
				array3[num7++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 1811359028U ^ 1186465216U);
				continue;
			}
			case 17U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array2 = new Type[parameters2.Length];
				int num11 = 0;
				num5 = (int)(num6 * 880667633U ^ 664774786U);
				continue;
			}
			case 18U:
				goto IL_203;
			case 19U:
			{
				int num11;
				num5 = ((num11 < array2.Length) ? 1453915644 : -2138800680);
				continue;
			}
			case 20U:
				array3[num7++] = 14;
				num5 = 1125595364;
				continue;
			case 21U:
				num5 = (int)(((!type2.IsByRef) ? 3972373002U : 4119164892U) ^ num6 * 1982836940U);
				continue;
			}
			break;
			IL_26E:
			num8 = num12;
			num9 = 0;
			num5 = -1376922594;
			continue;
			IL_444:
			type2 = type;
			num5 = ((!type2.IsClass) ? -2135910256 : 1562806626);
		}
		goto IL_463;
		IL_408:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_463:
		array3[num7++] = ((byte)fieldFromHandle.Name[3] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num7++] = (byte)tokenFor2;
		array3[num7++] = (byte)(tokenFor2 >> 8);
		array3[num7++] = (byte)(tokenFor2 >> 16);
		array3[num7++] = (byte)(tokenFor2 >> 24);
		array3[num7] = 42;
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 1742616173;
		goto IL_D7;
		IL_203:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num10 = 0;
		num5 = 2051028537;
		goto IL_D7;
	}

	// Token: 0x0600000A RID: 10 RVA: 0x00022834 File Offset: 0x00020A34
	static void \u206E\u200E\u202B\u206D\u206D\u202B\u206D\u206A\u202D\u206C\u206B\u202D\u200C\u206F\u202B\u206B\u200C\u200F\u202C\u200F\u200C\u206B\u206B\u200B\u200C\u200F\u206F\u200B\u206D\u200C\u202E\u200D\u206E\u200C\u206B\u202E\u200D\u206F\u202E\u206B\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 0);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[num - 1]) << (8 & 31))) * -100260533;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_42D;
		int num5;
		Type[] array2;
		MethodInfo[] methods;
		int num8;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		byte[] array3;
		int num9;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			Type type;
			int num10;
			int num11;
			int num12;
			Type type2;
			switch ((num6 = (uint)((num4 ^ -(-825718094 + 493040649 * 509945781)) - (--783790168 - -276854799))) % 22U)
			{
			case 0U:
			{
				int num7;
				num5 = ((num7 < array2.Length) ? -455223969 : -214318411);
				continue;
			}
			case 1U:
				goto IL_164;
			case 2U:
			{
				MethodInfo methodInfo = methods[num8];
				num5 = ((methodInfo.DeclaringType == fieldType) ? 36946647 : 549713887);
				continue;
			}
			case 3U:
				goto IL_D2;
			case 4U:
				goto IL_42D;
			case 5U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array2 = new Type[parameters.Length];
				int num7 = 0;
				num5 = (int)(num6 * 2290025588U ^ 3759171212U);
				continue;
			}
			case 6U:
				num5 = (int)((type.IsByRef ? 3925324663U : 400408800U) ^ num6 * 716160427U);
				continue;
			case 7U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = -188757092;
				continue;
			}
			case 8U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array3[num9++] = 116;
				array3[num9++] = (byte)tokenFor;
				array3[num9++] = (byte)(tokenFor >> 8);
				array3[num9++] = (byte)(tokenFor >> 16);
				array3[num9++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 3311777339U ^ 2536051193U);
				continue;
			}
			case 9U:
				num5 = ((num8 < methods.Length) ? 185289380 : -188757092);
				continue;
			case 10U:
				num5 = (int)(((!type.IsPointer) ? 3133659628U : 3092954453U) ^ num6 * 1800550347U);
				continue;
			case 11U:
				num8++;
				num5 = 185234721;
				continue;
			case 12U:
			{
				array3 = new byte[7 * array2.Length + 6];
				num9 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = (int)(num6 * 1903182708U ^ 331994696U);
					continue;
				}
				num10 = 0;
				goto IL_1BC;
			}
			case 14U:
				num10 = -1;
				goto IL_1BC;
			case 15U:
				num11++;
				num12++;
				num5 = 367982945;
				continue;
			case 16U:
				array3[num9++] = 14;
				array3[num9++] = (byte)num12;
				if (num11 != -1)
				{
					num5 = -739605930;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_40E;
			case 17U:
			{
				int num7;
				ParameterInfo[] parameters;
				array2[num7] = parameters[num7].ParameterType;
				num7++;
				num5 = 201831424;
				continue;
			}
			case 18U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				num5 = -696684782;
				continue;
			}
			case 19U:
				num9 += 5;
				num5 = 161884217;
				continue;
			case 20U:
			{
				ParameterInfo[] parameters2;
				type2 = parameters2[num11].ParameterType;
				goto IL_40E;
			}
			case 21U:
				num5 = ((num12 >= array2.Length) ? 282848699 : 1011043482);
				continue;
			}
			break;
			IL_1BC:
			num11 = num10;
			num12 = 0;
			num5 = 367982945;
			continue;
			IL_40E:
			type = type2;
			num5 = ((!type.IsClass) ? -6274623 : -673478574);
		}
		goto IL_46E;
		IL_164:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_46E:
		array3[num9++] = ((byte)fieldFromHandle.Name[4] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num9++] = (byte)tokenFor2;
		array3[num9++] = (byte)(tokenFor2 >> 8);
		array3[num9++] = (byte)(tokenFor2 >> 16);
		array3[num9++] = (byte)(tokenFor2 >> 24);
		array3[num9] = 42;
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = -540364903;
		goto IL_D7;
		IL_42D:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num8 = 0;
		num5 = 185234721;
		goto IL_D7;
	}

	// Token: 0x0600000B RID: 11 RVA: 0x00022D3C File Offset: 0x00020F3C
	static void \u206A\u200B\u200C\u206E\u200E\u206E\u202C\u206B\u200B\u202A\u202D\u206F\u202B\u202E\u206A\u206C\u206D\u202B\u200D\u206F\u200F\u206F\u202B\u206A\u206F\u206B\u206E\u202E\u206C\u206F\u200E\u202A\u206C\u200C\u206E\u206A\u202B\u200C\u206B\u206C\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 8) + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 0);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[num - 1]) << (16 & 31))) * 1194786981;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_3ED;
		int num5;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		Type[] array4;
		byte[] array3;
		int num7;
		int num11;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			Type type;
			int num8;
			int num9;
			int num10;
			Type type2;
			switch ((num6 = (uint)(~(-num4) * 1047771843 + 231337770)) % 22U)
			{
			case 0U:
				num5 = (int)(((!type.IsPointer) ? 1698116100U : 410533030U) ^ num6 * 961255742U);
				continue;
			case 1U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array2 = new byte[2];
				array2[0] = 7;
				dynamicILInfo2.SetLocalSignature(array2);
				array3 = new byte[7 * array4.Length + 6];
				num7 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = 425012673;
					continue;
				}
				num8 = 0;
				goto IL_423;
			}
			case 2U:
				goto IL_3ED;
			case 3U:
				num5 = (int)(((!type.IsByRef) ? 3362187329U : 2899206192U) ^ num6 * 1458428618U);
				continue;
			case 4U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array3[num7++] = 116;
				array3[num7++] = (byte)tokenFor;
				array3[num7++] = (byte)(tokenFor >> 8);
				array3[num7++] = (byte)(tokenFor >> 16);
				array3[num7++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 621717831U ^ 2934549337U);
				continue;
			}
			case 5U:
				goto IL_151;
			case 6U:
				num9++;
				num10++;
				num5 = -2050496056;
				continue;
			case 7U:
				num5 = ((num10 >= array4.Length) ? 609283543 : -833569897);
				continue;
			case 8U:
				num5 = ((num11 >= methods.Length) ? 1404697122 : -1879497468);
				continue;
			case 9U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array4 = new Type[parameters2.Length];
				int num12 = 0;
				num5 = (int)(num6 * 1750558012U ^ 3248185067U);
				continue;
			}
			case 11U:
			{
				ParameterInfo[] parameters2;
				int num12;
				array4[num12] = parameters2[num12].ParameterType;
				num12++;
				num5 = 698304425;
				continue;
			}
			case 12U:
			{
				ParameterInfo[] parameters;
				type2 = parameters[num9].ParameterType;
				goto IL_2B3;
			}
			case 13U:
				num11++;
				num5 = -299531971;
				continue;
			case 14U:
			{
				int num12;
				num5 = ((num12 >= array4.Length) ? 493420630 : 1822304494);
				continue;
			}
			case 15U:
				goto IL_D2;
			case 16U:
				num8 = -1;
				goto IL_423;
			case 17U:
				num7 += 5;
				num5 = 992147925;
				continue;
			case 18U:
				array3[num7++] = 14;
				array3[num7++] = (byte)num10;
				if (num9 != -1)
				{
					num5 = -230693887;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_2B3;
			case 19U:
			{
				MethodInfo methodInfo = methods[num11];
				num5 = ((methodInfo.DeclaringType != fieldType) ? -514539040 : -2133871860);
				continue;
			}
			case 20U:
				num5 = (int)(num6 * 1081204523U ^ 680135589U);
				continue;
			case 21U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array4, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 1404697122;
				continue;
			}
			}
			break;
			IL_2B3:
			type = type2;
			num5 = ((!type.IsClass) ? -1776155202 : -437706897);
			continue;
			IL_423:
			num9 = num8;
			num10 = 0;
			num5 = -2050496056;
		}
		goto IL_458;
		IL_151:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_458:
		array3[num7++] = ((byte)fieldFromHandle.Name[3] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num7++] = (byte)tokenFor2;
		array3[num7++] = (byte)(tokenFor2 >> 8);
		array3[num7++] = (byte)(tokenFor2 >> 16);
		array3[num7++] = (byte)(tokenFor2 >> 24);
		array3[num7] = 42;
		dynamicILInfo.SetCode(array3, array4.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 30938592;
		goto IL_D7;
		IL_3ED:
		dynamicMethod = null;
		array4 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num11 = 0;
		num5 = -299531971;
		goto IL_D7;
	}

	// Token: 0x0600000C RID: 12 RVA: 0x00023230 File Offset: 0x00021430
	static void \u200D\u206A\u206D\u202D\u200B\u206A\u206B\u200C\u200B\u206B\u206F\u206E\u200B\u202C\u206D\u200F\u200F\u200E\u202C\u202A\u200F\u200E\u206F\u206F\u206B\u200D\u202A\u200F\u202C\u206D\u206B\u200B\u206E\u206F\u200B\u202E\u206E\u206F\u202E\u202C\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 8);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (0 & 31))) * 319829257;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_3DD;
		int num5;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		Type[] array4;
		byte[] array3;
		int num8;
		int num11;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num7;
			Type type;
			int num9;
			int num10;
			Type type2;
			switch ((num6 = (uint)(-(uint)((num4 ^ -179863945 + (-658473406 + -1832217144) - (-1118563197 + 1395969991 - (-565798484 - 1214063669))) * 168527627 ^ 2044760175 * -168024537))) % 21U)
			{
			case 0U:
			{
				ParameterInfo[] parameters;
				type = parameters[num7].ParameterType;
				goto IL_35D;
			}
			case 1U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array2 = new byte[2];
				array2[0] = 7;
				dynamicILInfo2.SetLocalSignature(array2);
				array3 = new byte[7 * array4.Length + 6];
				num8 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = 1692220399;
					continue;
				}
				num9 = 0;
				goto IL_191;
			}
			case 2U:
				num5 = ((num10 >= array4.Length) ? 1464169122 : -693807057);
				continue;
			case 3U:
				array3[num8++] = 14;
				array3[num8++] = (byte)num10;
				if (num7 != -1)
				{
					num5 = -846770867;
					continue;
				}
				type = methodBase.DeclaringType;
				goto IL_35D;
			case 4U:
				num5 = ((num11 >= methods.Length) ? 1989479921 : -1263058380);
				continue;
			case 5U:
			{
				int num12;
				ParameterInfo[] parameters2;
				array4[num12] = parameters2[num12].ParameterType;
				num12++;
				num5 = -1669142304;
				continue;
			}
			case 6U:
				num5 = (int)(((!type2.IsPointer) ? 1202009830U : 1838996923U) ^ num6 * 3444546132U);
				continue;
			case 7U:
				goto IL_3B4;
			case 8U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array4, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 1989479921;
				continue;
			}
			case 9U:
				num5 = (int)(((!type2.IsByRef) ? 2580346704U : 3679022562U) ^ num6 * 1056608919U);
				continue;
			case 10U:
				num9 = -1;
				goto IL_191;
			case 11U:
				goto IL_3DD;
			case 12U:
			{
				int num12;
				num5 = ((num12 >= array4.Length) ? -529567489 : 2142361350);
				continue;
			}
			case 14U:
				num7++;
				num10++;
				num5 = 1123777379;
				continue;
			case 15U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array4 = new Type[parameters2.Length];
				int num12 = 0;
				num5 = (int)(num6 * 3705728793U ^ 330213263U);
				continue;
			}
			case 16U:
				goto IL_D2;
			case 17U:
				num11++;
				num5 = -1290742307;
				continue;
			case 18U:
				num8 += 5;
				num5 = 1391840075;
				continue;
			case 19U:
			{
				MethodInfo methodInfo = methods[num11];
				num5 = ((methodInfo.DeclaringType != fieldType) ? 1548828381 : 1104735710);
				continue;
			}
			case 20U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type2.TypeHandle);
				array3[num8++] = 116;
				array3[num8++] = (byte)tokenFor;
				array3[num8++] = (byte)(tokenFor >> 8);
				array3[num8++] = (byte)(tokenFor >> 16);
				array3[num8++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 3658401730U ^ 1467242019U);
				continue;
			}
			}
			break;
			IL_191:
			num7 = num9;
			num10 = 0;
			num5 = 1123777379;
			continue;
			IL_35D:
			type2 = type;
			num5 = (type2.IsClass ? -266302549 : 493980307);
		}
		goto IL_474;
		IL_3B4:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_474:
		array3[num8++] = ((byte)fieldFromHandle.Name[3] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num8++] = (byte)tokenFor2;
		array3[num8++] = (byte)(tokenFor2 >> 8);
		array3[num8++] = (byte)(tokenFor2 >> 16);
		array3[num8++] = (byte)(tokenFor2 >> 24);
		array3[num8] = 42;
		dynamicILInfo.SetCode(array3, array4.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 851261887;
		goto IL_D7;
		IL_3DD:
		dynamicMethod = null;
		array4 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num11 = 0;
		num5 = -1290742307;
		goto IL_D7;
	}

	// Token: 0x0600000D RID: 13 RVA: 0x00023740 File Offset: 0x00021940
	static void \u202D\u206F\u206C\u202E\u206A\u206E\u206B\u200D\u202B\u200B\u202C\u200D\u206A\u202A\u206B\u200D\u206F\u206C\u206C\u202D\u202B\u202A\u202A\u202B\u206D\u200D\u206B\u206F\u202E\u202A\u206E\u202B\u202E\u206B\u206B\u206E\u200D\u200D\u206B\u206A\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = -895941077;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 8);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[num - 1]) << (16 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_24F;
		int num6;
		int num8;
		byte[] array2;
		int num9;
		Type[] array3;
		DynamicILInfo dynamicILInfo;
		MethodInfo[] methods;
		DynamicMethod dynamicMethod;
		for (;;)
		{
			IL_D7:
			int num5 = num6;
			uint num7;
			int num10;
			int num11;
			Type type;
			Type type2;
			int num13;
			switch ((num7 = (uint)(~(uint)(-num5 * -761239631 * 1864766183))) % 21U)
			{
			case 0U:
				num8++;
				num6 = -1551341311;
				continue;
			case 1U:
				array2[num9++] = 14;
				array2[num9++] = (byte)num10;
				if (num11 != -1)
				{
					num6 = 44850689;
					continue;
				}
				type = methodBase.DeclaringType;
				goto IL_3BC;
			case 2U:
			{
				int num12;
				ParameterInfo[] parameters;
				array3[num12] = parameters[num12].ParameterType;
				num12++;
				num6 = -2091214643;
				continue;
			}
			case 3U:
				num6 = (int)((type2.IsByRef ? 1612997004U : 3459370950U) ^ num7 * 1950798602U);
				continue;
			case 5U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type2.TypeHandle);
				array2[num9++] = 116;
				array2[num9++] = (byte)tokenFor;
				array2[num9++] = (byte)(tokenFor >> 8);
				array2[num9++] = (byte)(tokenFor >> 16);
				array2[num9++] = (byte)(tokenFor >> 24);
				num6 = (int)(num7 * 498694315U ^ 463784101U);
				continue;
			}
			case 6U:
				num6 = ((num10 < array3.Length) ? -851831933 : -1087669055);
				continue;
			case 7U:
				num6 = ((num8 >= methods.Length) ? 723834988 : 1496063421);
				continue;
			case 8U:
			{
				int num12;
				num6 = ((num12 >= array3.Length) ? -1356933168 : -1179855189);
				continue;
			}
			case 9U:
				num11++;
				num10++;
				num6 = -205232529;
				continue;
			case 10U:
			{
				MethodInfo methodInfo = methods[num8];
				num6 = ((methodInfo.DeclaringType == fieldType) ? 1624558065 : 1338444038);
				continue;
			}
			case 11U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array3, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num6 = 723834988;
				continue;
			}
			case 12U:
				num9 += 5;
				num6 = 536181924;
				continue;
			case 13U:
			{
				ParameterInfo[] parameters2;
				type = parameters2[num11].ParameterType;
				goto IL_3BC;
			}
			case 14U:
				goto IL_D2;
			case 15U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array3 = new Type[parameters.Length];
				int num12 = 0;
				num6 = (int)(num7 * 513499847U ^ 889386407U);
				continue;
			}
			case 16U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array2 = new byte[7 * array3.Length + 6];
				num9 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num6 = 257669723;
					continue;
				}
				num13 = 0;
				goto IL_3DF;
			}
			case 17U:
				goto IL_2DE;
			case 18U:
				goto IL_24F;
			case 19U:
				num13 = -1;
				goto IL_3DF;
			case 20U:
				num6 = (int)(((!type2.IsPointer) ? 3189661391U : 266750111U) ^ num7 * 2597947661U);
				continue;
			}
			break;
			IL_3BC:
			type2 = type;
			num6 = ((!type2.IsClass) ? 840126550 : 1842950082);
			continue;
			IL_3DF:
			num11 = num13;
			num10 = 0;
			num6 = -205232529;
		}
		goto IL_444;
		IL_2DE:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_444:
		array2[num9++] = ((byte)fieldFromHandle.Name[0] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array2[num9++] = (byte)tokenFor2;
		array2[num9++] = (byte)(tokenFor2 >> 8);
		array2[num9++] = (byte)(tokenFor2 >> 16);
		array2[num9++] = (byte)(tokenFor2 >> 24);
		array2[num9] = 42;
		dynamicILInfo.SetCode(array2, array3.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num6 = -857923229;
		goto IL_D7;
		IL_24F:
		dynamicMethod = null;
		array3 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num8 = 0;
		num6 = -1551341311;
		goto IL_D7;
	}

	// Token: 0x0600000E RID: 14 RVA: 0x00023C20 File Offset: 0x00021E20
	static void \u200B\u202A\u206D\u202C\u200D\u202B\u206A\u200F\u202D\u206F\u206B\u200D\u202C\u202B\u200C\u200B\u202C\u200D\u200C\u200B\u202A\u206E\u200C\u200E\u200E\u202C\u202E\u206B\u206E\u200F\u200F\u200D\u200C\u200F\u206C\u206B\u206D\u206E\u200B\u202E\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = -1945017015;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 8) + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 16);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (0 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_30C;
		int num6;
		Type[] array2;
		MethodInfo[] methods;
		int num10;
		DynamicMethod dynamicMethod;
		int num11;
		DynamicILInfo dynamicILInfo;
		byte[] array3;
		for (;;)
		{
			IL_D7:
			int num5 = num6;
			uint num7;
			int num8;
			Type type;
			int num12;
			Type type2;
			int num13;
			switch ((num7 = (uint)(-(uint)(~(-num5) * 1090888029))) % 22U)
			{
			case 0U:
				num6 = ((num8 < array2.Length) ? -1565393719 : 798638055);
				continue;
			case 1U:
				goto IL_2E3;
			case 2U:
				goto IL_30C;
			case 3U:
			{
				int num9;
				num6 = ((num9 >= array2.Length) ? -650594107 : 1272131299);
				continue;
			}
			case 4U:
				goto IL_D2;
			case 5U:
				num6 = (int)((type.IsPointer ? 1998558263U : 4257272396U) ^ num7 * 2978744993U);
				continue;
			case 7U:
			{
				MethodInfo methodInfo = methods[num10];
				num6 = ((methodInfo.DeclaringType != fieldType) ? -456739685 : 2039510464);
				continue;
			}
			case 8U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num6 = -172178604;
				continue;
			}
			case 9U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array2 = new Type[parameters.Length];
				int num9 = 0;
				num6 = (int)(num7 * 182258464U ^ 2869668402U);
				continue;
			}
			case 10U:
			{
				int num9;
				ParameterInfo[] parameters;
				array2[num9] = parameters[num9].ParameterType;
				num9++;
				num6 = -128272238;
				continue;
			}
			case 11U:
				num11 += 5;
				num6 = -1750314185;
				continue;
			case 12U:
				num6 = (int)((type.IsByRef ? 2960297108U : 2876806239U) ^ num7 * 3501320296U);
				continue;
			case 13U:
			{
				ParameterInfo[] parameters2;
				type2 = parameters2[num12].ParameterType;
				goto IL_434;
			}
			case 14U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array3[num11++] = 116;
				array3[num11++] = (byte)tokenFor;
				array3[num11++] = (byte)(tokenFor >> 8);
				array3[num11++] = (byte)(tokenFor >> 16);
				num6 = (int)(num7 * 3264158063U ^ 866165592U);
				continue;
			}
			case 15U:
				num13 = -1;
				goto IL_274;
			case 16U:
				array3[num11++] = 14;
				array3[num11++] = (byte)num8;
				if (num12 != -1)
				{
					num6 = -1807342452;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_434;
			case 17U:
				num6 = ((num10 < methods.Length) ? -1707759352 : -172178604);
				continue;
			case 18U:
				num10++;
				num6 = 100524680;
				continue;
			case 19U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array3 = new byte[7 * array2.Length + 6];
				num11 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num6 = 32939048;
					continue;
				}
				num13 = 0;
				goto IL_274;
			}
			case 20U:
				num12++;
				num8++;
				num6 = 1507507913;
				continue;
			case 21U:
			{
				int tokenFor;
				array3[num11++] = (byte)(tokenFor >> 24);
				num6 = (int)(num7 * 104566546U ^ 2141520649U);
				continue;
			}
			}
			break;
			IL_274:
			num12 = num13;
			num8 = 0;
			num6 = 1507507913;
			continue;
			IL_434:
			type = type2;
			num6 = ((!type.IsClass) ? 12571396 : -506844782);
		}
		goto IL_453;
		IL_2E3:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_453:
		array3[num11++] = ((byte)fieldFromHandle.Name[2] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num11++] = (byte)tokenFor2;
		array3[num11++] = (byte)(tokenFor2 >> 8);
		array3[num11++] = (byte)(tokenFor2 >> 16);
		array3[num11++] = (byte)(tokenFor2 >> 24);
		array3[num11] = 42;
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num6 = 1032316300;
		goto IL_D7;
		IL_30C:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num10 = 0;
		num6 = 100524680;
		goto IL_D7;
	}

	// Token: 0x0600000F RID: 15 RVA: 0x0002410C File Offset: 0x0002230C
	static void \u200E\u206C\u202B\u206C\u202E\u202D\u202E\u202A\u200E\u202E\u202B\u206B\u202E\u206E\u200B\u206E\u200E\u200F\u200B\u206C\u202B\u202C\u202C\u206C\u200E\u206A\u200D\u200D\u200D\u206B\u200F\u202D\u206F\u200E\u200E\u202E\u200D\u200E\u202E\u206A\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = 1053065861;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 8);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[num - 1]) << (16 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		Type[] array2;
		DynamicILInfo dynamicILInfo;
		byte[] array3;
		int num10;
		DynamicMethod dynamicMethod;
		for (;;)
		{
			IL_C6:
			int num5 = 966512545;
			for (;;)
			{
				int num6 = num5;
				uint num7;
				int num9;
				Type type;
				int num12;
				int num13;
				Type type2;
				switch ((num7 = (uint)(~(~(-1109210696) - (num6 + (~(1684963936 + 1606080026) + 975825215 * 290100108))) - -2018185687)) % 22U)
				{
				case 0U:
				{
					int num8;
					ParameterInfo[] parameters;
					array2[num8] = parameters[num8].ParameterType;
					num8++;
					num5 = 578582884;
					continue;
				}
				case 1U:
					num5 = ((num9 >= array2.Length) ? -2131347370 : 1328653921);
					continue;
				case 2U:
					num5 = (int)((type.IsPointer ? 4104824857U : 1527295606U) ^ num7 * 1399201691U);
					continue;
				case 3U:
					goto IL_222;
				case 5U:
					goto IL_C6;
				case 6U:
				{
					int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
					array3[num10++] = 116;
					array3[num10++] = (byte)tokenFor;
					array3[num10++] = (byte)(tokenFor >> 8);
					array3[num10++] = (byte)(tokenFor >> 16);
					array3[num10++] = (byte)(tokenFor >> 24);
					num5 = (int)(num7 * 3990140408U ^ 2170361053U);
					continue;
				}
				case 7U:
					num5 = (int)((methodBase.IsStatic ? 2423529166U : 3747355204U) ^ num7 * 2482881931U);
					continue;
				case 8U:
				{
					int num11;
					num11++;
					num5 = 2070785374;
					continue;
				}
				case 9U:
				{
					MethodInfo methodInfo;
					ParameterInfo[] parameters = methodInfo.GetParameters();
					array2 = new Type[parameters.Length];
					int num8 = 0;
					num5 = (int)(num7 * 3342641090U ^ 2270374922U);
					continue;
				}
				case 10U:
				{
					dynamicILInfo = dynamicMethod.GetDynamicILInfo();
					DynamicILInfo dynamicILInfo2 = dynamicILInfo;
					byte[] array4 = new byte[2];
					array4[0] = 7;
					dynamicILInfo2.SetLocalSignature(array4);
					array3 = new byte[7 * array2.Length + 6];
					num10 = 0;
					ParameterInfo[] parameters2 = methodBase.GetParameters();
					if (!methodBase.IsConstructor)
					{
						num5 = -1875460012;
						continue;
					}
					num12 = 0;
					goto IL_3F2;
				}
				case 11U:
					num13++;
					num9++;
					num5 = 550732207;
					continue;
				case 12U:
					num5 = (int)((type.IsByRef ? 4292663205U : 419592102U) ^ num7 * 3507619336U);
					continue;
				case 13U:
				{
					dynamicMethod = null;
					array2 = null;
					MethodInfo[] methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
					int num11 = 0;
					num5 = 2070785374;
					continue;
				}
				case 14U:
				{
					int num11;
					MethodInfo[] methods;
					MethodInfo methodInfo = methods[num11];
					num5 = ((methodInfo.DeclaringType == fieldType) ? 1607799567 : 1091805142);
					continue;
				}
				case 15U:
				{
					ParameterInfo[] parameters2;
					type2 = parameters2[num13].ParameterType;
					goto IL_3CF;
				}
				case 16U:
					num12 = -1;
					goto IL_3F2;
				case 17U:
				{
					Type declaringType = methodBase.DeclaringType;
					MethodInfo methodInfo;
					dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
					num5 = 1423328666;
					continue;
				}
				case 18U:
				{
					int num11;
					MethodInfo[] methods;
					num5 = ((num11 >= methods.Length) ? 1423328666 : 1355693146);
					continue;
				}
				case 19U:
					num10 += 5;
					num5 = 1971392205;
					continue;
				case 20U:
				{
					int num8;
					num5 = ((num8 < array2.Length) ? 374605244 : 688539101);
					continue;
				}
				case 21U:
					array3[num10++] = 14;
					array3[num10++] = (byte)num9;
					if (num13 != -1)
					{
						num5 = 1474679509;
						continue;
					}
					type2 = methodBase.DeclaringType;
					goto IL_3CF;
				}
				goto Block_1;
				IL_3CF:
				type = type2;
				num5 = (type.IsClass ? 860726332 : -1882742603);
				continue;
				IL_3F2:
				num13 = num12;
				num9 = 0;
				num5 = 550732207;
			}
		}
		Block_1:
		goto IL_479;
		IL_222:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_479:
		array3[num10++] = ((byte)fieldFromHandle.Name[2] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num10++] = (byte)tokenFor2;
		array3[num10++] = (byte)(tokenFor2 >> 8);
		array3[num10++] = (byte)(tokenFor2 >> 16);
		array3[num10++] = (byte)(tokenFor2 >> 24);
		array3[num10] = 42;
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
	}

	// Token: 0x06000010 RID: 16 RVA: 0x00024620 File Offset: 0x00022820
	static void \u202E\u200C\u200F\u206C\u206E\u202C\u200E\u206C\u202B\u200F\u206D\u200F\u206D\u202A\u202D\u200C\u202E\u202B\u206B\u202A\u202E\u200F\u200B\u202D\u202B\u200B\u206F\u206D\u202C\u206D\u202C\u202A\u206B\u206A\u206D\u200B\u200C\u206D\u202C\u200D\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 16);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (8 & 31))) * -580188447;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_3C4;
		int num5;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		Type[] array4;
		byte[] array3;
		int num7;
		int num10;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num8;
			Type type;
			int num11;
			Type type2;
			int num12;
			switch ((num6 = (uint)((645640425 * (-1645202352 + -1374451953) - (num4 - -((555007831 ^ -1280440338) * -2125389427))) * -1808434911 - 1892856068)) % 21U)
			{
			case 0U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array2 = new byte[2];
				array2[0] = 7;
				dynamicILInfo2.SetLocalSignature(array2);
				array3 = new byte[7 * array4.Length + 6];
				num7 = 0;
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = 1329279954;
					continue;
				}
				num8 = 0;
				goto IL_2F2;
			}
			case 1U:
			{
				int num9;
				ParameterInfo[] parameters2;
				array4[num9] = parameters2[num9].ParameterType;
				num9++;
				num5 = -2063892222;
				continue;
			}
			case 3U:
				num8 = -1;
				goto IL_2F2;
			case 4U:
				goto IL_229;
			case 5U:
				num5 = ((num10 < methods.Length) ? -1952674966 : -2048652292);
				continue;
			case 6U:
				num5 = (int)((type.IsByRef ? 3119614141U : 302556330U) ^ num6 * 3633315260U);
				continue;
			case 7U:
			{
				MethodInfo methodInfo = methods[num10];
				num5 = ((methodInfo.DeclaringType != fieldType) ? 1024510651 : 293094554);
				continue;
			}
			case 8U:
				num7 += 5;
				num5 = 1129608443;
				continue;
			case 9U:
			{
				ParameterInfo[] parameters;
				type2 = parameters[num11].ParameterType;
				goto IL_41F;
			}
			case 10U:
				goto IL_D2;
			case 11U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array4 = new Type[parameters2.Length];
				int num9 = 0;
				num5 = (int)(num6 * 1768745178U ^ 2940828874U);
				continue;
			}
			case 12U:
				num11++;
				num12++;
				num5 = -923364359;
				continue;
			case 13U:
				num5 = (int)(((!type.IsPointer) ? 2823768892U : 3901471291U) ^ num6 * 3043540202U);
				continue;
			case 14U:
				num10++;
				num5 = -675095746;
				continue;
			case 15U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array3[num7++] = 116;
				array3[num7++] = (byte)tokenFor;
				array3[num7++] = (byte)(tokenFor >> 8);
				array3[num7++] = (byte)(tokenFor >> 16);
				array3[num7++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 833861399U ^ 3352806743U);
				continue;
			}
			case 16U:
				goto IL_3C4;
			case 17U:
				array3[num7++] = 14;
				array3[num7++] = (byte)num12;
				if (num11 != -1)
				{
					num5 = 516665008;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_41F;
			case 18U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array4, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = -2048652292;
				continue;
			}
			case 19U:
			{
				int num9;
				num5 = ((num9 >= array4.Length) ? -143149338 : 1134855580);
				continue;
			}
			case 20U:
				num5 = ((num12 >= array4.Length) ? -1199853797 : 1981394632);
				continue;
			}
			break;
			IL_2F2:
			num11 = num8;
			num12 = 0;
			num5 = -923364359;
			continue;
			IL_41F:
			type = type2;
			num5 = (type.IsClass ? -1531715713 : -1513609907);
		}
		goto IL_464;
		IL_229:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_464:
		array3[num7++] = ((byte)fieldFromHandle.Name[4] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num7++] = (byte)tokenFor2;
		array3[num7++] = (byte)(tokenFor2 >> 8);
		array3[num7++] = (byte)(tokenFor2 >> 16);
		array3[num7++] = (byte)(tokenFor2 >> 24);
		array3[num7] = 42;
		dynamicILInfo.SetCode(array3, array4.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 1928561792;
		goto IL_D7;
		IL_3C4:
		dynamicMethod = null;
		array4 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num10 = 0;
		num5 = -675095746;
		goto IL_D7;
	}

	// Token: 0x06000011 RID: 17 RVA: 0x00024B20 File Offset: 0x00022D20
	static void \u202C\u202C\u206E\u200D\u202E\u200F\u206C\u202A\u206E\u200D\u200B\u200E\u202B\u200E\u206A\u206E\u200B\u206C\u200F\u206D\u202D\u200C\u206E\u206B\u200F\u200D\u206D\u202A\u200B\u200E\u206C\u202D\u206E\u200E\u206C\u200C\u202A\u202E\u206F\u200F\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 8);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[num - 1]) << (0 & 31))) * 1121757157;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_184;
		int num5;
		int num7;
		Type[] array2;
		byte[] array3;
		DynamicILInfo dynamicILInfo;
		DynamicMethod dynamicMethod;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			Type type;
			int num9;
			int num11;
			Type type2;
			int num12;
			switch ((num6 = (uint)((-num4 ^ -1336850985 + 53673386 - -258859864) - (-394971835 ^ 1091986661) - -70892138)) % 22U)
			{
			case 0U:
				num7++;
				num5 = -914976628;
				continue;
			case 1U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array2 = new Type[parameters.Length];
				int num8 = 0;
				num5 = (int)(num6 * 2088532655U ^ 279696462U);
				continue;
			}
			case 2U:
				num5 = (int)(((!type.IsByRef) ? 1870830493U : 2859894371U) ^ num6 * 1167211618U);
				continue;
			case 3U:
				num5 = ((num9 >= array2.Length) ? -839736014 : -1849978065);
				continue;
			case 4U:
			{
				int num10;
				array3[num10++] = 14;
				array3[num10++] = (byte)num9;
				if (num11 != -1)
				{
					num5 = -330210856;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_497;
			}
			case 6U:
			{
				int num10;
				num10 += 5;
				num5 = 884886052;
				continue;
			}
			case 7U:
				num12 = -1;
				goto IL_385;
			case 8U:
				goto IL_D2;
			case 9U:
				num11++;
				num9++;
				num5 = 933745228;
				continue;
			case 10U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				int num10;
				array3[num10++] = 116;
				array3[num10++] = (byte)tokenFor;
				array3[num10++] = (byte)(tokenFor >> 8);
				array3[num10++] = (byte)(tokenFor >> 16);
				array3[num10++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 3369440182U ^ 1489210508U);
				continue;
			}
			case 11U:
			{
				int num10;
				array3[num10++] = ((byte)fieldFromHandle.Name[1] ^ opKey);
				int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
				array3[num10++] = (byte)tokenFor2;
				array3[num10++] = (byte)(tokenFor2 >> 8);
				array3[num10++] = (byte)(tokenFor2 >> 16);
				array3[num10++] = (byte)(tokenFor2 >> 24);
				array3[num10] = 42;
				num5 = (int)(num6 * 3296107458U ^ 1057865600U);
				continue;
			}
			case 12U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array3 = new byte[7 * array2.Length + 6];
				int num10 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = -973828812;
					continue;
				}
				num12 = 0;
				goto IL_385;
			}
			case 13U:
			{
				MethodInfo methodInfo = methods[num7];
				num5 = ((methodInfo.DeclaringType == fieldType) ? 672771928 : -1685246747);
				continue;
			}
			case 14U:
				goto IL_184;
			case 15U:
			{
				ParameterInfo[] parameters;
				int num8;
				array2[num8] = parameters[num8].ParameterType;
				num8++;
				num5 = 875520795;
				continue;
			}
			case 16U:
				goto IL_45B;
			case 17U:
				num5 = ((num7 >= methods.Length) ? 998187615 : 711603136);
				continue;
			case 18U:
				num5 = (int)((type.IsPointer ? 2759988543U : 2163014377U) ^ num6 * 1068653686U);
				continue;
			case 19U:
			{
				ParameterInfo[] parameters2;
				type2 = parameters2[num11].ParameterType;
				goto IL_497;
			}
			case 20U:
			{
				int num8;
				num5 = ((num8 < array2.Length) ? -1791088322 : -1789071610);
				continue;
			}
			case 21U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 998187615;
				continue;
			}
			}
			break;
			IL_385:
			num11 = num12;
			num9 = 0;
			num5 = 933745228;
			continue;
			IL_497:
			type = type2;
			num5 = ((!type.IsClass) ? -116435029 : -249418413);
		}
		goto IL_4DC;
		IL_45B:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_4DC:
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = -345045205;
		goto IL_D7;
		IL_184:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num7 = 0;
		num5 = -914976628;
		goto IL_D7;
	}

	// Token: 0x06000012 RID: 18 RVA: 0x00025028 File Offset: 0x00023228
	static void \u200D\u202C\u206B\u206C\u206E\u206D\u200E\u202A\u200C\u202A\u202B\u202D\u200C\u202C\u202B\u206E\u200F\u200E\u202B\u200E\u200C\u206A\u206E\u206A\u206A\u200E\u202D\u202B\u206C\u200B\u202E\u200B\u200E\u202E\u200C\u206A\u200B\u202D\u200C\u202E\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = -836060517;
		int num3 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 8) + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[--num]) << 16);
		num--;
		int num4 = num2 * (num3 + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[num - 1]) << (24 & 31)));
		num4 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num4);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_1EE;
		int num6;
		DynamicILInfo dynamicILInfo;
		byte[] array2;
		int num8;
		int num9;
		Type[] array3;
		DynamicMethod dynamicMethod;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num5 = num6;
			uint num7;
			Type type;
			int num10;
			int num12;
			Type type2;
			int num13;
			switch ((num7 = (uint)(-(uint)((--1796457223 * -45812783 - ~num5) * 795806555))) % 21U)
			{
			case 0U:
				goto IL_1EE;
			case 1U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array2[num8++] = 116;
				array2[num8++] = (byte)tokenFor;
				array2[num8++] = (byte)(tokenFor >> 8);
				array2[num8++] = (byte)(tokenFor >> 16);
				array2[num8++] = (byte)(tokenFor >> 24);
				num6 = (int)(num7 * 3073806509U ^ 2424160604U);
				continue;
			}
			case 2U:
				num9++;
				num6 = 2107185494;
				continue;
			case 3U:
				num10 = -1;
				goto IL_17E;
			case 4U:
			{
				int num11;
				ParameterInfo[] parameters;
				array3[num11] = parameters[num11].ParameterType;
				num11++;
				num6 = 698515135;
				continue;
			}
			case 5U:
			{
				ParameterInfo[] parameters2;
				type2 = parameters2[num12].ParameterType;
				goto IL_3BE;
			}
			case 6U:
				goto IL_382;
			case 7U:
				goto IL_D2;
			case 8U:
				num6 = ((num13 < array3.Length) ? 269496177 : -2020525309);
				continue;
			case 9U:
				array2[num8++] = 14;
				array2[num8++] = (byte)num13;
				if (num12 != -1)
				{
					num6 = -73388833;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_3BE;
			case 10U:
				num6 = (int)((type.IsPointer ? 3017589266U : 3904799307U) ^ num7 * 3483735045U);
				continue;
			case 11U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array3, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num6 = 388816515;
				continue;
			}
			case 12U:
			{
				int num11;
				num6 = ((num11 >= array3.Length) ? 956868020 : 1807725284);
				continue;
			}
			case 13U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array2 = new byte[7 * array3.Length + 6];
				num8 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num6 = -1813503069;
					continue;
				}
				num10 = 0;
				goto IL_17E;
			}
			case 15U:
				num6 = (int)(((!type.IsByRef) ? 2888249135U : 346632767U) ^ num7 * 3084877516U);
				continue;
			case 16U:
				num8 += 5;
				num6 = -854896733;
				continue;
			case 17U:
				num6 = ((num9 < methods.Length) ? 750181580 : 388816515);
				continue;
			case 18U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array3 = new Type[parameters.Length];
				int num11 = 0;
				num6 = (int)(num7 * 1398579644U ^ 2056179327U);
				continue;
			}
			case 19U:
			{
				MethodInfo methodInfo = methods[num9];
				num6 = ((methodInfo.DeclaringType != fieldType) ? 1391598298 : 2038361304);
				continue;
			}
			case 20U:
				num12++;
				num13++;
				num6 = 36291504;
				continue;
			}
			break;
			IL_17E:
			num12 = num10;
			num13 = 0;
			num6 = 36291504;
			continue;
			IL_3BE:
			type = type2;
			num6 = ((!type.IsClass) ? -317322793 : 1079126837);
		}
		goto IL_448;
		IL_382:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_448:
		array2[num8++] = ((byte)fieldFromHandle.Name[3] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array2[num8++] = (byte)tokenFor2;
		array2[num8++] = (byte)(tokenFor2 >> 8);
		array2[num8++] = (byte)(tokenFor2 >> 16);
		array2[num8++] = (byte)(tokenFor2 >> 24);
		array2[num8] = 42;
		dynamicILInfo.SetCode(array2, array3.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num6 = 785298599;
		goto IL_D7;
		IL_1EE:
		dynamicMethod = null;
		array3 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num9 = 0;
		num6 = 2107185494;
		goto IL_D7;
	}

	// Token: 0x06000013 RID: 19 RVA: 0x0002550C File Offset: 0x0002370C
	static void \u200F\u202C\u206B\u206E\u206B\u202E\u206C\u202D\u200F\u206C\u202E\u200F\u206D\u202B\u200C\u202C\u202D\u200F\u200B\u200C\u202D\u202D\u200F\u202D\u206C\u206F\u200D\u206C\u206D\u202E\u200C\u200C\u206A\u200C\u206E\u200D\u200C\u202C\u202D\u206D\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 8) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 24);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (0 & 31))) * 1438732079;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_3C7;
		int num5;
		Type[] array2;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		byte[] array4;
		int num9;
		int num11;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num7;
			Type type;
			int num10;
			Type type2;
			int num12;
			switch ((num6 = (uint)(~(uint)(~(uint)(-num4 * -515849401)))) % 22U)
			{
			case 0U:
			{
				ParameterInfo[] parameters = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = (int)(num6 * 2007581189U ^ 2901515993U);
					continue;
				}
				num7 = 0;
				goto IL_16F;
			}
			case 1U:
			{
				int num8;
				num5 = ((num8 < array2.Length) ? -1678604202 : 2066679218);
				continue;
			}
			case 2U:
			{
				int num8;
				ParameterInfo[] parameters2;
				array2[num8] = parameters2[num8].ParameterType;
				num8++;
				num5 = 275428901;
				continue;
			}
			case 3U:
				num5 = (int)(((!type.IsByRef) ? 1438978524U : 2102140770U) ^ num6 * 4176956927U);
				continue;
			case 4U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array3 = new byte[2];
				array3[0] = 7;
				dynamicILInfo2.SetLocalSignature(array3);
				array4 = new byte[7 * array2.Length + 6];
				num9 = 0;
				num5 = -1025946286;
				continue;
			}
			case 5U:
			{
				ParameterInfo[] parameters;
				type2 = parameters[num10].ParameterType;
				goto IL_37F;
			}
			case 6U:
				num11++;
				num5 = -1431949678;
				continue;
			case 7U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array4[num9++] = 116;
				array4[num9++] = (byte)tokenFor;
				array4[num9++] = (byte)(tokenFor >> 8);
				array4[num9++] = (byte)(tokenFor >> 16);
				array4[num9++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 3675536747U ^ 623358616U);
				continue;
			}
			case 9U:
				num7 = -1;
				goto IL_16F;
			case 10U:
			{
				MethodInfo methodInfo = methods[num11];
				num5 = ((methodInfo.DeclaringType == fieldType) ? -764780882 : 59577674);
				continue;
			}
			case 11U:
				num10++;
				num12++;
				num5 = -2072501661;
				continue;
			case 12U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters2 = methodInfo.GetParameters();
				array2 = new Type[parameters2.Length];
				int num8 = 0;
				num5 = (int)(num6 * 2316402630U ^ 1243476177U);
				continue;
			}
			case 13U:
				num5 = ((num12 < array2.Length) ? -610199293 : -1903407808);
				continue;
			case 14U:
				num5 = ((num11 < methods.Length) ? -1485576258 : 65548916);
				continue;
			case 15U:
				goto IL_3C7;
			case 16U:
				goto IL_39E;
			case 17U:
				array4[num9++] = 14;
				array4[num9++] = (byte)num12;
				if (num10 != -1)
				{
					num5 = -1973781947;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_37F;
			case 18U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 65548916;
				continue;
			}
			case 19U:
				goto IL_D2;
			case 20U:
				num5 = (int)((type.IsPointer ? 3169553223U : 2427306051U) ^ num6 * 1279076300U);
				continue;
			case 21U:
				num9 += 5;
				num5 = 2110402219;
				continue;
			}
			break;
			IL_16F:
			num10 = num7;
			num12 = 0;
			num5 = -2072501661;
			continue;
			IL_37F:
			type = type2;
			num5 = (type.IsClass ? -1710915358 : 835635231);
		}
		goto IL_456;
		IL_39E:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_456:
		array4[num9++] = ((byte)fieldFromHandle.Name[0] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array4[num9++] = (byte)tokenFor2;
		array4[num9++] = (byte)(tokenFor2 >> 8);
		array4[num9++] = (byte)(tokenFor2 >> 16);
		array4[num9++] = (byte)(tokenFor2 >> 24);
		array4[num9] = 42;
		dynamicILInfo.SetCode(array4, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = -739280576;
		goto IL_D7;
		IL_3C7:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num11 = 0;
		num5 = -1431949678;
		goto IL_D7;
	}

	// Token: 0x06000014 RID: 20 RVA: 0x000259FC File Offset: 0x00023BFC
	static void \u206D\u202C\u202B\u202A\u206E\u206C\u202B\u206B\u200C\u206E\u200F\u200D\u206D\u200C\u200B\u200B\u206B\u200E\u206F\u202D\u200F\u206D\u200C\u206D\u202A\u206F\u202A\u206C\u202B\u202A\u200F\u206D\u200F\u200E\u202D\u200E\u202C\u206C\u200F\u200C\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[--num]) << 16) + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 0);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[1] ^ (char)array[num - 1]) << (8 & 31))) * 1560733943;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_21A;
		int num5;
		Type[] array2;
		byte[] array3;
		int num9;
		DynamicILInfo dynamicILInfo;
		DynamicMethod dynamicMethod;
		int num12;
		MethodInfo[] methods;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			int num8;
			Type type;
			Type type2;
			int num10;
			int num11;
			switch ((num6 = (uint)(-(num4 ^ -(~(-1337794679)) ^ -468928146) - -463852635)) % 25U)
			{
			case 0U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array2 = new Type[parameters.Length];
				int num7 = 0;
				num5 = (int)(num6 * 2111633934U ^ 3512762482U);
				continue;
			}
			case 1U:
			{
				ParameterInfo[] parameters2;
				type = parameters2[num8].ParameterType;
				goto IL_32B;
			}
			case 2U:
			{
				int tokenFor;
				array3[num9++] = (byte)(tokenFor >> 8);
				array3[num9++] = (byte)(tokenFor >> 16);
				array3[num9++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 1916317521U ^ 1215872609U);
				continue;
			}
			case 3U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type2.TypeHandle);
				array3[num9++] = 116;
				num5 = (int)(num6 * 2207419478U ^ 4269950410U);
				continue;
			}
			case 4U:
				num5 = (int)((type2.IsByRef ? 2733184256U : 2099255395U) ^ num6 * 2534655121U);
				continue;
			case 5U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array2, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = -319838336;
				continue;
			}
			case 7U:
				goto IL_21A;
			case 8U:
				num5 = (int)(num6 * 2560981900U ^ 273981883U);
				continue;
			case 9U:
				goto IL_1F1;
			case 10U:
			{
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array3 = new byte[7 * array2.Length + 6];
				num9 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = -251095057;
					continue;
				}
				num10 = 0;
				goto IL_499;
			}
			case 11U:
				array3[num9++] = 14;
				array3[num9++] = (byte)num11;
				if (num8 != -1)
				{
					num5 = -1751329119;
					continue;
				}
				type = methodBase.DeclaringType;
				goto IL_32B;
			case 12U:
			{
				ParameterInfo[] parameters;
				int num7;
				array2[num7] = parameters[num7].ParameterType;
				num7++;
				num5 = -261296718;
				continue;
			}
			case 13U:
			{
				int num7;
				num5 = ((num7 < array2.Length) ? -33715775 : 1076313108);
				continue;
			}
			case 14U:
				num10 = -1;
				goto IL_499;
			case 15U:
				num5 = (int)(((!type2.IsClass) ? 3143651717U : 512039713U) ^ num6 * 2145282189U);
				continue;
			case 16U:
				num9 += 5;
				num5 = -37111747;
				continue;
			case 17U:
				num5 = ((num12 < methods.Length) ? -369631686 : -320950865);
				continue;
			case 18U:
				num8++;
				num11++;
				num5 = -1312112437;
				continue;
			case 19U:
			{
				int tokenFor;
				array3[num9++] = (byte)tokenFor;
				num5 = (int)(num6 * 3070584980U ^ 3743928935U);
				continue;
			}
			case 20U:
				num12++;
				num5 = -1916684354;
				continue;
			case 21U:
			{
				MethodInfo methodInfo = methods[num12];
				num5 = ((methodInfo.DeclaringType != fieldType) ? 1176714692 : -89195297);
				continue;
			}
			case 22U:
				num5 = ((num11 < array2.Length) ? -191521563 : 1478665761);
				continue;
			case 23U:
				goto IL_D2;
			case 24U:
				num5 = (int)(((!type2.IsPointer) ? 1971201494U : 1748518753U) ^ num6 * 1143924626U);
				continue;
			}
			break;
			IL_32B:
			type2 = type;
			num5 = -1833912729;
			continue;
			IL_499:
			num8 = num10;
			num11 = 0;
			num5 = -1312112437;
		}
		goto IL_4A8;
		IL_1F1:
		fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
		return;
		IL_4A8:
		array3[num9++] = ((byte)fieldFromHandle.Name[2] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array3[num9++] = (byte)tokenFor2;
		array3[num9++] = (byte)(tokenFor2 >> 8);
		array3[num9++] = (byte)(tokenFor2 >> 16);
		array3[num9++] = (byte)(tokenFor2 >> 24);
		array3[num9] = 42;
		dynamicILInfo.SetCode(array3, array2.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = -1773315063;
		goto IL_D7;
		IL_21A:
		dynamicMethod = null;
		array2 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num12 = 0;
		num5 = -1916684354;
		goto IL_D7;
	}

	// Token: 0x06000015 RID: 21 RVA: 0x00025F40 File Offset: 0x00024140
	static void \u206E\u200D\u206D\u200D\u202D\u200B\u200D\u206B\u206B\u200B\u200F\u202C\u206C\u206D\u200F\u200F\u200B\u200F\u206F\u200D\u202D\u206F\u200F\u206F\u200E\u202D\u200D\u200D\u200F\u202D\u200B\u202E\u206F\u202C\u200E\u202E\u200F\u202D\u206D\u206F\u202E(RuntimeFieldHandle field, byte opKey)
	{
		FieldInfo fieldFromHandle = FieldInfo.GetFieldFromHandle(field);
		byte[] array = fieldFromHandle.Module.ResolveSignature(fieldFromHandle.MetadataToken);
		int num = array.Length;
		int num2 = fieldFromHandle.GetOptionalCustomModifiers()[0].MetadataToken + (int)((int)(fieldFromHandle.Name[3] ^ (char)array[--num]) << 0) + (int)((int)(fieldFromHandle.Name[2] ^ (char)array[--num]) << 24) + (int)((int)(fieldFromHandle.Name[4] ^ (char)array[--num]) << 16);
		num--;
		int num3 = (num2 + (int)((int)(fieldFromHandle.Name[0] ^ (char)array[num - 1]) << (8 & 31))) * 1538488735;
		num3 *= fieldFromHandle.GetCustomAttributes(false)[0].GetHashCode();
		MethodBase methodBase = fieldFromHandle.Module.ResolveMethod(num3);
		Type fieldType = fieldFromHandle.FieldType;
		if (methodBase.IsStatic)
		{
			goto IL_D2;
		}
		goto IL_3A2;
		int num5;
		DynamicMethod dynamicMethod;
		DynamicILInfo dynamicILInfo;
		byte[] array2;
		int num8;
		int num9;
		MethodInfo[] methods;
		Type[] array3;
		for (;;)
		{
			IL_D7:
			int num4 = num5;
			uint num6;
			Type type;
			int num7;
			int num10;
			int num11;
			Type type2;
			switch ((num6 = (uint)(~(uint)(((num4 ^ (-(~1871069341) ^ 518789031 - (904519231 + -2122169888))) - (--1718736163 + (-482993804 ^ 2076560582))) * -1555150243))) % 23U)
			{
			case 0U:
				num5 = (int)(((!type.IsByRef) ? 3015502367U : 3073649317U) ^ num6 * 1147544563U);
				continue;
			case 1U:
				dynamicILInfo = dynamicMethod.GetDynamicILInfo();
				num5 = -576557481;
				continue;
			case 2U:
				num7 = -1;
				goto IL_320;
			case 3U:
				num5 = (int)((type.IsPointer ? 1884196889U : 1419132754U) ^ num6 * 435406865U);
				continue;
			case 4U:
				return;
			case 5U:
				goto IL_3A2;
			case 6U:
				goto IL_D2;
			case 7U:
			{
				int tokenFor = dynamicILInfo.GetTokenFor(type.TypeHandle);
				array2[num8++] = 116;
				array2[num8++] = (byte)tokenFor;
				array2[num8++] = (byte)(tokenFor >> 8);
				array2[num8++] = (byte)(tokenFor >> 16);
				array2[num8++] = (byte)(tokenFor >> 24);
				num5 = (int)(num6 * 2759901197U ^ 1874719857U);
				continue;
			}
			case 8U:
				num5 = ((num9 < methods.Length) ? 658699764 : 1169099003);
				continue;
			case 9U:
				num8 += 5;
				num5 = -224605545;
				continue;
			case 10U:
				num5 = ((num10 < array3.Length) ? -160560044 : -1337209757);
				continue;
			case 11U:
				array2[num8++] = 14;
				array2[num8++] = (byte)num10;
				if (num11 != -1)
				{
					num5 = -2107729361;
					continue;
				}
				type2 = methodBase.DeclaringType;
				goto IL_2FD;
			case 12U:
				num9++;
				num5 = 2134425457;
				continue;
			case 13U:
			{
				MethodInfo methodInfo = methods[num9];
				num5 = ((methodInfo.DeclaringType != fieldType) ? -1891033212 : -757060100);
				continue;
			}
			case 14U:
			{
				int num12;
				num5 = ((num12 >= array3.Length) ? -1158398087 : 324989644);
				continue;
			}
			case 15U:
				num11++;
				num10++;
				num5 = -1073202687;
				continue;
			case 16U:
			{
				MethodInfo methodInfo;
				ParameterInfo[] parameters = methodInfo.GetParameters();
				array3 = new Type[parameters.Length];
				int num12 = 0;
				num5 = (int)(num6 * 3932968922U ^ 3736972701U);
				continue;
			}
			case 17U:
			{
				DynamicILInfo dynamicILInfo2 = dynamicILInfo;
				byte[] array4 = new byte[2];
				array4[0] = 7;
				dynamicILInfo2.SetLocalSignature(array4);
				array2 = new byte[7 * array3.Length + 6];
				num8 = 0;
				ParameterInfo[] parameters2 = methodBase.GetParameters();
				if (!methodBase.IsConstructor)
				{
					num5 = (int)(num6 * 415227732U ^ 3249183876U);
					continue;
				}
				num7 = 0;
				goto IL_320;
			}
			case 18U:
			{
				ParameterInfo[] parameters2;
				type2 = parameters2[num11].ParameterType;
				goto IL_2FD;
			}
			case 19U:
			{
				Type declaringType = methodBase.DeclaringType;
				MethodInfo methodInfo;
				dynamicMethod = new DynamicMethod("", methodInfo.ReturnType, array3, (declaringType.IsInterface || declaringType.IsArray) ? fieldType : declaringType, true);
				num5 = 1169099003;
				continue;
			}
			case 20U:
				fieldFromHandle.SetValue(null, Delegate.CreateDelegate(fieldType, (MethodInfo)methodBase));
				num5 = (int)(num6 * 2821167630U ^ 2655822557U);
				continue;
			case 22U:
			{
				int num12;
				ParameterInfo[] parameters;
				array3[num12] = parameters[num12].ParameterType;
				num12++;
				num5 = 505582481;
				continue;
			}
			}
			break;
			IL_2FD:
			type = type2;
			num5 = (type.IsClass ? 463426323 : -355583386);
			continue;
			IL_320:
			num11 = num7;
			num10 = 0;
			num5 = -1073202687;
		}
		array2[num8++] = ((byte)fieldFromHandle.Name[1] ^ opKey);
		int tokenFor2 = dynamicILInfo.GetTokenFor(methodBase.MethodHandle);
		array2[num8++] = (byte)tokenFor2;
		array2[num8++] = (byte)(tokenFor2 >> 8);
		array2[num8++] = (byte)(tokenFor2 >> 16);
		array2[num8++] = (byte)(tokenFor2 >> 24);
		array2[num8] = 42;
		dynamicILInfo.SetCode(array2, array3.Length + 1);
		fieldFromHandle.SetValue(null, dynamicMethod.CreateDelegate(fieldType));
		return;
		IL_D2:
		num5 = 649733383;
		goto IL_D7;
		IL_3A2:
		dynamicMethod = null;
		array3 = null;
		methods = fieldFromHandle.FieldType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic);
		num9 = 0;
		num5 = 2134425457;
		goto IL_D7;
	}

	// Token: 0x06000016 RID: 22 RVA: 0x00026470 File Offset: 0x00024670
	internal static byte[] \u206B\u202C\u206C\u206A\u206A\u200B\u206E\u206D\u200B\u206D\u202D\u202D\u202E\u202A\u200E\u206E\u206F\u200B\u200E\u206E\u200C\u200F\u206B\u202A\u200C\u206E\u206C\u206E\u200E\u200C\u200C\u206B\u202B\u200F\u200E\u200E\u206F\u200F\u200D\u200D\u202E(byte[] data)
	{
		MemoryStream memoryStream = new MemoryStream(data);
		<Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E u200D_u202C_u206E_u206E_u200E_u206F_u202A_u202A_u200E_u202D_u200F_u202B_u206B_u200D_u200D_u206E_u200F_u200B_u202D_u206F_u206C_u200B_u200D_u200B_u200F_u200C_u206C_u206F_u202C_u206B_u200B_u200C_u200D_u206B_u206E_u202A_u202A_u202E_u206D_u206F_u202E = new <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E();
		byte[] array = new byte[5];
		int num = 0;
		for (;;)
		{
			IL_12D:
			int num2 = (num >= 5) ? -92732097 : 763219675;
			for (;;)
			{
				int num3 = num2;
				uint num4;
				switch ((num4 = (uint)(~(134591786 - (764844778 + 347897795) + ~(1051120697 ^ -2096677967) - num3 - -1417857317 * -744015093) + -1823431454)) % 9U)
				{
				case 0U:
					num2 = 763219675;
					continue;
				case 1U:
					num2 = ((num >= 4) ? 991694343 : 1731233065);
					continue;
				case 2U:
					Array.Reverse(array, 0, 4);
					num2 = (int)(num4 * 2401549805U ^ 1354317697U);
					continue;
				case 3U:
					num += memoryStream.Read(array, num, 5 - num);
					num2 = 1278209264;
					continue;
				case 5U:
					num2 = (int)(((!BitConverter.IsLittleEndian) ? 893090292U : 1159906547U) ^ num4 * 1216102267U);
					continue;
				case 6U:
					num += memoryStream.Read(array, num, 4 - num);
					num2 = 1790468513;
					continue;
				case 7U:
					goto IL_12D;
				case 8U:
					u200D_u202C_u206E_u206E_u200E_u206F_u202A_u202A_u200E_u202D_u200F_u202B_u206B_u200D_u200D_u206E_u200F_u200B_u202D_u206F_u206C_u200B_u200D_u200B_u200F_u200C_u206C_u206F_u202C_u206B_u200B_u200C_u200D_u206B_u206E_u202A_u202A_u202E_u206D_u206F_u202E.\u200F\u206A\u206B\u206A\u206B\u202E\u202E\u206C\u206A\u206D\u206D\u202C\u206E\u202C\u200D\u200F\u202C\u202B\u202D\u200D\u206F\u200C\u202D\u206D\u202D\u200F\u200B\u206C\u200E\u202C\u206B\u202E\u200F\u202B\u206F\u202D\u206D\u202A\u206F\u206E\u202E(array);
					num = 0;
					num2 = (int)(num4 * 2321865017U ^ 1485244556U);
					continue;
				}
				goto Block_1;
			}
		}
		Block_1:
		int num5 = BitConverter.ToInt32(array, 0);
		byte[] array2 = new byte[num5];
		MemoryStream outStream = new MemoryStream(array2, true);
		long inSize = memoryStream.Length - 5L - 4L;
		u200D_u202C_u206E_u206E_u200E_u206F_u202A_u202A_u200E_u202D_u200F_u202B_u206B_u200D_u200D_u206E_u200F_u200B_u202D_u206F_u206C_u200B_u200D_u200B_u200F_u200C_u206C_u206F_u202C_u206B_u200B_u200C_u200D_u206B_u206E_u202A_u202A_u202E_u206D_u206F_u202E.\u206B\u202A\u200F\u202D\u206F\u206F\u200C\u200E\u200D\u206C\u206A\u200F\u206A\u200D\u200D\u206E\u200D\u206E\u202B\u200B\u200D\u206C\u206F\u206D\u206E\u200E\u206A\u206C\u202B\u206E\u206C\u200B\u206C\u206E\u206C\u206C\u200F\u202C\u206F\u200D\u202E(memoryStream, outStream, inSize, (long)num5);
		return array2;
	}

	// Token: 0x06000017 RID: 23 RVA: 0x000265F8 File Offset: 0x000247F8
	internal static void \u200F\u206F\u202D\u202A\u202E\u202C\u202B\u206C\u200B\u200D\u202A\u200E\u200D\u202C\u200E\u200E\u206F\u202E\u200F\u206C\u202E\u202A\u206A\u206D\u202D\u206C\u200E\u202A\u202A\u202C\u202E\u206F\u206E\u202D\u206D\u200C\u202B\u200E\u206A\u202E\u202E()
	{
		uint num = 27216U;
		uint[] array = new uint[]
		{
			954855869U,
			1086062455U,
			3874296140U,
			3443275785U,
			4201658381U,
			2513249140U,
			1879805689U,
			2004348984U,
			1470347139U,
			4195144487U,
			3811265324U,
			3757258819U,
			519846813U,
			1359512294U,
			867701729U,
			386413327U,
			877742188U,
			3480591044U,
			2781397728U,
			3386223296U,
			2198171070U,
			3801817871U,
			389722143U,
			205953369U,
			2729389309U,
			1579026015U,
			3909221525U,
			690662566U,
			3849775457U,
			2483200209U,
			1389478781U,
			3194622164U,
			1468866677U,
			2568819117U,
			2125292243U,
			2806853063U,
			606709764U,
			3740913601U,
			1719201018U,
			3967576537U,
			2296327582U,
			1470795242U,
			3153431828U,
			524913511U,
			2290557501U,
			1110349135U,
			4120211703U,
			3941633380U,
			3856025019U,
			791357090U,
			631967569U,
			2970868727U,
			3105303179U,
			3439106303U,
			3897463549U,
			2293959620U,
			1660898286U,
			1901301235U,
			3346078873U,
			2470240182U,
			1929201433U,
			1409583604U,
			412227337U,
			1950497574U,
			351349769U,
			2301961684U,
			74285047U,
			819972560U,
			3687558963U,
			1228319176U,
			2573341231U,
			924411972U,
			1794058424U,
			1263665277U,
			1482143409U,
			3218710375U,
			4208292923U,
			4166716595U,
			183685765U,
			1171686401U,
			306738084U,
			2576989327U,
			3502191128U,
			3063386185U,
			1848216648U,
			2743554648U,
			2789558228U,
			2205521172U,
			1743208245U,
			1410482723U,
			39230675U,
			617661791U,
			1418064077U,
			3273263936U,
			2695429391U,
			2404682352U,
			568882352U,
			2308819229U,
			2309709781U,
			1229441663U,
			4171268178U,
			650912417U,
			65416679U,
			3175693630U,
			1929619648U,
			3800179524U,
			1496320059U,
			3755144672U,
			2701234664U,
			3226786834U,
			2220954342U,
			2495867365U,
			2051067879U,
			1651556666U,
			922292763U,
			3975184577U,
			2019731058U,
			804315951U,
			167716133U,
			182848800U,
			1965567642U,
			3937270738U,
			563961624U,
			3152691160U,
			2862422089U,
			2041625974U,
			3588341461U,
			2434043879U,
			1667221606U,
			1207611112U,
			190470792U,
			1661746820U,
			2431021664U,
			3127107708U,
			3687894453U,
			240694988U,
			1097439459U,
			3881190841U,
			3255270658U,
			1780926309U,
			1056521801U,
			303102387U,
			1227130622U,
			1651664904U,
			3682161514U,
			645350925U,
			3043145318U,
			4014779949U,
			649111827U,
			2353056803U,
			1364758881U,
			2873343213U,
			2005803111U,
			565195659U,
			2371077697U,
			2698389759U,
			3847144550U,
			3711284985U,
			2072377131U,
			3611278722U,
			265998372U,
			666671320U,
			560789801U,
			3965872245U,
			66975412U,
			3556197253U,
			3320573546U,
			3933047320U,
			2785637801U,
			468866249U,
			4263227260U,
			1900013588U,
			845003864U,
			2070575858U,
			3203229689U,
			141758702U,
			821417289U,
			4158318144U,
			1427981453U,
			1347826394U,
			3141516667U,
			531226523U,
			4120876709U,
			3772833714U,
			3372175968U,
			3756100861U,
			500438982U,
			143614308U,
			1616562873U,
			3176386647U,
			2614358166U,
			4072184212U,
			2982479609U,
			3688026605U,
			665114269U,
			4193433207U,
			4118948135U,
			3918360979U,
			28242253U,
			3572631363U,
			4063498646U,
			3130096522U,
			2898981721U,
			2600104U,
			1725057526U,
			3979869046U,
			848703386U,
			300946735U,
			1840414133U,
			2878875959U,
			1176413996U,
			2442187238U,
			1358393240U,
			414308492U,
			1856114084U,
			1072859025U,
			3346298250U,
			2504449559U,
			2966790510U,
			1876723783U,
			2399428187U,
			2454481987U,
			1995062178U,
			579292204U,
			2552507660U,
			69108630U,
			4288880158U,
			4020710864U,
			2597460347U,
			3847467154U,
			2791805288U,
			251054834U,
			4021802302U,
			445039111U,
			3420026777U,
			1360270143U,
			1888126053U,
			511304368U,
			679787704U,
			2022997663U,
			3965041530U,
			3486051869U,
			3555145147U,
			274596025U,
			2999501219U,
			1620554074U,
			3919108522U,
			2798231167U,
			3682162350U,
			1786696653U,
			1894409782U,
			1363982071U,
			1610852004U,
			3735000861U,
			2252585503U,
			1447347411U,
			2408898077U,
			1340665023U,
			1856552001U,
			2333552335U,
			1217861091U,
			2774691135U,
			563253263U,
			469830963U,
			249110229U,
			2253356964U,
			2815564561U,
			3664660354U,
			2034025851U,
			2940357218U,
			4076389185U,
			325003301U,
			855929629U,
			1226914041U,
			2221107907U,
			755875399U,
			1914846423U,
			1497471662U,
			1600384135U,
			3727219929U,
			2843667254U,
			2200601619U,
			42825052U,
			2561892955U,
			3306265857U,
			2520087387U,
			4183419841U,
			4284314700U,
			2161351625U,
			1404248813U,
			2283758439U,
			3271172031U,
			543612758U,
			2108444261U,
			3273524828U,
			2571580331U,
			1702919041U,
			151766792U,
			1809421048U,
			2160155049U,
			3746566800U,
			1483944406U,
			1044665145U,
			2121612182U,
			2253748776U,
			1215156253U,
			3736782994U,
			3247413423U,
			3074670096U,
			284263283U,
			2936589667U,
			614237132U,
			2721173610U,
			1326758833U,
			3909291473U,
			1026911334U,
			1623455528U,
			3830342677U,
			999382377U,
			1373539587U,
			4090648441U,
			2339593112U,
			1677582189U,
			2293930759U,
			2995254620U,
			4195295309U,
			2880414187U,
			1389262020U,
			1431210980U,
			1537584829U,
			1468333777U,
			762963022U,
			1865563097U,
			2983047638U,
			933936404U,
			2964582596U,
			1715878575U,
			2117254514U,
			3136852026U,
			1785480743U,
			1496629467U,
			1774024501U,
			2451487252U,
			3816074302U,
			441271754U,
			2793255545U,
			1302464072U,
			1755102518U,
			3639483803U,
			3884805521U,
			4199655601U,
			2053116286U,
			2633281616U,
			533836129U,
			3025481904U,
			2886487365U,
			535298222U,
			1033369493U,
			2506057074U,
			3317881589U,
			3196261574U,
			3657127851U,
			2635964711U,
			1924605103U,
			2985934826U,
			423404827U,
			2003548371U,
			2449944314U,
			2543337321U,
			1547124426U,
			2278706754U,
			3378982476U,
			634196026U,
			3842394060U,
			2624845695U,
			552355464U,
			3405644850U,
			3256724503U,
			3057139202U,
			569920215U,
			741107822U,
			4096155765U,
			3595216256U,
			2319180114U,
			3194033940U,
			3245364193U,
			1241820068U,
			4081637697U,
			2386233130U,
			2543104214U,
			4143356055U,
			3976389083U,
			1210234596U,
			3717375104U,
			2180568891U,
			3176529191U,
			3593192221U,
			3875948967U,
			406590122U,
			1360370927U,
			1135401307U,
			1850138344U,
			578119925U,
			3531536266U,
			405621699U,
			378954329U,
			2612911435U,
			2446680586U,
			2566504491U,
			1143051998U,
			3616251645U,
			2143392512U,
			564041965U,
			3433315612U,
			4132381933U,
			2607745896U,
			1850970352U,
			2595682531U,
			2778808978U,
			3711912882U,
			524165352U,
			2875557322U,
			3043386787U,
			79205121U,
			2500166163U,
			2057901311U,
			93428690U,
			4203710986U,
			2023243790U,
			2749637846U,
			232320043U,
			955194203U,
			2467996822U,
			2500311433U,
			823906243U,
			340197598U,
			4070691134U,
			411212664U,
			2208818435U,
			3263485424U,
			2486941094U,
			3698664076U,
			1236518957U,
			4105990350U,
			3771767323U,
			1534488389U,
			3857458466U,
			3048076110U,
			4030136747U,
			1816914487U,
			2377558769U,
			2185082406U,
			723827078U,
			1340625334U,
			4213081305U,
			2719862555U,
			581094770U,
			276833521U,
			1526170453U,
			2454846582U,
			1541832569U,
			2719009313U,
			2854298092U,
			2107089441U,
			4066362924U,
			3809146005U,
			3331995200U,
			376494254U,
			3370431654U,
			537908363U,
			2633002229U,
			3306567593U,
			4222629904U,
			1868019U,
			1677370799U,
			1509170790U,
			2674336410U,
			1821682216U,
			417276325U,
			417919303U,
			996529563U,
			3408835198U,
			3562250039U,
			362702029U,
			3198861897U,
			937005992U,
			114250992U,
			2659851215U,
			610225990U,
			1573597788U,
			2904545678U,
			277668577U,
			2696474383U,
			4146036038U,
			864813716U,
			4181740671U,
			2271490947U,
			3914868544U,
			3867581468U,
			731211440U,
			2220817375U,
			3740303202U,
			317609673U,
			3030675245U,
			1825154981U,
			1355230130U,
			320946755U,
			2816628683U,
			2059885539U,
			932579880U,
			2505041365U,
			1809466097U,
			1308898278U,
			3027256434U,
			2074095650U,
			496338826U,
			684585409U,
			3019524620U,
			1744226871U,
			852017112U,
			3114202489U,
			749628159U,
			1008274722U,
			1303606931U,
			2900447927U,
			2186985663U,
			1072536520U,
			3244983942U,
			1013444490U,
			2598341032U,
			3467909334U,
			2411443481U,
			1651269211U,
			526498115U,
			2871514005U,
			4021665076U,
			4136751521U,
			1369101077U,
			1499839613U,
			877733947U,
			2799287976U,
			714774239U,
			1282554307U,
			634712817U,
			623536692U,
			3344315563U,
			4139668762U,
			1929409721U,
			3695936719U,
			3510871403U,
			3206889901U,
			3138718058U,
			688644237U,
			2338203625U,
			82375584U,
			140827623U,
			2744246934U,
			400644960U,
			829563038U,
			3577528748U,
			2399521420U,
			1282127679U,
			2196272384U,
			1095280526U,
			559238516U,
			2693542047U,
			3443205071U,
			3226590858U,
			4171117623U,
			3514424892U,
			3678538752U,
			835860521U,
			181900142U,
			1199270700U,
			892168988U,
			140772196U,
			2972089124U,
			2234938977U,
			350005159U,
			573256973U,
			3568982549U,
			124169510U,
			609814312U,
			1870695132U,
			1234667172U,
			1336711100U,
			730613847U,
			1277504931U,
			4017953419U,
			1117988294U,
			2096353812U,
			1476995835U,
			4185694591U,
			2419988923U,
			3548080079U,
			3540600518U,
			1062143029U,
			4013762607U,
			562352392U,
			1166239767U,
			2254865845U,
			3637378284U,
			2967995878U,
			1628067183U,
			106365822U,
			3464125674U,
			3335284134U,
			3934002794U,
			2305375155U,
			2579318282U,
			2051739910U,
			1827911743U,
			425039990U,
			204355037U,
			4101764812U,
			3488045434U,
			2523852187U,
			172757278U,
			740112060U,
			2979913992U,
			566114806U,
			1664694520U,
			2196649545U,
			4085188599U,
			2550511379U,
			2437402973U,
			598086U,
			1863580641U,
			4131331165U,
			909458662U,
			2574171170U,
			3316912929U,
			3741699654U,
			1089524600U,
			19863816U,
			1103495374U,
			1556455474U,
			4004685409U,
			1995480038U,
			1291852486U,
			1115854047U,
			762044023U,
			4152208299U,
			1766386046U,
			830799889U,
			209344939U,
			2613105484U,
			1953778553U,
			1531086780U,
			2497227678U,
			4170297448U,
			1948557044U,
			3928865152U,
			267992888U,
			3590483396U,
			3844529149U,
			1293262914U,
			3383702210U,
			102822U,
			776530451U,
			1838269152U,
			1244566032U,
			4240646622U,
			2703412463U,
			1988048102U,
			3803083894U,
			650549082U,
			3146247405U,
			539849997U,
			3640134986U,
			891905426U,
			1796421439U,
			1930919516U,
			379274731U,
			4102722183U,
			3500195265U,
			3732351674U,
			3516223614U,
			1654052316U,
			2565451606U,
			2667131850U,
			2857864241U,
			254896612U,
			3964640877U,
			540391491U,
			957974423U,
			675759778U,
			2019878810U,
			3851447471U,
			441614788U,
			455896937U,
			2182004450U,
			761032948U,
			1649600534U,
			2373990145U,
			3164230846U,
			120547081U,
			3421699909U,
			556931918U,
			2052437904U,
			61886282U,
			1170466356U,
			546190087U,
			3567638012U,
			1514244655U,
			700148369U,
			3135418671U,
			426027792U,
			1696804173U,
			1456237881U,
			4095693931U,
			3368165815U,
			799497640U,
			3575332693U,
			68275013U,
			3212281001U,
			2710835506U,
			3122535921U,
			1448127478U,
			2607001111U,
			286857765U,
			256376276U,
			1332832808U,
			1855991805U,
			77756422U,
			3026613170U,
			2143162926U,
			442401507U,
			128372905U,
			4222046655U,
			2102834502U,
			1489712591U,
			3224984745U,
			590645559U,
			2912096362U,
			741168335U,
			2476447011U,
			4059397975U,
			1749675644U,
			2189135603U,
			2642072657U,
			716797635U,
			1102260210U,
			538826082U,
			1630124994U,
			513556443U,
			1260107189U,
			2901878579U,
			3717179582U,
			1222134135U,
			558498968U,
			3770058358U,
			2932553426U,
			960412362U,
			966455151U,
			2484364210U,
			1760655235U,
			459496062U,
			278776940U,
			129895224U,
			6336608U,
			3087083035U,
			791768985U,
			1313082010U,
			3327065787U,
			99994118U,
			2348390099U,
			2079626743U,
			2503560424U,
			3828907363U,
			1374584039U,
			1437215179U,
			388493660U,
			782963530U,
			3693338698U,
			3870751008U,
			297892698U,
			2075986436U,
			2208031994U,
			2365115575U,
			192167071U,
			4153553598U,
			3561875307U,
			3759475076U,
			2099248962U,
			561496684U,
			3688929802U,
			493508039U,
			2075086589U,
			359393265U,
			4081611414U,
			1776962815U,
			4126472114U,
			2585824616U,
			3785312098U,
			1194090274U,
			3031046123U,
			2588806190U,
			2031314992U,
			490827938U,
			1766939443U,
			2294799868U,
			359902415U,
			4097572871U,
			4243521756U,
			1613679247U,
			3398454292U,
			4259421966U,
			4107169613U,
			849136347U,
			3770402884U,
			1792765611U,
			61702287U,
			3220691284U,
			2692564086U,
			232748119U,
			4159578127U,
			1581087677U,
			4170963299U,
			1723465827U,
			1177256191U,
			283857404U,
			1277980762U,
			1660998826U,
			967413390U,
			1350368353U,
			4233195898U,
			1686011307U,
			2826036939U,
			525587934U,
			1193800013U,
			2603445858U,
			3578032375U,
			3556847274U,
			67277001U,
			4080301810U,
			2041270332U,
			2166253522U,
			70099746U,
			2452134569U,
			2086372224U,
			694139259U,
			749502369U,
			3308422484U,
			2368743896U,
			304184516U,
			1703527052U,
			2710949364U,
			2171039117U,
			564385866U,
			3673235391U,
			1735094594U,
			85847786U,
			3646248087U,
			3710294737U,
			3773554370U,
			1743312179U,
			464787580U,
			2128716632U,
			1362247475U,
			735242528U,
			1527475175U,
			3305070963U,
			1554862890U,
			584617325U,
			852319902U,
			583133062U,
			348941755U,
			1805544462U,
			3857790390U,
			3301109080U,
			1705075237U,
			350597522U,
			3461219016U,
			2706120891U,
			4031062169U,
			2336620756U,
			3834803126U,
			1013300259U,
			884726403U,
			2486494917U,
			2559340656U,
			692019087U,
			2904764594U,
			2654424347U,
			3628673381U,
			3519361710U,
			907457042U,
			1984171999U,
			1413737262U,
			21088411U,
			1779815137U,
			1407003456U,
			2112358563U,
			2984262509U,
			2676717454U,
			2050179156U,
			1863561441U,
			967777910U,
			1547804343U,
			4039580960U,
			2324629454U,
			232746347U,
			1963375618U,
			1854278338U,
			1403080943U,
			3836803399U,
			2762552359U,
			2023511170U,
			577172801U,
			820311909U,
			2539194837U,
			2956638948U,
			3302568174U,
			2388794323U,
			1821761662U,
			3209733947U,
			332661350U,
			2522534608U,
			3134276567U,
			3342383779U,
			1080377095U,
			4250962146U,
			4236120290U,
			861049898U,
			2747027067U,
			3151801644U,
			1456015443U,
			2990264341U,
			3886155286U,
			876276877U,
			3681298851U,
			1203233623U,
			868269375U,
			4244269615U,
			604905870U,
			3434109296U,
			1285850660U,
			169946973U,
			2260045513U,
			112605214U,
			1132731385U,
			1143120072U,
			2764880262U,
			219766277U,
			3536063063U,
			4258010836U,
			1453436815U,
			3793246564U,
			4032653568U,
			2235575693U,
			151062423U,
			3597262502U,
			2744389587U,
			1242303556U,
			1766907183U,
			3421347814U,
			767791836U,
			1907790285U,
			1224756745U,
			1483269716U,
			3551456089U,
			2102003201U,
			2537002465U,
			434104986U,
			905405538U,
			393478433U,
			3238149511U,
			1975745285U,
			2465527636U,
			3269198104U,
			4030888469U,
			132222088U,
			1867402969U,
			3136499560U,
			3314030355U,
			438977260U,
			3549347801U,
			976108368U,
			3980738116U,
			3954306871U,
			1893456217U,
			2524618557U,
			3401916361U,
			1216507942U,
			3823184937U,
			3487554350U,
			2137187790U,
			3916076638U,
			2073034804U,
			2649327331U,
			1642099669U,
			1931858642U,
			1546721937U,
			3216807216U,
			1764452447U,
			3945074258U,
			2651619286U,
			3298711566U,
			64305073U,
			822595771U,
			2222888557U,
			2343986813U,
			2741390119U,
			1653820655U,
			2854201104U,
			1870627401U,
			3961611695U,
			422262830U,
			590890507U,
			3537182334U,
			2551835311U,
			2939786211U,
			2724072006U,
			1036121851U,
			12829365U,
			4035811973U,
			1482925685U,
			2564494806U,
			338406440U,
			1205467328U,
			1866046565U,
			1370833748U,
			1046008271U,
			984155827U,
			3216054521U,
			1345424273U,
			1017687537U,
			4206090999U,
			318634122U,
			3007477710U,
			1342585217U,
			536309108U,
			74722197U,
			686076532U,
			3774598685U,
			666121137U,
			2853816477U,
			1600151833U,
			673487240U,
			1544270466U,
			3827663373U,
			1656405967U,
			2765406373U,
			1489999102U,
			1287712201U,
			1002732313U,
			3727319185U,
			457811883U,
			1886648127U,
			2853496682U,
			1834118136U,
			953189939U,
			113315757U,
			2679112562U,
			404799213U,
			326635468U,
			201426950U,
			2284284953U,
			2549946820U,
			96133767U,
			1586548760U,
			82501142U,
			2915143223U,
			4058516599U,
			3354358129U,
			3833606287U,
			3739507609U,
			3206768547U,
			2229096742U,
			2611915675U,
			2560016245U,
			2317936739U,
			1554882248U,
			7941204U,
			1652251853U,
			2322044289U,
			951079250U,
			842416094U,
			2830593594U,
			206657665U,
			768480507U,
			2599908358U,
			4150195304U,
			1601257768U,
			2015363019U,
			3398084167U,
			3398836632U,
			4273651584U,
			3447326250U,
			1425254896U,
			2612645617U,
			3341614928U,
			1276562904U,
			2522359195U,
			388693583U,
			3043558409U,
			462299087U,
			3454134691U,
			2769493552U,
			2520493389U,
			933959735U,
			3635669298U,
			4142373447U,
			1465367427U,
			3652420571U,
			3922599049U,
			3562469548U,
			2357090552U,
			3931470652U,
			585394511U,
			3656542012U,
			2040215550U,
			728083213U,
			2804059920U,
			1985321116U,
			2432076528U,
			606264041U,
			2429717142U,
			2941117626U,
			2192980898U,
			3194517675U,
			3871410632U,
			923351622U,
			414233343U,
			2357068938U,
			2938633686U,
			1089260665U,
			636683022U,
			2730511326U,
			2291368237U,
			1403571172U,
			2277182047U,
			1721565266U,
			1097957276U,
			1840024884U,
			1824119308U,
			144742123U,
			186590144U,
			4089622090U,
			1916656255U,
			3662253324U,
			2669968601U,
			142676674U,
			1399078415U,
			1434971480U,
			3130819006U,
			3483700203U,
			1951357501U,
			2746062459U,
			2025243654U,
			756158365U,
			1165301714U,
			3016068919U,
			4284487989U,
			1791665127U,
			1282795658U,
			2058343279U,
			1609523491U,
			3024661287U,
			3240592558U,
			3484059685U,
			614052241U,
			2370262813U,
			2092316943U,
			3773868026U,
			496454102U,
			2734382223U,
			452401208U,
			3836117498U,
			951829104U,
			1275406626U,
			3890557298U,
			1223953544U,
			1934400983U,
			3784818318U,
			3658920339U,
			2293200825U,
			16162682U,
			1188311006U,
			123996868U,
			1546154918U,
			2538383021U,
			90246793U,
			3450054337U,
			4041928570U,
			1939073422U,
			1435974216U,
			3197907384U,
			2863180945U,
			3875379450U,
			2551944917U,
			568221918U,
			1553490952U,
			1389066600U,
			788579307U,
			2686801026U,
			1523633706U,
			113881948U,
			1019676198U,
			3470827625U,
			3451945568U,
			839027957U,
			2718108785U,
			1594294057U,
			1503247268U,
			2841452394U,
			1344983597U,
			676300574U,
			1150372952U,
			1052335235U,
			2541872941U,
			271609053U,
			931073932U,
			3330300566U,
			2885215325U,
			2448907336U,
			2584775324U,
			3860501833U,
			1922573971U,
			1129940152U,
			428412118U,
			215958186U,
			1972297562U,
			561468054U,
			2530384762U,
			2864972909U,
			3945103792U,
			3658178748U,
			1679078802U,
			2343206009U,
			3110570588U,
			1241548326U,
			3255112228U,
			1628309396U,
			318341333U,
			3355914486U,
			1134824380U,
			4114057187U,
			769878509U,
			3707555939U,
			3487797609U,
			715642882U,
			530614133U,
			4166400101U,
			583515157U,
			3570080284U,
			3823935770U,
			2150665186U,
			2949795317U,
			1444631904U,
			264495046U,
			3559149218U,
			4116285648U,
			2050902722U,
			4109361262U,
			2582517179U,
			2714970894U,
			2036709992U,
			28566230U,
			3508849359U,
			2498642972U,
			3542513477U,
			3927880314U,
			1834397870U,
			3401095875U,
			1388254095U,
			403449563U,
			1515416758U,
			408227419U,
			3581074325U,
			1723231453U,
			3645256719U,
			1227675513U,
			1732141556U,
			1555674703U,
			3981300200U,
			80045642U,
			1140633974U,
			1531300577U,
			134754646U,
			2816398408U,
			3599660006U,
			4205105339U,
			2720789755U,
			2806096705U,
			1981267242U,
			1948691474U,
			3268417576U,
			292017788U,
			1973084543U,
			3115273062U,
			192794697U,
			1833703737U,
			447445635U,
			1437147291U,
			3130358329U,
			1461868617U,
			3550739423U,
			2939223854U,
			515348778U,
			2273111793U,
			195961007U,
			1256372226U,
			5139715U,
			1680700453U,
			2785472988U,
			3271154969U,
			2380789448U,
			2653891072U,
			1847374663U,
			1193650851U,
			1949691886U,
			2086484886U,
			2179658768U,
			3519306791U,
			3851443433U,
			4235484471U,
			3734287412U,
			1632959172U,
			897507222U,
			2340273716U,
			577310148U,
			2072458865U,
			2553560439U,
			547127751U,
			2328734567U,
			2108586925U,
			452946644U,
			935549498U,
			4172269478U,
			1628602701U,
			750932618U,
			1244170033U,
			3890779796U,
			2187723067U,
			3214940366U,
			4010658209U,
			429964070U,
			2076396608U,
			3937128995U,
			2817431254U,
			38493284U,
			663088438U,
			448052184U,
			1424255514U,
			4182318281U,
			260963511U,
			2103627343U,
			3894176931U,
			1361002568U,
			2738079042U,
			3045701764U,
			2545205192U,
			3365930562U,
			1807331950U,
			1884362328U,
			999721496U,
			1376692636U,
			21260608U,
			3249047902U,
			3415955094U,
			347278258U,
			3518508538U,
			1786008327U,
			707677646U,
			1712991578U,
			823023842U,
			2271258804U,
			1238441234U,
			3706387059U,
			1418165717U,
			89466734U,
			2650771621U,
			2665800615U,
			1272863550U,
			4116711270U,
			352706468U,
			3421892248U,
			4293923895U,
			504881416U,
			3338615149U,
			160009615U,
			1442768654U,
			4241022645U,
			1791101113U,
			82954570U,
			2719538846U,
			2013764548U,
			3632344672U,
			3137590222U,
			324505917U,
			4109085255U,
			2256058410U,
			776117973U,
			3020937391U,
			4222650383U,
			2001447197U,
			3904543904U,
			2399702797U,
			4245430137U,
			1354173688U,
			1002538543U,
			441313920U,
			1510233835U,
			18726703U,
			3776142824U,
			1393508374U,
			4041315074U,
			201135895U,
			3248367123U,
			403542389U,
			292071760U,
			785382510U,
			177090413U,
			2448459140U,
			3951579459U,
			1488026041U,
			623000146U,
			294899439U,
			2331768666U,
			987188275U,
			382997149U,
			1318313166U,
			4083435912U,
			4267311479U,
			139496533U,
			1036620169U,
			1129296608U,
			2865200282U,
			3539573873U,
			1043806460U,
			1466295077U,
			292333445U,
			3344382448U,
			278394853U,
			878107082U,
			1289888733U,
			2405981539U,
			3676669828U,
			3962091366U,
			1755572447U,
			1183192617U,
			3028213688U,
			1898552231U,
			4285137022U,
			3056965948U,
			3595767812U,
			299698682U,
			2599259041U,
			393859050U,
			3820733836U,
			1234863478U,
			2227982607U,
			706277957U,
			3320147624U,
			3094232587U,
			3954676025U,
			3377507507U,
			2485349374U,
			2164620772U,
			52026801U,
			2207864555U,
			2938427728U,
			1457343542U,
			1943850677U,
			1674649925U,
			2634823261U,
			2640392440U,
			2751279037U,
			2979734187U,
			2369145894U,
			2703291579U,
			859628977U,
			2684474260U,
			2106465962U,
			963576362U,
			35415574U,
			582235533U,
			3762395563U,
			2281903688U,
			2163260123U,
			1218612173U,
			1609196462U,
			1060325410U,
			1234344043U,
			2131078490U,
			1216866840U,
			2193482108U,
			321031278U,
			2121103042U,
			1827824250U,
			4224758746U,
			999360815U,
			3713152368U,
			1054655716U,
			1870365599U,
			80340480U,
			1350786806U,
			1100301171U,
			3587375024U,
			3730878461U,
			742678905U,
			3879826448U,
			942666198U,
			2335745569U,
			1420268733U,
			4202024162U,
			3455958280U,
			1042569751U,
			2559503626U,
			2570001341U,
			59387806U,
			1865603107U,
			291033728U,
			3224691850U,
			3680469309U,
			126646691U,
			2196348373U,
			4014907872U,
			283998314U,
			1260218660U,
			3497923739U,
			2894182463U,
			2223220852U,
			1449652810U,
			3007394814U,
			288381831U,
			2315481018U,
			2671742157U,
			4277694440U,
			2644567873U,
			411800250U,
			4083167300U,
			1716700853U,
			59537378U,
			2656124495U,
			2758618671U,
			1705178517U,
			3039567500U,
			2863240341U,
			490235181U,
			2530754717U,
			1654775979U,
			3484406889U,
			2349368568U,
			349874589U,
			4157211401U,
			2086919302U,
			1925622463U,
			1497305224U,
			2237281916U,
			2303204646U,
			1110014868U,
			1688449114U,
			1424382026U,
			2386779646U,
			3288708439U,
			695402873U,
			1691577375U,
			2513978182U,
			1983886158U,
			291203279U,
			1582837694U,
			3355439206U,
			3450891655U,
			584132775U,
			1507015470U,
			2766862966U,
			1417630227U,
			1828510281U,
			1160179754U,
			3827752173U,
			2295571851U,
			3484763748U,
			3584048929U,
			2377667567U,
			1541821644U,
			4219535467U,
			3712337944U,
			3030767612U,
			1601031392U,
			58978612U,
			912450655U,
			2406999933U,
			2181626132U,
			1648384492U,
			2645834895U,
			762199513U,
			1754584576U,
			1175142252U,
			4293827239U,
			3654867589U,
			3894164902U,
			2687747938U,
			2816498102U,
			2548012596U,
			541650636U,
			2639230832U,
			3694690368U,
			1800898098U,
			1603024396U,
			1640148899U,
			4095783422U,
			2840366503U,
			3818268739U,
			3714335805U,
			3754076148U,
			4027775004U,
			1701353030U,
			1530245873U,
			3903373850U,
			1012415167U,
			4076600024U,
			2753683113U,
			4219419619U,
			4248422111U,
			2586624393U,
			1676142139U,
			1956387772U,
			1039488452U,
			4051210204U,
			293158886U,
			3520297564U,
			3065007138U,
			2096483244U,
			1740702548U,
			3128777286U,
			2101114143U,
			2841869133U,
			500769630U,
			119363040U,
			1943842610U,
			103982587U,
			3955593234U,
			1829646845U,
			1903697895U,
			588250650U,
			1467847752U,
			4025248196U,
			1584415506U,
			2899616882U,
			1199908428U,
			837256278U,
			1294298059U,
			32588319U,
			2956851744U,
			1325728416U,
			327875962U,
			3244589952U,
			2626619041U,
			1742109000U,
			4148275361U,
			3948402935U,
			69534779U,
			4158424428U,
			2172118703U,
			726334737U,
			755025240U,
			2498200851U,
			2703531245U,
			2271897271U,
			3411387912U,
			788406489U,
			290226647U,
			542091892U,
			3226570092U,
			2048408666U,
			3508100773U,
			2546200492U,
			4294349455U,
			3933356048U,
			4292679185U,
			3010057073U,
			1111518661U,
			902116843U,
			560054467U,
			984016073U,
			2409487326U,
			3451143843U,
			2063074180U,
			78463569U,
			3092050913U,
			679695597U,
			3525872554U,
			2623311207U,
			490898121U,
			51311315U,
			758855751U,
			1861455436U,
			1813373110U,
			3910787290U,
			3222157457U,
			1415288419U,
			1503698461U,
			1326079685U,
			2446291870U,
			2680955136U,
			706849938U,
			3252327841U,
			2631444883U,
			3044641477U,
			4110215455U,
			1926677725U,
			1777885992U,
			1894560063U,
			1295073848U,
			1783302081U,
			2959144624U,
			504334972U,
			1797334811U,
			3002023290U,
			1385220077U,
			2973271787U,
			2082163653U,
			816433439U,
			2564915010U,
			2287020406U,
			3284434323U,
			2185251793U,
			2203950173U,
			3007023767U,
			1390366894U,
			1405800937U,
			3657715608U,
			1997366507U,
			4082625574U,
			349786875U,
			2061097264U,
			1513261583U,
			4239643936U,
			675364329U,
			3238480632U,
			2578462741U,
			425505318U,
			4067392656U,
			1624159334U,
			4028632545U,
			4194440018U,
			1035916659U,
			2569448694U,
			915500680U,
			507456075U,
			993644745U,
			1602186340U,
			4069312231U,
			980312466U,
			3633661160U,
			595491652U,
			3583833006U,
			2212622757U,
			989153368U,
			2988947368U,
			1302731371U,
			2590004851U,
			2565880708U,
			1582463519U,
			3175860290U,
			897574883U,
			470073513U,
			2519290665U,
			4100036759U,
			2602064710U,
			430129027U,
			1056989792U,
			2779867227U,
			3797273812U,
			3228002272U,
			1947094748U,
			3241903127U,
			511315161U,
			4010814891U,
			781707695U,
			3588309349U,
			3784468122U,
			4167162922U,
			1804422664U,
			1041692531U,
			602842385U,
			544701262U,
			1231500915U,
			1915409643U,
			1440592093U,
			768088865U,
			2733026442U,
			2340046522U,
			3260094909U,
			2225934130U,
			1574011084U,
			2905218302U,
			3717201171U,
			745406539U,
			3721510007U,
			3721627871U,
			3971558400U,
			592074360U,
			2503419257U,
			1008342416U,
			3996729815U,
			3375621729U,
			1634451396U,
			4046825398U,
			3632067597U,
			4170160373U,
			289720338U,
			2653401368U,
			588619522U,
			2535376758U,
			2547330071U,
			1899989588U,
			1959943602U,
			865875718U,
			2019401923U,
			426166823U,
			3409177780U,
			4129287676U,
			527439573U,
			4129574468U,
			3327563963U,
			57556948U,
			668407593U,
			3018082425U,
			651375728U,
			1501653155U,
			1901751871U,
			1374923411U,
			2697520988U,
			2402846608U,
			2536699211U,
			2923654366U,
			3184605328U,
			597219983U,
			3654315092U,
			1496452178U,
			393104762U,
			623709925U,
			170294795U,
			1578860378U,
			3776997042U,
			645032707U,
			2327626906U,
			640759619U,
			3558619563U,
			773462587U,
			2213988057U,
			2130333786U,
			2747854292U,
			4240800856U,
			374607608U,
			1697031824U,
			3120037343U,
			1514351029U,
			2486997342U,
			2868951121U,
			439858928U,
			2610334830U,
			295173306U,
			2135353508U,
			62785162U,
			1893126843U,
			1809231802U,
			3120281694U,
			3279547599U,
			1889812613U,
			1147354022U,
			4106462344U,
			2610583600U,
			392635887U,
			1489838869U,
			907615112U,
			3310452074U,
			2712153952U,
			3682708178U,
			3237421296U,
			1081742089U,
			3854227354U,
			2627495967U,
			330271785U,
			1076802616U,
			2799882318U,
			3936456060U,
			3848819546U,
			4039941829U,
			1001873395U,
			1619227089U,
			759710962U,
			1700003613U,
			1159158118U,
			244318697U,
			3237237399U,
			3350304325U,
			245722440U,
			2315176183U,
			1217161283U,
			1354441472U,
			509885032U,
			2507674939U,
			2667610517U,
			987623806U,
			987587383U,
			2000698829U,
			2124279780U,
			1822666339U,
			3363246622U,
			2166398835U,
			3228443837U,
			686532844U,
			3283134350U,
			1013740055U,
			990857687U,
			2464512625U,
			1303855704U,
			909191102U,
			3123439366U,
			876706199U,
			2143157290U,
			1794239505U,
			3048223027U,
			3701434458U,
			3911789337U,
			1149315949U,
			2518376883U,
			3019107966U,
			2850475535U,
			411223808U,
			3629558475U,
			2043364293U,
			3164620993U,
			221030583U,
			2913683273U,
			2497175995U,
			3897845341U,
			2226070878U,
			208087240U,
			770493660U,
			1654452970U,
			4232066866U,
			2300785522U,
			121707826U,
			3073998448U,
			2085077069U,
			1768435555U,
			2258078982U,
			1530028069U,
			767801901U,
			3700407587U,
			1124276049U,
			1111562793U,
			2503813515U,
			1916084672U,
			4107874905U,
			2220969873U,
			623992258U,
			858217877U,
			2252340262U,
			3835713782U,
			1707124403U,
			2572514773U,
			4172974211U,
			3424551539U,
			1251708138U,
			3360639179U,
			2335094733U,
			1855266707U,
			2449582109U,
			2287187347U,
			2279293190U,
			1710908008U,
			82070700U,
			3197282450U,
			1315948489U,
			117639881U,
			644978638U,
			3212053145U,
			262286123U,
			4180048790U,
			2136472694U,
			3321143383U,
			1681508330U,
			3936008805U,
			860289674U,
			2672065522U,
			838715771U,
			590033225U,
			708401949U,
			4206406988U,
			664856268U,
			420434226U,
			1879854878U,
			1975799605U,
			2492810350U,
			2696098610U,
			3169849207U,
			4271801801U,
			3345612038U,
			423587759U,
			3369402346U,
			943998602U,
			3581166981U,
			335420141U,
			2971786409U,
			2653672345U,
			1442751982U,
			1531725207U,
			4211625759U,
			1267885126U,
			1023266514U,
			1166441320U,
			2540964371U,
			3489158206U,
			103075741U,
			2565212816U,
			3620863964U,
			1036214123U,
			2724164724U,
			3673693874U,
			640325428U,
			3351821145U,
			1399069779U,
			1875813281U,
			384153825U,
			892348196U,
			169724166U,
			2412344652U,
			3520970078U,
			2904692638U,
			4097229847U,
			678985259U,
			1232546308U,
			1839992998U,
			1044232799U,
			3870914802U,
			1969810341U,
			2475466248U,
			194155995U,
			961722209U,
			3841590664U,
			2805005000U,
			1613592495U,
			3765630586U,
			2765889718U,
			3010735713U,
			2322272180U,
			2395959807U,
			4040765797U,
			2897376119U,
			2472025638U,
			2007986779U,
			1402676962U,
			1913727292U,
			790260720U,
			933706644U,
			1388907118U,
			507546379U,
			3574753691U,
			3437387422U,
			2092925156U,
			4121431344U,
			2907178783U,
			815895341U,
			921946329U,
			3740431249U,
			1409532124U,
			2611997400U,
			4124070908U,
			3050651155U,
			702905146U,
			809210060U,
			1296227147U,
			3906829158U,
			1445472056U,
			2517972906U,
			3998664089U,
			1649568124U,
			1515535448U,
			1061024229U,
			7730996U,
			1747009077U,
			2031848384U,
			1005008927U,
			129530663U,
			2432049855U,
			88132614U,
			4111106458U,
			1254837498U,
			3720981800U,
			2956547540U,
			1118451095U,
			1546546321U,
			3784052643U,
			2818894679U,
			3745260931U,
			295228389U,
			3468155022U,
			3976824784U,
			3173439953U,
			1135198475U,
			2503647339U,
			4090523295U,
			3247347550U,
			2694931310U,
			685439909U,
			2563926884U,
			3704667137U,
			1710413269U,
			2933388927U,
			104334627U,
			1229584153U,
			3071839369U,
			1097358012U,
			2194941337U,
			949750746U,
			2286574040U,
			1779303697U,
			4051509909U,
			1979855138U,
			2773648935U,
			1658368419U,
			3629168336U,
			74739511U,
			4085949171U,
			4172220537U,
			4412992U,
			252597296U,
			564094670U,
			4234651480U,
			873001961U,
			4031229501U,
			373327739U,
			3202314072U,
			3965387055U,
			1975858625U,
			251768188U,
			3807929670U,
			3700899629U,
			2462066763U,
			1942131320U,
			2113733824U,
			1438991422U,
			444635389U,
			805318190U,
			3069038736U,
			769659402U,
			1031995520U,
			489847111U,
			3481748021U,
			1810816664U,
			3565824009U,
			2480497291U,
			968342443U,
			389054419U,
			4004350575U,
			2706490079U,
			2181685861U,
			4183368557U,
			2746951567U,
			2052028412U,
			191979446U,
			3493122212U,
			3611765269U,
			1538549607U,
			1842674862U,
			1554883678U,
			3362558640U,
			1337079732U,
			2813663904U,
			1553727276U,
			301662430U,
			225254738U,
			217906561U,
			187830285U,
			1404909652U,
			2593754529U,
			4155854827U,
			2426513125U,
			1331641038U,
			4292952238U,
			1461103247U,
			3082817401U,
			3579290795U,
			4286120193U,
			51014217U,
			1285103570U,
			478057942U,
			1520401695U,
			3314397248U,
			3688460666U,
			2804155322U,
			3830864725U,
			3662087098U,
			170889831U,
			1918265906U,
			2584566849U,
			3817341222U,
			2055500159U,
			2828354587U,
			71711365U,
			3698801799U,
			2588345916U,
			2067898280U,
			2489527459U,
			1472529342U,
			652267722U,
			859954884U,
			1242127836U,
			927787544U,
			1551230472U,
			168041626U,
			970250146U,
			2152071648U,
			2513336563U,
			3774539922U,
			3028714389U,
			2930493924U,
			287253512U,
			2459899917U,
			2967244120U,
			3400488427U,
			2136288298U,
			773391759U,
			1186742852U,
			984546899U,
			1176807138U,
			326741494U,
			2926804555U,
			4167637482U,
			2619392369U,
			2121092241U,
			3705986858U,
			4052246450U,
			859409060U,
			422975646U,
			766390245U,
			3604150023U,
			4049556662U,
			2298739406U,
			3114264329U,
			154883859U,
			3365559255U,
			3223654797U,
			3477924573U,
			198459990U,
			4030919861U,
			961155619U,
			296136322U,
			3635179958U,
			55614121U,
			3396473825U,
			3659829664U,
			2318564161U,
			3250294502U,
			1958022763U,
			703739092U,
			852730100U,
			3830637627U,
			2303866360U,
			589696773U,
			2049325668U,
			2790071347U,
			1360789665U,
			3133084699U,
			1089264174U,
			3967074208U,
			2281067588U,
			2083669336U,
			2226145982U,
			1175736561U,
			1556106037U,
			3216325396U,
			732120003U,
			3018000914U,
			598348450U,
			3557209783U,
			4175979692U,
			847611521U,
			3941044913U,
			2114937967U,
			585695827U,
			2620648902U,
			1110529766U,
			1667042583U,
			1829067446U,
			3501723751U,
			3813523623U,
			1039623255U,
			1520927264U,
			2208179062U,
			1782145742U,
			2289947761U,
			136593995U,
			3553954176U,
			2778474376U,
			4114202825U,
			2922026390U,
			2338703588U,
			3275881998U,
			3444758414U,
			2498634957U,
			1696415221U,
			2873885214U,
			166322813U,
			2855655293U,
			931005315U,
			3407946232U,
			692897296U,
			3751180548U,
			3047726247U,
			1910214408U,
			1492825485U,
			2077334820U,
			1798838891U,
			1112251534U,
			2022306575U,
			4220820203U,
			2721285972U,
			1313955041U,
			2286930673U,
			2637686763U,
			1911163198U,
			3091407072U,
			25128247U,
			824875347U,
			3136390883U,
			2997993140U,
			3239026320U,
			780909009U,
			2836858292U,
			2978100901U,
			996376723U,
			770474625U,
			2906053851U,
			368442294U,
			1659454364U,
			3358017025U,
			4227289089U,
			649136322U,
			1261579141U,
			2401762355U,
			1505566987U,
			3320957438U,
			555460694U,
			842384699U,
			65629727U,
			2290657797U,
			3615077279U,
			3582210855U,
			1010135216U,
			2072180753U,
			1078866761U,
			4178827542U,
			3135054066U,
			1697064635U,
			4085600596U,
			1981346440U,
			3494976274U,
			615608878U,
			4255180952U,
			1270842514U,
			1547429740U,
			3535027518U,
			3580811236U,
			2446965860U,
			3226676136U,
			2252922911U,
			1361507644U,
			2591150218U,
			103981284U,
			1316773524U,
			1857775379U,
			774024968U,
			199566612U,
			1593734644U,
			735830684U,
			2449382535U,
			89462368U,
			12673670U,
			75774969U,
			2646373639U,
			3635197949U,
			1804115655U,
			639505208U,
			616327983U,
			436423703U,
			3641656663U,
			1135522281U,
			1734883143U,
			1518734647U,
			2013206461U,
			28051960U,
			2081963968U,
			3516044980U,
			2614491953U,
			3922482500U,
			1058691363U,
			3097948678U,
			619044415U,
			3129371401U,
			3016255186U,
			757066463U,
			2881998512U,
			1276188993U,
			454571968U,
			2347227919U,
			3809146822U,
			1737937492U,
			1682911088U,
			1964138272U,
			1085510387U,
			1849445831U,
			1224434279U,
			3558973185U,
			178898039U,
			2272735431U,
			333218702U,
			2082803777U,
			3992194382U,
			209102068U,
			1338535932U,
			915479452U,
			3240632721U,
			331147189U,
			3955454968U,
			529690610U,
			121707268U,
			2587025133U,
			2956838990U,
			15353116U,
			102355849U,
			2732282917U,
			2468065130U,
			3854178049U,
			3659675647U,
			4135873041U,
			1860981084U,
			1019854778U,
			2887789382U,
			303006683U,
			1521011263U,
			274024474U,
			2247758264U,
			1803938641U,
			2063035473U,
			378612284U,
			2280322371U,
			2077919890U,
			1378257609U,
			3776338154U,
			4087652159U,
			2833559185U,
			967729599U,
			3903948947U,
			1004735221U,
			2002490247U,
			2664077788U,
			4249333102U,
			240017923U,
			3085394872U,
			1538128129U,
			2746336739U,
			886426829U,
			2284943163U,
			3803168769U,
			314656689U,
			214928932U,
			3463928219U,
			2172274851U,
			3226458255U,
			3063729206U,
			489690138U,
			1123560601U,
			4249087013U,
			2709080162U,
			943742868U,
			4010057729U,
			1130885185U,
			2225939787U,
			1032957531U,
			2776268491U,
			2146014339U,
			2952432398U,
			2618808690U,
			1861857169U,
			3827287885U,
			1167870918U,
			3733492176U,
			1018573779U,
			1664924546U,
			2566880156U,
			4186726103U,
			1909615806U,
			2532628312U,
			2204487060U,
			1483954696U,
			3018465805U,
			3817643691U,
			3632866123U,
			4161987159U,
			2915748179U,
			3898026160U,
			924915942U,
			378887240U,
			4149528995U,
			3238868744U,
			2947371351U,
			1709098599U,
			1409345630U,
			3720596288U,
			2448920688U,
			1017606833U,
			1604273320U,
			3069987276U,
			1766728036U,
			3969399031U,
			766094811U,
			4228845981U,
			2298870425U,
			1270811282U,
			532566995U,
			4027373662U,
			4029722092U,
			2855122191U,
			2926106031U,
			916443687U,
			590752090U,
			2905733364U,
			4165312910U,
			108980791U,
			3564163465U,
			3312078627U,
			1975398981U,
			2039844225U,
			3919822281U,
			1156783757U,
			1387116432U,
			2447013280U,
			2857516006U,
			4126817369U,
			3952198953U,
			1587314792U,
			2538181767U,
			3926481358U,
			2166251217U,
			4109527036U,
			3968343306U,
			1533824210U,
			1591595659U,
			3224740981U,
			27237840U,
			2150957666U,
			1681539929U,
			221759651U,
			1609443895U,
			336343182U,
			4227518665U,
			1153231702U,
			2233025094U,
			2044442295U,
			3967862921U,
			3272004822U,
			1818097896U,
			2648763424U,
			3129426082U,
			2122615208U,
			1743333753U,
			2095907398U,
			1225222855U,
			1098706972U,
			3824070873U,
			2930988631U,
			1539354912U,
			1736806912U,
			4162607752U,
			4215370749U,
			2032594321U,
			4250016071U,
			3188643853U,
			1791422483U,
			2579136482U,
			1863200800U,
			3729714923U,
			800400597U,
			1880667368U,
			2841528195U,
			3115792424U,
			3698385166U,
			89110565U,
			1471971824U,
			1059037706U,
			969603041U,
			1242103046U,
			11158614U,
			2184255394U,
			2615626758U,
			2967264117U,
			639177427U,
			1317095716U,
			2580521423U,
			3089468290U,
			1884632189U,
			1581506032U,
			2959377670U,
			1722914788U,
			2515921502U,
			3192374538U,
			4156387272U,
			1553571437U,
			703801436U,
			3713768467U,
			154359784U,
			3999972458U,
			3817113993U,
			2803967031U,
			2476049777U,
			2977601703U,
			1815061071U,
			3770367157U,
			3331417179U,
			1978770863U,
			892947764U,
			4260825676U,
			3920245305U,
			674270416U,
			2202643589U,
			275567138U,
			213516354U,
			2628150758U,
			1323576310U,
			1985196847U,
			2083222920U,
			1140239239U,
			3511381946U,
			2500360013U,
			1311144511U,
			3137168759U,
			2980387156U,
			434144068U,
			2396136161U,
			3617638204U,
			3709011328U,
			1678240266U,
			1610178389U,
			1886321513U,
			1145183321U,
			3400877648U,
			1886637376U,
			2624007922U,
			762960122U,
			770750230U,
			3702835097U,
			3898721818U,
			1599893748U,
			2654860949U,
			4196322050U,
			2574627507U,
			3150568921U,
			2759604213U,
			846306115U,
			1346307059U,
			1586827710U,
			1303082632U,
			696940876U,
			3838400301U,
			4193013842U,
			2999646484U,
			4069461128U,
			2945413643U,
			2441470343U,
			338673062U,
			1243909674U,
			3456445460U,
			1215240376U,
			2964807831U,
			3572651144U,
			3962976311U,
			25808865U,
			1994286883U,
			1827691424U,
			4165783658U,
			4268623379U,
			3791202664U,
			2348298119U,
			2668433041U,
			3977574554U,
			4219789101U,
			1058006918U,
			1517507399U,
			2191703626U,
			2378405189U,
			1807813542U,
			2222758401U,
			3387585591U,
			1002281054U,
			3454377827U,
			2806684505U,
			1638188043U,
			3288094605U,
			2271501671U,
			3606915113U,
			4274331861U,
			3183083599U,
			3152756592U,
			2717144573U,
			3654069669U,
			2974284150U,
			3531361143U,
			2341685484U,
			4230029516U,
			3936896604U,
			1924300632U,
			713602783U,
			3787821248U,
			3998161192U,
			1041762160U,
			1613964131U,
			1254915409U,
			1918842774U,
			349425566U,
			1832627632U,
			2997525152U,
			2304747276U,
			3597874184U,
			2760758681U,
			768571630U,
			3782994599U,
			2179445930U,
			3371989295U,
			2451148598U,
			1779226846U,
			3567935235U,
			3635216244U,
			1041776027U,
			4010899790U,
			2545581629U,
			1253643653U,
			528787439U,
			3866461643U,
			1614472059U,
			3775239448U,
			2605931878U,
			2077264160U,
			2191672300U,
			915573076U,
			4132386701U,
			297271703U,
			484389187U,
			2254162140U,
			2139169466U,
			612523548U,
			1668403107U,
			1113594722U,
			1022402900U,
			1789138581U,
			2224953577U,
			3942769089U,
			2590655713U,
			773142131U,
			1497469228U,
			2513951492U,
			854423496U,
			3719810183U,
			4083620030U,
			2060416018U,
			249631007U,
			972836637U,
			3099536028U,
			2294549826U,
			1245278080U,
			2185770567U,
			901327032U,
			3166542905U,
			1687546491U,
			193041485U,
			2853366113U,
			2503017377U,
			42498964U,
			4268673546U,
			2682982904U,
			422485259U,
			1495454462U,
			3406313117U,
			1749857100U,
			1799865586U,
			3860832519U,
			312840999U,
			3778498096U,
			2427242508U,
			3204169032U,
			2770846360U,
			2670127569U,
			1079304032U,
			333590432U,
			3473357836U,
			3296605976U,
			1705907823U,
			277795424U,
			4161442859U,
			4012040075U,
			1458501646U,
			3317818072U,
			1035207392U,
			1795980848U,
			3873545050U,
			894442624U,
			2772206658U,
			2743583365U,
			96004353U,
			3202301790U,
			3763889012U,
			2898696494U,
			3227731891U,
			1068081166U,
			2775253693U,
			2764955145U,
			1221188699U,
			103942211U,
			1663880098U,
			3139977138U,
			3852976450U,
			360273582U,
			107443237U,
			2689754935U,
			1690502553U,
			3351940294U,
			1349801380U,
			4083840684U,
			273440140U,
			220201937U,
			1267886045U,
			825986315U,
			517035606U,
			1870663056U,
			18849353U,
			3001229021U,
			2549934419U,
			3967900946U,
			3840805736U,
			2763058049U,
			3960728890U,
			988085508U,
			2782410388U,
			2127029450U,
			4125631470U,
			1282275584U,
			47360759U,
			3381698078U,
			1404794799U,
			2081686049U,
			3251328426U,
			3542471965U,
			3221946531U,
			292438300U,
			3768379664U,
			1085350699U,
			492546986U,
			2512974920U,
			2403282413U,
			1986405766U,
			2933777956U,
			668372957U,
			1391332155U,
			81906187U,
			71330392U,
			4221476221U,
			5086583U,
			2593629997U,
			756705787U,
			2243692318U,
			3678457881U,
			2763687705U,
			2833295324U,
			3162948974U,
			1423862023U,
			1977647106U,
			4221352394U,
			3419096774U,
			4239017611U,
			2366872786U,
			2113251558U,
			3406579724U,
			3544226538U,
			1189978953U,
			122899434U,
			1468230983U,
			3703344920U,
			2102923659U,
			965160559U,
			1628271601U,
			2143826089U,
			3389881378U,
			443892643U,
			4033982434U,
			2652792343U,
			3810798174U,
			2382952839U,
			2996608089U,
			3734448204U,
			4126654447U,
			3497804692U,
			1627855431U,
			2604825725U,
			693875777U,
			2998274443U,
			2018101444U,
			2238021126U,
			744221732U,
			3894240902U,
			1854257757U,
			347705916U,
			1360799021U,
			850197073U,
			2692628568U,
			4173657864U,
			1963673772U,
			1252206629U,
			426230378U,
			69157411U,
			2371601241U,
			4197806027U,
			3693353475U,
			2745217450U,
			1064255716U,
			1440010898U,
			449086543U,
			4015787703U,
			522582198U,
			4164433548U,
			1627747390U,
			827981237U,
			1731380434U,
			3262073057U,
			2268770982U,
			4021831543U,
			777056652U,
			3944303564U,
			1704387043U,
			1244548368U,
			545639674U,
			3202911664U,
			3162899920U,
			68540332U,
			351436102U,
			691604686U,
			2284025255U,
			2140690607U,
			1165647911U,
			3448629668U,
			63332788U,
			3707500440U,
			3866165233U,
			1401620149U,
			3640853019U,
			861431742U,
			1687191962U,
			3099979176U,
			3531714880U,
			1627245328U,
			3589786282U,
			3426820210U,
			2498784595U,
			3418265683U,
			1899668405U,
			413211017U,
			2682350142U,
			2127924522U,
			2912985806U,
			4089762989U,
			3252301140U,
			2037899916U,
			591950110U,
			3206818342U,
			360065937U,
			3597663420U,
			819644541U,
			3617047062U,
			179010380U,
			2941963535U,
			4052724318U,
			2116524873U,
			4093219400U,
			1449776728U,
			15189603U,
			388770788U,
			2283663166U,
			2975237441U,
			4251994538U,
			1879261649U,
			1005565233U,
			1262500150U,
			1189324773U,
			4108664575U,
			2788505039U,
			1119865882U,
			3759747200U,
			2010895419U,
			939294517U,
			591571043U,
			4023271194U,
			1014308796U,
			2563517152U,
			2975044151U,
			4209826281U,
			1901250148U,
			750094943U,
			3718816385U,
			2483662156U,
			2704231607U,
			526747017U,
			3361926488U,
			3125669408U,
			2531323156U,
			2393936328U,
			3057914422U,
			2966364485U,
			2699598872U,
			4082058420U,
			1281177476U,
			3737724403U,
			401522990U,
			1168645137U,
			1200094550U,
			3264595901U,
			4156029036U,
			3080144265U,
			1075649194U,
			2358834587U,
			180220952U,
			2792087864U,
			4167013175U,
			3104995624U,
			3029823651U,
			3687545775U,
			3614610063U,
			4113580135U,
			528712879U,
			3135039338U,
			1184386463U,
			2595304469U,
			3418580254U,
			782898601U,
			165292366U,
			3274393819U,
			1711897164U,
			3886496503U,
			3028426289U,
			643344820U,
			2971590125U,
			719978756U,
			1861909200U,
			1952339104U,
			3773994020U,
			1692158379U,
			3737915975U,
			1457250758U,
			2061755742U,
			1147498825U,
			1555817139U,
			2676910237U,
			383662632U,
			2295454251U,
			1147517494U,
			1382875168U,
			3348757356U,
			1799816830U,
			218908529U,
			2627619480U,
			394770489U,
			3775465765U,
			1871079396U,
			4133081807U,
			3300846330U,
			4097938710U,
			115864643U,
			2679464536U,
			241411121U,
			2584256456U,
			1094734802U,
			467686387U,
			4201248109U,
			2569438739U,
			3787555444U,
			1876582302U,
			3001880220U,
			1195303215U,
			2343939915U,
			631971410U,
			3433666360U,
			3326545931U,
			2770352397U,
			1600476188U,
			920832824U,
			2609943325U,
			2471487972U,
			759202361U,
			2499379819U,
			439273701U,
			2066325994U,
			1466078922U,
			1552910205U,
			3373474690U,
			327987089U,
			2798123813U,
			3199510849U,
			192547036U,
			2032441411U,
			517497264U,
			1160124236U,
			642223080U,
			438766319U,
			1665117470U,
			783220123U,
			753030549U,
			834981768U,
			837003709U,
			3800937887U,
			2023926682U,
			3901198885U,
			1550754712U,
			890724500U,
			2294044365U,
			2930250767U,
			1394615581U,
			1454525902U,
			1596909865U,
			4096736591U,
			929435151U,
			298572482U,
			3715310396U,
			3941529961U,
			2076529199U,
			2006027786U,
			1544978573U,
			4054578408U,
			2572985367U,
			3953252292U,
			3155721459U,
			516006514U,
			2116797969U,
			3772593460U,
			1862272646U,
			539073027U,
			1381917355U,
			3366006143U,
			3353344679U,
			1408801865U,
			107130259U,
			3931212905U,
			2887705723U,
			2322814216U,
			688203986U,
			862167926U,
			848752216U,
			2608330322U,
			3674535676U,
			2787405872U,
			4261650894U,
			3151877393U,
			2205213167U,
			2914957056U,
			1233162087U,
			1320328601U,
			2336069338U,
			2839146982U,
			1979904177U,
			3070208049U,
			4117210763U,
			1997874943U,
			2532210661U,
			2554228365U,
			752128338U,
			45455937U,
			1644466243U,
			1072852259U,
			1792026133U,
			2377990600U,
			2043801785U,
			1905214719U,
			235574703U,
			1414805357U,
			885565646U,
			2724711687U,
			1045432331U,
			3354798042U,
			461015610U,
			624727010U,
			2542931917U,
			901594828U,
			3412181060U,
			3400470155U,
			1287396171U,
			2848782783U,
			2961553670U,
			2126344371U,
			2570918194U,
			2010885953U,
			1923153433U,
			269462170U,
			1544242682U,
			2702516404U,
			965477627U,
			3938018588U,
			2651353918U,
			3453796530U,
			347308160U,
			2699420342U,
			3699249990U,
			1912404359U,
			1701606431U,
			3882889567U,
			3393372770U,
			68527647U,
			3165142794U,
			2451202934U,
			2720580924U,
			1135170005U,
			593685854U,
			3764015083U,
			2826258248U,
			1144121457U,
			2175590493U,
			1116388972U,
			2741694737U,
			4280163493U,
			4042250150U,
			4281993716U,
			477792566U,
			3866341960U,
			4026868660U,
			3879609659U,
			847992985U,
			3179458162U,
			2923072392U,
			1361658836U,
			132494027U,
			3673998313U,
			2937991058U,
			1662897028U,
			1595091167U,
			372096275U,
			1940824029U,
			4006704665U,
			1668756290U,
			1537129471U,
			3489741468U,
			1644398023U,
			617382180U,
			66190130U,
			1324992248U,
			3663904365U,
			2073619567U,
			3935938800U,
			3233637762U,
			853756124U,
			611695401U,
			34175537U,
			1167972528U,
			1635160914U,
			2613152680U,
			4007364314U,
			325468710U,
			1605667141U,
			1406632185U,
			4261764901U,
			138460483U,
			3080241352U,
			1417634051U,
			1489283185U,
			3568507402U,
			830980562U,
			751945314U,
			3104827835U,
			3275857454U,
			71024301U,
			1279037598U,
			790432070U,
			1052541091U,
			2003899881U,
			2877350075U,
			3995006454U,
			1983644300U,
			2654367465U,
			4192682623U,
			2975370310U,
			2165177178U,
			129764730U,
			2213553922U,
			1949299668U,
			1614061333U,
			897124442U,
			3173814226U,
			2018675260U,
			1094295713U,
			3771548083U,
			2216380011U,
			3392092625U,
			4242242216U,
			2499481463U,
			1807175826U,
			195301671U,
			2896141263U,
			2101307809U,
			1232695498U,
			3529254086U,
			3442491316U,
			3050134503U,
			620744095U,
			1546992166U,
			3353130155U,
			3771859424U,
			802124753U,
			531293876U,
			212740018U,
			1022718272U,
			3408892889U,
			2524097530U,
			2948787239U,
			3312047148U,
			1797655404U,
			3800818744U,
			2890122394U,
			2056214196U,
			2859723851U,
			2563172004U,
			1932632744U,
			1063343472U,
			1111671778U,
			333151480U,
			1734402684U,
			54734483U,
			370875390U,
			2306635178U,
			1983844074U,
			1693224155U,
			3273845974U,
			2066377964U,
			767226862U,
			1107982017U,
			3186729148U,
			850156458U,
			2604754224U,
			2495776218U,
			1968564552U,
			2622334726U,
			3548799399U,
			1155693129U,
			2117522894U,
			3752834114U,
			3727663780U,
			2882616862U,
			3803962456U,
			680998097U,
			1961481561U,
			4197340659U,
			2138792029U,
			346826858U,
			2955192725U,
			191993095U,
			715113269U,
			2581110736U,
			2489569146U,
			1368103747U,
			3061514447U,
			3575408822U,
			2916526001U,
			3887562146U,
			3837657588U,
			775509522U,
			1836979168U,
			3103109718U,
			935420596U,
			501527540U,
			2876407308U,
			15918396U,
			1067456549U,
			350084772U,
			3857285950U,
			479790620U,
			4219816805U,
			1275074838U,
			3937165176U,
			2005382715U,
			2859322348U,
			456337979U,
			1013445773U,
			2418061242U,
			2456813583U,
			2255582245U,
			314735172U,
			2515515242U,
			2475625338U,
			604817058U,
			1221466108U,
			3542536726U,
			2384135871U,
			1389776132U,
			1338976426U,
			1643700406U,
			487125296U,
			3210126630U,
			1782388276U,
			3592408278U,
			2541204430U,
			570270433U,
			1896184759U,
			1062532196U,
			1456010825U,
			2941287047U,
			412895396U,
			2468082622U,
			184088500U,
			1237232244U,
			3573844860U,
			3506808034U,
			1081224160U,
			549615120U,
			2204252828U,
			3352649293U,
			2799043844U,
			3613920725U,
			3403938757U,
			536673289U,
			1230122738U,
			1804237399U,
			1685009106U,
			17677807U,
			3125789191U,
			3178351161U,
			79198930U,
			104342400U,
			2593853308U,
			162322171U,
			152309023U,
			1285435826U,
			3671930595U,
			1565786497U,
			2061778758U,
			1876871646U,
			1965712823U,
			2798462041U,
			1134807537U,
			575446279U,
			2474572569U,
			1469506487U,
			91478585U,
			442401174U,
			1027136331U,
			2501977040U,
			2515762308U,
			3160165630U,
			579784010U,
			2523858086U,
			943399116U,
			4036914402U,
			3889161346U,
			370826886U,
			2784791068U,
			1248522728U,
			4011727662U,
			2681910453U,
			3264010627U,
			790041558U,
			370378898U,
			136437057U,
			3174683078U,
			197922940U,
			2454407518U,
			2463393543U,
			3361275846U,
			294951827U,
			3137750201U,
			2397239890U,
			2557422769U,
			3161361504U,
			3683372137U,
			2164796196U,
			156099183U,
			926023127U,
			2873400326U,
			1832023176U,
			2341003280U,
			201504734U,
			3500214004U,
			453616051U,
			3814779111U,
			2276682872U,
			270437762U,
			1012090467U,
			3306243736U,
			926549833U,
			634170628U,
			4230633925U,
			2621398942U,
			1276639805U,
			2915915591U,
			824627265U,
			18980648U,
			1268439988U,
			1955653010U,
			994044220U,
			2581427081U,
			3383386477U,
			322282528U,
			3768566785U,
			1660048950U,
			3074043039U,
			538691705U,
			2763420994U,
			3216425018U,
			1165213117U,
			45246887U,
			625210745U,
			310676030U,
			1199628134U,
			99764288U,
			2992537472U,
			165503653U,
			2673366371U,
			3889365676U,
			3363879869U,
			1518653592U,
			2972543942U,
			2005255258U,
			282927952U,
			3638048049U,
			3420469504U,
			470743577U,
			1453729089U,
			2058773184U,
			2702758743U,
			1653888206U,
			2023605506U,
			3778228057U,
			595969029U,
			2922005250U,
			3493109097U,
			4183074436U,
			3852933816U,
			1310007714U,
			3003767146U,
			235960659U,
			232288640U,
			779080290U,
			665363331U,
			3333268199U,
			3377899634U,
			3768209149U,
			2236290359U,
			3850874126U,
			2242084557U,
			3235932444U,
			1850138057U,
			1711656189U,
			152982113U,
			1193951076U,
			4259931710U,
			565419914U,
			3967909771U,
			2614093014U,
			3500235831U,
			902840068U,
			3530917895U,
			17490573U,
			876998480U,
			704847611U,
			2716798545U,
			3458030067U,
			3535154872U,
			3874128479U,
			479797835U,
			3383328052U,
			2433059939U,
			2165772837U,
			1713177927U,
			1848669681U,
			4155871515U,
			1953384170U,
			1847330930U,
			1431004236U,
			829774251U,
			2338517916U,
			32088448U,
			250999254U,
			2683130045U,
			1036103973U,
			1073115048U,
			1509223473U,
			2518176412U,
			221048087U,
			4087241998U,
			2972840117U,
			2373072433U,
			3892917621U,
			1283040179U,
			2366063559U,
			1107923410U,
			2389443110U,
			451450992U,
			1753497776U,
			2888414451U,
			3294571171U,
			1270744375U,
			3203372400U,
			871235616U,
			3763445016U,
			19039039U,
			2034221729U,
			1082902512U,
			953699630U,
			2700368390U,
			1110147443U,
			1812519295U,
			1301159790U,
			3956924128U,
			1755665662U,
			2947220322U,
			3656516205U,
			1946604068U,
			3101209268U,
			84987772U,
			2972800413U,
			111053559U,
			4151750054U,
			3588547648U,
			2198496033U,
			2536332570U,
			509227594U,
			1992828148U,
			2995086625U,
			3391598726U,
			2698759770U,
			3984587317U,
			3408251021U,
			1012827920U,
			933272987U,
			355189081U,
			1964651969U,
			3994620687U,
			2291563868U,
			1329648779U,
			1684019229U,
			360469707U,
			3804869296U,
			1170139557U,
			1852931145U,
			1207837475U,
			385637084U,
			771082618U,
			2965831021U,
			510303692U,
			794612984U,
			1662145421U,
			4208232060U,
			173355099U,
			479106036U,
			755877031U,
			4167678686U,
			3193437334U,
			4254618575U,
			3004803645U,
			2521705338U,
			2592402552U,
			3921628226U,
			289927227U,
			476854476U,
			2144632172U,
			323231699U,
			3973952295U,
			2325061010U,
			4179832534U,
			2214002939U,
			324731552U,
			3877773770U,
			794884200U,
			3144823192U,
			868179058U,
			2534041270U,
			86723789U,
			3360857940U,
			1645426752U,
			969343423U,
			1512634279U,
			3148430634U,
			3118350598U,
			3509995542U,
			2182149127U,
			854373815U,
			2522813499U,
			4178650662U,
			3327062434U,
			172715297U,
			2341588312U,
			11747122U,
			2648370655U,
			1912932811U,
			1654640678U,
			2017992213U,
			3610603164U,
			407223564U,
			694478413U,
			1921584450U,
			996226725U,
			1711864510U,
			2683336816U,
			2543505892U,
			564048416U,
			164998494U,
			4249346138U,
			2744818543U,
			1101434093U,
			201873968U,
			1972141678U,
			2266982055U,
			822629663U,
			2144839476U,
			99932946U,
			3301102371U,
			1182433226U,
			4076927036U,
			2667744471U,
			2742277126U,
			1341861236U,
			3128223302U,
			4023643237U,
			1572493165U,
			4011820511U,
			1394650490U,
			4042243957U,
			3480648620U,
			955904782U,
			351450715U,
			664621312U,
			3474223933U,
			3807358026U,
			13680877U,
			1095239211U,
			994200846U,
			1248080181U,
			167205977U,
			2821259515U,
			1834608438U,
			149628615U,
			108852460U,
			610551716U,
			4280139744U,
			3107142856U,
			1389446905U,
			2121526862U,
			1803997045U,
			3474407527U,
			2202404103U,
			2471534176U,
			455419707U,
			2976718204U,
			823451175U,
			2399568146U,
			1221205963U,
			2751390999U,
			1929165641U,
			3785126751U,
			3621944402U,
			3317451468U,
			3828326529U,
			1114997468U,
			792786052U,
			1367010889U,
			345112063U,
			3011525350U,
			3558211429U,
			683233168U,
			1613363545U,
			3559885002U,
			2930559694U,
			1515695885U,
			3164896108U,
			4093630150U,
			3070064115U,
			1886869501U,
			424864606U,
			4126659594U,
			1833515314U,
			2473745097U,
			1958967214U,
			3997417697U,
			3375185932U,
			2997535507U,
			2054386446U,
			2601381701U,
			4291846984U,
			331797156U,
			42213877U,
			1433011336U,
			536281239U,
			4191286886U,
			3995629648U,
			2862539639U,
			3329801257U,
			2481208843U,
			953192232U,
			4005011332U,
			2386747998U,
			3332977766U,
			3520125847U,
			624775328U,
			1788311847U,
			2778418956U,
			1858599031U,
			1064887402U,
			1673154943U,
			355195651U,
			1044950435U,
			708741273U,
			129487551U,
			500122344U,
			3371341535U,
			2288471046U,
			329414500U,
			440223182U,
			1925041582U,
			767011479U,
			3552380864U,
			3872726155U,
			2799129004U,
			2204983881U,
			3234653351U,
			1343100264U,
			4250154710U,
			2348779732U,
			611296404U,
			3876090784U,
			2039256322U,
			3246519034U,
			2939956882U,
			3806680261U,
			2774088823U,
			646342824U,
			1372077716U,
			1967380888U,
			3582043951U,
			1581866310U,
			4007585756U,
			4106028595U,
			1583905721U,
			3868604229U,
			1095002036U,
			952610537U,
			641898017U,
			2283901625U,
			4037873712U,
			3709841370U,
			2306387344U,
			90692142U,
			177914378U,
			3628732951U,
			1613176655U,
			4253373979U,
			1584776410U,
			4078767762U,
			3288709767U,
			2528264758U,
			2168566170U,
			734126548U,
			1253557767U,
			1162234422U,
			2718376683U,
			2515371786U,
			3262065017U,
			4235536834U,
			3827799462U,
			552408917U,
			1550198841U,
			1072001420U,
			1928659659U,
			268720488U,
			1449734785U,
			3897145055U,
			3117656251U,
			1875429151U,
			819049988U,
			1455453805U,
			3884712980U,
			391217090U,
			216464247U,
			3173877642U,
			1128498419U,
			2475243767U,
			3112512872U,
			1164883618U,
			3385480827U,
			3687551019U,
			2515947841U,
			2360117937U,
			1767619284U,
			704925816U,
			1308821130U,
			552771981U,
			2895560326U,
			1007527354U,
			3610167773U,
			2705757927U,
			3289339780U,
			4089131484U,
			1780879942U,
			739073999U,
			3554050061U,
			806220715U,
			3717948034U,
			3857432958U,
			292817035U,
			1000664330U,
			3103521309U,
			2320494690U,
			516451135U,
			3949271934U,
			2301977758U,
			3624295955U,
			3516986455U,
			1654030597U,
			1848812393U,
			2506275103U,
			648228814U,
			2388767163U,
			2018674488U,
			2922737484U,
			2098310732U,
			1561315164U,
			3817370978U,
			2879708425U,
			1663176307U,
			909939964U,
			3052576586U,
			2454144077U,
			803058593U,
			2052819801U,
			1213659276U,
			1988161715U,
			2022492652U,
			403402810U,
			3179316461U,
			4241517787U,
			1911030199U,
			1951387000U,
			742322502U,
			1759945503U,
			423746609U,
			3957201738U,
			2337596347U,
			2383371302U,
			672492633U,
			3858971673U,
			1414416542U,
			716085408U,
			321157633U,
			1452207030U,
			128906108U,
			797131398U,
			3435482069U,
			3286054422U,
			645237082U,
			3057323606U,
			1580184344U,
			814216522U,
			3835478437U,
			2902611195U,
			1907754301U,
			2651121690U,
			510703108U,
			2464003776U,
			2806754855U,
			2843480102U,
			4250995140U,
			1298892204U,
			1729660261U,
			3847010207U,
			1696739634U,
			358848691U,
			1387747161U,
			1713252839U,
			3693247532U,
			3964788862U,
			384671151U,
			2626254113U,
			148418448U,
			1166441352U,
			3995471626U,
			773540881U,
			3579577349U,
			4764125U,
			2850643297U,
			2233436115U,
			2463134335U,
			472036179U,
			1579046540U,
			1825977095U,
			64488107U,
			2653006751U,
			3052413666U,
			3410901614U,
			3582159858U,
			1612810345U,
			106205713U,
			3171786962U,
			380734791U,
			1781354043U,
			2026622045U,
			832478133U,
			961373549U,
			2730981674U,
			2770214508U,
			1263381258U,
			3989023290U,
			1927789518U,
			1315532573U,
			2412885528U,
			1303923440U,
			693744160U,
			4144533698U,
			3152752343U,
			1051699064U,
			778310204U,
			110794268U,
			12957873U,
			3602164627U,
			1704484603U,
			641563948U,
			1533421256U,
			2090219719U,
			725190481U,
			3452627405U,
			444909240U,
			425281249U,
			3805038572U,
			4232364809U,
			151905940U,
			1207333012U,
			842964775U,
			379681951U,
			3815080571U,
			2196034161U,
			2152138647U,
			893617258U,
			544141945U,
			2661063807U,
			1353885772U,
			2682841482U,
			1951552804U,
			657621281U,
			2033307673U,
			3900460837U,
			2248063326U,
			1938359539U,
			920378531U,
			1040188409U,
			584382919U,
			3366830765U,
			1832682227U,
			3503464133U,
			3165695926U,
			59724046U,
			3037337278U,
			493139004U,
			3328374290U,
			3481834043U,
			3445135741U,
			2129044118U,
			3264721036U,
			3701157057U,
			148270362U,
			2523046886U,
			757733628U,
			1490006004U,
			1919706735U,
			635782275U,
			2866100657U,
			993960002U,
			543314115U,
			530619138U,
			3476400135U,
			3978070650U,
			2877461166U,
			1057295383U,
			1685238282U,
			1933067756U,
			4075572541U,
			2218999878U,
			2657564880U,
			3111262041U,
			812132446U,
			3863335050U,
			2215941778U,
			3250738392U,
			3194777500U,
			2206481959U,
			1857307122U,
			182238149U,
			1984715905U,
			2217182764U,
			709801290U,
			3442865053U,
			897501408U,
			1025453119U,
			849504755U,
			75922077U,
			4289514912U,
			2768352230U,
			3967410144U,
			628126729U,
			2275703325U,
			2605548466U,
			1125683442U,
			3840303208U,
			2572972863U,
			1584349585U,
			120576933U,
			1288535508U,
			144067959U,
			1268662296U,
			27587234U,
			3730538989U,
			2299738675U,
			2299763558U,
			3706270248U,
			773251584U,
			1193061393U,
			1523946710U,
			2091257298U,
			1900149037U,
			62024802U,
			340128605U,
			575611307U,
			3301320149U,
			2517792308U,
			2236911927U,
			2301704321U,
			3761528624U,
			1589176498U,
			3243387371U,
			3177495639U,
			634577113U,
			2855132294U,
			4180967691U,
			1303425895U,
			1908854869U,
			1360319327U,
			1923335024U,
			1231423558U,
			4165816777U,
			698633614U,
			1535090396U,
			973136482U,
			2333593722U,
			4251812970U,
			2497743794U,
			814732943U,
			451470424U,
			593125646U,
			588140920U,
			3196849919U,
			2152847322U,
			3617682515U,
			2229288442U,
			1869869051U,
			3815848924U,
			1064457543U,
			1810108986U,
			3228892818U,
			571067314U,
			209968437U,
			977165121U,
			25048914U,
			2639954766U,
			1986882667U,
			1653510023U,
			3504146546U,
			4290503882U,
			3981868775U,
			1851576074U,
			3421284613U,
			3132592928U,
			2428651589U,
			2603367862U,
			411898035U,
			2659492491U,
			2314087083U,
			2799887071U,
			3300694515U,
			314705189U,
			2969716088U,
			3627613246U,
			2931714343U,
			1823234278U,
			2666517908U,
			2916752590U,
			681424699U,
			2563878160U,
			3097121293U,
			955339407U,
			3176249759U,
			220034626U,
			161832131U,
			2430533692U,
			2514868490U,
			88681198U,
			4140862697U,
			479116171U,
			886730732U,
			1378076697U,
			3246652419U,
			3425152427U,
			728480910U,
			3772037257U,
			33068370U,
			4111411973U,
			684239268U,
			3133461788U,
			3958904537U,
			4190408118U,
			3512186978U,
			2207382731U,
			2971927120U,
			1179696464U,
			3581367912U,
			459844771U,
			1127289033U,
			1371034646U,
			2732641470U,
			1975226472U,
			788746667U,
			3103372513U,
			1844595680U,
			3510680848U,
			4096061597U,
			4073020368U,
			3572983394U,
			3093173349U,
			152622974U,
			1573622072U,
			4144730519U,
			3339225242U,
			3003154863U,
			1410926884U,
			210828896U,
			143710547U,
			4265872861U,
			3462585146U,
			884934732U,
			3362631072U,
			560396532U,
			951334653U,
			343361985U,
			3905994192U,
			191692616U,
			746290665U,
			1311440735U,
			2863386820U,
			3846337734U,
			2464244532U,
			835169034U,
			3838732712U,
			2888918371U,
			852092247U,
			1874964658U,
			224269506U,
			3013504483U,
			884411637U,
			3278634046U,
			1724245128U,
			1323954863U,
			276483860U,
			1641229012U,
			1981632830U,
			3625827861U,
			2047308853U,
			217499358U,
			4061713976U,
			328682166U,
			1594963688U,
			3438280910U,
			3567584202U,
			2713329675U,
			1399298332U,
			2830115980U,
			3874885475U,
			2142427880U,
			2614835529U,
			1757637009U,
			4050230569U,
			475006745U,
			743359335U,
			1451844435U,
			3812026230U,
			3554499380U,
			4177311922U,
			3302591178U,
			1145778272U,
			3792956833U,
			3139468069U,
			3866452326U,
			2520725059U,
			1307042216U,
			171337303U,
			3841439029U,
			3626762086U,
			3120829394U,
			364011924U,
			1598372242U,
			400046362U,
			825748935U,
			2117925711U,
			2169975781U,
			3569211359U,
			3964343179U,
			213087679U,
			4213571762U,
			270304709U,
			379336513U,
			1135074216U,
			2614642648U,
			2510590786U,
			743708960U,
			2978099083U,
			3711347350U,
			2308691959U,
			854305735U,
			3419344932U,
			2491227601U,
			2156086028U,
			283583622U,
			2585831812U,
			1751212499U,
			1107638945U,
			432567407U,
			2145097531U,
			2551963090U,
			200214993U,
			4024892702U,
			2402773872U,
			3568920220U,
			4240523825U,
			2803695142U,
			2584596018U,
			3398685503U,
			812168457U,
			638040559U,
			3956416622U,
			2840065821U,
			485624697U,
			4018771407U,
			2457198980U,
			3616202457U,
			3453022544U,
			4015446779U,
			532344135U,
			3329242493U,
			2412764243U,
			413470217U,
			1240335908U,
			1739294669U,
			1976793558U,
			1729310497U,
			292764780U,
			1188951012U,
			806881519U,
			2298827228U,
			2123676563U,
			4211819009U,
			841146904U,
			3326315005U,
			1952560663U,
			3068747119U,
			558875235U,
			2183884096U,
			3544712057U,
			1630057657U,
			3259314973U,
			1900266599U,
			4217641050U,
			891083126U,
			2663802445U,
			1085345423U,
			509492846U,
			450463195U,
			2991070132U,
			1028185478U,
			2494778675U,
			2447666348U,
			1104273167U,
			816693472U,
			1359571235U,
			3141755710U,
			184975562U,
			378251234U,
			2429057705U,
			2432758973U,
			1845806247U,
			2726038445U,
			1865308786U,
			2505208308U,
			3693896481U,
			1910477508U,
			2258999414U,
			4202864842U,
			3167767004U,
			1215428890U,
			3457756300U,
			1770948420U,
			13356089U,
			4118907914U,
			1088123590U,
			2122525252U,
			3365783429U,
			1533916840U,
			3100126594U,
			3857792809U,
			1691098323U,
			662630328U,
			1267287315U,
			310116928U,
			1409350586U,
			3178616626U,
			75672070U,
			3973331155U,
			3354422370U,
			3647613445U,
			3757150560U,
			920597263U,
			1238479878U,
			2363104875U,
			731734114U,
			2682679635U,
			2141737269U,
			2756783657U,
			1508735225U,
			2384717764U,
			3581063987U,
			928898970U,
			1181695430U,
			2040895302U,
			3605424612U,
			2736541932U,
			3348518013U,
			3037558012U,
			2664645779U,
			17727380U,
			3063906999U,
			3944803485U,
			1057613578U,
			2884042888U,
			107078189U,
			1402312629U,
			3973799623U,
			735155751U,
			3175121143U,
			3353404151U,
			4147258005U,
			1066550675U,
			2780723555U,
			2958186154U,
			2960673360U,
			1980310874U,
			2075209395U,
			25108768U,
			4277044189U,
			1865768383U,
			318912128U,
			3599812574U,
			3125314893U,
			256555318U,
			1021189433U,
			306528874U,
			664548129U,
			2387547350U,
			1637837229U,
			2296439227U,
			1232065453U,
			1997279291U,
			1573008771U,
			1694006495U,
			3414498565U,
			259224609U,
			188128527U,
			297577852U,
			1947666645U,
			1217304090U,
			1768821244U,
			1797484529U,
			1616305836U,
			3324523774U,
			966624922U,
			814183632U,
			460576976U,
			2945471893U,
			1195603470U,
			1043983736U,
			833611999U,
			1036294578U,
			3150638874U,
			3354902007U,
			4224563361U,
			3835944911U,
			2061212059U,
			607289519U,
			1758373420U,
			1290583041U,
			1333626863U,
			3740267555U,
			350952398U,
			2507870986U,
			1490615074U,
			205201555U,
			1904365307U,
			2337917449U,
			3025393560U,
			2832684437U,
			531133129U,
			2261688223U,
			773481759U,
			2270411817U,
			2375705793U,
			1558360802U,
			1400330767U,
			2221713792U,
			2947051763U,
			341542264U,
			3741187531U,
			3560726509U,
			1047565973U,
			1361927652U,
			3330693730U,
			2307493339U,
			3860220514U,
			1037448100U,
			3954632095U,
			3105388906U,
			3266854955U,
			4119107866U,
			595073402U,
			1157873545U,
			322235294U,
			3975345410U,
			1039405160U,
			869837430U,
			811014794U,
			3683760203U,
			2307817684U,
			2364606599U,
			388133120U,
			1017082811U,
			3439400060U,
			69547711U,
			3229683312U,
			4259835767U,
			2751539697U,
			4066437219U,
			4081913134U,
			1623272940U,
			159885376U,
			4075552261U,
			3667153823U,
			3001993002U,
			1314273482U,
			1701659352U,
			1731474184U,
			542435875U,
			1860394987U,
			1067968117U,
			1915646702U,
			214510467U,
			836089477U,
			4051681182U,
			2237674244U,
			3943566907U,
			1070225500U,
			3660072488U,
			2688772095U,
			2808168392U,
			4173069964U,
			2672682320U,
			4017633385U,
			3270237556U,
			2059485682U,
			868705911U,
			892474076U,
			410687085U,
			343074528U,
			2031643590U,
			4210972510U,
			252316775U,
			1323034073U,
			3509785892U,
			1816758304U,
			2113787259U,
			2235513542U,
			986203655U,
			2927770499U,
			4182654991U,
			2967928030U,
			542390749U,
			3215054684U,
			3717709124U,
			2034465393U,
			3670964971U,
			3642432471U,
			2789680253U,
			3259490931U,
			4186235546U,
			568788274U,
			3693377250U,
			3869693186U,
			1344838170U,
			817370816U,
			3669306588U,
			3695172013U,
			2928047680U,
			3388327487U,
			4069523837U,
			3706132588U,
			3648474177U,
			1894697446U,
			2418946813U,
			888657768U,
			1112744212U,
			3998221824U,
			308510419U,
			484212775U,
			1545049838U,
			2874060904U,
			3277062445U,
			2342938087U,
			2023142203U,
			1474808660U,
			4107358809U,
			2411670955U,
			1574833487U,
			2512733776U,
			3362207231U,
			2360899768U,
			2365972531U,
			58596455U,
			2193555188U,
			411243471U,
			2612589461U,
			1532295372U,
			84779211U,
			1856526835U,
			2776593977U,
			3449717577U,
			2869893232U,
			954798714U,
			2953431596U,
			4144367157U,
			2153593386U,
			2995444533U,
			2536944539U,
			3505767867U,
			2516184432U,
			3385380323U,
			1638759522U,
			1096222576U,
			3232038277U,
			551505310U,
			130217895U,
			715404480U,
			22538804U,
			3228561100U,
			2941632742U,
			1185058116U,
			1978177093U,
			3255388617U,
			3073968251U,
			4140853791U,
			603731757U,
			446917473U,
			772771272U,
			2251137722U,
			3368387202U,
			2313828087U,
			1662255500U,
			3367363489U,
			340016979U,
			2795375293U,
			3675807264U,
			2433819442U,
			219021518U,
			2041947889U,
			1419236616U,
			4232329263U,
			1453592854U,
			2039193067U,
			480276132U,
			2701034794U,
			4257495989U,
			3387011818U,
			420298116U,
			620150179U,
			2509655761U,
			3027984198U,
			1944645480U,
			280579179U,
			2114024339U,
			216377702U,
			903062284U,
			3127752487U,
			1356204900U,
			2473075074U,
			728601653U,
			2646165012U,
			4198550242U,
			449040336U,
			2458418828U,
			2670346275U,
			114051187U,
			2633535268U,
			2087492164U,
			761112329U,
			1317257502U,
			4020417870U,
			78989736U,
			2944119290U,
			1308998879U,
			1460874117U,
			4020316726U,
			1322722311U,
			858592875U,
			2880161699U,
			1144735169U,
			1153910936U,
			1915092605U,
			2027171533U,
			2443019018U,
			2241746761U,
			1703606796U,
			1666047406U,
			2100939880U,
			3848021999U,
			2288053011U,
			2879469688U,
			1483651554U,
			3481035317U,
			227059441U,
			3400707641U,
			877813244U,
			645169043U,
			2078774163U,
			4123084146U,
			2802997838U,
			963089676U,
			1569588699U,
			2711127221U,
			2779708893U,
			2353480670U,
			1676358668U,
			2077946319U,
			931119629U,
			359083849U,
			3298064947U,
			3155719171U,
			1457347059U,
			1587948665U,
			4169173843U,
			273627211U,
			2726982011U,
			3300538987U,
			603425113U,
			1235357815U,
			2133979293U,
			3534202141U,
			2634794779U,
			1249768050U,
			2499813484U,
			905947924U,
			8326442U,
			3701264567U,
			3365029174U,
			4275399652U,
			2376091840U,
			2285312675U,
			2318736981U,
			608558562U,
			2999513938U,
			3637280437U,
			3930385262U,
			279792060U,
			3130309727U,
			2245590258U,
			2654086717U,
			3866930754U,
			3202232804U,
			3653537194U,
			2013889330U,
			4089723476U,
			4186592122U,
			42274367U,
			3048683557U,
			3132865268U,
			835587971U,
			2490820338U,
			852837149U,
			7669213U,
			2285737614U,
			487009275U,
			718620001U,
			2874266871U,
			3778510053U,
			2306556774U,
			3351753845U,
			2609588232U,
			603731574U,
			782417336U,
			2526805832U,
			1156547622U,
			3075801176U,
			3155404056U,
			2666212342U,
			997226888U,
			3088268155U,
			1839503441U,
			236622196U,
			1643091707U,
			2367760794U,
			1998921374U,
			1042191567U,
			2963994271U,
			96073226U,
			3247753777U,
			299175235U,
			848098553U,
			2126105355U,
			1549280763U,
			21946657U,
			3496562300U,
			3104088763U,
			3370053129U,
			3904143471U,
			4202626629U,
			3185261342U,
			2443988490U,
			3767107033U,
			3325996840U,
			2538466221U,
			2980611302U,
			3260899750U,
			2741535408U,
			2391290389U,
			1652434000U,
			316305150U,
			2406424025U,
			971076352U,
			2813963036U,
			3354676415U,
			1957229885U,
			546838735U,
			1939589071U,
			3875016849U,
			3826070786U,
			1080210333U,
			3554984721U,
			3528465388U,
			2602629071U,
			1147809694U,
			69358028U,
			3446482672U,
			4240758406U,
			2794496303U,
			1932556193U,
			3452705259U,
			1678780643U,
			845258924U,
			2216516993U,
			2653642836U,
			3770242639U,
			1096742844U,
			1215766398U,
			201171950U,
			1943710342U,
			1934361885U,
			745807041U,
			2312073790U,
			3142860305U,
			4221585901U,
			3890794475U,
			4175365580U,
			534648611U,
			2021927550U,
			3792553079U,
			1503202683U,
			3063064067U,
			946561675U,
			2761405455U,
			2504943085U,
			2453115037U,
			738428611U,
			3171065001U,
			3252870399U,
			2341361115U,
			684018032U,
			2038714934U,
			4095912475U,
			1701815451U,
			2685248491U,
			4212712910U,
			2592820534U,
			3273744436U,
			4208716025U,
			2026165840U,
			18654320U,
			3319536765U,
			783328146U,
			2530073069U,
			3862187172U,
			25264522U,
			3077382474U,
			3645726264U,
			1759016856U,
			3477119978U,
			2722464067U,
			2744485860U,
			116884104U,
			1183824009U,
			514249711U,
			1650331954U,
			2613162366U,
			201369425U,
			2996439113U,
			1343673466U,
			3280496187U,
			1975686827U,
			3669252351U,
			2351618145U,
			1527219783U,
			3107822355U,
			3692447096U,
			3366651456U,
			409774053U,
			1676338598U,
			2793390568U,
			3343501287U,
			3302475630U,
			461404263U,
			2450869995U,
			3403903975U,
			4045527863U,
			514366311U,
			1542117981U,
			3049036346U,
			2620953787U,
			579072803U,
			2837121084U,
			2711327578U,
			3101666446U,
			2677753898U,
			2809612344U,
			3955106799U,
			4010931652U,
			2378367081U,
			1508872706U,
			3434853033U,
			2732022169U,
			1165790012U,
			3183625013U,
			1740802408U,
			183353466U,
			1336158936U,
			4069497766U,
			1660743876U,
			45821547U,
			1016479521U,
			3032104990U,
			2280991917U,
			1571198285U,
			2572099702U,
			1923049414U,
			919378184U,
			1490250258U,
			3034130047U,
			2742188370U,
			2953612132U,
			1578556643U,
			796825870U,
			1992018408U,
			3378218442U,
			1444713597U,
			3464058181U,
			11330369U,
			2797344705U,
			1910949682U,
			2203172453U,
			1158836544U,
			737063215U,
			3459718840U,
			3426431506U,
			3392267487U,
			1639089556U,
			2143568056U,
			2023997955U,
			1656353993U,
			1187516562U,
			3731149345U,
			1168687457U,
			1598805008U,
			1157764197U,
			614214839U,
			3747501337U,
			832187900U,
			526408934U,
			2727060983U,
			3089127045U,
			1695369310U,
			3201687749U,
			1334699214U,
			2251862274U,
			1261208478U,
			2324444499U,
			3691038223U,
			1597169204U,
			771646393U,
			3108609477U,
			1275961726U,
			3646151115U,
			738255186U,
			2539227550U,
			3524886057U,
			458444415U,
			1335877464U,
			695319941U,
			3325180112U,
			2936853162U,
			1935601365U,
			3745856008U,
			3225531354U,
			3819564680U,
			2517955657U,
			2829817614U,
			3672967086U,
			3162659054U,
			1084008074U,
			1111626300U,
			2898884287U,
			1897908692U,
			1307044954U,
			473995968U,
			2522662797U,
			3337769254U,
			2529391289U,
			1981802095U,
			3710587379U,
			3661489777U,
			1106556604U,
			2427323298U,
			1521647590U,
			1217358370U,
			1779501117U,
			983202917U,
			596010804U,
			1725628686U,
			3495909579U,
			4239661273U,
			537550773U,
			831227247U,
			1657552488U,
			3745406799U,
			1081162209U,
			1590992008U,
			2412283600U,
			3947535762U,
			2055034793U,
			2958433071U,
			371712480U,
			3006168025U,
			3224199241U,
			2469241297U,
			2529756489U,
			1196162920U,
			1951063304U,
			2407753760U,
			2020343471U,
			894310994U,
			2814708541U,
			4107799634U,
			2637174724U,
			3581832522U,
			199516068U,
			2502508192U,
			247082106U,
			1349860791U,
			3787168194U,
			1268998572U,
			2742664388U,
			965343930U,
			221836112U,
			714442629U,
			1441443637U,
			341603716U,
			4266079695U,
			3029144354U,
			875025023U,
			1522339505U,
			802768984U,
			1501720672U,
			2226917647U,
			428265161U,
			3655667528U,
			121029652U,
			248329031U,
			1289846863U,
			2238306341U,
			993535064U,
			601887903U,
			2868099916U,
			3514644505U,
			701441784U,
			3582581505U,
			2030039258U,
			2297920435U,
			3420230831U,
			402369006U,
			2866567697U,
			414687012U,
			2799051574U,
			2928113135U,
			2470339171U,
			960540912U,
			3881884879U,
			1137025605U,
			3533494415U,
			2671005486U,
			1692930343U,
			931704836U,
			2120806562U,
			3398234581U,
			770094868U,
			292185302U,
			1419405296U,
			915411270U,
			454120831U,
			833012294U,
			291277244U,
			2506177073U,
			3717176996U,
			601214643U,
			3406286116U,
			2794220042U,
			835531023U,
			2005887670U,
			1762636468U,
			1700630964U,
			2986014018U,
			2676719621U,
			1811877841U,
			448190558U,
			3796623218U,
			1882915370U,
			2657067488U,
			1774624284U,
			753896527U,
			3646623794U,
			1554270125U,
			1225367005U,
			1547249478U,
			2952347069U,
			2126174154U,
			1878733446U,
			195730922U,
			416408953U,
			2126146932U,
			1811664875U,
			477727423U,
			3112753837U,
			987302369U,
			2574098674U,
			2785914823U,
			2265406298U,
			3685169490U,
			4184436387U,
			1937414329U,
			4207456382U,
			3258418786U,
			306322911U,
			3846621265U,
			3603503013U,
			1774476867U,
			818894672U,
			3393088040U,
			101633427U,
			431294788U,
			3996196319U,
			1881637858U,
			1879243475U,
			4208249377U,
			928282346U,
			4125195367U,
			1110169810U,
			2233933325U,
			3708237112U,
			573673093U,
			3627910690U,
			14250245U,
			1188890581U,
			1115742226U,
			482861993U,
			1210739202U,
			1852367986U,
			1846426428U,
			2613020301U,
			547535944U,
			2077727359U,
			3065679342U,
			3865335739U,
			2619929200U,
			3690106172U,
			2994762809U,
			3309821677U,
			433220338U,
			1632032207U,
			1830066545U,
			4177228516U,
			8088934U,
			662419238U,
			1489804061U,
			3911168598U,
			1069671844U,
			738576368U,
			1174652511U,
			3545518853U,
			2510843986U,
			2490480531U,
			623719219U,
			124775384U,
			158083156U,
			3161768051U,
			3498990497U,
			135502058U,
			552617089U,
			4220070405U,
			1992266593U,
			3353640133U,
			3404225841U,
			4148220370U,
			942034580U,
			2835763499U,
			1629102381U,
			922340153U,
			2250837559U,
			286796054U,
			702565416U,
			2154985111U,
			2189593319U,
			2801479053U,
			2058912740U,
			1508772086U,
			214895263U,
			1942821122U,
			3939896913U,
			3041535154U,
			454591495U,
			67561136U,
			2425136803U,
			3102054387U,
			2415201174U,
			4239923670U,
			1625884167U,
			3843657657U,
			3870525557U,
			322577383U,
			3317126670U,
			4139115483U,
			3030418355U,
			3336557187U,
			432580844U,
			3789836989U,
			418048195U,
			182981391U,
			2555121561U,
			4273843192U,
			2809501621U,
			47302065U,
			2662179110U,
			2685626831U,
			3957121767U,
			2599349006U,
			3198797211U,
			3191627417U,
			1089262514U,
			1529459296U,
			3737470720U,
			1856184731U,
			753182463U,
			625760116U,
			529239766U,
			803617380U,
			26793017U,
			1255779976U,
			1775385641U,
			382791525U,
			2751796878U,
			2206250169U,
			920184270U,
			2311147927U,
			4074504033U,
			3872977991U,
			771512290U,
			1214538296U,
			2964376542U,
			3165200199U,
			1243732295U,
			2695560847U,
			2495770527U,
			571608717U,
			740655816U,
			919508840U,
			3377217284U,
			4235002522U,
			3718282476U,
			325026383U,
			612705189U,
			439335369U,
			2693467970U,
			4079852299U,
			1312861684U,
			2221664766U,
			2947184436U,
			2109558481U,
			3949525944U,
			2620567239U,
			3720495715U,
			1537693438U,
			1224265869U,
			2564424862U,
			469922449U,
			1519206666U,
			3079891132U,
			3564304842U,
			3558272039U,
			389920988U,
			1810276002U,
			796182106U,
			2170562446U,
			1736877875U,
			3449508241U,
			3638653383U,
			2577150186U,
			2913393879U,
			2469072430U,
			2453097681U,
			3099698677U,
			2647718821U,
			3581871257U,
			746262583U,
			394226836U,
			1461079642U,
			1107819160U,
			2468382034U,
			3875131445U,
			3723503539U,
			3413942416U,
			950425548U,
			3703915100U,
			3266599209U,
			1327637488U,
			2559539240U,
			130015010U,
			1176488798U,
			2692297076U,
			2463515496U,
			3256595948U,
			1842787476U,
			303599185U,
			1441477385U,
			4163586190U,
			2519408824U,
			1679399212U,
			1633676213U,
			2676259844U,
			2262863360U,
			1268428639U,
			1784749265U,
			2856927161U,
			1123346461U,
			1556797195U,
			1272804764U,
			1379068758U,
			3815859836U,
			584738608U,
			2086153883U,
			1954725585U,
			986114509U,
			1391758879U,
			1716862730U,
			1435881436U,
			2644180280U,
			3900179324U,
			1407476245U,
			1612131099U,
			2317951500U,
			2748606443U,
			905315077U,
			1765316052U,
			50228541U,
			2251585797U,
			610682147U,
			238802263U,
			1796337874U,
			311589636U,
			3692198053U,
			891088517U,
			3055618765U,
			548493869U,
			3304778848U,
			1378518468U,
			3834271045U,
			775780580U,
			4055459961U,
			3928896810U,
			1867517570U,
			1539334149U,
			1689688442U,
			1967514901U,
			1394165321U,
			797417335U,
			333846459U,
			3456554273U,
			866777163U,
			1988130796U,
			3614262050U,
			3390177796U,
			3513907601U,
			1625171664U,
			3090838704U,
			3525047998U,
			593093506U,
			1534363250U,
			1802143243U,
			1740075337U,
			2958437894U,
			3200021179U,
			286536377U,
			3950618206U,
			2714770266U,
			202453862U,
			1330029214U,
			471121736U,
			1588775407U,
			1283348261U,
			3889656508U,
			825638021U,
			1391469052U,
			4245110580U,
			390180930U,
			3084331314U,
			393317910U,
			2488764389U,
			1427559273U,
			3433633552U,
			4219998998U,
			2510807138U,
			845767952U,
			2414868638U,
			298933745U,
			2476042189U,
			1791111915U,
			1718746523U,
			2484162170U,
			105897976U,
			2650038726U,
			2606602146U,
			2673181459U,
			3500299229U,
			3956854807U,
			4041627068U,
			3640102078U,
			3481813288U,
			2307384584U,
			2253989163U,
			3606091025U,
			2449784046U,
			3719803482U,
			3795738608U,
			4235625396U,
			214802194U,
			2648194985U,
			2882249178U,
			1892665558U,
			3666234889U,
			2686467469U,
			3392636640U,
			1376910554U,
			144067610U,
			813200919U,
			3985425827U,
			107672486U,
			94884078U,
			370646405U,
			69050804U,
			2657313980U,
			542996445U,
			4162129104U,
			487089176U,
			3578824155U,
			3282364805U,
			445481725U,
			699743522U,
			298312665U,
			3344269944U,
			3247558156U,
			2001891415U,
			2727165640U,
			1819359335U,
			1967581800U,
			2668356772U,
			4149091753U,
			4139673750U,
			3374524132U,
			2941412502U,
			683193121U,
			2519725901U,
			1395640324U,
			3232738070U,
			716912014U,
			2867442805U,
			3521680141U,
			3262268779U,
			1593458465U,
			744497683U,
			3140941971U,
			882570356U,
			1749244774U,
			2515084626U,
			2029917035U,
			1310150063U,
			378797059U,
			2544440792U,
			4169241973U,
			2791793722U,
			2563960489U,
			922555065U,
			167980285U,
			479221329U,
			1183541186U,
			1383920489U,
			3404900258U,
			4043745012U,
			1296927078U,
			3695776862U,
			585558642U,
			2669160296U,
			1899091134U,
			2861778643U,
			1061337520U,
			1582687747U,
			3986082110U,
			3139976089U,
			3617123038U,
			3296479171U,
			502779053U,
			301658012U,
			2713993724U,
			3837796899U,
			1170939350U,
			1764728938U,
			1768899239U,
			3769083812U,
			3160160272U,
			622237986U,
			2106094622U,
			863667490U,
			3426373941U,
			2890981932U,
			2903379593U,
			4294678377U,
			2165846252U,
			2512382172U,
			520384036U,
			1900155012U,
			2373451690U,
			3050444002U,
			2855872125U,
			1557789788U,
			2367575187U,
			3890130610U,
			802905045U,
			913690982U,
			3377967675U,
			2343326695U,
			955650167U,
			3478652762U,
			880440539U,
			2498059503U,
			2455496307U,
			3286836426U,
			1368854091U,
			2664258175U,
			2802502237U,
			917670756U,
			1957473879U,
			3462980335U,
			344166693U,
			1348810842U,
			3234166615U,
			2596090158U,
			1414050562U,
			668748400U,
			3548848435U,
			3484441214U,
			491974604U,
			658178296U,
			3179411536U,
			937231482U,
			1525966572U,
			776198658U,
			2963214189U,
			1414166091U,
			2583135219U,
			1452950054U,
			164142192U,
			3041108270U,
			919490162U,
			1906584499U,
			249363269U,
			3365020704U,
			2934128405U,
			1673210676U,
			1176916112U,
			2904056210U,
			2215840894U,
			543868724U,
			3146565761U,
			4267419868U,
			369778291U,
			493421913U,
			601442044U,
			2296846687U,
			2721812545U,
			2463878379U,
			1358628160U,
			1040866885U,
			3018885583U,
			3483999402U,
			3754218049U,
			2373498672U,
			1338798828U,
			2675321143U,
			1734925507U,
			988300876U,
			3398350396U,
			3491447826U,
			1276329497U,
			505849287U,
			393978647U,
			3658998553U,
			4010034349U,
			1199114667U,
			1069965814U,
			543814369U,
			266499127U,
			3984281100U,
			3877011342U,
			616925828U,
			4222752187U,
			3613219200U,
			536051792U,
			810403519U,
			2014448186U,
			1737141700U,
			2049061518U,
			282478670U,
			1662409523U,
			2605800762U,
			3278527223U,
			4111993954U,
			3401395553U,
			959595351U,
			3633447646U,
			441970238U,
			4124136905U,
			2522085419U,
			1436709033U,
			651042233U,
			2116447401U,
			3240783406U,
			2983749881U,
			4091828482U,
			939445125U,
			3646486751U,
			3092101466U,
			3215351028U,
			3299976315U,
			1762887312U,
			2061509054U,
			2625822234U,
			3106441881U,
			1913482732U,
			3816590086U,
			2234775955U,
			90430017U,
			4036991318U,
			499006558U,
			1960016266U,
			1849069243U,
			75881637U,
			1379607218U,
			1936105865U,
			1858006032U,
			1113261208U,
			3263765758U,
			536183760U,
			3813684229U,
			931767212U,
			3732549663U,
			1456739517U,
			3886863381U,
			1294942767U,
			3547972531U,
			3498647477U,
			3646535459U,
			1311832758U,
			2249644821U,
			1859569964U,
			850396756U,
			438407201U,
			726831346U,
			1203680959U,
			2886188341U,
			278462083U,
			1139442200U,
			3608031714U,
			2178513563U,
			2067031334U,
			807605541U,
			3456156656U,
			1052142265U,
			1570525638U,
			1746709080U,
			2802497068U,
			2206522368U,
			384620821U,
			2659010416U,
			1858228798U,
			3874591372U,
			1976877212U,
			2673554072U,
			3281512774U,
			2642424965U,
			887321020U,
			1638940064U,
			3458857191U,
			490610413U,
			2718213386U,
			3720174897U,
			4206186803U,
			450438407U,
			578356135U,
			3029573995U,
			1690972696U,
			519775813U,
			2081366365U,
			2638573346U,
			273245378U,
			3986896391U,
			2512917123U,
			1420921769U,
			3661908913U,
			4290680912U,
			989434537U,
			735178234U,
			3323868556U,
			420663552U,
			3606878230U,
			1716747291U,
			2864591843U,
			474842676U,
			2504070657U,
			2363322303U,
			2470888636U,
			680251092U,
			1867483074U,
			174258873U,
			1036104504U,
			4250690784U,
			642133056U,
			3214804183U,
			1955061952U,
			3997737464U,
			1719363793U,
			1540867690U,
			2712273209U,
			2330872570U,
			2750394686U,
			2970649665U,
			1003583496U,
			4227202898U,
			1508430950U,
			2661651689U,
			1551818222U,
			341860701U,
			1266476550U,
			2609002060U,
			2298488180U,
			1265139655U,
			3794210888U,
			2274076178U,
			3669419299U,
			480240430U,
			3585881380U,
			1134460250U,
			3202864048U,
			3522832843U,
			3644613547U,
			3635808534U,
			2496461845U,
			1833402534U,
			3791964633U,
			222569965U,
			1573524587U,
			1176058537U,
			4178180855U,
			4021775425U,
			1164935534U,
			1239162736U,
			524748687U,
			249697919U,
			1668190173U,
			1356681511U,
			1187917718U,
			3666858125U,
			3741615964U,
			3825882231U,
			1343264277U,
			2237726887U,
			3662653445U,
			3533049825U,
			2725547242U,
			4170007744U,
			1820038677U,
			1383157221U,
			3038938404U,
			1328293509U,
			3230425387U,
			581375970U,
			3919459605U,
			1918674233U,
			1903192609U,
			2794992442U,
			1957050340U,
			3710277330U,
			1854894705U,
			2287019058U,
			3556622035U,
			2316894528U,
			1744424823U,
			2637639451U,
			826782763U,
			2950696645U,
			2446590656U,
			974622577U,
			1579192123U,
			3946090828U,
			2974103987U,
			1603551245U,
			3913441654U,
			3109634807U,
			2817679063U,
			1460331267U,
			1598646729U,
			1993941252U,
			3711458566U,
			4291236035U,
			2114915768U,
			1100307752U,
			128857977U,
			4090640260U,
			3843613334U,
			3550342463U,
			2818635288U,
			2353749336U,
			3314115856U,
			1432690323U,
			3303694008U,
			3882781754U,
			1216536429U,
			2054571847U,
			2895057014U,
			486005706U,
			428831160U,
			2380577510U,
			1183025567U,
			204852940U,
			3060564454U,
			1257762740U,
			3902743003U,
			726953923U,
			4161326368U,
			1586478782U,
			1534922394U,
			3080294404U,
			3839020215U,
			286214704U,
			273587632U,
			548665647U,
			3762718384U,
			3252875711U,
			3041586018U,
			1298386704U,
			2913622414U,
			3791319333U,
			526843335U,
			694207431U,
			2173811575U,
			767441613U,
			534210920U,
			3667757036U,
			1092553144U,
			3139993658U,
			957997300U,
			2361853538U,
			3237505564U,
			3863726324U,
			1549676392U,
			2248400125U,
			3543480301U,
			1366456265U,
			1246629523U,
			944168281U,
			365148819U,
			3797090963U,
			2254402985U,
			3062259372U,
			1620261814U,
			993462021U,
			56612967U,
			3247312682U,
			1598003634U,
			4270402200U,
			62931264U,
			1089698547U,
			2165838481U,
			3724054466U,
			1479049633U,
			839966110U,
			2482252536U,
			4164492345U,
			624427408U,
			193123463U,
			2955118632U,
			3048973526U,
			2534359150U,
			178720959U,
			978920394U,
			94382406U,
			326017433U,
			3201759757U,
			4131442543U,
			2366733122U,
			3880569278U,
			2621753405U,
			2953690361U,
			3031087362U,
			1734872644U,
			1278806858U,
			2283063833U,
			112512600U,
			1494177500U,
			1402109456U,
			2991190887U,
			3565435389U,
			3676238672U,
			3220862757U,
			1952913008U,
			114962649U,
			3022487338U,
			1689740524U,
			3041197869U,
			1241895596U,
			984138285U,
			1618540266U,
			1392076803U,
			1772057238U,
			3807501253U,
			2402860007U,
			2025762580U,
			1279806707U,
			3110225291U,
			769672307U,
			2867759609U,
			609971037U,
			2215426674U,
			2238900714U,
			2921923663U,
			2744192827U,
			1934706687U,
			3259343295U,
			4260851247U,
			563399774U,
			3556753227U,
			2208605838U,
			2128442858U,
			3969606085U,
			3673272050U,
			397587214U,
			1374413314U,
			1030658782U,
			2600436071U,
			581750229U,
			1843603367U,
			604330875U,
			93522183U,
			2686497201U,
			2553077026U,
			998041033U,
			2869236172U,
			457332302U,
			2792476849U,
			234012212U,
			508005971U,
			2760528756U,
			2842002818U,
			893262254U,
			73239023U,
			1659737110U,
			3868472209U,
			1888793274U,
			4213969830U,
			363640139U,
			4077310371U,
			3459581700U,
			2709265840U,
			1225709615U,
			255645472U,
			428392267U,
			2030295765U,
			1052092176U,
			4236122596U,
			4283632601U,
			3414058867U,
			12676639U,
			11601527U,
			401942496U,
			3348606315U,
			2198403210U,
			2345941199U,
			3435959961U,
			3888678957U,
			2398361977U,
			1643580617U,
			820084494U,
			1191252767U,
			497341353U,
			3112023716U,
			3017484918U,
			1023630261U,
			3213994003U,
			487804366U,
			1537471867U,
			679114864U,
			3885748375U,
			1813702025U,
			894766261U,
			3523715298U,
			4109269230U,
			925533943U,
			3810014180U,
			1310025401U,
			3443137600U,
			1833875499U,
			3599866292U,
			3219004256U,
			299321790U,
			556802654U,
			3155494071U,
			112050615U,
			1838559611U,
			1877871702U,
			1352566364U,
			67076378U,
			2601514349U,
			1598124120U,
			2744488374U,
			2750142824U,
			165276679U,
			2826857065U,
			2541347518U,
			2833846939U,
			3983716030U,
			2682741952U,
			1801734174U,
			954621668U,
			296685720U,
			3501115051U,
			2146666695U,
			1661715973U,
			3072148174U,
			2471447302U,
			3163254906U,
			3646296530U,
			909632275U,
			2649745665U,
			3533068610U,
			1921118878U,
			2595301493U,
			726651345U,
			3308786619U,
			4167346727U,
			511833636U,
			1976411036U,
			4171283990U,
			3150996001U,
			1138507666U,
			684682293U,
			1042531101U,
			507590942U,
			3364421926U,
			3975905509U,
			1440568998U,
			3813910625U,
			1720532153U,
			356552860U,
			171544599U,
			581622485U,
			1002086109U,
			436254634U,
			704383242U,
			1993185446U,
			1014583054U,
			2324071819U,
			3141258457U,
			3495876088U,
			3017321863U,
			2129430324U,
			3326211127U,
			3793245716U,
			1060874636U,
			796899536U,
			2529524580U,
			1152196229U,
			2478144882U,
			711675158U,
			891554245U,
			1938679963U,
			1417842169U,
			4197822754U,
			3183822870U,
			2780739491U,
			4051845598U,
			303682570U,
			4074675130U,
			3083080107U,
			172539597U,
			3444227283U,
			3023610977U,
			351352568U,
			4086642950U,
			2256178522U,
			3957452323U,
			19669827U,
			1891596005U,
			1010135813U,
			2977868353U,
			2327291028U,
			1903533188U,
			1989521033U,
			4039681921U,
			2319561318U,
			3917078434U,
			1753229018U,
			258900565U,
			3264342270U,
			323914241U,
			1895841315U,
			1912395588U,
			807131911U,
			1196176896U,
			1041055187U,
			3702023646U,
			154358419U,
			4244476261U,
			2757402799U,
			3627096719U,
			4283384008U,
			1772211920U,
			89168385U,
			2821595741U,
			2874427464U,
			433391342U,
			1895582876U,
			4252759360U,
			3046626084U,
			1138631480U,
			751071536U,
			1802502546U,
			1406115894U,
			3669726005U,
			1952691695U,
			2586295042U,
			324664096U,
			929827264U,
			2911109482U,
			2503465927U,
			1600700580U,
			2814707499U,
			2195692963U,
			2990527914U,
			1411772120U,
			3973823132U,
			2386455671U,
			190027285U,
			4234369556U,
			3502216017U,
			482462060U,
			1911984833U,
			828855479U,
			1339322470U,
			3449305410U,
			3155656076U,
			206637159U,
			1334831904U,
			4212809686U,
			1351535002U,
			1620234267U,
			3650704653U,
			2109204139U,
			438468544U,
			203765542U,
			2866465904U,
			3696327215U,
			666653029U,
			4256547330U,
			1433591992U,
			4141173471U,
			3067691795U,
			2312751463U,
			3973359297U,
			1635729515U,
			3025723707U,
			1575312417U,
			2769269891U,
			210159925U,
			2921789929U,
			2749235586U,
			3606295003U,
			353557086U,
			2727989719U,
			1446331049U,
			2763776831U,
			1776538869U,
			1839571226U,
			2688022596U,
			3563181353U,
			4254227706U,
			519457649U,
			1984101433U,
			3389363515U,
			2204166570U,
			1290903433U,
			3954377014U,
			3245031233U,
			3475433548U,
			3665664717U,
			599606419U,
			3735341865U,
			1938162422U,
			688992467U,
			3532219175U,
			2122362136U,
			3193490662U,
			2861522669U,
			766803249U,
			2761605056U,
			388311338U,
			2344762403U,
			1217248353U,
			1642989350U,
			381303359U,
			2873212528U,
			4143225297U,
			3831035358U,
			1503201408U,
			1842937593U,
			1916385718U,
			1755868580U,
			1759486444U,
			1812750033U,
			1839773378U,
			654359351U,
			3283654127U,
			1410757473U,
			2185846029U,
			4068100683U,
			1180999580U,
			3250854486U,
			3304855289U,
			2748085699U,
			2007754716U,
			2235597777U,
			2026063218U,
			3230597215U,
			2707112748U,
			3985292827U,
			3762982719U,
			125072586U,
			3655784811U,
			2331299182U,
			3191098551U,
			3242389722U,
			1527955379U,
			2648131767U,
			537917025U,
			293480124U,
			149279654U,
			1966891908U,
			697968508U,
			2320053994U,
			1526427145U,
			2638216438U,
			933213140U,
			3467377772U,
			2369723583U,
			1493190171U,
			3906127958U,
			1227599040U,
			1599631850U,
			3936659412U,
			410274142U,
			2432272238U,
			92052223U,
			1761461911U,
			988336841U,
			3391547699U,
			516051692U,
			1793684196U,
			4233355774U,
			699247188U,
			3362351838U,
			4137836585U,
			1085189778U,
			2585199331U,
			1272517261U,
			1452916924U,
			1848214657U,
			2822584974U,
			1705881538U,
			2619455217U,
			585488444U,
			1390081395U,
			994873372U,
			4124720567U,
			1747158656U,
			3035859129U,
			491525427U,
			3244115825U,
			4105946377U,
			2996885628U,
			2017056628U,
			2148370178U,
			1998835189U,
			1825250038U,
			2329239965U,
			1956467366U,
			2921454839U,
			2683379131U,
			2330080379U,
			3144350021U,
			2327437334U,
			1377321690U,
			1460402997U,
			4061968381U,
			4276652563U,
			3724463723U,
			461648732U,
			910107992U,
			2894279556U,
			1694297831U,
			220517696U,
			944911951U,
			545295617U,
			3140631650U,
			1371093732U,
			676219251U,
			546008044U,
			2750146190U,
			2639732541U,
			1458459673U,
			2258461312U,
			3150462159U,
			2065814188U,
			2044005026U,
			1492284195U,
			1858169949U,
			1073361025U,
			3672331597U,
			1507798430U,
			2354222850U,
			4114470589U,
			332090242U,
			2501451533U,
			3697332276U,
			821634930U,
			731474876U,
			1465928645U,
			3948541733U,
			3597281146U,
			4075427875U,
			4156506071U,
			3520812445U,
			3302588458U,
			1049648667U,
			1209148986U,
			3399748106U,
			3062071853U,
			3995520164U,
			1788092271U,
			716794404U,
			504135262U,
			2410349099U,
			3749586919U,
			2100082044U,
			3671384013U,
			3885246802U,
			2161280484U,
			53356593U,
			248730398U,
			851006741U,
			130805464U,
			3850188214U,
			2610192430U,
			1320640535U,
			4030290289U,
			1435113203U,
			549218163U,
			2811122500U,
			3243692784U,
			1101328897U,
			322124309U,
			280789704U,
			1156388572U,
			4277226653U,
			2003169191U,
			3161035253U,
			1911224495U,
			3942348829U,
			2948822240U,
			3718934215U,
			253987737U,
			324127005U,
			1464170851U,
			436351465U,
			2588679683U,
			1248466740U,
			325158873U,
			967903986U,
			4255713322U,
			2039783393U,
			3334096567U,
			3707363395U,
			562347203U,
			3152558792U,
			832473U,
			974516238U,
			3776516327U,
			2784526482U,
			3727485406U,
			509145342U,
			251977588U,
			2296416422U,
			2619959173U,
			437633994U,
			1979270885U,
			28346687U,
			2653923959U,
			2980659991U,
			1432646642U,
			2402055676U,
			3084650911U,
			1620381972U,
			4002017820U,
			2338386508U,
			284441628U,
			3685451441U,
			3511348460U,
			3068809972U,
			773318075U,
			1803959962U,
			890292617U,
			91230009U,
			1885446198U,
			130752228U,
			2390557256U,
			2834297162U,
			3882060851U,
			1142668226U,
			3584917765U,
			1657694854U,
			3979642139U,
			2308112479U,
			2898361003U,
			192829823U,
			1217032821U,
			675078042U,
			3517951902U,
			3673917484U,
			703831435U,
			1321897871U,
			2411047157U,
			928150264U,
			569064022U,
			2882482966U,
			63053664U,
			3926317919U,
			3791419650U,
			813466733U,
			3998898580U,
			3442819862U,
			2202795842U,
			5217361U,
			2380958455U,
			2622107915U,
			3311204886U,
			4046363140U,
			413050314U,
			693168221U,
			3927494339U,
			2631234326U,
			2295846492U,
			697344097U,
			1185728892U,
			3647087933U,
			1235100041U,
			638186814U,
			479588881U,
			1477460051U,
			2191251361U,
			4178070261U,
			813694194U,
			766676725U,
			1010268335U,
			2206914465U,
			3897904332U,
			4234559117U,
			2307581309U,
			3736842554U,
			2763572516U,
			2777618604U,
			2061805028U,
			3442473213U,
			2788492230U,
			2355911276U,
			1972001753U,
			3166977040U,
			3448531182U,
			1433139303U,
			3844962265U,
			1862964250U,
			2140189575U,
			45056961U,
			87931886U,
			669615906U,
			1408116782U,
			291337843U,
			1879156590U,
			2319776132U,
			1261360632U,
			2924863202U,
			1423413314U,
			725685320U,
			723747649U,
			3676406577U,
			3520912333U,
			3497980238U,
			3226997553U,
			2061684027U,
			3429177508U,
			4249569490U,
			2200874680U,
			3579920578U,
			1888946959U,
			3276056586U,
			2581714277U,
			4264938522U,
			3403479937U,
			765547533U,
			1770141143U,
			2213777364U,
			511633420U,
			287689002U,
			1252734054U,
			147790383U,
			3587259048U,
			314274750U,
			2235954005U,
			3834821495U,
			1185626441U,
			3742385354U,
			1508866935U,
			3466632939U,
			3677770549U,
			2693049547U,
			2445304702U,
			4180631999U,
			2041082620U,
			1828133460U,
			535551651U,
			2663010258U,
			2012140252U,
			737741915U,
			1995637189U,
			1085955116U,
			2887225094U,
			2917845486U,
			1419680946U,
			4156424781U,
			169178940U,
			355014496U,
			3337608144U,
			3303953417U,
			4202881769U,
			2952706772U,
			898454674U,
			2632717529U,
			408175692U,
			1660850768U,
			75122993U,
			241461743U,
			3227266089U,
			3959843928U,
			281156655U,
			1842348764U,
			3190363542U,
			3703992077U,
			3750481631U,
			230978525U,
			2091970629U,
			1663015978U,
			1621068166U,
			718437868U,
			1164478650U,
			3945472718U,
			3785364482U,
			1288143323U,
			925681451U,
			1604994003U,
			1394976312U,
			1446740525U,
			1653451585U,
			1669524535U,
			982025874U,
			1960023406U,
			702547246U,
			710962846U,
			3910255029U,
			1072286472U,
			1111708155U,
			1221013086U,
			2307758254U,
			1848105030U,
			1496541741U,
			915149688U,
			4243290267U,
			236174456U,
			863106628U,
			1418767909U,
			3113512611U,
			1277724522U,
			984639084U,
			1342077219U,
			1312450418U,
			2873222800U,
			2057589183U,
			2074815729U,
			2498470731U,
			2494548843U,
			3452197419U,
			884704816U,
			2405206209U,
			4150821564U,
			3536416741U,
			2456715026U,
			3806520143U,
			4135086166U,
			687052666U,
			3006307225U,
			21492595U,
			371135105U,
			3097038416U,
			4073458364U,
			1814195882U,
			3849277122U,
			3301708797U,
			3747333057U,
			3263036249U,
			634285711U,
			2745171978U,
			205600296U,
			304281641U,
			497392566U,
			1974058732U,
			4271368493U,
			1351096771U,
			1396222704U,
			1934159204U,
			3808068084U,
			3920026179U,
			2671967169U,
			4025316588U,
			1064639869U,
			4070374621U,
			2677541033U,
			561547513U,
			3619521536U,
			484778238U,
			296591174U,
			2552139060U,
			1291133370U,
			3856215329U,
			2987842271U,
			1924397051U,
			254428708U,
			752951705U,
			1298446146U,
			1963736903U,
			1973666962U,
			3899676475U,
			59394981U,
			4020650056U,
			3695029595U,
			287550982U,
			1668745688U,
			2384757168U,
			3969506418U,
			1043785083U,
			1460537654U,
			3079357208U,
			4234766804U,
			1357179811U,
			680493400U,
			1313152269U,
			3288658724U,
			3695550288U,
			3791673024U,
			96148301U,
			4124877379U,
			837057687U,
			77222145U,
			2359404344U,
			752495146U,
			1934227919U,
			3280652345U,
			982643051U,
			614644031U,
			1563353368U,
			3721761316U,
			3260047461U,
			3215044100U,
			418143092U,
			2869323541U,
			170429617U,
			1154568649U,
			1523600493U,
			3919841883U,
			1736756657U,
			3829913092U,
			3602131701U,
			2018570980U,
			3188608209U,
			4109409913U,
			878014249U,
			2920405576U,
			1947717719U,
			4035670872U,
			3681461103U,
			573448079U,
			721978547U,
			2518629434U,
			3074370193U,
			1885998393U,
			2084561994U,
			1149255153U,
			26862940U,
			3788034416U,
			4246854446U,
			1252762640U,
			1569258909U,
			3762179574U,
			3816253856U,
			401319753U,
			3458872104U,
			2739051616U,
			576043185U,
			1262274062U,
			1052876089U,
			3810208637U,
			413300763U,
			3373208585U,
			2720017442U,
			3208403174U,
			3913753684U,
			3998044367U,
			4118844398U,
			588499716U,
			1654700273U,
			699483281U,
			2138242459U,
			3875312617U,
			4083638717U,
			1873645552U,
			1162593471U,
			2877985664U,
			2639832392U,
			1508947335U,
			1349439039U,
			3334485209U,
			985364777U,
			2459642502U,
			2271803488U,
			1473840115U,
			4185061388U,
			1818073262U,
			1553701064U,
			1347924235U,
			642380261U,
			1669691603U,
			3919482630U,
			745201443U,
			3953549528U,
			4156573189U,
			1724090378U,
			2118136064U,
			1969462912U,
			114039715U,
			1142630653U,
			4137222598U,
			4284251164U,
			2683830315U,
			1059098116U,
			2483850768U,
			2785780518U,
			4186800250U,
			4075590971U,
			536706164U,
			2519475586U,
			1743687027U,
			1341895344U,
			1440994521U,
			915855503U,
			2059608957U,
			21503061U,
			2131054629U,
			4026235200U,
			2541173571U,
			2640757868U,
			335527399U,
			126160961U,
			1429766014U,
			3368556713U,
			112654449U,
			2739394557U,
			2713738512U,
			2634033352U,
			3579287993U,
			4264213953U,
			1525003857U,
			1452008334U,
			2425960599U,
			3015187808U,
			1927364238U,
			3834422805U,
			1817777722U,
			3338569442U,
			2512609747U,
			2454630526U,
			1514274967U,
			1752577178U,
			2168456619U,
			3430969487U,
			2742115113U,
			1827601711U,
			3555907924U,
			2288072189U,
			523125317U,
			3299339621U,
			3271305240U,
			820179178U,
			2862280043U,
			561395091U,
			852721764U,
			3356496525U,
			2274926632U,
			732518545U,
			408921636U,
			4027480820U,
			2183934148U,
			3222627551U,
			3390552910U,
			437334971U,
			1690014304U,
			624857983U,
			1519515252U,
			2412146040U,
			695053977U,
			304882229U,
			824972624U,
			1719241075U,
			212641030U,
			3033389105U,
			1739007815U,
			2736086188U,
			1557257070U,
			3767736777U,
			3564001631U,
			3939386742U,
			1888219487U,
			795870391U,
			2293134436U,
			3370204997U,
			2203373693U,
			3233263814U,
			1301385703U,
			855197307U,
			2536930364U,
			2317099369U,
			3106942203U,
			4156394186U,
			2102603310U,
			1662674241U,
			2802285505U,
			3260330570U,
			3848015535U,
			855337402U,
			2112641714U,
			907587104U,
			2607026367U,
			3644521351U,
			1351521121U,
			2696334383U,
			1660004833U,
			1548081567U,
			394783010U,
			2758242861U,
			1076511750U,
			644432116U,
			3863818606U,
			1341174526U,
			172855441U,
			3561562553U,
			939925471U,
			3157788322U,
			1580497846U,
			3859955967U,
			2182703685U,
			125223731U,
			3158936730U,
			1695166520U,
			583861511U,
			3350104296U,
			4232519060U,
			1342486917U,
			3819293769U,
			4250007501U,
			2619174118U,
			1813537120U,
			1067141514U,
			1745125318U,
			3524996527U,
			2607456674U,
			98364446U,
			778693982U,
			3472715256U,
			3875389748U,
			312298930U,
			3955923991U,
			620191394U,
			365960593U,
			1914221860U,
			536415395U,
			2721409607U,
			1194870768U,
			2003302620U,
			2246079834U,
			3332171685U,
			392310422U,
			2851761807U,
			2794249077U,
			1432104453U,
			1697110789U,
			1506597746U,
			1870221158U,
			3669965838U,
			1724644975U,
			395092103U,
			2196053513U,
			1469053356U,
			2299663180U,
			1286850309U,
			927764888U,
			2539837163U,
			2379480675U,
			1891191922U,
			2314131555U,
			2370647934U,
			821223166U,
			2893625387U,
			1193656972U,
			693869605U,
			1988298467U,
			2224691595U,
			3059654571U,
			776920718U,
			1885111968U,
			2210660293U,
			719589873U,
			1350196261U,
			782349260U,
			3483109653U,
			11764707U,
			3580050009U,
			3842287187U,
			1245536554U,
			873706230U,
			2822663388U,
			2685965943U,
			1636594424U,
			269595900U,
			1175728610U,
			2904858189U,
			3801220197U,
			3306953394U,
			1747527009U,
			3233053721U,
			1937114528U,
			2594823990U,
			1079894663U,
			2755683318U,
			1752837640U,
			1043119534U,
			3713674643U,
			3442409261U,
			1533533676U,
			2509612410U,
			2445607741U,
			671266653U,
			827767599U,
			3365126563U,
			1086210748U,
			703647311U,
			1141521636U,
			2901840368U,
			907839661U,
			842409312U,
			757548422U,
			672552683U,
			2455659527U,
			2579517330U,
			95510299U,
			2917764725U,
			3479267999U,
			688012949U,
			2904405361U,
			2450793761U,
			744899975U,
			3188553455U,
			3478970988U,
			3325553434U,
			3130542819U,
			1213089565U,
			2326992154U,
			1825090063U,
			3234343950U,
			77865682U,
			2183048635U,
			4234648389U,
			897861302U,
			2238247754U,
			4084532533U,
			2107235774U,
			788275640U,
			106874776U,
			2139258724U,
			888352673U,
			2502150945U,
			970278653U,
			4115851553U,
			1526360959U,
			2594256102U,
			2914206844U,
			221028683U,
			4134883486U,
			1339809875U,
			404376709U,
			2126696312U,
			3061870035U,
			188803638U,
			87828256U,
			467447180U,
			2263002882U,
			3819519523U,
			3993351246U,
			412004800U,
			2661016766U,
			3967084803U,
			3350598158U,
			3192773624U,
			317477264U,
			2424988601U,
			2743037350U,
			385658079U,
			4202722162U,
			1633298755U,
			3858543365U,
			707262423U,
			3581169880U,
			740052623U,
			402379437U,
			471278668U,
			2668214525U,
			3851427007U,
			3821666864U,
			2809145615U,
			4081612296U,
			1980699433U,
			2406311049U,
			2472506104U,
			661569653U,
			3371048096U,
			475186668U,
			409913201U,
			238852045U,
			157363919U,
			2326921955U,
			2679046700U,
			1479803892U,
			56821792U,
			2337816181U,
			2370380594U,
			1169958781U,
			2278710575U,
			2208032289U,
			4221445537U,
			1780883783U,
			1133259615U,
			1122368635U,
			3284726093U,
			1444009744U,
			3635468599U,
			2491755565U,
			3188568344U,
			3200762424U,
			1003092696U,
			174320160U,
			1919156527U,
			3478285927U,
			2615389437U,
			2399196251U,
			1365716945U,
			818588843U,
			3575626665U,
			164705173U,
			3502797908U,
			3296883914U,
			2182475201U,
			590337064U,
			3644819154U,
			803224481U,
			700716008U,
			1418409583U,
			1593395944U,
			1274588111U,
			2515230658U,
			1663946090U,
			737877514U,
			3551035127U,
			1859961075U,
			4027668208U,
			4274676086U,
			1703738049U,
			1979320192U,
			4066665045U,
			272179407U,
			41696466U,
			3261960738U,
			2308684860U,
			2903604892U,
			1252503260U,
			1832664348U,
			471665813U,
			497132179U,
			4085252131U,
			43267702U,
			3938031027U,
			2007821927U,
			1044280154U,
			3607132280U,
			1720945698U,
			765258635U,
			1171396157U,
			257911494U,
			2558533188U,
			2690848876U,
			1100374979U,
			849860539U,
			4141414840U,
			2366873573U,
			489018514U,
			603985740U,
			1011960689U,
			1209149313U,
			1333193718U,
			3411908772U,
			2099594637U,
			3152185188U,
			2162005718U,
			2517417637U,
			2849968196U,
			4248104447U,
			2629575522U,
			1746727479U,
			2149733822U,
			1683983774U,
			2123534731U,
			979414639U,
			2476076111U,
			3845538704U,
			224885338U,
			1471957298U,
			2715403464U,
			1803921818U,
			1918778381U,
			1459332202U,
			3835099595U,
			1840297288U,
			1716345727U,
			1760748886U,
			2472896787U,
			1559886886U,
			3958825400U,
			3948262743U,
			681020263U,
			3120555417U,
			3675954576U,
			3111050499U,
			2756837126U,
			606716275U,
			925978413U,
			53312253U,
			1842854585U,
			3716126648U,
			2163044705U,
			538425319U,
			1812231311U,
			1379351309U,
			3043049939U,
			720207563U,
			3593939466U,
			2046181536U,
			93537607U,
			3979941063U,
			3701501953U,
			415766755U,
			3853855748U,
			3992791865U,
			3826382188U,
			1999056600U,
			1495695452U,
			144057070U,
			2676098500U,
			2276183977U,
			3179311610U,
			1970300960U,
			745469443U,
			3208648580U,
			3653114893U,
			719086170U,
			3495770854U,
			574040023U,
			556131828U,
			26994566U,
			3165202242U,
			168241251U,
			3244749792U,
			3751660687U,
			2490943353U,
			2521158919U,
			2243117990U,
			1006737553U,
			3883966831U,
			808710885U,
			498937118U,
			1123914092U,
			1140864229U,
			1455577257U,
			1192398880U,
			3307967699U,
			2700274145U,
			347665765U,
			138893690U,
			2540832749U,
			4172250059U,
			2493994761U,
			1763777307U,
			3882097243U,
			811963268U,
			4033796987U,
			3152871680U,
			764093333U,
			2735729060U,
			3614569637U,
			2872655086U,
			89286407U,
			4000085667U,
			4040763819U,
			2950256398U,
			3511734803U,
			2900313091U,
			3599664614U,
			1047261984U,
			2820464928U,
			2474408111U,
			2016633998U,
			2277345200U,
			2245197910U,
			2901025908U,
			699602688U,
			1594012487U,
			3077862222U,
			3546067467U,
			714598582U,
			899760821U,
			3867663633U,
			1266584723U,
			3282572623U,
			2147502274U,
			630277193U,
			2188633795U,
			750971819U,
			183500529U,
			1666372510U,
			3961552609U,
			3179820377U,
			4159792451U,
			3293008092U,
			1244429629U,
			3714965950U,
			4268890648U,
			2631689741U,
			1357998171U,
			2348903880U,
			1103603374U,
			647917843U,
			4115248781U,
			2665459579U,
			884074065U,
			639920488U,
			2234579857U,
			86560081U,
			1437023796U,
			2843714885U,
			4008071828U,
			122867834U,
			723716829U,
			1180054783U,
			2475738791U,
			900124286U,
			10017632U,
			3205098706U,
			3120942062U,
			2342702723U,
			3008419259U,
			4167076993U,
			1492712484U,
			3017482899U,
			1271243466U,
			2877674197U,
			2362196874U,
			3252893865U,
			2630360167U,
			3558142257U,
			270475109U,
			2137689738U,
			874587874U,
			942456429U,
			1888325013U,
			767500796U,
			1703820403U,
			3376778062U,
			73411500U,
			29192669U,
			202781919U,
			3055981432U,
			4277742968U,
			4157086394U,
			4213303683U,
			1190851210U,
			1107221614U,
			458663672U,
			3338279403U,
			95602369U,
			3243176661U,
			739102215U,
			3668995563U,
			3954759791U,
			3149115995U,
			4011132416U,
			2653812310U,
			663868383U,
			1113120238U,
			4217743908U,
			453695887U,
			1969618058U,
			3960820164U,
			1286318220U,
			3812493537U,
			2082141552U,
			2663937911U,
			3295511058U,
			1335434407U,
			3200603136U,
			3734556496U,
			3450267558U,
			1695755229U,
			2284620093U,
			4210308246U,
			950285893U,
			1003410145U,
			2879732449U,
			3804167070U,
			1970860236U,
			883005566U,
			2405231358U,
			2560203297U,
			2898832251U,
			1228816223U,
			1067578154U,
			2964978337U,
			2673861520U,
			81152702U,
			974296380U,
			1813530855U,
			3157927458U,
			3458681237U,
			670318173U,
			3898178923U,
			963685297U,
			232399108U,
			1479599954U,
			98181439U,
			67332962U,
			299956740U,
			2886737754U,
			2777558164U,
			3942503874U,
			1354775544U,
			3881138394U,
			386397525U,
			624825897U,
			457713437U,
			402766199U,
			3277102531U,
			617514999U,
			2201449912U,
			3112397031U,
			821182429U,
			3660008099U,
			2030267289U,
			1095359931U,
			190838749U,
			1615918374U,
			2406619565U,
			2480472167U,
			1037232249U,
			4122929147U,
			1872052887U,
			2539781436U,
			945501601U,
			1513989891U,
			2630684312U,
			377613850U,
			3678115534U,
			762067050U,
			2687045279U,
			505027751U,
			493947776U,
			2229333821U,
			2747134998U,
			72134042U,
			725984030U,
			2837904199U,
			3053225295U,
			584728017U,
			2026871070U,
			1236863912U,
			1788017871U,
			213854317U,
			2211758811U,
			1687452972U,
			2391710467U,
			2378456281U,
			3328409169U,
			3162762101U,
			1406556899U,
			3420875670U,
			850739523U,
			3201259249U,
			3348662520U,
			1235442897U,
			2496768403U,
			2772626281U,
			179169767U,
			232844502U,
			801569014U,
			2048531986U,
			3653355687U,
			658127210U,
			4172908550U,
			3512259583U,
			380044234U,
			3271640396U,
			4196855191U,
			4046171509U,
			3669371185U,
			508988190U,
			694997316U,
			2938054509U,
			408047353U,
			3738006811U,
			1267329117U,
			968039590U,
			3400533712U,
			1420174454U,
			2721704316U,
			861170292U,
			2481375291U,
			4254597374U,
			2366109735U,
			3769479797U,
			312656641U,
			193368404U,
			2968606208U,
			1099711457U,
			2708224509U,
			920295784U,
			2984524570U,
			2385982273U,
			3752168954U,
			2777801644U,
			3962929689U,
			4030990437U,
			2666247622U,
			3322376975U,
			3764357468U,
			3559117014U,
			2574107412U,
			2112775807U,
			1339377493U,
			1946409066U,
			1954603299U,
			2970945512U,
			3665108705U,
			14725873U,
			3191398221U,
			1148265995U,
			3829616983U,
			4117018751U,
			3535627681U,
			319418830U,
			3889438368U,
			1641447762U,
			1184637169U,
			3808699380U,
			4218368807U,
			3225487330U,
			2332796469U,
			3002173876U,
			3916373231U,
			3219521769U,
			932840845U,
			979196367U,
			2391846060U,
			1776132097U,
			2570763886U,
			7451014U,
			1011840720U,
			2367434864U,
			2633806466U,
			2087147828U,
			248554103U,
			276218124U,
			237185654U,
			459187330U,
			3279790026U,
			1660019957U,
			1454630070U,
			1056380751U,
			1067629400U,
			371990813U,
			4115290810U,
			4118928938U,
			3920301375U,
			3884881879U,
			3580170624U,
			1262506218U,
			456873728U,
			3039362252U,
			2122631346U,
			284023309U,
			3123520069U,
			721980786U,
			276407644U,
			2225894476U,
			2998358511U,
			385001336U,
			2065460662U,
			2058061469U,
			279515676U,
			2799331566U,
			2051619962U,
			4240961101U,
			3556142047U,
			1247692419U,
			407095509U,
			1293144323U,
			2766703534U,
			3366746252U,
			1278930338U,
			4078456642U,
			2241956147U,
			3496451692U,
			1635688349U,
			3195404800U,
			2847776993U,
			2732244329U,
			119688190U,
			3643079204U,
			2223774835U,
			1181266811U,
			873225270U,
			2761572678U,
			2265861875U,
			414202160U,
			2071530324U,
			3814641273U,
			1983052391U,
			2551550711U,
			3687408731U,
			4109764202U,
			3188817619U,
			4012755131U,
			358710241U,
			3223482631U,
			3677614776U,
			1939105853U,
			3309608070U,
			3923534750U,
			2429355024U,
			1861452634U,
			2195376503U,
			1585802516U,
			3188069139U,
			4179002242U,
			3470365649U,
			2130000809U,
			2821495883U,
			4007814728U,
			3229275697U,
			706642008U,
			1937006282U,
			2757745350U,
			3980548373U,
			3998565104U,
			765932829U,
			1193908026U,
			2364861318U,
			3249085619U,
			1922368673U,
			1614830166U,
			1399911326U,
			1076697972U,
			1945248517U,
			686252266U,
			872705632U,
			4201657734U,
			3925506067U,
			2933882722U,
			466480416U,
			309392297U,
			1118180925U,
			1746316775U,
			2785643808U,
			465609528U,
			4063743312U,
			4262257967U,
			2958717171U,
			1920693104U,
			2899714693U,
			1258140486U,
			1833564709U,
			1256217482U,
			1272572775U,
			531609603U,
			2914848589U,
			3524083102U,
			3527184708U,
			3634031819U,
			3892488457U,
			3078176997U,
			4061814073U,
			3948257200U,
			3410125747U,
			7581936U,
			821992554U,
			3730207989U,
			1212550283U,
			2034409551U,
			2222190277U,
			2434646543U,
			4118784654U,
			3177581266U,
			3053998339U,
			4179254631U,
			4204025480U,
			1345121513U,
			1777681739U,
			3816273648U,
			454639231U,
			1769662387U,
			3054225857U,
			1015943908U,
			3272356479U,
			1150399621U,
			1468302418U,
			2479422857U,
			1277852339U,
			1634281943U,
			3284439590U,
			98361559U,
			2181349175U,
			1065471714U,
			1139835989U,
			2863042214U,
			565078573U,
			1154420338U,
			1297292314U,
			698410185U,
			3291345668U,
			3254172382U,
			895658853U,
			3387890045U,
			1114791707U,
			3620558720U,
			4244044265U,
			3586105184U,
			741695765U,
			494927502U,
			779552175U,
			2855928648U,
			63780027U,
			2314907605U,
			3281562035U,
			276948460U,
			3210313947U,
			2793504269U,
			2753588577U,
			2789122165U,
			1685391516U,
			2057531198U,
			2113715993U,
			83697671U,
			725332236U,
			291092020U,
			307203374U,
			2540932946U,
			1277867346U,
			4107399990U,
			507811546U,
			4206346764U,
			1034650071U,
			549577672U,
			240258677U,
			1846182814U,
			2790662175U,
			884255820U,
			1470241807U,
			1584523216U,
			3961237551U,
			3159237125U,
			3893186525U,
			3525534524U,
			3107326189U,
			4213723077U,
			3537281149U,
			1458398697U,
			220056715U,
			3099542708U,
			2618071618U,
			356891579U,
			3760864467U,
			3640552917U,
			2773046279U,
			2169152019U,
			2542032138U,
			330112328U,
			3568373747U,
			745999114U,
			3708679852U,
			2548034614U,
			1464535004U,
			3704870643U,
			1174331204U,
			2432368270U,
			3689095058U,
			2843430830U,
			4237058571U,
			1601891607U,
			55256316U,
			4110806128U,
			4077236806U,
			1998074069U,
			1491251633U,
			1076490405U,
			1376139005U,
			3755820659U,
			3183028540U,
			3626793598U,
			3473111944U,
			2149288225U,
			2892377508U,
			223780194U,
			1504130269U,
			2924627009U,
			2661229681U,
			275444532U,
			865795252U,
			3247925396U,
			1213207355U,
			1569035606U,
			2132062242U,
			234051781U,
			3249388530U,
			1605675692U,
			3642387843U,
			2577780774U,
			4160181369U,
			995111129U,
			3725310358U,
			3281472025U,
			733545581U,
			2991541741U,
			270521569U,
			4203301502U,
			76635945U,
			852846138U,
			94397428U,
			4225006260U,
			2544036120U,
			3310760740U,
			1943288342U,
			1468672158U,
			4165111073U,
			3658506228U,
			2875055616U,
			2564750028U,
			2471122165U,
			3264863811U,
			4034195484U,
			1409436616U,
			3359247313U,
			593378857U,
			2532676472U,
			1574832007U,
			3765292583U,
			3669195238U,
			262619604U,
			865235616U,
			2063223000U,
			1454835819U,
			2261687486U,
			384177064U,
			1475053309U,
			3577475074U,
			2617469572U,
			4134860650U,
			2307536843U,
			42140254U,
			561131243U,
			278198109U,
			4223268457U,
			2785932864U,
			625712019U,
			2329730115U,
			698789594U,
			2455594143U,
			634502173U,
			3540977053U,
			2516688940U,
			1299338217U,
			1308110620U,
			850553510U,
			1109070730U,
			2861705197U,
			2206914715U,
			3430674883U,
			882301336U,
			596802152U,
			2983491894U,
			735691258U,
			2371299318U,
			168035302U,
			904121468U,
			3087863672U,
			4073282511U,
			561506539U,
			1358305696U,
			2442969318U,
			1516992906U,
			505924773U,
			941885022U,
			3830557437U,
			292190335U,
			2125452728U,
			3546932824U,
			1429140515U,
			3464147984U,
			2376598288U,
			3039405541U,
			1065503674U,
			1072152432U,
			2502014933U,
			3811125769U,
			3984057957U,
			103618380U,
			3808896294U,
			186289843U,
			3579485505U,
			2548287801U,
			1586813229U,
			3238453104U,
			90902839U,
			3494246371U,
			776993243U,
			237715931U,
			3427647073U,
			2071012074U,
			3601762005U,
			1208227907U,
			4291897182U,
			2885972808U,
			326595884U,
			4036050925U,
			1280445083U,
			1869748730U,
			587494799U,
			3185105355U,
			2093112069U,
			4150263870U,
			3977360958U,
			3376401675U,
			1172907277U,
			4029262863U,
			504157101U,
			500965944U,
			3926950092U,
			3905330693U,
			3558947401U,
			834368866U,
			304799018U,
			1209735033U,
			1734033575U,
			3234965810U,
			1864307622U,
			2462549831U,
			1323010779U,
			211506293U,
			2187713476U,
			2540575502U,
			3191375278U,
			3211079332U,
			2132663301U,
			562150168U,
			1771570176U,
			997620260U,
			1199115048U,
			799628589U,
			238836031U,
			1436051528U,
			4235442968U,
			544601090U,
			3444776086U,
			3649118786U,
			2815222802U,
			2172331467U,
			954201623U,
			1518845419U,
			1705628903U,
			2275307173U,
			2616101234U,
			1036814985U,
			1484175697U,
			3793217768U,
			2598820555U,
			1989426618U,
			1377606226U,
			2598344545U,
			1635069847U,
			1057295611U,
			1038762822U,
			1889232235U,
			3584925574U,
			2091421820U,
			3741057058U,
			1494160582U,
			32703445U,
			260147694U,
			1843959791U,
			1380221837U,
			2158369587U,
			202795468U,
			4188381424U,
			2440466702U,
			4003912146U,
			587827427U,
			1280166125U,
			3209856633U,
			2549444566U,
			1517111555U,
			3459235043U,
			2852477733U,
			3216537796U,
			2486899995U,
			414876273U,
			2228839027U,
			1422912250U,
			3904099130U,
			1472003972U,
			787594660U,
			812451659U,
			347327542U,
			2738393548U,
			1256970275U,
			2543674728U,
			1292966868U,
			880028598U,
			2450385357U,
			4091756177U,
			464002526U,
			1371680430U,
			2424655281U,
			2157454328U,
			2534476003U,
			52845706U,
			1081964624U,
			2584354944U,
			714230920U,
			1579970315U,
			1855387701U,
			2860503807U,
			3335257375U,
			1614612736U,
			1500944698U,
			4244764575U,
			1093301302U,
			1079030162U,
			2532815485U,
			432758282U,
			1033033851U,
			3559019027U,
			1315043277U,
			2530024456U,
			725196675U,
			363029212U,
			3807360954U,
			945431387U,
			1641767289U,
			2143642908U,
			4038146858U,
			3083280901U,
			3493737112U,
			3388995950U,
			1985376729U,
			3619734809U,
			3032301611U,
			2877890750U,
			1187017388U,
			2247198745U,
			4135579039U,
			4017707302U,
			608670752U,
			2643335101U,
			805220282U,
			3486249102U,
			3950554696U,
			2927178862U,
			3096536493U,
			1786639548U,
			2011419664U,
			4023420664U,
			784887510U,
			820782013U,
			795091231U,
			2902904285U,
			593317134U,
			41628413U,
			1216445838U,
			2075949337U,
			870548145U,
			3471308146U,
			2386237887U,
			3169892927U,
			1810512020U,
			1622385698U,
			2136699834U,
			5801141U,
			2468763214U,
			46222048U,
			2465129634U,
			2007340718U,
			2149631628U,
			1033582227U,
			769530217U,
			2108114834U,
			1414061627U,
			2981943464U,
			1286912400U,
			3863827024U,
			552154229U,
			1813752230U,
			1444882251U,
			2601343420U,
			98560633U,
			3834372650U,
			1686758331U,
			3556643995U,
			1441720049U,
			2957990494U,
			4220320923U,
			2705263820U,
			3962787341U,
			173755344U,
			1851011196U,
			4112178676U,
			3405975140U,
			2186191151U,
			696265528U,
			181557233U,
			2722454737U,
			914299704U,
			2058198867U,
			3834438880U,
			4046282507U,
			1825673628U,
			2933651875U,
			1108685445U,
			1575286293U,
			3678477091U,
			2168057988U,
			3076644334U,
			1382481687U,
			4120098367U,
			2681379020U,
			2898046441U,
			4073208886U,
			2673625487U,
			3167337562U,
			3819167539U,
			1483857698U,
			219715521U,
			3423084547U,
			1977429531U,
			2715025390U,
			4265716119U,
			1564263762U,
			380903463U,
			2995834788U,
			1411517243U,
			2719642510U,
			2785748329U,
			548971029U,
			1646552621U,
			3272647499U,
			440236186U,
			2410088234U,
			3550045049U,
			2555646793U,
			3479497862U,
			357532517U,
			1709618367U,
			2895140382U,
			495865485U,
			3910942597U,
			1684438213U,
			1363193455U,
			3445519631U,
			3832491797U,
			352425367U,
			2046670246U,
			3362718724U,
			3243313419U,
			1264785182U,
			3848082659U,
			3083169675U,
			3599587536U,
			3510650425U,
			1257223654U,
			2742378092U,
			1799941364U,
			232988171U,
			4264289262U,
			416676951U,
			1736971457U,
			1759535548U,
			2707238628U,
			2145519878U,
			4293891990U,
			1701829963U,
			1754235024U,
			731159446U,
			1880114734U,
			915420611U,
			3465004816U,
			1553208939U,
			1455906060U,
			1825682745U,
			1554428041U,
			2953986673U,
			2907910172U,
			3478532848U,
			94635633U,
			740893774U,
			1888408202U,
			2998539592U,
			3630793691U,
			3273460412U,
			734309972U,
			134662606U,
			2506950076U,
			2288055222U,
			760148818U,
			1654910178U,
			3948775533U,
			1018998271U,
			2956105244U,
			3666324881U,
			1692236154U,
			1616822351U,
			1866821215U,
			3418523602U,
			1555047453U,
			3008383670U,
			126777666U,
			253170118U,
			3495324762U,
			1769224495U,
			3943625524U,
			2939000139U,
			733586657U,
			1011035814U,
			694307610U,
			1039222499U,
			2423730190U,
			2266826555U,
			3143396805U,
			1134190747U,
			4265736402U,
			1044655481U,
			4101249182U,
			3880612611U,
			4057275297U,
			2690223835U,
			1477782251U,
			1649627940U,
			3017108583U,
			3591140847U,
			551679501U,
			3097392253U,
			3990151888U,
			4240422176U,
			288106389U,
			2481010970U,
			2626959653U,
			2000183109U,
			2038078856U,
			2221597564U,
			921913112U,
			1288507556U,
			3169177819U,
			2976100918U,
			3325153368U,
			1276209970U,
			2414720650U,
			1635178262U,
			754475122U,
			1729136881U,
			2003432948U,
			3803733907U,
			2622070153U,
			337462563U,
			161004385U,
			1469206036U,
			3202494462U,
			3738868620U,
			1693025131U,
			596509604U,
			2944185554U,
			2330959852U,
			176437973U,
			684463407U,
			2269011556U,
			838775171U,
			1114397669U,
			3329242344U,
			731051719U,
			333448066U,
			2743735100U,
			4264777758U,
			1662448512U,
			2317434241U,
			1941879111U,
			1897375595U,
			3789545730U,
			3126164783U,
			1614432597U,
			4039163931U,
			3408609270U,
			594861671U,
			2255341142U,
			2976976954U,
			3404285673U,
			2703932293U,
			3056612301U,
			3354885023U,
			4221202737U,
			1410816359U,
			1729826512U,
			2365923064U,
			2422484779U,
			670670240U,
			3401464732U,
			3410229817U,
			3419159475U,
			270340893U,
			654080385U,
			1061838582U,
			3500117406U,
			78253790U,
			1852820894U,
			1089589273U,
			712815198U,
			4238992894U,
			1933469088U,
			2891376711U,
			603815815U,
			1158292509U,
			881269444U,
			3497563980U,
			98811417U,
			134750116U,
			2321746154U,
			2122398660U,
			3993313592U,
			2358145468U,
			607765091U,
			2856926943U,
			564626588U,
			2972522247U,
			530253650U,
			2761675463U,
			3903583657U,
			29766305U,
			2182373129U,
			1566946740U,
			1711836176U,
			2357929541U,
			1023469069U,
			1341148072U,
			227738340U,
			3675141540U,
			4018409662U,
			2782609163U,
			1230348387U,
			8846871U,
			88390712U,
			1440303381U,
			4032915421U,
			1627680640U,
			3069104995U,
			2930243336U,
			3101404143U,
			97580172U,
			2560054623U,
			2666184404U,
			3813065434U,
			1794350172U,
			1018614912U,
			3649781224U,
			1645241348U,
			3662239208U,
			1296040012U,
			3728692009U,
			3170583821U,
			1110212324U,
			3932145552U,
			3299846972U,
			3682256438U,
			2299546280U,
			2092121384U,
			2818864080U,
			3810995725U,
			2796948046U,
			3318414197U,
			3002529517U,
			1173612536U,
			574527174U,
			3208216609U,
			1835909765U,
			2128389905U,
			3250074641U,
			1445224951U,
			3057057213U,
			3160196377U,
			3974534975U,
			4275377468U,
			653322541U,
			3267699646U,
			2827576438U,
			3659596225U,
			847011963U,
			4145722672U,
			3138744193U,
			467641178U,
			1796347338U,
			717480148U,
			3506721675U,
			1002470449U,
			1185937460U,
			4153990223U,
			4055741368U,
			787075208U,
			3607597582U,
			1640515373U,
			1984805044U,
			4179217021U,
			3348414064U,
			2453469036U,
			2278380286U,
			779849009U,
			1183798731U,
			406824818U,
			4148134203U,
			2232427449U,
			2247812420U,
			3309171698U,
			4056061319U,
			2606085605U,
			1301129715U,
			3730303547U,
			3788455899U,
			3800786916U,
			2760597415U,
			137780944U,
			1680121345U,
			2886305901U,
			464418986U,
			119024247U,
			3308601879U,
			2311198751U,
			526607292U,
			2819181357U,
			1223154388U,
			47203890U,
			2468228561U,
			9215699U,
			132525971U,
			1475829265U,
			3195532919U,
			945951516U,
			1858228244U,
			880399572U,
			2266437454U,
			3581357026U,
			103606207U,
			2237854567U,
			3917523148U,
			963661158U,
			1110307811U,
			3940096694U,
			2169774915U,
			412680406U,
			1310939494U,
			2479339650U,
			407460629U,
			3618949215U,
			1776170690U,
			2900317879U,
			1376164612U,
			2991397553U,
			2147950484U,
			3279865070U,
			307993597U,
			3157979238U,
			1014349386U,
			2374036627U,
			3097635259U,
			2350355424U,
			1856354766U,
			3617307044U,
			3028906745U,
			160995262U,
			95892701U,
			1608337960U,
			480498826U,
			3879357726U,
			1408058763U,
			3362684152U,
			486919774U,
			1783525640U,
			2341528353U,
			137163229U,
			2774355524U,
			3880646455U,
			1304759112U,
			2179420874U,
			599564015U,
			4159247114U,
			2848253039U,
			3500062627U,
			2535537098U,
			3839617497U,
			3430684062U,
			3824136402U,
			2365638466U,
			2951232085U,
			1816158041U,
			3191455596U,
			488092980U,
			3532788531U,
			3351474725U,
			2611389515U,
			3675677451U,
			3168518753U,
			3268253734U,
			398621140U,
			1165492225U,
			1274196220U,
			3724519628U,
			128088338U,
			1616722362U,
			3530012045U,
			97000022U,
			4127339965U,
			3668163933U,
			3814358050U,
			244669841U,
			2694762203U,
			2383570793U,
			1431827634U,
			364380279U,
			3050694904U,
			4079295167U,
			2932553395U,
			2751806691U,
			698428066U,
			422100469U,
			3762122641U,
			3797285668U,
			3379988361U,
			552908104U,
			737523649U,
			3060907980U,
			1625419044U,
			3743356944U,
			3179063435U,
			11546192U,
			3656547976U,
			340717075U,
			558875531U,
			2776718872U,
			3359223307U,
			3656817439U,
			465437098U,
			484270058U,
			3331329490U,
			1259151690U,
			444892949U,
			4190664970U,
			538609975U,
			2284056659U,
			1645041784U,
			3384685475U,
			2437177943U,
			2555349915U,
			2744540319U,
			1976178750U,
			2757227101U,
			3365012715U,
			2641113448U,
			3873025419U,
			990316632U,
			2041161719U,
			3046895714U,
			588211731U,
			291934576U,
			4029351204U,
			330472281U,
			4005652728U,
			1259453573U,
			365339996U,
			310700987U,
			1259977235U,
			2869514995U,
			3489275355U,
			867824701U,
			3382900329U,
			4177299147U,
			3166645275U,
			1341436569U,
			702575632U,
			1962043860U,
			2754811616U,
			874821433U,
			1049848611U,
			116304054U,
			811052017U,
			1382200918U,
			2187975198U,
			2567726583U,
			2336284463U,
			3600158034U,
			3168876708U,
			659765794U,
			283574678U,
			2540938458U,
			2018531090U,
			4080397575U,
			3469728189U,
			175165424U,
			2046092619U,
			2639945266U,
			446355789U,
			626641356U,
			746408745U,
			3314262594U,
			1796608533U,
			4159316696U,
			2913496634U,
			3006966671U,
			4012872129U,
			1926301703U,
			301082709U,
			2588973494U,
			2440349279U,
			1597914508U,
			4027301073U,
			631235602U,
			3634301427U,
			1988751601U,
			3204475203U,
			4151913495U,
			2992692329U,
			592998482U,
			2547561669U,
			1163786645U,
			1515632579U,
			3842163013U,
			1773653186U,
			2876396282U,
			2068984884U,
			2292623699U,
			1771450328U,
			4131911584U,
			297692795U,
			3538438693U,
			1630886961U,
			1329054237U,
			3260163892U,
			3581587496U,
			614487293U,
			3480529345U,
			317066120U,
			3446496283U,
			1297176798U,
			2825505738U,
			2963712171U,
			3678690821U,
			3378779377U,
			3926468154U,
			971482993U,
			2445361632U,
			3741858868U,
			3955857232U,
			3134428650U,
			3458076336U,
			1029798820U,
			857763192U,
			429769515U,
			3514915747U,
			2156746784U,
			3679063565U,
			3169485321U,
			1840103870U,
			3576451846U,
			3607072668U,
			1748540549U,
			3843252486U,
			4003379029U,
			2028082566U,
			550769228U,
			97159226U,
			470699465U,
			1802289878U,
			174670008U,
			1810758229U,
			732791029U,
			2699297519U,
			1046389902U,
			3795106552U,
			2997301134U,
			596898158U,
			3321770712U,
			3532984547U,
			730989674U,
			1708594738U,
			3853912471U,
			575481527U,
			3349636423U,
			3691951332U,
			38006744U,
			2857295670U,
			2221178011U,
			2192644139U,
			65048311U,
			3694719762U,
			1741750452U,
			2370443338U,
			3226768969U,
			153618828U,
			3137974028U,
			1227912923U,
			3509191831U,
			351426148U,
			4206455769U,
			549867880U,
			2100238364U,
			3139990514U,
			3380945207U,
			1217832376U,
			1085827844U,
			3259958624U,
			3248869735U,
			2607984863U,
			701158112U,
			3422549446U,
			2573847187U,
			3475312973U,
			2908802372U,
			3811383317U,
			485053485U,
			2783769703U,
			3914125627U,
			215636243U,
			1055282670U,
			1097205298U,
			4230029490U,
			2167079147U,
			37726772U,
			3018812932U,
			705698998U,
			1947200693U,
			3454547665U,
			1609493517U,
			3945822955U,
			2519477277U,
			2091870350U,
			4063035245U,
			2910708621U,
			2558035557U,
			1875418952U,
			2843359864U,
			345265784U,
			2027092066U,
			3218669115U,
			116333074U,
			1048141294U,
			2159217146U,
			1974030932U,
			4070390308U,
			497057603U,
			3036659344U,
			1280131824U,
			1642062416U,
			1804346140U,
			2023298281U,
			515045056U,
			1041965924U,
			3500761685U,
			1608371202U,
			241920828U,
			2667313621U,
			4082678214U,
			1009566722U,
			4119556255U,
			2468271162U,
			3271832979U,
			3995217599U,
			2113164012U,
			809265686U,
			1212688121U,
			3828205579U,
			2533758547U,
			2496444814U,
			3722724808U,
			1561263671U,
			3960508581U,
			1164066821U,
			275153527U,
			2372505648U,
			3111709047U,
			3501305188U,
			3579546635U,
			3576708825U,
			1814656577U,
			2817538364U,
			1838303971U,
			3720064278U,
			3665363890U,
			3055902566U,
			2894065833U,
			684454408U,
			2574239426U,
			2356500291U,
			760593886U,
			2487689012U,
			1055371587U,
			2187377123U,
			3384133044U,
			1983237197U,
			2945548840U,
			2506873373U,
			1486001576U,
			4118629641U,
			2169606425U,
			692889494U,
			647233376U,
			1762263018U,
			872342411U,
			758447195U,
			2810296474U,
			1938784644U,
			621610362U,
			2617991491U,
			3256603497U,
			1313312695U,
			2763818469U,
			3884687430U,
			1857973247U,
			3344459848U,
			687483426U,
			615512383U,
			3407102505U,
			3024463035U,
			4028243487U,
			2938239854U,
			651095589U,
			3275404974U,
			1749268070U,
			1069175328U,
			4147184007U,
			2571193852U,
			875727561U,
			1881816253U,
			19105434U,
			3047603590U,
			2247187002U,
			831284086U,
			2357930252U,
			1541942186U,
			2211385120U,
			2194965598U,
			2220496443U,
			63150622U,
			2742004783U,
			2751774662U,
			1285752367U,
			2020266060U,
			2870465490U,
			4275228183U,
			3865703976U,
			3788055889U,
			3014801916U,
			1055777351U,
			2136612789U,
			1868511780U,
			10920088U,
			843146587U,
			3811501880U,
			1062571287U,
			3445973849U,
			2698829277U,
			2769485601U,
			351863318U,
			2142861185U,
			4093438227U,
			1845745446U,
			2448890699U,
			914833906U,
			2023368099U,
			4266832164U,
			1932209068U,
			3050738818U,
			2123772977U,
			456568658U,
			699874819U,
			2758832778U,
			1558490932U,
			4158405023U,
			1088487489U,
			1528478279U,
			1544322944U,
			823414614U,
			1238282141U,
			3489292681U,
			2107156012U,
			3174384911U,
			452069677U,
			654032472U,
			2509449164U,
			1673926756U,
			4082019114U,
			71478945U,
			3896233234U,
			1254978430U,
			3304193503U,
			2443601026U,
			1875735342U,
			158543343U,
			37203334U,
			3215790195U,
			3203558607U,
			2484957181U,
			1772171270U,
			3228426790U,
			2133054393U,
			2097336409U,
			1564678005U,
			374404158U,
			226551346U,
			1743236296U,
			2004876075U,
			688026205U,
			796509941U,
			1811013646U,
			1469241022U,
			409353941U,
			2093575194U,
			3102241861U,
			4077160605U,
			3605572489U,
			657768808U,
			4225684379U,
			159444289U,
			1424697127U,
			4057162661U,
			1067577906U,
			1829972234U,
			2062662151U,
			3593975577U,
			829370853U,
			2549558820U,
			3864015520U,
			1287875011U,
			3069001218U,
			1870705829U,
			3153976141U,
			4037974813U,
			630206100U,
			2822017137U,
			3501471213U,
			2123012202U,
			1590208273U,
			1827847183U,
			3383940802U,
			1387257637U,
			3521366595U,
			2420429117U,
			1127244987U,
			1473045045U,
			520679585U,
			3150967615U,
			3784766549U,
			911607216U,
			2582666243U,
			2486129838U,
			3684963775U,
			3821627327U,
			300028892U,
			221499673U,
			3536696550U,
			3357352145U,
			3132416993U,
			2243255484U,
			2638356661U,
			1809473920U,
			1890288565U,
			482860267U,
			723586124U,
			1899440220U,
			2793427163U,
			3907153958U,
			1547843487U,
			1557490100U,
			3482865713U,
			3036961189U,
			615332917U,
			3881549116U,
			1055869661U,
			988442990U,
			1718510409U,
			1413441293U,
			17243099U,
			1344035059U,
			3464330991U,
			1064935939U,
			3128561038U,
			2659756879U,
			2939857631U,
			540611040U,
			589521147U,
			3239912104U,
			3923068763U,
			2860793659U,
			198666401U,
			628014680U,
			3557449920U,
			1871979071U,
			1504973927U,
			1427606455U,
			3322244560U,
			3172116052U,
			3208766811U,
			429350110U,
			1466482030U,
			2927153721U,
			3840525066U,
			182159442U,
			2776102932U,
			1385448934U,
			1422772411U,
			1860494156U,
			222905178U,
			3103073417U,
			4271309067U,
			626074012U,
			843202975U,
			770389868U,
			742362356U,
			2025736154U,
			1068507423U,
			3189106944U,
			1815486690U,
			3839241922U,
			940582517U,
			3347597948U,
			3312649236U,
			2309277904U,
			1165368199U,
			2683541118U,
			196473588U,
			745772546U,
			1845001504U,
			1773272449U,
			1686101526U,
			37128880U,
			868812499U,
			2915279679U,
			359938590U,
			2496183559U,
			3024886828U,
			2944217419U,
			2165793215U,
			912647032U,
			4243970500U,
			916416496U,
			1460768670U,
			15200116U,
			3998714420U,
			4254804528U,
			2135094904U,
			212981488U,
			1056192019U,
			592087456U,
			3454872508U,
			1542122065U,
			585493353U,
			1229171119U,
			1186052716U,
			1239392357U,
			1225789654U,
			689135304U,
			3689394276U,
			3822108987U,
			722987840U,
			829784663U,
			4111970019U,
			437817582U,
			1247751050U,
			3415359465U,
			2157583147U,
			2766254444U,
			3884682391U,
			2712547596U,
			1511002634U,
			576621672U,
			1017459400U,
			1636610939U,
			1012114696U,
			2198184564U,
			1063087976U,
			2454649816U,
			3416356260U,
			166190140U,
			3400634700U,
			1145960319U,
			3274334530U,
			1792262159U,
			3442140810U,
			2938938891U,
			3205544478U,
			1046749559U,
			1735592568U,
			859586622U,
			177497328U,
			3498111333U,
			3651533136U,
			811964516U,
			917147669U,
			1006615247U,
			3710511167U,
			2975431068U,
			4209290307U,
			3014379149U,
			3426630381U,
			1376104305U,
			3264593064U,
			1239643362U,
			79020428U,
			3864292870U,
			3681260686U,
			2463640458U,
			3876096392U,
			3386825551U,
			16296439U,
			2140200432U,
			3951801198U,
			1766651573U,
			3744556729U,
			2419402861U,
			2187468977U,
			613606233U,
			3567359113U,
			3799435660U,
			1075166945U,
			191956884U,
			4194742491U,
			2212347988U,
			4005859214U,
			3596218769U,
			1967766037U,
			2341805773U,
			1538019943U,
			2672819502U,
			759113354U,
			893620637U,
			3704539592U,
			1579439399U,
			1011633582U,
			787056687U,
			2810854810U,
			1250757054U,
			2643639523U,
			2849293464U,
			1713127538U,
			1273225065U,
			52503879U,
			580110243U,
			3588468U,
			146009622U,
			2060092671U,
			3526310219U,
			1925457148U,
			3392023585U,
			3230945339U,
			3707576870U,
			156396964U,
			1031241207U,
			1133317975U,
			3578371725U,
			845739U,
			196445148U,
			2159762155U,
			537083465U,
			2075404637U,
			1298545665U,
			4101649980U,
			2274957526U,
			1545063U,
			2217575512U,
			3336860067U,
			885798828U,
			3488197484U,
			3794611294U,
			1066397364U,
			4045721848U,
			2005785915U,
			3487944752U,
			4288228839U,
			1471562638U,
			1267157455U,
			1502916699U,
			803158067U,
			64167825U,
			2436768169U,
			3751442181U,
			2503889438U,
			1813204430U,
			627668531U,
			3595172688U,
			284218556U,
			3826972977U,
			1987873801U,
			3536542608U,
			173308185U,
			1490050057U,
			1197056655U,
			2148733405U,
			3206198905U,
			471650745U,
			3386067038U,
			669845175U,
			2519480597U,
			4125643537U,
			3496608077U,
			2531920622U,
			3437729030U,
			2048153931U,
			931475439U,
			1681432401U,
			870151167U,
			3439065372U,
			2393724573U,
			4064764989U,
			2339878176U,
			2690343396U,
			2729995301U,
			3444663896U,
			376145381U,
			2216722057U,
			3132112136U,
			1593763109U,
			149865799U,
			451540625U,
			4073802056U,
			3601147301U,
			2720583781U,
			2681744524U,
			1577048481U,
			3232105796U,
			838124189U,
			1182260985U,
			2200199135U,
			3446275141U,
			2779516058U,
			1534817429U,
			1618300592U,
			1896049108U,
			1461845414U,
			79065754U,
			1880078297U,
			4127179297U,
			804343027U,
			767529843U,
			177873560U,
			276994402U,
			4014859671U,
			1195475508U,
			4056801809U,
			863253654U,
			3568063334U,
			768140969U,
			3722847657U,
			2232810382U,
			1386521007U,
			1618343402U,
			3915472595U,
			3181373971U,
			1719531436U,
			1826118099U,
			671036114U,
			3486929582U,
			3682097055U,
			197097392U,
			486816144U,
			2321374394U,
			813297241U,
			2867378693U,
			1659974584U,
			2324001083U,
			3675590500U,
			518079201U,
			1505609551U,
			1718787954U,
			1986123298U,
			3622904087U,
			3141387849U,
			2605569517U,
			3973039188U,
			3884566968U,
			2812553844U,
			890836519U,
			87677767U,
			3595837097U,
			69518295U,
			2491977999U,
			1089850625U,
			3612067208U,
			2764838400U,
			2121394020U,
			1423696769U,
			384616780U,
			1432982824U,
			936626271U,
			19176950U,
			4211769515U,
			112215814U,
			1572851707U,
			1748593664U,
			3988675749U,
			3798682595U,
			2453955111U,
			2389601939U,
			1231841474U,
			1865149780U,
			767549233U,
			2859003720U,
			3313506667U,
			2046555525U,
			2023612051U,
			2110602862U,
			3606696928U,
			3112514035U,
			636948007U,
			1292591341U,
			258417710U,
			3087190458U,
			2949217761U,
			3570269215U,
			3114115900U,
			2690223253U,
			1016258430U,
			2085381282U,
			1372386344U,
			2780047883U,
			1850396460U,
			745226643U,
			1003382460U,
			351008717U,
			3320108312U,
			351952757U,
			3129747932U,
			4162380101U,
			3217420278U,
			94752346U,
			1198153962U,
			939638159U,
			2013656904U,
			2136243921U,
			3281658894U,
			1356292679U,
			1126925823U,
			3481879532U,
			499516766U,
			377801781U,
			3701083542U,
			1108105060U,
			2426351462U,
			3161485404U,
			1967205944U,
			2354033312U,
			3800650990U,
			3538678150U,
			2046749915U,
			1337806366U,
			766984956U,
			2124873499U,
			2391573032U,
			3822585406U,
			4053492422U,
			1997180024U,
			3507407506U,
			398028675U,
			1501721306U,
			3106787707U,
			4264558217U,
			816779686U,
			265766865U,
			474908676U,
			3712340070U,
			4178990071U,
			1763251309U,
			2454591076U,
			189521826U,
			3697420287U,
			1925431767U,
			1089800386U,
			2345811071U,
			3900347957U,
			834787364U,
			658683468U,
			571329902U,
			4060808101U,
			1068070342U,
			426143012U,
			569273625U,
			555709680U,
			3020591434U,
			377887120U,
			2201894209U,
			494369011U,
			2555956314U,
			2759417977U,
			84522920U,
			197073820U,
			3308700437U,
			4133844923U,
			2992523956U,
			1891927855U,
			599871357U,
			3097464086U,
			2986046804U,
			2151394868U,
			464976587U,
			3826016327U,
			1674763502U,
			2240371818U,
			1036246599U,
			210473590U,
			2845508928U,
			1753173018U,
			2530390620U,
			845695379U,
			2570504278U,
			3032976111U,
			3740108U,
			2167992582U,
			2729247105U,
			801484836U,
			391047785U,
			838273748U,
			548893203U,
			1268093666U,
			1943809712U,
			3693049905U,
			910965218U,
			4166401485U,
			3861447901U,
			1989496771U,
			2880029606U,
			3515406152U,
			452277352U,
			2221447143U,
			262448305U,
			1288821605U,
			1963689469U,
			2917423463U,
			4195538586U,
			252303635U,
			33010362U,
			4234239050U,
			3389248303U,
			2177422242U,
			2760516220U,
			690525749U,
			2839048222U,
			4098320458U,
			2106606072U,
			2492614609U,
			394527035U,
			3469842529U,
			3272924878U,
			1369145676U,
			820732494U,
			2258415613U,
			1855669187U,
			4265916428U,
			3748750299U,
			3261107957U,
			1065070803U,
			508664782U,
			2627432132U,
			3432102296U,
			3811234647U,
			1593049874U,
			3881185076U,
			41234006U,
			1972579742U,
			4162034186U,
			3393403442U,
			999557569U,
			2734050032U,
			3476581350U,
			1020306041U,
			2938272728U,
			922539738U,
			898360567U,
			2820941906U,
			46676568U,
			2050000058U,
			46622322U,
			329386798U,
			2166903380U,
			3918199145U,
			2208445234U,
			3507688761U,
			3374575730U,
			2104858725U,
			1382167107U,
			19658344U,
			122353053U,
			145978993U,
			1969987893U,
			3107918147U,
			3663151155U,
			1638547043U,
			1002452917U,
			186494571U,
			1876187170U,
			2930792517U,
			1398330446U,
			2860079726U,
			2959706948U,
			735530277U,
			3349758867U,
			1971392111U,
			72591365U,
			1284026272U,
			1170682462U,
			120983655U,
			3314121509U,
			3943716059U,
			109898732U,
			3999726206U,
			2463152321U,
			1659672618U,
			3399240321U,
			933449126U,
			122243387U,
			2672304799U,
			3320264711U,
			2140067140U,
			2397661987U,
			1101657111U,
			777930835U,
			829474156U,
			2547718719U,
			3733259189U,
			3966820247U,
			148738782U,
			2996158180U,
			3229566559U,
			4074084187U,
			523597066U,
			510379782U,
			1995150638U,
			3272322691U,
			704602735U,
			2579400224U,
			1599662791U,
			4112470371U,
			920579947U,
			478250690U,
			2645317906U,
			994726244U,
			2157156553U,
			2544985411U,
			3323987459U,
			3755719691U,
			23798757U,
			3307941232U,
			2275882479U,
			666685205U,
			3575062134U,
			383257890U,
			1366858029U,
			2661227717U,
			957199591U,
			143078314U,
			1762258211U,
			3668713002U,
			697378227U,
			1369612462U,
			3836359027U,
			2471787122U,
			3874263052U,
			2580933681U,
			1109651746U,
			3303528674U,
			1277787988U,
			2583068993U,
			765594811U,
			1864011800U,
			3794832489U,
			3428543777U,
			3819183757U,
			2021240826U,
			1503463263U,
			3950237304U,
			643816145U,
			3495080149U,
			2257242657U,
			160231541U,
			2537924629U,
			1209625790U,
			885999020U,
			2743897158U,
			2496342365U,
			1331087192U,
			21410942U,
			1255853340U,
			3542445943U,
			1535218431U,
			2642251068U,
			1368177132U,
			1132461194U,
			1066791884U,
			709239351U,
			3941481908U,
			141349952U,
			1855447636U,
			2582249838U,
			1126477500U,
			150485275U,
			3764908057U,
			270025962U,
			2644029820U,
			2359691058U,
			2804140U,
			2325482111U,
			2129604056U,
			3873738057U,
			2082913086U,
			1107533514U,
			4227425759U,
			3471063483U,
			2862535518U,
			440016755U,
			3340426488U,
			806886363U,
			1260922593U,
			1744146469U,
			2638943148U,
			2938184241U,
			1364581408U,
			608665783U,
			1738214947U,
			1032444254U,
			2074175164U,
			3270708432U,
			1244428311U,
			4191781920U,
			4129390949U,
			2033367542U,
			3739370637U,
			1405126902U,
			176464671U,
			2102451051U,
			3921265618U,
			3240146011U,
			919422137U,
			3033514873U,
			1454618060U,
			957580054U,
			3295507383U,
			3169852128U,
			148824177U,
			3045426989U,
			2356860361U,
			3312615610U,
			3747061134U,
			806191421U,
			1646428430U,
			3128694230U,
			1070783909U,
			2474500208U,
			3773208395U,
			950693813U,
			1518037360U,
			3169846303U,
			2986179586U,
			1330194038U,
			2438290978U,
			3878331919U,
			1604020610U,
			3840496394U,
			2160484817U,
			608299298U,
			136429924U,
			950697899U,
			2791412871U,
			3529400822U,
			2466013231U,
			1547504396U,
			190759447U,
			446743323U,
			1120007534U,
			1091833475U,
			2070833053U,
			2170747955U,
			831426790U,
			3773838943U,
			1986746947U,
			952712026U,
			2049063066U,
			3406465775U,
			2880226148U,
			3344710084U,
			2556384044U,
			2779705172U,
			3851722576U,
			3236279907U,
			3048209964U,
			3358989285U,
			1357655268U,
			2509307662U,
			4231801679U,
			401259100U,
			419407611U,
			2035513267U,
			2786460597U,
			1123124361U,
			1443906892U,
			3295964658U,
			3644101299U,
			754030303U,
			3955692522U,
			4062925350U,
			4034508510U,
			1754467658U,
			122961081U,
			460833608U,
			3412054974U,
			2667381111U,
			1143255440U,
			4104054797U,
			1488603492U,
			2663924904U,
			263323938U,
			1039961151U,
			4082837552U,
			1981238634U,
			2547771126U,
			3982869744U,
			2447747796U,
			1025949519U,
			2923214565U,
			1553540266U,
			3615297693U,
			2466769231U,
			478163033U,
			3808010946U,
			53427231U,
			348205597U,
			3042446181U,
			4070727422U,
			1173308141U,
			859161316U,
			2245175897U,
			4102231325U,
			3549358658U,
			2630521419U,
			3943828483U,
			2047186533U,
			1060842397U,
			2501376340U,
			538648697U,
			3080581461U,
			1057475591U,
			284649695U,
			3859058418U,
			2848067568U,
			2969479927U,
			536546293U,
			2189499507U,
			26185465U,
			2766239165U,
			3616058375U,
			3630326085U,
			245985658U,
			681506477U,
			2871106321U,
			2518022625U,
			1036145078U,
			542399295U,
			305512811U,
			2642157019U,
			813834313U,
			2930383883U,
			2944036055U,
			2168531683U,
			3459905836U,
			679492861U,
			56933369U,
			3587868410U,
			1025274214U,
			3654745366U,
			824580817U,
			1506615287U,
			2415815456U,
			3186592550U,
			935696468U,
			1721755087U,
			3075529724U,
			2062669247U,
			3667665343U,
			1604511359U,
			615364897U,
			3849938144U,
			3593152846U,
			3397503619U,
			2536172912U,
			1549678134U,
			2137460493U,
			3868739995U,
			4264781157U,
			3397311354U,
			770121610U,
			3081139092U,
			4173006934U,
			2673327162U,
			700043683U,
			213833983U,
			4117502306U,
			3874614613U,
			1796614921U,
			1563627366U,
			1368621696U,
			3864777033U,
			1607175023U,
			3377887413U,
			961848936U,
			4151056783U,
			2884040737U,
			3695239436U,
			1253655287U,
			3239307825U,
			1845406502U,
			105787310U,
			2214420933U,
			457138116U,
			2296036893U,
			1449891868U,
			3910310370U,
			3931032989U,
			2723161204U,
			1521602635U,
			3000732852U,
			2413247415U,
			3944970990U,
			365231996U,
			2624870831U,
			73969487U,
			1856497107U,
			14800169U,
			4193553137U,
			3811175523U,
			652766297U,
			1545340085U,
			794215010U,
			1673076506U,
			110596863U,
			720313269U,
			1010979786U,
			172529706U,
			899298872U,
			4286648419U,
			3304575105U,
			660357921U,
			3199790773U,
			2983419905U,
			1178919762U,
			220833459U,
			2414081858U,
			1978505440U,
			817700013U,
			3024029275U,
			1669454452U,
			2684793453U,
			3743815774U,
			2468335161U,
			4142828305U,
			594835800U,
			1579735505U,
			4235313191U,
			240865715U,
			548956724U,
			2972414886U,
			2562405913U,
			2575788389U,
			1468517085U,
			964457633U,
			2280215809U,
			3590960811U,
			1017788269U,
			2160089304U,
			2760663894U,
			3964914108U,
			21522635U,
			4083164007U,
			750516749U,
			3752431858U,
			2725482765U,
			339470385U,
			1575045163U,
			496875024U,
			3199803578U,
			2135235466U,
			2379022177U,
			448951505U,
			962044283U,
			1764664958U,
			642069701U,
			1045091788U,
			3290438373U,
			3117169069U,
			2669094481U,
			3839706536U,
			2203096186U,
			2666684473U,
			4266010357U,
			517458793U,
			359748673U,
			1838074199U,
			2007566308U,
			1069805187U,
			462950332U,
			1232491542U,
			3526794101U,
			1461522278U,
			2118886025U,
			1462450908U,
			1397510528U,
			782955687U,
			572176367U,
			320664742U,
			3992468868U,
			1214558982U,
			1140566349U,
			1207649309U,
			111505997U,
			2791223402U,
			3433579177U,
			50509566U,
			2778204145U,
			509850685U,
			583136901U,
			4129071700U,
			3143207262U,
			2322110514U,
			1257358856U,
			2810997441U,
			2627077756U,
			4077196660U,
			435933858U,
			870702085U,
			2040957455U,
			1431019680U,
			776049938U,
			3054214207U,
			445913714U,
			3622807966U,
			3003370518U,
			4101254229U,
			3216131810U,
			843417465U,
			579292361U,
			47655712U,
			1865465559U,
			1184273603U,
			2157923842U,
			2654590483U,
			3917003613U,
			3559446670U,
			2413447805U,
			2966009684U,
			2180817237U,
			1959140506U,
			2377827602U,
			2975658695U,
			528516041U,
			111502091U,
			3448590059U,
			573998161U,
			1564308637U,
			1382403160U,
			848583088U,
			864081194U,
			3944438107U,
			757133055U,
			3541533118U,
			12905630U,
			"Not showing all elements because this array is too big (27216 elements)"
		};
		uint[] array2 = new uint[16];
		uint num2 = 1458347838U;
		int num3 = 0;
		byte[] array4;
		for (;;)
		{
			IL_EB:
			int num4 = (num3 >= 16) ? 285018829 : -1214991251;
			for (;;)
			{
				int num5 = num4;
				uint num6;
				switch ((num6 = (uint)(-(-(num5 * -542647945)) * 601476123)) % 14U)
				{
				case 0U:
				{
					int num7;
					num7 += 16;
					num4 = (int)(num6 * 528366774U ^ 2787242497U);
					continue;
				}
				case 1U:
				{
					int num7 = 0;
					int num8 = 0;
					uint[] array3 = new uint[16];
					array4 = new byte[num * 4U];
					num4 = (int)(num6 * 1898030059U ^ 2379111402U);
					continue;
				}
				case 2U:
				{
					uint[] array3;
					array3[0] = (array3[0] ^ array2[0]);
					array3[1] = (array3[1] ^ array2[1]);
					array3[2] = (array3[2] ^ array2[2]);
					array3[3] = (array3[3] ^ array2[3]);
					array3[4] = (array3[4] ^ array2[4]);
					array3[5] = (array3[5] ^ array2[5]);
					array3[6] = (array3[6] ^ array2[6]);
					array3[7] = (array3[7] ^ array2[7]);
					array3[8] = (array3[8] ^ array2[8]);
					array3[9] = (array3[9] ^ array2[9]);
					array3[10] = (array3[10] ^ array2[10]);
					array3[11] = (array3[11] ^ array2[11]);
					array3[12] = (array3[12] ^ array2[12]);
					array3[13] = (array3[13] ^ array2[13]);
					array3[14] = (array3[14] ^ array2[14]);
					array3[15] = (array3[15] ^ array2[15]);
					int num9 = 0;
					num4 = (int)(num6 * 2459166675U ^ 1322014783U);
					continue;
				}
				case 3U:
					num2 ^= num2 >> 12;
					num2 ^= num2 << 25;
					num2 ^= num2 >> 27;
					array2[num3] = num2;
					num3++;
					num4 = 1286503642;
					continue;
				case 4U:
					num4 = -1214991251;
					continue;
				case 5U:
				{
					int num7;
					num4 = (((long)num7 >= (long)((ulong)num)) ? -2070020682 : 1069526610);
					continue;
				}
				case 6U:
					goto IL_EB;
				case 7U:
				{
					uint[] array3;
					int num9;
					uint num10 = array3[num9];
					int num8;
					array4[num8++] = (byte)num10;
					array4[num8++] = (byte)(num10 >> 8);
					array4[num8++] = (byte)(num10 >> 16);
					array4[num8++] = (byte)(num10 >> 24);
					array2[num9] ^= num10;
					num9++;
					num4 = -2056207447;
					continue;
				}
				case 8U:
				{
					int num11 = 0;
					num4 = 1669251832;
					continue;
				}
				case 9U:
				{
					int num7;
					uint[] array3;
					int num11;
					array3[num11] = array[num7 + num11];
					num4 = 386198507;
					continue;
				}
				case 11U:
				{
					int num9;
					num4 = ((num9 >= 16) ? -587569540 : -549916215);
					continue;
				}
				case 12U:
				{
					int num11;
					num4 = ((num11 < 16) ? 682190123 : -1603458534);
					continue;
				}
				case 13U:
				{
					int num11;
					num11++;
					num4 = (int)(num6 * 3570680287U ^ 2696789065U);
					continue;
				}
				}
				goto Block_1;
			}
		}
		Block_1:
		<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E = <Module>.\u206B\u202C\u206C\u206A\u206A\u200B\u206E\u206D\u200B\u206D\u202D\u202D\u202E\u202A\u200E\u206E\u206F\u200B\u200E\u206E\u200C\u200F\u206B\u202A\u200C\u206E\u206C\u206E\u200E\u200C\u200C\u206B\u202B\u200F\u200E\u200E\u206F\u200F\u200D\u200D\u202E(array4);
	}

	// Token: 0x06000018 RID: 24 RVA: 0x00026904 File Offset: 0x00024B04
	internal static - \u206C\u206C\u206B\u200E\u200D\u206E\u206D\u202E\u202E\u206D\u206F\u200C\u202C\u206B\u200B\u206D\u200D\u202E\u202E\u200D\u200C\u206D\u200D\u202C\u206F\u200B\u202A\u200F\u206E\u206C\u206C\u202D\u200B\u206B\u200B\u206C\u202A\u206D\u206A\u200F\u202E<->(int id)
	{
		if (Assembly.GetExecutingAssembly().Equals(Assembly.GetCallingAssembly()))
		{
			for (;;)
			{
				IL_14:
				int num = 1559919201;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)((num2 - (-1909667015 + -(1579139753 + -354987058)) - ~(-897926076 + -1162874800) - (-522689085 ^ -176497648)) * 1141009415)) % 10U)
					{
					case 0U:
					{
						int num4;
						num = ((num4 != 0) ? 107802634 : 1587970067);
						continue;
					}
					case 2U:
					{
						int count = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						- result = (-)((object)string.Intern(Encoding.UTF8.GetString(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 4, count)));
						num = (int)(num3 * 2546131689U ^ 2701457344U);
						continue;
					}
					case 3U:
						goto IL_14;
					case 4U:
					{
						- result = default(-);
						num = 778380404;
						continue;
					}
					case 5U:
					{
						id = (id * 1976633049 ^ -582812680);
						int num4 = (int)((uint)id >> 30);
						id = (id & 1073741823) << 2;
						num = (int)(((num4 != 1) ? 2853551829U : 2829380443U) ^ num3 * 1017813905U);
						continue;
					}
					case 6U:
					{
						- result;
						return result;
					}
					case 7U:
					{
						-[] array = new -[1];
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id, array, 0, sizeof(-));
						- result = array[0];
						num = (int)(num3 * 3973500265U ^ 3508730361U);
						continue;
					}
					case 8U:
					{
						int num4;
						num = ((num4 == 3) ? -1382413357 : 856106746);
						continue;
					}
					case 9U:
					{
						int num5 = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						int length = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 4] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 5] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 6] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 7] << 24;
						Array array2 = Array.CreateInstance(typeof(-).GetElementType(), length);
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 8, array2, 0, num5 - 4);
						- result = (-)((object)array2);
						num = (int)(num3 * 2674008251U ^ 1465983891U);
						continue;
					}
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		return default(-);
	}

	// Token: 0x06000019 RID: 25 RVA: 0x00026B6C File Offset: 0x00024D6C
	internal static - \u206F\u202C\u206B\u202A\u202D\u200D\u200E\u200D\u200F\u200B\u202D\u202A\u202B\u206C\u202B\u206F\u206F\u200C\u202A\u206B\u202C\u202B\u202D\u202D\u206A\u202B\u202D\u200D\u200F\u206C\u200F\u200D\u200E\u206E\u202C\u206D\u200D\u206C\u200E\u202D\u202E<->(int id)
	{
		if (Assembly.GetExecutingAssembly().Equals(Assembly.GetCallingAssembly()))
		{
			for (;;)
			{
				IL_14:
				int num = -1728099607;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)((~(num2 + (785400883 - 485719594)) ^ ~2014633633) + 1763977640)) % 10U)
					{
					case 0U:
					{
						int count = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						- result = (-)((object)string.Intern(Encoding.UTF8.GetString(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 4, count)));
						num = (int)(num3 * 2972976087U ^ 1393946561U);
						continue;
					}
					case 1U:
					{
						int num4;
						num = ((num4 != 0) ? -2027373474 : -1931203065);
						continue;
					}
					case 2U:
						goto IL_14;
					case 3U:
					{
						id = (id * 513845061 ^ 1138125536);
						int num4 = (int)((uint)id >> 30);
						id = (id & 1073741823) << 2;
						num = (int)(((num4 == 3) ? 644868687U : 1362632863U) ^ num3 * 3995988393U);
						continue;
					}
					case 4U:
					{
						-[] array = new -[1];
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id, array, 0, sizeof(-));
						- result = array[0];
						num = (int)(num3 * 3930559909U ^ 202942371U);
						continue;
					}
					case 5U:
					{
						int num5 = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						int length = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 4] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 5] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 6] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 7] << 24;
						Array array2 = Array.CreateInstance(typeof(-).GetElementType(), length);
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 8, array2, 0, num5 - 4);
						- result = (-)((object)array2);
						num = (int)(num3 * 4020929753U ^ 1481284452U);
						continue;
					}
					case 6U:
					{
						- result = default(-);
						num = -1218578411;
						continue;
					}
					case 8U:
					{
						int num4;
						num = ((num4 != 1) ? -1503804615 : 1553323502);
						continue;
					}
					case 9U:
					{
						- result;
						return result;
					}
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		return default(-);
	}

	// Token: 0x0600001A RID: 26 RVA: 0x00026DB8 File Offset: 0x00024FB8
	internal static - \u202A\u200D\u206F\u200C\u200E\u202E\u206B\u206C\u206E\u202A\u206D\u200E\u200F\u200F\u200E\u200E\u202C\u202C\u202C\u200E\u206B\u200C\u202C\u202E\u206C\u202C\u202E\u200E\u202C\u200D\u202A\u202D\u206C\u202B\u206D\u202B\u206C\u202A\u206D\u206C\u202E<->(int id)
	{
		if (Assembly.GetExecutingAssembly().Equals(Assembly.GetCallingAssembly()))
		{
			for (;;)
			{
				IL_14:
				int num = 705949970;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(1868123022 - (~(num2 ^ 974340033 - 374278359 + --459577557 - (1470165109 - -344661045 ^ ~-2063470440)) ^ 1362499170 - -1482025309))) % 10U)
					{
					case 0U:
					{
						-[] array = new -[1];
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id, array, 0, sizeof(-));
						- result = array[0];
						num = (int)(num3 * 1993291454U ^ 4058580317U);
						continue;
					}
					case 1U:
					{
						int num4;
						num = ((num4 == 0) ? 375429753 : -1852365920);
						continue;
					}
					case 2U:
					{
						int num4;
						num = ((num4 == 3) ? 1213661154 : 1745600619);
						continue;
					}
					case 3U:
					{
						int num5 = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						int length = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 4] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 5] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 6] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 7] << 24;
						Array array2 = Array.CreateInstance(typeof(-).GetElementType(), length);
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 8, array2, 0, num5 - 4);
						- result = (-)((object)array2);
						num = (int)(num3 * 3139047905U ^ 2372675130U);
						continue;
					}
					case 5U:
					{
						- result;
						return result;
					}
					case 6U:
					{
						id = (id * -579077263 ^ 451538839);
						int num4 = (int)((uint)id >> 30);
						id = (id & 1073741823) << 2;
						num = (int)(((num4 != 1) ? 2942187252U : 3435840843U) ^ num3 * 1854216363U);
						continue;
					}
					case 7U:
					{
						int count = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						- result = (-)((object)string.Intern(Encoding.UTF8.GetString(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 4, count)));
						num = (int)(num3 * 1685063415U ^ 2891473988U);
						continue;
					}
					case 8U:
					{
						- result = default(-);
						num = 502878553;
						continue;
					}
					case 9U:
						goto IL_14;
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		return default(-);
	}

	// Token: 0x0600001B RID: 27 RVA: 0x00027024 File Offset: 0x00025224
	internal static - \u206B\u206C\u202A\u200B\u202D\u206F\u202C\u202C\u200B\u200C\u200B\u200D\u202C\u202E\u202E\u200F\u206A\u200F\u202E\u200D\u206E\u200B\u206A\u202B\u206C\u202A\u200C\u200E\u206D\u200C\u206F\u202C\u202A\u206C\u206A\u200C\u206C\u202A\u200D\u202E<->(int id)
	{
		if (Assembly.GetExecutingAssembly().Equals(Assembly.GetCallingAssembly()))
		{
			for (;;)
			{
				IL_14:
				int num = -1894510050;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(~(uint)((-(-(~1115184274)) - num2 - 2067602891) * 498743269))) % 10U)
					{
					case 0U:
					{
						- result = default(-);
						num = 629266455;
						continue;
					}
					case 2U:
					{
						- result;
						return result;
					}
					case 3U:
					{
						int count = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						- result = (-)((object)string.Intern(Encoding.UTF8.GetString(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 4, count)));
						num = (int)(num3 * 4053021421U ^ 1087056336U);
						continue;
					}
					case 4U:
					{
						int num4;
						num = ((num4 == 1) ? 1180938961 : 1988687153);
						continue;
					}
					case 5U:
					{
						id = (id * 1793884093 ^ -997901719);
						int num4 = (int)((uint)id >> 30);
						id = (id & 1073741823) << 2;
						num = (int)(((num4 != 2) ? 1816660525U : 1023885235U) ^ num3 * 1588516463U);
						continue;
					}
					case 6U:
					{
						int num5 = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						int length = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 4] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 5] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 6] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 7] << 24;
						Array array = Array.CreateInstance(typeof(-).GetElementType(), length);
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 8, array, 0, num5 - 4);
						- result = (-)((object)array);
						num = (int)(num3 * 878176472U ^ 2248122471U);
						continue;
					}
					case 7U:
						goto IL_14;
					case 8U:
					{
						-[] array2 = new -[1];
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id, array2, 0, sizeof(-));
						- result = array2[0];
						num = (int)(num3 * 3486654715U ^ 4153074663U);
						continue;
					}
					case 9U:
					{
						int num4;
						num = ((num4 != 3) ? 448358537 : -178056161);
						continue;
					}
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		return default(-);
	}

	// Token: 0x0600001C RID: 28 RVA: 0x0002726C File Offset: 0x0002546C
	internal static - \u200C\u202C\u206D\u206C\u206C\u200B\u200D\u200E\u202B\u202A\u206F\u206B\u206B\u206A\u202D\u202B\u202E\u202E\u206B\u206D\u202E\u206B\u206B\u200E\u202C\u200E\u200F\u206F\u202D\u202B\u200C\u202B\u202E\u200E\u206B\u202C\u206F\u202E\u200D\u200F\u202E<->(int id)
	{
		if (Assembly.GetExecutingAssembly().Equals(Assembly.GetCallingAssembly()))
		{
			for (;;)
			{
				IL_14:
				int num = 1251534160;
				for (;;)
				{
					int num2 = num;
					uint num3;
					switch ((num3 = (uint)(-(uint)(~(uint)(-(uint)(num2 ^ -((251149710 ^ -1695417869) - --862099537)))))) % 10U)
					{
					case 0U:
					{
						- result = default(-);
						num = 594869080;
						continue;
					}
					case 1U:
					{
						id = (id * 1184036759 ^ -253552170);
						int num4 = (int)((uint)id >> 30);
						id = (id & 1073741823) << 2;
						num = (int)(((num4 == 1) ? 866203031U : 492322189U) ^ num3 * 1610160255U);
						continue;
					}
					case 2U:
					{
						int num4;
						num = ((num4 == 0) ? 1635952391 : 1467899663);
						continue;
					}
					case 3U:
						goto IL_14;
					case 4U:
					{
						-[] array = new -[1];
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id, array, 0, sizeof(-));
						- result = array[0];
						num = (int)(num3 * 97279035U ^ 3709863150U);
						continue;
					}
					case 5U:
					{
						- result;
						return result;
					}
					case 7U:
					{
						int num4;
						num = ((num4 == 2) ? 27626987 : 202109475);
						continue;
					}
					case 8U:
					{
						int num5 = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						int length = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 4] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 5] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 6] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 7] << 24;
						Array array2 = Array.CreateInstance(typeof(-).GetElementType(), length);
						Buffer.BlockCopy(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 8, array2, 0, num5 - 4);
						- result = (-)((object)array2);
						num = (int)(num3 * 312183804U ^ 655622160U);
						continue;
					}
					case 9U:
					{
						int count = (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id] | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 1] << 8 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 2] << 16 | (int)<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E[id + 3] << 24;
						- result = (-)((object)string.Intern(Encoding.UTF8.GetString(<Module>.\u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E, id + 4, count)));
						num = (int)(num3 * 1826381322U ^ 842733266U);
						continue;
					}
					}
					goto Block_1;
				}
			}
			Block_1:;
		}
		return default(-);
	}

	// Token: 0x04000001 RID: 1
	internal static byte[] \u206B\u200B\u206F\u202A\u206A\u202D\u202A\u206E\u200F\u200B\u206A\u206A\u206F\u200D\u200D\u200F\u202D\u200B\u200E\u206D\u200D\u200B\u200D\u202C\u200F\u202B\u200C\u202D\u202E\u206B\u206D\u200D\u200D\u206E\u202B\u202D\u200D\u200E\u202B\u202A\u202E;

	// Token: 0x04000002 RID: 2 RVA: 0x00002048 File Offset: 0x00000248
	internal static <Module>.\u206E\u200C\u206E\u202D\u206A\u206F\u200F\u202A\u202D\u202A\u202A\u202D\u206F\u200E\u200F\u206B\u200F\u200E\u206A\u202B\u202A\u206E\u206D\u206C\u206F\u200E\u200C\u202B\u202E\u200F\u200B\u202A\u202C\u202D\u206B\u200E\u206F\u206E\u202E\u202E \u202B\u200E\u206A\u202E\u206E\u202B\u206C\u200F\u206C\u206C\u200E\u200B\u206D\u202C\u200F\u206C\u202E\u200C\u200E\u200E\u202C\u202C\u206D\u202B\u206E\u206E\u200B\u206E\u200B\u200B\u202C\u202E\u202D\u202E\u202B\u206E\u200E\u202A\u200D\u200C\u202E;

	// Token: 0x02000002 RID: 2
	internal struct \u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E
	{
		// Token: 0x0600001D RID: 29 RVA: 0x000274B8 File Offset: 0x000256B8
		internal void \u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E()
		{
			this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E = 1024U;
		}

		// Token: 0x0600001E RID: 30 RVA: 0x000274D0 File Offset: 0x000256D0
		internal uint \u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder)
		{
			uint num = (rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E >> 11) * this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E;
			if (rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E < num)
			{
				goto IL_1D;
			}
			goto IL_10A;
			int num3;
			for (;;)
			{
				IL_22:
				int num2 = num3;
				uint num4;
				switch ((num4 = (uint)((num2 ^ 351530028 + ~(-569870780)) - -(2131247658 ^ 865243474) - -650401887 * 173520273 - 1861542490)) % 7U)
				{
				case 0U:
					goto IL_10A;
				case 2U:
					return 0U;
				case 3U:
					rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = (rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E << 8 | (uint)((byte)rangeDecoder.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E.ReadByte()));
					rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E <<= 8;
					num3 = (int)(num4 * 3650347358U ^ 3887469158U);
					continue;
				case 4U:
					rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = (rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E << 8 | (uint)((byte)rangeDecoder.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E.ReadByte()));
					rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E <<= 8;
					num3 = (int)(num4 * 55723669U ^ 1427585281U);
					continue;
				case 5U:
					goto IL_1D;
				case 6U:
					rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E = num;
					this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E += 2048U - this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E >> 5;
					num3 = (int)(((rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E >= 16777216U) ? 3811943955U : 3383273602U) ^ num4 * 2735704175U);
					continue;
				}
				break;
			}
			return 1U;
			IL_1D:
			num3 = 613728139;
			goto IL_22;
			IL_10A:
			rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E -= num;
			rangeDecoder.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E -= num;
			this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E -= this.\u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E >> 5;
			num3 = ((rangeDecoder.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E >= 16777216U) ? -261868750 : 555576413);
			goto IL_22;
		}

		// Token: 0x04000003 RID: 3
		internal uint \u202A\u206B\u200F\u206B\u202A\u206E\u202D\u206B\u202C\u202D\u200C\u206F\u200C\u200E\u200E\u206C\u200B\u200B\u202B\u200D\u206A\u200E\u200E\u206A\u206C\u206A\u206D\u202D\u206D\u206B\u206A\u206A\u202A\u206C\u200D\u202B\u206C\u206B\u202D\u200E\u202E;
	}

	// Token: 0x02000003 RID: 3
	internal struct \u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E
	{
		// Token: 0x0600001F RID: 31 RVA: 0x00027678 File Offset: 0x00025878
		internal \u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(int numBitLevels)
		{
			this.\u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E = numBitLevels;
			this.\u200B\u206D\u206C\u200B\u200E\u200D\u206B\u206C\u206D\u202E\u206D\u200D\u206A\u206D\u200B\u206B\u200E\u200E\u206B\u202D\u206C\u206C\u200B\u206A\u200D\u202D\u202E\u202E\u200C\u200F\u202E\u200C\u200E\u202B\u206E\u202C\u202A\u206A\u200C\u206B\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[1 << numBitLevels];
		}

		// Token: 0x06000020 RID: 32 RVA: 0x000276A0 File Offset: 0x000258A0
		internal void \u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E()
		{
			uint num = 1U;
			for (;;)
			{
				IL_64:
				int num2 = ((ulong)num < (ulong)(1L << (this.\u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E & 31))) ? 1628770953 : 1169617095;
				for (;;)
				{
					int num3 = num2;
					switch ((-(num3 - -1272463115 * (-1102890376 * 1791734651) - ~(-158253151)) ^ 1099790554) % 4)
					{
					case 0:
						num2 = 1628770953;
						continue;
					case 2:
						goto IL_64;
					case 3:
						this.\u200B\u206D\u206C\u200B\u200E\u200D\u206B\u206C\u206D\u202E\u206D\u200D\u206A\u206D\u200B\u206B\u200E\u200E\u206B\u202D\u206C\u206C\u200B\u206A\u200D\u202D\u202E\u202E\u200C\u200F\u202E\u200C\u200E\u202B\u206E\u202C\u202A\u206A\u200C\u206B\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						num += 1U;
						num2 = 1690767294;
						continue;
					}
					return;
				}
			}
		}

		// Token: 0x06000021 RID: 33 RVA: 0x00027734 File Offset: 0x00025934
		internal uint \u200B\u202B\u200F\u206E\u206E\u200C\u202A\u206A\u202D\u206A\u200D\u200B\u206D\u202D\u206D\u202E\u200E\u200F\u206B\u206A\u206A\u202E\u206B\u206A\u202E\u200E\u206F\u202D\u206A\u202C\u200C\u200E\u200C\u200F\u206B\u202B\u202C\u200E\u206F\u202D\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder)
		{
			uint num = 1U;
			int num2 = this.\u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E;
			for (;;)
			{
				IL_9A:
				int num3 = (num2 <= 0) ? 708768016 : 2114801277;
				for (;;)
				{
					int num4 = num3;
					switch ((~1037409890 - ((num4 ^ (-1103934594 - -1579881324 ^ 1084170786 + 157387133) + ~(750137523 - -117871525)) + (-491564066 ^ -1152069538 ^ ~-786312572))) * -1489367143 % 4)
					{
					case 0:
						num3 = 2114801277;
						continue;
					case 1:
						num = (num << 1) + this.\u200B\u206D\u206C\u200B\u200E\u200D\u206B\u206C\u206D\u202E\u206D\u200D\u206A\u206D\u200B\u206B\u200E\u200E\u206B\u202D\u206C\u206C\u200B\u206A\u200D\u202D\u202E\u202E\u200C\u200F\u202E\u200C\u200E\u202B\u206E\u202C\u202A\u206A\u200C\u206B\u202E[(int)num].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder);
						num2--;
						num3 = 624605251;
						continue;
					case 3:
						goto IL_9A;
					}
					goto Block_1;
				}
			}
			Block_1:
			return num - (1U << this.\u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E);
		}

		// Token: 0x06000022 RID: 34 RVA: 0x00027800 File Offset: 0x00025A00
		internal uint \u200B\u200F\u200C\u200B\u202B\u200D\u206B\u200F\u206E\u200B\u206B\u206B\u200B\u202A\u202D\u206B\u200C\u206D\u200E\u200C\u206E\u200E\u200F\u202E\u202A\u206A\u206F\u206D\u202D\u202D\u200D\u202D\u206B\u206B\u200C\u206F\u200C\u206D\u202D\u206E\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder)
		{
			uint num = 1U;
			uint num2 = 0U;
			int num3 = 0;
			for (;;)
			{
				IL_7F:
				int num4 = (num3 < this.\u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E) ? 1363881680 : 1489788534;
				for (;;)
				{
					int num5 = num4;
					switch (~((num5 ^ -(864749317 * --1593436665)) + -(1384712146 + 1282250344) - ~1098708812) % 4)
					{
					case 0:
						goto IL_7F;
					case 1:
					{
						uint num6 = this.\u200B\u206D\u206C\u200B\u200E\u200D\u206B\u206C\u206D\u202E\u206D\u200D\u206A\u206D\u200B\u206B\u200E\u200E\u206B\u202D\u206C\u206C\u200B\u206A\u200D\u202D\u202E\u202E\u200C\u200F\u202E\u200C\u200E\u202B\u206E\u202C\u202A\u206A\u200C\u206B\u202E[(int)num].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder);
						num <<= 1;
						num += num6;
						num2 |= num6 << num3;
						num3++;
						num4 = 1310093667;
						continue;
					}
					case 2:
						num4 = 1363881680;
						continue;
					}
					return num2;
				}
			}
			return num2;
		}

		// Token: 0x06000023 RID: 35 RVA: 0x000278AC File Offset: 0x00025AAC
		internal static uint \u206A\u202B\u206C\u206E\u202C\u200C\u202B\u206D\u202D\u200C\u202C\u200B\u200C\u206E\u200F\u206A\u202D\u202E\u202C\u200F\u200D\u200E\u202E\u200F\u206F\u206E\u200D\u206F\u200D\u202D\u206C\u202D\u202A\u202B\u206C\u206A\u202B\u200D\u206C\u200F\u202E(<Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] Models, uint startIndex, <Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder, int NumBitLevels)
		{
			uint num = 1U;
			uint num2 = 0U;
			int num3 = 0;
			for (;;)
			{
				IL_73:
				int num4 = (num3 < NumBitLevels) ? -1067475629 : 863007525;
				for (;;)
				{
					int num5 = num4;
					switch ((~num5 + -(358304855 - -1469036287)) * -2141934577 * -913018765 % 4)
					{
					case 1:
						goto IL_73;
					case 2:
					{
						uint num6 = Models[(int)(startIndex + num)].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder);
						num <<= 1;
						num += num6;
						num2 |= num6 << num3;
						num3++;
						num4 = 1935770288;
						continue;
					}
					case 3:
						num4 = -1067475629;
						continue;
					}
					return num2;
				}
			}
			return num2;
		}

		// Token: 0x04000004 RID: 4
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u200B\u206D\u206C\u200B\u200E\u200D\u206B\u206C\u206D\u202E\u206D\u200D\u206A\u206D\u200B\u206B\u200E\u200E\u206B\u202D\u206C\u206C\u200B\u206A\u200D\u202D\u202E\u202E\u200C\u200F\u202E\u200C\u200E\u202B\u206E\u202C\u202A\u206A\u200C\u206B\u202E;

		// Token: 0x04000005 RID: 5
		internal readonly int \u200C\u206C\u206C\u202B\u206C\u202B\u206E\u206B\u206E\u206E\u206E\u200E\u200B\u200C\u202A\u206E\u206E\u206B\u200D\u206E\u206F\u202A\u200D\u202D\u206A\u200E\u200B\u200E\u200D\u206B\u206C\u206D\u206F\u206B\u206F\u202B\u206B\u206D\u206F\u202C\u202E;
	}

	// Token: 0x02000004 RID: 4
	internal class \u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E
	{
		// Token: 0x06000024 RID: 36 RVA: 0x00027944 File Offset: 0x00025B44
		internal void \u202D\u206E\u202E\u200C\u202A\u200C\u200C\u200F\u202B\u202C\u202B\u206A\u206B\u200E\u206D\u202B\u206D\u200E\u200C\u200E\u202B\u200C\u206F\u202A\u200E\u202D\u202C\u200B\u202B\u206B\u206F\u206D\u200C\u206B\u206F\u202B\u200C\u206F\u206B\u202C\u202E(Stream stream)
		{
			this.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E = stream;
			this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = 0U;
			this.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E = uint.MaxValue;
			int num = 0;
			for (;;)
			{
				IL_8E:
				int num2 = (num >= 5) ? 1555014618 : -382451925;
				for (;;)
				{
					int num3 = num2;
					switch ((~(num3 + ((-1126717384 - -574680563 ^ ~-868809977) + 754623957) - -(173148473 * -1427344293)) - 1348048735) % 4)
					{
					case 0:
						num2 = -382451925;
						continue;
					case 1:
						goto IL_8E;
					case 2:
						this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = (this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E << 8 | (uint)((byte)this.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E.ReadByte()));
						num++;
						num2 = 1360619156;
						continue;
					}
					return;
				}
			}
		}

		// Token: 0x06000025 RID: 37 RVA: 0x000279F8 File Offset: 0x00025BF8
		internal void \u202A\u202B\u206F\u202D\u206B\u202B\u206D\u206A\u200B\u206E\u200E\u200C\u202B\u200D\u206E\u200B\u200D\u202D\u206E\u200B\u200B\u206A\u206A\u206A\u206E\u200C\u200F\u202B\u202E\u206F\u206C\u206D\u202B\u202D\u206A\u206C\u206B\u202A\u200F\u202E\u202E()
		{
			this.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E = null;
		}

		// Token: 0x06000026 RID: 38 RVA: 0x00027A0C File Offset: 0x00025C0C
		internal void \u206F\u206C\u202E\u202C\u200F\u206D\u202A\u206F\u200D\u202C\u206E\u202A\u202E\u206B\u200E\u200E\u200B\u200F\u202A\u206D\u206D\u202A\u206B\u202C\u206F\u206A\u206D\u202C\u206C\u202C\u206E\u206C\u206B\u200E\u200E\u206B\u200B\u206D\u206F\u202E\u202E()
		{
			for (;;)
			{
				IL_6D:
				int num = (this.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E >= 16777216U) ? -956257602 : -1667194673;
				for (;;)
				{
					int num2 = num;
					switch (~(num2 * 791654203 * -1659925501 ^ 2072790153 - 444019076) % 4)
					{
					case 1:
						this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = (this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E << 8 | (uint)((byte)this.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E.ReadByte()));
						this.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E <<= 8;
						num = -1291146628;
						continue;
					case 2:
						goto IL_6D;
					case 3:
						num = -1667194673;
						continue;
					}
					return;
				}
			}
		}

		// Token: 0x06000027 RID: 39 RVA: 0x00027AA8 File Offset: 0x00025CA8
		internal uint \u206E\u200F\u200F\u202C\u202E\u206B\u200B\u202B\u206D\u206E\u206E\u200D\u200F\u200C\u206C\u206A\u206F\u202C\u200D\u206F\u202D\u200B\u206E\u206B\u200C\u200D\u206F\u202A\u206C\u200F\u206F\u202A\u202C\u206E\u206E\u200C\u206B\u202C\u202E\u202C\u202E(int numTotalBits)
		{
			uint num = this.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E;
			uint num2 = this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E;
			uint num3 = 0U;
			int num4 = numTotalBits;
			for (;;)
			{
				IL_C4:
				int num5 = (num4 > 0) ? 342826847 : -1053657690;
				for (;;)
				{
					int num6 = num5;
					uint num7;
					switch ((num7 = (uint)((-num6 + ~(~-1608377684)) * 1624118697 - -1689210088)) % 7U)
					{
					case 0U:
						goto IL_C4;
					case 1U:
						num2 = (num2 << 8 | (uint)((byte)this.\u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E.ReadByte()));
						num <<= 8;
						num5 = (int)(num7 * 1953917310U ^ 1713762678U);
						continue;
					case 2U:
						num5 = 342826847;
						continue;
					case 3U:
						num >>= 1;
						num5 = -1696340746;
						continue;
					case 5U:
					{
						uint num8 = num2 - num >> 31;
						num2 -= (num & num8 - 1U);
						num3 = (num3 << 1 | 1U - num8);
						num5 = (int)(((num >= 16777216U) ? 2587910096U : 4257806842U) ^ num7 * 3482148829U);
						continue;
					}
					case 6U:
						num4--;
						num5 = 294294059;
						continue;
					}
					goto Block_1;
				}
			}
			Block_1:
			this.\u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E = num;
			this.\u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E = num2;
			return num3;
		}

		// Token: 0x06000028 RID: 40 RVA: 0x00027BBC File Offset: 0x00025DBC
		internal \u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E()
		{
		}

		// Token: 0x04000006 RID: 6
		internal uint \u206F\u202D\u206D\u200E\u200B\u206C\u202D\u200E\u206A\u202B\u200D\u206A\u200C\u200E\u200E\u200F\u206D\u200E\u200D\u206D\u206E\u202D\u206D\u202A\u200F\u202E\u202E\u206B\u206D\u202D\u200B\u202D\u206E\u200C\u202B\u200E\u206E\u200C\u206C\u200E\u202E;

		// Token: 0x04000007 RID: 7
		internal uint \u202B\u202C\u206B\u200E\u206A\u206E\u206E\u202A\u200D\u206C\u202D\u200B\u206B\u206D\u206F\u200B\u202B\u206C\u200E\u202E\u206D\u200F\u206E\u200B\u202A\u206B\u206D\u200D\u202E\u206E\u206D\u202B\u200B\u200D\u200B\u200F\u202E\u200F\u206B\u202B\u202E;

		// Token: 0x04000008 RID: 8
		internal Stream \u206B\u206E\u206D\u200F\u202A\u200D\u200D\u206A\u202D\u202B\u202D\u200E\u206B\u206B\u206A\u206C\u206D\u200D\u206E\u206B\u202B\u206F\u206D\u200D\u206B\u200D\u200D\u200B\u200F\u200E\u202C\u200C\u206C\u202E\u206B\u202A\u202E\u202A\u206F\u200C\u202E;
	}

	// Token: 0x02000005 RID: 5
	internal class \u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E
	{
		// Token: 0x06000029 RID: 41 RVA: 0x00027BD0 File Offset: 0x00025DD0
		internal \u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E()
		{
			this.\u206B\u206A\u200C\u206F\u206C\u206B\u200B\u206F\u200C\u202C\u206A\u206E\u206A\u200C\u202D\u202A\u200E\u202E\u202A\u202E\u206D\u206D\u200B\u200E\u206C\u200D\u200E\u202B\u206A\u206A\u206C\u200D\u202E\u202E\u202E\u202E\u206C\u206C\u200D\u202E\u202E = uint.MaxValue;
			int num = 0;
			while ((long)num < 4L)
			{
				this.\u202C\u206E\u206A\u202A\u200F\u200C\u200E\u206D\u200D\u200D\u206E\u200C\u206B\u200E\u200C\u202C\u202B\u200F\u202A\u200B\u200C\u200D\u206D\u206F\u200C\u202C\u206C\u202C\u206A\u200B\u200D\u202C\u200E\u202B\u206B\u206D\u206D\u206E\u200E\u200D\u202E[num] = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(6);
				num++;
			}
		}

		// Token: 0x0600002A RID: 42 RVA: 0x00027CBC File Offset: 0x00025EBC
		internal void \u200E\u202C\u200F\u200B\u206D\u200B\u206F\u200E\u200E\u200B\u206A\u200E\u202E\u206F\u202B\u206E\u200B\u206D\u200F\u200D\u206B\u206D\u206C\u202B\u200D\u206B\u202C\u206E\u200D\u206C\u202E\u202D\u206B\u200E\u206E\u202D\u206D\u206F\u206E\u200D\u202E(uint dictionarySize)
		{
			if (this.\u206B\u206A\u200C\u206F\u206C\u206B\u200B\u206F\u200C\u202C\u206A\u206E\u206A\u200C\u202D\u202A\u200E\u202E\u202A\u202E\u206D\u206D\u200B\u200E\u206C\u200D\u200E\u202B\u206A\u206A\u206C\u200D\u202E\u202E\u202E\u202E\u206C\u206C\u200D\u202E\u202E != dictionarySize)
			{
				for (;;)
				{
					IL_09:
					int num = -700969803;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(-num2 - 762879907 * (-2049300971 * -1297042662))) % 3U)
						{
						case 0U:
							goto IL_09;
						case 2U:
						{
							this.\u206B\u206A\u200C\u206F\u206C\u206B\u200B\u206F\u200C\u202C\u206A\u206E\u206A\u200C\u202D\u202A\u200E\u202E\u202A\u202E\u206D\u206D\u200B\u200E\u206C\u200D\u200E\u202B\u206A\u206A\u206C\u200D\u202E\u202E\u202E\u202E\u206C\u206C\u200D\u202E\u202E = dictionarySize;
							this.\u202A\u206E\u200F\u202B\u206A\u202B\u206D\u206C\u200C\u200E\u206C\u206C\u202C\u206B\u200B\u202C\u206E\u202A\u200C\u202C\u200E\u202E\u200D\u200B\u200E\u202E\u202E\u200E\u206F\u202E\u200B\u202A\u200D\u202C\u206F\u200D\u202B\u206F\u206C\u202A\u202E = Math.Max(this.\u206B\u206A\u200C\u206F\u206C\u206B\u200B\u206F\u200C\u202C\u206A\u206E\u206A\u200C\u202D\u202A\u200E\u202E\u202A\u202E\u206D\u206D\u200B\u200E\u206C\u200D\u200E\u202B\u206A\u206A\u206C\u200D\u202E\u202E\u202E\u202E\u206C\u206C\u200D\u202E\u202E, 1U);
							uint windowSize = Math.Max(this.\u202A\u206E\u200F\u202B\u206A\u202B\u206D\u206C\u200C\u200E\u206C\u206C\u202C\u206B\u200B\u202C\u206E\u202A\u200C\u202C\u200E\u202E\u200D\u200B\u200E\u202E\u202E\u200E\u206F\u202E\u200B\u202A\u200D\u202C\u206F\u200D\u202B\u206F\u206C\u202A\u202E, 4096U);
							this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202D\u206D\u200C\u206E\u206D\u206B\u200D\u202E\u202E\u200F\u200E\u202A\u200B\u206E\u202D\u200E\u200E\u200B\u200F\u206E\u206A\u202C\u202C\u202A\u200B\u202C\u206E\u206C\u202C\u202B\u200E\u202D\u202E\u206D\u206B\u200C\u202C\u206C\u200C\u206B\u202E(windowSize);
							num = (int)(num3 * 1884036469U ^ 2127587636U);
							continue;
						}
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x0600002B RID: 43 RVA: 0x00027D48 File Offset: 0x00025F48
		internal void \u200D\u202C\u206B\u200F\u200B\u206D\u200F\u202C\u200D\u206D\u206D\u206F\u206E\u206D\u202B\u206A\u200D\u206D\u200E\u200E\u202C\u202C\u200B\u202E\u202C\u206D\u200E\u206A\u202E\u200B\u200D\u200B\u206A\u206E\u206E\u200E\u206E\u206F\u200E\u200E\u202E(int lp, int lc)
		{
			this.\u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E.\u206C\u200B\u202A\u202D\u202A\u200D\u200F\u202D\u206D\u202E\u206E\u200D\u206A\u206E\u202C\u200E\u200B\u202D\u206D\u200B\u206D\u202C\u206A\u202B\u200F\u202B\u206F\u206E\u202A\u206F\u206C\u206E\u206B\u200C\u200F\u206F\u206D\u200E\u202B\u202A\u202E(lp, lc);
		}

		// Token: 0x0600002C RID: 44 RVA: 0x00027D64 File Offset: 0x00025F64
		internal void \u202D\u206D\u202B\u206C\u206A\u206A\u202E\u200F\u200C\u202B\u200C\u200D\u200E\u202D\u202E\u200C\u206D\u202A\u200D\u202C\u206A\u202D\u206A\u202C\u206A\u200E\u200F\u200B\u202E\u200C\u200E\u206F\u200C\u200C\u200B\u206E\u206D\u200F\u202B\u202B\u202E(int pb)
		{
			uint num = 1U << pb;
			this.\u202D\u202D\u206E\u202B\u206A\u206F\u200D\u206C\u202C\u202B\u206F\u202A\u202D\u206B\u202A\u206E\u206A\u202D\u202D\u202D\u206E\u202B\u200C\u206B\u206E\u202E\u202B\u206C\u206F\u202D\u206B\u200B\u200C\u206A\u206F\u202C\u200E\u200F\u202C\u206E\u202E.\u200F\u202C\u206A\u206A\u202E\u206F\u200C\u206E\u206B\u206A\u202A\u206A\u202D\u200C\u202B\u202D\u200D\u206B\u200C\u200F\u202A\u202B\u200B\u200C\u200E\u206F\u200E\u200C\u206F\u206F\u206C\u202B\u202E\u200D\u206C\u206F\u206F\u200C\u200B\u200E\u202E(num);
			this.\u202B\u206A\u202B\u206E\u200D\u200D\u202D\u200E\u202D\u202E\u206F\u202D\u200C\u202E\u206C\u200E\u202E\u202A\u206B\u206E\u200D\u202D\u206B\u200B\u202C\u202C\u200F\u202B\u206D\u202D\u202C\u202C\u206B\u200C\u202A\u206F\u202D\u202A\u206B\u202E.\u200F\u202C\u206A\u206A\u202E\u206F\u200C\u206E\u206B\u206A\u202A\u206A\u202D\u200C\u202B\u202D\u200D\u206B\u200C\u200F\u202A\u202B\u200B\u200C\u200E\u206F\u200E\u200C\u206F\u206F\u206C\u202B\u202E\u200D\u206C\u206F\u206F\u200C\u200B\u200E\u202E(num);
			this.\u202E\u206F\u200B\u202C\u200C\u206D\u206A\u202B\u206C\u200C\u206A\u200C\u200D\u202E\u206D\u200E\u202C\u200E\u200D\u202A\u206C\u206C\u200C\u202B\u206A\u206B\u202B\u200C\u206F\u200F\u206A\u202B\u200E\u200D\u200E\u202E\u202B\u202D\u200F\u202E = num - 1U;
		}

		// Token: 0x0600002D RID: 45 RVA: 0x00027D9C File Offset: 0x00025F9C
		internal void \u206F\u202D\u206C\u202D\u206B\u206A\u206F\u202B\u200E\u200B\u200F\u206F\u206B\u200B\u206E\u202D\u206D\u206B\u202D\u200D\u206E\u200E\u202D\u206E\u200F\u206C\u200F\u200B\u206A\u206E\u200B\u206E\u206D\u200D\u206C\u206F\u202A\u202A\u202A\u202E(Stream inStream, Stream outStream)
		{
			this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E.\u202D\u206E\u202E\u200C\u202A\u200C\u200C\u200F\u202B\u202C\u202B\u206A\u206B\u200E\u206D\u202B\u206D\u200E\u200C\u200E\u202B\u200C\u206F\u202A\u200E\u202D\u202C\u200B\u202B\u206B\u206F\u206D\u200C\u206B\u206F\u202B\u200C\u206F\u206B\u202C\u202E(inStream);
			this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202A\u200E\u200D\u202A\u202D\u202B\u202E\u206F\u206D\u202D\u202B\u206F\u206B\u202A\u202A\u206F\u202C\u202D\u202B\u206F\u202D\u206E\u200F\u202E\u200E\u200D\u206C\u202C\u200C\u206B\u206D\u200D\u202A\u200E\u200B\u200F\u206F\u200B\u202C\u206E\u202E(outStream, this.\u206B\u202B\u202E\u202D\u206F\u200B\u206A\u202D\u206E\u206C\u206B\u202D\u200E\u200C\u202E\u202D\u202A\u200C\u202A\u200D\u200D\u206E\u202B\u202E\u202D\u206B\u206A\u202D\u200C\u202A\u202A\u200B\u200D\u206D\u206B\u200D\u202C\u206F\u206C\u202E);
			uint num = 0U;
			for (;;)
			{
				IL_15E:
				int num2 = (num >= 12U) ? -330020064 : -25351696;
				for (;;)
				{
					int num3 = num2;
					uint num4;
					switch ((num4 = (uint)(-(uint)(num3 ^ (1833148959 - 798101645 ^ 1845516736)))) % 13U)
					{
					case 0U:
						goto IL_15E;
					case 1U:
					{
						uint num5 = 0U;
						num2 = -94844035;
						continue;
					}
					case 2U:
					{
						uint num5;
						uint num6 = (num << 4) + num5;
						this.\u200F\u200B\u200C\u202B\u206A\u202B\u200E\u202B\u200D\u200C\u206C\u202D\u206A\u202E\u202A\u200E\u206F\u200B\u200C\u200C\u206B\u202E\u200F\u200B\u206F\u206D\u206B\u206D\u202D\u206C\u200D\u202B\u202E\u200D\u206A\u202A\u206E\u202D\u200C\u200F\u202E[(int)num6].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						this.\u206A\u206C\u206F\u202A\u200F\u202D\u206E\u206F\u202B\u200C\u200B\u206D\u200E\u200D\u206D\u200F\u202E\u202D\u206E\u206A\u202D\u206F\u202E\u200B\u200F\u200F\u200F\u200E\u206E\u206F\u200C\u200B\u206C\u202B\u206F\u202E\u202B\u206E\u200E\u202C\u202E[(int)num6].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						num5 += 1U;
						num2 = -94844035;
						continue;
					}
					case 3U:
					{
						uint num5;
						num2 = ((num5 > this.\u202E\u206F\u200B\u202C\u200C\u206D\u206A\u202B\u206C\u200C\u206A\u200C\u200D\u202E\u206D\u200E\u202C\u200E\u200D\u202A\u206C\u206C\u200C\u202B\u206A\u206B\u202B\u200C\u206F\u200F\u206A\u202B\u200E\u200D\u200E\u202E\u202B\u202D\u200F\u202E) ? -1589499431 : -495118599);
						continue;
					}
					case 4U:
						num2 = ((num < 114U) ? -3329285 : -1317192007);
						continue;
					case 5U:
						this.\u202C\u206E\u206A\u202A\u200F\u200C\u200E\u206D\u200D\u200D\u206E\u200C\u206B\u200E\u200C\u202C\u202B\u200F\u202A\u200B\u200C\u200D\u206D\u206F\u200C\u202C\u206C\u202C\u206A\u200B\u200D\u202C\u200E\u202B\u206B\u206D\u206D\u206E\u200E\u200D\u202E[(int)num].\u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E();
						num += 1U;
						num2 = -1512386884;
						continue;
					case 6U:
						num = 0U;
						num2 = (int)(num4 * 31396517U ^ 2227639978U);
						continue;
					case 7U:
						this.\u206F\u202C\u202D\u206F\u206A\u202C\u202C\u206A\u206A\u200C\u202B\u206C\u206A\u202C\u200E\u206C\u206F\u200F\u202D\u200C\u202B\u200D\u202C\u200D\u200B\u202D\u202B\u200E\u202C\u206E\u202C\u202B\u200C\u206F\u206A\u206C\u202B\u202B\u200B\u206B\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						this.\u206F\u206A\u200C\u202C\u202B\u200E\u202E\u200B\u206D\u200F\u200B\u200D\u202D\u206B\u202C\u202D\u200F\u206C\u200D\u202C\u200F\u206B\u200D\u200B\u202A\u206E\u202E\u202E\u200F\u202B\u206C\u202C\u206B\u206F\u200C\u202D\u202A\u206A\u200F\u202B\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						this.\u200D\u202A\u200B\u206C\u206E\u200D\u206D\u202A\u206B\u206D\u206E\u202D\u202C\u206F\u206D\u200E\u206B\u206E\u200D\u200F\u206A\u200C\u206E\u202D\u200D\u200E\u200B\u206B\u202C\u200E\u206D\u206A\u206B\u206D\u206B\u202A\u206C\u202D\u202A\u200F\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						this.\u200D\u206D\u206E\u206C\u200D\u202E\u200C\u200D\u200D\u206D\u200B\u206A\u200F\u206B\u200C\u206C\u202B\u206E\u206D\u206D\u206D\u202C\u206C\u202B\u202E\u206C\u202B\u206D\u202E\u206A\u200B\u202D\u206B\u202D\u202C\u206D\u200B\u202C\u206A\u202D\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						num += 1U;
						num2 = (int)(num4 * 2326413830U ^ 1997535881U);
						continue;
					case 8U:
						num2 = ((num < 4U) ? -1858381551 : -1103328004);
						continue;
					case 9U:
						this.\u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E.\u200D\u200F\u206F\u206B\u206D\u206C\u206C\u206F\u200B\u206E\u206A\u206E\u200F\u200F\u200B\u200B\u206A\u200B\u206C\u200E\u206F\u206F\u202E\u202B\u202C\u206A\u200F\u206E\u206B\u200F\u202C\u200F\u200E\u202B\u200F\u206E\u206C\u202E\u206B\u206C\u202E();
						num = 0U;
						num2 = (int)(num4 * 3075155332U ^ 1150841220U);
						continue;
					case 10U:
						this.\u206E\u202D\u202E\u206A\u202B\u200E\u200E\u200F\u200B\u202E\u206F\u206C\u200F\u206F\u202E\u206F\u200D\u202C\u200C\u202D\u200D\u206C\u202A\u200F\u202A\u202D\u206C\u202E\u202E\u200D\u206A\u206F\u206B\u202A\u202B\u202A\u206E\u206E\u202C\u206D\u202E[(int)num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
						num += 1U;
						num2 = -146052752;
						continue;
					case 12U:
						num2 = -25351696;
						continue;
					}
					goto Block_1;
				}
			}
			Block_1:
			this.\u202D\u202D\u206E\u202B\u206A\u206F\u200D\u206C\u202C\u202B\u206F\u202A\u202D\u206B\u202A\u206E\u206A\u202D\u202D\u202D\u206E\u202B\u200C\u206B\u206E\u202E\u202B\u206C\u206F\u202D\u206B\u200B\u200C\u206A\u206F\u202C\u200E\u200F\u202C\u206E\u202E.\u206D\u206C\u202C\u206C\u202D\u200D\u200D\u200E\u206B\u206E\u200C\u206A\u206F\u202B\u200B\u200E\u200E\u200D\u202D\u202B\u206D\u206F\u202C\u202A\u206F\u206F\u206C\u202B\u200B\u202D\u202A\u200C\u202A\u200B\u206C\u200B\u202E\u206B\u200D\u206D\u202E();
			this.\u202B\u206A\u202B\u206E\u200D\u200D\u202D\u200E\u202D\u202E\u206F\u202D\u200C\u202E\u206C\u200E\u202E\u202A\u206B\u206E\u200D\u202D\u206B\u200B\u202C\u202C\u200F\u202B\u206D\u202D\u202C\u202C\u206B\u200C\u202A\u206F\u202D\u202A\u206B\u202E.\u206D\u206C\u202C\u206C\u202D\u200D\u200D\u200E\u206B\u206E\u200C\u206A\u206F\u202B\u200B\u200E\u200E\u200D\u202D\u202B\u206D\u206F\u202C\u202A\u206F\u206F\u206C\u202B\u200B\u202D\u202A\u200C\u202A\u200B\u206C\u200B\u202E\u206B\u200D\u206D\u202E();
			this.\u202D\u206F\u206E\u206E\u206A\u200E\u206A\u200D\u202B\u206B\u206C\u202C\u200D\u202A\u206E\u206C\u202A\u200C\u200E\u200C\u206E\u206E\u200E\u200E\u200E\u202A\u206E\u202B\u206B\u206D\u202D\u202A\u202A\u206C\u202C\u206D\u202E\u202E\u206D\u206F\u202E.\u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E();
		}

		// Token: 0x0600002E RID: 46 RVA: 0x00027FC0 File Offset: 0x000261C0
		internal void \u206B\u202A\u200F\u202D\u206F\u206F\u200C\u200E\u200D\u206C\u206A\u200F\u206A\u200D\u200D\u206E\u200D\u206E\u202B\u200B\u200D\u206C\u206F\u206D\u206E\u200E\u206A\u206C\u202B\u206E\u206C\u200B\u206C\u206E\u206C\u206C\u200F\u202C\u206F\u200D\u202E(Stream inStream, Stream outStream, long inSize, long outSize)
		{
			this.\u206F\u202D\u206C\u202D\u206B\u206A\u206F\u202B\u200E\u200B\u200F\u206F\u206B\u200B\u206E\u202D\u206D\u206B\u202D\u200D\u206E\u200E\u202D\u206E\u200F\u206C\u200F\u200B\u206A\u206E\u200B\u206E\u206D\u200D\u206C\u206F\u202A\u202A\u202A\u202E(inStream, outStream);
			<Module>.\u202D\u206A\u202A\u202D\u202C\u202A\u206A\u206F\u206E\u200E\u206C\u200B\u202B\u206B\u206A\u206B\u200F\u200C\u202E\u206C\u202A\u202B\u200F\u206F\u206B\u200C\u206C\u200D\u206D\u206E\u206F\u206C\u206F\u202D\u200F\u200C\u206E\u202B\u206C\u200E\u202E u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E = default(<Module>.\u202D\u206A\u202A\u202D\u202C\u202A\u206A\u206F\u206E\u200E\u206C\u200B\u202B\u206B\u206A\u206B\u200F\u200C\u202E\u206C\u202A\u202B\u200F\u206F\u206B\u200C\u206C\u200D\u206D\u206E\u206F\u206C\u206F\u202D\u200F\u200C\u206E\u202B\u206C\u200E\u202E);
			u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200B\u202B\u202A\u202C\u202E\u206B\u206C\u200B\u202E\u206D\u206A\u206C\u206A\u202C\u202E\u202C\u206F\u206C\u206C\u206A\u206D\u202C\u202B\u202D\u202C\u200B\u206F\u200F\u202B\u202D\u202D\u202B\u200F\u200D\u206D\u200F\u202A\u200B\u206C\u206B\u202E();
			uint num = 0U;
			uint num2 = 0U;
			uint num3 = 0U;
			uint num4 = 0U;
			ulong num5 = 0UL;
			if (num5 < (ulong)outSize)
			{
				goto IL_31;
			}
			goto IL_3A2;
			int num7;
			for (;;)
			{
				IL_36:
				int num6 = num7;
				uint num8;
				switch ((num8 = (uint)((num6 * -1946421941 ^ -911134889) + (-90362088 + 1402402658))) % 32U)
				{
				case 0U:
					u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u206F\u200B\u202E\u206C\u202C\u202E\u202A\u206A\u200C\u206A\u202D\u200B\u202B\u202C\u200B\u206D\u202D\u202D\u206E\u200D\u206C\u206A\u206C\u202A\u202B\u206A\u202A\u206E\u202B\u206A\u206E\u206D\u206E\u206B\u206F\u206A\u206F\u206D\u206C\u200D\u202E();
					this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202A\u206E\u200C\u206E\u200E\u200C\u202D\u200C\u202B\u202C\u202C\u202B\u202A\u206D\u206D\u200F\u202E\u206B\u200B\u200D\u202A\u202B\u206B\u202E\u202C\u206B\u206A\u202A\u200C\u200B\u206F\u206A\u202C\u202B\u202A\u206E\u202E\u206A\u200B\u202C\u202E(this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u200E\u200D\u202A\u206A\u202D\u200E\u200F\u206F\u200F\u200C\u202B\u202A\u200F\u206E\u206A\u206B\u200C\u206C\u200D\u206E\u200C\u206A\u206B\u202D\u206D\u202C\u200D\u206C\u206F\u202A\u202D\u202C\u200E\u206A\u200F\u206F\u200E\u202E\u206A\u206E\u202E(num));
					num5 += 1UL;
					num7 = (int)(num8 * 1250184642U ^ 928848359U);
					continue;
				case 1U:
				{
					uint num9 = (uint)num5 & this.\u202E\u206F\u200B\u202C\u200C\u206D\u206A\u202B\u206C\u200C\u206A\u200C\u200D\u202E\u206D\u200E\u202C\u200E\u200D\u202A\u206C\u206C\u200C\u202B\u206A\u206B\u202B\u200C\u206F\u200F\u206A\u202B\u200E\u200D\u200E\u202E\u202B\u202D\u200F\u202E;
					num7 = ((this.\u200F\u200B\u200C\u202B\u206A\u202B\u200E\u202B\u200D\u200C\u206C\u202D\u206A\u202E\u202A\u200E\u206F\u200B\u200C\u200C\u206B\u202E\u200F\u200B\u206F\u206D\u206B\u206D\u202D\u206C\u200D\u202B\u202E\u200D\u206A\u202A\u206E\u202D\u200C\u200F\u202E[(int)((u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E << 4) + num9)].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 0U) ? 947128440 : -528071735);
					continue;
				}
				case 2U:
				{
					num4 = num3;
					num3 = num2;
					num2 = num;
					uint num9;
					uint num10 = 2U + this.\u202D\u202D\u206E\u202B\u206A\u206F\u200D\u206C\u202C\u202B\u206F\u202A\u202D\u206B\u202A\u206E\u206A\u202D\u202D\u202D\u206E\u202B\u200C\u206B\u206E\u202E\u202B\u206C\u206F\u202D\u206B\u200B\u200C\u206A\u206F\u202C\u200E\u200F\u202C\u206E\u202E.\u202E\u206F\u202A\u200C\u202C\u202D\u206C\u202B\u200E\u206C\u202B\u206F\u206A\u200D\u206D\u202D\u200D\u200F\u206C\u206F\u206A\u200F\u202C\u200F\u200B\u206E\u202A\u200D\u200B\u202A\u202B\u202A\u206D\u206D\u206E\u200F\u206A\u202A\u200D\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, num9);
					u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u202B\u202D\u206C\u200D\u202B\u200F\u200D\u200F\u206C\u202D\u202D\u200D\u200B\u200F\u206C\u200B\u206D\u200F\u206C\u200B\u206E\u202D\u206F\u206C\u202C\u200E\u206A\u200C\u202B\u206D\u206D\u202C\u206D\u200B\u206D\u200C\u206B\u202A\u206E\u206A\u202E();
					num7 = 626139588;
					continue;
				}
				case 3U:
					num += this.\u202D\u206F\u206E\u206E\u206A\u200E\u206A\u200D\u202B\u206B\u206C\u202C\u200D\u202A\u206E\u206C\u202A\u200C\u200E\u200C\u206E\u206E\u200E\u200E\u200E\u202A\u206E\u202B\u206B\u206D\u202D\u202A\u202A\u206C\u202C\u206D\u202E\u202E\u206D\u206F\u202E.\u200B\u200F\u200C\u200B\u202B\u200D\u206B\u200F\u206E\u200B\u206B\u206B\u200B\u202A\u202D\u206B\u200C\u206D\u200E\u200C\u206E\u200E\u200F\u202E\u202A\u206A\u206F\u206D\u202D\u202D\u200D\u202D\u206B\u206B\u200C\u206F\u200C\u206D\u202D\u206E\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E);
					num7 = (int)(num8 * 3243478500U ^ 3320869673U);
					continue;
				case 4U:
					num7 = ((this.\u200D\u206D\u206E\u206C\u200D\u202E\u200C\u200D\u200D\u206D\u200B\u206A\u200F\u206B\u200C\u206C\u202B\u206E\u206D\u206D\u206D\u202C\u206C\u202B\u202E\u206C\u202B\u206D\u202E\u206A\u200B\u202D\u206B\u202D\u202C\u206D\u200B\u202C\u206A\u202D\u202E[(int)u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 0U) ? 1106892655 : 1482975742);
					continue;
				case 6U:
					goto IL_31;
				case 7U:
				{
					uint num11 = num2;
					num7 = (int)(num8 * 3570909683U ^ 85528217U);
					continue;
				}
				case 8U:
				{
					uint num9;
					num7 = (int)(((this.\u206A\u206C\u206F\u202A\u200F\u202D\u206E\u206F\u202B\u200C\u200B\u206D\u200E\u200D\u206D\u200F\u202E\u202D\u206E\u206A\u202D\u206F\u202E\u200B\u200F\u200F\u200F\u200E\u206E\u206F\u200C\u200B\u206C\u202B\u206F\u202E\u202B\u206E\u200E\u202C\u202E[(int)((u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E << 4) + num9)].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 0U) ? 314456832U : 2669689395U) ^ num8 * 430346352U);
					continue;
				}
				case 9U:
					num7 = (int)(((num >= this.\u202A\u206E\u200F\u202B\u206A\u202B\u206D\u206C\u200C\u200E\u206C\u206C\u202C\u206B\u200B\u202C\u206E\u202A\u200C\u202C\u200E\u202E\u200D\u200B\u200E\u202E\u202E\u200E\u206F\u202E\u200B\u202A\u200D\u202C\u206F\u200D\u202B\u206F\u206C\u202A\u202E) ? 450547834U : 483576545U) ^ num8 * 368213464U);
					continue;
				case 10U:
				{
					byte b;
					this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202A\u206E\u200C\u206E\u200E\u200C\u202D\u200C\u202B\u202C\u202C\u202B\u202A\u206D\u206D\u200F\u202E\u206B\u200B\u200D\u202A\u202B\u206B\u202E\u202C\u206B\u206A\u202A\u200C\u200B\u206F\u206A\u202C\u202B\u202A\u206E\u202E\u206A\u200B\u202C\u202E(b);
					u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u200E\u202E\u206A\u200C\u200E\u206B\u206B\u202A\u206D\u202A\u206C\u202E\u202C\u202D\u206D\u200F\u206C\u202E\u200D\u202E\u206C\u206F\u202A\u202A\u206F\u200C\u202A\u202B\u206F\u206A\u206F\u200B\u206C\u200F\u202D\u202E\u206E\u206D\u202C\u202E();
					num5 += 1UL;
					num7 = 382539431;
					continue;
				}
				case 11U:
				{
					uint num12;
					int num13;
					num += <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E.\u206A\u202B\u206C\u206E\u202C\u200C\u202B\u206D\u202D\u200C\u202C\u200B\u200C\u206E\u200F\u206A\u202D\u202E\u202C\u200F\u200D\u200E\u202E\u200F\u206F\u206E\u200D\u206F\u200D\u202D\u206C\u202D\u202A\u202B\u206C\u206A\u202B\u200D\u206C\u200F\u202E(this.\u206E\u202D\u202E\u206A\u202B\u200E\u200E\u200F\u200B\u202E\u206F\u206C\u200F\u206F\u202E\u206F\u200D\u202C\u200C\u202D\u200D\u206C\u202A\u200F\u202A\u202D\u206C\u202E\u202E\u200D\u206A\u206F\u206B\u202A\u202B\u202A\u206E\u206E\u202C\u206D\u202E, num - num12 - 1U, this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, num13);
					num7 = (int)(num8 * 883815230U ^ 4246499887U);
					continue;
				}
				case 12U:
				{
					uint num11 = num4;
					num4 = num3;
					num7 = 466610444;
					continue;
				}
				case 13U:
					num3 = num2;
					num7 = 1246564156;
					continue;
				case 14U:
				{
					byte prevByte = this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u200E\u200D\u202A\u206A\u202D\u200E\u200F\u206F\u200F\u200C\u202B\u202A\u200F\u206E\u206A\u206B\u200C\u206C\u200D\u206E\u200C\u206A\u206B\u202D\u206D\u202C\u200D\u206C\u206F\u202A\u202D\u202C\u200E\u206A\u200F\u206F\u200E\u202E\u206A\u206E\u202E(0U);
					num7 = (int)((u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u202D\u202E\u200E\u200C\u206F\u206C\u206E\u206E\u206B\u200F\u202C\u200E\u200B\u200E\u200D\u206C\u202B\u200F\u206C\u200F\u202C\u206E\u200E\u200E\u200B\u202D\u206F\u206A\u200F\u200D\u206A\u202C\u200C\u200D\u200D\u202E\u206A\u202C\u202E\u206A\u202E() ? 1982838009U : 2267360053U) ^ num8 * 3660237156U);
					continue;
				}
				case 15U:
				{
					int num13;
					num += this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E.\u206E\u200F\u200F\u202C\u202E\u206B\u200B\u202B\u206D\u206E\u206E\u200D\u200F\u200C\u206C\u206A\u206F\u202C\u200D\u206F\u202D\u200B\u206E\u206B\u200C\u200D\u206F\u202A\u206C\u200F\u206F\u202A\u202C\u206E\u206E\u200C\u206B\u202C\u202E\u202C\u202E(num13 - 4) << 4;
					num7 = 862346426;
					continue;
				}
				case 16U:
					num7 = ((this.\u200D\u202A\u200B\u206C\u206E\u200D\u206D\u202A\u206B\u206D\u206E\u202D\u202C\u206F\u206D\u200E\u206B\u206E\u200D\u200F\u206A\u200C\u206E\u202D\u200D\u200E\u200B\u206B\u202C\u200E\u206D\u206A\u206B\u206D\u206B\u202A\u206C\u202D\u202A\u200F\u202E[(int)u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 0U) ? 2041872439 : -569017778);
					continue;
				case 17U:
				{
					uint num9;
					uint num10 = this.\u202B\u206A\u202B\u206E\u200D\u200D\u202D\u200E\u202D\u202E\u206F\u202D\u200C\u202E\u206C\u200E\u202E\u202A\u206B\u206E\u200D\u202D\u206B\u200B\u202C\u202C\u200F\u202B\u206D\u202D\u202C\u202C\u206B\u200C\u202A\u206F\u202D\u202A\u206B\u202E.\u202E\u206F\u202A\u200C\u202C\u202D\u206C\u202B\u200E\u206C\u202B\u206F\u206A\u200D\u206D\u202D\u200D\u200F\u206C\u206F\u206A\u200F\u202C\u200F\u200B\u206E\u202A\u200D\u200B\u202A\u202B\u202A\u206D\u206D\u206E\u200F\u206A\u202A\u200D\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, num9) + 2U;
					u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200D\u206D\u206B\u200D\u206F\u200D\u206C\u202E\u206E\u200B\u200B\u206E\u206F\u202B\u200C\u202D\u200F\u206F\u206D\u202C\u206E\u202D\u202D\u200C\u206E\u200F\u206E\u200E\u202E\u202E\u202B\u200C\u206A\u202C\u202D\u202E\u206B\u206B\u202D\u202D\u202E();
					num7 = 1479323397;
					continue;
				}
				case 18U:
				{
					byte prevByte;
					byte b = this.\u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E.\u202A\u202E\u200F\u202E\u202A\u206A\u200F\u202A\u200B\u202A\u202E\u202D\u202C\u206F\u202D\u202C\u200F\u202E\u206B\u200E\u206F\u206E\u202B\u202D\u200B\u206D\u200B\u206B\u206F\u200C\u200E\u202C\u200B\u206A\u202C\u200D\u202C\u200D\u200E\u206A\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, (uint)num5, prevByte, this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u200E\u200D\u202A\u206A\u202D\u200E\u200F\u206F\u200F\u200C\u202B\u202A\u200F\u206E\u206A\u206B\u200C\u206C\u200D\u206E\u200C\u206A\u206B\u202D\u206D\u202C\u200D\u206C\u206F\u202A\u202D\u202C\u200E\u206A\u200F\u206F\u200E\u202E\u206A\u206E\u202E(num));
					num7 = (int)(num8 * 1393240267U ^ 2045993875U);
					continue;
				}
				case 19U:
				{
					uint num12;
					int num13 = (int)((num12 >> 1) - 1U);
					num = (2U | (num12 & 1U)) << num13;
					num7 = (int)(((num12 < 14U) ? 320275640U : 2929014316U) ^ num8 * 2540205358U);
					continue;
				}
				case 20U:
					goto IL_3A2;
				case 21U:
				{
					uint num10;
					uint num12 = this.\u202C\u206E\u206A\u202A\u200F\u200C\u200E\u206D\u200D\u200D\u206E\u200C\u206B\u200E\u200C\u202C\u202B\u200F\u202A\u200B\u200C\u200D\u206D\u206F\u200C\u202C\u206C\u202C\u206A\u200B\u200D\u202C\u200E\u202B\u206B\u206D\u206D\u206E\u200E\u200D\u202E[(int)<Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u202A\u200C\u206C\u206C\u206D\u206A\u200E\u206A\u206B\u200F\u206B\u200E\u200D\u206C\u206C\u206B\u202D\u200F\u202D\u202D\u206E\u206F\u206A\u202D\u206A\u206A\u200D\u206E\u206D\u206E\u206A\u202C\u202B\u200E\u200D\u206B\u202C\u202B\u206D\u206E\u202E(num10)].\u200B\u202B\u200F\u206E\u206E\u200C\u202A\u206A\u202D\u206A\u200D\u200B\u206D\u202D\u206D\u202E\u200E\u200F\u206B\u206A\u206A\u202E\u206B\u206A\u202E\u200E\u206F\u202D\u206A\u202C\u200C\u200E\u200C\u200F\u206B\u202B\u202C\u200E\u206F\u202D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E);
					num7 = (int)(((num12 >= 4U) ? 406395613U : 3586670376U) ^ num8 * 1569201403U);
					continue;
				}
				case 22U:
				{
					byte prevByte;
					byte b = this.\u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E.\u202B\u200D\u206A\u206A\u200C\u202C\u200E\u200F\u202D\u200D\u206E\u206F\u206E\u206C\u200C\u206D\u200C\u206C\u202A\u206A\u202C\u202B\u206A\u200F\u206E\u206F\u206A\u206E\u202D\u200B\u206D\u206B\u206C\u202D\u206E\u206E\u202D\u200D\u200F\u202C\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, (uint)num5, prevByte);
					num7 = -1861975787;
					continue;
				}
				case 23U:
				{
					uint num11 = num3;
					num7 = (int)(num8 * 3684156847U ^ 3554514709U);
					continue;
				}
				case 24U:
					num7 = (int)(((this.\u206F\u206A\u200C\u202C\u202B\u200E\u202E\u200B\u206D\u200F\u200B\u200D\u202D\u206B\u202C\u202D\u200F\u206C\u200D\u202C\u200F\u206B\u200D\u200B\u202A\u206E\u202E\u202E\u200F\u202B\u206C\u202C\u206B\u206F\u200C\u202D\u202A\u206A\u200F\u202B\u202E[(int)u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 0U) ? 1075671667U : 918362011U) ^ num8 * 2998681770U);
					continue;
				case 25U:
					num7 = ((this.\u206F\u202C\u202D\u206F\u206A\u202C\u202C\u206A\u206A\u200C\u202B\u206C\u206A\u202C\u200E\u206C\u206F\u200F\u202D\u200C\u202B\u200D\u202C\u200D\u200B\u202D\u202B\u200E\u202C\u206E\u202C\u202B\u200C\u206F\u206A\u206C\u202B\u202B\u200B\u206B\u202E[(int)u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E) != 1U) ? 405355357 : 1518651355);
					continue;
				case 26U:
					num7 = (((ulong)num < num5) ? -1850452312 : -1830082078);
					continue;
				case 27U:
					num7 = ((num != uint.MaxValue) ? -1797049479 : 2000013940);
					continue;
				case 28U:
				{
					uint num12;
					num = num12;
					num7 = 1479323397;
					continue;
				}
				case 29U:
				{
					num2 = num;
					uint num11;
					num = num11;
					num7 = 1843373184;
					continue;
				}
				case 30U:
				{
					uint num10;
					this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u200F\u206D\u206E\u206F\u202E\u206E\u200C\u200E\u206A\u200E\u202D\u202C\u206D\u206F\u206D\u200C\u206E\u200D\u200E\u200E\u200F\u206F\u202C\u202D\u202D\u206F\u202D\u200E\u206B\u206B\u202B\u206F\u200B\u200B\u206E\u206E\u200C\u200F\u202B\u202B\u202E(num, num10);
					num5 += (ulong)num10;
					num7 = 382539431;
					continue;
				}
				case 31U:
				{
					this.\u200F\u200B\u200C\u202B\u206A\u202B\u200E\u202B\u200D\u200C\u206C\u202D\u206A\u202E\u202A\u200E\u206F\u200B\u200C\u200C\u206B\u202E\u200F\u200B\u206F\u206D\u206B\u206D\u202D\u206C\u200D\u202B\u202E\u200D\u206A\u202A\u206E\u202D\u200C\u200F\u202E[(int)((int)u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E << 4)].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E);
					u202D_u206A_u202A_u202D_u202C_u202A_u206A_u206F_u206E_u200E_u206C_u200B_u202B_u206B_u206A_u206B_u200F_u200C_u202E_u206C_u202A_u202B_u200F_u206F_u206B_u200C_u206C_u200D_u206D_u206E_u206F_u206C_u206F_u202D_u200F_u200C_u206E_u202B_u206C_u200E_u202E.\u200C\u200E\u202E\u206A\u200C\u200E\u206B\u206B\u202A\u206D\u202A\u206C\u202E\u202C\u202D\u206D\u200F\u206C\u202E\u200D\u202E\u206C\u206F\u202A\u202A\u206F\u200C\u202A\u202B\u206F\u206A\u206F\u200B\u206C\u200F\u202D\u202E\u206E\u206D\u202C\u202E();
					byte b2 = this.\u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E.\u202B\u200D\u206A\u206A\u200C\u202C\u200E\u200F\u202D\u200D\u206E\u206F\u206E\u206C\u200C\u206D\u200C\u206C\u202A\u206A\u202C\u202B\u206A\u200F\u206E\u206F\u206A\u206E\u202D\u200B\u206D\u206B\u206C\u202D\u206E\u206E\u202D\u200D\u200F\u202C\u202E(this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E, 0U, 0);
					this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202A\u206E\u200C\u206E\u200E\u200C\u202D\u200C\u202B\u202C\u202C\u202B\u202A\u206D\u206D\u200F\u202E\u206B\u200B\u200D\u202A\u202B\u206B\u202E\u202C\u206B\u206A\u202A\u200C\u200B\u206F\u206A\u202C\u202B\u202A\u206E\u202E\u206A\u200B\u202C\u202E(b2);
					num5 += 1UL;
					num7 = (int)(num8 * 236868595U ^ 1128401066U);
					continue;
				}
				}
				break;
			}
			this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u206D\u200D\u206D\u206D\u202A\u200C\u200D\u200D\u206C\u202A\u200D\u202A\u202D\u206C\u206B\u200F\u206F\u206D\u202E\u200B\u206A\u202A\u200C\u202C\u206E\u200D\u200E\u200B\u200F\u202A\u202A\u206E\u200F\u206A\u206A\u200F\u206D\u200E\u202C\u200E\u202E();
			this.\u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E.\u202C\u202E\u200D\u202D\u202D\u206C\u200F\u202B\u206C\u206A\u200C\u202C\u202A\u200C\u200D\u202D\u206A\u200E\u202A\u202E\u200D\u202D\u206F\u200F\u202E\u206C\u206E\u206D\u202A\u200E\u202B\u206D\u202E\u206E\u206C\u202B\u200F\u200F\u200B\u206A\u202E();
			this.\u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E.\u202A\u202B\u206F\u202D\u206B\u202B\u206D\u206A\u200B\u206E\u200E\u200C\u202B\u200D\u206E\u200B\u200D\u202D\u206E\u200B\u200B\u206A\u206A\u206A\u206E\u200C\u200F\u202B\u202E\u206F\u206C\u206D\u202B\u202D\u206A\u206C\u206B\u202A\u200F\u202E\u202E();
			return;
			IL_31:
			num7 = -480741066;
			goto IL_36;
			IL_3A2:
			num7 = ((num5 >= (ulong)outSize) ? 2000013940 : -1231792784);
			goto IL_36;
		}

		// Token: 0x0600002F RID: 47 RVA: 0x000285AC File Offset: 0x000267AC
		internal void \u200F\u206A\u206B\u206A\u206B\u202E\u202E\u206C\u206A\u206D\u206D\u202C\u206E\u202C\u200D\u200F\u202C\u202B\u202D\u200D\u206F\u200C\u202D\u206D\u202D\u200F\u200B\u206C\u200E\u202C\u206B\u202E\u200F\u202B\u206F\u202D\u206D\u202A\u206F\u206E\u202E(byte[] properties)
		{
			int lc = (int)(properties[0] % 9);
			byte b = properties[0] / 9;
			int lp = (int)(b % 5);
			int pb = (int)(b / 5);
			uint num = 0U;
			int num2 = 0;
			for (;;)
			{
				IL_7E:
				int num3 = (num2 >= 4) ? 594337421 : 312511679;
				for (;;)
				{
					int num4 = num3;
					switch (-(num4 - (-(2086368361 * 787816086) ^ 741893051 * (1590620783 * -846093346))) % 4)
					{
					case 0:
						goto IL_7E;
					case 1:
						num += (uint)((uint)properties[1 + num2] << num2 * 8);
						num2++;
						num3 = -822592032;
						continue;
					case 2:
						num3 = 312511679;
						continue;
					}
					goto Block_1;
				}
			}
			Block_1:
			this.\u200E\u202C\u200F\u200B\u206D\u200B\u206F\u200E\u200E\u200B\u206A\u200E\u202E\u206F\u202B\u206E\u200B\u206D\u200F\u200D\u206B\u206D\u206C\u202B\u200D\u206B\u202C\u206E\u200D\u206C\u202E\u202D\u206B\u200E\u206E\u202D\u206D\u206F\u206E\u200D\u202E(num);
			this.\u200D\u202C\u206B\u200F\u200B\u206D\u200F\u202C\u200D\u206D\u206D\u206F\u206E\u206D\u202B\u206A\u200D\u206D\u200E\u200E\u202C\u202C\u200B\u202E\u202C\u206D\u200E\u206A\u202E\u200B\u200D\u200B\u206A\u206E\u206E\u200E\u206E\u206F\u200E\u200E\u202E(lp, lc);
			this.\u202D\u206D\u202B\u206C\u206A\u206A\u202E\u200F\u200C\u202B\u200C\u200D\u200E\u202D\u202E\u200C\u206D\u202A\u200D\u202C\u206A\u202D\u206A\u202C\u206A\u200E\u200F\u200B\u202E\u200C\u200E\u206F\u200C\u200C\u200B\u206E\u206D\u200F\u202B\u202B\u202E(pb);
		}

		// Token: 0x06000030 RID: 48 RVA: 0x00028664 File Offset: 0x00026864
		internal static uint \u202A\u200C\u206C\u206C\u206D\u206A\u200E\u206A\u206B\u200F\u206B\u200E\u200D\u206C\u206C\u206B\u202D\u200F\u202D\u202D\u206E\u206F\u206A\u202D\u206A\u206A\u200D\u206E\u206D\u206E\u206A\u202C\u202B\u200E\u200D\u206B\u202C\u202B\u206D\u206E\u202E(uint len)
		{
			len -= 2U;
			if (len < 4U)
			{
				for (;;)
				{
					int num = -889532400;
					switch (-((-(-(--1419486878)) - num) * 695684411 ^ -1584594270 + -1439489654) % 3)
					{
					case 0:
						continue;
					case 1:
						return len;
					}
					break;
				}
				return 3U;
			}
			return 3U;
		}

		// Token: 0x04000009 RID: 9
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u200F\u200B\u200C\u202B\u206A\u202B\u200E\u202B\u200D\u200C\u206C\u202D\u206A\u202E\u202A\u200E\u206F\u200B\u200C\u200C\u206B\u202E\u200F\u200B\u206F\u206D\u206B\u206D\u202D\u206C\u200D\u202B\u202E\u200D\u206A\u202A\u206E\u202D\u200C\u200F\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[192];

		// Token: 0x0400000A RID: 10
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u206A\u206C\u206F\u202A\u200F\u202D\u206E\u206F\u202B\u200C\u200B\u206D\u200E\u200D\u206D\u200F\u202E\u202D\u206E\u206A\u202D\u206F\u202E\u200B\u200F\u200F\u200F\u200E\u206E\u206F\u200C\u200B\u206C\u202B\u206F\u202E\u202B\u206E\u200E\u202C\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[192];

		// Token: 0x0400000B RID: 11
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u206F\u202C\u202D\u206F\u206A\u202C\u202C\u206A\u206A\u200C\u202B\u206C\u206A\u202C\u200E\u206C\u206F\u200F\u202D\u200C\u202B\u200D\u202C\u200D\u200B\u202D\u202B\u200E\u202C\u206E\u202C\u202B\u200C\u206F\u206A\u206C\u202B\u202B\u200B\u206B\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[12];

		// Token: 0x0400000C RID: 12
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u206F\u206A\u200C\u202C\u202B\u200E\u202E\u200B\u206D\u200F\u200B\u200D\u202D\u206B\u202C\u202D\u200F\u206C\u200D\u202C\u200F\u206B\u200D\u200B\u202A\u206E\u202E\u202E\u200F\u202B\u206C\u202C\u206B\u206F\u200C\u202D\u202A\u206A\u200F\u202B\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[12];

		// Token: 0x0400000D RID: 13
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u200D\u202A\u200B\u206C\u206E\u200D\u206D\u202A\u206B\u206D\u206E\u202D\u202C\u206F\u206D\u200E\u206B\u206E\u200D\u200F\u206A\u200C\u206E\u202D\u200D\u200E\u200B\u206B\u202C\u200E\u206D\u206A\u206B\u206D\u206B\u202A\u206C\u202D\u202A\u200F\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[12];

		// Token: 0x0400000E RID: 14
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u200D\u206D\u206E\u206C\u200D\u202E\u200C\u200D\u200D\u206D\u200B\u206A\u200F\u206B\u200C\u206C\u202B\u206E\u206D\u206D\u206D\u202C\u206C\u202B\u202E\u206C\u202B\u206D\u202E\u206A\u200B\u202D\u206B\u202D\u202C\u206D\u200B\u202C\u206A\u202D\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[12];

		// Token: 0x0400000F RID: 15
		internal readonly <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E \u202D\u202D\u206E\u202B\u206A\u206F\u200D\u206C\u202C\u202B\u206F\u202A\u202D\u206B\u202A\u206E\u206A\u202D\u202D\u202D\u206E\u202B\u200C\u206B\u206E\u202E\u202B\u206C\u206F\u202D\u206B\u200B\u200C\u206A\u206F\u202C\u200E\u200F\u202C\u206E\u202E = new <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E();

		// Token: 0x04000010 RID: 16
		internal readonly <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E \u202C\u200C\u200C\u206B\u200C\u202D\u202A\u206E\u200E\u202D\u202A\u206B\u200F\u206B\u200F\u202A\u202E\u206D\u206E\u206A\u206B\u200D\u202B\u202E\u200E\u206F\u202C\u200C\u202C\u202B\u206C\u200E\u200B\u202D\u206A\u206E\u202B\u206D\u200F\u200C\u202E = new <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E();

		// Token: 0x04000011 RID: 17
		internal readonly <Module>.\u206D\u202B\u202C\u200E\u202D\u202D\u206A\u202A\u202A\u200B\u202E\u200D\u200B\u200E\u200E\u202D\u200F\u202A\u200B\u202E\u200E\u202C\u200D\u206C\u206C\u206A\u200F\u206D\u206A\u206C\u200B\u200E\u202E\u200C\u206E\u206A\u202E\u202A\u206B\u206A\u202E \u200F\u200D\u206D\u202D\u206C\u202E\u206E\u200C\u202D\u206F\u206A\u202C\u202B\u206C\u200C\u202E\u202D\u206F\u206D\u200C\u206F\u202E\u200F\u200F\u200C\u202E\u202C\u202C\u200D\u202C\u200E\u206C\u202E\u200D\u206D\u206E\u206D\u200E\u200C\u206C\u202E = new <Module>.\u206D\u202B\u202C\u200E\u202D\u202D\u206A\u202A\u202A\u200B\u202E\u200D\u200B\u200E\u200E\u202D\u200F\u202A\u200B\u202E\u200E\u202C\u200D\u206C\u206C\u206A\u200F\u206D\u206A\u206C\u200B\u200E\u202E\u200C\u206E\u206A\u202E\u202A\u206B\u206A\u202E();

		// Token: 0x04000012 RID: 18
		internal readonly <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u206E\u202D\u202E\u206A\u202B\u200E\u200E\u200F\u200B\u202E\u206F\u206C\u200F\u206F\u202E\u206F\u200D\u202C\u200C\u202D\u200D\u206C\u202A\u200F\u202A\u202D\u206C\u202E\u202E\u200D\u206A\u206F\u206B\u202A\u202B\u202A\u206E\u206E\u202C\u206D\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[114];

		// Token: 0x04000013 RID: 19
		internal readonly <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[] \u202C\u206E\u206A\u202A\u200F\u200C\u200E\u206D\u200D\u200D\u206E\u200C\u206B\u200E\u200C\u202C\u202B\u200F\u202A\u200B\u200C\u200D\u206D\u206F\u200C\u202C\u206C\u202C\u206A\u200B\u200D\u202C\u200E\u202B\u206B\u206D\u206D\u206E\u200E\u200D\u202E = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[4];

		// Token: 0x04000014 RID: 20
		internal readonly <Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E \u206C\u206E\u202D\u206F\u200F\u202E\u206E\u206D\u202D\u206F\u202B\u206A\u202A\u202D\u206A\u200E\u206A\u202C\u200D\u206C\u206A\u202D\u206B\u200B\u200F\u206E\u206D\u202C\u200D\u206E\u200E\u200F\u206F\u206D\u206F\u206A\u202C\u206C\u206A\u206D\u202E = new <Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E();

		// Token: 0x04000015 RID: 21
		internal readonly <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E \u202B\u206A\u202B\u206E\u200D\u200D\u202D\u200E\u202D\u202E\u206F\u202D\u200C\u202E\u206C\u200E\u202E\u202A\u206B\u206E\u200D\u202D\u206B\u200B\u202C\u202C\u200F\u202B\u206D\u202D\u202C\u202C\u206B\u200C\u202A\u206F\u202D\u202A\u206B\u202E = new <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E();

		// Token: 0x04000016 RID: 22
		internal bool \u206B\u202B\u202E\u202D\u206F\u200B\u206A\u202D\u206E\u206C\u206B\u202D\u200E\u200C\u202E\u202D\u202A\u200C\u202A\u200D\u200D\u206E\u202B\u202E\u202D\u206B\u206A\u202D\u200C\u202A\u202A\u200B\u200D\u206D\u206B\u200D\u202C\u206F\u206C\u202E;

		// Token: 0x04000017 RID: 23
		internal uint \u206B\u206A\u200C\u206F\u206C\u206B\u200B\u206F\u200C\u202C\u206A\u206E\u206A\u200C\u202D\u202A\u200E\u202E\u202A\u202E\u206D\u206D\u200B\u200E\u206C\u200D\u200E\u202B\u206A\u206A\u206C\u200D\u202E\u202E\u202E\u202E\u206C\u206C\u200D\u202E\u202E;

		// Token: 0x04000018 RID: 24
		internal uint \u202A\u206E\u200F\u202B\u206A\u202B\u206D\u206C\u200C\u200E\u206C\u206C\u202C\u206B\u200B\u202C\u206E\u202A\u200C\u202C\u200E\u202E\u200D\u200B\u200E\u202E\u202E\u200E\u206F\u202E\u200B\u202A\u200D\u202C\u206F\u200D\u202B\u206F\u206C\u202A\u202E;

		// Token: 0x04000019 RID: 25
		internal <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E \u202D\u206F\u206E\u206E\u206A\u200E\u206A\u200D\u202B\u206B\u206C\u202C\u200D\u202A\u206E\u206C\u202A\u200C\u200E\u200C\u206E\u206E\u200E\u200E\u200E\u202A\u206E\u202B\u206B\u206D\u202D\u202A\u202A\u206C\u202C\u206D\u202E\u202E\u206D\u206F\u202E = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(4);

		// Token: 0x0400001A RID: 26
		internal uint \u202E\u206F\u200B\u202C\u200C\u206D\u206A\u202B\u206C\u200C\u206A\u200C\u200D\u202E\u206D\u200E\u202C\u200E\u200D\u202A\u206C\u206C\u200C\u202B\u206A\u206B\u202B\u200C\u206F\u200F\u206A\u202B\u200E\u200D\u200E\u202E\u202B\u202D\u200F\u202E;

		// Token: 0x02000006 RID: 6
		internal class \u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E
		{
			// Token: 0x06000031 RID: 49 RVA: 0x000286C8 File Offset: 0x000268C8
			internal void \u200F\u202C\u206A\u206A\u202E\u206F\u200C\u206E\u206B\u206A\u202A\u206A\u202D\u200C\u202B\u202D\u200D\u206B\u200C\u200F\u202A\u202B\u200B\u200C\u200E\u206F\u200E\u200C\u206F\u206F\u206C\u202B\u202E\u200D\u206C\u206F\u206F\u200C\u200B\u200E\u202E(uint numPosStates)
			{
				uint num = this.\u206C\u200D\u202C\u206D\u206D\u206A\u206A\u206A\u206A\u206D\u202B\u200E\u202E\u200D\u202C\u206C\u202B\u206E\u202C\u200B\u200F\u200C\u200D\u202A\u200F\u200D\u200F\u206D\u206D\u202A\u206B\u200B\u202C\u206D\u206E\u206D\u206B\u206E\u202E\u206C\u202E;
				for (;;)
				{
					IL_70:
					int num2 = (num < numPosStates) ? 576983198 : 148411456;
					for (;;)
					{
						int num3 = num2;
						switch (~(-(~1088899899) - -num3 - (1900570653 ^ -551066524)) % 4)
						{
						case 1:
							goto IL_70;
						case 2:
							this.\u206B\u202A\u206D\u200D\u206D\u206D\u200C\u200C\u200B\u206D\u206B\u202E\u206D\u200D\u202C\u202D\u200D\u202E\u202C\u206F\u202C\u202B\u200B\u202C\u200B\u206B\u202D\u200E\u206C\u200D\u206B\u206C\u200F\u202D\u202C\u206B\u200B\u202E\u200E\u206A\u202E[(int)num] = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(3);
							this.\u202A\u202B\u206A\u200B\u206A\u202E\u206A\u206C\u200E\u206D\u200B\u200E\u206D\u202A\u206E\u202C\u206F\u202C\u202E\u206C\u202A\u202B\u202A\u206E\u202B\u206D\u206A\u206B\u202B\u206D\u206D\u206B\u206E\u200E\u206C\u202E\u202A\u202B\u202C\u206C\u202E[(int)num] = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(3);
							num += 1U;
							num2 = -189351029;
							continue;
						case 3:
							num2 = 576983198;
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:
				this.\u206C\u200D\u202C\u206D\u206D\u206A\u206A\u206A\u206A\u206D\u202B\u200E\u202E\u200D\u202C\u206C\u202B\u206E\u202C\u200B\u200F\u200C\u200D\u202A\u200F\u200D\u200F\u206D\u206D\u202A\u206B\u200B\u202C\u206D\u206E\u206D\u206B\u206E\u202E\u206C\u202E = numPosStates;
			}

			// Token: 0x06000032 RID: 50 RVA: 0x00028764 File Offset: 0x00026964
			internal void \u206D\u206C\u202C\u206C\u202D\u200D\u200D\u200E\u206B\u206E\u200C\u206A\u206F\u202B\u200B\u200E\u200E\u200D\u202D\u202B\u206D\u206F\u202C\u202A\u206F\u206F\u206C\u202B\u200B\u202D\u202A\u200C\u202A\u200B\u206C\u200B\u202E\u206B\u200D\u206D\u202E()
			{
				this.\u202D\u202C\u202B\u206B\u200E\u206C\u200F\u206B\u202E\u206E\u206E\u200D\u206E\u202D\u206B\u202A\u202C\u206B\u200C\u202B\u206D\u200C\u200C\u200E\u206C\u200C\u206C\u202B\u202C\u202E\u202D\u206E\u206F\u202B\u202A\u202D\u200D\u202D\u206B\u206B\u202E.\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
				uint num = 0U;
				for (;;)
				{
					IL_A2:
					int num2 = (num < this.\u206C\u200D\u202C\u206D\u206D\u206A\u206A\u206A\u206A\u206D\u202B\u200E\u202E\u200D\u202C\u206C\u202B\u206E\u202C\u200B\u200F\u200C\u200D\u202A\u200F\u200D\u200F\u206D\u206D\u202A\u206B\u200B\u202C\u206D\u206E\u206D\u206B\u206E\u202E\u206C\u202E) ? 890710394 : -1808183963;
					for (;;)
					{
						int num3 = num2;
						switch (~(1313633285 - 1995119801 - (num3 - (~-333251715 - (-48431197 ^ -2081043068) - -1096706693) - ((-443562828 ^ 2035229790) - (-1160161556 ^ -1896299024)))) % 4)
						{
						case 0:
							num2 = 890710394;
							continue;
						case 1:
							goto IL_A2;
						case 3:
							this.\u206B\u202A\u206D\u200D\u206D\u206D\u200C\u200C\u200B\u206D\u206B\u202E\u206D\u200D\u202C\u202D\u200D\u202E\u202C\u206F\u202C\u202B\u200B\u202C\u200B\u206B\u202D\u200E\u206C\u200D\u206B\u206C\u200F\u202D\u202C\u206B\u200B\u202E\u200E\u206A\u202E[(int)num].\u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E();
							this.\u202A\u202B\u206A\u200B\u206A\u202E\u206A\u206C\u200E\u206D\u200B\u200E\u206D\u202A\u206E\u202C\u206F\u202C\u202E\u206C\u202A\u202B\u202A\u206E\u202B\u206D\u206A\u206B\u202B\u206D\u206D\u206B\u206E\u200E\u206C\u202E\u202A\u202B\u202C\u206C\u202E[(int)num].\u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E();
							num += 1U;
							num2 = 1954058656;
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:
				this.\u200E\u206A\u206B\u200D\u206D\u202D\u206C\u202E\u200C\u206B\u206B\u200D\u202C\u200C\u202B\u200B\u202E\u202E\u206A\u202C\u202D\u206A\u200C\u202A\u206F\u202D\u202E\u200C\u206F\u206F\u200E\u206E\u200B\u202D\u200C\u200C\u206B\u200E\u206A\u202B\u202E.\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
				this.\u202C\u206A\u202A\u200D\u206C\u200B\u202C\u202D\u202D\u202A\u200E\u206F\u202E\u206C\u200E\u202B\u200E\u206A\u202C\u200C\u206D\u206C\u206D\u200D\u200D\u202D\u202D\u200F\u206A\u202C\u206F\u206A\u202A\u200B\u206C\u206D\u202D\u200E\u200B\u206C\u202E.\u202E\u206A\u206D\u206C\u200B\u206A\u202D\u202B\u206E\u206D\u200D\u202A\u202C\u200B\u206C\u206F\u200B\u200C\u202A\u206B\u200F\u200E\u202B\u206F\u202E\u200D\u202B\u202D\u200B\u206B\u202E\u200C\u202B\u202E\u202A\u206D\u202A\u202C\u206A\u202D\u202E();
			}

			// Token: 0x06000033 RID: 51 RVA: 0x00028848 File Offset: 0x00026A48
			internal uint \u202E\u206F\u202A\u200C\u202C\u202D\u206C\u202B\u200E\u206C\u202B\u206F\u206A\u200D\u206D\u202D\u200D\u200F\u206C\u206F\u206A\u200F\u202C\u200F\u200B\u206E\u202A\u200D\u200B\u202A\u202B\u202A\u206D\u206D\u206E\u200F\u206A\u202A\u200D\u200D\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder, uint posState)
			{
				if (this.\u202D\u202C\u202B\u206B\u200E\u206C\u200F\u206B\u202E\u206E\u206E\u200D\u206E\u202D\u206B\u202A\u202C\u206B\u200C\u202B\u206D\u200C\u200C\u200E\u206C\u200C\u206C\u202B\u202C\u202E\u202D\u206E\u206F\u202B\u202A\u202D\u200D\u202D\u206B\u206B\u202E.\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder) == 0U)
				{
					goto IL_0E;
				}
				goto IL_6C;
				int num2;
				uint num4;
				for (;;)
				{
					IL_13:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(~(uint)(-(-(16621607 + 122149805)) - num))) % 6U)
					{
					case 0U:
						goto IL_0E;
					case 1U:
						goto IL_4A;
					case 2U:
						goto IL_6C;
					case 3U:
						num4 += this.\u202A\u202B\u206A\u200B\u206A\u202E\u206A\u206C\u200E\u206D\u200B\u200E\u206D\u202A\u206E\u202C\u206F\u202C\u202E\u206C\u202A\u202B\u202A\u206E\u202B\u206D\u206A\u206B\u202B\u206D\u206D\u206B\u206E\u200E\u206C\u202E\u202A\u202B\u202C\u206C\u202E[(int)posState].\u200B\u202B\u200F\u206E\u206E\u200C\u202A\u206A\u202D\u206A\u200D\u200B\u206D\u202D\u206D\u202E\u200E\u200F\u206B\u206A\u206A\u202E\u206B\u206A\u202E\u200E\u206F\u202D\u206A\u202C\u200C\u200E\u200C\u200F\u206B\u202B\u202C\u200E\u206F\u202D\u202E(rangeDecoder);
						num2 = (int)(num3 * 1753495747U ^ 1690632138U);
						continue;
					case 5U:
						num4 += 8U;
						num4 += this.\u202C\u206A\u202A\u200D\u206C\u200B\u202C\u202D\u202D\u202A\u200E\u206F\u202E\u206C\u200E\u202B\u200E\u206A\u202C\u200C\u206D\u206C\u206D\u200D\u200D\u202D\u202D\u200F\u206A\u202C\u206F\u206A\u202A\u200B\u206C\u206D\u202D\u200E\u200B\u206C\u202E.\u200B\u202B\u200F\u206E\u206E\u200C\u202A\u206A\u202D\u206A\u200D\u200B\u206D\u202D\u206D\u202E\u200E\u200F\u206B\u206A\u206A\u202E\u206B\u206A\u202E\u200E\u206F\u202D\u206A\u202C\u200C\u200E\u200C\u200F\u206B\u202B\u202C\u200E\u206F\u202D\u202E(rangeDecoder);
						num2 = 734454825;
						continue;
					}
					break;
				}
				return num4;
				IL_4A:
				return this.\u206B\u202A\u206D\u200D\u206D\u206D\u200C\u200C\u200B\u206D\u206B\u202E\u206D\u200D\u202C\u202D\u200D\u202E\u202C\u206F\u202C\u202B\u200B\u202C\u200B\u206B\u202D\u200E\u206C\u200D\u206B\u206C\u200F\u202D\u202C\u206B\u200B\u202E\u200E\u206A\u202E[(int)posState].\u200B\u202B\u200F\u206E\u206E\u200C\u202A\u206A\u202D\u206A\u200D\u200B\u206D\u202D\u206D\u202E\u200E\u200F\u206B\u206A\u206A\u202E\u206B\u206A\u202E\u200E\u206F\u202D\u206A\u202C\u200C\u200E\u200C\u200F\u206B\u202B\u202C\u200E\u206F\u202D\u202E(rangeDecoder);
				IL_0E:
				num2 = 722676882;
				goto IL_13;
				IL_6C:
				num4 = 8U;
				num2 = ((this.\u200E\u206A\u206B\u200D\u206D\u202D\u206C\u202E\u200C\u206B\u206B\u200D\u202C\u200C\u202B\u200B\u202E\u202E\u206A\u202C\u202D\u206A\u200C\u202A\u206F\u202D\u202E\u200C\u206F\u206F\u200E\u206E\u200B\u202D\u200C\u200C\u206B\u200E\u206A\u202B\u202E.\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder) == 0U) ? 1994447414 : 1320107692);
				goto IL_13;
			}

			// Token: 0x06000034 RID: 52 RVA: 0x00028928 File Offset: 0x00026B28
			internal \u200C\u206F\u200B\u202E\u200E\u206C\u206F\u202A\u200C\u206F\u206C\u206B\u206F\u206C\u206F\u202E\u202C\u200B\u202D\u200C\u202D\u206A\u206F\u206F\u202D\u202E\u200F\u200F\u200F\u200E\u200F\u200F\u200B\u202A\u200F\u206B\u202A\u206A\u202B\u206E\u202E()
			{
			}

			// Token: 0x0400001B RID: 27
			internal readonly <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[] \u206B\u202A\u206D\u200D\u206D\u206D\u200C\u200C\u200B\u206D\u206B\u202E\u206D\u200D\u202C\u202D\u200D\u202E\u202C\u206F\u202C\u202B\u200B\u202C\u200B\u206B\u202D\u200E\u206C\u200D\u206B\u206C\u200F\u202D\u202C\u206B\u200B\u202E\u200E\u206A\u202E = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[16];

			// Token: 0x0400001C RID: 28
			internal readonly <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[] \u202A\u202B\u206A\u200B\u206A\u202E\u206A\u206C\u200E\u206D\u200B\u200E\u206D\u202A\u206E\u202C\u206F\u202C\u202E\u206C\u202A\u202B\u202A\u206E\u202B\u206D\u206A\u206B\u202B\u206D\u206D\u206B\u206E\u200E\u206C\u202E\u202A\u202B\u202C\u206C\u202E = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E[16];

			// Token: 0x0400001D RID: 29
			internal <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E \u202D\u202C\u202B\u206B\u200E\u206C\u200F\u206B\u202E\u206E\u206E\u200D\u206E\u202D\u206B\u202A\u202C\u206B\u200C\u202B\u206D\u200C\u200C\u200E\u206C\u200C\u206C\u202B\u202C\u202E\u202D\u206E\u206F\u202B\u202A\u202D\u200D\u202D\u206B\u206B\u202E;

			// Token: 0x0400001E RID: 30
			internal <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E \u200E\u206A\u206B\u200D\u206D\u202D\u206C\u202E\u200C\u206B\u206B\u200D\u202C\u200C\u202B\u200B\u202E\u202E\u206A\u202C\u202D\u206A\u200C\u202A\u206F\u202D\u202E\u200C\u206F\u206F\u200E\u206E\u200B\u202D\u200C\u200C\u206B\u200E\u206A\u202B\u202E;

			// Token: 0x0400001F RID: 31
			internal <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E \u202C\u206A\u202A\u200D\u206C\u200B\u202C\u202D\u202D\u202A\u200E\u206F\u202E\u206C\u200E\u202B\u200E\u206A\u202C\u200C\u206D\u206C\u206D\u200D\u200D\u202D\u202D\u200F\u206A\u202C\u206F\u206A\u202A\u200B\u206C\u206D\u202D\u200E\u200B\u206C\u202E = new <Module>.\u206D\u202A\u206E\u202C\u200B\u206B\u200E\u200B\u200E\u200D\u200B\u202A\u200E\u202E\u200F\u206C\u200D\u202E\u202B\u206D\u202B\u202C\u202D\u200E\u206B\u202D\u202D\u200F\u206B\u202E\u202B\u206E\u206A\u202D\u202C\u202C\u200D\u206C\u206D\u202B\u202E(8);

			// Token: 0x04000020 RID: 32
			internal uint \u206C\u200D\u202C\u206D\u206D\u206A\u206A\u206A\u206A\u206D\u202B\u200E\u202E\u200D\u202C\u206C\u202B\u206E\u202C\u200B\u200F\u200C\u200D\u202A\u200F\u200D\u200F\u206D\u206D\u202A\u206B\u200B\u202C\u206D\u206E\u206D\u206B\u206E\u202E\u206C\u202E;
		}

		// Token: 0x02000007 RID: 7
		internal class \u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E
		{
			// Token: 0x06000035 RID: 53 RVA: 0x00028964 File Offset: 0x00026B64
			internal void \u206C\u200B\u202A\u202D\u202A\u200D\u200F\u202D\u206D\u202E\u206E\u200D\u206A\u206E\u202C\u200E\u200B\u202D\u206D\u200B\u206D\u202C\u206A\u202B\u200F\u202B\u206F\u206E\u202A\u206F\u206C\u206E\u206B\u200C\u200F\u206F\u206D\u200E\u202B\u202A\u202E(int numPosBits, int numPrevBits)
			{
				if (this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E != null)
				{
					goto IL_0B;
				}
				goto IL_C1;
				int num2;
				for (;;)
				{
					IL_10:
					int num = num2;
					uint num3;
					switch ((num3 = (uint)(-(uint)(-(uint)(num ^ -232208271 + (-1137015687 ^ 1231155014) + ((2006019793 ^ 1673705419) - 1779591131) ^ -(82048485 * -1552153792))))) % 10U)
					{
					case 0U:
						num2 = (int)(((this.\u202E\u200C\u202B\u206B\u206A\u206F\u206B\u206C\u200D\u206C\u206A\u206D\u202C\u206E\u202A\u202A\u206E\u202E\u202D\u200C\u200D\u202E\u202C\u202D\u202D\u200B\u200C\u202A\u206E\u202E\u206D\u206A\u206D\u206E\u200D\u202E\u206B\u206C\u200B\u206F\u202E == numPosBits) ? 2430544547U : 3578235774U) ^ num3 * 748390768U);
						continue;
					case 1U:
						goto IL_C1;
					case 2U:
					{
						this.\u206D\u206A\u206D\u206E\u200B\u200F\u202E\u200B\u206C\u206F\u200B\u200E\u202C\u202B\u202B\u202A\u206F\u200C\u206A\u206E\u206F\u206A\u200E\u200B\u206C\u202D\u206B\u206C\u202A\u200D\u200C\u206A\u200D\u202C\u200E\u202A\u202D\u206A\u206A\u206E\u202E = (1U << numPosBits) - 1U;
						this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E = numPrevBits;
						uint num4 = 1U << this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E + this.\u202E\u200C\u202B\u206B\u206A\u206F\u206B\u206C\u200D\u206C\u206A\u206D\u202C\u206E\u202A\u202A\u206E\u202E\u202D\u200C\u200D\u202E\u202C\u202D\u202D\u200B\u200C\u202A\u206E\u202E\u206D\u206A\u206D\u206E\u200D\u202E\u206B\u206C\u200B\u206F\u202E;
						num2 = (int)(num3 * 2300040976U ^ 2899262468U);
						continue;
					}
					case 3U:
					{
						uint num4;
						this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E = new <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E.\u200F\u206A\u202A\u206C\u200E\u206D\u206D\u202E\u200C\u206A\u206D\u202B\u206A\u206A\u200C\u206E\u206B\u202A\u202C\u206F\u200E\u202C\u202D\u206E\u206C\u206A\u206B\u206D\u200D\u206F\u202C\u206A\u202C\u202B\u200D\u200D\u202D\u206B\u202E\u202D\u202E[num4];
						uint num5 = 0U;
						num2 = (int)(num3 * 1894456327U ^ 1604623497U);
						continue;
					}
					case 4U:
						return;
					case 5U:
					{
						uint num4;
						uint num5;
						num2 = ((num5 >= num4) ? -420347984 : -421048782);
						continue;
					}
					case 6U:
						num2 = (int)(((this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E != numPrevBits) ? 3554965862U : 3552223083U) ^ num3 * 4289120844U);
						continue;
					case 7U:
					{
						uint num5;
						this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E[(int)num5].\u202A\u200D\u206C\u202D\u200E\u206C\u200E\u202A\u206B\u200B\u200B\u200C\u202B\u202D\u202E\u206B\u202D\u206B\u206A\u202D\u200E\u202E\u202D\u200E\u206E\u200D\u200F\u206C\u206A\u202D\u206D\u206E\u200F\u202E\u200B\u206C\u200C\u202E\u206E\u206A\u202E();
						num5 += 1U;
						num2 = -1185046268;
						continue;
					}
					case 8U:
						goto IL_0B;
					}
					break;
				}
				return;
				IL_0B:
				num2 = -1750397443;
				goto IL_10;
				IL_C1:
				this.\u202E\u200C\u202B\u206B\u206A\u206F\u206B\u206C\u200D\u206C\u206A\u206D\u202C\u206E\u202A\u202A\u206E\u202E\u202D\u200C\u200D\u202E\u202C\u202D\u202D\u200B\u200C\u202A\u206E\u202E\u206D\u206A\u206D\u206E\u200D\u202E\u206B\u206C\u200B\u206F\u202E = numPosBits;
				num2 = -1359633103;
				goto IL_10;
			}

			// Token: 0x06000036 RID: 54 RVA: 0x00028AF0 File Offset: 0x00026CF0
			internal void \u200D\u200F\u206F\u206B\u206D\u206C\u206C\u206F\u200B\u206E\u206A\u206E\u200F\u200F\u200B\u200B\u206A\u200B\u206C\u200E\u206F\u206F\u202E\u202B\u202C\u206A\u200F\u206E\u206B\u200F\u202C\u200F\u200E\u202B\u200F\u206E\u206C\u202E\u206B\u206C\u202E()
			{
				uint num = 1U << this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E + this.\u202E\u200C\u202B\u206B\u206A\u206F\u206B\u206C\u200D\u206C\u206A\u206D\u202C\u206E\u202A\u202A\u206E\u202E\u202D\u200C\u200D\u202E\u202C\u202D\u202D\u200B\u200C\u202A\u206E\u202E\u206D\u206A\u206D\u206E\u200D\u202E\u206B\u206C\u200B\u206F\u202E;
				uint num2 = 0U;
				for (;;)
				{
					IL_58:
					int num3 = (num2 >= num) ? -409872115 : -170433444;
					for (;;)
					{
						int num4 = num3;
						switch (~(~(~num4)) % 4)
						{
						case 0:
							num3 = -170433444;
							continue;
						case 1:
							goto IL_58;
						case 3:
							this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E[(int)num2].\u202A\u202C\u200D\u200D\u200D\u202C\u206D\u200B\u206E\u206C\u202A\u206A\u200B\u200B\u200F\u202B\u206F\u206F\u200B\u200E\u206A\u200D\u206C\u202C\u200B\u206F\u200C\u206B\u206F\u202B\u202C\u200D\u206F\u206B\u206B\u200C\u200C\u200B\u206E\u206F\u202E();
							num2 += 1U;
							num3 = -443382110;
							continue;
						}
						return;
					}
				}
			}

			// Token: 0x06000037 RID: 55 RVA: 0x00028B6C File Offset: 0x00026D6C
			internal uint \u206E\u202E\u206D\u202E\u202B\u206F\u206E\u200E\u206E\u200B\u206F\u200E\u200E\u202B\u206D\u200B\u200B\u202C\u206C\u206A\u200D\u202C\u200F\u200D\u200D\u200B\u206B\u206B\u202D\u206F\u200E\u200F\u206B\u206A\u202D\u200E\u200C\u200B\u202A\u200D\u202E(uint pos, byte prevByte)
			{
				return ((pos & this.\u206D\u206A\u206D\u206E\u200B\u200F\u202E\u200B\u206C\u206F\u200B\u200E\u202C\u202B\u202B\u202A\u206F\u200C\u206A\u206E\u206F\u206A\u200E\u200B\u206C\u202D\u206B\u206C\u202A\u200D\u200C\u206A\u200D\u202C\u200E\u202A\u202D\u206A\u206A\u206E\u202E) << this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E) + (uint)(prevByte >> 8 - this.\u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E);
			}

			// Token: 0x06000038 RID: 56 RVA: 0x00028B9C File Offset: 0x00026D9C
			internal byte \u202B\u200D\u206A\u206A\u200C\u202C\u200E\u200F\u202D\u200D\u206E\u206F\u206E\u206C\u200C\u206D\u200C\u206C\u202A\u206A\u202C\u202B\u206A\u200F\u206E\u206F\u206A\u206E\u202D\u200B\u206D\u206B\u206C\u202D\u206E\u206E\u202D\u200D\u200F\u202C\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder, uint pos, byte prevByte)
			{
				return this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E[(int)this.\u206E\u202E\u206D\u202E\u202B\u206F\u206E\u200E\u206E\u200B\u206F\u200E\u200E\u202B\u206D\u200B\u200B\u202C\u206C\u206A\u200D\u202C\u200F\u200D\u200D\u200B\u206B\u206B\u202D\u206F\u200E\u200F\u206B\u206A\u202D\u200E\u200C\u200B\u202A\u200D\u202E(pos, prevByte)].\u206E\u206E\u202E\u202A\u202C\u206D\u202E\u206E\u202B\u206F\u206B\u200F\u206E\u200F\u206B\u206A\u200C\u206F\u206C\u202D\u202D\u200F\u202B\u206E\u202B\u200D\u202C\u206B\u206C\u206D\u200E\u200E\u202B\u206C\u202E\u200F\u202B\u202D\u200E\u202C\u202E(rangeDecoder);
			}

			// Token: 0x06000039 RID: 57 RVA: 0x00028BC4 File Offset: 0x00026DC4
			internal byte \u202A\u202E\u200F\u202E\u202A\u206A\u200F\u202A\u200B\u202A\u202E\u202D\u202C\u206F\u202D\u202C\u200F\u202E\u206B\u200E\u206F\u206E\u202B\u202D\u200B\u206D\u200B\u206B\u206F\u200C\u200E\u202C\u200B\u206A\u202C\u200D\u202C\u200D\u200E\u206A\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder, uint pos, byte prevByte, byte matchByte)
			{
				return this.\u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E[(int)this.\u206E\u202E\u206D\u202E\u202B\u206F\u206E\u200E\u206E\u200B\u206F\u200E\u200E\u202B\u206D\u200B\u200B\u202C\u206C\u206A\u200D\u202C\u200F\u200D\u200D\u200B\u206B\u206B\u202D\u206F\u200E\u200F\u206B\u206A\u202D\u200E\u200C\u200B\u202A\u200D\u202E(pos, prevByte)].\u200B\u202E\u206C\u206B\u200B\u202E\u206A\u200B\u206E\u206D\u202B\u206A\u200C\u200B\u202E\u200B\u200D\u206D\u200F\u202E\u200F\u200E\u202B\u206B\u200C\u202D\u202A\u206A\u202A\u200F\u206A\u202B\u206A\u202D\u200C\u206F\u206E\u202D\u206D\u202A\u202E(rangeDecoder, matchByte);
			}

			// Token: 0x0600003A RID: 58 RVA: 0x00027BBC File Offset: 0x00025DBC
			internal \u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E()
			{
			}

			// Token: 0x04000021 RID: 33
			internal <Module>.\u200D\u202C\u206E\u206E\u200E\u206F\u202A\u202A\u200E\u202D\u200F\u202B\u206B\u200D\u200D\u206E\u200F\u200B\u202D\u206F\u206C\u200B\u200D\u200B\u200F\u200C\u206C\u206F\u202C\u206B\u200B\u200C\u200D\u206B\u206E\u202A\u202A\u202E\u206D\u206F\u202E.\u206D\u202B\u202A\u206E\u202B\u206C\u200C\u206B\u206A\u206A\u200F\u200E\u200D\u206C\u200C\u202A\u206E\u202D\u200C\u206C\u206F\u202C\u206C\u206E\u202C\u202C\u200F\u202E\u202D\u202E\u206F\u202B\u200D\u202C\u206A\u200D\u206D\u206B\u202E\u202E\u202E.\u200F\u206A\u202A\u206C\u200E\u206D\u206D\u202E\u200C\u206A\u206D\u202B\u206A\u206A\u200C\u206E\u206B\u202A\u202C\u206F\u200E\u202C\u202D\u206E\u206C\u206A\u206B\u206D\u200D\u206F\u202C\u206A\u202C\u202B\u200D\u200D\u202D\u206B\u202E\u202D\u202E[] \u202C\u202A\u206B\u202C\u206F\u206A\u206F\u200D\u202C\u200B\u202A\u200B\u202C\u202A\u200C\u202A\u202E\u206B\u206C\u200B\u200E\u200D\u206C\u200E\u206A\u202A\u206E\u202C\u206F\u200E\u200D\u202D\u200C\u200B\u202E\u206C\u202E\u200C\u206B\u202D\u202E;

			// Token: 0x04000022 RID: 34
			internal int \u202E\u200C\u202B\u206B\u206A\u206F\u206B\u206C\u200D\u206C\u206A\u206D\u202C\u206E\u202A\u202A\u206E\u202E\u202D\u200C\u200D\u202E\u202C\u202D\u202D\u200B\u200C\u202A\u206E\u202E\u206D\u206A\u206D\u206E\u200D\u202E\u206B\u206C\u200B\u206F\u202E;

			// Token: 0x04000023 RID: 35
			internal int \u202B\u200E\u206C\u200D\u202A\u206E\u206F\u206C\u206B\u200D\u200E\u202A\u202D\u200E\u202E\u202D\u206C\u202D\u200E\u206D\u206A\u200C\u202C\u202A\u206F\u206F\u206F\u202C\u202D\u202C\u202A\u202E\u202E\u206F\u206A\u200E\u202E\u202E\u206D\u200D\u202E;

			// Token: 0x04000024 RID: 36
			internal uint \u206D\u206A\u206D\u206E\u200B\u200F\u202E\u200B\u206C\u206F\u200B\u200E\u202C\u202B\u202B\u202A\u206F\u200C\u206A\u206E\u206F\u206A\u200E\u200B\u206C\u202D\u206B\u206C\u202A\u200D\u200C\u206A\u200D\u202C\u200E\u202A\u202D\u206A\u206A\u206E\u202E;

			// Token: 0x02000008 RID: 8
			internal struct \u200F\u206A\u202A\u206C\u200E\u206D\u206D\u202E\u200C\u206A\u206D\u202B\u206A\u206A\u200C\u206E\u206B\u202A\u202C\u206F\u200E\u202C\u202D\u206E\u206C\u206A\u206B\u206D\u200D\u206F\u202C\u206A\u202C\u202B\u200D\u200D\u202D\u206B\u202E\u202D\u202E
			{
				// Token: 0x0600003B RID: 59 RVA: 0x00028BEC File Offset: 0x00026DEC
				internal void \u202A\u200D\u206C\u202D\u200E\u206C\u200E\u202A\u206B\u200B\u200B\u200C\u202B\u202D\u202E\u206B\u202D\u206B\u206A\u202D\u200E\u202E\u202D\u200E\u206E\u200D\u200F\u206C\u206A\u202D\u206D\u206E\u200F\u202E\u200B\u206C\u200C\u202E\u206E\u206A\u202E()
				{
					this.\u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E = new <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[768];
				}

				// Token: 0x0600003C RID: 60 RVA: 0x00028C0C File Offset: 0x00026E0C
				internal void \u202A\u202C\u200D\u200D\u200D\u202C\u206D\u200B\u206E\u206C\u202A\u206A\u200B\u200B\u200F\u202B\u206F\u206F\u200B\u200E\u206A\u200D\u206C\u202C\u200B\u206F\u200C\u206B\u206F\u202B\u202C\u200D\u206F\u206B\u206B\u200C\u200C\u200B\u206E\u206F\u202E()
				{
					int num = 0;
					for (;;)
					{
						IL_68:
						int num2 = (num >= 768) ? -374657097 : -980089756;
						for (;;)
						{
							int num3 = num2;
							switch (((num3 ^ (1761711058 ^ -(1268048772 ^ 1872253838))) + -1706441811 - --331687764 + 741503972) % 4)
							{
							case 1:
								this.\u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E[num].\u206F\u200E\u202E\u200F\u200D\u206B\u206A\u206B\u200C\u206A\u206F\u206C\u206B\u202C\u202C\u200B\u206E\u200F\u206D\u200E\u206F\u206E\u206C\u206D\u206A\u202A\u206A\u202B\u200C\u202C\u202D\u206F\u206E\u202B\u202C\u206D\u206E\u202E\u206C\u206B\u202E();
								num++;
								num2 = 154532394;
								continue;
							case 2:
								num2 = -980089756;
								continue;
							case 3:
								goto IL_68;
							}
							return;
						}
					}
				}

				// Token: 0x0600003D RID: 61 RVA: 0x00028C9C File Offset: 0x00026E9C
				internal byte \u206E\u206E\u202E\u202A\u202C\u206D\u202E\u206E\u202B\u206F\u206B\u200F\u206E\u200F\u206B\u206A\u200C\u206F\u206C\u202D\u202D\u200F\u202B\u206E\u202B\u200D\u202C\u206B\u206C\u206D\u200E\u200E\u202B\u206C\u202E\u200F\u202B\u202D\u200E\u202C\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder)
				{
					uint num = 1U;
					for (;;)
					{
						IL_02:
						int num2 = 730977655;
						for (;;)
						{
							int num3 = num2;
							switch (~(~num3 - (1639922981 ^ 1321758916) * 701852685 - (1166945816 - -640563692)) % 3)
							{
							case 0:
								goto IL_02;
							case 1:
								num = (num << 1 | this.\u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E[(int)num].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder));
								num2 = ((num >= 256U) ? 755274770 : 730977655);
								continue;
							}
							goto Block_1;
						}
					}
					Block_1:
					return (byte)num;
				}

				// Token: 0x0600003E RID: 62 RVA: 0x00028D1C File Offset: 0x00026F1C
				internal byte \u200B\u202E\u206C\u206B\u200B\u202E\u206A\u200B\u206E\u206D\u202B\u206A\u200C\u200B\u202E\u200B\u200D\u206D\u200F\u202E\u200F\u200E\u202B\u206B\u200C\u202D\u202A\u206A\u202A\u200F\u206A\u202B\u206A\u202D\u200C\u206F\u206E\u202D\u206D\u202A\u202E(<Module>.\u206C\u206E\u202E\u206F\u202D\u206A\u202A\u202B\u200B\u206C\u206E\u206D\u206F\u206F\u200D\u206D\u206B\u206D\u206A\u200E\u202A\u202E\u206B\u206B\u200B\u200D\u206D\u206E\u206A\u206F\u202C\u202D\u200C\u200F\u206E\u200D\u202A\u206D\u202E\u202E rangeDecoder, byte matchByte)
				{
					uint num = 1U;
					for (;;)
					{
						IL_02:
						int num2 = -763537267;
						for (;;)
						{
							int num3 = num2;
							uint num4;
							switch ((num4 = (uint)(~(uint)((num3 ^ (1414119186 + 1958366601 ^ (1195437460 ^ -1605276972)) * 1733232445) + (-868485666 - -1119293729) + (1483416619 ^ -266707036)))) % 8U)
							{
							case 0U:
								num = (num << 1 | this.\u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E[(int)num].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder));
								num2 = 70444162;
								continue;
							case 1U:
								num2 = (int)(num4 * 4186586532U ^ 1578592356U);
								continue;
							case 3U:
								goto IL_02;
							case 4U:
								num2 = ((num >= 256U) ? -690794665 : 215906934);
								continue;
							case 5U:
								num2 = (int)(num4 * 258512724U ^ 2415250118U);
								continue;
							case 6U:
								num2 = ((num < 256U) ? -763537267 : 238412000);
								continue;
							case 7U:
							{
								uint num5 = (uint)(matchByte >> 7 & 1);
								matchByte = (byte)(matchByte << 1);
								uint num6 = this.\u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E[(int)((1U + num5 << 8) + num)].\u206A\u206A\u206A\u200D\u202A\u206B\u202C\u202C\u206D\u206A\u200B\u206C\u202E\u206B\u202E\u200E\u200E\u200F\u206A\u200D\u206B\u200B\u206B\u206E\u200D\u200D\u202D\u206C\u202E\u202A\u206A\u200F\u200F\u200F\u206F\u206E\u206A\u200F\u206A\u200D\u202E(rangeDecoder);
								num = (num << 1 | num6);
								num2 = ((num5 != num6) ? -124408693 : 264015452);
								continue;
							}
							}
							goto Block_1;
						}
					}
					Block_1:
					return (byte)num;
				}

				// Token: 0x04000025 RID: 37
				internal <Module>.\u206A\u206F\u206E\u200E\u202C\u206A\u206D\u202B\u200D\u206D\u202C\u206E\u202C\u202A\u202C\u200F\u206D\u206B\u206D\u206D\u206E\u206F\u206B\u202C\u206F\u202C\u202D\u200D\u200E\u206C\u200C\u200E\u200E\u202A\u206C\u206C\u206D\u202D\u200D\u206E\u202E[] \u202A\u202C\u202E\u200E\u202D\u202B\u200E\u206D\u202C\u202E\u206C\u206B\u202D\u202E\u206F\u200D\u202D\u202E\u200F\u206F\u206B\u206F\u202E\u200B\u206C\u200E\u200D\u200D\u206D\u202B\u200F\u200B\u206A\u202E\u202D\u206A\u206A\u200D\u202C\u202B\u202E;
			}
		}
	}

	// Token: 0x02000009 RID: 9
	internal class \u206D\u202B\u202C\u200E\u202D\u202D\u206A\u202A\u202A\u200B\u202E\u200D\u200B\u200E\u200E\u202D\u200F\u202A\u200B\u202E\u200E\u202C\u200D\u206C\u206C\u206A\u200F\u206D\u206A\u206C\u200B\u200E\u202E\u200C\u206E\u206A\u202E\u202A\u206B\u206A\u202E
	{
		// Token: 0x0600003F RID: 63 RVA: 0x00028E5C File Offset: 0x0002705C
		internal void \u202D\u206D\u200C\u206E\u206D\u206B\u200D\u202E\u202E\u200F\u200E\u202A\u200B\u206E\u202D\u200E\u200E\u200B\u200F\u206E\u206A\u202C\u202C\u202A\u200B\u202C\u206E\u206C\u202C\u202B\u200E\u202D\u202E\u206D\u206B\u200C\u202C\u206C\u200C\u206B\u202E(uint windowSize)
		{
			if (this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E != windowSize)
			{
				for (;;)
				{
					IL_09:
					int num = 478481835;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)((-num2 * 461025765 - -757730822) * -2016469403)) % 3U)
						{
						case 0U:
							goto IL_09;
						case 2U:
							this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E = new byte[windowSize];
							num = (int)(num3 * 3838069438U ^ 4142119197U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
			this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E = windowSize;
			this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E = 0U;
			this.\u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E = 0U;
		}

		// Token: 0x06000040 RID: 64 RVA: 0x00028ED4 File Offset: 0x000270D4
		internal void \u202A\u200E\u200D\u202A\u202D\u202B\u202E\u206F\u206D\u202D\u202B\u206F\u206B\u202A\u202A\u206F\u202C\u202D\u202B\u206F\u202D\u206E\u200F\u202E\u200E\u200D\u206C\u202C\u200C\u206B\u206D\u200D\u202A\u200E\u200B\u200F\u206F\u200B\u202C\u206E\u202E(Stream stream, bool solid)
		{
			this.\u202C\u202E\u200D\u202D\u202D\u206C\u200F\u202B\u206C\u206A\u200C\u202C\u202A\u200C\u200D\u202D\u206A\u200E\u202A\u202E\u200D\u202D\u206F\u200F\u202E\u206C\u206E\u206D\u202A\u200E\u202B\u206D\u202E\u206E\u206C\u202B\u200F\u200F\u200B\u206A\u202E();
			this.\u200F\u206A\u200F\u206C\u200D\u200C\u206D\u206E\u200B\u202B\u206B\u200B\u200C\u202A\u202D\u206C\u206B\u202B\u206F\u202B\u200C\u206C\u202C\u206C\u202D\u206E\u200D\u202C\u206D\u206E\u200F\u206B\u206B\u200D\u202D\u200E\u202A\u200E\u200E\u206C\u202E = stream;
			if (!solid)
			{
				for (;;)
				{
					IL_10:
					int num = -2054412707;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(num2 * -280924647 ^ ~(--2103771796))) % 3U)
						{
						case 0U:
							goto IL_10;
						case 1U:
							this.\u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E = 0U;
							this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E = 0U;
							num = (int)(num3 * 789109998U ^ 1522166035U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06000041 RID: 65 RVA: 0x00028F3C File Offset: 0x0002713C
		internal void \u202C\u202E\u200D\u202D\u202D\u206C\u200F\u202B\u206C\u206A\u200C\u202C\u202A\u200C\u200D\u202D\u206A\u200E\u202A\u202E\u200D\u202D\u206F\u200F\u202E\u206C\u206E\u206D\u202A\u200E\u202B\u206D\u202E\u206E\u206C\u202B\u200F\u200F\u200B\u206A\u202E()
		{
			this.\u206D\u200D\u206D\u206D\u202A\u200C\u200D\u200D\u206C\u202A\u200D\u202A\u202D\u206C\u206B\u200F\u206F\u206D\u202E\u200B\u206A\u202A\u200C\u202C\u206E\u200D\u200E\u200B\u200F\u202A\u202A\u206E\u200F\u206A\u206A\u200F\u206D\u200E\u202C\u200E\u202E();
			this.\u200F\u206A\u200F\u206C\u200D\u200C\u206D\u206E\u200B\u202B\u206B\u200B\u200C\u202A\u202D\u206C\u206B\u202B\u206F\u202B\u200C\u206C\u202C\u206C\u202D\u206E\u200D\u202C\u206D\u206E\u200F\u206B\u206B\u200D\u202D\u200E\u202A\u200E\u200E\u206C\u202E = null;
			Buffer.BlockCopy(new byte[this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E.Length], 0, this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E, 0, this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E.Length);
		}

		// Token: 0x06000042 RID: 66 RVA: 0x00028F78 File Offset: 0x00027178
		internal void \u206D\u200D\u206D\u206D\u202A\u200C\u200D\u200D\u206C\u202A\u200D\u202A\u202D\u206C\u206B\u200F\u206F\u206D\u202E\u200B\u206A\u202A\u200C\u202C\u206E\u200D\u200E\u200B\u200F\u202A\u202A\u206E\u200F\u206A\u206A\u200F\u206D\u200E\u202C\u200E\u202E()
		{
			uint num = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E - this.\u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E;
			if (num == 0U)
			{
				goto IL_11;
			}
			goto IL_7A;
			int num3;
			for (;;)
			{
				IL_16:
				int num2 = num3;
				uint num4;
				switch ((num4 = (uint)(-1088255726 - (num2 - ~(-649459070 + -166167134 + (-1348926713 ^ -901992820)) + -(~-877675033)) * -1396877381)) % 5U)
				{
				case 0U:
					goto IL_11;
				case 1U:
					goto IL_7A;
				case 2U:
					return;
				case 3U:
					this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E = 0U;
					num3 = (int)(num4 * 1746459308U ^ 190577722U);
					continue;
				}
				break;
			}
			this.\u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E;
			return;
			IL_11:
			num3 = -703578744;
			goto IL_16;
			IL_7A:
			this.\u200F\u206A\u200F\u206C\u200D\u200C\u206D\u206E\u200B\u202B\u206B\u200B\u200C\u202A\u202D\u206C\u206B\u202B\u206F\u202B\u200C\u206C\u202C\u206C\u202D\u206E\u200D\u202C\u206D\u206E\u200F\u206B\u206B\u200D\u202D\u200E\u202A\u200E\u200E\u206C\u202E.Write(this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E, (int)this.\u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E, (int)num);
			num3 = ((this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E < this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E) ? 506504414 : -1495301203);
			goto IL_16;
		}

		// Token: 0x06000043 RID: 67 RVA: 0x00029058 File Offset: 0x00027258
		internal void \u200F\u206D\u206E\u206F\u202E\u206E\u200C\u200E\u206A\u200E\u202D\u202C\u206D\u206F\u206D\u200C\u206E\u200D\u200E\u200E\u200F\u206F\u202C\u202D\u202D\u206F\u202D\u200E\u206B\u206B\u202B\u206F\u200B\u200B\u206E\u206E\u200C\u200F\u202B\u202B\u202E(uint distance, uint len)
		{
			uint num = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E - distance - 1U;
			if (num >= this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E)
			{
				goto IL_14;
			}
			goto IL_62;
			int num3;
			for (;;)
			{
				IL_19:
				int num2 = num3;
				uint num4;
				switch ((num4 = (uint)(~(num2 * -495078917) + ~-601970952 - 1130619441)) % 9U)
				{
				case 0U:
					num = 0U;
					num3 = (int)(num4 * 3292400599U ^ 3154729080U);
					continue;
				case 1U:
					len -= 1U;
					num3 = -327679523;
					continue;
				case 2U:
					num3 = ((num < this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E) ? -1051799604 : -1417989397);
					continue;
				case 3U:
				{
					byte[] u206A_u200F_u200B_u206E_u206F_u206E_u202D_u206B_u202A_u200C_u202A_u200B_u206D_u202E_u206D_u206A_u206B_u206E_u200B_u202B_u206F_u206A_u200E_u206F_u206E_u200D_u200F_u202A_u206D_u206A_u202B_u206F_u206D_u206C_u200D_u200C_u206D_u200C_u206C_u202E = this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E;
					uint u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E;
					this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E = u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E + 1U;
					u206A_u200F_u200B_u206E_u206F_u206E_u202D_u206B_u202A_u200C_u202A_u200B_u206D_u202E_u206D_u206A_u206B_u206E_u200B_u202B_u206F_u206A_u200E_u206F_u206E_u200D_u200F_u202A_u206D_u206A_u202B_u206F_u206D_u206C_u200D_u200C_u206D_u200C_u206C_u202E[(int)u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E] = this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E[(int)num++];
					num3 = ((this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E < this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E) ? 957484064 : 26563393);
					continue;
				}
				case 4U:
					goto IL_14;
				case 5U:
					num += this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E;
					num3 = (int)(num4 * 967562529U ^ 2970902443U);
					continue;
				case 6U:
					this.\u206D\u200D\u206D\u206D\u202A\u200C\u200D\u200D\u206C\u202A\u200D\u202A\u202D\u206C\u206B\u200F\u206F\u206D\u202E\u200B\u206A\u202A\u200C\u202C\u206E\u200D\u200E\u200B\u200F\u202A\u202A\u206E\u200F\u206A\u206A\u200F\u206D\u200E\u202C\u200E\u202E();
					num3 = (int)(num4 * 3121803369U ^ 42687370U);
					continue;
				case 8U:
					goto IL_62;
				}
				break;
			}
			return;
			IL_14:
			num3 = -954393555;
			goto IL_19;
			IL_62:
			num3 = ((len <= 0U) ? -1307311757 : 1033166102);
			goto IL_19;
		}

		// Token: 0x06000044 RID: 68 RVA: 0x00029194 File Offset: 0x00027394
		internal void \u202A\u206E\u200C\u206E\u200E\u200C\u202D\u200C\u202B\u202C\u202C\u202B\u202A\u206D\u206D\u200F\u202E\u206B\u200B\u200D\u202A\u202B\u206B\u202E\u202C\u206B\u206A\u202A\u200C\u200B\u206F\u206A\u202C\u202B\u202A\u206E\u202E\u206A\u200B\u202C\u202E(byte b)
		{
			byte[] u206A_u200F_u200B_u206E_u206F_u206E_u202D_u206B_u202A_u200C_u202A_u200B_u206D_u202E_u206D_u206A_u206B_u206E_u200B_u202B_u206F_u206A_u200E_u206F_u206E_u200D_u200F_u202A_u206D_u206A_u202B_u206F_u206D_u206C_u200D_u200C_u206D_u200C_u206C_u202E = this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E;
			uint u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E;
			this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E = u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E + 1U;
			u206A_u200F_u200B_u206E_u206F_u206E_u202D_u206B_u202A_u200C_u202A_u200B_u206D_u202E_u206D_u206A_u206B_u206E_u200B_u202B_u206F_u206A_u200E_u206F_u206E_u200D_u200F_u202A_u206D_u206A_u202B_u206F_u206D_u206C_u200D_u200C_u206D_u200C_u206C_u202E[(int)u202A_u202C_u200F_u200E_u200C_u200C_u206C_u202A_u202E_u200E_u202E_u206F_u202A_u200C_u202E_u200F_u206A_u206D_u202E_u206F_u206F_u206D_u206D_u206B_u202B_u206D_u206B_u206C_u202E_u206D_u206B_u206F_u200D_u202B_u206B_u206B_u206E_u200C_u200E_u206A_u202E] = b;
			if (this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E >= this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E)
			{
				for (;;)
				{
					IL_27:
					int num = 1023856998;
					for (;;)
					{
						int num2 = num;
						uint num3;
						switch ((num3 = (uint)(~(~(-num2)) * 2000826235)) % 3U)
						{
						case 0U:
							goto IL_27;
						case 2U:
							this.\u206D\u200D\u206D\u206D\u202A\u200C\u200D\u200D\u206C\u202A\u200D\u202A\u202D\u206C\u206B\u200F\u206F\u206D\u202E\u200B\u206A\u202A\u200C\u202C\u206E\u200D\u200E\u200B\u200F\u202A\u202A\u206E\u200F\u206A\u206A\u200F\u206D\u200E\u202C\u200E\u202E();
							num = (int)(num3 * 402969870U ^ 3613908075U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
		}

		// Token: 0x06000045 RID: 69 RVA: 0x00029204 File Offset: 0x00027404
		internal byte \u200E\u200D\u202A\u206A\u202D\u200E\u200F\u206F\u200F\u200C\u202B\u202A\u200F\u206E\u206A\u206B\u200C\u206C\u200D\u206E\u200C\u206A\u206B\u202D\u206D\u202C\u200D\u206C\u206F\u202A\u202D\u202C\u200E\u206A\u200F\u206F\u200E\u202E\u206A\u206E\u202E(uint distance)
		{
			uint num = this.\u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E - distance - 1U;
			if (num >= this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E)
			{
				for (;;)
				{
					IL_14:
					int num2 = 285589008;
					for (;;)
					{
						int num3 = num2;
						uint num4;
						switch ((num4 = (uint)((num3 ^ (-1503697491 ^ -1194687187 ^ ~948299362) - (382003873 * 212563839 + (1579376854 + 2143161314))) * 2105534187 ^ -937335932)) % 3U)
						{
						case 0U:
							goto IL_14;
						case 1U:
							num += this.\u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E;
							num2 = (int)(num4 * 1717568257U ^ 4082448690U);
							continue;
						}
						goto Block_1;
					}
				}
				Block_1:;
			}
			return this.\u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E[(int)num];
		}

		// Token: 0x06000046 RID: 70 RVA: 0x00027BBC File Offset: 0x00025DBC
		internal \u206D\u202B\u202C\u200E\u202D\u202D\u206A\u202A\u202A\u200B\u202E\u200D\u200B\u200E\u200E\u202D\u200F\u202A\u200B\u202E\u200E\u202C\u200D\u206C\u206C\u206A\u200F\u206D\u206A\u206C\u200B\u200E\u202E\u200C\u206E\u206A\u202E\u202A\u206B\u206A\u202E()
		{
		}

		// Token: 0x04000026 RID: 38
		internal byte[] \u206A\u200F\u200B\u206E\u206F\u206E\u202D\u206B\u202A\u200C\u202A\u200B\u206D\u202E\u206D\u206A\u206B\u206E\u200B\u202B\u206F\u206A\u200E\u206F\u206E\u200D\u200F\u202A\u206D\u206A\u202B\u206F\u206D\u206C\u200D\u200C\u206D\u200C\u206C\u202E;

		// Token: 0x04000027 RID: 39
		internal uint \u202A\u202C\u200F\u200E\u200C\u200C\u206C\u202A\u202E\u200E\u202E\u206F\u202A\u200C\u202E\u200F\u206A\u206D\u202E\u206F\u206F\u206D\u206D\u206B\u202B\u206D\u206B\u206C\u202E\u206D\u206B\u206F\u200D\u202B\u206B\u206B\u206E\u200C\u200E\u206A\u202E;

		// Token: 0x04000028 RID: 40
		internal Stream \u200F\u206A\u200F\u206C\u200D\u200C\u206D\u206E\u200B\u202B\u206B\u200B\u200C\u202A\u202D\u206C\u206B\u202B\u206F\u202B\u200C\u206C\u202C\u206C\u202D\u206E\u200D\u202C\u206D\u206E\u200F\u206B\u206B\u200D\u202D\u200E\u202A\u200E\u200E\u206C\u202E;

		// Token: 0x04000029 RID: 41
		internal uint \u202D\u200B\u202A\u202D\u206C\u206B\u202C\u202C\u202E\u202D\u200F\u206E\u206A\u206A\u202E\u206D\u206C\u206F\u206F\u200D\u200E\u200B\u202A\u202B\u206E\u206F\u202C\u206F\u206E\u200B\u202E\u206E\u202A\u206D\u202E\u200F\u202E\u200E\u202E\u206D\u202E;

		// Token: 0x0400002A RID: 42
		internal uint \u200D\u206D\u200E\u206A\u206B\u200C\u202E\u200E\u206F\u200E\u206A\u202A\u206A\u206D\u206F\u200B\u200C\u206A\u202C\u206C\u202A\u202C\u206C\u200F\u206C\u200E\u200E\u200E\u200F\u200E\u206D\u200B\u206D\u202B\u206D\u202C\u202E\u206E\u206D\u202C\u202E;
	}

	// Token: 0x0200000A RID: 10
	internal struct \u202D\u206A\u202A\u202D\u202C\u202A\u206A\u206F\u206E\u200E\u206C\u200B\u202B\u206B\u206A\u206B\u200F\u200C\u202E\u206C\u202A\u202B\u200F\u206F\u206B\u200C\u206C\u200D\u206D\u206E\u206F\u206C\u206F\u202D\u200F\u200C\u206E\u202B\u206C\u200E\u202E
	{
		// Token: 0x06000047 RID: 71 RVA: 0x0002929C File Offset: 0x0002749C
		internal void \u200B\u202B\u202A\u202C\u202E\u206B\u206C\u200B\u202E\u206D\u206A\u206C\u206A\u202C\u202E\u202C\u206F\u206C\u206C\u206A\u206D\u202C\u202B\u202D\u202C\u200B\u206F\u200F\u202B\u202D\u202D\u202B\u200F\u200D\u206D\u200F\u202A\u200B\u206C\u206B\u202E()
		{
			this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E = 0U;
		}

		// Token: 0x06000048 RID: 72 RVA: 0x000292B0 File Offset: 0x000274B0
		internal void \u200C\u200E\u202E\u206A\u200C\u200E\u206B\u206B\u202A\u206D\u202A\u206C\u202E\u202C\u202D\u206D\u200F\u206C\u202E\u200D\u202E\u206C\u206F\u202A\u202A\u206F\u200C\u202A\u202B\u206F\u206A\u206F\u200B\u206C\u200F\u202D\u202E\u206E\u206D\u202C\u202E()
		{
			if (this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E >= 4U)
			{
				goto IL_5A;
			}
			IL_09:
			int num = -342677694;
			IL_0E:
			int num2 = num;
			switch (~(num2 - 722656195 * -1254619607) % 5)
			{
			case 0:
				this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E -= 3U;
				return;
			case 1:
				this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E = 0U;
				return;
			case 3:
				goto IL_09;
			case 4:
				IL_5A:
				num = ((this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E >= 10U) ? 919134155 : 899501427);
				goto IL_0E;
			}
			this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E -= 6U;
		}

		// Token: 0x06000049 RID: 73 RVA: 0x00029358 File Offset: 0x00027558
		internal void \u202B\u202D\u206C\u200D\u202B\u200F\u200D\u200F\u206C\u202D\u202D\u200D\u200B\u200F\u206C\u200B\u206D\u200F\u206C\u200B\u206E\u202D\u206F\u206C\u202C\u200E\u206A\u200C\u202B\u206D\u206D\u202C\u206D\u200B\u206D\u200C\u206B\u202A\u206E\u206A\u202E()
		{
			this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E = ((this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E < 7U) ? 7U : 10U);
		}

		// Token: 0x0600004A RID: 74 RVA: 0x0002937C File Offset: 0x0002757C
		internal void \u200D\u206D\u206B\u200D\u206F\u200D\u206C\u202E\u206E\u200B\u200B\u206E\u206F\u202B\u200C\u202D\u200F\u206F\u206D\u202C\u206E\u202D\u202D\u200C\u206E\u200F\u206E\u200E\u202E\u202E\u202B\u200C\u206A\u202C\u202D\u202E\u206B\u206B\u202D\u202D\u202E()
		{
			this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E = ((this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E < 7U) ? 8U : 11U);
		}

		// Token: 0x0600004B RID: 75 RVA: 0x000293A0 File Offset: 0x000275A0
		internal void \u206F\u200B\u202E\u206C\u202C\u202E\u202A\u206A\u200C\u206A\u202D\u200B\u202B\u202C\u200B\u206D\u202D\u202D\u206E\u200D\u206C\u206A\u206C\u202A\u202B\u206A\u202A\u206E\u202B\u206A\u206E\u206D\u206E\u206B\u206F\u206A\u206F\u206D\u206C\u200D\u202E()
		{
			this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E = ((this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E < 7U) ? 9U : 11U);
		}

		// Token: 0x0600004C RID: 76 RVA: 0x000293C4 File Offset: 0x000275C4
		internal bool \u202D\u202E\u200E\u200C\u206F\u206C\u206E\u206E\u206B\u200F\u202C\u200E\u200B\u200E\u200D\u206C\u202B\u200F\u206C\u200F\u202C\u206E\u200E\u200E\u200B\u202D\u206F\u206A\u200F\u200D\u206A\u202C\u200C\u200D\u200D\u202E\u206A\u202C\u202E\u206A\u202E()
		{
			return this.\u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E < 7U;
		}

		// Token: 0x0400002B RID: 43
		internal uint \u200C\u202A\u202A\u206D\u206F\u202C\u200C\u206E\u202E\u206E\u200D\u202E\u202B\u206F\u202C\u206D\u206E\u200B\u200D\u202A\u202E\u200C\u200F\u200E\u200F\u206A\u202A\u200B\u206D\u200D\u206C\u206A\u202B\u200F\u200E\u206E\u202D\u200D\u202B\u200C\u202E;
	}

	// Token: 0x0200000B RID: 11
	[StructLayout(LayoutKind.Explicit, Size = 108864)]
	internal struct \u206E\u200C\u206E\u202D\u206A\u206F\u200F\u202A\u202D\u202A\u202A\u202D\u206F\u200E\u200F\u206B\u200F\u200E\u206A\u202B\u202A\u206E\u206D\u206C\u206F\u200E\u200C\u202B\u202E\u200F\u200B\u202A\u202C\u202D\u206B\u200E\u206F\u206E\u202E\u202E
	{
	}
}
