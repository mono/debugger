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
	public delegate object TargetAccessDelegate (TargetAccess target, object user_data);

	[Serializable]
	public abstract class TargetAccess : ISerializable
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

		public abstract ITargetMemoryAccess TargetMemoryAccess {
			get;
		}

		public abstract ITargetInfo TargetInfo {
			get;
		}

		public abstract ITargetMemoryInfo TargetMemoryInfo {
			get;
		}

		internal abstract TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							    TargetAddress arg2);

		internal abstract TargetAddress CallMethod (TargetAddress method, long method_argument,
							    string string_argument);

		internal abstract object Invoke (TargetAccessDelegate func, object data);

		public abstract AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address);

		public abstract AssemblerMethod DisassembleMethod (Method method);

		//
		// ISerializable
		//

		public void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("id", id);
			info.SetType (typeof (Mono.Debugger.Backends.TargetAccessHelper));
		}
	}
}

namespace Mono.Debugger.Backends
{
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
			: base (process.ID, process.Name)
		{
			this.process = process;
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get { return process.TargetMemoryAccess; }
		}

		public override ITargetInfo TargetInfo {
			get { return process.TargetInfo; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return process.TargetMemoryInfo; }
		}

		internal override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetAddress CallMethod (TargetAddress method, long method_argument,
							    string string_argument)
		{
			throw new InvalidOperationException ();
		}

		internal override object Invoke (TargetAccessDelegate func, object data)
		{
			throw new InvalidOperationException ();
		}

		public override AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address)
		{
			return process.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			return process.DisassembleMethod (method);
		}
	}

	[Serializable]
	internal sealed class ServerTargetAccess : TargetAccess
	{
		SingleSteppingEngine sse;

		public ServerTargetAccess (SingleSteppingEngine sse)
			: base (sse.ID, sse.Name)
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

		public override ITargetInfo TargetInfo {
			get { return sse.TargetInfo; }
		}

		internal override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							    TargetAddress arg2)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				return sse.Process.CallMethod (method, arg1, arg2);
		}

		internal override TargetAddress CallMethod (TargetAddress method, long method_argument,
							    string string_argument)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				return sse.Process.CallMethod (method, method_argument, string_argument);
		}

		internal override object Invoke (TargetAccessDelegate func, object data)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return func (this, data);
			else
				return sse.ThreadManager.SendCommand (sse, func, data);
		}

		public override AssemblerLine DisassembleInstruction (Method method,
								      TargetAddress address)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return sse.DisassembleInstruction (method, address);
			else
				return sse.Process.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return sse.DisassembleMethod (method);
			else
				return sse.Process.DisassembleMethod (method);
		}
	}
}
