using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalObject : MonoFundamentalObjectBase
	{
		new MonoFundamentalTypeInfo type;

		public MonoFundamentalObject (MonoFundamentalTypeInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (TargetBlob blob, TargetLocation locaction)
		{
			switch (System.Type.GetTypeCode (type.Type.TypeHandle)) {
			case TypeCode.Boolean:
				return blob.Contents [0] != 0;

			case TypeCode.Char:
				return BitConverter.ToChar (blob.Contents, 0);

			case TypeCode.SByte:
				return (sbyte) blob.Contents [0];

			case TypeCode.Byte:
				return (byte) blob.Contents [0];

			case TypeCode.Int16:
				return BitConverter.ToInt16 (blob.Contents, 0);

			case TypeCode.UInt16:
				return BitConverter.ToUInt16 (blob.Contents, 0);

			case TypeCode.Int32:
				return BitConverter.ToInt32 (blob.Contents, 0);

			case TypeCode.UInt32:
				return BitConverter.ToUInt32 (blob.Contents, 0);

			case TypeCode.Int64:
				return BitConverter.ToInt64 (blob.Contents, 0);

			case TypeCode.UInt64:
				return BitConverter.ToUInt64 (blob.Contents, 0);

			case TypeCode.Single:
				return BitConverter.ToSingle (blob.Contents, 0);

			case TypeCode.Double:
				return BitConverter.ToDouble (blob.Contents, 0);

			case TypeCode.Object:
				if (type.Type.TypeHandle == typeof (System.IntPtr)) {
					if (blob.Contents.Length == 4)
						return new IntPtr (BitConverter.ToInt32 (blob.Contents, 0));
					else
						return new IntPtr (BitConverter.ToInt64 (blob.Contents, 0));
				} else if (type.Type.TypeHandle == typeof (System.UIntPtr)) {
					if (blob.Size == 4)
						return new UIntPtr (BitConverter.ToUInt32 (blob.Contents, 0));
					else
						return new UIntPtr (BitConverter.ToUInt64 (blob.Contents, 0));
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
