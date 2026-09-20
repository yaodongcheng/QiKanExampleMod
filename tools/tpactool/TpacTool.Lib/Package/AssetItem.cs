using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;

namespace TpacTool.Lib
{
	public class AssetItem : IDependence
	{
		private Guid _resourceGuid;

		public Guid Type { private set; get; }

		public bool Removed { private set; get; }

		public List<AbstractExternalLoader> TypelessDataSegments { private set; get; }

		public List<UnknownDependence> UnknownDependences { private set; get; }

		public uint Version { protected internal set; get; }

		public Guid Guid
		{
			set => _resourceGuid = value;
			get
			{
				/*if (_resourceGuid == Guid.Empty)
					_resourceGuid = Guid.NewGuid();*/
				return _resourceGuid;
			}
		}

		[NotNull]
		public string Name { set; get; }

		public bool Invalid { internal set; get; }

		public AssetItem(Guid type)
		{
			this.Type = type;
			this.Guid = Guid.Empty;
			this.Name = String.Empty;
			this.TypelessDataSegments = new List<AbstractExternalLoader>(0);
			this.UnknownDependences = new List<UnknownDependence>(0);
			Removed = false;
		}

		private byte[] _temp_metadata;

		/// <summary>加载时捕获的元数据原始字节(与引擎写入字节一致; Save 时 RawMeta 优先直写)。</summary>
		public byte[] RawMeta { set; get; }

		/// <summary>🔴 元数据后面那个 8 字节字段（库作者注释写作 "wtf checksum"）。
		/// 原先 Load 读出来就丢、Save 硬写 0 → **任何 Save 过的包，每个资产都有 8 字节被清零**
		/// （实测 roundtrip 一个 6 资产小包 = 48 字节被改）。原样搬运才谈得上"保真保存"。</summary>
		public long UnknownMetadataChecksum { set; get; }
		public virtual void ReadMetadata([NotNull] BinaryReader stream, int totalSize)
		{
			// skip metadata without processing
			_temp_metadata = stream.ReadBytes(totalSize);
		}

		public virtual void ConsumeDataSegments(AbstractExternalLoader[] externalData)
		{
			TypelessDataSegments.Capacity = externalData.Length;
			TypelessDataSegments.AddRange(externalData);
		}

		public byte[] WriteMetadata()
		{
			var memStream = new MemoryStream();
			using (var stream = new BinaryWriter(memStream, Encoding.UTF8))
			{
				WriteMetadata(stream);
				stream.Flush();
				return memStream.ToArray();
			}
		}

		public virtual void WriteMetadata([NotNull] BinaryWriter stream)
		{
			if (_temp_metadata != null)
				stream.Write(_temp_metadata);
			else
				throw new NotImplementedException();
		}

		public virtual AssetItem Clone()
		{
			throw new NotImplementedException();
		}

		protected void CloneDo(AssetItem parent)
		{
			this.Name = parent.Name;
			this.Version = parent.Version;
			this.Guid = parent.Guid;
			this._temp_metadata = parent._temp_metadata;
			foreach (var dependence in parent.UnknownDependences)
			{
				this.UnknownDependences.Add(dependence.Clone());
			}
		}

		protected void SetDataSegment([CanBeNull] AbstractExternalLoader oldValue, [CanBeNull] AbstractExternalLoader newValue)
		{
			if (oldValue != null)
				TypelessDataSegments.Remove(oldValue);
			if (newValue != null)
				TypelessDataSegments.Add(newValue);
		}

		public class UnknownDependence
		{
			public Guid UnknownGuid1 { set; get; } = System.Guid.Empty;

			public Guid UnknownGuid2 { set; get; } = System.Guid.Empty;

			public Guid UnknownGuid3 { set; get; } = System.Guid.Empty;

			public UnknownDependence Clone()
			{
				return new UnknownDependence()
				{
					UnknownGuid1 = this.UnknownGuid1,
					UnknownGuid2 = this.UnknownGuid2,
					UnknownGuid3 = this.UnknownGuid3
				};
			}
		}
	}
}