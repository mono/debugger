using System;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : TargetFunctionType
	{
		MonoClassType klass;
		Cecil.IMethodDefinition method_info;
		TargetType return_type;
		TargetType[] parameter_types;
		bool has_return_type;
		string full_name;
		int token;

		public MonoFunctionType (MonoSymbolFile file, MonoClassType klass,
					 Cecil.IMethodDefinition mdef, string full_name)
			: base (file.MonoLanguage)
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

			parameter_types = new TargetType [mdef.Parameters.Count];
			for (int i = 0; i < mdef.Parameters.Count; i++)
				parameter_types [i] = file.MonoLanguage.LookupMonoType (
					mdef.Parameters[i].ParameterType);
		}

		public override string Name {
			get { return full_name; }
		}

		public override bool IsByRef {
			get { return true; }
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

		public override ITargetStructType DeclaringType {
			get { return klass; }
		}

		public override SourceMethod Source {
			get {
				int token = MonoDebuggerSupport.GetMethodToken (method_info);
				return klass.File.GetMethodByToken (token);
			}
		}

		public override object MethodHandle {
			get { return method_info; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return klass.File.TargetInfo.TargetAddressSize; }
		}

		public TargetAddress GetMethodAddress (ITargetAccess target)
		{
			try {
				MonoClassInfo info = klass.GetTypeInfo ();
				if (info == null)
					throw new LocationInvalidException ();

				return info.GetMethodAddress (target, Token);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			throw new InvalidOperationException ();
		}
	}
}