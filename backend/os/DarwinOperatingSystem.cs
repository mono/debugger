using System;
using System.IO;
using System.Collections;

using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class DarwinOperatingSystem : OperatingSystemBackend
	{
		Hashtable bfd_hash;
		Bfd main_bfd;

		public DarwinOperatingSystem (ProcessServant process)
			: base (process)
		{
			this.bfd_hash = Hashtable.Synchronized (new Hashtable ());
		}

		internal override void ReadNativeTypes ()
		{
			foreach (Bfd bfd in bfd_hash.Values)
				bfd.ReadTypes ();
		}

		public override NativeExecutableReader LookupLibrary (TargetAddress address)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				if (!bfd.IsContinuous)
					continue;

				if ((address >= bfd.StartAddress) && (address < bfd.EndAddress))
					return bfd;
			}

			return null;
		}

		public override NativeExecutableReader LoadExecutable (TargetMemoryInfo memory, string filename,
								       bool load_native_symtabs)
		{
			check_disposed ();
			Bfd bfd = (Bfd) bfd_hash [filename];
			if (bfd != null)
				return bfd;

			bfd = new Bfd (this, memory, filename, TargetAddress.Null, true);
			bfd_hash.Add (filename, bfd);
			main_bfd = bfd;
			return bfd;
		}

		public override NativeExecutableReader AddExecutableFile (Inferior inferior, string filename,
									  TargetAddress base_address, bool step_into,
									  bool is_loaded)
		{
			check_disposed ();
			Bfd bfd = (Bfd) bfd_hash [filename];
			if (bfd != null)
				return bfd;

			bfd = new Bfd (this, inferior.TargetMemoryInfo, filename, base_address, is_loaded);
			bfd_hash.Add (filename, bfd);
			check_loaded_library (inferior, bfd);
			return bfd;
		}

		protected void check_loaded_library (Inferior inferior, Bfd bfd)
		{
			if (!Process.IsManaged)
				check_for_mono_runtime (inferior, bfd);
		}

		TargetAddress pending_mono_init = TargetAddress.Null;

		void check_for_mono_runtime (Inferior inferior, Bfd bfd)
		{
			TargetAddress info = bfd.LookupSymbol ("MONO_DEBUGGER__debugger_info");
			if (info.IsNull)
			{
				info = bfd.LookupSymbol ("MONO_DEBUGGER__debugger_info_ptr");
				if (info.IsNull)
					return;
				TargetAddress data = inferior.ReadAddress (info);
				if (data.IsNull)
				{
					pending_mono_init = info;
					return;
				}
				info = data;
			}	
			Process.InitializeMono (inferior, info);
		}

		internal override bool CheckForPendingMonoInit (Inferior inferior)
		{
			if (pending_mono_init.IsNull)
				return false;

			TargetAddress data = inferior.ReadAddress (pending_mono_init);
			if (data.IsNull)
				return false;

			pending_mono_init = TargetAddress.Null;
			Process.InitializeMono (inferior, data);
			return true;
		}

		public override TargetAddress LookupSymbol (string name)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				TargetAddress symbol = bfd.LookupSymbol (name);
				if (!symbol.IsNull)
					return symbol;
			}

			return TargetAddress.Null;
		}

#if FIXME
		public void CloseBfd (Bfd bfd)
		{
			if (bfd == null)
				return;

			bfd_hash.Remove (bfd.FileName);
			bfd.Dispose ();
		}
#endif

		public override NativeExecutableReader LookupLibrary (string name)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				if (Path.GetFileName (bfd.FileName) == name)
					return bfd;
			}

			return null;
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				if (bfd.GetTrampoline (memory, address, out trampoline, out is_start))
					return true;
			}

			is_start = false;
			trampoline = TargetAddress.Null;
			return false;
		}

		public TargetAddress GetSectionAddress (string name)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				TargetAddress address = bfd.GetSectionAddress (name);
				if (!address.IsNull)
					return address;
			}

			return TargetAddress.Null;
		}

#region Dynamic Linking

		bool has_dynlink_info;
		TargetAddress first_link_map = TargetAddress.Null;
		TargetAddress dynlink_breakpoint_addr = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;

		AddressBreakpoint dynlink_breakpoint;

		internal override void UpdateSharedLibraries (Inferior inferior)
		{
			// This fails if it's a statically linked executable.
			try {
				do_update_shlib_info (inferior);
	
				check_loaded_library (inferior, main_bfd);
			} catch (Exception ex) {
				Report.Error ("Failed to read shared libraries: {0}", ex);
				return;
			}
		}

		void do_update_shlib_info (Inferior inferior)
		{
			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
				Console.WriteLine("!!!!map entry.");
				int the_size = 4 * inferior.TargetAddressSize;
				TargetReader map_reader = new TargetReader (inferior.ReadMemory (map, the_size));

				TargetAddress l_addr = map_reader.ReadAddress ();
				TargetAddress l_name = map_reader.ReadAddress ();
				map_reader.ReadAddress ();

				string name;
				try {
					name = inferior.ReadString (l_name);
					// glibc 2.3.x uses the empty string for the virtual
					// "linux-gate.so.1".
					if ((name != null) && (name == ""))
						name = null;
				} catch {
					name = null;
				}

				map = map_reader.ReadAddress ();

				if (first) {
					first = false;
					continue;
				}

				if (name == null)
					continue;

				if (bfd_hash.Contains (name))
					continue;

				bool step_into = Process.ProcessStart.LoadNativeSymbolTable;
				AddExecutableFile (inferior, name, l_addr, step_into, true);
			}
		}

#endregion

		protected override void DoDispose ()
		{
			if (bfd_hash != null) {
				foreach (Bfd bfd in bfd_hash.Values)
					bfd.Dispose ();
				bfd_hash = null;
			}
		}
	}
}
