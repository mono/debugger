using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoEnumType : TargetEnumType
	{
		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;

		MonoSymbolFile file;
		Cecil.TypeDefinition type;
		bool is_flags;

		public MonoEnumType (MonoSymbolFile file, Cecil.TypeDefinition type)
			: base (file.MonoLanguage)
		{
			this.type = type;
			this.file = file;
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public Cecil.TypeDefinition Type {
			get { return type; }
		}

		public override string Name {
			get { return type.FullName; }
		}

		public override bool IsByRef {
			get { return !type.IsValueType; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return 2 * Language.TargetInfo.TargetAddressSize; }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			foreach (Cecil.CustomAttribute cattr in type.CustomAttributes) {
				if (cattr.Constructor.DeclaringType.FullName == "System.FlagsAttribute") {
					is_flags = true;
					break;
				}
			}

			int num_fields = 0, num_sfields = 0;

			foreach (Cecil.FieldDefinition field in type.Fields) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0, i = 0;
			foreach (Cecil.FieldDefinition field in type.Fields) {
				TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
				if (field.IsStatic) {
					static_fields [spos] = new MonoFieldInfo (ftype, spos, i, field);
					spos++;
				} else {
					if (field.Name != "value__")
						throw new InternalError ("Mono enum type has instance field with name other than 'value__'.");
					fields [pos] = new MonoFieldInfo (ftype, pos, i, field);
					pos++;
				}

				i++;
			}
			if (pos > 1)
				throw new InternalError ("Mono enum type has more than one instance field.");
		}

		public override bool IsFlagsEnum {
			get {
				get_fields ();
				return is_flags;
			}
		}

		public override TargetFieldInfo Value {
			get {
				get_fields ();
				return fields[0];
			}
		}

		public override TargetFieldInfo[] Members {
			get {
				get_fields ();
				return static_fields;
			}
		}
	}
}
