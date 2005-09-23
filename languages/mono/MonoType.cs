using System;
using Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoType : MarshalByRefObject, ITargetType
	{
		protected readonly ILanguage language;
		protected readonly TargetObjectKind kind;

		protected MonoType (ILanguage language, TargetObjectKind kind)
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

		public virtual bool CheckValid (TargetLocation location)
		{
			return !location.HasAddress || !location.Address.IsNull;
		}

		public void SetObject (TargetLocation location, MonoObject obj)
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

		public abstract MonoObject GetObject (TargetLocation location);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
