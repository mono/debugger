namespace Mono.Debugger
{
	public enum TargetObjectKind
	{
		Unknown,
		Fundamental,
		Array,
		Struct,
		Class,
		Pointer,
		Function,
		Opaque,
		Alias
	}
}
