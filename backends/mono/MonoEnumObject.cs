using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoEnumObject : MonoFundamentalObjectBase
	{
		MonoFundamentalObjectBase element_object;

		public MonoEnumObject (MonoEnumType type, TargetLocation location,
				       MonoFundamentalObjectBase element_obj)
			: base (type, location)
		{
			this.element_object = element_obj;
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		protected override object GetObject (ITargetMemoryReader reader,
						     TargetLocation location)
		{
			return Enum.ToObject ((Type) type.TypeHandle,
					      element_object.GetObject ());
		}
	}
}
