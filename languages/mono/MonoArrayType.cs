using System;
using System.Text;
using C = Mono.CompilerServices.SymbolWriter;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoArrayType : TargetArrayType
	{
		public MonoArrayType (TargetType element_type, int rank)
			: base (element_type, rank)
		{ }

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasClassType {
			get { return true; }
		}

		public override TargetClassType ClassType {
			get { return Language.ArrayType; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 4 * Language.TargetInfo.TargetAddressSize; }
		}

		internal override int GetElementSize (TargetMemoryAccess target)
		{
			TargetType element_type;
			if (ElementType is MonoEnumType)
				element_type = ((MonoEnumType) ElementType).ClassType;
			else
				element_type = ElementType;

			IMonoStructType stype = element_type as IMonoStructType;
			if ((stype == null) || stype.Type.IsByRef)
				return base.GetElementSize (target);

			MonoClassInfo cinfo = stype.ResolveClass (target, true);
			return cinfo.GetInstanceSize (target);
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new MonoArrayObject (this, location);
		}
	}
}
