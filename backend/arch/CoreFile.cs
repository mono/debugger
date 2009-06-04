using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Mono;

namespace Mono.Debugger.Backend
{
	internal class CoreFile : ProcessServant
	{
		TargetMemoryInfo info;
		Bfd bfd, core_bfd;
		string core_file;
		ArrayList threads;

		MonoDebuggerInfo debugger_info;

		public static CoreFile OpenCoreFile (ThreadManager manager, ProcessStart start)
		{
			return new CoreFile (manager, start);
		}

		protected CoreFile (ThreadManager manager, ProcessStart start)
			: base (manager, start)
		{
			info = Inferior.GetTargetMemoryInfo (manager.AddressDomain);

			bfd = (Bfd) NativeLanguage.OperatingSystem.LoadExecutable (
				info, start.TargetApplication, true);

			core_file = start.CoreFile;

			core_bfd = bfd.OpenCoreFile (core_file);

#if FIXME
			string crash_program = core_bfd.CrashProgram;
			string[] crash_program_args = crash_program.Split (' ');

			if (crash_program_args [0] != application)
				throw new TargetException (
					TargetError.CannotStartTarget,
					"Core file (generated from {0}) doesn't match executable {1}.",
					crash_program, application);

			bool ok;
			try {
				DateTime core_date = Directory.GetLastWriteTime (core_file);
				DateTime app_date = Directory.GetLastWriteTime (application);

				ok = app_date < core_date;
			} catch {
				ok = false;
			}

			if (!ok)
				throw new TargetException (
					TargetError.CannotStartTarget,
					"Executable {0} is more recent than core file {1}.",
					application, core_file);
#endif

			read_note_section ();
			main_thread = (CoreFileThread) threads [0];

			TargetMemoryAccess target_access = ((CoreFileThread) threads [0]).TargetAccess;
			// bfd.UpdateSharedLibraryInfo (null, target_access);

			TargetAddress mdb_debug_info = bfd.GetSectionAddress (".mdb_debug_info");
			if (!mdb_debug_info.IsNull) {
				mdb_debug_info = main_thread.ReadAddress (mdb_debug_info);
				debugger_info = MonoDebuggerInfo.Create (target_access, mdb_debug_info);
				read_thread_table ();
				CreateMonoLanguage (debugger_info);
				mono_language.InitializeCoreFile (target_access);
				mono_language.Update (target_access);
			}
		}

		void read_thread_table ()
		{
			TargetAddress ptr = main_thread.ReadAddress (debugger_info.ThreadTable);
			while (!ptr.IsNull) {
				int size = 56 + main_thread.TargetMemoryInfo.TargetAddressSize;
				TargetReader reader = new TargetReader (main_thread.ReadMemory (ptr, size));

				long tid = reader.ReadLongInteger ();
				TargetAddress lmf_addr = reader.ReadAddress ();
				reader.ReadAddress (); // end stack

				ptr = reader.ReadAddress ();

				TargetAddress stack_start = reader.ReadAddress ();
				TargetAddress signal_stack_start = reader.ReadAddress ();
				int stack_size = reader.ReadInteger ();
				int signal_stack_size = reader.ReadInteger ();

				bool found = false;
				foreach (CoreFileThread thread in threads) {
					TargetAddress sp = thread.CurrentFrame.StackPointer;

					if ((sp >= stack_start) && (sp < stack_start + stack_size)) {
						thread.SetLMFAddress (tid, lmf_addr);
						found = true;
						break;
					} else if (!signal_stack_start.IsNull &&
						   (sp >= signal_stack_start) &&
						   (sp < signal_stack_start + signal_stack_size)) {
						thread.SetLMFAddress (tid, lmf_addr);
						found = true;
						break;
					}
				}

				if (!found)
					Console.WriteLine ("InternalError: did not find the address for the thread");
			}
		}

		protected TargetReader GetReader (TargetAddress address)
		{
			TargetReader reader = core_bfd.GetReader (address, true);
			if (reader != null)
				return reader;

			NativeExecutableReader exe = NativeLanguage.OperatingSystem.LookupLibrary (address);
			if (exe != null) {
				reader = exe.GetReader (address);
				if (reader != null)
					return reader;
			}

			throw new TargetException (
				TargetError.MemoryAccess, "Memory region containing {0} not in " +
				"core file.", address);
		}

