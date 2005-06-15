using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoFundamentalObjectBase : MonoObject, ITargetFundamentalObject
	{
		public MonoFundamentalObjectBase (MonoTypeInfo type_info, TargetLocation location)
			: base (type_info, location)
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
				if (type_info.HasFixedSize)
					reader = location.ReadMemory (type_info.Size);
				else
					reader = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (reader, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract object GetObject (ITargetMemoryReader reader,
						     TargetLocation location);

		// XXX this is here due to mono bug #75270.  without the
		// (unused) implementation, MonoFundamentalObject.SetObject
		// is never called when going through an interface.
		public override void SetObject (ITargetObject obj)
		{
			throw new NotImplementedException ();
		}

		public override string Print ()
		{
			object obj = GetObject ();
			if (obj is IntPtr)
				return String.Format ("0x{0:x}", ((IntPtr) obj).ToInt64 ());
			else if (obj is UIntPtr)
				return String.Format ("0x{0:x}", ((UIntPtr) obj).ToUInt64 ());
			else
				return obj.ToString ();
		}
	}
}
