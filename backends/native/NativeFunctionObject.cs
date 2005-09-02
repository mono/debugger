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

		public NativeObject Invoke (NativeObject[] args)
		{
			return null;
		}

		ITargetObject ITargetFunctionObject.Invoke (ITargetObject[] args, bool debug)
		{
			NativeObject[] nargs = new NativeObject [args.Length];
			args.CopyTo (nargs, 0);
			return Invoke (nargs);
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
