using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassObject : MonoStructObject, ITargetClassObject
	{
		new MonoClassType type;

		public MonoClassObject (MonoClassType type, ITargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetClassType Type {
			get {
				return type;
			}
		}

		public ITargetClassObject Parent {
			get {
				if (type.ParentType == null)
					return null;

				return new MonoClassObject (type.ParentType, location);
			}
		}
	}
}
