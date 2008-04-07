using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeTypeAlias : TargetTypeAlias
	{
		public NativeTypeAlias (Language language, string name, string target_name)
			: base (language, name, target_name)
		{ }

		public NativeTypeAlias (Language language, string name, string target_name,
					TargetType target)
			: this (language, name, target_name)
		{
			this.target_type = target;
		}

		TargetType target_type;

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override int Size {
			get {
				if (target_type != null)
					return target_type.Size;

				return 0;
			}
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get {
				if (target_type != null)
					return target_type.IsByRef;

				return false;
			}
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		public override TargetType TargetType {
			get { return target_type; }
		}

		internal void SetTargetType (TargetType type)
		{
			this.target_type = type;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			if (target_type == null)
				target_type = language.LookupType (TargetName);

			if (target_type == null)
				return null;

			TargetObject obj = target_type.GetObject (target, location);
			if (obj == null)
				return null;

			obj.SetTypeName (Name);
			return obj;
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (),
					      Name, TargetName, TargetType);
		}
	}
}
