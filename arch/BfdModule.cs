using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal sealed class BfdModule : NativeModule, ISymbolContainer, IDisposable
	{
		Bfd bfd;
		bool dwarf_loaded;
		DwarfReader dwarf;
		Module module;
		bool is_library;
		TargetAddress start, end;
		DebuggerBackend backend;

		public BfdModule (DebuggerBackend backend, Module module, Bfd bfd)
			: base (backend, module, bfd.FileName)
		{
			this.backend = backend;
			this.module = module;
			this.bfd = bfd;

			if (bfd.IsContinuous) {
				start = bfd.StartAddress;
				end = bfd.EndAddress;
				is_library = true;
			}

			module.ModuleData = this;

			module.ModuleChangedEvent += new ModuleEventHandler (module_changed);
			module_changed (module);
		}

		public override object Language {
			get { return null; }
		}

		public override bool SymbolsLoaded {
			get { return dwarf != null; }
		}

		public override SourceInfo[] Sources {
			get {
				if (dwarf == null)
					return new SourceInfo [0];

				return dwarf.GetSources ();
			}
		}

		public override ISymbolTable SymbolTable {
			get {
				if (dwarf != null)
					return dwarf.SymbolTable;
				else
					return bfd.SymbolTable;
			}
		}

		protected override void ReadModuleData ()
		{ }

		public override TargetAddress SimpleLookup (string name)
		{
			return bfd [name];
		}

		void load_dwarf ()
		{
			if (dwarf_loaded)
				return;

			try {
				dwarf = new DwarfReader (bfd, module, bfd.SymbolTable);
			} catch (Exception e) {
				// Silently ignore.
			}

			dwarf_loaded = true;

			if (dwarf != null)
				OnSymbolsLoadedEvent ();
		}

		void unload_dwarf ()
		{
			if (!dwarf_loaded)
				return;

			dwarf_loaded = false;
			if (dwarf != null) {
				dwarf = null;
				OnSymbolsUnLoadedEvent ();
			}
		}

		void module_changed (Module module)
		{
			if (module.LoadSymbols)
				load_dwarf ();
			else
				unload_dwarf ();
		}

		//
		// ISymbolContainer
		//

		public bool IsContinuous {
			get {
				return is_library;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return start;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!IsContinuous)
					throw new InvalidOperationException ();

				return end;
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose, dispose all managed resources.
				if (disposing) {
					module.ModuleData = null;
					dwarf = null;
				}
				
				// Release unmanaged resources
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BfdModule ()
		{
			Dispose (false);
		}
	}
}
