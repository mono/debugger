using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayTypeInfo : MonoTypeInfo
	{
		new public readonly MonoArrayType Type;
		protected MonoTypeInfo element_type;
		protected MonoArrayTypeInfo subarray_type;

		public MonoArrayTypeInfo (MonoArrayType type, MonoTypeInfo element_type)
			: base (type, 3 * type.File.TargetInfo.TargetAddressSize + 4)
		{
			this.Type = type;
			this.element_type = element_type;

			if (type.SubArrayType != null)
				subarray_type = new MonoArrayTypeInfo (type.SubArrayType, element_type);
		}

		public MonoTypeInfo ElementType {
			get { return element_type; }
		}

		public MonoArrayTypeInfo SubArrayType {
			get { return subarray_type; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoArrayObject (this, location);
		}
	}
}
