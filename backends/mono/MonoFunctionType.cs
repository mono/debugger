using System;
using System.Collections;
using R = System.Reflection;
using C = Mono.CompilerServices.SymbolWriter;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoFunctionType : MonoType, IMonoTypeInfo, ITargetFunctionType
	{
		MonoClassType klass;
		R.MethodBase method_info;
		MonoType return_type;
		MonoType[] parameter_types;
		bool has_return_type;
		int token;

		public MonoFunctionType (MonoSymbolFile file, MonoClassType klass, R.MethodBase mbase)
			: base (file, TargetObjectKind.Function, mbase.ReflectedType)
		{
			this.klass = klass;
			this.method_info = mbase;
			this.token = MonoDebuggerSupport.GetMethodToken (mbase);

			Type rtype;
			if (mbase is R.ConstructorInfo) {
				rtype = mbase.DeclaringType;
				has_return_type = true;
			} else {
				rtype = ((R.MethodInfo) mbase).ReturnType;
				has_return_type = rtype != typeof (void);
			}
			return_type = file.MonoLanguage.LookupMonoType (rtype);

			R.ParameterInfo[] pinfo = mbase.GetParameters ();
			parameter_types = new MonoType [pinfo.Length];
			for (int i = 0; i < parameter_types.Length; i++)
				parameter_types [i] = file.MonoLanguage.LookupMonoType (
					pinfo [i].ParameterType);

			type_info = this;
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

		internal ITargetObject Invoke (ITargetAccess target, MonoFunctionObject method,
					       MonoObject instance, MonoObject[] args, bool debug)
		{
			if (ParameterTypes.Length != args.Length)
				throw new ArgumentException ();

			if (debug) {
				target.RuntimeInvoke (method, instance, args);
				return null;
			}

			string exc_message;
			ITargetObject retval = target.RuntimeInvoke (
				method, instance, args, out exc_message);

			if (exc_message != null)
				throw new TargetException (
					TargetError.InvocationException, exc_message);

			return retval;
		}

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
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

		public MonoFunctionObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}

		MonoObject IMonoTypeInfo.GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}
	}
}
