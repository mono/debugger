using System;

namespace Mono.Debugger.Interface
{
	public interface ITargetPointerType : ITargetType
	{
		bool IsArray {
			get;
		}

		bool IsTypesafe {
			get;
		}

		bool HasStaticType {
			get;
		}

		ITargetType StaticType {
			get;
		}
	}
}
