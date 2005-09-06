using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoVariable : MarshalByRefObject, IVariable
	{
		VariableInfo info;
		string name;
		MonoType type;
		DebuggerBackend backend;
		TargetAddress start_liveness, end_liveness;
		TargetAddress start_scope, end_scope;
		bool has_liveness_info, is_byref;

		public MonoVariable (DebuggerBackend backend, string name, MonoType type,
				     bool is_local, bool is_byref, IMethod method,
				     VariableInfo info, int start_scope_offset,
				     int end_scope_offset)
			: this (backend, name, type, is_local, is_byref, method, info)
		{
			if (is_local) {
				start_scope = method.StartAddress + start_scope_offset;
				end_scope = method.StartAddress + end_scope_offset;
			} else if (method.HasMethodBounds) {
				start_scope = method.MethodStartAddress;
				end_scope = method.MethodEndAddress;
			} else {
				start_scope = method.StartAddress;
				end_scope = method.EndAddress;
			}

			if (has_liveness_info) {
				if (start_liveness < start_scope)
					start_liveness = start_scope;
				if (end_liveness > end_scope)
					end_liveness = end_scope;
			} else {
				start_liveness = start_scope;
				end_liveness = end_scope;
				has_liveness_info = true;
			}
		}

		public MonoVariable (DebuggerBackend backend, string name, MonoType type,
				     bool is_local, bool is_byref, IMethod method,
				     VariableInfo info)
		{
			this.backend = backend;
			this.name = name;
			this.type = type;
			this.info = info;
			this.is_byref = is_byref;

			if (info.HasLivenessInfo) {
				start_liveness = method.StartAddress + info.BeginLiveness;
				end_liveness = method.StartAddress + info.EndLiveness;
				has_liveness_info = true;
			} else {
				start_liveness = method.MethodStartAddress;
				end_liveness = method.MethodEndAddress;
				has_liveness_info = false;
			}
		}

		public DebuggerBackend Backend {
			get { return backend; }
		}

		public string Name {
			get { return name; }
		}

		public ITargetType Type {
			get { return type; }
		}

		public TargetAddress StartLiveness {
			get { return start_liveness; }
		}

		public TargetAddress EndLiveness {
			get { return end_liveness; }
		}

		public TargetLocation GetLocation (StackFrame frame)
		{
			if (info.Mode == VariableInfo.AddressMode.Register)
				return new MonoVariableLocation (
					frame, false, info.Index, info.Offset, is_byref);
			else if (info.Mode == VariableInfo.AddressMode.RegOffset)
				return new MonoVariableLocation (
					frame, true, info.Index, info.Offset, is_byref);
			else
				return null;
		}

		public bool CheckValid (StackFrame frame)
		{
			if (!IsAlive (frame.TargetAddress))
				return false;

			TargetLocation location = GetLocation (frame);

			if (location == null)
				return false;

			return type.CheckValid (location);
		}

		public bool IsAlive (TargetAddress address)
		{
			return (address >= start_liveness) && (address <= end_liveness);
		}

		public ITargetObject GetObject (StackFrame frame)
		{
			TargetLocation location = GetLocation (frame);

			if (location == null)
				throw new LocationInvalidException ();

			MonoTypeInfo tinfo = type.GetTypeInfo ();
			if (tinfo == null)
				return null;

			return tinfo.GetObject (location);
		}

		public bool CanWrite {
			get { return true; }
		}

		public void SetObject (StackFrame frame, ITargetObject obj)
		{
			if (obj.TypeInfo.Type != Type)
				throw new InvalidOperationException ();

			MonoObject var_object = (MonoObject)GetObject (frame);
			if (var_object == null)
				return;

			var_object.SetObject ((MonoObject) obj);
		}

		public override string ToString ()
		{
			return String.Format ("MonoVariable [{0}:{1}:{2}:{3}]",
					      Name, Type, StartLiveness, EndLiveness);
		}
	}
}
