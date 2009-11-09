using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Xml;
using System.Xml.XPath;

using Mono.Debugger.Languages;
using Mono.Debugger.Backend;

namespace Mono.Debugger
{
	public delegate void ModuleEventHandler (Module module);

	internal interface ILoadHandler
	{
		object UserData {
			get;
		}

		void Remove ();
	}

	internal abstract class SymbolFile : DebuggerMarshalByRefObject, IDisposable
	{
		public abstract Module Module {
			get;
		}

		public abstract bool IsNative {
			get;
		}

		public abstract string FullName {
			get;
		}

		public abstract string CodeBase {
			get;
		}

		public abstract Language Language {
			get;
		}

		public abstract bool SymbolsLoaded {
			get;
		}

		public abstract bool HasDebuggingInfo {
			get;
		}

		public abstract SourceFile[] Sources {
			get;
		}

		public abstract MethodSource[] GetMethods (SourceFile file);

		// public abstract Method GetMethod (int domain, long handle);

		public abstract MethodSource FindMethod (string name);

		public abstract Symbol SimpleLookup (TargetAddress address, bool exact_match);

		public abstract ISymbolTable SymbolTable {
			get;
		}

		internal abstract StackFrame UnwindStack (StackFrame last_frame,
							  TargetMemoryAccess memory);

		internal abstract void OnModuleChanged ();

		//
		// IDisposable
		//

		private bool disposed = false;

		protected virtual void DoDispose ()
		{
			Module.UnLoadModule ();
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing)
				DoDispose ();
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~SymbolFile ()
		{
			Dispose (false);
		}
	}

	public abstract class ModuleBase : DebuggerMarshalByRefObject
	{
		protected string name;
		protected int id;
		private static int next_id;

		protected ModuleBase (string name)
		{
			this.name = name;
			this.id = ++next_id;
		}

		public int ID {
			get { return id; }
		}

		public string Name {
			get { return name; }
		}

		public abstract bool HideFromUser {
			get; set;
		}

		public abstract bool LoadSymbols {
			get; set;
		}

		public abstract bool StepInto {
			get; set;
		}

		internal void GetSessionData (XmlElement root)
		{
			XmlElement element = root.OwnerDocument.CreateElement (GetType ().Name);
			root.AppendChild (element);

			if (this is Module)
				element.SetAttribute ("group", ((Module) this).ModuleGroup.Name);
			element.SetAttribute ("name", Name);

			XmlElement hide = root.OwnerDocument.CreateElement ("HideFromUser");
			hide.InnerText = HideFromUser ? "true" : "false";
			element.AppendChild (hide);

			XmlElement step = root.OwnerDocument.CreateElement ("StepInto");
			step.InnerText = StepInto ? "true" : "false";
			element.AppendChild (step);

			XmlElement load = root.OwnerDocument.CreateElement ("LoadSymbols");
			load.InnerText = LoadSymbols ? "true" : "false";
			element.AppendChild (load);
		}

		internal void SetSessionData (XPathNodeIterator iter)
		{
			XPathNodeIterator children = iter.Current.SelectChildren (XPathNodeType.Element);
			while (children.MoveNext ()) {
				if (children.Current.Name == "HideFromUser")
					HideFromUser = Boolean.Parse (children.Current.Value);
				else if (children.Current.Name == "LoadSymbols")
					LoadSymbols = Boolean.Parse (children.Current.Value);
				else if (children.Current.Name == "StepInto")
					StepInto = Boolean.Parse (children.Current.Value);
				else
					throw new InternalError ();
			}
		}

