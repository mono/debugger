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

		public static ReportWriter ReportWriter {
			get { return CurrentContext.ReportWriter; }
		}

		public static TargetAccess GetTargetAccess (int id)
		{
			return CurrentContext.GetTargetAccess (id);
		}

		protected abstract class DebuggerContextBase
		{
			internal abstract ThreadManager ThreadManager {
				get;
			}

			public abstract ReportWriter ReportWriter {
				get;
			}

			public abstract TargetAccess GetTargetAccess (int id);
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

			public override ReportWriter ReportWriter {
				get { return report; }
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
			ReportWriter report;

			public DebuggerServerContext (Debugger backend)
			{
				this.backend = backend;
				this.report = backend.DebuggerManager.ReportWriter;
			}

			internal override ThreadManager ThreadManager {
				get { return backend.ThreadManager; }
			}

			public override ReportWriter ReportWriter {
				get { return report; }
			}

			public override TargetAccess GetTargetAccess (int id)
			{
				SingleSteppingEngine sse = ThreadManager.GetEngine (id);
				return new ServerTargetAccess (sse);
			}
		}
	}
}
