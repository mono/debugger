using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalObject : MonoFundamentalObjectBase
	{
		public MonoFundamentalObject (MonoType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader,
						     TargetLocation locaction)
		{
			switch (System.Type.GetTypeCode ((Type) type.TypeHandle)) {
			case TypeCode.Boolean:
				return reader.BinaryReader.PeekByte () != 0;

			case TypeCode.Char:
				return BitConverter.ToChar (reader.Contents, 0);

			case TypeCode.SByte:
				return (sbyte) reader.BinaryReader.PeekByte ();

			case TypeCode.Byte:
				return (byte) reader.BinaryReader.PeekByte ();

			case TypeCode.Int16:
				return BitConverter.ToInt16 (reader.Contents, 0);

			case TypeCode.UInt16:
				return BitConverter.ToUInt16 (reader.Contents, 0);

			case TypeCode.Int32:
				return BitConverter.ToInt32 (reader.Contents, 0);

			case TypeCode.UInt32:
				return BitConverter.ToUInt32 (reader.Contents, 0);

			case TypeCode.Int64:
				return BitConverter.ToInt64 (reader.Contents, 0);

			case TypeCode.UInt64:
				return BitConverter.ToUInt64 (reader.Contents, 0);

			case TypeCode.Single:
				return BitConverter.ToSingle (reader.Contents, 0);

			case TypeCode.Double:
				return BitConverter.ToDouble (reader.Contents, 0);

			default:
				throw new InvalidOperationException ();
			}
		}

		protected override byte[] CreateObject (object obj)
		{
			switch (System.Type.GetTypeCode ((Type) type.TypeHandle)) {
			case TypeCode.Boolean:
				return BitConverter.GetBytes (Convert.ToBoolean (obj));

			case TypeCode.Char:
				return BitConverter.GetBytes (Convert.ToChar (obj));

			case TypeCode.SByte:
				return BitConverter.GetBytes (Convert.ToSByte (obj));

			case TypeCode.Byte:
				return BitConverter.GetBytes (Convert.ToByte (obj));

			case TypeCode.Int16:
				return BitConverter.GetBytes (Convert.ToInt16 (obj));

			case TypeCode.UInt16:
				return BitConverter.GetBytes (Convert.ToUInt16 (obj));

			case TypeCode.Int32:
				return BitConverter.GetBytes (Convert.ToInt32 (obj));

			case TypeCode.UInt32:
				return BitConverter.GetBytes (Convert.ToUInt32 (obj));

			case TypeCode.Int64:
				return BitConverter.GetBytes (Convert.ToInt64 (obj));

			case TypeCode.UInt64:
				return BitConverter.GetBytes (Convert.ToUInt64 (obj));

			case TypeCode.Single:
				return BitConverter.GetBytes (Convert.ToSingle (obj));

			case TypeCode.Double:
				return BitConverter.GetBytes (Convert.ToDouble (obj));

			default:
				throw new InvalidOperationException ();
			}
		}
	}
}