		protected virtual string MyToString ()
		{
			return "";
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4}:{5}{6})", GetType (), id, name,
					      HideFromUser ? "hide" : "nohide",
					      LoadSymbols ? "load" : "noload",
					      StepInto ? "step" : "nostep", MyToString ());
		}
	}

	public class ModuleGroup : ModuleBase
	{
		string regexp;
		bool hide_from_user;
		bool load_symbols;
		bool step_into;

		public string Regexp {
			get { return regexp; }
			set { regexp = value; }
		}

		public override bool HideFromUser {
			get { return hide_from_user; }
			set { hide_from_user = value; }
		}

		public override bool LoadSymbols {
			get { return load_symbols; }
			set { load_symbols = value; }
		}

		public override bool StepInto {
			get { return step_into; }
			set { step_into = value; }
		}

		internal ModuleGroup (string name)
			: this (name, false, false, false)
		{
		}

		internal ModuleGroup (string name, bool hide_from_user, bool load_symbols,
				      bool step_into)
			: base (name)
		{
			this.hide_from_user = hide_from_user;
			this.load_symbols = load_symbols;
			this.step_into = step_into;
		}
	}

	// <summary>
	//   A module is either a shared library (containing unmanaged code) or a dll
	//   (containing managed code).
	//
	//   A module maintains all the breakpoints and controls whether to enter methods
	//   while single-stepping.
	// </summary>
	public sealed class Module : ModuleBase
	{
		ModuleGroup group;
		[NonSerialized]
		SymbolFile symfile;
		bool has_hide_from_user, hide_from_user;
		bool has_load_symbols, load_symbols;
		bool has_step_into, step_into;

		internal Module (ModuleGroup group, string name, SymbolFile symfile)
			: base (name)
		{
			this.group = group;
			this.symfile = symfile;
		}

		internal void LoadModule (SymbolFile symfile)
		{
			this.symfile = symfile;
			OnModuleChanged ();
			if (ModuleLoadedEvent != null)
				ModuleLoadedEvent (this);
		}

		internal void UnLoadModule ()
		{
			if (ModuleUnloadedEvent != null)
				ModuleUnloadedEvent (this);
			this.symfile = null;
		}

		public event ModuleEventHandler ModuleLoadedEvent;
		public event ModuleEventHandler ModuleUnloadedEvent;

		public Language Language {
			get { return SymbolFile.Language; }
		}

		internal SymbolFile SymbolFile {
			get {
				if (symfile != null)
					return symfile;

				throw new InvalidOperationException ();
			}
		}

		public ModuleGroup ModuleGroup {
			get { return group; }
		}

		// <summary>
		//   The full pathname where this module was loaded from.
		//   May only be used while @IsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if IsLoaded was false.
		// </summary>
		public string FullName {
			get { return SymbolFile.FullName; }
		}

		// <summary>
		//   Whether the module is currently loaded in memory.
		// </summary>
		public bool IsLoaded {
			get { return symfile != null; }
		}

		// <summary>
		//   Whether the module's symbol tables are currently loaded.
		// </summary>
		public bool SymbolsLoaded {
			get { return IsLoaded && SymbolFile.SymbolsLoaded; }
		}

		// <summary>
		//   Whether to load the module's symbol tables when the module is loaded
		//   into memory.  You may set this to false to completely "ignore" a
		//   module the next time you restart the target; the debugger will
		//   neither step into any of the module's methods nor will you get any
		//   method names or source locations in a backtrace.
		//
		//   Note that setting this to false does not disable any breakpoints -
		//   the debugger will still stop if it its a breakpoint inside this
		//   module, but you'll see nothing but an address in the backtrace and
		//   you won't see any source code.
		// </summary>
		public override bool LoadSymbols {
			get {
				return has_load_symbols ? load_symbols : group.LoadSymbols;
			}

			set {
				if (has_load_symbols && (load_symbols == value))
					return;

				has_load_symbols = true;
				load_symbols = value;
				OnModuleChanged ();
			}
		}

		// <summary>
		//   Whether to enter this module's methods while single-stepping.
		//   If you set this to false, you will still get full debugging support
		//   if the debugger ever stops in this module, for instance because it
		//   hit a breakpoint or received a signal.
		//
		//   When debugging managed applications, you should set this to false for
		//   `mono' and its shared libraries.  If the application ever crashes
		//   somewhere inside an unmanaged method, you'll get full debugging
		//   information in the backtrace, but the debugger will never enter
		//   unmanaged methods while single-stepping.
		// </summary>
		public override bool StepInto {
			get {
				return has_step_into ? step_into : group.StepInto;
			}

			set {
				if (has_step_into && (step_into == value))
					return;

				has_step_into = true;
				step_into = value;
				OnModuleChanged ();
			}
		}

		public override bool HideFromUser {
			get {
				return has_hide_from_user ? hide_from_user : group.HideFromUser;
			}

			set {
				if (has_hide_from_user && (hide_from_user == value))
					return;

				has_hide_from_user = true;
				hide_from_user = value;
				OnModuleChanged ();
			}
		}

		// <summary>
		//   Returns whether this module has debugging info.
		//   Note that this property is initialized when trying to read the debugging
		//   info for the first time.
		// </summary>
		public bool HasDebuggingInfo {
			get { return SymbolFile.HasDebuggingInfo; }
		}

		void OnModuleChanged ()
		{
			if (symfile != null)
				symfile.OnModuleChanged ();
		}

		public SourceFile[] Sources {
			get { return SymbolFile.Sources; }
		}

		public MethodSource[] GetMethods (SourceFile file)
		{
			return SymbolFile.GetMethods (file);
		}

#if FIXME
		public Method GetMethod (int domain, long handle)
		{
			return SymbolFile.GetMethod (domain, handle);
		}
#endif

		// <summary>
		//   Find method @name, which must be a full method name including the
		//   signature (System.DateTime.GetUtcOffset(System.DateTime)).
		// </summary>
		public MethodSource FindMethod (string name)
		{
			if (!SymbolsLoaded)
				return null;

			return SymbolFile.FindMethod (name);
		}

		// <summary>
		//   Find source file @filename.  If @filename is a full pathname, an exact
		//   match is required.  Otherwise, a match from any directory is ok.
		// </summary>
		public SourceFile FindFile (string filename)
		{
			if (!SymbolsLoaded)
				return null;

			if (filename.IndexOf ('/') >= 0) {
				foreach (SourceFile source in Sources) {
					if (source.FileName == filename)
						return source;
				}
			} else {
				filename = Path.GetFileName (filename);
				foreach (SourceFile source in Sources) {
					if (Path.GetFileName (source.FileName).Equals (filename))
						return source;
				}
			}

			return null;
		}

		// <summary>
		//   Find the method containing line @line in @source_file, which must be
		//   the file's full pathname.
		// </summary>
		public SourceLocation FindLocation (string source_file, int line)
		{
			SourceFile file = FindFile (source_file);
			if (file == null)
				return null;

			return file.FindLine (line);
		}

		public Symbol SimpleLookup (TargetAddress address, bool exact_match)
		{
			return SymbolFile.SimpleLookup (address, exact_match);
		}

		// <summary>
		//   Returns the module's ISymbolTable which can be used to find a method
		//   by its address.  May only be used while @SymbolsLoaded is true.
		//
		//   Throws:
		//     InvalidOperationException - if @SymbolsLoaded was false
		// </summary>
		public ISymbolTable SymbolTable {
			get { return SymbolFile.SymbolTable; }
		}

		internal StackFrame UnwindStack (StackFrame last_frame,
						 TargetMemoryAccess memory)
		{
			return SymbolFile.UnwindStack (last_frame, memory);
		}

		protected override string MyToString ()
		{
			return String.Format (":{0}:{1}", IsLoaded, SymbolsLoaded);
		}
	}
}
