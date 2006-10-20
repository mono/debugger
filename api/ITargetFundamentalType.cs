using System;

namespace Mono.Debugger.Interface
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

	public interface ITargetFundamentalType : ITargetType
	{
		FundamentalKind FundamentalKind {
			get;
		}
	}
}
