using System;

namespace Mono.Debugger
{
	// <summary>
	//   A fundamental type is a type which can be represented with a Mono type,
	//   such as bool, int, string, etc.
	// </summary>
	public interface ITargetFundamentalType : ITargetType
	{
		Type Type {
			get;
		}
	}
}
