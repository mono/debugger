using System;
using System.Text;

namespace Mono.Debugger.Languages.CSharp
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
		internal readonly int Rank;
		internal readonly int Dimension;
		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;
		internal readonly int BoundsOffset;
		internal readonly int BoundsSize;
		internal readonly int BoundsLowerOffset;
		internal readonly int BoundsLowerSize;
		internal readonly int BoundsLengthOffset;
		internal readonly int BoundsLengthSize;
		protected readonly MonoType element_type;
		MonoArrayType subarray_type;

		public MonoArrayType (Type type, int size, ITargetMemoryReader info, bool is_multi,
				      MonoSymbolFileTable table)
			: base (type, size, false)
		{
			LengthOffset = info.ReadByte ();
			LengthSize = info.ReadByte ();
			DataOffset = info.ReadByte ();

			if (is_multi) {
				Rank = info.ReadByte ();
				BoundsOffset = info.ReadByte ();
				BoundsSize = info.ReadByte ();
				BoundsLowerOffset = info.ReadByte ();
				BoundsLowerSize = info.ReadByte ();
				BoundsLengthOffset = info.ReadByte ();
				BoundsLengthSize = info.ReadByte ();
			}

			TargetAddress element_type_info = info.ReadAddress ();
			element_type = GetType (type.GetElementType (), info.TargetMemoryAccess,
						element_type_info, table);
			setup ();
		}

		static Type get_subarray_type (Type type)
		{
			Type elt_type = type.GetElementType ();
			StringBuilder sb = new StringBuilder (elt_type.FullName);
			sb.Append ("[");
			for (int i = 2; i < type.GetArrayRank (); i++)
				sb.Append (",");
			sb.Append ("]");
			return Type.GetType (sb.ToString ());
		}

		private MonoArrayType (MonoArrayType type)
			: base (get_subarray_type (type.type), type.Size, false)
		{
			Rank = type.Rank;
			Dimension = type.Dimension + 1;
			LengthOffset = type.LengthOffset;
			LengthSize = type.LengthSize;
			DataOffset = type.DataOffset;
			BoundsOffset = type.BoundsOffset;
			BoundsSize = type.BoundsSize;
			BoundsLowerOffset = type.BoundsLowerOffset;
			BoundsLowerSize = type.BoundsLowerSize;
			BoundsLengthOffset = type.BoundsLengthOffset;
			BoundsLengthSize = type.BoundsLengthSize;
			element_type = type.element_type;
			setup ();
		}

		void setup ()
		{
			if (Dimension + 1 < Rank)
				subarray_type = new MonoArrayType (this);
		}

		public static bool Supports (Type type, TargetBinaryReader info)
		{
			return type.IsArray;
		}

		public override bool HasFixedSize {
			get {
				return false;
			}
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public override bool HasObject {
			get {
				if (Dimension + 1 < Rank)
					return true;

				return element_type.HasObject &&
					(element_type.IsByRef || element_type.HasFixedSize);
			}
		}

		bool ITargetType.HasObject {
			get {
				return HasObject;
			}
		}

		internal MonoType ElementType {
			get {
				if (Dimension + 1 >= Rank)
					return element_type;

				return subarray_type;
			}
		}

		internal MonoArrayType SubArrayType {
			get {
				return subarray_type;
			}
		}

		ITargetType ITargetArrayType.ElementType {
			get {
				return ElementType;
			}
		}

		public override MonoObject GetObject (ITargetLocation location)
		{
			if (!HasObject)
				throw new InvalidOperationException ();

			return new MonoArrayObject (this, location);
		}
	}
}
