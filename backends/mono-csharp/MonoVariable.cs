using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoVariable : IVariable
	{
		VariableInfo info;
		string name;
		MonoType type;
		DebuggerBackend backend;
		TargetAddress start_liveness, end_liveness;
		TargetAddress start_scope, end_scope;
		bool is_local;

		public MonoVariable (DebuggerBackend backend, string name, MonoType type,
				     bool is_local, IMethod method, VariableInfo info,
				     int start_scope, int end_scope)
		{
			this.backend = backend;
			this.name = name;
			this.type = type;
			this.is_local = is_local;
			this.info = info;

			if (start_scope != 0)
				this.start_scope = method.StartAddress + start_scope;
			else
				this.start_scope = method.MethodStartAddress;
			if (end_scope != 0)
				this.end_scope = method.StartAddress + end_scope;
			else
				this.end_scope = method.MethodEndAddress;
			if (info.BeginLiveness != 0)
				this.start_liveness = method.StartAddress + info.BeginLiveness;
			else
				this.start_liveness = method.MethodStartAddress;
			if (info.EndLiveness != 0)
				this.end_liveness = method.StartAddress + info.EndLiveness;
			else
				this.end_liveness = method.MethodEndAddress;
		}

		public DebuggerBackend Backend {
			get {
				return backend;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public ITargetType Type {
			get {
				return type;
			}
		}

		internal VariableInfo VariableInfo {
			get {
				return info;
			}
		}

		public TargetAddress StartLiveness {
			get {
				return start_liveness;
			}
		}

		public TargetAddress EndLiveness {
			get {
				return end_liveness;
			}
		}

		MonoTargetLocation GetLocation (StackFrame frame)
		{
			if (info.Mode == VariableInfo.AddressMode.Register) {
				if (frame.Level != 0)
					return null;
				else
					return new MonoRegisterLocation (
						frame, type.IsByRef, info.Index, info.Offset,
						start_liveness, end_liveness);
			} else if (info.Mode == VariableInfo.AddressMode.Stack)
				return new MonoStackLocation (
					frame, type.IsByRef, is_local, info.Offset, 0,
					start_liveness, end_liveness);
			else
				return null;
		}

		public bool IsValid (StackFrame frame)
		{
			if ((frame.TargetAddress < start_scope) || (frame.TargetAddress > end_scope))
				return false;

			MonoTargetLocation location = GetLocation (frame);

			if ((location == null) || !location.IsValid)
				return false;

			return true;
		}

		public ITargetObject GetObject (StackFrame frame)
		{
			MonoTargetLocation location = GetLocation (frame);

			if ((location == null) || !location.IsValid)
				throw new LocationInvalidException ();

			return type.GetObject (location);
		}

		public override string ToString ()
		{
			return String.Format ("MonoVariable [{0}:{1}:{2}:{3}]",
					      Name, Type, StartLiveness, EndLiveness);
		}
	}
}
