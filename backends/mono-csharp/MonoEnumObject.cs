using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoEnumObject : MonoFundamentalObjectBase
	{
		MonoFundamentalObjectBase element_object;

		public MonoEnumObject (MonoEnumType type, MonoTargetLocation location,
				       MonoFundamentalObjectBase element_obj)
			: base (type, location)
		{
			this.element_object = element_obj;
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
			return Enum.ToObject ((Type) type.TypeHandle, element_object.GetObject ());
		}
	}
}
