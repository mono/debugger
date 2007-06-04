using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeTypeAlias : TargetType
	{
		string name;

		public NativeTypeAlias (Language language, string name, string target_name)
			: base (language, TargetObjectKind.Alias)
		{
			this.target_name = target_name;
			this.name = name;
		}

		public NativeTypeAlias (Language language, string name, string target_name,
					TargetType target)
			: this (language, name, target_name)
		{
			this.target_type = target;
		}

		string target_name;
		TargetType target_type;

		public override string Name {
			get { return name; }
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
				return false;
			}
		}

		public string TargetName {
			get { return target_name; }
		}

		public TargetType TargetType {
			get { return target_type; }
			set { target_type = value; }
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			if (target_type == null)
				target_type = language.LookupType (target_name);

			if (target_type == null)
				return null;

			TargetObject obj = target_type.GetObject (location);
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
