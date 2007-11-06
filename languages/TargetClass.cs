namespace Mono.Debugger.Languages
{
	public abstract class TargetClass : DebuggerMarshalByRefObject
	{
		public abstract TargetClassType Type {
			get;
		}

		public abstract bool HasParent {
			get;
		}

		public abstract TargetClass GetParent (TargetMemoryAccess memory);

		public abstract TargetObject GetField (TargetMemoryAccess target,
						       TargetStructObject instance,
						       TargetFieldInfo field);

		public abstract TargetObject GetStaticField (Thread target,
							     TargetFieldInfo field);

		public abstract void SetField (TargetAccess target, TargetStructObject instance,
					       TargetFieldInfo field, TargetObject value);

		public abstract void SetStaticField (Thread target, TargetFieldInfo field,
						     TargetObject obj);

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

#if FIXME
		public abstract TargetMemberInfo FindMember (string name, bool search_static,
							     bool search_instance);
#endif
	}
}
