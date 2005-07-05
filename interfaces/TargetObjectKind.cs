namespace Mono.Debugger
{
	public enum TargetObjectKind
	{
		Unknown,
		Fundamental,
		Enum,
		Array,
		Struct,
		Class,
		Pointer,
		Function,
		Opaque,
		Alias
	}
}
