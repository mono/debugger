using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoArray : MonoObject, ITargetArray
	{
		int rank;
		int length;
		int dimension;
		int base_index;
		MonoArrayBounds[] bounds;
		ITargetLocation location;
		MonoType element_type;
		new MonoArrayType type;

		public MonoArray (MonoArrayType type, int length, ITargetLocation location)
			: base (type, null)
		{
			this.type = type;
			this.length = length;
			this.location = location;
			this.element_type = type.ElementType;
		}

		public MonoArray (MonoArrayType type, int length, MonoArrayBounds[] bounds,
				  int dimension, int base_index, ITargetLocation location)
			: this (type, length, location)
		{
			this.bounds = bounds;
			this.dimension = dimension;
			this.base_index = base_index;
			this.rank = bounds.Length;
		}

		public int Count {
			get {
				if (rank == 0)
					return length;

				return bounds [dimension].Length;
			}
		}

		public int LowerBound {
			get {
				if (rank == 0)
					return 0;

				return bounds [dimension].Lower;
			}
		}

		public int UpperBound {
			get {
				if (rank == 0)
					return length;

				return bounds [dimension].Lower + bounds [dimension].Length;
			}
		}

		public ITargetObject this [int index] {
			get {
				if ((index < LowerBound) || (index >= UpperBound))
					throw new ArgumentException ();
				index -= LowerBound;

				if (dimension + 1 >= rank)
					return element_type.GetElementObject (location, base_index + index);

				for (int i = dimension + 1; i < rank; i++)
					index *= bounds [i].Length;

				return new MonoArray (type, length, bounds, dimension + 1,
						      base_index + index, location);
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), type,
					      element_type, length);
		}
	}
}
