using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetType : MarshalByRefObject
	{
		protected readonly Language language;
		protected readonly TargetObjectKind kind;

		protected TargetType (Language language, TargetObjectKind kind)
		{
			this.language = language;
			this.kind = kind;
		}

		public TargetObjectKind Kind {
			get { return kind; }
		}

		public abstract string Name {
			get;
		}

		public abstract bool IsByRef {
			get;
		}

		public abstract bool HasFixedSize {
			get;
		}

		public Language Language {
			get { return language; }
		}

		public abstract int Size {
			get;
		}

		internal void SetObject (ITargetAccess target, TargetLocation location,
					 TargetObject obj)
		{
			if (obj == null) {
				if (IsByRef) {
					location.WriteAddress (target, TargetAddress.Null);
					return;
				}

				throw new InvalidOperationException ();
			}

			if (IsByRef) {
				if (obj.Type.IsByRef) {
					location.WriteAddress (target, obj.Location.Address);
					return;
				}

				throw new InvalidOperationException ();
			}

			if (!HasFixedSize || !obj.Type.HasFixedSize)
				throw new InvalidOperationException ();
			if (Size != obj.Type.Size)
				throw new InvalidOperationException ();

			byte[] contents = obj.Location.ReadBuffer (target, obj.Type.Size);
			location.WriteBuffer (target, contents);
		}

		internal abstract TargetObject GetObject (TargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
