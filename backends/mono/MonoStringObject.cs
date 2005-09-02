using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringObject : MonoFundamentalObjectBase
	{
		new MonoStringTypeInfo type;

		public MonoStringObject (MonoStringTypeInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		protected override int MaximumDynamicSize {
			get {
				return MonoStringTypeInfo.MaximumStringLength;
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			TargetBinaryReader reader = blob.GetReader ();
			reader.Position = type.LengthOffset;
			dynamic_location = location.GetLocationAtOffset (type.DataOffset, false);
			return reader.ReadInteger (type.LengthSize) * 2;
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
			object obj = GetObject ();
			return '"' + (string) obj + '"';
		}
	}
}