		void read_note_section ()
		{
			threads = new ArrayList ();
			foreach (Bfd.Section section in core_bfd.Sections) {
				if (!section.name.StartsWith (".reg/"))
					continue;

				int pid = Int32.Parse (section.name.Substring (5));
				CoreFileThread thread = new CoreFileThread (this, pid);
				OnThreadCreatedEvent (thread);
				threads.Add (thread);
			}

#if FIXME
			TargetReader reader = core_bfd.GetSectionReader ("note0");
			while (reader.Offset < reader.Size) {
				long offset = reader.Offset;
				int namesz = reader.ReadInteger ();
				int descsz = reader.ReadInteger ();
				int type = reader.ReadInteger ();

				string name = null;
				if (namesz != 0) {
					char[] namebuf = new char [namesz];
					for (int i = 0; i < namesz; i++)
						namebuf [i] = (char) reader.ReadByte ();

					name = new String (namebuf);
				}

				byte[] desc = null;
				if (descsz != 0)
					desc = reader.BinaryReader.ReadBuffer (descsz);

				// Console.WriteLine ("NOTE: {0} {1:x} {2}", offset, type, name);
				// Console.WriteLine (TargetBinaryReader.HexDump (desc));

				reader.Offset += 4 - (reader.Offset % 4);
			}
#endif
		}

		public Bfd Bfd {
			get { return bfd; }
		}

		public Bfd CoreBfd {
			get { return core_bfd; }
		}

		public TargetMemoryInfo TargetMemoryInfo {
			get { return info; }
		}

		protected class CoreFileThread : ThreadServant
		{
			public readonly CoreFile CoreFile;
			public readonly Thread Thread;
			public readonly Registers Registers;
			public readonly TargetMemoryAccess TargetAccess;
			
			Backtrace current_backtrace;
			StackFrame current_frame;
			Method current_method;
			long tid;
			int pid;

			TargetAddress lmf_address = TargetAddress.Null;

			[DllImport("monodebuggerserver")]
			static extern void mono_debugger_server_get_registers_from_core_file (IntPtr values, IntPtr data);

			public CoreFileThread (CoreFile core, int pid)
				: base (core.ThreadManager, core)
			{
				this.pid = pid;
				this.CoreFile = core;
				this.Thread = new Thread (this, ID);

				this.Registers = read_registers ();

				this.TargetAccess = new CoreFileTargetAccess (this);
			}

			Registers read_registers ()
			{
				string sname = String.Format (".reg/{0}", PID);
				TargetReader reader = CoreFile.CoreBfd.GetSectionReader (sname);

				Architecture arch = CoreFile.Architecture;

				IntPtr buffer = IntPtr.Zero;
				IntPtr regs_buffer = IntPtr.Zero;
				try {
					buffer = Marshal.AllocHGlobal ((int) reader.Size);
					Marshal.Copy (reader.Contents, 0, buffer, (int) reader.Size);

					int count = arch.CountRegisters;
					int regs_size = count * 8;
					regs_buffer = Marshal.AllocHGlobal (regs_size);
					mono_debugger_server_get_registers_from_core_file (
						regs_buffer, buffer);
					long[] retval = new long [count];
					Marshal.Copy (regs_buffer, retval, 0, count);

					return new Registers (arch, retval);
				} finally {
					if (buffer != IntPtr.Zero)
						Marshal.FreeHGlobal (buffer);
					if (regs_buffer != IntPtr.Zero)
						Marshal.FreeHGlobal (regs_buffer);
				}
			}

			internal void SetLMFAddress (long tid, TargetAddress lmf)
			{
				this.tid = tid;
				this.lmf_address = lmf;
			}

			internal Disassembler Disassembler {
				get { return CoreFile.Architecture.Disassembler; }
			}

			internal override ThreadManager ThreadManager {
				get { return CoreFile.ThreadManager; }
			}

			internal override ProcessServant ProcessServant {
				get { return CoreFile; }
			}

			public override TargetEventArgs LastTargetEvent {
				get { throw new InvalidOperationException (); }
			}

			public override TargetMemoryInfo TargetMemoryInfo {
				get { return CoreFile.TargetMemoryInfo; }
			}

			internal override object DoTargetAccess (TargetAccessHandler func)
			{
				return func (TargetAccess);
			}

			public override int PID {
				get { return pid; }
			}

			public override long TID {
				get { return tid; }
			}

			public override TargetAddress LMFAddress {
				get { return lmf_address; }
			}

			public override bool IsAlive {
				get { return true; }
			}

			public override bool CanRun {
				get { return false; }
			}

			public override bool CanStep {
				get { return false; }
			}

