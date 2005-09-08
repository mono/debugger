using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringObject : MonoFundamentalObjectBase
	{
		new MonoStringType type;

		public MonoStringObject (MonoStringType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
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
			reader.Position = type.ObjectSize;
			dynamic_location = location.GetLocationAtOffset (type.ObjectSize + 4, false);
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

		public override string Print ()
		{
			if (location.Address.IsNull)
				return "null";
			object obj = GetObject ();
			return '"' + (string) obj + '"';
		}
	}
}

