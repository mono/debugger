using System;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;

namespace Mono.Debugger
{
	public delegate object ObjectCacheFunc (object user_data);

	public class ObjectCache : IDisposable
	{
		WeakReference weak_reference;
		ObjectCacheFunc func;
		object user_data;
		object cached_object;

		TimeSpan timeout;
		Timer timer;

		public ObjectCache (ObjectCacheFunc func, object user_data, TimeSpan timeout)
		{
			this.func = func;
			this.user_data = user_data;
			this.timeout = timeout;

			timer = new Timer (new TimerCallback (timeout_cb), null,
					   TimeSpan.Zero, TimeSpan.Zero);
		}

		void timeout_cb (object dummy)
		{
			cached_object = null;
		}

		public object Data {
			get {
				check_disposed ();

				// We must avoid a race condition here: when restarting the
				// timeout, this may wipe out the cached_object immediately.
				object data = cached_object;

				// If we still have a hard reference to the data.
				if (data != null) {
					// Reset timeout since the data has been accessed.
					timer.Change (timeout, TimeSpan.Zero);
					return data;
				}

				// Maybe we still have a weak reference to it.
				if (weak_reference != null) {
					try {
						data = weak_reference.Target;
					} catch {
						weak_reference = null;
					}
				}
				if (data != null) {
					// Data is still there and has just been accessed, so
					// add a hard reference to it again and restart the timeout.
					cached_object = data;
					timer.Change (timeout, TimeSpan.Zero);
					return data;
				}

				data = func (user_data);
				weak_reference = new WeakReference (data);

				// Just created a new object, add a hard reference to it and restart
				// the timeout.
				cached_object = data;
				timer.Change (timeout, TimeSpan.Zero);

				return data;
			}
		}

		public TimeSpan Timeout {
			get {
				check_disposed ();
				return timeout;
			}

			set {
				check_disposed ();
				timeout = value;
				timer.Change (timeout, TimeSpan.Zero);
			}
		}

		public void Flush ()
		{
			timer.Change (TimeSpan.Zero, TimeSpan.Zero);
			cached_object = null;
			weak_reference = null;
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
					timer.Dispose ();
					IDisposable data_dispose = cached_object as IDisposable;
					if (data_dispose != null)
						data_dispose.Dispose ();
					cached_object = null;
					weak_reference = null;
					user_data = null;
					timer = null;
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

		~ObjectCache ()
		{
			Dispose (false);
		}
	}
}
