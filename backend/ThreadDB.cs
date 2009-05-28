using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backend
{
	internal class ThreadDB : DebuggerMarshalByRefObject
	{
		IntPtr handle;
		ProcessServant process;
		TargetMemoryAccess target;
		DebuggerMutex mutex;

		enum PsErr {
			Ok = 0,
			Err,
			BadPid,
			BadLid,
			BadAddr,
			NoSym,
			NoFpRegs
		};

		internal delegate void GetThreadInfoFunc (int lwp, long tid);

		delegate PsErr GlobalLookupFunc (string obj_name, string sym_name, out long addr);
		delegate PsErr ReadMemoryFunc (long address, IntPtr buffer, int size);
		delegate PsErr WriteMemoryFunc (long address, IntPtr buffer, int size);
		delegate bool IterateOverThreadsFunc (IntPtr th);

		[DllImport("monodebuggerserver")]
		static extern IntPtr mono_debugger_thread_db_init (int pid, GlobalLookupFunc lookup_func, ReadMemoryFunc read_memory_func, WriteMemoryFunc write_memory_func);

		[DllImport("monodebuggerserver")]
		static extern void mono_debugger_thread_db_destroy (IntPtr handle);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_thread_db_iterate_over_threads (IntPtr handle, IterateOverThreadsFunc func);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_thread_db_get_thread_info (IntPtr th, out long tid, out long tls, out long lwp);

		GlobalLookupFunc global_lookup_func;
		ReadMemoryFunc read_memory_func;
		WriteMemoryFunc write_memory_func;

		protected ThreadDB (ProcessServant process)
		{
			this.process = process;

			mutex = new DebuggerMutex ("thread_db_mutex");

			global_lookup_func = new GlobalLookupFunc (global_lookup);
			read_memory_func = new ReadMemoryFunc (read_memory);
			write_memory_func = new WriteMemoryFunc (write_memory);
		}

		protected bool Initialize (Inferior target)
		{
			try {
				mutex.Lock ();
				this.target = target;

				handle = mono_debugger_thread_db_init (
					target.PID, global_lookup_func, read_memory_func,
					write_memory_func);

				return handle != IntPtr.Zero;
			} finally {
				this.target = null;
				mutex.Unlock ();
			}
		}

		public static ThreadDB Create (ProcessServant process, Inferior target)
		{
			ThreadDB db = new ThreadDB (process);
			if (!db.Initialize (target))
				return null;

			return db;
		}

		bool get_thread_info (IntPtr th)
		{
			long tid, tls, lwp;
			if (!mono_debugger_thread_db_get_thread_info (th, out tid, out tls, out lwp))
				return false;

			return true;
		}

		public void GetThreadInfo (TargetMemoryAccess target, GetThreadInfoFunc func)
		{
			try {
				mutex.Lock ();
				this.target = target;
				mono_debugger_thread_db_iterate_over_threads (
					handle, delegate (IntPtr th) {
						long tid, tls, lwp;
						if (!mono_debugger_thread_db_get_thread_info (
							    th, out tid, out tls, out lwp))
							return false;

						func ((int) lwp, tid);
						return true;
					});
			} finally {
				this.target = null;
				mutex.Unlock ();
			}
		}

		PsErr global_lookup (string obj_name, string sym_name, out long sym_addr)
		{
			NativeExecutableReader exe = process.OperatingSystem.LookupLibrary (obj_name);
			if (exe == null) {
				sym_addr = 0;
				return PsErr.NoSym;
			}

			TargetAddress addr = exe.LookupLocalSymbol (sym_name);
			if (addr.IsNull) {
				sym_addr = 0;
				return PsErr.NoSym;
			}

			sym_addr = addr.Address;
			return PsErr.Ok;
		}

		TargetAddress create_address (long address)
		{
			return new TargetAddress (target.AddressDomain, address);
		}

		PsErr read_memory (long address, IntPtr ptr, int size)
		{
			try {
				byte[] buffer = target.ReadBuffer (create_address (address), size);
				Marshal.Copy (buffer, 0, ptr, size);
			} catch {
				return PsErr.BadAddr;
			}
			return PsErr.Ok;
		}

		PsErr write_memory (long address, IntPtr ptr, int size)
		{
#if FIXME
			byte[] buffer = new byte [size];
			Marshal.Copy (ptr, buffer, 0, size);

			try {
				target.WriteBuffer (create_address (address), buffer);
			} catch {
				return PsErr.BadAddr;
			}
			return PsErr.Ok;
#else
			return PsErr.Err;
#endif
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("ThreadDB");
		}

		private void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			lock (this) {
				if (disposed)
					return;

				if (handle != IntPtr.Zero) {
					mono_debugger_thread_db_destroy (handle);
					handle = IntPtr.Zero;
				}

				if (mutex != null) {
					mutex.Dispose ();
					mutex = null;
				}

				disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~ThreadDB ()
		{
			Dispose (false);
		}

	}
}
