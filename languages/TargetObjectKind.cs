namespace Mono.Debugger.Languages
{
	public enum TargetObjectKind
	{
		Unknown,
		Null,
		Fundamental,
		Enum,
		Array,
		Struct,
		Class,
		Pointer,
		Object,
		Function,
		Alias,
		GenericParameter,
		GenericInstance
	}
}
