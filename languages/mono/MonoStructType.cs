using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStructType : IMonoStructType
	{
		public MonoSymbolFile File {
			get; private set;
		}

		public TargetClassType Type {
			get; private set;
		}

		public Cecil.TypeDefinition TypeDef {
			get; private set;
		}

		MonoClassInfo class_info;

		public MonoStructType (MonoSymbolFile file, TargetClassType type, Cecil.TypeDefinition typedef)
		{
			this.File = file;
			this.Type = type;
			this.TypeDef = typedef;
		}

		MonoClassInfo IMonoStructType.ClassInfo {
			get { return class_info; }
			set { class_info = value; }
		}

		public MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			return null;
		}

		MonoFieldInfo[] fields;
		MonoPropertyInfo[] properties;
		MonoEventInfo[] events;
		MonoMethodInfo[] methods;
		MonoMethodInfo[] constructors;

		Dictionary<Cecil.MethodDefinition,MonoFunctionType> function_hash;

		void get_fields ()
		{
			lock (this) {
				if (fields != null)
					return;

				fields = new MonoFieldInfo [TypeDef.Fields.Count];

				for (int i = 0; i < fields.Length; i++) {
					Cecil.FieldDefinition field = TypeDef.Fields [i];
					TargetType ftype = File.MonoLanguage.LookupMonoType (field.FieldType);
					fields [i] = new MonoFieldInfo (this, ftype, i, field);
				}
			}
		}

		void get_properties ()
		{
			lock (this) {
				if (properties != null)
					return;

				properties = new MonoPropertyInfo [TypeDef.Properties.Count];

				for (int i = 0; i < properties.Length; i++) {
					Cecil.PropertyDefinition prop = TypeDef.Properties [i];
					Cecil.MethodDefinition m = prop.GetMethod;
					if (m == null) m = prop.SetMethod;

					properties [i] = MonoPropertyInfo.Create (this, i, prop);
				}
			}
		}

		void get_events ()
		{
			lock (this) {
				if (events != null)
					return;

				events = new MonoEventInfo [TypeDef.Events.Count];

				for (int i = 0; i < events.Length; i++) {
					Cecil.EventDefinition ev = TypeDef.Events [i];
					events [i] = MonoEventInfo.Create (this, i, ev);
				}
			}
		}

		void get_methods ()
		{
			lock (this) {
				if (methods != null)
					return;

				function_hash = new Dictionary<Cecil.MethodDefinition, MonoFunctionType> ();

				foreach (Cecil.MethodDefinition method in TypeDef.Methods) {
					MonoFunctionType func = new MonoFunctionType (this, method);
					function_hash.Add (method, func);
				}

				int num_methods = 0;
				foreach (Cecil.MethodDefinition method in GetMethods (TypeDef, false)) {
					if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
						continue;
					num_methods++;
				}

				methods = new MonoMethodInfo [num_methods];

				int pos = 0;
				foreach (Cecil.MethodDefinition method in GetMethods (TypeDef, false)) {
					if ((method.Attributes & Cecil.MethodAttributes.SpecialName) != 0)
						continue;
					methods [pos] = MonoMethodInfo.Create (this, pos, method);
					pos++;
				}
			}
		}

		static IEnumerable<Cecil.MethodDefinition> GetMethods (Cecil.TypeDefinition type, bool constructor)
		{
			foreach (var method in type.Methods) {
				if (constructor && method.IsConstructor)
					yield return method;

				if (!constructor && !method.IsConstructor)
					yield return method;
			}
		}

		void get_constructors ()
		{
			lock (this) {
				if (constructors != null)
					return;

				var ctors = new List<Cecil.MethodDefinition> (GetMethods (TypeDef, true));

				for (int i = 0; i < ctors.Count; i++) {
					Cecil.MethodDefinition method = ctors [i];
					constructors [i] = MonoMethodInfo.Create (this, i, method);
				}
			}
		}

		public TargetMethodInfo[] Methods {
			get {
				get_methods ();
				return methods;
			}
		}

		public TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return fields;
			}
		}

		public TargetPropertyInfo[] Properties {
			get {
				get_properties ();
				return properties;
			}
		}

		public TargetEventInfo[] Events {
			get {
				get_events ();
				return events;
			}
		}

		public TargetMethodInfo[] Constructors {
			get {
				get_constructors ();
				return constructors;
			}
		}

		public MonoFunctionType LookupFunction (Cecil.MethodDefinition mdef)
		{
			get_methods ();
			return function_hash [mdef];
		}
	}
}
