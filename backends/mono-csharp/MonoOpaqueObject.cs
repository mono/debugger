using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueObject : MonoObject
	{
		public MonoOpaqueObject (MonoOpaqueType type, TargetLocation location)
			: base (type, location)
		{ }

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override string ToString ()
		{
			return TargetBinaryReader.HexDump (RawContents);
		}
	}
}

