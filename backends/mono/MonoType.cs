using System;
using Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoType : MarshalByRefObject, ITargetType
	{
		protected readonly MonoSymbolFile file;
		protected readonly TargetObjectKind kind;
		protected IMonoTypeInfo type_info;

		public MonoType (MonoSymbolFile file, TargetObjectKind kind)
		{
			this.file = file;
			this.kind = kind;
		}

		public TargetObjectKind Kind {
			get { return kind; }
		}

		public MonoSymbolFile File {
			get { return file; }
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
			get {
				return file.MonoLanguage;
			}
		}

		public virtual int Size {
			get {
				IMonoTypeInfo info = GetTypeInfo ();
				if (info != null)
					return info.Size;
				else
					throw new LocationInvalidException ();
			}
		}

		public virtual IMonoTypeInfo GetTypeInfo ()
		{
			if (type_info != null)
				return type_info;

			type_info = DoGetTypeInfo ();
			return type_info;
		}

		protected abstract IMonoTypeInfo DoGetTypeInfo ();

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
				if (obj.TypeInfo.Type.IsByRef) {
					location.WriteAddress (obj.Location.Address);
					return;
				}

				throw new InvalidOperationException ();
			}

			if (GetTypeInfo () == null)
				throw new InvalidOperationException ();

			if (!type_info.HasFixedSize || !obj.TypeInfo.HasFixedSize)
				throw new InvalidOperationException ();
			if (type_info.Size != obj.TypeInfo.Size)
				throw new InvalidOperationException ();

			location.WriteBuffer (obj.RawContents);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
