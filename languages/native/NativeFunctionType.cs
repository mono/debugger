using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionType : TargetType, ITargetFunctionType
	{
		string name;
		TargetType return_type;
		TargetType[] parameter_types;

		public NativeFunctionType (ILanguage language, string name,
					   TargetType return_type, TargetType[] parameter_types)
			: base (language, TargetObjectKind.Function)
		{
			this.name = name;
			this.return_type = return_type;
			this.parameter_types = parameter_types;
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return Language.TargetInfo.TargetAddressSize; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public bool HasReturnValue {
			get {
				return return_type != Language.VoidType;
			}
		}

		public TargetType ReturnType {
			get {
				return return_type;
			}
		}

		public TargetType[] ParameterTypes {
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

		public ITargetObject Invoke (ITargetAccess target, ITargetObject instance,
					     ITargetObject[] args)
		{
			throw new NotSupportedException ();
		}

		ITargetStructType ITargetFunctionType.DeclaringType {
			get {
				return null;
			}
		}

	        internal override TargetObject GetObject (TargetLocation location)
		{
			throw new NotSupportedException ();
		}
	}
}
