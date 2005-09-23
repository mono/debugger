using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectObject : TargetObject, ITargetPointerObject
	{
		new MonoObjectType type;

		public MonoObjectObject (MonoObjectType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get { return type; }
		}

		protected TargetType GetCurrentType ()
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = location.TargetMemoryAccess.ReadAddress (location.Address);
			address = location.TargetMemoryAccess.ReadGlobalAddress (address);

			return type.File.MonoLanguage.GetClass (location.TargetAccess, address);
		}

		public TargetType CurrentType {
			get {
				TargetType type = GetCurrentType ();
				if (type == null)
					throw new LocationInvalidException ();
				return type;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				return CurrentType;
			}
		}

		public ITargetObject DereferencedObject {
			get {
				TargetType current_type = GetCurrentType ();
				if (current_type == null)
					return null;

				// If this is a reference type, then the `MonoObject *' already
				// points to the boxed object itself.
				// If it's a valuetype, then the boxed contents is immediately
				// after the `MonoObject' header.

				int offset = current_type.IsByRef ? 0 : type.Size;
				TargetLocation new_location = location.GetLocationAtOffset (offset, false);
				ITargetObject obj = current_type.GetObject (new_location);
				return obj;
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			throw new InvalidOperationException ();
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public bool HasAddress {
			get {
				return location.HasAddress;
			}
		}

		public TargetAddress Address {
			get { return location.Address; }
		}

		public ITargetObject GetArrayElement (int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print (ITargetAccess target)
		{
			if (HasAddress)
				return String.Format ("MonoObject ({0})", Address);
			else
				return String.Format ("MonoObject ({0})", location);
		}
	}
}
