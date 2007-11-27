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

		protected override void DoGetArrayBounds (TargetMemoryAccess target)
		{ }

		public override TargetObject GetElement (TargetMemoryAccess target, int[] indices)
		{
			if (indices.Length != 1)
				throw new ArgumentException ();

			int index = indices [0];
			int size = Type.ElementType.Size;

			TargetLocation new_location = Location.GetLocationAtOffset (index * size);
			if (Type.ElementType.IsByRef)
				new_location = new_location.GetDereferencedLocation ();

			return Type.ElementType.GetObject (target, new_location);
		}

		public override void SetElement (TargetMemoryAccess target, int[] indices,
						 TargetObject obj)
		{
			throw new NotSupportedException ();
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal override string Print (TargetMemoryAccess target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}

		public override bool HasClassObject {
			get { return false; }
		}

		public override TargetClassObject GetClassObject (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}
	}
}
