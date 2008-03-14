namespace Mono.Debugger.Languages
{
	public abstract class TargetClassType : TargetStructType
	{
		protected TargetClassType (Language language, TargetObjectKind kind)
			: base (language, kind)
		{ }

		public abstract bool IsCompilerGenerated {
			get;
		}

		public abstract TargetFieldInfo[] Fields {
			get;
		}

		public abstract TargetPropertyInfo[] Properties {
			get;
		}

		public abstract TargetEventInfo[] Events {
			get;
		}

		public abstract TargetMethodInfo[] Methods {
			get;
		}

		public abstract TargetMethodInfo[] Constructors {
			get;
		}

		public virtual TargetMemberInfo FindMember (string name, bool search_static,
							    bool search_instance)
		{
			foreach (TargetFieldInfo field in Fields) {
				if (field.IsStatic && !search_static)
					continue;
				if (!field.IsStatic && !search_instance)
					continue;
				if (field.Name == name)
					return field;
			}

			foreach (TargetPropertyInfo property in Properties) {
				if (property.IsStatic && !search_static)
					continue;
				if (!property.IsStatic && !search_instance)
					continue;
				if (property.Name == name)
					return property;
			}

			foreach (TargetEventInfo ev in Events) {
				if (ev.IsStatic && !search_static)
					continue;
				if (!ev.IsStatic && !search_instance)
					continue;
				if (ev.Name == name)
					return ev;
			}

			return null;
		}
	}
}
