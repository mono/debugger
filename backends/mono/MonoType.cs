using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoType : ITargetType
	{
		protected readonly Type type;
		protected readonly MonoSymbolFile file;
		protected readonly TargetObjectKind kind;
		protected MonoTypeInfo type_info;

		public MonoType (MonoSymbolFile file, TargetObjectKind kind, Type type)
		{
			this.file = file;
			this.type = type;
			this.kind = kind;
		}

		public Type Type {
			get { return type; }
		}

		public TargetObjectKind Kind {
			get { return kind; }
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public virtual string Name {
			get { return type.FullName; }
		}

		object ITargetType.TypeHandle {
			get { return type; }
		}

		public Type TypeHandle {
			get { return type; }
		}

		public abstract bool IsByRef {
			get;
		}

		ITargetTypeInfo ITargetType.Resolve ()
		{
			return Resolve ();
		}

		public virtual MonoTypeInfo Resolve ()
		{
			if (type_info != null)
				return type_info;

			type_info = CreateTypeInfo ();
			if (type_info != null)
				return type_info;

			TargetBinaryReader info = file.GetTypeInfo (this);
			if (info == null)
				return null;

			info.Position = 8;
			info.ReadLeb128 ();
			info.ReadLeb128 ();

			type_info = DoResolve (info);
			return type_info;
		}

		protected virtual MonoTypeInfo CreateTypeInfo ()
		{
			return null;
		}

		protected abstract MonoTypeInfo DoResolve (TargetBinaryReader info);

		public virtual bool CheckValid (TargetLocation location)
		{
			return !location.HasAddress || !location.Address.IsNull;
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
