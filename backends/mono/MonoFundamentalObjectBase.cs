using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoFundamentalObjectBase : MonoObject, ITargetFundamentalObject
	{
		public MonoFundamentalObjectBase (IMonoTypeInfo type_info, TargetLocation location)
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
				TargetBlob blob;
				if (type_info.HasFixedSize)
					blob = location.ReadMemory (type_info.Size);
				else
					blob = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (blob, location);
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract object GetObject (TargetBlob blob, TargetLocation location);

		void ITargetFundamentalObject.SetObject (ITargetObject obj)
		{
			SetObject ((MonoObject) obj);
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
