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
		}

		public IDebuggerBackend Backend {
			get {
				return backend;
			}
		}

		public VariableInfo VariableInfo {
			get {
				return info;
			}
		}

		public TargetAddress StartScope {
			get {
				return start_scope;
			}
		}

		public TargetAddress EndScope {
			get {
				return end_scope;
			}
		}

		public override ITargetObject GetObject (IStackFrame frame)
		{
			return new MonoObject (frame, this, is_local);
		}
	}
}
