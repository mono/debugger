using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeOpaqueObject : NativeObject
	{
		new NativeOpaqueType type;

		public NativeOpaqueObject (NativeOpaqueType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

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

