using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpSymbolTable : SymbolTable
	{
		MonoSymbolTableReader symtab;
		ISourceFileFactory source_factory;

		public CSharpSymbolTable (MonoSymbolTableReader symtab, ISourceFileFactory factory)
		{
			this.symtab = symtab;
			this.source_factory = factory;
		}

		protected override ArrayList GetMethods ()
		{
			ArrayList methods = new ArrayList ();

			foreach (MethodEntry method in symtab.Methods)
				methods.Add (new CSharpMethod (this, method));

			return methods;
		}

		internal ISourceFileFactory SourceFactory {
			get {
				return source_factory;
			}
		}

		internal string ImageFile {
			get {
				return symtab.ImageFile;
			}
		}
	}
}
