using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoObjectObject : MonoObject, ITargetPointerObject
	{
		new MonoObjectTypeInfo type;

		public MonoObjectObject (MonoObjectTypeInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get { return type.Type; }
		}

		protected MonoTypeInfo GetCurrentType ()
		{
			try {
				// location.Address resolves to the address of the MonoObject,
				// dereferencing it once gives us the vtable, dereferencing it
				// twice the class.
				TargetAddress address;
				address = location.TargetMemoryAccess.ReadAddress (location.Address);
				address = location.TargetMemoryAccess.ReadGlobalAddress (address);

				MonoType klass = type.Type.File.MonoLanguage.GetClass (address);
				if (klass == null)
					return null;

				return klass.GetTypeInfo ();
			} catch {
				return null;
			}
		}

		public MonoTypeInfo CurrentType {
			get {
				MonoTypeInfo type = GetCurrentType ();
				if (type == null)
					throw new LocationInvalidException ();
				return type;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				return CurrentType.Type;
			}
		}

		public bool HasDereferencedObject {
			get {
				return GetCurrentType () != null;
			}
		}

		public ITargetObject DereferencedObject {
			get {
				MonoTypeInfo current_type = CurrentType;

				// If this is a reference type, then the `MonoObject *' already
				// points to the boxed object itself.
				// If it's a valuetype, then the boxed contents is immediately
				// after the `MonoObject' header.

				int offset = current_type.Type.IsByRef ? 0 : type.Size;
				TargetLocation new_location = location.GetLocationAtOffset (offset, false);

				return current_type.GetObject (new_location);
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			throw new InvalidOperationException ();
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
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

		public override string Print ()
		{
			if (HasAddress)
				return String.Format ("MonoObject ({0})", Address);
			else
				return String.Format ("MonoObject ({0})", location);
		}
	}
}
