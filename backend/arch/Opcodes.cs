using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architectures
{
	internal abstract class Opcodes : DebuggerMarshalByRefObject, IDisposable
	{
		internal abstract Instruction ReadInstruction (TargetMemoryAccess memory,
							       TargetAddress address);

		internal abstract byte[] GenerateNopInstruction ();

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Opcodes");
		}

		protected virtual void DoDispose ()
		{
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

		~Opcodes ()
		{
			Dispose (false);
		}
	}
}
