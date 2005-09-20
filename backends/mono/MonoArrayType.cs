using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal struct MonoArrayBounds
	{
		public readonly int Lower;
		public readonly int Length;

		public MonoArrayBounds (int lower, int length)
		{
			this.Lower = lower;
			this.Length = length;
		}
	}

	internal class MonoArrayType : MonoType, IMonoTypeInfo, ITargetArrayType
	{
		public readonly int Rank;

		readonly MonoType element_type;
		readonly string full_name;

		public MonoArrayType (MonoSymbolFile file, Cecil.IArrayType type)
			: base (file, TargetObjectKind.Array)
		{
			this.Rank = type.Rank;

			type_info = this;
			element_type = file.MonoLanguage.LookupMonoType (type.ElementType);
			full_name = compute_fullname ();
		}

		public MonoArrayType (MonoType element_type, int rank)
			: base (element_type.File, TargetObjectKind.Array)
		{
			this.element_type = element_type;
			this.Rank = rank;

			type_info = this;
			full_name = compute_fullname ();
		}

		string compute_fullname ()
		{
			string rank_specifier;
			if (Rank == 1)
				rank_specifier = "[]";
			else
				rank_specifier = "[" + new String (',', Rank-1) + "]";

			return element_type.Name + rank_specifier;
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 4 * file.TargetInfo.TargetAddressSize; }
		}

		public override string Name {
			get { return full_name; }
		}

		internal MonoType ElementType {
			get { return element_type; }
		}

		int ITargetArrayType.Rank {
			get { return Rank; }
		}

		ITargetType ITargetArrayType.ElementType {
			get { return ElementType; }
		}

		protected override IMonoTypeInfo DoGetTypeInfo ()
		{
			throw new InvalidOperationException ();
		}

		MonoType IMonoTypeInfo.Type {
			get { return this; }
		}

		public virtual MonoObject GetObject (TargetLocation location)
		{
			return new MonoArrayObject (this, location);
		}
	}
}
