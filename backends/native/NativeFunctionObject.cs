using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionObject : NativeObject, ITargetFunctionObject
	{
		new NativeFunctionType type;

		public NativeFunctionObject (NativeFunctionType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetFunctionType Type {
			get {
				return type;
			}
		}

		public NativeObject Invoke (object[] args)
		{
			Console.WriteLine ("INVOKE: {0} {1}", this, location.Address);
			return null;
		}

		ITargetObject ITargetFunctionObject.Invoke (object[] args)
		{
			return Invoke (args);
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
