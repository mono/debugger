using System;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal delegate void ClassInitHandler (TargetMemoryAccess target, TargetAddress klass);

	internal interface IMonoStructType
	{
		MonoSymbolFile File {
			get;
		}

		TargetStructType Type {
			get;
		}

		MonoClassInfo ClassInfo {
			get; set;
		}

		MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail);

		MonoFunctionType LookupFunction (Cecil.MethodDefinition mdef);
	}

	internal class MonoClassType : TargetClassType, IMonoStructType
	{
		MonoFieldInfo[] fields;
		MonoMethodInfo[] methods;
		MonoPropertyInfo[] properties;
		MonoEventInfo[] events;
		MonoMethodInfo[] constructors;

		Cecil.TypeDefinition type;
		MonoSymbolFile file;
		IMonoStructType parent_type;
		MonoClassInfo class_info;

		bool resolved;
		Hashtable load_handlers;
		int load_handler_id;

		string full_name;

		DebuggerDisplayAttribute debugger_display;
		bool is_compiler_generated;

		public MonoClassType (MonoSymbolFile file, Cecil.TypeDefinition type)
			: base (file.MonoLanguage, TargetObjectKind.Class)
		{
			this.type = type;
			this.file = file;

			if (type.GenericParameters.Count > 0) {
				StringBuilder sb = new StringBuilder (type.FullName);
				sb.Append ('<');
				for (int i = 0; i < type.GenericParameters.Count; i++) {
					if (i > 0)
						sb.Append (',');
					sb.Append (type.GenericParameters [i].Name);
				}
				sb.Append ('>');
				full_name = sb.ToString ();
			} else
				full_name = type.FullName;

			DebuggerBrowsableState? browsable_state;
			MonoSymbolFile.CheckCustomAttributes (type,
							      out browsable_state,
							      out debugger_display,
							      out is_compiler_generated);
		}

		public MonoClassType (MonoSymbolFile file, Cecil.TypeDefinition typedef,
				      MonoClassInfo class_info)
			: this (file, typedef)
		{
			this.class_info = class_info;
		}

		public override string BaseName {
			get { return type.FullName; }
		}

		TargetStructType IMonoStructType.Type {
			get { return this; }
		}

		public Cecil.TypeDefinition Type {
			get { return type; }
		}

		public override bool IsCompilerGenerated {
			get { return is_compiler_generated; }
		}

		public override string Name {
			get { return full_name; }
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

		public override DebuggerDisplayAttribute DebuggerDisplayAttribute {
			get { return debugger_display; }
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public override bool HasParent {
			get { return type.BaseType != null; }
		}

		public override bool ContainsGenericParameters {
			get { return type.GenericParameters.Count != 0; }
		}

		internal override TargetStructType GetParentType (TargetMemoryAccess target)
		{
			if (parent_type != null)
				return parent_type.Type;

			ResolveClass (target, true);
			return parent_type != null ? parent_type.Type : null;
		}

		public override Module Module {
			get { return file.Module; }
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return this; }
		}

		internal int Token {
			get { return (int) (type.MetadataToken.TokenType + type.MetadataToken.RID); }
		}

		void get_fields ()
		{
			if (fields != null)
				return;

			fields = new MonoFieldInfo [type.Fields.Count];

			for (int i = 0; i < fields.Length; i++) {
				Cecil.FieldDefinition field = type.Fields [i];
				TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
				fields [i] = new MonoFieldInfo (this, ftype, i, field);
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		void get_methods ()
		{
			if (methods != null)
				return;

			int num_methods = 0;
			foreach (Cecil.MethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				num_methods++;
			}

			methods = new MonoMethodInfo [num_methods];

			int pos = 0;
			foreach (Cecil.MethodDefinition method in type.Methods) {
				if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
					continue;
				methods [pos] = MonoMethodInfo.Create (this, pos, method);
				pos++;
			}
		}

		void get_properties ()
		{
			if (properties != null)
				return;

			properties = new MonoPropertyInfo [type.Properties.Count];

			for (int i = 0; i < properties.Length; i++) {
				Cecil.PropertyDefinition prop = type.Properties [i];
				Cecil.MethodDefinition m = prop.GetMethod;
				if (m == null) m = prop.SetMethod;

				properties [i] = MonoPropertyInfo.Create (this, i, prop);
			}
		}

		public override TargetMethodInfo[] Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		public override TargetPropertyInfo[] Properties {
			get {
				get_properties ();
				return properties;
			}
		}

		void get_events ()
		{
			if (events != null)
				return;

			events = new MonoEventInfo [type.Events.Count];

			for (int i = 0; i < events.Length; i++) {
				Cecil.EventDefinition ev = type.Events [i];
				events [i] = MonoEventInfo.Create (this, i, ev);
			}
		}

		public override TargetEventInfo[] Events {
			get {
				get_events ();
				return events;
			}
		}

		public TargetObject GetStaticEvent (StackFrame frame, int index)
		{
			get_events ();
			return null;
		}

		void get_constructors ()
		{
			if (constructors != null)
				return;

			constructors = new MonoMethodInfo [type.Constructors.Count];

			for (int i = 0; i < constructors.Length; i++) {
				Cecil.MethodDefinition method = type.Constructors [i];
				constructors [i] = MonoMethodInfo.Create (this, i, method);
			}
		}

		public override TargetMethodInfo[] Constructors {
			get {
				get_constructors ();
				return constructors;
			}
		}

		internal override TargetClass GetClass (TargetMemoryAccess target)
		{
			return ResolveClass (target, false);
		}

		public MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			if (resolved)
				return class_info;

			if (class_info == null) {
				int token = (int) type.MetadataToken.ToUInt ();
				class_info = file.LookupClassInfo (target, token);
			}

			if (class_info == null) {
				if (!fail)
					return null;

				throw new TargetException (TargetError.ClassNotInitialized,
							   "Class `{0}' not initialized yet.", Name);
			}

			if (class_info.HasParent) {
				MonoClassInfo parent_info = class_info.GetParent (target);
				parent_type = (IMonoStructType) parent_info.Type;
				parent_type.ClassInfo = parent_info;
				if (parent_type.ResolveClass (target, fail) == null)
					return null;
			}

			resolved = true;
			return class_info;
		}

		MonoClassInfo IMonoStructType.ClassInfo {
			get { return class_info; }
			set { class_info = value; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			ResolveClass (target, true);
			return new MonoClassObject (this, class_info, location);
		}

		internal TargetStructObject GetCurrentObject (TargetMemoryAccess target,
							      TargetLocation location)
		{
			// location.Address resolves to the address of the MonoObject,
			// dereferencing it once gives us the vtable, dereferencing it
			// twice the class.
			TargetAddress address;
			address = target.ReadAddress (location.GetAddress (target));
			address = target.ReadAddress (address);

			TargetType current = File.MonoLanguage.ReadMonoClass (target, address);
			if (current == null)
				return null;

			if (IsByRef && !current.IsByRef) // Unbox
				location = location.GetLocationAtOffset (
					2 * target.TargetMemoryInfo.TargetAddressSize);

			return (TargetStructObject) current.GetObject (target, location);
		}

		Dictionary<int,MonoFunctionType> function_hash;

		public MonoFunctionType LookupFunction (Cecil.MethodDefinition mdef)
		{
			int token = MonoDebuggerSupport.GetMethodToken (mdef);
			if (function_hash == null)
				function_hash = new Dictionary<int,MonoFunctionType> ();
			if (!function_hash.ContainsKey (token)) {
				MonoFunctionType function = new MonoFunctionType (this, mdef);
				function_hash.Add (token, function);
				return function;
			}

			return function_hash [token];
		}
	}
}
