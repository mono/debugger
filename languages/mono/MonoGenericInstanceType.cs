using System;
using System.Text;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetGenericInstanceType, IMonoStructType
	{
		public readonly MonoClassType Container;
		DebuggerDisplayAttribute debugger_display;
		DebuggerTypeProxyAttribute type_proxy;
		TargetType[] type_args;
		TargetAddress class_ptr;
		MonoClassInfo class_info;
		IMonoStructType parent_type;
		string full_name;
		bool resolved;

		MonoStructType struct_type;

		public MonoGenericInstanceType (MonoClassType container, TargetType[] type_args,
						TargetAddress class_ptr)
			: base (container.File.MonoLanguage)
		{
			this.Container = container;
			this.type_args = type_args;
			this.class_ptr = class_ptr;

			struct_type = new MonoStructType (container.File, this, container.Type);

			StringBuilder sb = new StringBuilder (container.BaseName);
			sb.Append ('<');
			for (int i = 0; i < type_args.Length; i++) {
				if (i > 0)
					sb.Append (',');
				sb.Append (type_args [i].Name);
			}
			sb.Append ('>');
			full_name = sb.ToString ();

			bool is_compiler_generated;
			DebuggerBrowsableState? browsable_state;
			MonoSymbolFile.CheckCustomAttributes (container.Type,
							      out browsable_state,
							      out debugger_display,
							      out type_proxy,
							      out is_compiler_generated);
		}

		TargetClassType IMonoStructType.Type {
			get { return this; }
		}

		public override string BaseName {
			get { return Container.BaseName; }
		}

		public override string Name {
			get { return full_name; }
		}

		public override Module Module {
			get { return Container.Module; }
		}

		public MonoSymbolFile File {
			get { return Container.File; }
		}

		public override TargetClassType ContainerType {
			get { return Container; }
		}

		public override TargetType[] TypeArguments {
			get { return type_args; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override bool IsByRef {
			get { return Container.IsByRef; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 2 * Language.TargetInfo.TargetAddressSize; }
		}

		public override bool HasParent {
			get { return true; }
		}

		#region Members

		public override TargetMethodInfo[] Methods {
			get { return struct_type.Methods; }
		}

		public override TargetMethodInfo[] Constructors {
			get { return struct_type.Constructors; }
		}

		public override TargetFieldInfo[] Fields {
			get { return struct_type.Fields; }
		}

		public override TargetPropertyInfo[] Properties {
			get { return struct_type.Properties; }
		}

		public override TargetEventInfo[] Events {
			get { return struct_type.Events; }
		}

		#endregion

		public override DebuggerDisplayAttribute DebuggerDisplayAttribute {
			get { return debugger_display; }
		}

		public override DebuggerTypeProxyAttribute DebuggerTypeProxyAttribute {
			get { return type_proxy; }
		}

		internal override TargetClassType GetParentType (TargetMemoryAccess target)
		{
			ResolveClass (target, true);

			MonoClassInfo parent = class_info.GetParent (target);
			if (parent == null)
				return null;

			return parent.Type;
		}

		internal TargetClassObject GetCurrentObject (TargetMemoryAccess target,
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

			return (TargetClassObject) current.GetObject (target, location);
		}

		public MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			if (resolved)
				return class_info;

			if (class_info == null) {
				if (class_ptr.IsNull)
					return null;

				TargetAddress klass = target.ReadAddress (class_ptr);
				if (klass.IsNull)
					return null;

				class_info = File.MonoLanguage.ReadClassInfo (target, klass);
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

		public override bool IsClassInitialized {
			get { return class_info != null; }
		}

		MonoClassInfo IMonoStructType.ClassInfo {
			get { return class_info; }
			set { class_info = value; }
		}

		internal override TargetClass GetClass (TargetMemoryAccess target)
		{
			ResolveClass (target, true);
			return class_info;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			ResolveClass (target, true);
			return new MonoGenericInstanceObject (this, class_info, location);
		}

		Dictionary<int,MonoFunctionType> function_hash;

		MonoFunctionType IMonoStructType.LookupFunction (Cecil.MethodDefinition mdef)
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
