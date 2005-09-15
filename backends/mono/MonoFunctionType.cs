using System;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : MonoType, IMonoTypeInfo, ITargetFunctionType
	{
		MonoClassType klass;
		Cecil.IMethodDefinition method_info;
		MonoType return_type;
		MonoType[] parameter_types;
		bool has_return_type;
		string full_name;
		int token;

		public MonoFunctionType (MonoSymbolFile file, MonoClassType klass,
					 Cecil.IMethodDefinition mdef, string full_name)
			: base (file, TargetObjectKind.Function)
		{
			this.klass = klass;
			this.method_info = mdef;
			this.token = MonoDebuggerSupport.GetMethodToken (mdef);
			this.full_name = full_name;

			Cecil.ITypeReference rtype;
			if (mdef.IsConstructor) {
				rtype = mdef.DeclaringType;
				has_return_type = true;
			} else {
				rtype = mdef.ReturnType.ReturnType;
				has_return_type = rtype.FullName != "System.Void";
			}
			return_type = file.MonoLanguage.LookupMonoType (rtype);

			parameter_types = new MonoType [mdef.Parameters.Count];
			for (int i = 0; i < mdef.Parameters.Count; i++)
				parameter_types [i] = file.MonoLanguage.LookupMonoType (
					mdef.Parameters[i].ParameterType);

			type_info = this;
		}

		public override string Name {
			get { return full_name; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public MonoType ReturnType {
			get { return return_type; }
		}

		public bool HasReturnValue {
			get { return has_return_type; }
		}

		public MonoType[] ParameterTypes {
			get { return parameter_types; }
		}

		public int Token {
			get { return token; }
		}

		ITargetType ITargetFunctionType.ReturnType {
			get { return return_type; }
		}

		ITargetType[] ITargetFunctionType.ParameterTypes {
			get { return parameter_types; }
		}

		public SourceMethod Source {
			get {
				int token = MonoDebuggerSupport.GetMethodToken (method_info);
				return file.GetMethodByToken (token);
			}
		}

		object ITargetFunctionType.MethodHandle {
			get { return method_info; }
		}

		protected override IMonoTypeInfo DoGetTypeInfo ()
		{
			throw new InvalidOperationException ();
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return File.TargetInfo.TargetAddressSize; }
		}

		MonoType IMonoTypeInfo.Type {
			get { return this; }
		}

		public TargetAddress GetMethodAddress (ITargetAccess target)
		{
			try {
				MonoClassInfo info = klass.GetTypeInfo () as MonoClassInfo;
				if (info == null)
					throw new LocationInvalidException ();

				return info.GetMethodAddress (target, Token);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		MonoObject IMonoTypeInfo.GetObject (TargetLocation location)
		{
			throw new InvalidOperationException ();
		}
	}
}
