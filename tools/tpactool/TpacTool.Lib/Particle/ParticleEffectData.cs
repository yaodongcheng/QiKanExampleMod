using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using JetBrains.Annotations;

namespace TpacTool.Lib
{
	public class ParticleEffectData : ExternalData
	{
		public static readonly Guid TYPE_GUID = Guid.Parse("326587ce-bb0c-4c22-8782-97e20cf03c5e");

		[NotNull]
		public string SoundCode { set; get; }

		[NotNull]
		public List<float> UnknownFloats { set; get; }

		[NotNull]
		public List<Emitter> Emitters { private set; get; }

		/// <summary>
		/// 原始字节（整段 external data）。解析出来的字段只是"读出来的视图"。
		/// 🔴 本类**重写了读侧**，所以基类的 _unknownRawData 永远是空的 —— 不留存本字段，
		/// 「读进来 → 另存/克隆」就会写出**空数据**（症状：195 字节的空壳 Particle 资产，
		/// 编辑器连缩略图都生成不出来）。为 null 时才走自己序列化（= 我们凭空造粒子那条路）。
		/// </summary>
		[CanBeNull]
		public byte[] RawData { get; private set; }

		/// <summary>丢掉原始字节，强制走自序列化 —— roundtrip 闸门用它来验写侧正确性。</summary>
		public void DiscardRawData()
		{
			RawData = null;
		}

		public ParticleEffectData() : base(TYPE_GUID)
		{
			SoundCode = String.Empty;
			UnknownFloats = new List<float>();
			Emitters = new List<Emitter>();
		}

		/// <summary>解析失败的说明（null = 解析干净）。留 RawData 就能原样搬运，不需要理解内容。</summary>
		[CanBeNull]
		public string ParseError { get; private set; }

		public override void ReadData(BinaryReader rawStream, IDictionary<object, object> userdata, int totalSize)
		{
			// 先把整段原始字节留存，再从内存副本解析（见 RawData 的注释）。
			RawData = rawStream.ReadBytes(totalSize);
			ParseError = null;
			try
			{
				ParseBody(new BinaryReader(new MemoryStream(RawData, false)));
			}
			catch (Exception ex)
			{
				// 🔴 吞掉：引擎有比这个解析器更新的布局（天气/破坏类），我们读不懂但**原始字节是完整的**
				// —— 克隆/改包名这类"不理解内容也要搬运"的场合照样能用。roundtrip 闸门仍会抓到它（写回会不一致）。
				ParseError = ex.GetType().Name + ": " + ex.Message;
			}
		}

		void ParseBody(BinaryReader stream)
		{

			SoundCode = stream.ReadSizedString();
			int length = stream.ReadInt32();
			// should a version check here
			UnknownFloats.Clear();
			UnknownFloats.Capacity = length;
			for (int i = 0; i < length; i++)
			{
				UnknownFloats.Add(stream.ReadSingle());
			}

			var i1 = stream.ReadInt32(); // emmiter number
			Emitters.Clear();
			Emitters.Capacity = i1;
			for (int i = 0; i < i1; i++)
			{
				Emitters.Add(new Emitter(stream));
			}
		}

		public override void WriteData(BinaryWriter stream, IDictionary<object, object> userdata)
		{
			if (RawData != null)
			{
				// 保真路径：原样写回（克隆 / 重打包 / 改包内其它资产时走这里）
				stream.Write(RawData);
				return;
			}

			// 自序列化路径：凭空造的粒子（XML → 粒子资产）走这里。
			// 🔴 字段顺序**必须**与 ReadData 逐字对应，否则引擎读到的是垃圾。
			stream.WriteSizedString(SoundCode);
			stream.Write(UnknownFloats.Count);
			foreach (var f in UnknownFloats)
			{
				stream.Write(f);
			}

			stream.Write(Emitters.Count);
			foreach (var e in Emitters)
			{
				e.Write(stream);
			}
		}

		public override ExternalData Clone()
		{
			var copy = new ParticleEffectData();
			copy.RawData = RawData;
			copy.SoundCode = SoundCode;
			copy.UnknownFloats = new List<float>(UnknownFloats);
			copy.Emitters = new List<Emitter>(Emitters);
			return copy;
		}

