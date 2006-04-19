using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Languages;

namespace Mono.Debugger.Backends
{
	internal class CoreFile : ProcessServant
	{
		TargetInfo info;
		Bfd bfd, core_bfd;
		string core_file;
		string application;
		ArrayList threads;

		public static CoreFile OpenCoreFile (ThreadManager manager, ProcessStart start)
		{
			return new CoreFile (manager, start);
		}

		protected CoreFile (ThreadManager manager, ProcessStart start)
			: base (manager, start)
		{
			info = Inferior.GetTargetInfo (manager.AddressDomain);

			bfd = BfdContainer.AddFile (
				info, start.TargetApplication, TargetAddress.Null,
				start.LoadNativeSymbolTable, true);

			core_file = start.CoreFile;
			application = bfd.FileName;

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
			main_thread = ((CoreFileThread) threads [0]).Thread;

			ReachedMain ();
			InitializeModules ();
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
				CoreFileThread thread = new CoreFileThread (this, pid);
				OnThreadCreatedEvent (thread.Thread);
				threads.Add (thread);
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

		public Bfd Bfd {
			get { return bfd; }
		}

		public Bfd CoreBfd {
			get { return core_bfd; }
		}

		public Architecture Architecture {
			get { return bfd.Architecture; }
		}

		public TargetInfo TargetInfo {
			get { return info; }
		}

		protected class CoreFileThread : ThreadServant
		{
			public readonly CoreFile CoreFile;
			public readonly Thread Thread;
			public readonly Registers Registers;
			public readonly BfdDisassembler Disassembler;
			Backtrace current_backtrace;
			StackFrame current_frame;
			Method current_method;
			int pid;

			public CoreFileThread (CoreFile core, int pid)
				: base (core.ThreadManager, core)
			{
				this.pid = pid;
				this.CoreFile = core;
				this.Thread = new Thread (this, pid);

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

			internal override ProcessServant ProcessServant {
				get { return CoreFile; }
			}

			public override TargetInfo TargetInfo {
				get { return CoreFile.TargetInfo; }
			}

			public override int PID {
				get { return pid; }
			}

			public override long TID {
				get { return -1; }
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

			public override Backtrace GetBacktrace (int max_frames)
			{
				current_backtrace = new Backtrace (CurrentFrame);

				current_backtrace.GetBacktrace (
					this, TargetAddress.Null, max_frames);

				return current_backtrace;
			}

			public override TargetState State {
				get { return TargetState.STOPPED; }
			}

			public override StackFrame CurrentFrame {
				get {
					if (current_frame == null)
						current_frame = CoreFile.Architecture.CreateFrame (
							Thread, Registers);

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

			//
			// TargetMemoryAccess
			//

			public override AddressDomain AddressDomain {
				get { return CoreFile.TargetInfo.AddressDomain; }
			}

			internal override Architecture Architecture {
				get { return CoreFile.Architecture; }
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
				return new TargetBlob (ReadBuffer (address, size), TargetInfo);
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

			public override void Finish (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void FinishNative (CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Continue (TargetAddress until, bool in_background,
						       CommandResult result)
			{
				throw new InvalidOperationException ();
			}

			public override void Kill ()
			{
				throw new InvalidOperationException ();
			}

			public override void Detach ()
			{
				throw new InvalidOperationException ();
			}

			public override void Stop ()
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult InsertBreakpoint (Breakpoint breakpoint,
									TargetFunctionType func)
			{
				throw new InvalidOperationException ();
			}

			public override void RemoveBreakpoint (int index)
			{
				throw new InvalidOperationException ();
			}

			public override int AddEventHandler (EventType type, Event handle)
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
				throw new InvalidOperationException ();
			}

			public override string PrintType (Style style, TargetType type)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult RuntimeInvoke (TargetFunctionType function,
								     TargetClassObject object_argument,
								     TargetObject[] param_objects,
								     bool is_virtual, bool debug)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method,
								  TargetAddress arg1,
								  TargetAddress arg2)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method,
								  long method_arg,
								  string string_arg)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult CallMethod (TargetAddress method,
								  TargetAddress arg)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult Return (bool run_finally)
			{
				throw new InvalidOperationException ();
			}

			public override CommandResult AbortInvocation ()
			{
				throw new InvalidOperationException ();
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
