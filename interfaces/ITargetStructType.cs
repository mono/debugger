namespace Mono.Debugger.Languages
{
	public interface ITargetStructType : ITargetType
	{
		TargetFieldInfo[] Fields {
			get;
		}

		TargetFieldInfo[] StaticFields {
			get;
		}

		ITargetObject GetStaticField (ITargetAccess target, int index);

		void SetStaticField (ITargetAccess target, int index, ITargetObject obj);

		TargetPropertyInfo[] Properties {
			get;
		}

		TargetPropertyInfo[] StaticProperties {
			get;
		}

		TargetEventInfo[] Events {
			get;
		}

		TargetEventInfo[] StaticEvents {
			get;
		}

		TargetMethodInfo[] Methods {
			get;
		}

		TargetMethodInfo[] StaticMethods {
			get;
		}

		TargetMethodInfo[] Constructors {
			get;
		}

		TargetMethodInfo[] StaticConstructors {
			get;
		}

		TargetMemberInfo FindMember (string name, bool search_static,
					     bool search_instance);

		bool ResolveClass (ITargetAccess target);
	}
}
