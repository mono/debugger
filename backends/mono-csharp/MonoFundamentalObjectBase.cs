using System;
using Mono.CSharp.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal abstract class MonoFundamentalObjectBase : MonoStructObject, ITargetFundamentalObject
	{
		public MonoFundamentalObjectBase (MonoStructType type, TargetLocation location)
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
			set {
				SetObject (value);
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

		internal virtual void SetObject (object obj)
		{
			try {
				byte [] data = CreateObject (obj);
				if (!type.HasFixedSize || (data == null) || (data.Length != type.Size))
					throw new NotSupportedException ();

				RawContents = data;
			} catch (TargetException ex) {
				is_valid = false;
				throw new LocationInvalidException (ex);
			}
		}

		protected abstract object GetObject (ITargetMemoryReader reader, TargetLocation location);

		protected abstract byte[] CreateObject (object obj);

		public override string Print ()
		{
			return GetObject ().ToString ();
		}
	}
}
