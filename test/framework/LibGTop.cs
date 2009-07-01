using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Test.Framework
{
	public static class LibGTop
	{
		static IntPtr handle;

		[DllImport("monodebuggerserver")]
		static extern int mono_debugger_libgtop_glue_get_pid ();

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_libgtop_glue_get_memory (int pid, ref LibGTopGlueMemoryInfo info);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_libgtop_glue_get_open_files (int pid, out int files);

		struct LibGTopGlueMemoryInfo {
			public long size;
			public long vsize;
			public long resident;
			public long share;
			public long rss;
			public long rss_rlim;
		}

		public static int GetPid ()
		{
			return mono_debugger_libgtop_glue_get_pid ();
		}

		public struct MemoryInfo
		{
			public readonly long Size;
			public readonly long VirtualSize;
			public readonly long Resident;
			public readonly long Share;
			public readonly long RSS;

			public MemoryInfo (long size, long vsize, long resident, long share, long rss)
			{	
				this.Size = size;
				this.VirtualSize = vsize;
				this.Resident = resident;
				this.Share = share;
				this.RSS = rss;
			}
		}

		public static MemoryInfo GetMemoryInfo (int pid)
		{
			LibGTopGlueMemoryInfo info = new LibGTopGlueMemoryInfo ();
			if (!mono_debugger_libgtop_glue_get_memory (pid, ref info))
				throw new TargetException (
					TargetError.IOError, "Cannot get memory info for process %d",
					pid);

			return new MemoryInfo (
				info.size, info.vsize / 1024,
				info.resident, info.share, info.rss);
		}

		public static int GetOpenFiles (int pid)
		{
			int files;
			if (!mono_debugger_libgtop_glue_get_open_files (pid, out files))
				throw new TargetException (
					TargetError.IOError, "Cannot get open files for process %d",
					pid);

			return files;
		}
	}
}
