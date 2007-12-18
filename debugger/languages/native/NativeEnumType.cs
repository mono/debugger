using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeEnumType : TargetEnumType
	{
		string name;
		int size;

		string[] element_names;
		int[] element_values;

		NativeEnumInfo[] members;
		NativeEnumInfo value;

		public NativeEnumType (Language language, string name, int size,
				       string[] element_names, int[] element_values)
			: base (language)
		{
			this.name = name;
			this.size = size;
			this.element_names = element_names;
			this.element_values = element_values;

			members = new NativeEnumInfo [element_names.Length];
			for (int i = 0; i < element_names.Length; i++)
				members [i] = new NativeEnumInfo (
					this, element_names [i], i, element_values [i]);

			value = new NativeEnumInfo (language.IntegerType, "__value", 0, 0);
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
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

		public override TargetEnumInfo Value {
			get {
				return value;
			}
		}

		public override TargetEnumInfo[] Members {
			get {
				return members;
			}
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}
	}

	[Serializable]
	internal class NativeEnumInfo : TargetEnumInfo
	{
		int const_value;

		public NativeEnumInfo (TargetType field_type, string name, int index, int value)
			: base (field_type, name, index, false, 0, 0, true)
		{
			this.const_value = value;
		}

		public NativeEnumInfo (TargetType field_type, string name, int index)
			: base (field_type, name, index, false, 0, 0, false)
		{ }

		public override object ConstValue {
			get {
				if (HasConstValue)
					return const_value;
				else
					throw new InvalidOperationException ();
			}
		}
	}
}
