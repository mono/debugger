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
	internal class BfdContainer : ILanguageBackend, ISerializable, IDisposable
	{
		Hashtable bfd_hash;
		Hashtable module_hash;
		DebuggerBackend backend;

		public BfdContainer (DebuggerBackend backend)
		{
			this.backend = backend;
			this.bfd_hash = new Hashtable ();
			this.module_hash = new Hashtable ();

			backend.TargetExited += new TargetExitedHandler (target_exited_handler);
		}

		public Module this [string filename] {
			get {
				check_disposed ();
				return (Module) module_hash [filename];
			}
		}

		public Bfd AddFile (IInferior inferior, string filename, bool step_into)
		{
			return AddFile (inferior, filename, step_into, TargetAddress.Null, null);
		}

		public Bfd AddFile (IInferior inferior, string filename, bool step_into,
				    TargetAddress base_address, Bfd core_bfd)
		{
			bool new_module = false;

			check_disposed ();
			if (bfd_hash.Contains (filename))
				return (Bfd) bfd_hash [filename];

			BfdModule module = (BfdModule) module_hash [filename];
			if (module == null) {
				module = new BfdModule (filename, backend, !base_address.IsNull);
				module.LoadSymbols = step_into;
				module.StepInto = step_into;
				module_hash.Add (filename, module);
 				new_module = true;
			}

			Bfd bfd = new Bfd (this, inferior, filename, false, module, base_address);
			bfd.CoreFileBfd = core_bfd;
 			module.Bfd = bfd;

			if (module.StepInto) {
				try {
					bfd.ReadDwarf ();
				} catch {
					// Silently ignore.
				}
			}

			module.Inferior = inferior;
			bfd_hash.Add (filename, bfd);

			OnModulesChangedEvent ();

			return bfd;
		}

		public void CloseBfd (Bfd bfd)
		{
			if (bfd == null)
				return;

			bfd_hash.Remove (bfd.FileName);
			bfd.Dispose ();
		}

		void target_exited_handler ()
		{
			foreach (Bfd bfd in bfd_hash.Values)
				bfd.Dispose ();
			bfd_hash = new Hashtable ();

			foreach (BfdModule module in module_hash.Values) {
				module.Bfd = null;
				module.Inferior = null;
			}
		}

		//
		// ILanguageBackend
		//

		string ILanguageBackend.Name {
			get {
				return "native";
			}
		}

		public ISymbolTable SymbolTable {
			get {
				throw new InvalidOperationException ();
			}
		}

		public Module[] Modules {
			get {
				Module[] modules = new Module [module_hash.Values.Count];
				module_hash.Values.CopyTo (modules, 0);
				return modules;
			}
		}

		public event ModulesChangedHandler ModulesChangedEvent;

		protected virtual void OnModulesChangedEvent ()
		{
			if (ModulesChangedEvent != null)
				ModulesChangedEvent ();
		}

		public TargetAddress GenericTrampolineCode {
			get {
				return TargetAddress.Null;
			}
		}

		public TargetAddress GetTrampoline (TargetAddress address)
		{
			return TargetAddress.Null;
		}

		public bool BreakpointHit (TargetAddress address)
		{
			return true;
		}

		//
		// ISerializable
		//

		public void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("modules", module_hash.Values, typeof (ICollection));
		}

		private BfdContainer (SerializationInfo info, StreamingContext context)
		{
			backend = context.Context as DebuggerBackend;
			if (backend == null)
				throw new InvalidOperationException ();

			ICollection modules = (ICollection) info.GetValue ("modules", typeof (ICollection));

			bfd_hash = new Hashtable ();
			module_hash = new Hashtable ();

			backend.TargetExited += new TargetExitedHandler (target_exited_handler);

			foreach (BfdModule module in module_hash) {
				module_hash.Add (module.FullName, module);
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("BfdContainer");
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					if (bfd_hash != null) {
						foreach (Bfd bfd in bfd_hash.Values)
							bfd.Dispose ();
					}
				}
				
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~BfdContainer ()
		{
			Dispose (false);
		}

	}
}
