using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoClassObject : MonoObject, ITargetClassObject
	{
		new MonoClassInfo type;

		public MonoClassObject (MonoClassInfo type, TargetLocation location)
			: base (type.Type, location)
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

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