		public class Emitter
		{
			public uint SubVersion { set; get; }
			public Guid G1 { set; get; }
			public Guid G2 { set; get; }
			public Guid G3 { set; get; }
			public Guid G4 { set; get; }
			[NotNull] public string Name { set; get; }
			public uint U2 { set; get; }
			[NotNull] public List<string> Flags { private set; get; }
			public string ParticleSizeCurveOp { set; get; }
			public string S4 { set; get; }
			public float F1 { set; get; }
			public uint U3 { set; get; }
			public float F2 { set; get; }
			public float F3 { set; get; }
			public int I1 { set; get; }
			public int I2 { set; get; }
			public int I3 { set; get; }
			public int I4 { set; get; }
			public int I5 { set; get; }
			public float F4 { set; get; }
			public float F5 { set; get; }
			public float F6 { set; get; }
			public float F7 { set; get; }
			public float F8 { set; get; }
			public float F9 { set; get; }
			public float F10 { set; get; }
			public float F11 { set; get; }
			public float BacklightMultiplier { set; get; }
			public float DiffuseMultiplier { set; get; }
			public float EmissiveMultiplier { set; get; }
			public float HeatmapMultiplier { set; get; }
			public float F16 { set; get; }
			public float F17 { set; get; }
			public float ConeEmitAngle { set; get; }
			public float F19 { set; get; }
			public float F20 { set; get; }
			public float F21 { set; get; }
			public Vector4 V1 { set; get; }
			public Vector4 V2 { set; get; }
			public EmitterParameter Curve1 { set; get; }
			public Guid G5 { set; get; }
			public string CollisionBehaviour { set; get; }
			public string EmissionVelocityModel { set; get; }
			/// <summary>只有 SubVersion &gt;= 2 才在流里</summary>
			public string S3 { set; get; }
			public EmitterParameter Curve2 { set; get; }
			public EmitterParameter Curve3 { set; get; }
			public EmitterParameter Curve4 { set; get; }
			public EmitterParameter Curve5 { set; get; }
			public EmitterParameter Curve6 { set; get; }
			public uint U4 { set; get; }
			public uint U5 { set; get; }
			public float ParticleSizeBase { set; get; }
			public float ParticleSizeBias { set; get; }
			public Curve Curve7 { set; get; }
			public Curve Curve8 { set; get; }
			public EmitterParameter Curve9 { set; get; }
			public EmitterParameter Curve10 { set; get; }
			public EmitterParameter Curve11 { set; get; }
			public EmitterParameter Curve12 { set; get; }
			public EmitterParameter Curve13 { set; get; }
			public EmitterParameter Curve14 { set; get; }
			/// <summary>只有 SubVersion &gt;= 1 才在流里</summary>
			public EmitterParameter Curve15 { set; get; }
			public uint U6 { set; get; }
			public float F22 { set; get; }
			public float F23 { set; get; }
			public uint U7 { set; get; }
			public float F24 { set; get; }
			public float F25 { set; get; }
			public uint U8 { set; get; }
			public float F26 { set; get; }
			public float F27 { set; get; }
			public string BillboardType { set; get; }
			public string EmitVolumeType { set; get; }
			public ParticleColorParameter Color { set; get; }
			public Vector4 V3 { set; get; }
			public float F28 { set; get; }
			public float F29 { set; get; }
			public float F30 { set; get; }
			public float F31 { set; get; }
			public Vector4 V4 { set; get; }
			public Vector4 V5 { set; get; }
			public Vector4 Gravity { set; get; }
			public Vector4 V7 { set; get; }
			public Vector4 FixedBillboardDirection { set; get; }
			public float F32 { set; get; }
			public int TextureSpriteCountX { set; get; }
			public int TextureSpriteCountY { set; get; }
			public uint TextureSpriteFrameCount { set; get; }
			public float TextureSpriteFrameRate { set; get; }
			[NotNull] public List<Guid> GuidList { private set; get; }
			public Vector2 DecalMinScale { set; get; }
			public Vector2 DecalMaxScale { set; get; }
			public Vector2 QuadScale { set; get; }
			public Vector2 QuadBias { set; get; }
			public int SkinnedDecalStartIndex { set; get; }
			public int SkinnedDecalEndIndex { set; get; }
			public uint MaxAliveParticleCount { set; get; }
			public string EmitterSoundCode { set; get; }
			public string S11 { set; get; }
			public float F34 { set; get; }

