using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal abstract class NativeType : ITargetType
	{
		protected readonly TargetObjectKind kind;

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

		public virtual object TypeHandle {
			get {
				return null;
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

		public abstract NativeObject GetObject (MonoTargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (),
					      IsByRef, HasFixedSize, Size);
		}
	}
}
