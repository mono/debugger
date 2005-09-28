using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoPointerType : TargetPointerType
	{
		TargetType element_type;

		public MonoPointerType (TargetType element_type)
			: base (element_type.Language, element_type.Name + "&", 0)
		{
			this.element_type = element_type;
		}

		public override bool IsTypesafe {
			get { return true; }
		}

		public override bool HasStaticType {
			get { return true; }
		}

		public override bool IsArray {
			get { return false; }
		}

		public override TargetType StaticType {
			get { return element_type; }
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			return new MonoPointerObject (this, location);
		}
	}
}
