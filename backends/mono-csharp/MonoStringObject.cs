using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
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

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			reader.Offset = type.LengthOffset;
			dynamic_location = location.GetLocationAtOffset (type.DataOffset, false);
			return reader.BinaryReader.ReadInteger (type.LengthSize) * 2;
		}

		protected override object GetObject (ITargetMemoryReader reader,
						     TargetLocation location)
		{
			int length = (int) reader.Size / 2;

			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) reader.BinaryReader.ReadInt16 ();

			return new String (retval);
		}
	}
}

