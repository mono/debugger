using System;
using System.Text;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Backends
{
	internal abstract class Utils
	{
		[DllImport("glib-2.0")]
		extern static bool g_file_get_contents (string filename, out IntPtr contents, out int length, out IntPtr error);

		public static string GetFileContents (string filename)
		{
			int length;
			IntPtr contents, error;

			if (!g_file_get_contents (filename, out contents, out length, out error))
				return null;

			return Marshal.PtrToStringAnsi (contents, length);
		}
		
	}
}
