using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObject : ITargetObject
	{
		protected MonoType type;
		protected object data;

		public MonoObject (MonoType type, object data)
		{
			this.type = type;
			this.data = data;
		}

		public ITargetType Type {
			get {
				return type;
			}
		}

		public bool HasObject {
			get {
				return data != null;
			}
		}

		public virtual object Object {
			get {
				if (!HasObject)
					throw new InvalidOperationException ();

				return data;
			}
		}

		public override string ToString ()
		{
			if (HasObject)
				return String.Format ("{0} [{1}:{2}]", GetType (), Type, Object);
			else
				return String.Format ("{0} [{1}]", GetType (), Type);
		}
	}
}
