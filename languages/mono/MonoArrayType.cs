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
		{
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 4 * Language.TargetInfo.TargetAddressSize; }
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			return new MonoArrayObject (this, location);
		}
	}
}
