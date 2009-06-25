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
			foreach (Bfd bfd in bfd_hash.Values) {
				if (bfd == null)
					continue;
				bfd.ReadTypes ();
			}
		}

		public override NativeExecutableReader LookupLibrary (TargetAddress address)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				if (bfd == null)
					continue;
					
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
			if (!Process.MonoRuntimeFound)
				check_for_mono_runtime (inferior, bfd);
		}

		TargetAddress pending_mono_init = TargetAddress.Null;

		void check_for_mono_runtime (Inferior inferior, Bfd bfd)
		{
			TargetAddress info = bfd.LookupSymbol ("MONO_DEBUGGER__debugger_info_ptr");
			if (info.IsNull)
				return;

			TargetAddress data = inferior.ReadAddress (info);
			if (data.IsNull || true) {
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

				// Add a breakpoint in mini_debugger_init, to make sure that InitializeMono()
				// gets called in time to set the breakpoint at debugger_initialize, needed to 
				// initialize the notifications.
				TargetAddress mini_debugger_init = bfd.LookupSymbol ("mini_debugger_init");
				if (!mini_debugger_init.IsNull)
				{
					Instruction insn = inferior.Architecture.ReadInstruction (inferior, mini_debugger_init);
					if ((insn == null) || !insn.CanInterpretInstruction)
						throw new InternalError ("Unknown dynlink breakpoint: {0}", mini_debugger_init);

					DynlinkBreakpoint init_breakpoint = new DynlinkBreakpoint (this, insn);
					init_breakpoint.Insert (inferior);
				}
				return;
			}
			
			Process.InitializeMono (inferior, data);
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
				if (bfd == null)
					continue;
					
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
				if (bfd == null)
					continue;
					
				if (Path.GetFileName (bfd.FileName) == name)
					return bfd;
			}

			return null;
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
			foreach (Bfd bfd in bfd_hash.Values) {
				if (bfd == null)
					continue;
					
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
				if (bfd == null)
					continue;

				TargetAddress address = bfd.GetSectionAddress (name);
				if (!address.IsNull)
					return address;
			}

			return TargetAddress.Null;
		}

#region Dynamic Linking

		bool has_dynlink_info;
		TargetAddress dyld_all_image_infos = TargetAddress.Null;
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
				if (!dyld_all_image_infos.IsNull)
					do_update_shlib_info (inferior);
				return;
			}

			TargetMemoryInfo info = Inferior.GetTargetMemoryInfo (AddressDomain.Global);			Bfd dyld_image = new Bfd (this, info, "/usr/lib/dyld", TargetAddress.Null, true);

			dyld_all_image_infos = dyld_image.LookupSymbol("dyld_all_image_infos");
			if (dyld_all_image_infos.IsNull)
				return;
			

			int size = 2 * inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize;
			TargetReader reader = new TargetReader (inferior.ReadMemory (dyld_all_image_infos, size));

			reader.ReadLongInteger (); // version
			reader.ReadLongInteger (); // infoArrayCount
			reader.ReadAddress (); // infoArray
			TargetAddress dyld_image_notifier = reader.ReadAddress ();

			has_dynlink_info = true;

			Instruction insn = inferior.Architecture.ReadInstruction (inferior, dyld_image_notifier);
			if ((insn == null) || !insn.CanInterpretInstruction)
				throw new InternalError ("Unknown dynlink breakpoint: {0}", dyld_image_notifier);

			dynlink_breakpoint = new DynlinkBreakpoint (this, insn);
			dynlink_breakpoint.Insert (inferior);

			do_update_shlib_info (inferior);

			check_loaded_library (inferior, main_bfd);
		}

		void do_update_shlib_info (Inferior inferior)
		{
			if (!dyld_all_image_infos.IsNull) {
				int size = 2 * inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize;
				TargetReader reader = new TargetReader (inferior.ReadMemory (dyld_all_image_infos, size));

				reader.ReadLongInteger (); // version
				int infoArrayCount = (int)reader.ReadLongInteger ();
				TargetAddress infoArray = reader.ReadAddress ();

				size = infoArrayCount * (inferior.TargetLongIntegerSize + 2 * inferior.TargetAddressSize);
				reader = new TargetReader (inferior.ReadMemory (infoArray, size));
				Console.Write("Loading symbols for shared libraries:");
				for (int i=0; i<infoArrayCount; i++)
				{
					TargetAddress imageLoadAddress = reader.ReadAddress();
					TargetAddress imageFilePath = reader.ReadAddress();
					reader.ReadLongInteger(); //imageFileModDate
					string name = inferior.ReadString (imageFilePath);

					if (name == null)
						continue;
	
					if (bfd_hash.Contains (name))
						continue;
					
					try {
						Console.Write(".");
						AddExecutableFile (inferior, name, imageLoadAddress/*TargetAddress.Null*/, false, true);
					}
					catch (SymbolTableException e)
					{
						Console.WriteLine("Unable to load binary for "+name);
						bfd_hash.Add (name, null);
					}
				}	
				Console.WriteLine("");
			}
		}

		protected class DynlinkBreakpoint : AddressBreakpoint
		{
			protected readonly DarwinOperatingSystem OS;
			public readonly Instruction Instruction;

			public DynlinkBreakpoint (DarwinOperatingSystem os, Instruction instruction)
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
				OS.do_update_shlib_info (inferior);
				if (!Instruction.InterpretInstruction (inferior))
					throw new InternalError ();
				remain_stopped = false;
				return true;
			}
		}


#endregion

		protected override void DoDispose ()
		{
			if (bfd_hash != null) {
				foreach (Bfd bfd in bfd_hash.Values) {
					if (bfd == null)
						continue;
					bfd.Dispose ();
				}
				bfd_hash = null;
			}
		}
	}
}
