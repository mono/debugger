using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringObject : MonoObject
	{
		new MonoStringType type;

		public MonoStringObject (MonoStringType type, MonoTargetLocation location)
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

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							MonoTargetLocation location,
							out MonoTargetLocation dynamic_location)
		{
			Console.WriteLine ("GET DYNAMIC SIZE: {0} {1}", type, location);
			reader.Offset = type.LengthOffset;
			dynamic_location = location.GetLocationAtOffset (type.DataOffset, false);
			long length = reader.BinaryReader.ReadInteger (type.LengthSize) * 2;
			Console.WriteLine ("STRING SIZE: {0} {1}", dynamic_location, length);
			return length;
		}

		protected override object GetObject (ITargetMemoryReader reader,
						     MonoTargetLocation location)
		{
			Console.WriteLine ("GET STRING CONTENTS: {0}", location);

			int length = (int) reader.Size / 2;

			char[] retval = new char [length];

			for (int i = 0; i < length; i++)
				retval [i] = (char) reader.BinaryReader.ReadInt16 ();

			return new String (retval);
		}
	}
}

