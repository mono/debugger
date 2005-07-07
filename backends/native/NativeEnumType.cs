using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumType : NativeType, ITargetEnumType
	{
		string[] element_names;
		int[] element_values;

		NativeFieldInfo[] members;
		NativeFieldInfo value;

		public NativeEnumType (string name, int size, string[] element_names, int[] element_values)
		  : base (name, TargetObjectKind.Enum, size, true)
		{
			this.element_names = element_names;
			this.element_values = element_values;

			members = new NativeFieldInfo [element_names.Length];
			int i;
			for (i = 0; i < element_names.Length; i ++) {
				members[i] = new NativeFieldInfo (this, element_names[i], i, true, element_values[i]);
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

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeEnumObject (this, location);
		}
	}
}
