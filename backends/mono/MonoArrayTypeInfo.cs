using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayTypeInfo : MonoTypeInfo
	{
		new public readonly MonoArrayType Type;
		protected IMonoTypeInfo element_type;

		public MonoArrayTypeInfo (MonoArrayType type, IMonoTypeInfo element_type)
			: base (type, 4 * type.File.TargetInfo.TargetAddressSize)
		{
			this.Type = type;
			this.element_type = element_type;
		}

		public IMonoTypeInfo ElementType {
			get { return element_type; }
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoArrayObject (this, location);
		}
	}
}
