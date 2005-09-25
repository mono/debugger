using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectObject : TargetPointerObject
	{
		public new readonly MonoObjectType Type;

		public MonoObjectObject (MonoObjectType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

		protected TargetType GetCurrentType ()
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = Location.TargetMemoryAccess.ReadAddress (Location.Address);
			address = Location.TargetMemoryAccess.ReadGlobalAddress (address);

			return Type.File.MonoLanguage.GetClass (Location.TargetAccess, address);
		}

		public override TargetType CurrentType {
			get {
				TargetType type = GetCurrentType ();
				if (type == null)
					throw new LocationInvalidException ();
				return type;
			}
		}

		public override TargetObject DereferencedObject {
			get {
				TargetType current_type = GetCurrentType ();
				if (current_type == null)
					return null;

				// If this is a reference type, then the `MonoObject *' already
				// points to the boxed object itself.
				// If it's a valuetype, then the boxed contents is immediately
				// after the `MonoObject' header.

				int offset = current_type.IsByRef ? 0 : type.Size;
				TargetLocation new_location = Location.GetLocationAtOffset (offset);
				TargetObject obj = current_type.GetObject (new_location);
				return obj;
			}
		}

		public override byte[] GetDereferencedContents (int size)
		{
			throw new InvalidOperationException ();
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override TargetObject GetArrayElement (TargetAccess target, int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (TargetAccess target)
		{
			if (HasAddress)
				return String.Format ("MonoObject ({0})", Address);
			else
				return String.Format ("MonoObject ({0})", Location);
		}
	}
}
