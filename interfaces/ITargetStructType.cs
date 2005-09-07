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

		ITargetFunctionType Getter {
			get;
		}

		bool CanWrite {
			get;
		}

		ITargetFunctionType Setter {
			get;
		}
	}

	public interface ITargetEventInfo : ITargetMemberInfo
	{
		ITargetFunctionType Add {
			get;
		}

		ITargetFunctionType Remove {
			get;
		}
	}

	public interface ITargetMethodInfo : ITargetMemberInfo
	{
		new ITargetFunctionType Type {
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

		ITargetPropertyInfo[] Properties {
			get;
		}

		ITargetPropertyInfo[] StaticProperties {
			get;
		}

		ITargetObject GetStaticProperty (ITargetAccess target, int index);

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

		ITargetFunctionObject GetMethod (ITargetAccess target, int index);

		ITargetFunctionObject GetStaticMethod (ITargetAccess target, int index);

		ITargetMethodInfo[] Constructors {
			get;
		}

		ITargetMethodInfo[] StaticConstructors {
			get;
		}

		ITargetFunctionObject GetConstructor (ITargetAccess target, int index);

		ITargetFunctionObject GetStaticConstructor (ITargetAccess target, int index);
	}
}
