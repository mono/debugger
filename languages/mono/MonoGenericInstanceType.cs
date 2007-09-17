using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : MonoClassType
	{
		MonoFieldInfo[] inflated_fields;
		public readonly TargetType UnderlyingType;
		public readonly MonoGenericInst GenericInst;
		string full_name;

		TargetAddress klass_address;

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

		public MonoGenericInstanceType (MonoClassType underlying, MonoGenericInst inst,
						TargetAddress klass_address)
			: this (underlying, inst)
		{
			this.klass_address = klass_address;
		}

		public override string Name {
			get { return full_name; }
		}

		internal override MonoClassInfo DoResolveClass ()
		{
			Console.WriteLine ("GENERIC INST - DO RESOLVE CLASS: {0} {1}", this,
					   klass_address);
			MonoClassInfo info = File.MonoLanguage.GetClassInfo (klass_address);
			return base.DoResolveClass ();
		}

		void inflate_fields ()
		{
			if (inflated_fields != null)
				return;

			get_fields ();
			inflated_fields = new MonoFieldInfo [fields.Length];

			for (int i = 0; i < fields.Length; i++) {
				Console.WriteLine ("INFLATE FIELDS: {0} {1} {2}", this, i, fields [i]);
				inflated_fields [i] = fields [i];
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				inflate_fields ();
				return inflated_fields;
			}
		}

		protected override TargetObject DoGetObject (TargetLocation location)
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
