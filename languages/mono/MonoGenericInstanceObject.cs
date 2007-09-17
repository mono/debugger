using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceObject : TargetObject
	{
		new MonoGenericInstanceType type;

		public MonoGenericInstanceObject (MonoGenericInstanceType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		internal override long GetDynamicSize (Thread target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