			public Emitter()
			{
				Name = String.Empty;
				Flags = new List<string>();
				GuidList = new List<Guid>();
				Color = new ParticleColorParameter();
				Curve1 = new EmitterParameter();
				Curve2 = new EmitterParameter();
				Curve3 = new EmitterParameter();
				Curve4 = new EmitterParameter();
				Curve5 = new EmitterParameter();
				Curve6 = new EmitterParameter();
				Curve7 = new Curve();
				Curve8 = new Curve();
				Curve9 = new EmitterParameter();
				Curve10 = new EmitterParameter();
				Curve11 = new EmitterParameter();
				Curve12 = new EmitterParameter();
				Curve13 = new EmitterParameter();
				Curve14 = new EmitterParameter();
			}

			public Emitter(BinaryReader stream)
			{
				SubVersion = stream.ReadUInt32();

				G1 = stream.ReadGuid();
				G2 = stream.ReadGuid();
				G3 = stream.ReadGuid();
				G4 = stream.ReadGuid();
				Name = stream.ReadSizedString();
				U2 = stream.ReadUInt32();
				Flags = stream.ReadStringList();

				ParticleSizeCurveOp = stream.ReadSizedString();
				S4 = stream.ReadSizedString();

				F1 = stream.ReadSingle();
				U3 = stream.ReadUInt32();
				F2 = stream.ReadSingle();
				F3 = stream.ReadSingle();
				I1 = stream.ReadInt32();
				I2 = stream.ReadInt32();
				I3 = stream.ReadInt32();
				I4 = stream.ReadInt32();
				I5 = stream.ReadInt32();
				F4 = stream.ReadSingle();
				F5 = stream.ReadSingle();
				F6 = stream.ReadSingle();
				F7 = stream.ReadSingle();
				F8 = stream.ReadSingle();
				F9 = stream.ReadSingle();
				F10 = stream.ReadSingle();
				F11 = stream.ReadSingle();
				BacklightMultiplier = stream.ReadSingle();
				DiffuseMultiplier = stream.ReadSingle();
				EmissiveMultiplier = stream.ReadSingle();
				HeatmapMultiplier = stream.ReadSingle();
				F16 = stream.ReadSingle();
				F17 = stream.ReadSingle();
				ConeEmitAngle = stream.ReadSingle();
				F19 = stream.ReadSingle();
				F20 = stream.ReadSingle();
				F21 = stream.ReadSingle();
				V1 = stream.ReadVec4();
				V2 = stream.ReadVec4();

				Curve1 = new EmitterParameter(stream);

				G5 = stream.ReadGuid();
				CollisionBehaviour = stream.ReadSizedString();
				EmissionVelocityModel = stream.ReadSizedString();
				if (SubVersion >= 2)
				{
					S3 = stream.ReadSizedString();
				}

				Curve2 = new EmitterParameter(stream);
				Curve3 = new EmitterParameter(stream);
				Curve4 = new EmitterParameter(stream);
				Curve5 = new EmitterParameter(stream);
				Curve6 = new EmitterParameter(stream);
				U4 = stream.ReadUInt32();
				U5 = stream.ReadUInt32();
				ParticleSizeBase = stream.ReadSingle();
				ParticleSizeBias = stream.ReadSingle();
				Curve7 = new Curve(stream);
				Curve8 = new Curve(stream);
				Curve9 = new EmitterParameter(stream);
				Curve10 = new EmitterParameter(stream);
				Curve11 = new EmitterParameter(stream);
				Curve12 = new EmitterParameter(stream);
				Curve13 = new EmitterParameter(stream);
				Curve14 = new EmitterParameter(stream);
				if (SubVersion >= 1)
				{
					Curve15 = new EmitterParameter(stream);
				}
				U6 = stream.ReadUInt32();
				F22 = stream.ReadSingle();
				F23 = stream.ReadSingle();
				U7 = stream.ReadUInt32();
				F24 = stream.ReadSingle();
				F25 = stream.ReadSingle();
				U8 = stream.ReadUInt32();
				F26 = stream.ReadSingle();
				F27 = stream.ReadSingle();
				BillboardType = stream.ReadSizedString();
				EmitVolumeType = stream.ReadSizedString();

				Color = new ParticleColorParameter(stream);
				V3 = stream.ReadVec4();
				F28 = stream.ReadSingle();
				F29 = stream.ReadSingle();
				F30 = stream.ReadSingle();
				F31 = stream.ReadSingle();
				V4 = stream.ReadVec4();
				V5 = stream.ReadVec4();
				Gravity = stream.ReadVec4();
				V7 = stream.ReadVec4();
				FixedBillboardDirection = stream.ReadVec4();
				F32 = stream.ReadSingle();
				TextureSpriteCountX = stream.ReadInt32();
				TextureSpriteCountY = stream.ReadInt32();
				TextureSpriteFrameCount = stream.ReadUInt32();
				TextureSpriteFrameRate = stream.ReadSingle();

				int length = stream.ReadInt32();
				GuidList = new List<Guid>(length);
				for (int i = 0; i < length; i++)
				{
					GuidList.Add(stream.ReadGuid());
				}
				DecalMinScale = stream.ReadVec2();
				DecalMaxScale = stream.ReadVec2();
				QuadScale = stream.ReadVec2();
				QuadBias = stream.ReadVec2();
				SkinnedDecalStartIndex = stream.ReadInt32();
				SkinnedDecalEndIndex = stream.ReadInt32();
				MaxAliveParticleCount = stream.ReadUInt32(); // not sure
				EmitterSoundCode = stream.ReadSizedString();
				S11 = stream.ReadSizedString(); // unknown. always empty
				F34 = stream.ReadSingle(); // unknown, always 1f
			}

