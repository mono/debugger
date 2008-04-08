using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeArrayType : TargetArrayType
	{
		string name;
		int size;
		TargetArrayBounds bounds;

		public NativeArrayType (Language language, string name, TargetType element_type,
					TargetArrayBounds bounds, int size)
			: base (element_type, bounds.Rank)
		{
			this.name = name;
			this.size = size;
			this.bounds = bounds;
		}
	  
		public override bool IsByRef {
			get { return false; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override string Name {
			get { return name; }
		}

		public override int Size {
			get { return size; }
		}

		public override bool HasFixedSize {
			get { return !bounds.IsUnbound; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			return new NativeArrayObject (this, location, bounds);
		}
	}

}

