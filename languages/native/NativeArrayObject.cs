using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayObject : TargetObject, ITargetArrayObject
	{
		int lower_bound;
		int upper_bound;
		new NativeArrayType type;

		public NativeArrayObject (NativeArrayType type, TargetLocation location,
					  int lower_bound, int upper_bound)
			: base (type, location)
		{
			this.type = type;
			this.lower_bound = lower_bound;
			this.upper_bound = upper_bound;
		}

		ITargetArrayType ITargetArrayObject.Type {
			get { return type; }
		}

		public int GetLowerBound (int dimension)
		{
			if (dimension != 0)
				throw new ArgumentException ();

			return lower_bound;
		}

		public int GetUpperBound (int dimension)
		{
			if (dimension != 0)
				throw new ArgumentException ();

			return upper_bound;
		}

		public ITargetObject GetElement (ITargetAccess target, int[] indices)
		{
			if (indices.Length != 1)
				throw new ArgumentException ();

			int index = indices [0];
			int size = type.ElementType.Size;

			TargetLocation new_location = location.GetLocationAtOffset (index * size);
			if (type.ElementType.IsByRef)
				new_location = new_location.GetDereferencedLocation (target);

			return type.ElementType.GetObject (new_location);
		}

		public void SetElement (ITargetAccess target, int[] indices, ITargetObject obj)
		{
			throw new NotSupportedException ();
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
