using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoPointerObject : MonoObject, ITargetPointerObject
	{
		new MonoPointerType type;

		public MonoPointerObject (MonoPointerType type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				return type.TargetType;
			}
		}

		bool ITargetPointerObject.HasDereferencedObject {
			get {
				return !type.IsVoid;
			}
		}

		ITargetObject ITargetPointerObject.DereferencedObject {
			get {
				if (type.IsVoid)
					throw new InvalidOperationException ();

				TargetLocation new_location = location.GetLocationAtOffset (0, false);
				return type.TargetType.GetObject (new_location);
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			try {
				return location.ReadBuffer (size);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
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
			get {
				return location.Address;
			}
		}

		public ITargetObject GetArrayElement (int index)
		{
			throw new InvalidOperationException ();
		}

		public override string Print ()
		{
			return String.Format ("{0}", Address);
		}
	}
}

