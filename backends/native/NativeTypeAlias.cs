using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeTypeAlias : NativeType, ITargetTypeAlias
	{
		public NativeTypeAlias (string name, string target_name)
			: base (name, TargetObjectKind.Alias, 0)
		{
			this.target_name = target_name;
		}

		public NativeTypeAlias (string name, string target_name, ITargetType target)
			: this (name, target_name)
		{
			this.target_type = target_type;
		}

		string target_name;
		NativeType target_type;

		public override bool IsByRef {
			get {
				return false;
			}
		}

		public string TargetName {
			get { return target_name; }
		}

		public NativeType TargetType {
			get { return target_type; }
			set { target_type = value; }
		}

		ITargetType ITargetTypeAlias.TargetType {
			get { return target_type; }
		}

		public override NativeObject GetObject (TargetLocation location)
		{
			if (target_type == null)
				return null;

			return target_type.GetObject (location);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (),
					      Name, TargetName, TargetType);
		}
	}
}
