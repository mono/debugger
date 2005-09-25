using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayObject : TargetArrayObject
	{
		public NativeArrayObject (NativeArrayType type, TargetLocation location,
					  int lower_bound, int upper_bound)
			: base (type, location)
		{
			bounds = new ArrayBounds [1];
			bounds [0] = new ArrayBounds (lower_bound, upper_bound - lower_bound);
		}

		protected override void DoGetArrayBounds (ITargetAccess target)
		{ }

		public override TargetObject GetElement (ITargetAccess target, int[] indices)
		{
			if (indices.Length != 1)
				throw new ArgumentException ();

			int index = indices [0];
			int size = Type.ElementType.Size;

			TargetLocation new_location = Location.GetLocationAtOffset (index * size);
			if (Type.ElementType.IsByRef)
				new_location = new_location.GetDereferencedLocation (target);

			return Type.ElementType.GetObject (new_location);
		}

		public override void SetElement (ITargetAccess target, int[] indices,
						 TargetObject obj)
		{
			throw new NotSupportedException ();
		}

		internal override long GetDynamicSize (TargetBlob blob, TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