			public override bool IsStopped {
				get { return true; }
			}

			public override Backtrace GetBacktrace (Backtrace.Mode mode, int max_frames)
			{
				current_backtrace = new Backtrace (CurrentFrame);

				current_backtrace.GetBacktrace (
					this, TargetAccess, mode, TargetAddress.Null, max_frames);

				return current_backtrace;
			}

			public override TargetState State {
				get { return TargetState.CoreFile; }
			}

			public override StackFrame CurrentFrame {
				get {
					if (current_frame == null)
						current_frame = CoreFile.Architecture.CreateFrame (
							Thread, FrameType.Normal, TargetAccess, Registers);

					return current_frame;
				}
			}

			public override Method CurrentMethod {
				get {
					if (current_method == null)
						current_method = Lookup (CurrentFrameAddress);

					return current_method;
				}
			}

			public override TargetAddress CurrentFrameAddress {
				get { return CurrentFrame.TargetAddress; }
			}

			public override Backtrace CurrentBacktrace {
				get { return current_backtrace; }
			}

			public override Registers GetRegisters ()
			{
				return Registers;
			}

			public override int GetInstructionSize (TargetAddress address)
			{
				return Disassembler.GetInstructionSize (TargetAccess, address);
			}

			public override AssemblerLine DisassembleInstruction (Method method,
									      TargetAddress address)
			{
				return Disassembler.DisassembleInstruction (TargetAccess, method, address);
			}

			public override AssemblerMethod DisassembleMethod (Method method)
			{
				return Disassembler.DisassembleMethod (TargetAccess, method);
			}

			public override TargetMemoryArea[] GetMemoryMaps ()
			{
				throw new NotImplementedException ();
			}

			public override Method Lookup (TargetAddress address)
			{
				return CoreFile.SymbolTableManager.Lookup (address);
			}

			public override Symbol SimpleLookup (TargetAddress address, bool exact_match)
			{
				return CoreFile.SymbolTableManager.SimpleLookup (address, exact_match);
			}

			internal override object Invoke (TargetAccessDelegate func, object data)
			{
				throw new InvalidOperationException ();
			}

			internal override void AcquireThreadLock ()
			{
				throw new InvalidOperationException ();
			}

			internal override void ReleaseThreadLock ()
			{
				throw new InvalidOperationException ();
			}

			internal override void ReleaseThreadLockDone ()
			{
				throw new InvalidOperationException ();
			}

			//
			// TargetMemoryAccess
			//

			internal override Architecture Architecture {
				get { return CoreFile.Architecture; }
			}

			public override byte ReadByte (TargetAddress address)
			{
				return CoreFile.GetReader (address).ReadByte ();
			}

			public override int ReadInteger (TargetAddress address)
			{
				return CoreFile.GetReader (address).ReadInteger ();
			}

			public override long ReadLongInteger (TargetAddress address)
			{
				return CoreFile.GetReader (address).ReadLongInteger ();
			}

			public override TargetAddress ReadAddress (TargetAddress address)
			{
				return CoreFile.GetReader (address).ReadAddress ();
			}

			public override string ReadString (TargetAddress address)
			{
				return CoreFile.GetReader (address).BinaryReader.ReadString ();
			}

			public override TargetBlob ReadMemory (TargetAddress address, int size)
			{
				return new TargetBlob (ReadBuffer (address, size), TargetMemoryInfo);
			}

			public override byte[] ReadBuffer (TargetAddress address, int size)
			{
				return CoreFile.GetReader (address).BinaryReader.ReadBuffer (size);
			}

			internal override Inferior.CallbackFrame GetCallbackFrame (TargetAddress stack_pointer,
										   bool exact_match)
			{
				return null;
			}

			public override bool CanWrite {
				get { return false; }
			}

			public override void WriteBuffer (TargetAddress address, byte[] buffer)
			{
				throw new InvalidOperationException ();
			}

			public override void WriteByte (TargetAddress address, byte value)
			{
				throw new InvalidOperationException ();
			}

			public override void WriteInteger (TargetAddress address, int value)
			{
				throw new InvalidOperationException ();
			}

			public override void WriteLongInteger (TargetAddress address, long value)
			{
				throw new InvalidOperationException ();
			}

			public override void WriteAddress (TargetAddress address, TargetAddress value)
			{
				throw new InvalidOperationException ();
			}

			public override void SetRegisters (Registers registers)
			{
				throw new InvalidOperationException ();
			}

