using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumType : TargetEnumType
	{
		string name;
		int size;

		string[] element_names;
		int[] element_values;

		NativeFieldInfo[] members;
		NativeFieldInfo value;

		public NativeEnumType (Language language, string name, int size,
				       string[] element_names, int[] element_values)
			: base (language)
		{
			this.name = name;
			this.size = size;
			this.element_names = element_names;
			this.element_values = element_values;

			members = new NativeFieldInfo [element_names.Length];
			int i;
			for (i = 0; i < element_names.Length; i ++) {
				members[i] = new NativeFieldInfo (
					this, element_names[i], i, true, element_values[i]);
			}
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public string[] ElementNames {
			get {
				return element_names;
			}
		}

		public int[] ElementValues {
			get {
				return element_values;
			}
		}

		public override bool IsFlagsEnum {
			get {
				return false;
			}
		}

		public override TargetFieldInfo Value {
			get {
				return value;
			}
		}

		public override TargetFieldInfo[] Members {
			get {
				return members;
			}
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new NativeEnumObject (this, location);
		}
	}
}
