namespace Mono.Debugger
{
	public interface ISymbolFile
	{
		SourceFile[] Sources {
			get;
		}

		void GetMethods (SourceFile file);

		IMethod GetMethod (long handle);
	}
}