			internal override void InsertBreakpoint (BreakpointHandle handle,
								 TargetAddress address, int domain)
			{
				throw new InvalidOperationException ();
			}

			internal override void RemoveBreakpoint (BreakpointHandle handle)
			{
				throw new InvalidOperationException ();
			}

			public override void StepInstruction (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void StepNativeInstruction (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void NextInstruction (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void StepLine (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void NextLine (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Finish (bool native, CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Continue (TargetAddress until, CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Background (TargetAddress until, CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Kill ()
			{ }

			public override void Detach ()
			{
				throw new InvalidOperationException ();
			}

			internal override void DetachThread ()
			{
				throw new InvalidOperationException ();
			}

			public override void Stop ()
			{
				throw new InvalidOperationException ();
			}

			public override int AddEventHandler (Event handle)
			{
				throw new InvalidOperationException ();
			}

			public override void RemoveEventHandler (int index)
			{
				throw new InvalidOperationException ();
			}

			public override string PrintObject (Style style, TargetObject obj,
							    DisplayFormat format)
			{
				return style.FormatObject (Thread, obj, format);
			}

			public override string PrintType (Style style, TargetType type)
			{
				return style.FormatType (Thread, type);
			}

			public override void RuntimeInvoke (TargetFunctionType function,
							    TargetStructObject object_argument,
							    TargetObject[] param_objects,
							    RuntimeInvokeFlags flags,
							    RuntimeInvokeResult result)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method,
								  long arg1, long arg2)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method,
								  long arg1, long arg2, long arg3,
								  string string_arg)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method, TargetAddress method_arg,
								  TargetObject object_arg)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult Return (ReturnMode mode)
			{
				throw new InvalidOperationException ();
			}

			internal override void AbortInvocation (long ID)
			{
				throw new InvalidOperationException ();
			}

			protected class CoreFileTargetAccess : TargetMemoryAccess
			{
				public readonly CoreFileThread Thread;

				public CoreFileTargetAccess (CoreFileThread thread)
				{
					this.Thread = thread;
				}

				public override TargetMemoryInfo TargetMemoryInfo {
					get { return Thread.TargetMemoryInfo; }
				}

				public override AddressDomain AddressDomain {
					get { return Thread.AddressDomain; }
				}

				public override int TargetIntegerSize {
					get {
						return Thread.TargetIntegerSize;
					}
				}

				public override int TargetLongIntegerSize {
					get {
						return Thread.TargetLongIntegerSize;
					}
				}

				public override int TargetAddressSize {
					get {
						return Thread.TargetAddressSize;
					}
				}

				public override bool IsBigEndian {
					get {
				return Thread.IsBigEndian;
					}
				}

				public override byte ReadByte (TargetAddress address)
				{
					return Thread.ReadByte (address);
				}

				public override int ReadInteger (TargetAddress address)
				{
					return Thread.ReadInteger (address);
				}

				public override long ReadLongInteger (TargetAddress address)
				{
					return Thread.ReadLongInteger (address);
				}

				public override TargetAddress ReadAddress (TargetAddress address)
				{
					return Thread.ReadAddress (address);
				}

				public override string ReadString (TargetAddress address)
				{
					return Thread.ReadString (address);
				}

				public override TargetBlob ReadMemory (TargetAddress address, int size)
				{
					return Thread.ReadMemory (address, size);
				}

				public override byte[] ReadBuffer (TargetAddress address, int size)
				{
					return Thread.ReadBuffer (address, size);
				}

				public override Registers GetRegisters ()
				{
					return Thread.GetRegisters ();
				}

				public override bool CanWrite {
					get { return false; }
				}

				public override void WriteBuffer (TargetAddress address, byte[] buffer)
				{
					throw new InvalidOperationException ();
				}

				public override void WriteByte (TargetAddress address, byte value)
				{
					throw new InvalidOperationException ();
				}

				public override void WriteInteger (TargetAddress address, int value)
				{
					throw new InvalidOperationException ();
				}

				public override void WriteLongInteger (TargetAddress address, long value)
				{
					throw new InvalidOperationException ();
				}

				public override void WriteAddress (TargetAddress address,
								   TargetAddress value)
				{
					throw new InvalidOperationException ();
				}

				public override void SetRegisters (Registers registers)
				{
					throw new InvalidOperationException ();
				}
			}
		}

		//
		// IDisposable
		//

		protected override void DoDispose ()
		{
			if (core_bfd != null)
				core_bfd.Dispose ();
			base.DoDispose ();
		}
	}
}
