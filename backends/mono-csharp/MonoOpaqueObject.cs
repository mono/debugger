using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueObject : MonoObject
	{
		public MonoOpaqueObject (MonoType type, ITargetLocation location)
			: base (type, location)
		{ }

		public override bool HasObject {
			get {
				return false;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}
	}
}

