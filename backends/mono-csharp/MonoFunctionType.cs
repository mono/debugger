using System;
using System.Collections;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFunctionType : MonoType, ITargetFunctionType
	{
		new MonoClass klass;
		MethodInfo method_info;
		TargetAddress method;
		MonoType return_type;
		MonoType[] parameter_types;
		TargetAddress invoke_method;
		MonoSymbolTable table;

		public MonoFunctionType (MonoClass klass, MethodInfo minfo, TargetBinaryReader info, MonoSymbolTable table)
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0)
		{
			this.klass = klass;
			this.method_info = minfo;
			this.table = table;
			this.method = new TargetAddress (table.AddressDomain, info.ReadAddress ());
			int type_info = info.ReadInt32 ();
			if (type_info != 0)
				return_type = table.GetType (minfo.ReturnType, type_info);

			int num_params = info.ReadInt32 ();
			parameter_types = new MonoType [num_params];

			ParameterInfo[] parameters = minfo.GetParameters ();
			if (parameters.Length != num_params) {
				throw new InternalError (
					"MethodInfo.GetParameters() returns {0} parameters " +
					"for method {1}, but the JIT has {2}",
					parameters.Length, minfo.ReflectedType.Name + "." +
					minfo.Name, num_params);
			}
			for (int i = 0; i < num_params; i++) {
				int param_info = info.ReadInt32 ();
				parameter_types [i] = table.GetType (parameters [i].ParameterType, param_info);
			}

			invoke_method = table.Language.MonoDebuggerInfo.RuntimeInvoke;
		}

		public MonoFunctionType (MonoClass klass, MethodInfo minfo, TargetAddress method,
					 MonoType return_type, MonoSymbolTable table)
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0)
		{
			this.klass = klass;
			this.method_info = minfo;
			this.method = method;
			this.return_type = return_type;
			this.table = table;

			parameter_types = new MonoType [0];

			invoke_method = table.Language.MonoDebuggerInfo.RuntimeInvoke;
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
				return method_info.ReturnType != typeof (void);
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

		object ITargetFunctionType.MethodHandle {
			get {
				return method_info;
			}
		}

		MonoObject MarshalArgument (StackFrame frame, int index, object arg)
		{
			MonoType type = parameter_types [index];

			if (arg is ITargetFundamentalObject)
				arg = ((ITargetFundamentalObject) arg).Object;

			if (type.Kind == TargetObjectKind.Fundamental) {
				MonoFundamentalType ftype = (MonoFundamentalType) type;

				return ftype.CreateInstance (frame, arg);
			}

			return null;
		}

		protected ITargetObject Invoke (StackFrame frame, TargetAddress this_object, object[] args)
		{
			TargetAddress exc_object;

			if (parameter_types.Length != args.Length)
				throw new MethodOverloadException (
					"Method takes {0} arguments, but specified {1}.",
					parameter_types.Length, args.Length);

			TargetAddress[] arg_ptr = new TargetAddress [args.Length];
			for (int i = 0; i < args.Length; i++) {
				MonoObject obj;
				try {
					obj = MarshalArgument (frame, i, args [i]);
				} catch (ArgumentException) {
					throw new MethodOverloadException ("Cannot marshal argument {0}: invalid argument.", i+1);
				} catch {
					obj = null;
				}
				if (obj == null)
					throw new MethodOverloadException ("Cannot marshal argument {0}.", i+1);
				arg_ptr [i] = obj.Location.Address;
			}

			TargetAddress retval = frame.TargetAccess.CallInvokeMethod (
				invoke_method, method, this_object, arg_ptr, out exc_object);

			if (retval.IsNull) {
				if (exc_object.IsNull)
					return null;

				TargetLocation exc_loc = new AbsoluteTargetLocation (frame, exc_object);
				MonoStringObject exc_obj = (MonoStringObject) table.StringType.GetObject (exc_loc);
				string exc_message = (string) exc_obj.Object;

				throw new TargetInvocationException (exc_message);
			}

			TargetLocation retval_loc = new AbsoluteTargetLocation (frame, retval);
			MonoObjectObject retval_obj = (MonoObjectObject) table.ObjectType.GetObject (retval_loc);

			if ((retval_obj == null) || !retval_obj.HasDereferencedObject || (return_type == table.ObjectType))
				return retval_obj;
			else
				return retval_obj.DereferencedObject;
		}

		internal ITargetObject Invoke (TargetLocation location, object[] args)
		{
			return Invoke (location.StackFrame, location.Address, args);
		}

		public ITargetObject InvokeStatic (StackFrame frame, object[] args)
		{
			return Invoke (frame, TargetAddress.Null, args);
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}
	}
}
