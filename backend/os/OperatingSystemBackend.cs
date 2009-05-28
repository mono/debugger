using System;
using Mono.Debugger;

namespace Mono.Debugger.Backend
{
	internal abstract class OperatingSystemBackend : DebuggerMarshalByRefObject, IDisposable
	{
		public readonly ProcessServant Process;

		protected OperatingSystemBackend (ProcessServant process)
		{
			this.Process = process;
		}

		public abstract NativeExecutableReader LoadExecutable (TargetMemoryInfo memory, string filename,
								       bool load_native_symtabs);

		public abstract NativeExecutableReader AddExecutableFile (TargetMemoryInfo memory, string filename,
									  TargetAddress base_address,
									  bool step_info, bool is_loaded);

		public abstract TargetAddress LookupSymbol (string name);

		public abstract NativeExecutableReader LookupLibrary (TargetAddress address);

		public abstract NativeExecutableReader LookupLibrary (string name);

		public abstract bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start);

		internal abstract void UpdateSharedLibraries (Inferior inferior);

		internal abstract void ReadNativeTypes ();

#region IDisposable

		//
		// IDisposable
		//

		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException (GetType ().Name);
		}

		protected virtual void DoDispose ()
		{ }

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

		~OperatingSystemBackend ()
		{
			Dispose (false);
		}

#endregion
	}
}
