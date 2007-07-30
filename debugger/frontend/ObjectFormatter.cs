using System;
using System.Text;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Frontend
{
	public class ObjectFormatter
	{
		public readonly DisplayFormat DisplayFormat;

		public static int Columns = 75;
		public static bool WrapLines = true;

		StringBuilder sb = new StringBuilder ();

		public ObjectFormatter (DisplayFormat format)
		{
			this.DisplayFormat = format;
		}

		public void Format (Thread target, object obj)
		{
			if ((obj is byte) || (obj is sbyte) || (obj is char) ||
			    (obj is short) || (obj is ushort) || (obj is int) || (obj is uint) ||
			    (obj is long) || (obj is ulong)) {
				if (DisplayFormat == DisplayFormat.HexaDecimal)
					Append (String.Format ("0x{0:x}", obj));
				else
					Append (obj.ToString ());
			} else if (obj is bool) {
				Append (((bool) obj) ? "true" : "false");
			} else if (obj is string) {
				Append ('"' + (string) obj + '"');
			} else if (obj is TargetType) {
				Append (((TargetType) obj).Name);
			} else if (obj is TargetObject) {
				Format (target, (TargetObject) obj);
			} else {
				Append (obj.ToString ());
			}
		}

		public void Format (Thread target, TargetObject obj)
		{
			sb.Append (String.Format ("({0}) ", obj.TypeName));
			FormatObjectRecursed (target, obj, false);
		}

		public void FormatVariable (StackFrame frame, TargetVariable variable)
		{
			TargetObject obj = null;

			Append ("{0} = ", variable.Name);

			try {
				obj = variable.GetObject (frame);
				if (obj != null)
					Format (frame.Thread, obj);
				else
					Append ("<cannot display object>");
			} catch {
				Append ("<cannot display object>");
			}
		}

		new public string ToString ()
		{
			return sb.ToString ();
		}

		int pos = 0;
		int last = -1;
		int indent_level = 0;

		protected void Append (string text)
		{
			sb.Append (text);
			pos += text.Length;
		}

		protected void Append (string text, params object[] args)
		{
			Append (String.Format (text, args));
		}

		protected void CheckLineWrap ()
		{
			if (!WrapLines)
				return;

			if (pos < Columns) {
				last = sb.Length;
				return;
			}

			string wrap = "\n" + new String (' ', indent_level);

			if (last < 0)
				sb.Append (wrap);
			else
				sb.Insert (last, wrap);

			last = -1;
			pos = 0;
		}

		protected void FormatObjectRecursed (Thread target, TargetObject obj, bool recursed)
		{
			try {
				if (DisplayFormat == DisplayFormat.Address) {
					if (obj.HasAddress)
						Append (obj.GetAddress (target).ToString ());
					else
						Append ("<cannot get address>");
					return;
				} else if (obj.HasAddress && obj.GetAddress (target).IsNull) {
					Append ("null");
					return;
				} else if (!recursed) {
					FormatObject (target, obj);
					return;
				}

				switch (obj.Kind) {
				case TargetObjectKind.Enum:
					FormatObject (target, obj);
					break;

				case TargetObjectKind.Fundamental:
					TargetFundamentalObject fobj = (TargetFundamentalObject) obj;
					object value = fobj.GetObject (target);
					Format (target, value);
					break;

				default:
					PrintObject (target, obj);
					break;
				}
			} catch {
				Append ("<cannot display object>");
			}
		}

		protected void FormatObject (Thread target, TargetObject obj)
		{
			switch (obj.Kind) {
			case TargetObjectKind.Array:
				FormatArray (target, (TargetArrayObject) obj);
				break;

			case TargetObjectKind.Pointer:
				TargetPointerObject pobj = (TargetPointerObject) obj;
				if (!pobj.Type.IsTypesafe) {
					FormatObjectRecursed (target, pobj, true);
					break;
				}

				try {
					TargetObject deref = pobj.GetDereferencedObject (target);
					Append ("&({0}) ", deref.TypeName);
					FormatObjectRecursed (target, deref, true);
				} catch {
					FormatObjectRecursed (target, pobj, true);
				}
				break;

			case TargetObjectKind.Object:
				TargetObjectObject oobj = (TargetObjectObject) obj;
				try {
					TargetObject deref = oobj.GetDereferencedObject (target);
					Append ("&({0}) ", deref.TypeName);
					FormatObjectRecursed (target, deref, false);
				} catch {
					FormatObjectRecursed (target, oobj, true);
				}
				break;

			case TargetObjectKind.Class:
			case TargetObjectKind.Struct:
				FormatClassObject (target, (TargetClassObject) obj);
				break;

			case TargetObjectKind.Fundamental: {
				object value = ((TargetFundamentalObject) obj).GetObject (target);
				Format (target, value);
				break;
			}

			case TargetObjectKind.Enum:
				FormatEnum (target, (TargetEnumObject) obj);
				break;

			default:
				PrintObject (target, obj);
				break;
			}
		}

		protected void FormatClassObject (Thread target, TargetClassObject obj)
		{
			Append ("{ ");
			indent_level += 3;

			bool first = true;

			TargetClassObject parent = obj.GetParentObject (target);
			if ((parent != null) && (parent.Type != parent.Type.Language.ObjectType)) {
				Append ("<{0}> = ", parent.Type.Name);
				CheckLineWrap ();
				FormatClassObject (target, parent);
				first = false;
			}

			TargetFieldInfo[] fields = obj.Type.Fields;
			for (int i = 0; i < fields.Length; i++) {
				if (!first) {
					Append (", ");
					CheckLineWrap ();
				}
				first = false;
				Append (fields [i].Name + " = ");
				try {
					TargetObject fobj = obj.GetField (target, fields [i]);
					if (fobj == null)
						Append ("null");
					else
						FormatObjectRecursed (target, fobj, true);
				} catch {
					Append ("<cannot display object>");
				}
			}

			Append (first ? "}" : " }");
			indent_level -= 3;
		}

		protected void FormatEnum (Thread target, TargetEnumObject eobj)
		{
			TargetFundamentalObject fobj = eobj.Value as TargetFundamentalObject;

			if ((DisplayFormat == DisplayFormat.HexaDecimal) || (fobj == null)) {
				FormatObjectRecursed (target, eobj.Value, true);
				return;
			}

			object value = fobj.GetObject (target);

			SortedList members = new SortedList ();
			foreach (TargetFieldInfo field in eobj.Type.Members)
				members.Add (field.Name, field.ConstValue);

			if (!eobj.Type.IsFlagsEnum) {
				foreach (DictionaryEntry entry in members) {
					if (entry.Value.Equals (value)) {
						Append ((string) entry.Key);
						return;
					}
				}
			} else if (value is ulong) {
				bool first = true;
				ulong the_value = (ulong) value;
				foreach (DictionaryEntry entry in members) {
					ulong fvalue = System.Convert.ToUInt64 (entry.Value);
					if ((the_value & fvalue) != fvalue)
						continue;
					if (!first) {
						Append (" | ");
						CheckLineWrap ();
					}
					first = false;
					Append ((string) entry.Key);
				}
				if (!first)
					return;
			} else {
				bool first = true;
				long the_value = System.Convert.ToInt64 (value);
				foreach (DictionaryEntry entry in members) {
					long fvalue = System.Convert.ToInt64 (entry.Value);
					if ((the_value & fvalue) != fvalue)
						continue;
					if (!first) {
						Append (" | ");
						CheckLineWrap ();
					}
					first = false;
					Append ((string) entry.Key);
				}
				if (!first)
					return;
			}

			FormatObjectRecursed (target, fobj, true);
		}

		protected void FormatArray (Thread target, TargetArrayObject aobj)
		{
			int rank = aobj.Type.Rank;
			FormatArray (target, aobj, 0, rank, new int [0]);
		}

		protected void FormatArray (Thread target, TargetArrayObject aobj,
					    int dimension, int rank, int[] indices)
		{
			Append ("[ ");
			indent_level += 3;

			bool first = true;

			int[] new_indices = new int [dimension + 1];
			indices.CopyTo (new_indices, 0);

			int lower = aobj.GetLowerBound (target, dimension);
			int upper = aobj.GetUpperBound (target, dimension);

			for (int i = lower; i < upper; i++) {
				if (!first) {
					Append (", ");
					CheckLineWrap ();
				}
				first = false;

				new_indices [dimension] = i;
				if (dimension + 1 < rank)
					FormatArray (target, aobj, dimension + 1, rank, new_indices);
				else {
					TargetObject eobj = aobj.GetElement (target, new_indices);
					FormatObjectRecursed (target, eobj, false);
				}
			}

			Append (first ? "]" : " ]");
			indent_level -= 3;
		}

		protected void PrintObject (Thread target, TargetObject obj)
		{
			try {
				Append (obj.Print (target));
			} catch {
				Append ("<cannot display object>");
			}
		}
	}
}
