using System;

namespace Mono.Debugger.Interface
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
		bool HasConstValue {
			get;
		}

		object ConstValue {
			get;
		}
	}

	public interface ITargetPropertyInfo : ITargetMemberInfo
	{
		bool CanRead {
			get;
		}

		bool CanWrite {
			get;
		}

		ITargetFunctionType Getter {
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

		ITargetFunctionType Raise {
			get;
		}
	}

	public interface ITargetMethodInfo : ITargetMemberInfo
	{
		string FullName {
			get;
		}

		new ITargetFunctionType Type {
			get;
		}
	}
}
