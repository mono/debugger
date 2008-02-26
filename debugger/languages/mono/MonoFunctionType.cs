using System;
using System.Collections;
using System.Runtime.Serialization;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : TargetFunctionType
	{
		IMonoStructType klass;
		SourceFile file;
		int start_row, end_row;
		Cecil.MethodDefinition method_info;
		TargetType return_type;
		TargetType[] parameter_types;
		bool has_return_type;
		string name;
		int token;

		int load_handler;

		internal MonoFunctionType (IMonoStructType klass, Cecil.MethodDefinition mdef)
			: base (klass.File.MonoLanguage)
		{
			this.klass = klass;
			this.method_info = mdef;
			this.token = MonoDebuggerSupport.GetMethodToken (mdef);
			this.name = GetMethodName (mdef) + MonoSymbolFile.GetMethodSignature (mdef);

			Cecil.TypeReference rtype;
			if (mdef.IsConstructor) {
				rtype = mdef.DeclaringType;
				has_return_type = true;
			} else {
				rtype = mdef.ReturnType.ReturnType;
				has_return_type = rtype.FullName != "System.Void";
			}
			return_type = klass.File.MonoLanguage.LookupMonoType (rtype);

			parameter_types = new TargetType [mdef.Parameters.Count];
			for (int i = 0; i < mdef.Parameters.Count; i++)
				parameter_types [i] = klass.File.MonoLanguage.LookupMonoType (
					mdef.Parameters[i].ParameterType);

			file = klass.File.GetSourceFile (token, out start_row, out end_row);
		}

		internal static string GetMethodName (Cecil.MethodDefinition mdef)
		{
			Cecil.GenericParameterCollection gen_params = mdef.GenericParameters;
			if ((gen_params == null) || (gen_params.Count == 0))
				return mdef.Name;
			else
				return mdef.Name + "`" + gen_params.Count;
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

		public override string FullName {
			get { return klass.Type.Name + '.' + name; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool IsStatic {
			get { return method_info.IsStatic; }
		}

		public override bool IsConstructor {
			get { return method_info.IsConstructor; }
		}

		public override TargetType ReturnType {
			get { return return_type; }
		}

		public override bool HasReturnValue {
			get { return has_return_type; }
		}

		public override TargetType[] ParameterTypes {
			get { return parameter_types; }
		}

		public int Token {
			get { return token; }
		}

		public override TargetStructType DeclaringType {
			get { return klass.Type; }
		}

		internal MonoSymbolFile SymbolFile {
			get { return klass.File; }
		}

		internal Cecil.MethodDefinition MethodInfo {
			get { return method_info; }
		}

		public override object MethodHandle {
			get { return method_info; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return klass.File.TargetMemoryInfo.TargetAddressSize; }
		}

		public override bool HasSourceCode {
			get { return file != null; }
		}

		public override SourceFile SourceFile {
			get {
				if (!HasSourceCode)
					throw new InvalidOperationException ();

				return file;
			}
		}

		public override int StartRow {
			get {
				if (!HasSourceCode)
					throw new InvalidOperationException ();

				return start_row;
			}
		}

		public override int EndRow {
			get {
				if (!HasSourceCode)
					throw new InvalidOperationException ();

				return end_row;
			}
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			throw new InvalidOperationException ();
		}

		public override bool IsManaged {
			get { return true; }
		}

		internal override bool InsertBreakpoint (Thread target,
							 FunctionBreakpointHandle handle)
		{
			load_handler = klass.File.MonoLanguage.RegisterMethodLoadHandler (
				target, this, handle);
			return load_handler > 0;
		}

		internal override void RemoveBreakpoint (Thread target)
		{
			if (load_handler > 0) {
				klass.File.MonoLanguage.RemoveMethodLoadHandler (target, load_handler);
				load_handler = -1;
			}
		}

		internal MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			return klass.ResolveClass (target, fail);
		}
	}
}
