using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFunctionObject : MonoObject, ITargetFunctionObject
	{
		new MonoFunctionType type;

		public MonoFunctionObject (MonoFunctionType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		ITargetFunctionType ITargetFunctionObject.Type {
			get {
				return type;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public ITargetObject Invoke (MonoObject[] args, bool debug)
		{
			return type.Invoke (location, args, debug);
		}

		ITargetObject ITargetFunctionObject.Invoke (ITargetObject[] args, bool debug)
		{
			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return Invoke (margs, debug);
		}
	}
}

