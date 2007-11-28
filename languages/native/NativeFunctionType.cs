using System;
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

		public override bool HasSourceCode {
			get { return false; }
		}

		public override SourceFile SourceFile {
			get { throw new InvalidOperationException (); }
		}

		public override int StartRow {
			get { throw new InvalidOperationException (); }
		}

		public override int EndRow {
			get { throw new InvalidOperationException (); }
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

		public override object MethodHandle {
			get {
				return null;
			}
		}

		public override TargetClassType DeclaringType {
			get {
				return null;
			}
		}

	        protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			throw new NotSupportedException ();
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
	}
}
