using System;

namespace Mono.Debugger
{
	public interface ITargetFieldInfo
	{
		// <summary>
		//   Type of this field.
		// </summary>
		ITargetType Type {
			get;
		}

		// <summary>
		//   Name of this field.
		// </summary>
		string Name {
			get;
		}

		int Index {
			get;
		}

		// <summary>
		//   The current programming language's native representation of
		//   a field.  This is a System.Reflection.FieldInfo or a
		//   System.Reflection.PropertyInfo for managed data types.
		// </summary>
		object FieldHandle {
			get;
		}
	}

	public interface ITargetMethodInfo
	{
		ITargetFunctionType Type {
			get;
		}

		string Name {
			get;
		}

		string FullName {
			get;
		}

		int Index {
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

		ITargetFieldInfo[] Properties {
			get;
		}

		ITargetFieldInfo[] StaticProperties {
			get;
		}

		ITargetObject GetStaticProperty (StackFrame frame, int index);

		ITargetMethodInfo[] Methods {
			get;
		}

		ITargetMethodInfo[] StaticMethods {
			get;
		}

		ITargetFunctionType GetStaticMethod (int index);
	}
}
