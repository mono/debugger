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

		internal abstract TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							    TargetAddress arg2);

		internal abstract TargetAddress CallMethod (TargetAddress method, long method_argument,
							    string string_argument);

		internal abstract int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address);

		internal abstract int InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func);

		internal abstract void RemoveBreakpoint (int index);

		internal abstract void AddEventHandler (EventType type, EventHandle handle);

		internal abstract void RemoveEventHandler (int index);

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
		Thread thread;

		public ClientTargetAccess (Thread thread)
			: base (thread.ID, thread.Name)
		{
			this.thread = thread;
		}

		public override Debugger Debugger {
			get { return thread.Debugger; }
		}

		public override ITargetMemoryAccess TargetMemoryAccess {
			get { return thread.TargetMemoryAccess; }
		}

		public override ITargetInfo TargetInfo {
			get { return thread.TargetInfo; }
		}

		public override ITargetMemoryInfo TargetMemoryInfo {
			get { return thread.TargetMemoryInfo; }
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func)
		{
			throw new InvalidOperationException ();
		}

		internal override void RemoveBreakpoint (int index)
		{
			throw new InvalidOperationException ();
		}

		internal override void AddEventHandler (EventType type, EventHandle handle)
		{
			thread.AddEventHandler (type, handle);
		}

		internal override void RemoveEventHandler (int index)
		{
			thread.RemoveEventHandler (index);
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
			return thread.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			return thread.DisassembleMethod (method);
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

		internal override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							    TargetAddress arg2)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();

			CommandResult result = (CommandResult) sse.ThreadManager.SendCommand (
				sse, delegate (TargetAccess target, object user_data) {
					return sse.CallMethod (method, arg1, arg2);
				}, null);

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		internal override TargetAddress CallMethod (TargetAddress method, long method_arg,
							    string string_arg)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();

			CommandResult result = (CommandResult) sse.ThreadManager.SendCommand (
				sse, delegate (TargetAccess target, object user_data) {
					return sse.CallMethod (method, method_arg, string_arg);
				}, null);

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (TargetAddress) result.Result;
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
		{
			return (int) Invoke (delegate (TargetAccess target, object user_data) {
				return sse.InsertBreakpoint (breakpoint, address);
				}, null);
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func)
		{
			if (sse.ThreadManager.InBackgroundThread)
				throw new InvalidOperationException ();

			CommandResult result = (CommandResult) sse.ThreadManager.SendCommand (
				sse, delegate (TargetAccess target, object user_data) {
					return sse.InsertBreakpoint (breakpoint, func);
				}, null);

			result.Wait ();

			if (result.Result == null)
				throw new TargetException (TargetError.UnknownError);

			return (int) result.Result;
		}

		internal override void RemoveBreakpoint (int index)
		{
			Invoke (delegate (TargetAccess target, object user_data) {
				sse.RemoveBreakpoint (index);
				return null;
				}, null);
		}

		internal override void AddEventHandler (EventType type, EventHandle handle)
		{
			Invoke (delegate (TargetAccess target, object user_data) {
				sse.AddEventHandler (type, handle);
				return null;
				}, null);
		}

		internal override void RemoveEventHandler (int index)
		{
			Invoke (delegate (TargetAccess target, object user_data) {
				sse.RemoveEventHandler (index);
				return null;
			}, null);
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
				return sse.Thread.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			if (sse.ThreadManager.InBackgroundThread)
				return sse.DisassembleMethod (method);
			else
				return sse.Thread.DisassembleMethod (method);
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

		internal override TargetAddress CallMethod (TargetAddress method, TargetAddress arg1,
							    TargetAddress arg2)
		{
			throw new InvalidOperationException ();
		}

		internal override TargetAddress CallMethod (TargetAddress method, long method_arg,
							    string string_arg)
		{
			throw new InvalidOperationException ();
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
		{
			throw new InvalidOperationException ();
		}

		internal override int InsertBreakpoint (Breakpoint breakpoint, TargetFunctionType func)
		{
			throw new InvalidOperationException ();
		}

		internal override void RemoveBreakpoint (int index)
		{
			throw new InvalidOperationException ();
		}

		internal override void AddEventHandler (EventType type, EventHandle handle)
		{
			throw new InvalidOperationException ();
		}

		internal override void RemoveEventHandler (int index)
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
			return thread.DisassembleInstruction (method, address);
		}

		public override AssemblerMethod DisassembleMethod (Method method)
		{
			return thread.DisassembleMethod (method);
		}
	}
}
