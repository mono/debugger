using GLib;
using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Globalization;
using Mono.CSharp.Debugger;

namespace Mono.Debugger
{
	public class ILDisassembler : ISourceBuffer
	{
		string image_file;
		string standard_output;
		string standard_error;

		static Hashtable files = new Hashtable ();

		ArrayList methods;
		ArrayList end_lines;

		public string Name {
			get {
				return image_file;
			}
		}

		public string Contents {
			get {
				return standard_output;
			}
		}

		public LineNumberEntry[] GetLines (int index)
		{
			ArrayList lines = (ArrayList) methods [index - 1];

			LineNumberEntry[] retval = new LineNumberEntry [lines.Count];
			lines.CopyTo (retval, 0);
			return retval;
		}

		public int GetEndLine (int index)
		{
			return (int) end_lines [index - 1];
		}

		public int GetStartLine (int index)
		{
			LineNumberEntry lne = (LineNumberEntry) ((ArrayList) methods [index - 1]) [0];
			return (int) lne.Row;
		}

		protected ILDisassembler (string image_file)
		{
			this.image_file = image_file;
			string command_line = "monodis " + image_file;

			int exitcode = Spawn.SpawnCommandLine (command_line, out standard_output,
							       out standard_error);

			if (exitcode != 0)
				throw new Exception ();

			if (standard_error != "")
				throw new Exception (standard_error);

			methods = new ArrayList ();
			end_lines = new ArrayList ();

			int i = 0;
			string[] lines = standard_output.Split ('\n');
			while (i < lines.Length) {
				string line = lines [i].TrimStart (' ', '\t').TrimEnd (' ', '\t');

				if (!line.StartsWith ("// method line ")) {
					i++;
					continue;
				}

				int idx = Int32.Parse (line.Substring (15));
				i++;

				ArrayList list = new ArrayList ();
				list.Add (new LineNumberEntry (i, 0));

				while (i < lines.Length) {
					line = lines [i].TrimStart (' ', '\t').TrimEnd (' ', '\t');
					i++;

					if (line.StartsWith ("}")) {
						end_lines.Add (i);
						break;
					}

					if (line.StartsWith ("IL_")) {
						int offset = Int32.Parse (
							line.Substring (3, 4), NumberStyles.HexNumber);
						list.Add (new LineNumberEntry (i, offset));
					}
				}

				methods.Add (list);
			}
		}

		public static ILDisassembler Disassemble (string image_file)
		{
			if (files.Contains (image_file))
				return (ILDisassembler) files [image_file];

			ILDisassembler dis = new ILDisassembler (image_file);
			files.Add (image_file, dis);
			return dis;
		}
	}
}
