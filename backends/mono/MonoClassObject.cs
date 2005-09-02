using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : MonoObject, ITargetClassObject
	{
		new MonoClassInfo type;

		public MonoClassObject (MonoClassInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		ITargetStructType ITargetStructObject.Type {
			get { return type.Type; }
		}

		ITargetClassType ITargetClassObject.Type {
			get { return type.Type; }
		}

		public ITargetClassObject Parent {
			get {
				if (!type.Type.HasParent)
					return null;

				return type.GetParentObject (location);
			}
		}

		[Command]
		public ITargetObject GetField (int index)
		{
			return type.GetField (location, index);
		}

		public ITargetObject GetProperty (int index)
		{
			return type.GetProperty (location, index);
		}

		public ITargetObject GetEvent (int index)
		{
			// return type.GetEvent (location, index);
			return null;
		}

		public ITargetFunctionObject GetMethod (int index)
		{
			return type.GetMethod (location, index);
		}

		public string PrintObject ()
		{
			ITargetObject[] args = new ITargetObject[0];
			ITargetFunctionObject func = null;
			ITargetStructType stype = (ITargetStructType)type.Type;

		again:
			foreach (ITargetMethodInfo m in stype.Methods)
				// XXX also check that the return type is String, right?
				if (m.Name == "ToString" && m.Type.ParameterTypes.Length == 0) {
					func = GetMethod (m.Index);
					break;
				}

			if (func == null) {
				ITargetClassType ctype = stype as ITargetClassType;
				if (ctype != null && ctype.HasParent) {
					stype = ctype.ParentType;
					goto again;
				}
				else {
					return null;
				}
			}

			try {
				ITargetObject retval = func.Invoke (args, false);

				return retval.Print();
			}
			catch (TargetInvocationException ex) {
				return "Exception calling ToString()";
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
