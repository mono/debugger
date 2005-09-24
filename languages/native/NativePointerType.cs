using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativePointerType : TargetType, ITargetPointerType
	{
		string name;
		int size;

		public NativePointerType (ILanguage language, string name, int size)
			: base (language, TargetObjectKind.Pointer)
		{
			this.name = name;
			this.size = size;
		}

		public NativePointerType (ILanguage language, string name,
					  TargetType target_type, int size)
			: this (language, name, size)
		{
			this.target_type = target_type;
		}

		TargetType target_type;

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public bool IsTypesafe {
			get {
				return false;
			}
		}

		public bool HasStaticType {
			get {
				return target_type != null;
			}
		}

		public bool IsArray {
			get {
				return true;
			}
		}

		public TargetType StaticType {
			get {
				if (target_type == null)
					throw new InvalidOperationException ();

				return target_type;
			}
		}

		ITargetType ITargetPointerType.StaticType {
			get {
				return StaticType;
			}
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new NativePointerObject (this, location);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (),
					      Name, Size, target_type);
		}
	}
}
