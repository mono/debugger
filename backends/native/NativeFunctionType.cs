using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionType : NativeType, ITargetFunctionType
	{
		NativeType return_type;
		NativeType[] parameter_types;

		public NativeFunctionType (string name, NativeType return_type, NativeType[] parameter_types)
			: base (name, TargetObjectKind.Function, 0)
		{
			this.return_type = return_type;
			this.parameter_types = parameter_types;
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public bool HasReturnValue {
			get {
				return return_type != VoidType;
			}
		}

		public NativeType ReturnType {
			get {
				return return_type;
			}
		}

		public NativeType[] ParameterTypes {
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

		SourceMethod ITargetFunctionType.Source {
			get {
				return null;
			}
		}

		object ITargetFunctionType.MethodHandle {
			get {
				return null;
			}
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			return new NativeFunctionObject (this, location);
		}

		ITargetObject ITargetFunctionType.InvokeStatic (StackFrame frame, object[] args, bool debug)
		{
			throw new NotImplementedException ();
		}
	}
}
