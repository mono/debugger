using System;

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

		public ITargetObject Invoke (ITargetAccess target, ITargetObject instance,
					     ITargetObject[] args)
		{
			throw new NotSupportedException ();
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
