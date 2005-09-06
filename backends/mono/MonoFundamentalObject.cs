using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFundamentalObject : MonoFundamentalObjectBase
	{
		new MonoFundamentalType type;

		public MonoFundamentalObject (MonoFundamentalType type, TargetLocation location)
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
			switch (type.FundamentalKind) {
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

			case FundamentalKind.IntPtr:
				if (blob.Contents.Length == 4)
					return new IntPtr (BitConverter.ToInt32 (blob.Contents, 0));
				else
					return new IntPtr (BitConverter.ToInt64 (blob.Contents, 0));

			case FundamentalKind.UIntPtr:
				if (blob.Size == 4)
					return new UIntPtr (BitConverter.ToUInt32 (blob.Contents, 0));
				else
					return new UIntPtr (BitConverter.ToUInt64 (blob.Contents, 0));

			default:
				throw new InvalidOperationException ();
			}
		}

		public override void SetObject (MonoObject obj)
		{
			RawContents = obj.RawContents;
		}
	}
}
