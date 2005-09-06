using System;
using Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoType : MarshalByRefObject, ITargetType
	{
		protected readonly Cecil.ITypeReference typeref;
		protected readonly MonoSymbolFile file;
		protected readonly TargetObjectKind kind;
		protected MonoTypeInfo type_info;

		public MonoType (MonoSymbolFile file, TargetObjectKind kind, Cecil.ITypeReference typeref)
		{
			this.file = file;
			this.typeref = typeref;
			this.kind = kind;
		}

		public Cecil.ITypeReference Type {
			get { return typeref; }
		}

		public TargetObjectKind Kind {
			get { return kind; }
		}

		public MonoSymbolFile File {
			get { return file; }
		}

		public virtual string Name {
			get { return typeref.FullName; }
		}

		public Cecil.ITypeReference TypeHandle {
			get { return typeref; }
		}

		public abstract bool IsByRef {
			get;
		}

		ITargetTypeInfo ITargetType.GetTypeInfo ()
		{
			return GetTypeInfo ();
		}

		public virtual MonoTypeInfo GetTypeInfo ()
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

		protected virtual MonoTypeInfo CreateTypeInfo ()
		{
			return null;
		}

		protected abstract MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info);

		public virtual bool CheckValid (TargetLocation location)
		{
			return !location.HasAddress || !location.Address.IsNull;
		}

		public MonoObject GetObject (TargetLocation location)
		{
			MonoTypeInfo tinfo = GetTypeInfo ();
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
