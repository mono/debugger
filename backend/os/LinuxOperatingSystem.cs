using System;
using System.IO;
using System.Collections;

using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class LinuxOperatingSystem : OperatingSystemBackend
	{
		Hashtable bfd_hash;
		Bfd main_bfd;

		public LinuxOperatingSystem (ProcessServant process)
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
			check_nptl_setxid (inferior, bfd);

			if (!Process.MonoRuntimeFound)
				check_for_mono_runtime (inferior, bfd);
		}

		TargetAddress pending_mono_init = TargetAddress.Null;

		void check_for_mono_runtime (Inferior inferior, Bfd bfd)
		{
			TargetAddress info = bfd.GetSectionAddress (".mdb_debug_info");
			if (info.IsNull)
				return;

			TargetAddress data = inferior.ReadAddress (info);
			if (data.IsNull) {
				//
				// See CheckForPendingMonoInit() below - this should only happen when
				// the Mono runtime is embedded - for instance Moonlight inside Firefox.
				//
				// Note that we have to do a symbol lookup for it because we do not know
				// whether the mono runtime is recent enough to have this variable.
				//
				data = bfd.LookupSymbol ("MONO_DEBUGGER__using_debugger");
				if (data.IsNull) {
					Report.Error ("Failed to initialize the Mono runtime!");
					return;
				}

				inferior.WriteInteger (data, 1);
				pending_mono_init = info;
				return;
			}

			Process.InitializeMono (inferior, data);
		}

		//
		// There seems to be a bug in some versions of glibc which causes _dl_debug_state() being
		// called with RT_CONSISTENT before relocations are done.
		//
		// If that happens, the debugger cannot read the `MONO_DEBUGGER__debugger_info' structure
		// at the time the `libmono.so' library is loaded.
		//
		// As a workaround, the `mdb_debug_info' now also contains a global variable called
		// `MONO_DEBUGGER__using_debugger' which may we set to 1 by the debugger to tell us that
		// we're running inside the debugger.
		//

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
				read_dynamic_info (inferior);
			} catch (Exception ex) {
				Report.Error ("Failed to read shared libraries: {0}", ex);
				return;
			}
		}

		void read_dynamic_info (Inferior inferior)
		{
			if (has_dynlink_info) {
				if (!first_link_map.IsNull)
					do_update_shlib_info (inferior);
				return;
			}

			TargetAddress debug_base = main_bfd.ReadDynamicInfo (inferior);
			if (debug_base.IsNull)
				return;

			int size = 2 * inferior.TargetLongIntegerSize + 3 * inferior.TargetAddressSize;

			TargetReader reader = new TargetReader (inferior.ReadMemory (debug_base, size));
			if (reader.ReadLongInteger () != 1)
				return;

			first_link_map = reader.ReadAddress ();
			dynlink_breakpoint_addr = reader.ReadAddress ();

			rdebug_state_addr = debug_base + reader.Offset;

			if (reader.ReadLongInteger () != 0)
				return;

			has_dynlink_info = true;

			Instruction insn = inferior.Architecture.ReadInstruction (inferior, dynlink_breakpoint_addr);
			if ((insn == null) || !insn.CanInterpretInstruction)
				throw new InternalError ("Unknown dynlink breakpoint: {0}", dynlink_breakpoint_addr);

			dynlink_breakpoint = new DynlinkBreakpoint (this, insn);
			dynlink_breakpoint.Insert (inferior);

			do_update_shlib_info (inferior);

			check_loaded_library (inferior, main_bfd);
		}

		bool dynlink_handler (Inferior inferior)
		{
			if (inferior.ReadInteger (rdebug_state_addr) != 0)
				return false;

			do_update_shlib_info (inferior);
			return false;
		}

		void do_update_shlib_info (Inferior inferior)
		{
			bool first = true;
			TargetAddress map = first_link_map;
			while (!map.IsNull) {
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

		protected class DynlinkBreakpoint : AddressBreakpoint
		{
			protected readonly LinuxOperatingSystem OS;
			public readonly Instruction Instruction;

			public DynlinkBreakpoint (LinuxOperatingSystem os, Instruction instruction)
				: base ("dynlink", ThreadGroup.System, instruction.Address)
			{
				this.OS = os;
				this.Instruction = instruction;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				return true;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				OS.dynlink_handler (inferior);
				if (!Instruction.InterpretInstruction (inferior))
					throw new InternalError ();
				remain_stopped = false;
				return true;
			}
		}

#endregion

#region __nptl_setxid hack

		AddressBreakpoint setxid_breakpoint;

		void check_nptl_setxid (Inferior inferior, Bfd bfd)
		{
			if (setxid_breakpoint != null)
				return;

			TargetAddress vtable = bfd.LookupSymbol ("__libc_pthread_functions");
			if (vtable.IsNull)
				return;

			/*
			 * Big big hack to allow debugging gnome-vfs:
			 * We intercept any calls to __nptl_setxid() and make it
			 * return 0.  This is safe to do since we do not allow
			 * debugging setuid programs or running as root, so setxid()
			 * will always be a no-op anyways.
			 */

			TargetAddress nptl_setxid = inferior.ReadAddress (vtable + 51 * inferior.TargetAddressSize);

			if (!nptl_setxid.IsNull) {
				setxid_breakpoint = new SetXidBreakpoint (this, nptl_setxid);
				setxid_breakpoint.Insert (inferior);
			}
		}

		protected class SetXidBreakpoint : AddressBreakpoint
		{
			protected readonly LinuxOperatingSystem OS;

			public SetXidBreakpoint (LinuxOperatingSystem os, TargetAddress address)
				: base ("setxid", ThreadGroup.System, address)
			{
				this.OS = os;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				return true;
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				inferior.Architecture.Hack_ReturnNull (inferior);
				remain_stopped = false;
				return true;
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
