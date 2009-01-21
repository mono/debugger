using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetNullableType : TargetType
	{
		TargetType element_type;

		public TargetNullableType (TargetType element_type)
			: base (element_type.Language, TargetObjectKind.Nullable)
		{
			this.element_type = element_type;
		}

		public override string Name {
			get { return element_type.Name + "?"; }
		}

		public override int Size {
			get { return 0; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public TargetType ElementType {
			get { return element_type; }
		}

		public override bool ContainsGenericParameters {
			get { return element_type.ContainsGenericParameters; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}
	}
}
