using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoObjectObject : MonoObject, ITargetPointerObject
	{
		new MonoObjectType type;

		public MonoObjectObject (MonoObjectType type, MonoTargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		protected MonoType GetCurrentType ()
		{
			try {
				// location.Address resolves to the address of the MonoObject,
				// dereferencing it once gives us the vtable, dereferencing it
				// twice the class.
				TargetAddress address;
				address = location.TargetMemoryAccess.ReadAddress (location.Address);
				address = location.TargetMemoryAccess.ReadAddress (address);
				return type.Table.GetTypeFromClass (address.Address);
			} catch {
				return null;
			}
		}

		public MonoType CurrentType {
			get {
				MonoType type = GetCurrentType ();
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

		public bool HasDereferencedObject {
			get {
				return GetCurrentType () != null;
			}
		}

		public ITargetObject DereferencedObject {
			get {
				MonoType current_type = CurrentType;

				// If this is a reference type, then the `MonoObject *' already
				// points to the boxed object itself.
				// If it's a valuetype, then the boxed contents is immediately
				// after the `MonoObject' header.

				int offset = current_type.IsByRef ? 0 : type.Size;
				MonoTargetLocation new_location = location.GetLocationAtOffset (offset, false);

				return current_type.GetObject (new_location);
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			throw new InvalidOperationException ();
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							MonoTargetLocation location,
							out MonoTargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
