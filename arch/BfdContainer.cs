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
	internal class BfdContainer : ISerializable, IDisposable
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

		public IModule this [string filename] {
			get {
				check_disposed ();
				return (IModule) module_hash [filename];
			}
		}

		public Bfd AddFile (IInferior inferior, string filename, bool load_native_symtab)
		{
			check_disposed ();
			if (bfd_hash.Contains (filename))
				return (Bfd) bfd_hash [filename];

			BfdModule module = (BfdModule) module_hash [filename];
			if (module == null) {
				module = new BfdModule (filename);
				module.LoadSymbols = true;
				module.StepInto = load_native_symtab;
				module_hash.Add (filename, module);
			}

			Bfd bfd = new Bfd (inferior, filename, false, true, module);
			module.IsLoaded = module.SymbolsLoaded = true;
			bfd_hash.Add (filename, bfd);
			return bfd;
		}

		public void CloseBfd (Bfd bfd)
		{
			if (bfd == null)
				return;

			bfd_hash.Remove (bfd.FileName);
			bfd.Dispose ();
		}

		public IModule[] Modules {
			get {
				IModule[] modules = new IModule [module_hash.Values.Count];
				module_hash.Values.CopyTo (modules, 0);
				return modules;
			}
		}

		void target_exited_handler ()
		{
			foreach (Bfd bfd in bfd_hash.Values)
				bfd.Dispose ();
			bfd_hash = new Hashtable ();

			foreach (BfdModule module in module_hash.Values)
				module.IsLoaded = module.SymbolsLoaded = false;
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
