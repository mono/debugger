using System;

namespace Mono.Debugger.Languages.Mono
{
	internal abstract class MonoFundamentalObjectBase : MonoObject, ITargetFundamentalObject
	{
		new public readonly MonoFundamentalType Type;

		public MonoFundamentalObjectBase (MonoFundamentalType type, TargetLocation location)
			: base (type, location)
		{
			this.Type = type;
		}

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
				if (Type.HasFixedSize)
					blob = location.ReadMemory (Type.Size);
				else
					blob = GetDynamicContents (location, MaximumDynamicSize);

				return GetObject (blob, location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract object GetObject (TargetBlob blob, TargetLocation location);

		void ITargetFundamentalObject.SetObject (ITargetObject obj)
		{
			Type.SetObject (location, (MonoObject) obj);
		}

		public override string Print (ITargetAccess target)
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
