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
		protected MonoArrayType subarray_type;

		public MonoArrayType (MonoSymbolFile file, Type type)
			: base (file, TargetObjectKind.Array, type)
		{
			this.Rank = type.GetArrayRank ();
			this.Dimension = 0;

			element_type = file.LookupType (type.GetElementType ());

			if (Dimension + 1 < Rank)
				subarray_type = new MonoArrayType (this);
		}

		private MonoArrayType (MonoArrayType type)
			: base (type.File, TargetObjectKind.Array,
				C.MonoDebuggerSupport.MakeArrayType (type.element_type.Type, type.Rank - 1))
		{
			Rank = type.Rank;
			Dimension = type.Dimension + 1;
			element_type = type.element_type;

			if (Dimension + 1 < Rank)
				subarray_type = new MonoArrayType (this);
		}

		public override bool IsByRef {
			get { return true; }
		}

		internal MonoType ElementType {
			get { return element_type; }
		}

		internal MonoArrayType SubArrayType {
			get { return subarray_type; }
		}

		ITargetType ITargetArrayType.ElementType {
			get {
				return ElementType;
			}
		}

		protected override MonoTypeInfo DoResolve (TargetBinaryReader info)
		{
			MonoTypeInfo element_info = element_type.Resolve ();
			if (element_info == null)
				return null;

			return new MonoArrayTypeInfo (this, element_info);
		}
	}
}
