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
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0, TargetAddress.Null)
		{
			this.klass = klass;
			this.method_info = minfo;
			this.table = table;
			this.method = new TargetAddress (table.AddressDomain, info.ReadAddress ());
			int type_info = info.ReadInt32 ();
			if (type_info != 0)
				return_type = GetType (minfo.ReturnType, type_info, table);

			int num_params = info.ReadInt32 ();
			parameter_types = new MonoType [num_params];

			ParameterInfo[] parameters = minfo.GetParameters ();
			if (parameters.Length != num_params)
				throw new InternalError (
					"MethodInfo.GetParameters() returns {0} parameters " +
					"for method {1}, but the JIT has {2}",
					parameters.Length, minfo.ReflectedType.Name + "." +
					minfo.Name, num_params);
			for (int i = 0; i < num_params; i++) {
				int param_info = info.ReadInt32 ();
				parameter_types [i] = GetType (parameters [i].ParameterType, param_info, table);
			}

			invoke_method = table.Language.MonoDebuggerInfo.RuntimeInvoke;
		}

		public MonoFunctionType (MonoClass klass, MethodInfo minfo, TargetAddress method,
					 MonoType return_type, MonoSymbolTable table)
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0, TargetAddress.Null)
		{
			this.klass = klass;
			this.method_info = minfo;
			this.method = method;
			this.return_type = return_type;

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

		internal ITargetObject Invoke (TargetLocation location, object[] args)
		{
			TargetAddress exc_object;
			TargetAddress this_object = location.Address;

			if (parameter_types.Length != args.Length)
				throw new MethodOverloadException (
					"Method takes {0} arguments, but specified {1}.",
					parameter_types.Length, args.Length);

			TargetAddress[] arg_ptr = new TargetAddress [args.Length];
			for (int i = 0; i < args.Length; i++) {
				MonoObject obj;
				try {
					obj = MarshalArgument (location.StackFrame, i, args [i]);
				} catch (ArgumentException) {
					throw new MethodOverloadException ("Cannot marshal argument {0}: invalid argument.", i+1);
				} catch {
					obj = null;
				}
				if (obj == null)
					throw new MethodOverloadException ("Cannot marshal argument {0}.", i+1);
				arg_ptr [i] = obj.Location.Address;
			}

			TargetAddress retval = location.TargetAccess.CallInvokeMethod (
				invoke_method, method, this_object, arg_ptr, out exc_object);

			if (retval.IsNull)
				return null;

			TargetLocation retval_loc = new RelativeTargetLocation (location, retval);

			MonoObjectObject retval_obj = new MonoObjectObject (ObjectType, retval_loc);
			if ((retval_obj == null) || !retval_obj.HasDereferencedObject || (return_type == ObjectType))
				return retval_obj;

			return retval_obj.DereferencedObject;
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoFunctionObject (this, location);
		}
	}
}
