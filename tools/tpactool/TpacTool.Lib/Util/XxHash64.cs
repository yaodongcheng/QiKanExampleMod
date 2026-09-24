using System;
using System.IO;

namespace TpacTool.Lib
{
	/// <summary>
	/// xxHash64（种子 0）——**引擎对 tpac 数据段做的校验值**。
	///
	/// 🔴 实证（2026-09-24）：段头那两个"未知字段"里，8 字节的那个 =
	///   `XxHash64(解压后的段数据)`，4 字节的那个常规段恒为 1。
	///   实测：原版 `psys_game_blood_sword_exit` 段（5991 字节）算出 8634471121823550541，
	///   与包里存的值**逐位相同**。
	///
	/// 为什么必须自己算：新建的资产（不是从包里读出来的）这两个字段是 0 —— 引擎校验不过就
	/// **找得到资产、读不到数据**（症状：编辑器/游戏里粒子名字在、emitter 全空）。
	/// </summary>
	public static class XxHash64
	{
		const ulong P1 = 0x9E3779B185EBCA87UL;
		const ulong P2 = 0xC2B2AE3D27D4EB4FUL;
		const ulong P3 = 0x165667B19E3779F9UL;
		const ulong P4 = 0x85EBCA77C2B2AE63UL;
		const ulong P5 = 0x27D4EB2F165667C5UL;

		static ulong Rotl(ulong x, int r) => (x << r) | (x >> (64 - r));

		static ulong Round(ulong acc, ulong input) => Rotl(acc + input * P2, 31) * P1;

		static ulong MergeRound(ulong acc, ulong val)
		{
			acc ^= Round(0, val);
			return acc * P1 + P4;
		}

		public static ulong Hash(byte[] data, int offset, int length, ulong seed = 0)
		{
			int end = offset + length;
			int i = offset;
			ulong h;

			if (length >= 32)
			{
				ulong v1 = seed + P1 + P2;
				ulong v2 = seed + P2;
				ulong v3 = seed;
				ulong v4 = seed - P1;

				int limit = end - 32;
				while (i <= limit)
				{
					v1 = Round(v1, BitConverter.ToUInt64(data, i)); i += 8;
					v2 = Round(v2, BitConverter.ToUInt64(data, i)); i += 8;
					v3 = Round(v3, BitConverter.ToUInt64(data, i)); i += 8;
					v4 = Round(v4, BitConverter.ToUInt64(data, i)); i += 8;
				}

				h = Rotl(v1, 1) + Rotl(v2, 7) + Rotl(v3, 12) + Rotl(v4, 18);
				h = MergeRound(h, v1);
				h = MergeRound(h, v2);
				h = MergeRound(h, v3);
				h = MergeRound(h, v4);
			}
			else
			{
				h = seed + P5;
			}

			h += (ulong)length;

			while (i + 8 <= end)
			{
				h ^= Round(0, BitConverter.ToUInt64(data, i));
				h = Rotl(h, 27) * P1 + P4;
				i += 8;
			}

			if (i + 4 <= end)
			{
				h ^= (ulong)BitConverter.ToUInt32(data, i) * P1;
				h = Rotl(h, 23) * P2 + P3;
				i += 4;
			}

			while (i < end)
			{
				h ^= data[i] * P5;
				h = Rotl(h, 11) * P1;
				i++;
			}

			h ^= h >> 33;
			h *= P2;
			h ^= h >> 29;
			h *= P3;
			h ^= h >> 32;
			return h;
		}

		public static ulong Hash(byte[] data) => Hash(data, 0, data.Length);
	}
}
