using System;
using System.Collections;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : MonoType, ITargetFunctionType
	{
		MonoClass klass;
		R.MethodBase method_info;
		MonoType return_type;
		MonoType[] parameter_types;
		bool has_return_type;
		int index;

		public MonoFunctionType (MonoSymbolFile file, MonoClass klass, R.MethodBase mbase, int index)
			: base (file, TargetObjectKind.Function, mbase.ReflectedType)
		{
			this.klass = klass;
			this.method_info = mbase;
			this.klass = klass;
			this.index = index;

			Type rtype;
			if (mbase is R.ConstructorInfo) {
				rtype = mbase.DeclaringType;
				has_return_type = true;
			} else {
				rtype = ((R.MethodInfo) mbase).ReturnType;
				has_return_type = rtype != typeof (void);
			}
			return_type = file.LookupMonoType (rtype);

			R.ParameterInfo[] pinfo = mbase.GetParameters ();
			parameter_types = new MonoType [pinfo.Length];
			for (int i = 0; i < parameter_types.Length; i++)
				parameter_types [i] = file.LookupMonoType (pinfo [i].ParameterType);
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
			MonoFunctionTypeInfo info = (MonoFunctionTypeInfo) Resolve ();
			if (info == null)
				return null;

			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return info.InvokeStatic (frame, margs, debug);
		}

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			MonoClassInfo class_info = (MonoClassInfo) klass.Resolve ();
			if (class_info == null)
				return null;

			return new MonoFunctionTypeInfo (this, class_info);
		}
	}
}
