using System;
using System.Collections;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : MonoType, ITargetFunctionType
	{
		MonoClassType klass;
		Cecil.IMethodDefinition method_info;
		MonoType return_type;
		MonoType[] parameter_types;
		bool has_return_type;
		int index;

		public MonoFunctionType (MonoSymbolFile file, MonoClassType klass, Cecil.IMethodDefinition mdef, int index)
			: base (file, TargetObjectKind.Function, mdef.DeclaringType)
		{
			this.klass = klass;
			this.method_info = mdef;
			this.index = index;

			Type rtype;
			if (mdef is R.ConstructorInfo) {
				rtype = mdef.DeclaringType;
				has_return_type = true;
			} else {
				rtype = mdef.ReturnType.ReturnType;
				has_return_type = rtype != typeof (void);
			}
			return_type = file.LookupMonoType (rtype);

			parameter_types = new MonoType [mdef.Parameters.Count];
			for (int i = 0; i < mdef.Parameters.Count; i++)
				parameter_types [i] = file.LookupMonoType (mdef.Parameters[i].ParameterType);
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

		public int Index {
			get { return index; }
		}

		ITargetType ITargetFunctionType.ReturnType {
			get { return return_type; }
		}

		ITargetType[] ITargetFunctionType.ParameterTypes {
			get { return parameter_types; }
		}

		public SourceMethod Source {
			get {
				int token = C.MonoDebuggerSupport.GetMethodToken (method_info);
				return file.GetMethodByToken (token);
			}
		}

		object ITargetFunctionType.MethodHandle {
			get { return method_info; }
		}

		ITargetObject ITargetFunctionType.InvokeStatic (StackFrame frame,
								ITargetObject[] args,
								bool debug)
		{
			MonoFunctionTypeInfo info = GetTypeInfo () as MonoFunctionTypeInfo;
			if (info == null)
				return null;

			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return info.InvokeStatic (frame, margs, debug);
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			MonoClassInfo class_info = klass.GetTypeInfo () as MonoClassInfo;
			if (class_info == null)
				return null;

			return new MonoFunctionTypeInfo (this, class_info);
		}
	}
}
