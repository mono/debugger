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

		int Index {
			get;
		}
	}

	public interface ITargetStructType : ITargetType
	{
		ITargetFieldInfo[] Fields {
			get;
		}

		ITargetFieldInfo[] Properties {
			get;
		}

		ITargetMethodInfo[] Methods {
			get;
		}
	}
}
