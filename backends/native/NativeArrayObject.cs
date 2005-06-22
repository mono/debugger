using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayObject : NativeObject, ITargetArrayObject
	{
		int lower_bound;
		int upper_bound;
		NativeArrayType type;

		public NativeArrayObject (NativeArrayType type, TargetLocation location, int lower_bound, int upper_bound)
			: base (type, location)
		{
			this.type = type;
			this.lower_bound = lower_bound;
			this.upper_bound = upper_bound;
		}

		public int LowerBound {
			get {
				return lower_bound;
			}
		}

		public int UpperBound {
			get {
				return upper_bound;
			}
		}

		public ITargetObject this [int index] {
			get {
				int size = type.ElementType.Size;

				TargetLocation new_location = location.GetLocationAtOffset (
						    index * size, type.ElementType.IsByRef);

				return type.ElementType.GetObject (new_location);
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}
	}
}
