using System;

namespace Mono.Debugger.Languages.Native
{

	internal class NativeArrayType : NativeType, ITargetArrayType
	{
		public NativeArrayType (string name, NativeType element_type, int lower_bound, int upper_bound, int size)
			: base (name, TargetObjectKind.Array, size, true)
		{
			this.element_type = element_type;
			this.lower_bound = lower_bound;
			this.upper_bound = upper_bound;
		}
	  
		NativeType element_type;
		int lower_bound;
		int upper_bound;

		public NativeType ElementType {
			get {
				return element_type;
			}
		}

		ITargetType ITargetArrayType.ElementType {
			get {
				return ElementType;
			}
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeArrayObject (this, location, lower_bound, upper_bound);
		}
	}

}

