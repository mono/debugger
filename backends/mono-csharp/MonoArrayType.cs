using System;

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

	internal class MonoArrayType : MonoType
	{
		internal readonly int Rank;
		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;
		internal readonly int BoundsOffset;
		internal readonly int BoundsSize;
		internal readonly int BoundsLowerOffset;
		internal readonly int BoundsLowerSize;
		internal readonly int BoundsLengthOffset;
		internal readonly int BoundsLengthSize;
		MonoType element_type;

		public MonoArrayType (Type type, int size, ITargetMemoryReader info, bool is_multi)
			: base (type, size, false, info)
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
						element_type_info);
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
				return element_type.HasObject &&
					(element_type.IsByRef || element_type.HasFixedSize);
			}
		}

		internal MonoType ElementType {
			get {
				return element_type;
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
