using System;

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

		ITargetClassObject ITargetClassObject.CurrentObject {
			get {
				return GetCurrentObject ();
			}
		}

		public MonoClassObject GetCurrentObject ()
		{
			ITargetAccess target = location.TargetAccess;
			TargetAddress vtable = target.ReadAddress (location.Address);
			TargetAddress klass_address = target.ReadAddress (vtable);

			MonoType klass = type.Type.File.MonoLanguage.GetClass (klass_address);
			Console.WriteLine ("KLASS CURRENT: {0} {1}", klass_address, klass);
			// return klass.GetClassObject (location);
			return null;
		}

		public ITargetClassObject Parent {
			get {
				if (type.Type.ParentType == null)
					return null;

				// return type.Type.ParentType.GetClassObject (location);
				return null;
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
