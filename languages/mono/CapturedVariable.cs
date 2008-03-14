using System;
using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal class ScopeInfo : DebuggerMarshalByRefObject
	{
		public readonly int ID;
		public readonly CapturedVariable Parent;

		TargetVariable var;

		bool resolved;
		MonoClassInfo klass;
		TargetStructType type;
		TargetFieldInfo[] fields;

		public ScopeInfo (int id, TargetVariable var, TargetStructType type)
		{
			this.ID = id;
			this.var = var;
			this.type = type;
		}

		public ScopeInfo (int id, CapturedVariable parent)
		{
			this.ID = id;
			this.Parent = parent;
		}

		public bool Resolve (TargetMemoryAccess target)
		{
			if (resolved)
				return true;

			try {
				if (!DoResolve (target))
					return false;
			} catch {
				return false;
			}

			resolved = true;
			return true;
		}

		protected bool DoResolve (TargetMemoryAccess target)
		{
			if (Parent != null) {
				if (!Parent.Resolve (target))
					return false;

				type = (TargetStructType) Parent.Type;
			}

			klass = (MonoClassInfo) type.GetClass (target);
			if (klass == null)
				return false;

			fields = klass.GetFields (target);
			return true;
		}

		public TargetFieldInfo GetField (TargetMemoryAccess target, string name)
		{
			if (!Resolve (target))
				return null;

			foreach (TargetFieldInfo field in fields) {
				if (field.Name == name)
					return field;
			}

			return null;
		}

		public TargetObject GetVariable (StackFrame frame, TargetMemoryAccess target,
						 string name)
		{
			TargetObject obj = GetObject (frame, target);
			if ((obj == null) || (obj is MonoNullObject))
				return null;

			TargetStructObject sobj = (TargetStructObject) obj;
			foreach (TargetFieldInfo field in fields) {
				if (field.Name != name)
					continue;

				return klass.GetInstanceField (target, sobj, field);
			}

			return null;
		}

		public TargetObject GetObject (StackFrame frame, TargetMemoryAccess target)
		{
			if (!Resolve (target))
				return null;

			if (Parent != null) {
				return Parent.GetObject (frame, target);
			} else {
				return var.GetObject (frame, target);
			}
		}

		public override string ToString ()
		{
			return String.Format ("ScopeInfo ({0}:{1}:{2})", ID, Parent, type);
		}
	}

	internal class CapturedVariable : TargetVariable
	{
		ScopeInfo scope;
		string name;
		string field_name;

		TargetType type;
		TargetFieldInfo field;
		TargetClass klass;

		bool resolved;

		TargetAddress start_liveness, end_liveness;
		TargetAddress start_scope, end_scope;

		public CapturedVariable (ScopeInfo scope, Method method, string name,
					 string field_name)
		{
			this.scope = scope;
			this.name = name;
			this.field_name = field_name;

			start_scope = method.StartAddress;
			end_scope = method.EndAddress;

			start_liveness = method.MethodStartAddress;
			end_liveness = method.MethodEndAddress;
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
			TargetObject obj = GetObject (frame, target);
			return obj.Location;
		}

		public override bool IsInScope (TargetAddress address)
		{
			return (address >= start_scope) && (address <= end_scope);
		}

		public override bool IsAlive (TargetAddress address)
		{
			return (address >= start_liveness) && (address <= end_liveness);
		}

		public bool Resolve (TargetMemoryAccess target)
		{
			if (resolved)
				return true;

			try {
				DoResolve (target);
			} catch {
				return false;
			}

			resolved = true;
			return true;
		}

		protected bool DoResolve (TargetMemoryAccess target)
		{
			field = scope.GetField (target, field_name);
			if (field == null)
				return false;

			type = field.Type;
			return true;
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
			if (!Resolve (target))
				return null;

			return scope.GetVariable (frame, target, field_name);
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
			return String.Format ("CapturedVariable [{0}:{1}:{2}:{3}]",
					      Name, Type, StartLiveness, EndLiveness);
		}
	}
}
