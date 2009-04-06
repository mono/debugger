#if MARTIN_PRIVATE
using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SD = System.Diagnostics;
using C = Mono.CompilerServices.SymbolWriter;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Mono
{
	internal static class MonoUtils
	{
		public static void DumpLineNumberTable (TextWriter writer, MonoSymbolFile file,
							Cecil.MethodDefinition mdef, C.MethodEntry entry)
		{
			try {
				DumpLineNumberTable_internal (writer, file, mdef, entry);
			} catch (Exception ex) {
				writer.WriteLine ("DUMP LNT EX: {0}", ex);
			}
		}

		static void DumpLineNumberTable_internal (TextWriter writer, MonoSymbolFile file,
							  Cecil.MethodDefinition mdef, C.MethodEntry entry)
		{
			string full_name = MonoSymbolFile.GetMethodName (mdef);
			if (mdef.MetadataToken.TokenType != Cecil.Metadata.TokenType.Method) {
				writer.WriteLine ("UNKNOWN METHOD: {0}", full_name);
				return;
			}

			writer.WriteLine ();
			writer.WriteLine ("Symfile Line Numbers (file / row / offset):");
			writer.WriteLine ("-------------------------------------------");

			C.LineNumberEntry[] lnt;
			lnt = entry.GetLineNumberTable ().LineNumbers;
			for (int i = 0; i < lnt.Length; i++) {
				C.LineNumberEntry lne = lnt [i];

				writer.WriteLine ("{0,4} {1,4} {2,4} {3,4:x}{4}", i,
						  lne.File, lne.Row, lne.Offset,
						  lne.IsHidden ? " (hidden)" : "");
			}

			writer.WriteLine ("-------------------------------------------");
			writer.WriteLine ();

			List<string> lines;
			Dictionary<int,int> offsets;

			if (!DisassembleMethod_internal (file.ImageFile, (int) mdef.MetadataToken.RID, out lines, out offsets)) {
				writer.WriteLine ("Cannot disassemble method: {0}", full_name);
				return;
			}

			writer.WriteLine ("Disassembling {0}:\n\n{1}\n", full_name, String.Join ("\n", lines.ToArray ()));
		}

		public static void DisassembleMethod (string image_file, int index)
		{
			Console.WriteLine ("DISASSEMBLE METHOD: {0} {1} - {2}", image_file, index, BuildInfo.monodis);

			try {
				List<string> lines;
				Dictionary<int,int> offsets;

				if (!DisassembleMethod_internal (image_file, index, out lines, out offsets)) {
					Console.WriteLine ("No such method {0} in image {1}.", index, image_file);
					return;
				}

				using (TextWriter writer = new LessPipe ()) {
					writer.WriteLine (String.Join ("\n", lines.ToArray ()));
				}
			} catch (Exception ex) {
				Console.WriteLine ("DISASSEMBLE EX: {0}", ex);
			}
		}

		static bool DisassembleMethod_internal (string image_file, int index, out List<string> lines,
							out Dictionary<int,int> offsets)
		{
			SD.ProcessStartInfo psi = new SD.ProcessStartInfo (BuildInfo.monodis, image_file);
			psi.UseShellExecute = false;
			psi.RedirectStandardOutput = true;

			SD.Process process = SD.Process.Start (psi);

			Regex start_re = new Regex (@"^\s*// method line (\d+)");
			Regex end_re = new Regex (@"^\s*\}\s*// end of method");
			Regex line_re = new Regex (@"^\s*IL_([A-Fa-f0-9]{4}):");

			bool found = false;
			lines = new List<string> ();
			offsets = new Dictionary<int,int> ();

			string line;
			StreamReader reader = process.StandardOutput;
			while ((line = reader.ReadLine ()) != null) {
				Match start_match = start_re.Match (line);
				Match end_match = end_re.Match (line);
				Match line_match = line_re.Match (line);

				if (start_match.Success) {
					int mline = Int32.Parse (start_match.Groups [1].Value);
					if (mline == index)
						found = true;
				}

				if (!found)
					continue;

				if (line_match.Success) {
					int offset = Int32.Parse (line_match.Groups [1].Value, NumberStyles.HexNumber);
					offsets.Add (offset, lines.Count);
				}

				lines.Add (line);

				if (end_match.Success)
					break;
			}

			process.WaitForExit ();
			process.Close ();

			return found;
		}
	}
}
#endif
