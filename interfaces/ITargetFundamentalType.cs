using System;

namespace Mono.Debugger.Languages
{
	public enum FundamentalKind
	{
		Unknown,
		Object,
		Boolean,
		Char,
		SByte,
		Byte,
		Int16,
		UInt16,
		Int32,
		UInt32,
		Int64,
		UInt64,
		Single,
		Double,
		String,
		IntPtr,
		UIntPtr
	}

	// <summary>
	//   A fundamental type is a type which can be represented with a Mono type,
	//   such as bool, int, string, etc.
	// </summary>
	public interface ITargetFundamentalType : ITargetType
	{
		FundamentalKind FundamentalKind {
			get;
		}
	}
}
