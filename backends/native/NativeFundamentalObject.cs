using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFundamentalObject : NativeObject, ITargetFundamentalObject
	{
		public NativeFundamentalObject (NativeType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
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
				TargetBlob blob;
				if (type.HasFixedSize)
					blob = location.ReadMemory (type.Size);
				else
					blob = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (blob, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		internal void SetObject (object obj)
		{
			try {
				byte [] data = CreateObject (obj);
				if (!type.HasFixedSize || (data == null) ||
				    (data.Length != type.Size))
					throw new NotSupportedException ();

				RawContents = data;
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected object GetObject (TargetBlob blob, TargetLocation locaction)
		{
			switch (((NativeFundamentalType) type).FundamentalKind) {
			case FundamentalKind.Boolean:
				return blob.Contents [0] != 0;

			case FundamentalKind.Char:
				return BitConverter.ToChar (blob.Contents, 0);

			case FundamentalKind.SByte:
				return (sbyte) blob.Contents [0];

			case FundamentalKind.Byte:
				return (byte) blob.Contents [0];

			case FundamentalKind.Int16:
				return BitConverter.ToInt16 (blob.Contents, 0);

			case FundamentalKind.UInt16:
				return BitConverter.ToUInt16 (blob.Contents, 0);

			case FundamentalKind.Int32:
				return BitConverter.ToInt32 (blob.Contents, 0);

			case FundamentalKind.UInt32:
				return BitConverter.ToUInt32 (blob.Contents, 0);

			case FundamentalKind.Int64:
				return BitConverter.ToInt64 (blob.Contents, 0);

			case FundamentalKind.UInt64:
				return BitConverter.ToUInt64 (blob.Contents, 0);

			case FundamentalKind.Single:
				return BitConverter.ToSingle (blob.Contents, 0);

			case FundamentalKind.Double:
				return BitConverter.ToDouble (blob.Contents, 0);

			default:
				throw new InvalidOperationException ();
			}
		}

		protected byte[] CreateObject (object obj)
		{
			switch (((NativeFundamentalType) type).FundamentalKind) {
			case FundamentalKind.Boolean:
				return BitConverter.GetBytes (Convert.ToBoolean (obj));

			case FundamentalKind.Char:
				return BitConverter.GetBytes (Convert.ToChar (obj));

			case FundamentalKind.SByte:
				return BitConverter.GetBytes (Convert.ToSByte (obj));

			case FundamentalKind.Byte:
				return BitConverter.GetBytes (Convert.ToByte (obj));

			case FundamentalKind.Int16:
				return BitConverter.GetBytes (Convert.ToInt16 (obj));

			case FundamentalKind.UInt16:
				return BitConverter.GetBytes (Convert.ToUInt16 (obj));

			case FundamentalKind.Int32:
				return BitConverter.GetBytes (Convert.ToInt32 (obj));

			case FundamentalKind.UInt32:
				return BitConverter.GetBytes (Convert.ToUInt32 (obj));

			case FundamentalKind.Int64:
				return BitConverter.GetBytes (Convert.ToInt64 (obj));

			case FundamentalKind.UInt64:
				return BitConverter.GetBytes (Convert.ToUInt64 (obj));

			case FundamentalKind.Single:
				return BitConverter.GetBytes (Convert.ToSingle (obj));

			case FundamentalKind.Double:
				return BitConverter.GetBytes (Convert.ToDouble (obj));

			default:
				throw new InvalidOperationException ();
			}
		}

		public override string Print (ITargetAccess target)
		{
			return GetObject ().ToString ();
		}

		public void SetObject (ITargetObject obj)
		{
			throw new NotImplementedException ();
		}
	}
}
