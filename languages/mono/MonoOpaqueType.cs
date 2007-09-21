using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoOpaqueType : TargetType
	{
		Cecil.TypeDefinition typedef;
		MonoClassType class_type;

		public MonoOpaqueType (MonoSymbolFile file, Cecil.TypeDefinition typedef)
			: base (file.MonoLanguage, TargetObjectKind.Unknown)
		{
			this.typedef = typedef;

			class_type = new MonoClassType (file, typedef);
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return class_type; }
		}

		public Cecil.TypeReference Type {
			get { return typedef; }
		}

		public override string Name {
			get { return typedef.FullName; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 0; }
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			throw new TargetException (TargetError.LocationInvalid,
						   "Cannot access variables of type `{0}'", Name);
		}
	}
}
