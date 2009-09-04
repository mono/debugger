using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetArrayObject : TargetObject
	{
		public new readonly TargetArrayType Type;
		public readonly int Rank;
		protected TargetArrayBounds bounds;

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

		public TargetArrayBounds GetArrayBounds (Thread thread)
		{
			return (TargetArrayBounds) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					GetArrayBounds (target);
					return bounds;
			});
		}

		protected int GetArrayOffset (TargetMemoryAccess target, int[] indices)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			if (indices.Length != Rank)
				throw new ArgumentException ();

			if (bounds.IsMultiDimensional) {
				for (int i = 0; i < Rank; i++) {
					if (indices [i] < bounds.LowerBounds [i])
						throw new ArgumentException ();
					if (indices [i] > bounds.UpperBounds [i])
						throw new ArgumentException ();
				}
			} else if (!bounds.IsUnbound &&
				   ((indices [0] < 0) || (indices [0] >= bounds.Length))) {
				throw new ArgumentException ();
			}

			int index = indices [0];
			for (int i = 1; i < Rank; i++) {
				int length = bounds.UpperBounds [i] - bounds.LowerBounds [i] + 1;
				index = index * length + indices [i];
			}

			return index * Type.GetElementSize (target);
		}

		protected int GetLength (TargetMemoryAccess target)
		{
			if (!GetArrayBounds (target))
				throw new LocationInvalidException ();

			if (!bounds.IsMultiDimensional)
				return bounds.Length;

			int length = 0;
			for (int i = 0; i < Rank; i++)
				length *= bounds.UpperBounds [i] - bounds.LowerBounds [i] + 1;
			return length;
		}

		public TargetObject GetElement (Thread thread, int[] indices)
		{
			return (TargetObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetElement (target, indices);
			});
		}

		internal abstract TargetObject GetElement (TargetMemoryAccess target, int[] indices);

		public void SetElement (Thread thread, int[] indices, TargetObject obj)
		{
			thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					SetElement (target, indices, obj);
					return null;
			});
		}

		internal abstract void SetElement (TargetMemoryAccess target, int[] indices,
						   TargetObject obj);

		public abstract bool HasClassObject {
			get;
		}

		public TargetClassObject GetClassObject (Thread thread)
		{
			return (TargetClassObject) thread.ThreadServant.DoTargetAccess (
				delegate (TargetMemoryAccess target) {
					return GetClassObject (target);
			});
		}

		internal abstract TargetClassObject GetClassObject (TargetMemoryAccess target);

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), Type,
					      Type.ElementType, Rank);
		}
	}
}

