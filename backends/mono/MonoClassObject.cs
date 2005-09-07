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
			return type.Type.GetProperty (this, index);
		}

		public ITargetObject GetEvent (int index)
		{
			// return type.GetEvent (location, index);
			return null;
		}

		public ITargetFunctionObject GetMethod (int index)
		{
			return type.Type.GetMethod (location.TargetAccess, index);
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