			/// <summary>与上面的读**逐字段同序**；改动这个顺序 = 引擎读到垃圾。</summary>
			public void Write(BinaryWriter stream)
			{
				stream.Write(SubVersion);

				stream.Write(G1);
				stream.Write(G2);
				stream.Write(G3);
				stream.Write(G4);
				stream.WriteSizedString(Name);
				stream.Write(U2);
				stream.WriteStringList(Flags);

				stream.WriteSizedString(ParticleSizeCurveOp);
				stream.WriteSizedString(S4);

				stream.Write(F1);
				stream.Write(U3);
				stream.Write(F2);
				stream.Write(F3);
				stream.Write(I1);
				stream.Write(I2);
				stream.Write(I3);
				stream.Write(I4);
				stream.Write(I5);
				stream.Write(F4);
				stream.Write(F5);
				stream.Write(F6);
				stream.Write(F7);
				stream.Write(F8);
				stream.Write(F9);
				stream.Write(F10);
				stream.Write(F11);
				stream.Write(BacklightMultiplier);
				stream.Write(DiffuseMultiplier);
				stream.Write(EmissiveMultiplier);
				stream.Write(HeatmapMultiplier);
				stream.Write(F16);
				stream.Write(F17);
				stream.Write(ConeEmitAngle);
				stream.Write(F19);
				stream.Write(F20);
				stream.Write(F21);
				stream.Write(V1);
				stream.Write(V2);

				Curve1.Write(stream);

				stream.Write(G5);
				stream.WriteSizedString(CollisionBehaviour);
				stream.WriteSizedString(EmissionVelocityModel);
				if (SubVersion >= 2)
				{
					stream.WriteSizedString(S3);
				}

				Curve2.Write(stream);
				Curve3.Write(stream);
				Curve4.Write(stream);
				Curve5.Write(stream);
				Curve6.Write(stream);
				stream.Write(U4);
				stream.Write(U5);
				stream.Write(ParticleSizeBase);
				stream.Write(ParticleSizeBias);
				Curve7.Write(stream);
				Curve8.Write(stream);
				Curve9.Write(stream);
				Curve10.Write(stream);
				Curve11.Write(stream);
				Curve12.Write(stream);
				Curve13.Write(stream);
				Curve14.Write(stream);
				if (SubVersion >= 1)
				{
					Curve15.Write(stream);
				}
				stream.Write(U6);
				stream.Write(F22);
				stream.Write(F23);
				stream.Write(U7);
				stream.Write(F24);
				stream.Write(F25);
				stream.Write(U8);
				stream.Write(F26);
				stream.Write(F27);
				stream.WriteSizedString(BillboardType);
				stream.WriteSizedString(EmitVolumeType);

				Color.Write(stream);
				stream.Write(V3);
				stream.Write(F28);
				stream.Write(F29);
				stream.Write(F30);
				stream.Write(F31);
				stream.Write(V4);
				stream.Write(V5);
				stream.Write(Gravity);
				stream.Write(V7);
				stream.Write(FixedBillboardDirection);
				stream.Write(F32);
				stream.Write(TextureSpriteCountX);
				stream.Write(TextureSpriteCountY);
				stream.Write(TextureSpriteFrameCount);
				stream.Write(TextureSpriteFrameRate);

				stream.Write(GuidList.Count);
				foreach (var g in GuidList)
				{
					stream.Write(g);
				}
				stream.Write(DecalMinScale);
				stream.Write(DecalMaxScale);
				stream.Write(QuadScale);
				stream.Write(QuadBias);
				stream.Write(SkinnedDecalStartIndex);
				stream.Write(SkinnedDecalEndIndex);
				stream.Write(MaxAliveParticleCount);
				stream.WriteSizedString(EmitterSoundCode);
				stream.WriteSizedString(S11);
				stream.Write(F34);
			}
		}

