using System;

namespace Mono.Debugger.Languages.Mono
{
	internal interface IMonoTypeInfo
	{
		new MonoType Type {
			get;
		}

		bool HasFixedSize {
			get;
		}

		int Size {
			get;
		}

		MonoObject GetObject (TargetLocation location);

		void SetObject (TargetLocation location, MonoObject obj);
	}

	internal abstract class MonoTypeInfo : MarshalByRefObject, IMonoTypeInfo
	{
		protected readonly MonoType type;
		protected readonly int size;
		protected readonly TargetAddress KlassAddress;

		protected MonoTypeInfo (MonoType type, TargetBinaryReader info)
		{
			this.type = type;

			size = info.ReadLeb128 ();
			KlassAddress = new TargetAddress (type.File.GlobalAddressDomain, info.ReadAddress ());

			type.File.MonoLanguage.AddClass (KlassAddress, type);
		}

		protected MonoTypeInfo (MonoType type, int size)
			: this (type, size, TargetAddress.Null)
		{ }

		protected MonoTypeInfo (MonoType type, int size, TargetAddress klass_address)
		{
			this.type = type;
			this.size = size;
			this.KlassAddress = klass_address;

			if (!klass_address.IsNull)
				type.File.MonoLanguage.AddClass (klass_address, type);
		}

		public MonoType Type {
			get { return type; }
		}

		public bool HasFixedSize {
			get { return type.HasFixedSize; }
		}

		public int Size {
			get { return size; }
		}

		public abstract MonoObject GetObject (TargetLocation location);

		public virtual void SetObject (TargetLocation location, MonoObject obj)
		{
			type.SetObject (location, obj);
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}]", GetType (), type);
		}
	}
}
