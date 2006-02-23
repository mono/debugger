using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Remoting;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal class CoreFile : MarshalByRefObject
	{
		ITargetInfo info;
		ITargetMemoryInfo memory_info;
		DebuggerManager debugger_manager;
		CoreFileThread main_thread;
		ThreadManager manager;
		Bfd bfd, core_bfd;
		string core_file;
		string application;
		ArrayList threads;

		public CoreFile (ThreadManager manager, Inferior inferior, Bfd bfd, string core_file)
		{
			this.manager = manager;
			this.debugger_manager = manager.Debugger.DebuggerManager;
			this.info = inferior.TargetInfo;
			this.memory_info = inferior.TargetMemoryInfo;
			this.bfd = bfd;
			this.core_file = core_file;
			this.application = bfd.FileName;

			core_bfd = bfd.OpenCoreFile (core_file);

			string crash_program = core_bfd.CrashProgram;
			string[] crash_program_args = crash_program.Split (' ');

#if FIXME
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
		}

		public void InitializeModules ()
		{
			bfd.UpdateSharedLibraryInfo (null, main_thread);
		}

		void read_note_section ()
		{
			threads = new ArrayList ();
			foreach (Bfd.Section section in core_bfd.Sections) {
				if (!section.name.StartsWith (".reg/"))
					continue;

				int pid = Int32.Parse (section.name.Substring (5));
				threads.Add (new CoreFileThread (this, pid));
			}

			return;

#if FIXME
			TargetReader reader = core_bfd.GetSectionReader ("note0", true);
			while (reader.Offset < reader.Size) {
				long offset = reader.Offset;
				int namesz = reader.ReadInteger ();
				int descsz = reader.ReadInteger ();
				int type = reader.ReadInteger ();

				Console.WriteLine ("NOTE: {0} {1} {2} {3:x}", offset, namesz,
						   descsz, type);

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

				Console.WriteLine ("NOTE #1: {0} {1} {2:x} - {3} {4}", namesz, descsz,
						   type, name, TargetBinaryReader.HexDump (desc));

				reader.Offset += 4 - (reader.Offset % 4);
			}
#endif
		}

		public ThreadManager ThreadManager {
			get { return manager; }
		}

		public DebuggerManager DebuggerManager {
			get { return debugger_manager; }
		}

		public Process MainProcess {
			get { return main_thread.Process; }
		}

		public Process[] Threads {
			get {
				Process[] retval = new Process [threads.Count];
				for (int i = 0; i < threads.Count; i++) {
					CoreFileThread thread = (CoreFileThread) threads [i];
					retval [i] = thread.Process;
				}
				return retval;
			}
		}

		public Bfd Bfd {
			get { return bfd; }
		}

		public Bfd CoreBfd {
			get { return core_bfd; }
		}

		public Architecture Architecture {
			get { return bfd.Architecture; }
		}

		public ITargetInfo TargetInfo {
			get { return info; }
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get { return memory_info; }
		}

		protected class CoreFileThread : ThreadBase, ITargetMemoryAccess
		{
			public readonly int PID;
			public readonly CoreFile CoreFile;
			public readonly Process Process;
			public readonly TargetAccess TargetAccess;
			public readonly Registers Registers;
			public readonly BfdDisassembler Disassembler;
			Backtrace current_backtrace;
			StackFrame current_frame;

			public CoreFileThread (CoreFile core, int pid)
			{
				this.PID = pid;
				this.CoreFile = core;
				this.Process = core.DebuggerManager.CreateProcess (this, pid);
				this.TargetAccess = new ThreadTargetAccess (
					this, this, Process.ID, Process.Name);

				this.Disassembler = core.CoreBfd.GetDisassembler (this);
				this.Registers = read_registers ();
			}

			Registers read_registers ()
			{
				string sname = String.Format (".reg/{0}", PID);
				TargetReader reader = CoreFile.CoreBfd.GetSectionReader (sname, true);

				Architecture arch = CoreFile.Architecture;
				long[] values = new long [arch.CountRegisters];
				for (int i = 0; i < values.Length; i++) {
					int size = arch.RegisterSizes [i];
					if (size == 4)
						values [i] = reader.BinaryReader.ReadInt32 ();
					else if (size == 8)
						values [i] = reader.BinaryReader.ReadInt64 ();
					else
						throw new InternalError ();
				}

				return new Registers (arch, values);
			}

			internal override ThreadManager ThreadManager {
				get { return CoreFile.ThreadManager; }
			}

			public override ITargetInfo TargetInfo {
				get { return CoreFile.TargetInfo; }
			}

			public override ITargetMemoryInfo TargetMemoryInfo {
				get { return CoreFile.TargetMemoryInfo; }
			}

			public override Backtrace GetBacktrace (int max_frames)
			{
				current_backtrace = new Backtrace (CurrentFrame);

				current_backtrace.GetBacktrace (
					TargetAccess, Architecture, TargetAddress.Null, max_frames);

				return current_backtrace;
			}

			public override TargetState State {
				get { return TargetState.STOPPED; }
			}

			public override StackFrame CurrentFrame {
				get {
					if (current_frame == null)
						current_frame = CoreFile.Architecture.CreateFrame (
							Process, TargetAccess, CoreFile.TargetMemoryInfo,
							Registers);

					return current_frame;
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
				return Disassembler.GetInstructionSize (address);
			}

			public override AssemblerLine DisassembleInstruction (Method method,
									      TargetAddress address)
			{
				return Disassembler.DisassembleInstruction (method, address);
			}

			public override AssemblerMethod DisassembleMethod (Method method)
			{
				return Disassembler.DisassembleMethod (method);
			}

			//
			// ITargetMemoryAccess
			//

			public override int TargetIntegerSize {
				get { return CoreFile.TargetInfo.TargetIntegerSize; }
			}

			public override int TargetLongIntegerSize {
				get { return CoreFile.TargetInfo.TargetLongIntegerSize; }
			}

			public override int TargetAddressSize {
				get { return CoreFile.TargetInfo.TargetAddressSize; }
			}

			public override bool IsBigEndian {
				get { return CoreFile.TargetInfo.IsBigEndian; }
			}

			public override Architecture Architecture {
				get { return CoreFile.TargetMemoryInfo.Architecture; }
			}

			public override AddressDomain AddressDomain {
				get { return CoreFile.TargetMemoryInfo.AddressDomain; }
			}

			public override byte ReadByte (TargetAddress address)
			{
				return CoreFile.CoreBfd.GetReader (address).ReadByte ();
			}

			public override int ReadInteger (TargetAddress address)
			{
				return CoreFile.CoreBfd.GetReader (address).ReadInteger ();
			}

			public override long ReadLongInteger (TargetAddress address)
			{
				return CoreFile.CoreBfd.GetReader (address).ReadLongInteger ();
			}

			public override TargetAddress ReadAddress (TargetAddress address)
			{
				return CoreFile.CoreBfd.GetReader (address).ReadAddress ();
			}

			public override string ReadString (TargetAddress address)
			{
				return CoreFile.CoreBfd.GetReader (address).BinaryReader.ReadString ();
			}

			public override TargetBlob ReadMemory (TargetAddress address, int size)
			{
				return new TargetBlob (ReadBuffer (address, size), this);
			}

			public override byte[] ReadBuffer (TargetAddress address, int size)
			{
				return CoreFile.CoreBfd.GetReader (address).BinaryReader.ReadBuffer (size);
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

			public override int InsertBreakpoint (Breakpoint breakpoint, TargetAddress address)
			{
				throw new InvalidOperationException ();
			}
		}

		//
		// IDisposable
		//

		private bool disposed = false;

		protected void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Process");
		}

		protected virtual void DoDispose ()
		{
			if (core_bfd != null)
				core_bfd.Dispose ();
		}

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (disposed)
				return;

			lock (this) {
				if (disposed)
					return;

				disposed = true;
			}

			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				DoDispose ();
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~CoreFile ()
		{
			Dispose (false);
		}
	}
}
