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

		// <summary>
		//   The current programming language's native representation of this member.
		//   This is a System.Reflection.MemberInfo for managed data types.
		// </summary>
		object Handle {
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

		ITargetObject GetConstValue (StackFrame frame);
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

		ITargetObject GetStaticField (StackFrame frame, int index);

		ITargetPropertyInfo[] Properties {
			get;
		}

		ITargetPropertyInfo[] StaticProperties {
			get;
		}

		ITargetObject GetStaticProperty (StackFrame frame, int index);

		ITargetEventInfo[] Events {
			get;
		}

		ITargetEventInfo[] StaticEvents {
			get;
		}

		ITargetObject GetStaticEvent (StackFrame frame, int index);

		ITargetMethodInfo[] Methods {
			get;
		}

		ITargetMethodInfo[] StaticMethods {
			get;
		}

		ITargetFunctionObject GetStaticMethod (StackFrame frame, int index);


		ITargetMethodInfo[] Constructors {
			get;
		}

		ITargetFunctionObject GetConstructor (StackFrame frame, int index);

		ITargetMethodInfo[] StaticConstructors {
			get;
		}

		ITargetFunctionObject GetStaticConstructor (StackFrame frame, int index);
	}
}
