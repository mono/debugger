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

	public interface ITargetMethodInfo : ITargetMemberInfo
	{
		ITargetFunctionType Type {
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

		ITargetMethodInfo[] Methods {
			get;
		}

		ITargetMethodInfo[] StaticMethods {
			get;
		}

		ITargetMethodInfo[] Constructors {
			get;
		}

		ITargetFunctionObject GetStaticMethod (StackFrame frame, int index);

		ITargetFunctionObject GetConstructor (StackFrame frame, int index);
	}
}
