using System;
using System.Collections;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStructType : MonoType, ITargetStructType
	{
		MonoFieldInfo[] fields;
		MonoPropertyInfo[] properties;
		MonoMethodInfo[] methods;
		ITargetMemoryReader info;
		internal readonly MonoSymbolFileTable Table;
		int num_fields, num_properties, num_methods;
		int field_info_size, property_info_size, method_info_size;
		long offset;
		bool is_byref;

		protected readonly TargetAddress invoke_method;

		public MonoStructType (Type type, int size, ITargetMemoryReader info,
				       MonoSymbolFileTable table)
			: base (type, size, true)
		{
			is_byref = info.ReadByte () != 0;
			num_fields = info.BinaryReader.ReadInt32 ();
			field_info_size = info.BinaryReader.ReadInt32 ();
			num_properties = info.BinaryReader.ReadInt32 ();
			property_info_size = info.BinaryReader.ReadInt32 ();
			num_methods = info.BinaryReader.ReadInt32 ();
			method_info_size = info.BinaryReader.ReadInt32 ();
			this.info = info;
			this.offset = info.Offset;
			this.Table = table;
			info.Offset += field_info_size + property_info_size + method_info_size;
			invoke_method = table.Language.MonoDebuggerInfo.runtime_invoke;
		}

		// <remarks>
		//   We can't do this in the .ctor since a field may be of the current
		//   classes type, but the .ctor is called before the current class is
		//   inserted into the type table hash.
		// </remarks>
		void init_fields ()
		{
			if (fields != null)
				return;

			info.Offset = offset;
			fields = new MonoFieldInfo [num_fields];

			FieldInfo[] mono_fields = type.GetFields (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);
			if (mono_fields.Length != num_fields)
				throw new InternalError (
					"Type.GetFields() returns {0} fields, but the JIT has {1}",
					mono_fields.Length, num_fields);

			for (int i = 0; i < num_fields; i++)
				fields [i] = new MonoFieldInfo (this, i, mono_fields [i], info, Table);
		}

		public ITargetFieldInfo[] Fields {
			get {
				init_fields ();
				return fields;
			}
		}

		protected class MonoFieldInfo : ITargetFieldInfo
		{
			public readonly MonoType Type;
			public readonly FieldInfo FieldInfo;
			public readonly int Offset;
			public readonly int Index;

			internal MonoFieldInfo (MonoStructType type, int index, FieldInfo finfo,
						ITargetMemoryReader info, MonoSymbolFileTable table)
			{
				Index = index;
				FieldInfo = finfo;
				Offset = info.BinaryReader.ReadInt32 ();
				TargetAddress type_info = info.ReadAddress ();
				Type = type.GetType (
					finfo.FieldType, info.TargetMemoryAccess, type_info, table);
			}

			ITargetType ITargetFieldInfo.Type {
				get {
					return Type;
				}
			}

			string ITargetFieldInfo.Name {
				get {
					return FieldInfo.Name;
				}
			}

			int ITargetFieldInfo.Index {
				get {
					return Index;
				}
			}

			object ITargetFieldInfo.FieldHandle {
				get {
					return FieldInfo;
				}
			}

			public override string ToString ()
			{
				return String.Format ("MonoField ({0:x}:{1}:{2})",
						      Offset, FieldInfo.Name, Type);
			}
		}

		internal ITargetObject GetField (ITargetLocation location, int index)
		{
			init_fields ();

			ITargetMemoryAccess memory;
			TargetAddress address = GetAddress (location, out memory);

			try {
				ITargetLocation field_loc = new RelativeTargetLocation (
					location, address + fields [index].Offset);

				return fields [index].Type.GetObject (field_loc);
			} catch {
				throw new LocationInvalidException ();
			}
		}

		void init_properties ()
		{
			if (properties != null)
				return;

			info.Offset = offset + field_info_size;
			properties = new MonoPropertyInfo [num_properties];

			PropertyInfo[] mono_properties = type.GetProperties (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public | BindingFlags.NonPublic);

			if (mono_properties.Length != num_properties)
				throw new InternalError (
					"Type.GetProperties() returns {0} properties, but the JIT has {1}",
					mono_properties.Length, num_properties);

			for (int i = 0; i < num_properties; i++)
				properties [i] = new MonoPropertyInfo (
					this, i, mono_properties [i], info, Table);
		}

		public ITargetFieldInfo[] Properties {
			get {
				init_properties ();
				return properties;
			}
		}

		protected ITargetObject Invoke (TargetAddress method, ITargetLocation this_location)
		{
			ITargetMemoryAccess memory;
			TargetAddress this_object = GetAddress (this_location, out memory);
			TargetAddress exc_object;

			IInferior inferior = memory as IInferior;
			if (inferior == null)
				throw new LocationInvalidException ();

			TargetAddress retval = inferior.CallInvokeMethod (
				invoke_method, method, this_object, new TargetAddress [0], out exc_object);

			if (!exc_object.IsNull) {
				TargetAddress exc_class = memory.ReadAddress (exc_object);
				exc_class = memory.ReadAddress (exc_class);

				ITargetClassObject exc = null;

				try {
					MonoType exc_type = Table.GetTypeFromClass (exc_class.Address);

					ITargetLocation exc_loc = new RelativeTargetLocation (
						this_location, exc_object);

					exc = new MonoClassObject ((MonoClassType) exc_type, exc_loc, false);
				} catch {
					throw new LocationInvalidException ();
				}

				throw new TargetInvocationException (exc);
			}

			ITargetLocation retval_loc = new RelativeTargetLocation (this_location, retval);

			MonoObjectObject retval_obj = new MonoObjectObject (ObjectType, retval_loc, false);
			try {
				ITargetObject obj = retval_obj.Object as ITargetObject;
				if (obj != null)
					return obj;
			} catch {
				// Do nothing.
			}
			return retval_obj;
		}

		protected class MonoPropertyInfo : ITargetFieldInfo
		{
			public readonly MonoType Type;
			public readonly PropertyInfo PropertyInfo;
			public readonly int Index;
			public readonly TargetAddress Getter, Setter;
			public readonly MonoStructType StructType;

			internal MonoPropertyInfo (MonoStructType type, int index, PropertyInfo pinfo,
						   ITargetMemoryReader info, MonoSymbolFileTable table)
			{
				StructType = type;
				Index = index;
				PropertyInfo = pinfo;
				TargetAddress type_info = info.ReadAddress ();
				if (!type_info.IsNull)
					Type = type.GetType (
						pinfo.PropertyType, info.TargetMemoryAccess, type_info, table);
				Getter = info.ReadAddress ();
				Setter = info.ReadAddress ();
			}

			ITargetType ITargetFieldInfo.Type {
				get {
					return Type;
				}
			}

			string ITargetFieldInfo.Name {
				get {
					return PropertyInfo.Name;
				}
			}

			int ITargetFieldInfo.Index {
				get {
					return Index;
				}
			}

			object ITargetFieldInfo.FieldHandle {
				get {
					return PropertyInfo;
				}
			}

			internal ITargetObject Get (ITargetLocation location)
			{
				return StructType.Invoke (Getter, location);
			}

			public override string ToString ()
			{
				return String.Format ("MonoProperty ({0:x}:{1}:{2})",
						      Index, PropertyInfo.Name, Type);
			}
		}

		internal ITargetObject GetProperty (ITargetLocation location, int index)
		{
			init_properties ();

			return properties [index].Get (location);
		}

		void init_methods ()
		{
			if (methods != null)
				return;

			info.Offset = offset + field_info_size + property_info_size;
			methods = new MonoMethodInfo [num_methods];

			MethodInfo[] mono_methods = type.GetMethods (
				BindingFlags.DeclaredOnly | BindingFlags.Instance |
				BindingFlags.Public);

			ArrayList list = new ArrayList ();
			for (int i = 0; i < mono_methods.Length; i++) {
				if (mono_methods [i].IsSpecialName)
					continue;

				list.Add (mono_methods [i]);
			}

			if (list.Count != num_methods)
				throw new InternalError (
					"Type.GetMethods() returns {0} methods, but the JIT has {1}",
					list.Count, num_methods);

			for (int i = 0; i < num_methods; i++)
				methods [i] = new MonoMethodInfo (
					this, i, (MethodInfo) list [i], info, Table);
		}

		public ITargetMethodInfo[] Methods {
			get {
				init_methods ();
				return methods;
			}
		}

		protected class MonoMethodInfo : ITargetMethodInfo
		{
			public readonly MethodInfo MethodInfo;
			public readonly int Index;
			public readonly TargetAddress Method;
			public readonly MonoStructType StructType;
			public readonly MonoType ReturnType;
			public readonly MonoType[] Parameters;

			internal MonoMethodInfo (MonoStructType type, int index, MethodInfo minfo,
						 ITargetMemoryReader info, MonoSymbolFileTable table)
			{
				StructType = type;
				Index = index;
				MethodInfo = minfo;
				Method = info.ReadAddress ();
				TargetAddress type_info = info.ReadAddress ();
				if (!type_info.IsNull)
					ReturnType = type.GetType (
						minfo.ReturnType, info.TargetMemoryAccess, type_info, table);
				
				int num_params = info.BinaryReader.ReadInt32 ();
				Parameters = new MonoType [num_params];

				ParameterInfo[] parameters = minfo.GetParameters ();
				if (parameters.Length != num_params)
					throw new InternalError (
						"MethodInfo.GetParameters() returns {0} parameters " +
						"for method {1}, but the JIT has {2}",
						parameters.Length, minfo.ReflectedType.Name + "." +
						minfo.Name, num_params);
				for (int i = 0; i < num_params; i++) {
					TargetAddress param_info = info.ReadAddress ();
					Parameters [i] = type.GetType (
						parameters [i].ParameterType, info.TargetMemoryAccess,
						param_info, table);
				}
			}

			ITargetType ITargetMethodInfo.ReturnType {
				get {
					return null;
				}
			}

			ITargetType[] ITargetMethodInfo.ParameterTypes {
				get {
					return Parameters;
				}
			}

			string ITargetMethodInfo.Name {
				get {
					return MethodInfo.Name;
				}
			}

			int ITargetMethodInfo.Index {
				get {
					return Index;
				}
			}

			object ITargetMethodInfo.MethodHandle {
				get {
					return MethodInfo;
				}
			}

			internal ITargetObject Invoke (ITargetLocation location, ITargetObject[] args)
			{
				if (args.Length != 0)
					throw new NotSupportedException ();

				return StructType.Invoke (Method, location);
			}

			public override string ToString ()
			{
				return String.Format ("MonoMethod ({0:x}:{1})",
						      Index, MethodInfo.Name);
			}
		}

		internal ITargetObject InvokeMethod (ITargetLocation location, int index,
						     ITargetObject[] arguments)
		{
			init_methods ();

			return methods [index].Invoke (location, arguments);
		}

		public override bool IsByRef {
			get {
				return is_byref;
			}
		}

		public override bool HasObject {
			get {
				return true;
			}
		}

		bool ITargetType.HasObject {
			get {
				return true;
			}
		}

		public override MonoObject GetObject (ITargetLocation location, bool isbyref)
		{
			return new MonoStructObject (this, location, isbyref);
		}

		public string PrintObject (ITargetLocation location)
		{
			MonoMethodInfo method = ObjectToString as MonoMethodInfo;
			if (method == null)
				throw new InternalError ();

			ITargetObject obj = method.Invoke (location, new ITargetObject [0]);
			return (string) obj.Object;
		}
	}
}
