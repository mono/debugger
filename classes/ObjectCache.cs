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
		int initial_ttl, ttl;
		int id;

		static Timer timer;
		static ArrayList objects;
		static int next_id = 0;

		public ObjectCache (ObjectCacheFunc func, object user_data, int ttl)
		{
			this.func = func;
			this.user_data = user_data;
			this.initial_ttl = this.ttl = ttl;
			this.id = ++next_id;

			objects.Add (this);
		}

		static ObjectCache ()
		{
			objects = ArrayList.Synchronized (new ArrayList ());
			timer = new Timer (new TimerCallback (cleanup_process), null, 0, 60000);
		}

		static void cleanup_process (object dummy)
		{
			foreach (ObjectCache obj in objects)
				obj.timeout_func ();
		}

		void timeout_func ()
		{
			if (ttl > 0)
				--ttl;
			if (ttl > 0)
				return;

			cached_object = null;
		}

		public object PeekData {
			get {
				check_disposed ();

				// We must avoid a race condition here: when restarting the
				// timeout, this may wipe out the cached_object immediately.
				object data = cached_object;

				// If we still have a hard reference to the data.
				if (data != null)
					return data;

				// Maybe we still have a weak reference to it.
				if (weak_reference != null) {
					try {
						data = weak_reference.Target;
					} catch {
						weak_reference = null;
					}
				}

				return data;
			}
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
					ttl = initial_ttl;
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
					ttl = initial_ttl;
					return data;
				}

				data = func (user_data);
				weak_reference = new WeakReference (data);

				// Just created a new object, add a hard reference to it and restart
				// the timeout.
				cached_object = data;
				ttl = initial_ttl;

				return data;
			}
		}

		public void Flush ()
		{
			ttl = 0;
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
					ttl = -1;
					objects.Remove (this);
					IDisposable data_dispose = cached_object as IDisposable;
					if (data_dispose != null)
						data_dispose.Dispose ();
					cached_object = null;
					weak_reference = null;
					user_data = null;
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

		public override string ToString ()
		{
			return String.Format ("ObjectCache ({0}:{1}:{2}:{3})", id,
					      initial_ttl, ttl, cached_object != null);
		}
	}
}
