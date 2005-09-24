using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumType : TargetType, ITargetEnumType
	{
		string name;
		int size;

		string[] element_names;
		int[] element_values;

		NativeFieldInfo[] members;
		NativeFieldInfo value;

		public NativeEnumType (ILanguage language, string name, int size,
				       string[] element_names, int[] element_values)
			: base (language, TargetObjectKind.Enum)
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

		public ITargetFieldInfo Value {
			get {
				return value;
			}
		}

		public ITargetFieldInfo[] Members {
			get {
				return members;
			}
		}

		public ITargetObject GetMember (StackFrame frame, int index)
		{
			return null;
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