		public class EmitterParameter
		{
			public uint UnknownUInt1 { set; get; }

			public uint UnknownUInt2 { set; get; }

			public float UnknownFloat1 { set; get; }

			public float UnknownFloat2 { set; get; }

			[NotNull]
			public Curve Curve { set; get; }

			public EmitterParameter()
			{
				Curve = new Curve();
			}

			public EmitterParameter(BinaryReader stream)
			{
				UnknownUInt1 = stream.ReadUInt32();
				UnknownUInt2 = stream.ReadUInt32();
				UnknownFloat1 = stream.ReadSingle();
				UnknownFloat2 = stream.ReadSingle();

				Curve = new Curve(stream);
			}

			public void Write(BinaryWriter stream)
			{
				stream.Write(UnknownUInt1);
				stream.Write(UnknownUInt2);
				stream.Write(UnknownFloat1);
				stream.Write(UnknownFloat2);

				Curve.Write(stream);
			}
		}

		public class Curve
		{
			public uint Version { set; get; }

			public float Default { set; get; }

			public float CurveMultiplier { set; get; }

			[NotNull]
			public List<Vector4> Keys { private set; get; }

			public Curve()
			{
				Keys = new List<Vector4>();
			}

			public Curve(BinaryReader stream)
			{
				Version = stream.ReadUInt32();
				Default = stream.ReadSingle();
				CurveMultiplier = stream.ReadSingle();
				var length = stream.ReadInt32();
				Keys = new List<Vector4>(length * 2);
				for (int i = 0; i < length; i++)
				{
					Keys.Add(stream.ReadVec4());
					Keys.Add(stream.ReadVec4());
				}
			}

			/// <summary>Keys 是"每键两个 vec4"（值 + 切线）展开存的，写回时按 2 个一组算键数。</summary>
			public void Write(BinaryWriter stream)
			{
				stream.Write(Version);
				stream.Write(Default);
				stream.Write(CurveMultiplier);
				stream.Write(Keys.Count / 2);
				foreach (var k in Keys)
				{
					stream.Write(k);
				}
			}
		}

		public class ParticleColorParameter
		{
			public uint UnknownUInt { set; get; }

			public SortedList<float, Vector4> Colors { private set; get; }

			public SortedList<float, float> Alphas { private set; get; }

			public ParticleColorParameter()
			{
				Colors = new SortedList<float, Vector4>();
				Alphas = new SortedList<float, float>();
			}

			public ParticleColorParameter(BinaryReader stream)
			{
				UnknownUInt = stream.ReadUInt32();

				int length = stream.ReadInt32();
				Colors = new SortedList<float, Vector4>(length);
				for (int i = 0; i < length; i++)
				{
					var key = stream.ReadSingle();
					Colors[key] = stream.ReadVec4();
				}

				length = stream.ReadInt32();
				Alphas = new SortedList<float, float>(length);
				for (int i = 0; i < length; i++)
				{
					var key = stream.ReadSingle();
					Alphas[key] = stream.ReadSingle();
				}
			}

			public void Write(BinaryWriter stream)
			{
				stream.Write(UnknownUInt);

				stream.Write(Colors.Count);
				foreach (var kv in Colors)
				{
					stream.Write(kv.Key);
					stream.Write(kv.Value);
				}

				stream.Write(Alphas.Count);
				foreach (var kv in Alphas)
				{
					stream.Write(kv.Key);
					stream.Write(kv.Value);
				}
			}
		}
	}
}
