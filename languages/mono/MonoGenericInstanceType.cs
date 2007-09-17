using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetType
	{
		public readonly TargetType UnderlyingType;
		public readonly MonoGenericContext GenericContext;
		string full_name;

		public MonoGenericInstanceType (MonoClassType underlying, MonoGenericContext context)
			: base (underlying.File.MonoLanguage, TargetObjectKind.Object)
		{
			this.UnderlyingType = underlying;
			this.GenericContext = context;

			Console.WriteLine ("GENERIC INSTANCE TYPE CTOR: {0} {1}", underlying, context);

			StringBuilder sb = new StringBuilder (underlying.Type.FullName);
			sb.Append ("<");
			for (int i = 0; i < context.MethodInst.Types.Length; i++) {
				if (i > 0)
					sb.Append (",");
				sb.Append (context.MethodInst.Types [i].Name);
			}
			sb.Append (">");
			full_name = sb.ToString ();
		}

		public override string Name {
			get { return full_name; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { throw new InvalidOperationException (); }
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			return new MonoGenericInstanceObject (this, location);
		}
	}
}
