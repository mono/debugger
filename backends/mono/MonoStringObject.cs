using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringObject : MonoFundamentalObjectBase
	{
		new protected readonly MonoStringType Type;

		public MonoStringObject (MonoStringType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		protected override int MaximumDynamicSize {
			get {
				return MonoStringType.MaximumStringLength;
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			TargetBinaryReader reader = blob.GetReader ();
			reader.Position = Type.ObjectSize;
			dynamic_location = location.GetLocationAtOffset (Type.ObjectSize + 4, false);
			return reader.ReadInteger (4) * 2;
		}

		protected override object GetObject (TargetBlob blob, TargetLocation location)
		{
			TargetBinaryReader reader = blob.GetReader ();
			int length = (int) reader.Size / 2;

			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) reader.ReadInt16 ();

			return new String (retval);
		}

		public override string Print (ITargetAccess target)
		{
			if (location.Address.IsNull)
				return "null";
			object obj = GetObject (target);
			return '"' + (string) obj + '"';
		}
	}
}

