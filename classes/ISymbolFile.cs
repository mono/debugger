namespace Mono.Debugger
{
	public interface ISymbolFile
	{
		SourceFile[] Sources {
			get;
		}

		IMethod GetMethod (long handle);
	}
}
