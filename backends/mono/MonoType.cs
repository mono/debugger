using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoType : MarshalByRefObject, ITargetType
	{
		protected readonly Type type;
		protected readonly MonoSymbolFile file;
		protected readonly TargetObjectKind kind;
		protected IMonoTypeInfo type_info;

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

		public Type TypeHandle {
			get { return type; }
		}

		public abstract bool IsByRef {
			get;
		}

		ITargetTypeInfo ITargetType.GetTypeInfo ()
		{
			return GetTypeInfo ();
		}

		public virtual IMonoTypeInfo GetTypeInfo ()
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

			type_info = DoGetTypeInfo (info);
			return type_info;
		}

		protected virtual IMonoTypeInfo CreateTypeInfo ()
		{
			return null;
		}

		protected abstract IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info);

		public virtual bool CheckValid (TargetLocation location)
		{
			return !location.HasAddress || !location.Address.IsNull;
		}

		public MonoObject GetObject (TargetLocation location)
		{
			IMonoTypeInfo tinfo = GetTypeInfo ();
			if (tinfo == null)
				return null;

			return tinfo.GetObject (location);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), Name);
		}
	}
}
