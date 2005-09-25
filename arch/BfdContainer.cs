using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger;
using Mono.Debugger.Backends;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;

namespace Mono.Debugger.Architecture
{
	internal class BfdContainer : MarshalByRefObject, IDisposable
	{
		Hashtable bfd_hash;
		DebuggerBackend backend;
		NativeLanguage language;

		public BfdContainer (DebuggerBackend backend)
		{
			this.backend = backend;
			this.bfd_hash = new Hashtable ();
		}

		public NativeLanguage NativeLanguage {
			get { return language; }
		}

		public DebuggerBackend DebuggerBackend {
			get { return backend; }
		}

		public Bfd this [string filename] {
			get {
				check_disposed ();
				return (Bfd) bfd_hash [filename];
			}
		}

		internal void SetupInferior (ITargetInfo info)
		{
			language = new NativeLanguage (this, info);
		}

		public Bfd AddFile (ITargetMemoryAccess memory, string filename,
				    bool step_into, bool is_main, bool is_loaded)
		{
			return AddFile (
				memory, filename, step_into, TargetAddress.Null,
				null, is_main, is_loaded);
		}

		public Bfd AddFile (ITargetMemoryAccess memory, string filename, bool step_into,
				    TargetAddress base_address, Bfd core_bfd, bool is_main,
				    bool is_loaded)
		{
			check_disposed ();
			Bfd bfd = (Bfd) bfd_hash [filename];
			if (bfd != null)
				return bfd;

			bfd = new Bfd (this, memory, memory, filename, false,
				       base_address, is_main, is_loaded);
			bfd.StepInto = step_into;
			bfd.CoreFileBfd = core_bfd;

			bfd_hash.Add (filename, bfd);

			backend.AddLanguage (bfd);

			return bfd;
		}

		public TargetAddress LookupSymbol (string name)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				TargetAddress symbol = bfd [name];
				if (!symbol.IsNull)
					return symbol;
			}

			return TargetAddress.Null;
		}

		public TargetType LookupType (StackFrame frame, string name)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				TargetType type = bfd.LookupType (frame, name);
				if (type != null)
					return type;
			}

			return null;
		}

		public void CloseBfd (Bfd bfd)
		{
			if (bfd == null)
				return;

			bfd_hash.Remove (bfd.FileName);
			bfd.Dispose ();
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
						bfd_hash = null;
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
