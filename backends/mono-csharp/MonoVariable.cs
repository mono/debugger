using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoVariable : Variable
	{
		VariableInfo info;
		IDebuggerBackend backend;
		TargetAddress start_scope, end_scope;
		ITargetLocation location;
		bool is_local;

		public MonoVariable (IDebuggerBackend backend, string name, MonoType type,
				     bool is_local, IMethod method, VariableInfo info)
			: base (name, type)
		{
			this.backend = backend;
			this.is_local = is_local;
			this.info = info;

			if (info.BeginScope != 0)
				start_scope = method.StartAddress + info.BeginScope;
			else
				start_scope = method.MethodStartAddress;
			if (info.EndScope != 0)
				end_scope = method.StartAddress + info.EndScope;
			else
				end_scope = method.MethodEndAddress;

			if (info.Mode == VariableInfo.AddressMode.Stack)
				location = new TargetStackLocation (
					backend, is_local, info.Offset, start_scope, end_scope);
		}

		public override ITargetLocation Location {
			get {
				if (location == null)
					throw new LocationInvalidException ();

				return location;
			}
		}

		public override ITargetMemoryReader MemoryReader {
			get {
				if (!Location.IsValid)
					throw new LocationInvalidException ();

				IStackFrame frame = (IStackFrame) Location.Handle;
				IInferiorStackFrame iframe = (IInferiorStackFrame) frame.FrameHandle;

				return iframe.Inferior.ReadMemory (Location.Address, Type.Size);
			}
		}
	}
}
