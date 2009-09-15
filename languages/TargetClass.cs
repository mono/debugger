using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetClass : DebuggerMarshalByRefObject
	{
		public abstract TargetClassType Type {
			get;
		}

		public abstract TargetType RealType {
			get;
		}

		public abstract bool HasParent {
			get;
		}

		public abstract TargetClass GetParent (Thread thread);

		public abstract TargetFieldInfo[] GetFields (Thread thread);

		public abstract TargetObject GetField (Thread thread,
						       TargetClassObject instance,
						       TargetFieldInfo field);

		public abstract void SetField (Thread thread, TargetClassObject instance,
					       TargetFieldInfo field, TargetObject value);

		public abstract TargetPropertyInfo[] GetProperties (Thread thread);

		public abstract TargetMethodInfo[] GetMethods (Thread thread);

		public virtual TargetMemberInfo FindMember (Thread thread, string name,
							    bool search_static, bool search_instance)
		{
			foreach (TargetFieldInfo field in GetFields (thread)) {
				if (field.IsStatic && !search_static)
					continue;
				if (!field.IsStatic && !search_instance)
					continue;
				if (field.Name == name)
					return field;
			}

			foreach (TargetPropertyInfo property in GetProperties (thread)) {
				if (property.IsStatic && !search_static)
					continue;
				if (!property.IsStatic && !search_instance)
					continue;
				if (property.Name == name)
					return property;
			}

			return null;
		}
	}
}
