using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayType : TargetType, ITargetArrayType
	{
		string name;
		int size;

		public NativeArrayType (ILanguage language, string name, TargetType element_type,
					int lower_bound, int upper_bound, int size)
			: base (language, TargetObjectKind.Array)
		{
			this.name = name;
			this.size = size;

			this.element_type = element_type;
			this.lower_bound = lower_bound;
			this.upper_bound = upper_bound;
		}
	  
		TargetType element_type;
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

		public TargetType ElementType {
			get {
				return element_type;
			}
		}

		int ITargetArrayType.Rank {
			get {
				return 1;
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

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new NativeArrayObject (this, location, lower_bound, upper_bound);
		}
	}

}

