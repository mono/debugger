using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Remoting;

namespace Mono.Debugger.Backends
{
	[Serializable]
	public abstract class TargetAccess : ITargetAccess, ISerializable
	{
		int id;

		protected TargetAccess (int id)
		{
			this.id = id;
		}

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract ITargetMemoryInfo TargetMemoryInfo {
			get;
		}

		public abstract TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2);

		//
		// ISerializable
		//

		public void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("id", id);
			info.SetType (typeof (TargetAccessHelper));
		}
	}

	[Serializable]
	internal sealed class TargetAccessHelper : ISerializable, IObjectReference
	{
		int id;

		public object GetRealObject (StreamingContext context)
		{
			return DebuggerContext.GetTargetAccess (id);
		}

		protected TargetAccessHelper (SerializationInfo sinfo, StreamingContext context)
		{
			this.id = (int) sinfo.GetValue ("id", typeof (int));
		}

		void ISerializable.GetObjectData (SerializationInfo info, StreamingContext context)
		{
			throw new InvalidOperationException ();
		}
	}

	[Serializable]
	internal sealed class ClientTargetAccess : TargetAccess
	{
		Process process;

		public ClientTargetAccess (Process process)
			: base (process.ID)
		{
			this.process = process;
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get { return process.TargetMemoryAccess; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return process.TargetMemoryInfo; }
		}

		public override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2)
		{
			return process.CallMethod (method, arg1, arg2);
		}
	}

	[Serializable]
	internal sealed class ServerTargetAccess : TargetAccess
	{
		SingleSteppingEngine sse;

		public ServerTargetAccess (SingleSteppingEngine sse)
			: base (sse.ID)
		{
			this.sse = sse;
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get {
				if (sse.ThreadManager.InBackgroundThread)
					return sse.Inferior;
				else
					return sse.Process;
			}
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return sse.TargetMemoryInfo; }
		}

		public override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				return sse.Process.CallMethod (method, arg1, arg2);
		}

	}
}
