using System;
using System.Collections;
using R = System.Reflection;
using C = Mono.CSharp.Debugger;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFunctionType : MonoType, ITargetFunctionType
	{
		R.MethodBase method_info;
		TargetAddress method;
		MonoType return_type;
		MonoType[] parameter_types;
		MonoSymbolFile file;
		bool has_return_type;

		public MonoFunctionType (MonoClass klass, R.MethodBase mbase, TargetBinaryReader info, MonoSymbolFile file)
			: base (TargetObjectKind.Function, mbase.ReflectedType, 0)
		{
			this.method_info = mbase;
			this.file = file;
			this.method = new TargetAddress (file.Table.AddressDomain, info.ReadAddress ());
			int type_info = info.ReadInt32 ();
			if (type_info != 0) {
				R.MethodInfo minfo = (R.MethodInfo) mbase;
				return_type = file.Table.GetType (minfo.ReturnType, type_info);
				has_return_type = minfo.ReturnType != typeof (void);
			} else if (mbase is R.ConstructorInfo) {
				return_type = klass;
				has_return_type = true;
			}

			int num_params = info.ReadInt32 ();
			parameter_types = new MonoType [num_params];

			R.ParameterInfo[] parameters = mbase.GetParameters ();
			if (parameters.Length != num_params) {
				throw new InternalError (
					"MethodInfo.GetParameters() returns {0} parameters " +
					"for method {1}, but the JIT has {2}",
					parameters.Length, mbase.ReflectedType.Name + "." +
					mbase.Name, num_params);
			}
			for (int i = 0; i < num_params; i++) {
				int param_info = info.ReadInt32 ();
				parameter_types [i] = file.Table.GetType (parameters [i].ParameterType, param_info);
			}
		}

		public MonoFunctionType (MonoClass klass, R.MethodInfo minfo, TargetAddress method,
					 MonoType return_type, MonoSymbolFile file)
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0)
		{
			this.method_info = minfo;
			this.method = method;
			this.return_type = return_type;
			this.file = file;

			parameter_types = new MonoType [0];
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public MonoType ReturnType {
			get {
				return return_type;
			}
		}

		public bool HasReturnValue {
			get {
				return has_return_type;
			}
		}

		public MonoType[] ParameterTypes {
			get {
				return parameter_types;
			}
		}

		ITargetType ITargetFunctionType.ReturnType {
			get {
				return return_type;
			}
		}

		ITargetType[] ITargetFunctionType.ParameterTypes {
			get {
				return parameter_types;
			}
		}

		public SourceMethod Source {
			get {
				int token = C.MonoDebuggerSupport.GetMethodToken (method_info);

				return file.GetMethodByToken (token);
			}
		}

		object ITargetFunctionType.MethodHandle {
			get {
				return method_info;
			}
		}

		protected ITargetObject Invoke (StackFrame frame, TargetAddress this_object,
						MonoObject[] args, bool debug)
		{
			TargetAddress exc_object;

			if (parameter_types.Length != args.Length)
				throw new ArgumentException ();

			TargetAddress[] arg_ptr = new TargetAddress [args.Length];
			for (int i = 0; i < args.Length; i++) {
				if (args [i].Location.HasAddress) {
					arg_ptr [i] = args [i].Location.Address;
					continue;
				}

				Heap heap = file.Table.Language.DataHeap;
				byte[] contents = args [i].RawContents;
				TargetLocation new_loc = heap.Allocate (frame, contents.Length);
				frame.TargetAccess.WriteBuffer (new_loc.Address, contents);

				arg_ptr [i] = new_loc.Address;
			}

			if (debug) {
				frame.RuntimeInvoke (method, this_object, arg_ptr);
				return null;
			}

			TargetAddress retval = frame.RuntimeInvoke (
				method, this_object, arg_ptr, out exc_object);

			if (retval.IsNull) {
				if (exc_object.IsNull)
					return null;

				TargetLocation exc_loc = new AbsoluteTargetLocation (frame, exc_object);
				MonoStringObject exc_obj = (MonoStringObject) file.Table.StringType.GetObject (exc_loc);
				string exc_message = (string) exc_obj.Object;

				throw new TargetException (
					TargetError.InvocationException, exc_message);
			}

			TargetLocation retval_loc = new AbsoluteTargetLocation (frame, retval);
			MonoObjectObject retval_obj = (MonoObjectObject) file.Table.ObjectType.GetObject (retval_loc);

			if ((retval_obj == null) || !retval_obj.HasDereferencedObject || (return_type == file.Table.ObjectType))
				return retval_obj;
			else
				return retval_obj.DereferencedObject;
		}

		internal ITargetObject Invoke (TargetLocation location, MonoObject[] args,
					       bool debug)
		{
			return Invoke (location.StackFrame, location.Address, args, debug);
		}

		public ITargetObject InvokeStatic (StackFrame frame, MonoObject[] args,
						   bool debug)
		{
			return Invoke (frame, TargetAddress.Null, args, debug);
		}

		ITargetObject ITargetFunctionType.InvokeStatic (StackFrame frame,
								ITargetObject[] args,
								bool debug)
		{
			MonoObject[] margs = new MonoObject [args.Length];
			args.CopyTo (margs, 0);
			return InvokeStatic (frame, margs, debug);
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}

		public MonoFunctionObject GetStaticObject (StackFrame frame)
		{
			return new MonoFunctionObject (this, new AbsoluteTargetLocation (frame, TargetAddress.Null));
		}
	}
}
