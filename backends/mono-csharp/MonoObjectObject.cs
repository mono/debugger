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

		public override bool HasObject {
			get {
				return CurrentType.HasObject;
			}
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return HasObject;
			}
		}

		public MonoType CurrentType {
			get {
				try {
					// location.Address resolves to the address of the MonoObject,
					// dereferencing it once gives us the vtable, dereferencing it
					// twice the class.
					TargetAddress address;
					address = location.TargetMemoryAccess.ReadAddress (location.Address);
					address = location.TargetMemoryAccess.ReadAddress (address);
					return type.Table.GetTypeFromClass (address.Address);
				} catch {
					throw new LocationInvalidException ();
				}
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				return CurrentType;
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

		protected override object GetObject (ITargetMemoryReader reader,
						     MonoTargetLocation location)
		{
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
}
