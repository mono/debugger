using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoArray : MonoObject, ITargetArray
	{
		int length;
		ITargetLocation location;
		MonoType element_type;

		public MonoArray (MonoArrayType type, int length, ITargetLocation location)
			: base (type, null)
		{
			this.length = length;
			this.location = location;
			this.element_type = type.ElementType;
		}

		public int Count {
			get {
				return length;
			}
		}

		public int LowerBound {
			get {
				return 0;
			}
		}

		public int UpperBound {
			get {
				return length;
			}
		}

		public ITargetObject this [int index] {
			get {
				if ((index < LowerBound) || (index >= UpperBound))
					throw new ArgumentException ();

				return element_type.GetElementObject (location, index-LowerBound);
			}
		}

		public override string ToString ()
		{
			return String.Format ("{0} [{1}:{2}:{3}]", GetType (), Type,
					      element_type, length);
		}
	}
}
