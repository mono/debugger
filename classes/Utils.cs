using System;
using System.IO;
using System.Text;
using System.Globalization;
using SD = System.Diagnostics;

namespace Mono.Debugger
{
	public class LessPipe : TextWriter
	{
		SD.Process process;

		public LessPipe ()
		{
			SD.ProcessStartInfo psi = new SD.ProcessStartInfo ("/usr/bin/less", "-F -M -N -X -- -");
			psi.UseShellExecute = false;
			psi.RedirectStandardInput = true;

			process = SD.Process.Start (psi);
		}

                public override Encoding Encoding {
			get { return process.StandardInput.Encoding; }
		}

                public override void Flush ()
		{
			process.StandardInput.Flush ();
		}

                public override void Write (char value)
		{
			process.StandardInput.Write (value);
		}

		bool disposed;

                protected override void Dispose (bool disposing)
		{
			if (disposing) {
				if (disposed)
					return;
				disposed = true;

				process.StandardInput.Close ();
				process.WaitForExit ();
				process.Close ();
			}

			base.Dispose (disposing);
		}
	}
}
