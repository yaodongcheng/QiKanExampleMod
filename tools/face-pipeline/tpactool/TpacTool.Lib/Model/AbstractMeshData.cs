using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

#if NATIVE_INTEROP_SHIT
using Buffer = NativeInterop.Buffer;
#endif

namespace TpacTool.Lib
{
	public abstract class AbstractMeshData : ExternalData
	{
		public AbstractMeshData()
		{
		}

		public AbstractMeshData(Guid typeGuid) : base(typeGuid)
		{
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct Color : IFormattable
		{
			public byte R;
			public byte G;
			public byte B;
			public byte A;

			public override string ToString()
			{
				return ToString("G", CultureInfo.CurrentCulture);
			}

			public string ToString(string format)
			{
				return ToString(format, CultureInfo.CurrentCulture);
			}

			public string ToString(string format, IFormatProvider formatProvider)
			{
				StringBuilder stringBuilder = new StringBuilder();
				string numberGroupSeparator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
				stringBuilder.Append('<');
				stringBuilder.Append(((IFormattable)R).ToString(format, formatProvider));
				stringBuilder.Append(numberGroupSeparator);
				stringBuilder.Append(' ');
				stringBuilder.Append(((IFormattable)G).ToString(format, formatProvider));
				stringBuilder.Append(numberGroupSeparator);
				stringBuilder.Append(' ');
				stringBuilder.Append(((IFormattable)B).ToString(format, formatProvider));
				stringBuilder.Append(numberGroupSeparator);
				stringBuilder.Append(' ');
				stringBuilder.Append(((IFormattable)A).ToString(format, formatProvider));
				stringBuilder.Append('>');
				return stringBuilder.ToString();
			}
		}

		protected static T[] ReadStructArray<T>(BinaryReader stream) where T : struct
		{
			int count = stream.ReadInt32();
			return ReadStructArray<T>(stream, count);
		}

		protected static T[] ReadStructArray<T>(BinaryReader stream, int count) where T : struct
		{
			int unitSize = GetStructSize<T>();
			int size = count * unitSize;
			var rawData = stream.ReadBytes(size);
			var array = new T[size / unitSize];
			if (size > 0)
			{
#if NATIVE_INTEROP_SHIT
				Buffer.Copy(rawData, array);
#else
				var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
				IntPtr ptr = handle.AddrOfPinnedObject();
				Marshal.Copy(rawData, 0, ptr, rawData.Length);
				handle.Free();
#endif
			}

			return array;
		}

		protected static void WriteStructArray<T>(BinaryWriter stream, T[] data) where T : struct
		{
			int unitSize = GetStructSize<T>();
			int count = data.Length;
			int size = count * unitSize;
			var array = new byte[size];
			if (size > 0)
			{
				var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				IntPtr ptr = handle.AddrOfPinnedObject();
				Marshal.Copy(ptr, array, 0, size);
				handle.Free();
			}
			stream.Write(count);
			stream.Write(array);
		}

		/// <summary>
		/// 无计数前缀版：只写裸字节数组。
		/// VertexStreamData 的 ReadData 走 ReadStructArray&lt;T&gt;(stream, count)（长度取自 sizes 表、不读前缀），
		/// 若写入端用带前缀的 WriteStructArray 就会多出 N×4 字节 → 后续数组整体错位（骨骼/UV 全毁）。
		/// 该不对称是 TpacTool 原版就有的 bug——只写网格元数据时不触发，一旦重写顶点流即中招。
		/// </summary>
		protected static void WriteStructArrayNoCount<T>(BinaryWriter stream, T[] data) where T : struct
		{
			int unitSize = GetStructSize<T>();
			int size = data.Length * unitSize;
			var array = new byte[size];
			if (size > 0)
			{
				var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
				IntPtr ptr = handle.AddrOfPinnedObject();
				Marshal.Copy(ptr, array, 0, size);
				handle.Free();
			}
			stream.Write(array);
		}

		protected static int GetStructSize<T>()
		{
#if NET40 || NET45
			return Marshal.SizeOf(typeof(T));
#else
			return Marshal.SizeOf<T>();
#endif
		}

		protected static T[] CreateEmptyArray<T>()
		{
			return new T[0];
		}
	}
}