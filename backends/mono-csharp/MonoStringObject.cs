using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringObject : MonoObject
	{
		new MonoStringType type;

		public MonoStringObject (MonoStringType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		protected override int MaximumDynamicSize {
			get {
				return MonoStringType.MaximumStringLength;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			reader.Offset = type.LengthOffset;
			dynamic_address = address + type.DataOffset;
			return reader.BinaryReader.ReadInteger (type.LengthSize) * 2;
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			int length = (int) reader.Size / 2;

			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) reader.BinaryReader.ReadInt16 ();

			return new String (retval);
		}
	}
}

