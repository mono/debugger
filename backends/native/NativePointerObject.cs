using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerObject : NativeObject, ITargetPointerObject
	{
		new NativePointerType type;

		public NativePointerObject (NativePointerType type, TargetLocation location)
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
				throw new InvalidOperationException ();
			}
		}

		bool ITargetPointerObject.HasDereferencedObject {
			get { return type.HasStaticType; }
		}

		ITargetObject ITargetPointerObject.DereferencedObject {
			get {
				if (!type.HasStaticType)
					throw new InvalidOperationException ();

				TargetLocation new_location = location.GetLocationAtOffset (0, false);
				return type.StaticType.GetObject (new_location);
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

		public override string Print ()
		{
			return String.Format ("{0}", Address);
		}
	}
}

