namespace Mono.Debugger
{
	public interface ISymbolFile
	{
		SourceFile[] Sources {
			get;
		}
	}
}
