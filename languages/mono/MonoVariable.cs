using System;

using Mono.Debugger.Backend;
using Mono.Debugger.Backend.Mono;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoVariable : TargetVariable
	{
		VariableInfo info;
		string name;
		TargetType type;
		TargetAddress start_liveness, end_liveness;
		TargetAddress start_scope, end_scope;
		bool has_liveness_info, is_byref;

		public MonoVariable (string name, TargetType type, bool is_local, bool is_byref,
				     Method method, VariableInfo info, int start_scope_offset,
				     int end_scope_offset)
			: this (name, type, is_local, is_byref, method, info)
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

		public MonoVariable (string name, TargetType type, bool is_local, bool is_byref,
				     Method method, VariableInfo info)
		{
			this.name = name;
			this.type = type;
			this.info = info;
			this.is_byref = is_byref;

			start_scope = method.StartAddress;
			end_scope = method.EndAddress;

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

		public VariableInfo VariableInfo {
			get { return info; }
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
			return (TargetLocation) frame.Thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					return GetLocation (frame, target);
			});
		}

		internal TargetLocation GetLocation (StackFrame frame, TargetMemoryAccess target)
		{
			Register register = frame.Registers [info.Index];
			if (info.Mode == VariableInfo.AddressMode.Register)
				return MonoVariableLocation.Create (
					target, false, register, info.Offset, is_byref);
			else if (info.Mode == VariableInfo.AddressMode.RegOffset)
				return MonoVariableLocation.Create (
					target, true, register, info.Offset, is_byref);
			else
				return null;
		}

		public override bool IsInScope (TargetAddress address)
		{
			return (address >= start_scope) && (address <= end_scope);
		}

		public override bool IsAlive (TargetAddress address)
		{
			if (info.Mode == VariableInfo.AddressMode.Dead)
				return false;
			return (address >= start_liveness) && (address <= end_liveness) &&
				(address >= start_scope) && (address <= end_scope);
		}

		public override string PrintLocation (StackFrame frame)
		{
			TargetLocation location = GetLocation (frame);
			if (location == null)
				return null;

			return location.Print ();
		}

		internal override TargetObject GetObject (StackFrame frame,
							  TargetMemoryAccess target)
		{
			TargetLocation location = GetLocation (frame, target);

			if (location == null)
				throw new LocationInvalidException ();

			if (location.HasAddress && location.GetAddress (target).IsNull) {
				TargetLocation null_loc = new AbsoluteTargetLocation (TargetAddress.Null);
				return new TargetNullObject (type);
			}

			return type.GetObject (target, location);
		}

		public override bool CanWrite {
			get { return true; }
		}

		public override void SetObject (StackFrame frame, TargetObject obj)
		{
			frame.Thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target)  {
					SetObject (frame, target, obj);
					return null;
			});
		}

		internal void SetObject (StackFrame frame, TargetMemoryAccess target,
					 TargetObject obj)
		{
			TargetLocation location = GetLocation (frame, target);

			if (location == null)
				throw new LocationInvalidException ();

			type.SetObject (target, location, (TargetObject) obj);
		}

		public override string ToString ()
		{
			return String.Format ("MonoVariable [{0}:{1}:{2}:{3}]",
					      Name, Type, StartLiveness, EndLiveness);
		}
	}
}
