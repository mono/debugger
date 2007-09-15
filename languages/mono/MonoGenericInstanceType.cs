using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : MonoClassType
	{
		public readonly TargetType UnderlyingType;
		public readonly MonoGenericInst GenericInst;
		string full_name;

		public MonoGenericInstanceType (MonoClassType underlying, MonoGenericInst inst)
			: base (underlying.File, underlying.Type)
		{
			this.UnderlyingType = underlying;
			this.GenericInst = inst;

			StringBuilder sb = new StringBuilder (underlying.Name);
			sb.Append ('<');
			for (int i = 0; i < inst.Types.Length; i++) {
				if (i > 0)
					sb.Append (',');
				sb.Append (inst.Types [i].Name);
			}
			sb.Append ('>');
			full_name = sb.ToString ();
		}

		public override string Name {
			get { return full_name; }
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			Console.WriteLine ("GENERIC INST GET OBJECT: {0}", this);
			return new MonoClassObject (this, location);
		}

		internal override TargetObject GetField (Thread target, TargetLocation location,
							 MonoFieldInfo finfo)
		{
			Console.WriteLine ("GENERIC INST GET FIELD: {0} {1}", this, finfo);

			MonoGenericParameterType gparam = finfo.Type as MonoGenericParameterType;
			if (gparam != null) {
				Console.WriteLine ("GENERIC INST GET FIELD #1: {0} {1}",
						   GenericInst, gparam.Position);

				TargetType type = GenericInst.Types [gparam.Position];
				MonoFieldInfo inflated = new MonoFieldInfo (
					type, finfo.Index, finfo.Position, finfo.FieldInfo);

				return base.GetField (target, location, inflated);
			}

			return base.GetField (target, location, finfo);
		}
	}
}
