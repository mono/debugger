namespace Mono.Debugger
{
	public interface ISymbolFile
	{
		SourceFile[] Sources {
			get;
		}

		void GetMethods (SourceFile file);

		Method GetMethod (long handle);

		SourceMethod FindMethod (string name);
	}
}
