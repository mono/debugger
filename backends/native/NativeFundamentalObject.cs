using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalObject : NativeObject, ITargetFundamentalObject
	{
		public NativeFundamentalObject (NativeType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public bool HasObject {
			get {
				return true;
			}
		}

		public object Object {
			get {
				return GetObject ();
			}
		}

		protected virtual object GetObject ()
		{
			try {
				ITargetMemoryReader reader;
				if (type.HasFixedSize)
					reader = location.ReadMemory (type.Size);
				else
					reader = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (reader, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected object GetObject (ITargetMemoryReader reader, TargetLocation locaction)
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

		public override string Print ()
		{
			return GetObject ().ToString ();
		}
	}
}
