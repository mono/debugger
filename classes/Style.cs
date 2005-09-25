using System;
using System.Text;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Languages;

namespace Mono.Debugger
{
	public class StructFormatter
	{
		public string head;
		public static int Columns = 60;

		ArrayList items = new ArrayList ();

		public StructFormatter (string header) {
			head = header;
		}

		public void Add (string item)
		{
			items.Add (item);
		}

		public string Format ()
		{
			StringBuilder sb = new StringBuilder ();

			int pos = head.Length + 1;
			bool multi_line = false;
			for (int i = 0; i < items.Count; i++) {
				if (i > 0) {
					sb.Append (", ");
					pos += 2;
				} else {
					sb.Append (" ");
					pos++;
				}

				string item = (string) items [i];

				pos += item.Length;
				if (pos > Columns) {
					sb.Append ("\n  ");
					multi_line = true;
					pos = 2;
				}

				sb.Append (item);
			}

			string text = sb.ToString ();
			if (multi_line)
				return head + "{\n " + text + "\n}";
			else
				return head + "{" + text + " }";
		}
	}

	/// <summary>
	///   This interface controls how things are being displayed to the
	///   user, for instance the current stack frame or variables from
	///   the target.
	/// </summary>
	[Serializable]
	public abstract class Style
	{
		public abstract string Name {
			get;
		}

		public abstract string ShowVariableType (TargetType type, string name);

		public abstract string PrintVariable (IVariable variable, StackFrame frame);

		public abstract string FormatObject (ITargetAccess target, object obj);

		public abstract string FormatType (ITargetAccess target, TargetType type);
	}
}
