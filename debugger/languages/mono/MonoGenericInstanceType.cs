using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : MonoClassType
	{
		public readonly TargetType UnderlyingType;
		string full_name;

		public MonoGenericInstanceType (MonoSymbolFile file,
						MonoClassType underlying_type, TargetType[] type_args)
			: base (file, underlying_type.Type)
		{
			this.UnderlyingType = underlying_type;

			StringBuilder sb = new StringBuilder (underlying_type.Name);
			sb.Append ('<');
			for (int i = 0; i < type_args.Length; i++) {
				if (i > 0)
					sb.Append (',');
				sb.Append (type_args [i].Name);
			}
			sb.Append ('>');
			full_name = sb.ToString ();
		}

		public override string Name {
			get { return full_name; }
		}
	}
}
