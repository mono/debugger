using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayType : TargetArrayType
	{
		string name;
		int size;

		public NativeArrayType (Language language, string name, TargetType element_type,
					int lower_bound, int upper_bound, int size)
			: base (element_type, 1)
		{
			this.name = name;
			this.size = size;

			this.lower_bound = lower_bound;
			this.upper_bound = upper_bound;
		}
	  
		int lower_bound;
		int upper_bound;

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			return new NativeArrayObject (this, location, lower_bound, upper_bound);
		}
	}

}

