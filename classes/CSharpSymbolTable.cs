using System;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class CSharpSymbolTable : ISymbolTable
	{
		MonoSymbolTableReader symtab;
		ISourceFileFactory source_factory;
		Hashtable method_hash;

		public CSharpSymbolTable (MonoSymbolTableReader symtab, ISourceFileFactory factory)
		{
			this.symtab = symtab;
			this.source_factory = factory;
			this.method_hash = new Hashtable ();
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

		public ISourceLocation Lookup (ITargetLocation target)
		{
			IMethod method;

			return Lookup (target, out method);
		}

		public ISourceLocation Lookup (ITargetLocation target, out IMethod imethod)
		{
			imethod = null;

			if (source_factory == null)
				return null;

			ulong address = (ulong) target.Location;

			foreach (MethodEntry method in symtab.Methods) {

				if (method.Address == null)
					continue;

				MethodAddress method_address = method.Address;

				if ((address < method_address.StartAddress) ||
				    (address >= method_address.EndAddress))
					continue;

				if (method_hash.Contains (method))
					imethod = (IMethod) method_hash [method];
				else {
					imethod = new CSharpMethod (this, method);
					method_hash.Add (method, imethod);
				}

				if (imethod.Source == null)
					return null;

				return imethod.Lookup (target);
			}

			return null;
		}

		public ITargetLocation Lookup (ISourceLocation source)
		{
			throw new NotImplementedException ();
		}
	}
}
