using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoClassObject : MonoObject, ITargetClassObject
	{
		new MonoClass type;

		public MonoClassObject (MonoClass type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		ITargetStructType ITargetStructObject.Type {
			get {
				return type;
			}
		}

		ITargetClassType ITargetClassObject.Type {
			get {
				return type;
			}
		}

		ITargetClassObject ITargetClassObject.CurrentObject {
			get {
				return GetCurrentObject ();
			}
		}

		public MonoClassObject GetCurrentObject ()
		{
			ITargetAccess target = location.TargetAccess;
			TargetAddress vtable = target.ReadAddress (location.Address);
			TargetAddress klass = target.ReadAddress (vtable);

			MonoType ctype = type.File.Table.GetTypeFromClass (klass.Address);
			return new MonoClassObject ((MonoClass) ctype, location);
		}

		public ITargetClassObject Parent {
			get {
				if (type.ParentType == null)
					return null;

				return new MonoClassObject (type.ParentType, location);
			}
		}

		public ITargetObject GetField (int index)
		{
			return type.GetField (location, index);
		}

		public ITargetObject GetProperty (int index)
		{
			return type.GetProperty (location, index);
		}

		public ITargetFunctionObject GetMethod (int index)
		{
			return type.GetMethod (location, index);
		}

		public string PrintObject ()
		{
			throw new NotImplementedException ();
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
