using System;
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
	internal delegate void ClassInitHandler (TargetMemoryAccess target, TargetAddress klass);

	internal interface IMonoStructType
	{
		MonoSymbolFile File {
			get;
		}

		TargetClassType Type {
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
		Cecil.TypeDefinition type;
		MonoSymbolFile file;
		IMonoStructType parent_type;
		MonoClassInfo class_info;

		MonoStructType struct_type;

		bool resolved;
		Hashtable load_handlers;
		int load_handler_id;

		string full_name;

		DebuggerDisplayAttribute debugger_display;
		DebuggerTypeProxyAttribute type_proxy;
		bool is_compiler_generated;

		public MonoClassType (MonoSymbolFile file, Cecil.TypeDefinition type)
			: base (file.MonoLanguage, TargetObjectKind.Class)
		{
			this.type = type;
			this.file = file;

			struct_type = new MonoStructType (file, this, type);

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
							      out type_proxy,
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

		TargetClassType IMonoStructType.Type {
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

		public override DebuggerTypeProxyAttribute DebuggerTypeProxyAttribute {
			get { return type_proxy; }
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

		internal override TargetClassType GetParentType (TargetMemoryAccess target)
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

		public TargetObject GetStaticEvent (StackFrame frame, int index)
		{
			throw new NotImplementedException ();
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

		public override TargetClass ForceClassInitialization (Thread thread)
		{
			if (class_info != null)
				return class_info;

			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					class_info = ResolveClass (target, false);
					return class_info;
				});

			if (class_info != null)
				return class_info;

			TargetAddress image = file.MonoImage;

			TargetAddress klass = thread.CallMethod (
				file.MonoLanguage.MonoDebuggerInfo.LookupClass, image, 0, 0,
				Name);

			return (TargetClass) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					class_info = MonoClassInfo.ReadClassInfo (
						file.MonoLanguage, target, klass);
					return class_info;
				});
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
