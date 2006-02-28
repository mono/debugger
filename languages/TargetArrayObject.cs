using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetArrayObject : TargetObject
	{
		public new readonly TargetArrayType Type;
		public readonly int Rank;
		protected ArrayBounds[] bounds;

		internal TargetArrayObject (TargetArrayType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
			this.Rank = type.Rank;
		}

		protected abstract void DoGetArrayBounds (Thread target);

		protected bool GetArrayBounds (Thread target)
		{
			if (bounds != null)
				return true;

			try {
				DoGetArrayBounds (target);
				return bounds != null;
			} catch (TargetException) {
				throw;
			} catch {
				return false;
			}
		}

		public int GetLowerBound (Thread target, int dimension)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			if ((dimension < 0) || (dimension >= Rank))
				throw new ArgumentException ();

			return bounds [dimension].Lower;
		}

		public int GetUpperBound (Thread target, int dimension)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			if ((dimension < 0) || (dimension >= Rank))
				throw new ArgumentException ();

			return bounds [dimension].Lower + bounds [dimension].Length;
		}

		protected int GetArrayOffset (Thread target, int[] indices)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			if (indices.Length != Rank)
				throw new ArgumentException ();

			if (Rank > 1) {
				for (int i = 0; i < Rank; i++) {
					if (indices [i] < bounds [i].Lower)
						throw new ArgumentException ();

					indices [i] -= bounds [i].Lower;

					if (indices [i] >= bounds [i].Length)
						throw new ArgumentException ();
				}
			} else if ((indices [0] < 0) || (indices [0] >= bounds [0].Length))
				throw new ArgumentException ();

			int index = indices [0];
			for (int i = 1; i < Rank; i++)
				index = index * bounds [i].Length + indices [i];

			if (Type.ElementType.IsByRef)
				return index * target.TargetInfo.TargetAddressSize;
			else if (Type.ElementType.HasFixedSize)
				return index * Type.ElementType.Size;
			else
				throw new InvalidOperationException ();
		}

		protected int GetLength (Thread target)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			int length = bounds [0].Length;
			for (int i = 1; i < Rank; i++)
				length *= bounds [i].Length;
			return length;
		}

		public abstract TargetObject GetElement (Thread target, int[] indices);

		public abstract void SetElement (Thread target, int[] indices,
						 TargetObject obj);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), Type,
					      Type.ElementType, Rank);
		}

		protected struct ArrayBounds
		{
			public readonly int Lower;
			public readonly int Length;

			public ArrayBounds (int lower, int length)
			{
				this.Lower = lower;
				this.Length = length;
			}
		}
	}
}

