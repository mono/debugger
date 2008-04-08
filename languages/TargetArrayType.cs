using System;
using System.Text;

namespace Mono.Debugger.Languages
{
	public class TargetArrayBounds
	{
		public int Rank {
			get;
			private set;
		}

		public bool IsMultiDimensional {
			get;
			private set;
		}

		int? length;

		public bool IsUnbound {
			get { return !IsMultiDimensional && (length == null); }
		}

		public int Length {
			get {
				if (IsMultiDimensional || IsUnbound)
					throw new InvalidOperationException ();

				return (int) length;
			}
		}

		public int[] LowerBounds {
			get;
			private set;
		}

		public int[] UpperBounds {
			get;
			private set;
		}

		private TargetArrayBounds ()
		{ }

		public static TargetArrayBounds MakeSimpleArray (int length)
		{
			TargetArrayBounds bounds = new TargetArrayBounds { Rank = 1 };
			bounds.length = length;
			return bounds;
		}

		public static TargetArrayBounds MakeUnboundArray ()
		{
			return new TargetArrayBounds { Rank = 1 };
		}

		public static TargetArrayBounds MakeMultiArray (int[] lower, int[] upper)
		{
			return new TargetArrayBounds {
				Rank = lower.Length, IsMultiDimensional = true,
				LowerBounds = lower, UpperBounds = upper
				};
		}

		public override string ToString ()
		{
			if (IsUnbound)
				return "TargetArrayBounds (<unbound>)";
			else if (length != null)
				return String.Format ("TargetArrayBounds ({0})", (int) length);
			else {
				StringBuilder sb = new StringBuilder ();
				sb.Append ("TargetArrayBounds (");
				for (int i = 0; i < Rank; i++) {
					if (i > 0)
						sb.Append (",");
					sb.Append (String.Format ("[{0},{1}]", LowerBounds [i],
								  UpperBounds [i]));
				}
				sb.Append (")");
				return sb.ToString ();
			}
		}
	}

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

		public override bool ContainsGenericParameters {
			get { return ElementType.ContainsGenericParameters; }
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
