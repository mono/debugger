using System;

namespace Mono.Debugger.Languages
{
	public abstract class TargetArrayType : TargetType
	{
		TargetType element_type;
		string full_name;
		int rank;

		protected TargetArrayType (TargetType element_type, int rank)
			: base (element_type.Language, TargetObjectKind.Array)
		{
			this.element_type = element_type;
			this.rank = rank;

			full_name = compute_fullname ();
		}

		public int Rank {
			get { return rank; }
		}

		public TargetType ElementType {
			get { return element_type; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override string Name {
			get { return full_name; }
		}

		string compute_fullname ()
		{
			string rank_specifier;
			if (Rank == 1)
				rank_specifier = "[]";
			else
				rank_specifier = "[" + new String (',', Rank-1) + "]";

			return element_type.Name + rank_specifier;
		}
	}
}
