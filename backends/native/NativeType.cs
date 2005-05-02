using System;

namespace Mono.Debugger.Languages.Native
{
	internal abstract class NativeType : ITargetType, ITargetTypeInfo
	{
		protected readonly TargetObjectKind kind;
		protected object type_handle;

		string name;
		bool has_fixed_size;
		int size;

		protected NativeType (string name, TargetObjectKind kind, int size)
			: this (name, kind, size, true)
		{ }

		protected NativeType (string name, TargetObjectKind kind, int size, bool has_fixed_size)
		{
			this.name = name;
			this.size = size;
			this.kind = kind;
			this.has_fixed_size = has_fixed_size;
		}

		public static readonly NativeType VoidType = new NativeOpaqueType ("void", 0);

		public TargetObjectKind Kind {
			get {
				return kind;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public object TypeHandle {
			get {
				return type_handle;
			}
		}

		public abstract bool IsByRef {
			get;
		}

		public virtual bool HasFixedSize {
			get {
				return has_fixed_size;
			}
		}

		public virtual int Size {
			get {
				return size;
			}
		}

		public abstract NativeObject GetObject (TargetLocation location);

		ITargetObject ITargetTypeInfo.GetObject (TargetLocation location)
		{
			return GetObject (location);
		}

		ITargetType ITargetTypeInfo.Type {
			get { return this; }
		}

		ITargetTypeInfo ITargetType.GetTypeInfo ()
		{
			return this;
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}:{4}]", GetType (),
					      Name, IsByRef, HasFixedSize, Size);
		}
	}
}
