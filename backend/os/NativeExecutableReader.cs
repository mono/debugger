using System;

using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal abstract class NativeExecutableReader : DebuggerMarshalByRefObject, IDisposable
	{
		public abstract Module Module {
			get;
		}

		public abstract TargetAddress LookupSymbol (string name);

		public abstract TargetAddress LookupLocalSymbol (string name);

		public abstract TargetAddress GetSectionAddress (string name);

		public abstract TargetAddress EntryPoint {
			get;
		}

		public abstract TargetReader GetReader (TargetAddress address);

		//
		// IDisposable
		//

		private bool disposed = false;

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

		~NativeExecutableReader ()
		{
			Dispose (false);
		}
	}
}
