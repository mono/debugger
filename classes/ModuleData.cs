using System;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public delegate void ModuleDataEventHandler (ModuleData data);

	public abstract class ModuleData
	{
		protected ModuleData (Module module, string name)
		{
			this.module = module;
			this.name = name;
		}

		Module module;
		string name;

		public Module Module {
			get { return module; }
		}

		public string FullName {
			get { return name; }
		}

		public abstract ILanguage Language {
			get;
		}

		public abstract object LanguageBackend {
			get;
		}

		public abstract bool SymbolsLoaded {
			get;
		}

		public abstract SourceFile[] Sources {
			get;
		}

		public abstract ISymbolTable SymbolTable {
			get;
		}

		public abstract ISimpleSymbolTable SimpleSymbolTable {
			get;
		}

		public abstract bool HasDebuggingInfo {
			get;
		}

		internal abstract void ReadModuleData ();

		public abstract SourceMethod FindMethod (string name);

		public abstract TargetAddress SimpleLookup (string name);

		// <summary>
		//   This event is emitted when the module's symbol tables are loaded.
		// </summary>
		public event ModuleDataEventHandler SymbolsLoadedEvent;

		// <summary>
		//   This event is emitted when the module's symbol tables are unloaded.
		// </summary>
		public event ModuleDataEventHandler SymbolsUnLoadedEvent;

		protected virtual void OnSymbolsLoadedEvent ()
		{
			if (SymbolsLoadedEvent != null)
				SymbolsLoadedEvent (this);
		}

		protected virtual void OnSymbolsUnLoadedEvent ()
		{
			if (SymbolsUnLoadedEvent != null)
				SymbolsUnLoadedEvent (this);
		}

		// <summary>
		//   This event is emitted when adding or removing a breakpoint or
		//   enabling/disabling a breakpoint.
		// </summary>
		public event ModuleDataEventHandler BreakpointsChangedEvent;

		protected virtual void OnBreakpointsChangedEvent ()
		{
			if (BreakpointsChangedEvent != null)
				BreakpointsChangedEvent (this);
		}
	}
}
