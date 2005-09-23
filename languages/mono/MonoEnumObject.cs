using System;

namespace Mono.Debugger.Languages.Mono
{
#if FIXME
	internal class MonoEnumObject : MonoObject, ITargetEnumObject
	{
		new MonoEnumTypeInfo type;

		public MonoEnumObject (MonoEnumTypeInfo type, TargetLocation location)
			: base (type, location)
		{
			this.type = type;
		}

		public ITargetEnumType Type {
			get {
				return type.Type;
			}
		}
		public ITargetObject Value {
			get {
				return type.GetValue (location);
			}
		}

		protected override long GetDynamicSize (TargetBlob blob, TargetLocation location,
							out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public override string ToString ()
		{
			return String.Format ("{0}", GetType());
		}
	}
#endif
}
