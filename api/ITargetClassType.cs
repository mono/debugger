namespace Mono.Debugger.Interface
{
	public interface ITargetClassType : ITargetType
	{
		bool HasParent {
			get;
		}

		ITargetClassType ParentType {
			get;
		}

		ITargetFieldInfo[] Fields {
			get;
		}

		ITargetFieldInfo[] StaticFields {
			get;
		}

		ITargetObject GetStaticField (IThread target, ITargetFieldInfo field);

		void SetStaticField (IThread target, ITargetFieldInfo field,
				     ITargetObject obj);

		ITargetPropertyInfo[] Properties {
			get;
		}

		ITargetPropertyInfo[] StaticProperties {
			get;
		}

		ITargetEventInfo[] Events {
			get;
		}

		ITargetEventInfo[] StaticEvents {
			get;
		}

		ITargetMethodInfo[] Methods {
			get;
		}

		ITargetMethodInfo[] StaticMethods {
			get;
		}

		ITargetMethodInfo[] Constructors {
			get;
		}

		ITargetMethodInfo[] StaticConstructors {
			get;
		}

		ITargetMemberInfo FindMember (string name, bool search_static,
					      bool search_instance);
	}
}
