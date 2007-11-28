namespace Mono.Debugger.Languages
{
	public abstract class TargetClassType : TargetType
	{
		protected TargetClassType (Language language, TargetObjectKind kind)
			: base (language, kind)
		{ }

		public abstract bool HasParent {
			get;
		}

		public abstract TargetClass GetClass (Thread target);

		public abstract Module Module {
			get;
		}

		public abstract TargetClassType ParentType {
			get;
		}

		public abstract TargetFieldInfo[] Fields {
			get;
		}

		public abstract TargetFieldInfo[] StaticFields {
			get;
		}

		public abstract TargetPropertyInfo[] Properties {
			get;
		}

		public abstract TargetPropertyInfo[] StaticProperties {
			get;
		}

		public abstract TargetEventInfo[] Events {
			get;
		}

		public abstract TargetEventInfo[] StaticEvents {
			get;
		}

		public abstract TargetMethodInfo[] Methods {
			get;
		}

		public abstract TargetMethodInfo[] StaticMethods {
			get;
		}

		public abstract TargetMethodInfo[] Constructors {
			get;
		}

		public abstract TargetMethodInfo[] StaticConstructors {
			get;
		}

		public abstract  TargetMemberInfo FindMember (string name, bool search_static,
							      bool search_instance);
	}
}
