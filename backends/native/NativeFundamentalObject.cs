using System;

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
			set {
				SetObject (value);
			}
		}

		protected virtual object GetObject ()
		{
			try {
				ITargetMemoryReader reader;
				if (type_info.HasFixedSize)
					reader = location.ReadMemory (type_info.Size);
				else
					reader = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (reader, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		internal void SetObject (object obj)
		{
			try {
				byte [] data = CreateObject (obj);
				if (!type_info.HasFixedSize || (data == null) || (data.Length != type_info.Size))
					throw new NotSupportedException ();

				RawContents = data;
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected object GetObject (ITargetMemoryReader reader, TargetLocation locaction)
		{
			switch (System.Type.GetTypeCode ((Type) type_info.Type.TypeHandle)) {
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

		protected byte[] CreateObject (object obj)
		{
			switch (System.Type.GetTypeCode ((Type) type_info.Type.TypeHandle)) {
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

		public override string Print ()
		{
			return GetObject ().ToString ();
		}
	}
}
