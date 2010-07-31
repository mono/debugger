using System;
using System.IO;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class WindowsOperatingSystem : OperatingSystemBackend
	{

		public WindowsOperatingSystem(Process process)
			: base (process)
		{
		}

		internal override void ReadNativeTypes ()
		{
			throw new NotImplementedException();
		}

		public override NativeExecutableReader LookupLibrary (TargetAddress address)
		{
			throw new NotImplementedException();
		}

		public override NativeExecutableReader LoadExecutable (TargetMemoryInfo memory, string filename,
								       bool load_native_symtabs)
		{
			throw new NotImplementedException();
		}

		public override NativeExecutableReader AddExecutableFile (Inferior inferior, string filename,
									  TargetAddress base_address, bool step_into,
									  bool is_loaded)
		{
			throw new NotImplementedException();
		}

		internal override bool CheckForPendingMonoInit (Inferior inferior)
		{
			throw new NotImplementedException();
		}

		public override TargetAddress LookupSymbol (string name)
		{
			throw new NotImplementedException();
		}

		public override NativeExecutableReader LookupLibrary (string name)
		{
			throw new NotImplementedException();
		}

		public override bool GetTrampoline (TargetMemoryAccess memory, TargetAddress address,
						    out TargetAddress trampoline, out bool is_start)
		{
			throw new NotImplementedException();
		}

		public TargetAddress GetSectionAddress (string name)
		{
			throw new NotImplementedException();
		}

#region Dynamic Linking

		bool has_dynlink_info;
		TargetAddress dyld_all_image_infos = TargetAddress.Null;
		TargetAddress rdebug_state_addr = TargetAddress.Null;

		AddressBreakpoint dynlink_breakpoint;

		internal override void UpdateSharedLibraries (Inferior inferior)
		{
			throw new NotImplementedException();
		}

		protected class DynlinkBreakpoint : AddressBreakpoint
		{
			protected readonly WindowsOperatingSystem OS;
			public readonly Instruction Instruction;

			public DynlinkBreakpoint (WindowsOperatingSystem os, Instruction instruction)
				: base ("dynlink", ThreadGroup.System, instruction.Address)
			{
				this.OS = os;
				this.Instruction = instruction;
			}

			public override bool CheckBreakpointHit (Thread target, TargetAddress address)
			{
				throw new NotImplementedException();
			}

			internal override bool BreakpointHandler (Inferior inferior,
								  out bool remain_stopped)
			{
				throw new NotImplementedException();
			}
		}


#endregion

		protected override void DoDispose ()
		{
			throw new NotImplementedException();
		}
	}
}
