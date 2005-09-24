using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringObject : TargetFundamentalObject
	{
		new protected readonly MonoStringType Type;

		public MonoStringObject (MonoStringType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			TargetBinaryReader reader = blob.GetReader ();
			reader.Position = Type.ObjectSize;
			dynamic_location = location.GetLocationAtOffset (Type.ObjectSize + 4);
			return reader.ReadInteger (4) * 2;
		}

		public override object GetObject (ITargetAccess target)
		{
			TargetLocation dynamic_location;
			TargetBlob object_blob = Location.ReadMemory (target, type.Size);
			long size = GetDynamicSize (object_blob, Location, out dynamic_location);

			if (size > (long) MonoStringType.MaximumStringLength)
				size = MonoStringType.MaximumStringLength;

			TargetBlob blob = dynamic_location.ReadMemory (target, (int) size);

			TargetBinaryReader reader = blob.GetReader ();
			int length = (int) reader.Size / 2;

			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) reader.ReadInt16 ();

			return new String (retval);
		}

		public override string Print (ITargetAccess target)
		{
			if (Location.Address.IsNull)
				return "null";
			object obj = GetObject (target);
			return '"' + (string) obj + '"';
		}
	}
}

