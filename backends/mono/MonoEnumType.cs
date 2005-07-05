using System;
using System.Text;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoEnumType : MonoType, ITargetEnumType
	{
		MonoFieldInfo[] fields;
		MonoFieldInfo[] static_fields;

		public MonoEnumType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Enum, type)
		{ }

		public override bool IsByRef {
			get { return !type.IsValueType; }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			int num_fields = 0, num_sfields = 0;

			R.FieldInfo[] finfo = type.GetFields (
				R.BindingFlags.DeclaredOnly | R.BindingFlags.Static | R.BindingFlags.Instance |
				R.BindingFlags.Public | R.BindingFlags.NonPublic);

			foreach (R.FieldInfo field in finfo) {
				if (field.IsStatic)
					num_sfields++;
				else
					num_fields++;
			}

			fields = new MonoFieldInfo [num_fields];
			static_fields = new MonoFieldInfo [num_sfields];

			int pos = 0, spos = 0;
			for (int i = 0; i < finfo.Length; i++) {
				if (finfo [i].IsStatic) {
					static_fields [spos] = new MonoFieldInfo (File, spos, i, finfo [i]);
					spos++;
				} else {
					if (finfo[i].Name != "value__")
						throw new InternalError ("Mono enum type has instance field with name other than 'value__'.");
					fields [pos] = new MonoFieldInfo (File, pos, i, finfo [i]);
					pos++;
				}
			}
			if (pos > 1)
				throw new InternalError ("Mono enum type has more than one instance field.");
		}

		internal MonoFieldInfo Value {
			get {
				get_fields ();
				return fields[0];
			}
		}

		internal MonoFieldInfo[] Members {
			get {
				get_fields ();
				return static_fields;
			}
		}

		ITargetFieldInfo ITargetEnumType.Value {
			get { return Value; }		    
		}

		ITargetFieldInfo[] ITargetEnumType.Members {
			get { return Members; }
		}

		public ITargetObject GetMember (StackFrame frame, int index)
		{
			MonoEnumTypeInfo info = GetTypeInfo () as MonoEnumTypeInfo;
			if (info == null)
				return null;

			return info.GetMember (frame, index);
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			return new MonoEnumTypeInfo (this, info);
		}
	}

}
