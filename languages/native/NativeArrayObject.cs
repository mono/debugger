using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayObject : NativeObject, ITargetArrayObject
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

		public ITargetObject this [int[] indices] {
			get {
				if (indices.Length != 1)
					throw new ArgumentException ();

				int index = indices [0];
				int size = type.ElementType.Size;

				TargetLocation new_location = location.GetLocationAtOffset (
						    index * size, type.ElementType.IsByRef);

				return type.ElementType.GetObject (new_location);
			}

			set {
				throw new NotSupportedException ();
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
