using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoOpaqueObject : MonoObject, ITargetPointerObject
	{
		new MonoOpaqueType type;

		public MonoOpaqueObject (MonoOpaqueType type, MonoTargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public new ITargetPointerType Type {
			get {
				return type;
			}
		}

		public override bool HasObject {
			get {
				return false;
			}
		}

		bool ITargetObject.HasObject {
			get {
				return false;
			}
		}

		ITargetType ITargetPointerObject.CurrentType {
			get {
				throw new InvalidOperationException ();
			}
		}

		public byte[] GetDereferencedContents (int size)
		{
			try {
				return location.ReadBuffer (size);
			} catch {
				throw new LocationInvalidException ();
			}
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
			throw new InvalidOperationException ();
		}
	}
}

