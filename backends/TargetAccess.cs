using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Remoting;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	[Serializable]
	public delegate object TargetAccessDelegate (Thread target, object user_data);

	[Serializable]
	internal abstract class TargetAccess
	{
		int id;
		string name;

		protected TargetAccess (int id, string name)
		{
			this.id = id;
			this.name = name;
		}

		public int ID {
			get { return id; }
		}

		public string Name {
			get { return name; }
		}

		public abstract Debugger Debugger {
			get;
		}

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract ITargetInfo TargetInfo {
			get;
		}

		public abstract ITargetMemoryInfo TargetMemoryInfo {
			get;
		}
	}
}

namespace Mono.Debugger.Backends
{
	[Serializable]
	internal sealed class ServerTargetAccess : TargetAccess
	{
		SingleSteppingEngine sse;

		public ServerTargetAccess (SingleSteppingEngine sse)
			: base (sse.ID, sse.Name)
		{
			this.sse = sse;
		}

		public override Debugger Debugger {
			get { return sse.ThreadManager.Debugger; }
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get {
				if (sse.ThreadManager.InBackgroundThread)
					return sse.Inferior;
				else
					return sse.Thread;
			}
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return sse.TargetMemoryInfo; }
		}

		public override ITargetInfo TargetInfo {
			get { return sse.TargetInfo; }
		}
	}

	[Serializable]
	internal sealed class ThreadTargetAccess : TargetAccess
	{
		ThreadBase thread;
		ITargetMemoryAccess memory;

		public ThreadTargetAccess (ThreadBase thread, ITargetMemoryAccess memory,
					   int id, string name)
			: base (id, name)
		{
			this.thread = thread;
			this.memory = memory;
		}

		public override Debugger Debugger {
			get { return thread.ThreadManager.Debugger; }
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get { return memory; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return thread.TargetMemoryInfo; }
		}

		public override ITargetInfo TargetInfo {
			get { return thread.TargetInfo; }
		}
	}
}
