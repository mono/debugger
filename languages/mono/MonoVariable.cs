using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoVariable : TargetVariable
	{
		VariableInfo info;
		string name;
		TargetType type;
		ProcessServant process;
		TargetAddress start_liveness, end_liveness;
		TargetAddress start_scope, end_scope;
		bool has_liveness_info, is_byref;

		public MonoVariable (ProcessServant process, string name, TargetType type,
				     bool is_local, bool is_byref, Method method,
				     VariableInfo info, int start_scope_offset,
				     int end_scope_offset)
			: this (process, name, type, is_local, is_byref, method, info)
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

		public MonoVariable (ProcessServant process, string name, TargetType type,
				     bool is_local, bool is_byref, Method method,
				     VariableInfo info)
		{
			this.process = process;
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

		public override string Name {
			get { return name; }
		}

		public override TargetType Type {
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
			Register register = frame.Registers [info.Index];
			if (info.Mode == VariableInfo.AddressMode.Register)
				return new MonoVariableLocation (
					frame.Thread, false, register, info.Offset, is_byref);
			else if (info.Mode == VariableInfo.AddressMode.RegOffset)
				return new MonoVariableLocation (
					frame.Thread, true, register, info.Offset, is_byref);
			else
				return null;
		}

		public override bool IsAlive (TargetAddress address)
		{
			return (address >= start_liveness) && (address <= end_liveness);
		}

		public override string PrintLocation (StackFrame frame)
		{
			TargetLocation location = GetLocation (frame);
			if (location == null)
				return null;

			return location.Print ();
		}

		public override TargetObject GetObject (StackFrame frame)
		{
			TargetLocation location = GetLocation (frame);

			if (location == null)
				throw new LocationInvalidException ();

			if (location.HasAddress && location.Address.IsNull)
				return process.MonoLanguage.CreateNullObject (
					frame.Thread, type);

			return type.GetObject (location);
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override void SetObject (StackFrame frame, TargetObject obj)
		{
			TargetLocation location = GetLocation (frame);

			if (location == null)
				throw new LocationInvalidException ();

			type.SetObject (frame.Thread, location, (TargetObject) obj);
		}

		public override string ToString ()
		{
			return String.Format ("MonoVariable [{0}:{1}:{2}:{3}]",
					      Name, Type, StartLiveness, EndLiveness);
		}
	}
}
