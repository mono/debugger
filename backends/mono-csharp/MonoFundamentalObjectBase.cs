using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoFundamentalObjectBase : MonoObject, ITargetFundamentalObject
	{
		public MonoFundamentalObjectBase (MonoType type, MonoTargetLocation location)
			: base (type, location)
		{ }

		public bool HasObject {
			get {
				return true;
			}
		}

		public object Object {
			get {
				return GetObject ();
			}
		}

		internal object GetObject ()
		{
			try {
				ITargetMemoryReader reader;
				if (type.HasFixedSize)
					reader = location.ReadMemory (type.Size);
				else
					reader = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (reader, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract object GetObject (ITargetMemoryReader reader, MonoTargetLocation location);
	}
}
