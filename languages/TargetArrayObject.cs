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

		protected abstract void DoGetArrayBounds (TargetMemoryAccess target);

		protected bool GetArrayBounds (TargetMemoryAccess target)
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

		protected bool GetArrayBounds (Thread thread)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					return GetArrayBounds (target);
			}, null);
			return bounds != null;
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

		protected int GetArrayOffset (TargetMemoryAccess target, int[] indices)
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
				return index * target.TargetMemoryInfo.TargetAddressSize;
			else if (Type.ElementType.HasFixedSize)
				return index * Type.ElementType.Size;
			else
				throw new InvalidOperationException ();
		}

		protected int GetLength (TargetMemoryAccess target)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			int length = bounds [0].Length;
			for (int i = 1; i < Rank; i++)
				length *= bounds [i].Length;
			return length;
		}

		public TargetObject GetElement (Thread thread, int[] indices)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					return GetElement (target, indices);
			}, null);
		}

		internal abstract TargetObject GetElement (TargetMemoryAccess target, int[] indices);

		public void SetElement (Thread thread, int[] indices, TargetObject obj)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					SetElement (target, indices, obj);
					return null;
			}, null);
		}

		internal abstract void SetElement (TargetMemoryAccess target, int[] indices,
						   TargetObject obj);

		public abstract bool HasClassObject {
			get;
		}

		public TargetClassObject GetClassObject (Thread thread)
		{
			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target, object user_data) {
					return GetClassObject (target);
			}, null);
		}

		internal abstract TargetClassObject GetClassObject (TargetMemoryAccess target);

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

