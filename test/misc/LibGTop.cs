#if HAVE_LIBGTOP
using System;
using System.Runtime.InteropServices;

namespace Mono.Debugger.Tests
{
	public static class LibGTop
	{
		static IntPtr handle;

		[DllImport("monodebuggertest_support")]
		static extern IntPtr glibtop_init ();

		[DllImport("monodebuggertest_support")]
		static extern int mono_debugger_libgtop_glue_get_pid ();

		[DllImport("monodebuggertest_support")]
		static extern bool mono_debugger_libgtop_glue_test ();

		[DllImport("monodebuggertest_support")]
		static extern bool mono_debugger_libgtop_glue_get_memory (IntPtr handle, int pid, ref LibGTopGlueMemoryInfo info);

		[DllImport("monodebuggertest_support")]
		static extern bool mono_debugger_libgtop_glue_get_open_files (IntPtr handle, int pid, out int files);

		struct LibGTopGlueMemoryInfo {
			public long pagesize;
			public long size;
			public long vsize;
			public long resident;
			public long share;
			public long rss;
			public long rss_rlim;
		}

		static LibGTop ()
		{
			handle = glibtop_init ();
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
			if (!mono_debugger_libgtop_glue_get_memory (handle, pid, ref info))
				throw new TargetException (
					TargetError.IOError, "Cannot get memory info for process %d",
					pid);

			return new MemoryInfo (
				info.size / info.pagesize, info.vsize / 1024,
				info.resident / info.pagesize, info.share / info.pagesize,
				info.rss / info.pagesize);
		}

		public static int GetOpenFiles (int pid)
		{
			int files;
			if (!mono_debugger_libgtop_glue_get_open_files (handle, pid, out files))
				throw new TargetException (
					TargetError.IOError, "Cannot get open files for process %d",
					pid);

			return files;
		}

		public static bool Test ()
		{
			return mono_debugger_libgtop_glue_test ();
		}
	}
}
#endif
