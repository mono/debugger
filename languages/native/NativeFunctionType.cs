using System;
using System.Text;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionType : TargetFunctionType
	{
		string name;
		TargetType return_type;
		TargetType[] parameter_types;

		public NativeFunctionType (Language language, string name,
					   TargetType return_type, TargetType[] parameter_types)
			: base (language)
		{
			this.name = name;
			this.return_type = return_type;
			this.parameter_types = parameter_types;
		}

		internal NativeFunctionType (Language language)
			: base (language)
		{ }

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override string Name {
			get { return name; }
		}

		public override string FullName {
			get { return name; }
		}

		public override MethodSource GetSourceCode ()
		{
			return null;
		}

		public override int Size {
			get { return Language.TargetInfo.TargetAddressSize; }
		}

		public override bool IsStatic {
			get { return true; }
		}

		public override bool IsConstructor {
			get { return false; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		public override bool HasReturnValue {
			get {
				return return_type != Language.VoidType;
			}
		}

		public override TargetType ReturnType {
			get {
				return return_type;
			}
		}

		public override TargetType[] ParameterTypes {
			get {
				return parameter_types;
			}
		}

		internal void SetPrototype (TargetType ret_type, TargetType[] param_types)
		{
			this.return_type = ret_type;
			this.parameter_types = param_types;

			StringBuilder sb = new StringBuilder ();
			sb.Append (ret_type.Name);
			sb.Append (" (*) (");

			for (int i = 0; i < param_types.Length; i++) {
				if (i > 0)
					sb.Append (",");
				sb.Append (param_types [i].Name);
			}

			sb.Append (")");
			name = sb.ToString ();
		}

		internal string GetPointerName ()
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (return_type.Name);
			sb.Append (" (**) (");

			for (int i = 0; i < parameter_types.Length; i++) {
				if (i > 0)
					sb.Append (",");
				sb.Append (parameter_types [i].Name);
			}

			sb.Append (")");
			return sb.ToString ();
		}

		public override object MethodHandle {
			get {
				return null;
			}
		}

		public override TargetStructType DeclaringType {
			get {
				return null;
			}
		}

	        protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			throw new InvalidOperationException ();
		}

		public override bool IsManaged {
			get { return false; }
		}

		internal override bool InsertBreakpoint (Thread target,
							 FunctionBreakpointHandle handle)
		{
			throw new InvalidOperationException ();
		}

		internal override void RemoveBreakpoint (Thread target)
		{
			throw new InvalidOperationException ();
		}

		public override TargetMethodSignature GetSignature (Thread target)
		{
			throw new InvalidOperationException ();
		}
	}
}
