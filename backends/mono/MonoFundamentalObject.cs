using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFundamentalObject : MonoFundamentalObjectBase
	{
		new MonoFundamentalType type;

		public MonoFundamentalObject (MonoFundamentalType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader,
						     TargetLocation locaction)
		{
			switch (System.Type.GetTypeCode (type.TypeHandle)) {
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

			case TypeCode.Object:
				if (type.TypeHandle == typeof (System.IntPtr)) {
					if (reader.Size == 4)
						return new IntPtr (BitConverter.ToInt32 (reader.Contents, 0));
					else
						return new IntPtr (BitConverter.ToInt64 (reader.Contents, 0));
				} else if (type.TypeHandle == typeof (System.UIntPtr)) {
					if (reader.Size == 4)
						return new UIntPtr (BitConverter.ToUInt32 (reader.Contents, 0));
					else
						return new UIntPtr (BitConverter.ToUInt64 (reader.Contents, 0));
				}
				throw new InvalidOperationException ();

			default:
				throw new InvalidOperationException ();
			}
		}

		public override void SetObject (ITargetObject obj)
		{
			RawContents = obj.RawContents;
		}
	}
}
