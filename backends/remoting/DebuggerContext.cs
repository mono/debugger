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

		internal static ThreadManager ThreadManager {
			get { return CurrentContext.ThreadManager; }
		}

		internal static DebuggerManager DebuggerManager {
			get { return CurrentContext.DebuggerManager; }
		}

		public static ReportWriter ReportWriter {
			get { return CurrentContext.ReportWriter; }
		}

		protected abstract class DebuggerContextBase
		{
			internal abstract ThreadManager ThreadManager {
				get;
			}

			internal abstract DebuggerManager DebuggerManager {
				get;
			}

			public abstract ReportWriter ReportWriter {
				get;
			}
		}

		private sealed class DebuggerClientContext : DebuggerContextBase
		{
			DebuggerManager manager;
			ReportWriter report;

			public DebuggerClientContext (DebuggerManager manager)
			{
				this.manager = manager;
				this.report = manager.ReportWriter;
			}

			internal override ThreadManager ThreadManager {
				get { throw new InvalidOperationException (); }
			}

			internal override DebuggerManager DebuggerManager {
				get { return manager; }
			}

			public override ReportWriter ReportWriter {
				get { return report; }
			}
		}

		private sealed class DebuggerServerContext : DebuggerContextBase
		{
			Debugger backend;
			ReportWriter report;

			public DebuggerServerContext (Debugger backend)
			{
				this.backend = backend;
				this.report = backend.DebuggerManager.ReportWriter;
			}

			internal override ThreadManager ThreadManager {
				get { return backend.ThreadManager; }
			}

			internal override DebuggerManager DebuggerManager {
				get { return backend.DebuggerManager; }
			}

			public override ReportWriter ReportWriter {
				get { return report; }
			}
		}
	}
}
