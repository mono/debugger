using System;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeFunctionPointer : TargetPointerType
	{
		public readonly TargetFunctionType Type;

		public NativeFunctionPointer (Language language, TargetFunctionType func)
			: base (language, TargetObjectKind.Pointer, func.Name,
				language.TargetInfo.TargetAddressSize)
		{
			this.Type = func;
		}

		public override string Name {
			get { return Type.Name; }
		}

		public override int Size {
			get { return Type.Language.TargetInfo.TargetAddressSize; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override bool ContainsGenericParameters {
			get { return false; }
		}

		public override bool IsArray {
			get { return false; }
		}

		public override bool IsTypesafe {
			get { return true; }
		}

		public override bool HasStaticType {
			get { return true; }
		}

		public override TargetType StaticType {
			get { return Type; }
		}

		public override bool CanDereference {
			get { return false; }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			return new NativeFunctionObject (this, location);
		}

		public override TargetPointerObject GetObject (TargetAddress address)
		{
			return new NativeFunctionObject (this, new AbsoluteTargetLocation (address));
		}
	}
}
