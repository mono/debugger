using System;
using System.Collections;
using System.Runtime.Serialization;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : TargetFunctionType
	{
		IMonoStructType klass;
		Cecil.MethodDefinition method_info;
		TargetType return_type;
		TargetType[] parameter_types;
		bool has_return_type;
		string name;
		int token;

		MonoMethodSignature signature;

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
				rtype = mdef.ReturnType;
				has_return_type = rtype.FullName != "System.Void";
			}
			return_type = klass.File.MonoLanguage.LookupMonoType (rtype);

			parameter_types = new TargetType [mdef.Parameters.Count];
			for (int i = 0; i < mdef.Parameters.Count; i++)
				parameter_types [i] = klass.File.MonoLanguage.LookupMonoType (
					mdef.Parameters[i].ParameterType);
		}

		public override MethodSource GetSourceCode ()
		{
			return klass.File.GetMethodByToken (token);
		}

		internal static string GetMethodName (Cecil.MethodDefinition mdef)
		{
			var gen_params = mdef.GenericParameters;
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

		public override TargetClassType DeclaringType {
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

		public override bool ContainsGenericParameters {
			get {
				if (return_type.ContainsGenericParameters)
					return true;

				foreach (TargetType type in parameter_types)
					if (type.ContainsGenericParameters)
						return true;

				return false;
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

		internal override FunctionBreakpointHandle GetBreakpointHandle (
			Breakpoint breakpoint, int line, int column)
		{
			return new MyBreakpointHandle (breakpoint, this, line, column);
		}

		internal MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			return klass.ResolveClass (target, fail);
		}

		public override TargetMethodSignature GetSignature (Thread thread)
		{
			if (signature != null)
				return signature;

			if (!ContainsGenericParameters)
				return new MonoMethodSignature (return_type, parameter_types);

			if (!thread.CurrentFrame.Language.IsManaged)
				throw new TargetException (TargetError.InvalidContext);

			TargetAddress addr = (TargetAddress) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					MonoClassInfo class_info = ResolveClass (target, true);
					return class_info.GetMethodAddress (target, token);
			});

			MonoLanguageBackend mono = klass.File.MonoLanguage;

			TargetAddress sig = thread.CallMethod (
				mono.MonoDebuggerInfo.GetMethodSignature, addr, 0);

			signature = (MonoMethodSignature) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					return mono.MetadataHelper.GetMethodSignature (mono, target, sig);
			});

			return signature;
		}

		protected class MyBreakpointHandle : FunctionBreakpointHandle
		{
			MonoFunctionType function;
			int load_handler = -1;

			public MyBreakpointHandle (Breakpoint bpt, MonoFunctionType function,
						   int line, int column)
				: base (bpt, function, line, column)
			{
				this.function = function;
			}

			public override void Insert (Thread thread)
			{
				if (load_handler > 0)
					return;

				load_handler = function.SymbolFile.MonoLanguage.RegisterMethodLoadHandler (
					thread, function, this);
			}

			internal override void MethodLoaded (TargetAccess target, Method method)
			{
				TargetAddress address;
				if (Line != -1) {
					if (method.HasLineNumbers)
						address = method.LineNumberTable.Lookup (Line, Column);
					else
						address = TargetAddress.Null;
				} else if (method.HasMethodBounds)
					address = method.MethodStartAddress;
				else
					address = method.StartAddress;

				if (address.IsNull)
					return;

				try {
					target.InsertBreakpoint (this, address, method.Domain);
				} catch (TargetException ex) {
					Report.Error ("Can't insert breakpoint {0} at {1}: {2}",
						      Breakpoint.Index, address, ex.Message);
				}
			}

			public override void Remove (Thread thread)
			{
				thread.RemoveBreakpoint (this);

				if (load_handler > 0) {
					function.SymbolFile.MonoLanguage.RemoveMethodLoadHandler (
						thread, load_handler);
					load_handler = -1;
				}
			}
		}
	}
}
