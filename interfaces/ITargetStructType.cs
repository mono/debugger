namespace Mono.Debugger.Languages
{
	public interface ITargetMemberInfo
	{
		ITargetType Type {
			get;
		}

		string Name {
			get;
		}

		int Index {
			get;
		}

		bool IsStatic {
			get;
		}
	}

	public interface ITargetFieldInfo : ITargetMemberInfo
	{
		int Offset {
			get;
		}

		bool HasConstValue {
			get;
		}

		ITargetObject GetConstValue (ITargetAccess target);
	}

	public interface ITargetPropertyInfo : ITargetMemberInfo
	{
		bool CanRead {
			get;
		}

		TargetFunctionType Getter {
			get;
		}

		bool CanWrite {
			get;
		}

		TargetFunctionType Setter {
			get;
		}
	}

	public interface ITargetEventInfo : ITargetMemberInfo
	{
		TargetFunctionType Add {
			get;
		}

		TargetFunctionType Remove {
			get;
		}

		TargetFunctionType Raise {
			get;
		}
	}

	public interface ITargetMethodInfo : ITargetMemberInfo
	{
		new TargetFunctionType Type {
			get;
		}

		string FullName {
			get;
		}
	}

	public interface ITargetStructType : ITargetType
	{
		ITargetFieldInfo[] Fields {
			get;
		}

		ITargetFieldInfo[] StaticFields {
			get;
		}

		ITargetObject GetStaticField (ITargetAccess target, int index);

		void SetStaticField (ITargetAccess target, int index, ITargetObject obj);

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

		bool ResolveClass (ITargetAccess target);
	}
}
