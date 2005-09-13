using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Messaging;

using Mono.Debugger.Remoting;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	[Serializable]
	public abstract class TargetAccess : ITargetAccess, ISerializable
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

		public abstract TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2);

		public abstract void RuntimeInvoke (ITargetFunctionType method_argument,
						    ITargetObject object_argument,
						    ITargetObject[] param_objects);

		public abstract ITargetObject RuntimeInvoke (ITargetFunctionType method_argument,
							     ITargetObject object_argument,
							     ITargetObject[] param_objects,
							     out string exc_message);

		public abstract object Invoke (TargetAccessDelegate func, object data);

		public abstract AssemblerLine DisassembleInstruction (IMethod method,
								      TargetAddress address);

		public abstract AssemblerMethod DisassembleMethod (IMethod method);

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

		public override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2)
		{
			return process.CallMethod (method, arg1, arg2);
		}

		public override void RuntimeInvoke (ITargetFunctionType method_argument,
						    ITargetObject object_argument,
						    ITargetObject[] param_objects)
		{
			process.RuntimeInvoke (method_argument, object_argument, param_objects);
		}

		public override ITargetObject RuntimeInvoke (ITargetFunctionType method_argument,
							     ITargetObject object_argument,
							     ITargetObject[] param_objects,
							     out string exc_message)
		{
			return process.RuntimeInvoke (
				method_argument, object_argument, param_objects, out exc_message);
		}

		public override object Invoke (TargetAccessDelegate func, object data)
		{
			return process.Invoke (func, data);
		}

		public override AssemblerLine DisassembleInstruction (IMethod method,
								      TargetAddress address)
		{
			return process.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (IMethod method)
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

		public override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							  TargetAddress arg2)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				return sse.Process.CallMethod (method, arg1, arg2);
		}

		public override void RuntimeInvoke (ITargetFunctionType method_argument,
						    ITargetObject object_argument,
						    ITargetObject[] param_objects)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				sse.Process.RuntimeInvoke (
					method_argument, object_argument, param_objects);
		}

		public override ITargetObject RuntimeInvoke (ITargetFunctionType method_argument,
							     ITargetObject object_argument,
							     ITargetObject[] param_objects,
							     out string exc_message)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();
			else
				return sse.Process.RuntimeInvoke (
					method_argument, object_argument, param_objects,
					out exc_message);
		}

		public override object Invoke (TargetAccessDelegate func, object data)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return func (this, data);
			else
				return sse.ThreadManager.SendCommand (sse, func, data);
		}

		public override AssemblerLine DisassembleInstruction (IMethod method,
								      TargetAddress address)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return sse.DisassembleInstruction (method, address);
			else
				return sse.Process.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (IMethod method)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return sse.DisassembleMethod (method);
			else
				return sse.Process.DisassembleMethod (method);
		}
	}
}
