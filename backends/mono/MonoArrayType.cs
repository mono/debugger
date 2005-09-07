using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;

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

	internal class MonoArrayType : MonoType, ITargetArrayType
	{
		public readonly int Rank;
		protected readonly int Dimension;

		protected MonoType element_type;

		public MonoArrayType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Array, type)
		{
			this.Rank = type.GetArrayRank ();
			this.Dimension = 0;

			element_type = file.MonoLanguage.LookupMonoType (type.GetElementType ());
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasFixedSize {
			get { return false; }
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

		protected override IMonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			IMonoTypeInfo element_info = element_type.GetTypeInfo ();
			if (element_info == null)
				return null;

			return new MonoArrayTypeInfo (this, element_info);
		}
	}
}
