using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public class ObjectContainer : IDisposable
	{
		ArrayList objects = new ArrayList ();

		public void AddObject (ObjectCache data)
		{
			check_disposed ();
			objects.Add (data);
		}

		//
		// IDisposable
		//

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Inferior");
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					foreach (ObjectCache data in objects)
						data.Dispose ();
					objects = null;
					// Do stuff here
				}
				
				this.disposed = true;

				lock (this) {
					// Release unmanaged resources
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ObjectContainer ()
		{
			Dispose (false);
		}
	}
}
