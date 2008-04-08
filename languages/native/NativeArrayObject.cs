using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayObject : TargetArrayObject
	{
		public NativeArrayObject (NativeArrayType type, TargetLocation location,
					  TargetArrayBounds bounds)
			: base (type, location)
		{
			this.bounds = bounds;
		}

		protected override void DoGetArrayBounds (TargetMemoryAccess target)
		{ }

		internal override TargetObject GetElement (TargetMemoryAccess target, int[] indices)
		{
			int offset = GetArrayOffset (target, indices);

			TargetLocation new_location = Location.GetLocationAtOffset (offset);
			if (Type.ElementType.IsByRef)
				new_location = new_location.GetDereferencedLocation ();

			return Type.ElementType.GetObject (target, new_location);
		}

		internal override void SetElement (TargetMemoryAccess target, int[] indices,
						   TargetObject obj)
		{
			throw new NotSupportedException ();
		}

		internal override long GetDynamicSize (TargetMemoryAccess target, TargetBlob blob,
						       TargetLocation location,
						       out TargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		internal override string Print (TargetMemoryAccess target)
		{
			if (Location.HasAddress)
				return String.Format ("{0}", Location.GetAddress (target));
			else
				return String.Format ("{0}", Location);
		}

		public override bool HasClassObject {
			get { return false; }
		}

		internal override TargetClassObject GetClassObject (TargetMemoryAccess target)
		{
			throw new InvalidOperationException ();
		}
	}
}
