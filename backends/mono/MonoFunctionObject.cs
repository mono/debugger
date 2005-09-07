using System;

namespace Mono.Debugger.Languages.Mono
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
			get { return type; }
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public ITargetObject Invoke (ITargetAccess target, ITargetObject instance,
					     ITargetObject[] args, bool debug)
		{
			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return type.Invoke (target, location.Address,
					    (MonoObject) instance, margs, debug);
		}
	}
}

