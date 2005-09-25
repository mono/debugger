using System;
using System.Threading;
using System.Collections;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Remoting
{
	public static class DebuggerContext
	{
		static DebuggerContextBase CurrentContext;

		internal static void CreateServerContext (Debugger backend)
		{
			if (CurrentContext != null)
				throw new InvalidOperationException ();

			CurrentContext = new DebuggerServerContext (backend);
		}

		internal static void CreateClientContext (DebuggerManager manager)
		{
			if (CurrentContext != null)
				throw new InvalidOperationException ();

			CurrentContext = new DebuggerClientContext (manager);
		}

		public static ThreadManager ThreadManager {
			get { return CurrentContext.ThreadManager; }
		}

		public static TargetAccess GetTargetAccess (int id)
		{
			return CurrentContext.GetTargetAccess (id);
		}

		protected abstract class DebuggerContextBase
		{
			public abstract ThreadManager ThreadManager {
				get;
			}

			public abstract TargetAccess GetTargetAccess (int id);
		}

		private  sealed class DebuggerClientContext : DebuggerContextBase
		{
			DebuggerManager manager;

			public DebuggerClientContext (DebuggerManager manager)
			{
				this.manager = manager;
			}

			public override ThreadManager ThreadManager {
				get { throw new InvalidOperationException (); }
			}

			public override TargetAccess GetTargetAccess (int id)
			{
				Process process = manager.GetProcess (id);
				return new ClientTargetAccess (process);
			}
		}

		private sealed class DebuggerServerContext : DebuggerContextBase
		{
			Debugger backend;

			public DebuggerServerContext (Debugger backend)
			{
				this.backend = backend;
			}

			public override ThreadManager ThreadManager {
				get { return backend.ThreadManager; }
			}

			public override TargetAccess GetTargetAccess (int id)
			{
				SingleSteppingEngine sse = ThreadManager.GetEngine (id);
				return new ServerTargetAccess (sse);
			}
		}
	}
}
