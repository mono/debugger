using System;

using Mono.Debugger.Backend;

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

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			TargetBinaryReader reader = blob.GetReader ();
			reader.Position = Type.ObjectSize;
			dynamic_location = location.GetLocationAtOffset (Type.ObjectSize + 4);
			return reader.ReadInteger (4) * 2;
		}

		internal override object DoGetObject (TargetMemoryAccess target)
		{
			TargetLocation dynamic_location;
			TargetBlob object_blob = Location.ReadMemory (target, type.Size);
			long size = GetDynamicSize (
				target, object_blob, Location, out dynamic_location);

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

		internal override string Print (TargetMemoryAccess target)
		{
			if (Location.GetAddress (target).IsNull)
				return "null";
			object obj = DoGetObject (target);
			return '"' + (string) obj + '"';
		}
	}
}

