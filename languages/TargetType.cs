using System;

namespace Mono.Debugger.Languages
{
	internal abstract class TargetType : MarshalByRefObject, ITargetType
	{
		protected readonly ILanguage language;
		protected readonly TargetObjectKind kind;

		protected TargetType (ILanguage language, TargetObjectKind kind)
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

		public ILanguage Language {
			get { return language; }
		}

		public abstract int Size {
			get;
		}

		internal void SetObject (TargetLocation location, TargetObject obj)
		{
			if (obj == null) {
				if (IsByRef) {
					location.WriteAddress (TargetAddress.Null);
					return;
				}

				throw new InvalidOperationException ();
			}

			if (IsByRef) {
				if (obj.Type.IsByRef) {
					location.WriteAddress (obj.Location.Address);
					return;
				}

				throw new InvalidOperationException ();
			}

			if (!HasFixedSize || !obj.Type.HasFixedSize)
				throw new InvalidOperationException ();
			if (Size != obj.Type.Size)
				throw new InvalidOperationException ();

			byte[] contents = obj.Location.ReadBuffer (obj.Type.Size);
			location.WriteBuffer (contents);
		}

		internal abstract TargetObject GetObject (TargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
