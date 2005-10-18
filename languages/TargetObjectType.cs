using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetObjectType : TargetPointerType
	{
		public TargetObjectType (Language language, string name, int size)
			: base (language, TargetObjectKind.Object, name, size)
		{ }

		public abstract TargetClassType ClassType {
			get;
		}

		public override bool IsTypesafe {
			get { return true; }
		}

		public override bool HasStaticType {
			get { return false; }
		}

		public override bool IsArray {
			get { return false; }
		}

		public override TargetType StaticType {
			get {
				throw new InvalidOperationException ();
			}
		}
	}
}
