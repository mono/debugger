using System;
using System.Collections;
using System.Reflection;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoFunctionType : MonoType, ITargetFunctionType
	{
		MonoStructType struct_type;
		MethodInfo method_info;
		TargetAddress method;
		MonoType return_type;
		MonoType[] parameter_types;

		public MonoFunctionType (MonoStructType struct_type, MethodInfo minfo,
					 TargetBinaryReader info, MonoSymbolTable table)
			: base (TargetObjectKind.Function, minfo.ReflectedType, 0)
		{
			this.struct_type = struct_type;
			this.method_info = minfo;
			this.method = new TargetAddress (table.AddressDomain, info.ReadAddress ());
			int type_info = info.ReadInt32 ();
			if (type_info != 0)
				return_type = struct_type.GetType (minfo.ReturnType, type_info, table);
				
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
				parameter_types [i] = struct_type.GetType (
					parameters [i].ParameterType, param_info, table);
			}
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

		internal ITargetObject Invoke (TargetLocation location, ITargetObject[] args)
		{
			if (args.Length != 0)
				throw new NotSupportedException ();

			return struct_type.Invoke (method, location);
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			throw new NotImplementedException ();
		}
	}
}
