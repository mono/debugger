using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoEnumObject : MonoObject
	{
		MonoObject element_object;

		public MonoEnumObject (MonoEnumType type, ITargetLocation location, MonoObject element_obj)
			: base (type, location)
		{
			this.element_object = element_obj;
		}

		public override bool HasObject {
			get {
				return element_object.HasObject;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader, TargetAddress address,
							out TargetAddress dynamic_address)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader, TargetAddress address)
		{
			return Enum.ToObject ((Type) type.TypeHandle, element_object.Object);
		}
	}
}
