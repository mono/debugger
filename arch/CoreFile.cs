using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	public abstract class CoreFile : ITargetMemoryAccess
	{
		protected Bfd bfd;
		protected Bfd core_bfd;
		protected BfdContainer bfd_container;

		protected BfdDisassembler bfd_disassembler;
		protected IArchitecture arch;

		DebuggerBackend backend;
		SymbolTableManager symtab_manager;
		ISymbolTable current_symtab;
		ISimpleSymbolTable current_simple_symtab;
		AddressDomain address_domain;
		ILanguage native_language;

		public CoreFile (DebuggerBackend backend, ProcessStart start,
				 string application, string core_file)
		{
			this.backend = backend;
			this.symtab_manager = backend.SymbolTableManager;
			this.bfd_container = backend.BfdContainer;

			arch = new ArchitectureI386 ();

			address_domain = new AddressDomain ("core");

			core_file = Path.GetFullPath (core_file);
			application = Path.GetFullPath (application);

			core_bfd = new Bfd (bfd_container, this, core_file, true, null, TargetAddress.Null);
			bfd = bfd_container.AddFile (this, application, true, TargetAddress.Null, core_bfd);

			core_bfd.MainBfd = bfd;

			bfd_disassembler = bfd.GetDisassembler (this);

			native_language = new Mono.Debugger.Languages.Native.NativeLanguage ((ITargetInfo) this);

			string crash_program = Path.GetFullPath (core_bfd.CrashProgram);
			string[] crash_program_args = crash_program.Split (' ');

			if (crash_program_args [0] != application)
				throw new CannotStartTargetException (String.Format (
					"Core file (generated from {0}) doesn't match executable {1}.",
					crash_program, application));

			bool ok;
			try {
				DateTime core_date = Directory.GetLastWriteTime (core_file);
				DateTime app_date = Directory.GetLastWriteTime (application);

				ok = app_date < core_date;
			} catch {
				ok = false;
			}

			if (!ok)
				throw new CannotStartTargetException (String.Format (
					"Executable {0} is more recent than core file {1}.",
					application, core_file));

			UpdateModules ();
		}

		public DebuggerBackend DebuggerBackend {
			get {
				return backend;
			}
		}

		public ILanguage NativeLanguage {
			get { return native_language; }
		}

		public void UpdateModules ()
		{
			current_symtab = symtab_manager.SymbolTable;
			current_simple_symtab = symtab_manager.SimpleSymbolTable;
		}

		bool has_current_method = false;
		IMethod current_method = null;

		public IMethod CurrentMethod {
			get {
				if (has_current_method)
					return current_method;

				has_current_method = true;
				if (current_symtab == null)
					return null;
				current_method = current_symtab.Lookup (CurrentFrameAddress);
				return current_method;
			}
		}

		bool has_current_frame = false;
		StackFrame current_frame = null;

		public StackFrame CurrentFrame {
			get {
				if (has_current_frame)
					return current_frame;

				TargetAddress address = CurrentFrameAddress;
				IMethod method = CurrentMethod;

				if ((method != null) && method.HasSource) {
					SourceAddress source = method.Source.Lookup (address);

					current_frame = new MyStackFrame (
						this, address, 0, null, source, method);
				} else
					current_frame = new MyStackFrame (
						this, address, 0, null);

				has_current_frame = true;
				return current_frame;
			}
		}

		public string SimpleLookup (TargetAddress address, bool exact_match)
		{
			if (current_simple_symtab == null)
				return null;

			return current_simple_symtab.SimpleLookup (address, exact_match);
		}

		bool has_backtrace = false;
		Backtrace backtrace = null;

		public Backtrace GetBacktrace ()
		{
			return GetBacktrace (-1);
		}

		public Backtrace GetBacktrace (int max_frames)
		{
			if (has_backtrace)
				return backtrace;

			Inferior.StackFrame[] iframes = GetBacktrace (max_frames, TargetAddress.Null);
			StackFrame[] frames = new StackFrame [iframes.Length];

			for (int i = 0; i < iframes.Length; i++) {
				TargetAddress address = iframes [i].Address;

				IMethod method = null;
				if (current_symtab != null)
					method = current_symtab.Lookup (address);
				if ((method != null) && method.HasSource) {
					SourceAddress source = method.Source.Lookup (address);
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i], source, method);
				} else
					frames [i] = new MyStackFrame (
						this, address, i, iframes [i]);
			}

			has_backtrace = true;
			backtrace = new Backtrace (this, arch, frames);
			return backtrace;
		}

		protected class CoreFileStackFrame
		{
			CoreFile core;
			TargetAddress address;
			TargetAddress params_address;
			TargetAddress locals_address;

			public CoreFileStackFrame (CoreFile core, long address,
						   long params_address, long locals_address)
			{
				this.core = core;
				this.address = new TargetAddress (core.AddressDomain, address);
				this.params_address = new TargetAddress (core.AddressDomain, params_address);
				this.locals_address = new TargetAddress (core.AddressDomain, locals_address);
			}

			public TargetAddress Address {
				get {
					return address;
				}
			}

			public TargetAddress ParamsAddress {
				get {
					return params_address;
				}
			}

			public TargetAddress LocalsAddress {
				get {
					return locals_address;
				}
			}
		}

		protected class MyStackFrame : StackFrame
		{
			Inferior.StackFrame frame;
			CoreFile core;
			ILanguage language;

			public MyStackFrame (CoreFile core, TargetAddress address, int level,
					     Inferior.StackFrame frame, SourceAddress source, IMethod method)
				: base (address, level, source, method)
			{
				this.frame = frame;
				this.core = core;
				this.language = method.Module.Language;
			}

			public MyStackFrame (CoreFile core, TargetAddress address, int level,
					     Inferior.StackFrame frame)
				: base (address, level, core.SimpleLookup (address, false))
			{
				this.frame = frame;
				this.core = core;
				this.language = core.NativeLanguage;
			}

			public override Process Process {
				get {
					throw new InvalidOperationException ();
				}
			}

			public override ITargetAccess TargetAccess {
				get {
					return null;
				}
			}

			public override Register[] Registers {
				get {
					return core.GetRegisters ();
				}
			}

			public override ILanguage Language {
				get {
					return language;
				}
			}

			public override TargetLocation GetRegisterLocation (int index, long reg_offset, bool dereference, long offset)
			{
				throw new NotImplementedException ();
			}

			public override void SetRegister (int index, long value)
			{
				throw new InvalidOperationException ();
			}

			protected override AssemblerLine DoDisassembleInstruction (TargetAddress address)
			{
				return core.Disassembler.DisassembleInstruction (Method, address);
			}

			public override AssemblerMethod DisassembleMethod ()
			{
				if (Method == null)
					throw new NoMethodException ();

				return core.Disassembler.DisassembleMethod (Method);
			}

			public override bool RuntimeInvoke (TargetAddress method_argument,
							    TargetAddress object_argument,
							    TargetAddress[] param_objects)
			{
				throw new InvalidOperationException ();
			}

			public override TargetAddress RuntimeInvoke (TargetAddress method_arg,
								     TargetAddress object_arg,
								     TargetAddress[] param,
								     out TargetAddress exc_obj)
			{
				throw new InvalidOperationException ();
			}
		}

		public ITargetMemoryInfo TargetMemoryInfo {
			get { return this; }
		}		

		public ITargetMemoryAccess TargetMemoryAccess {
			get { return this; }
		}

		//
		// ITargetAccess
		//

		protected abstract TargetAddress GetCurrentFrame ();

		public TargetAddress CurrentFrameAddress {
			get {
				return GetCurrentFrame ();
			}
		}

		public TargetAddress SimpleLookup (string name)
		{
			return bfd [name];
		}

		public Register GetRegister (int index)
		{
			foreach (Register register in GetRegisters ()) {
				if (register.Index == index)
					return register;
			}

			throw new NoSuchRegisterException ();
		}

		public abstract Register[] GetRegisters ();

		protected abstract Inferior.StackFrame[] GetBacktrace (int max_frames, TargetAddress stop);

		public IDisassembler Disassembler {
			get {
				check_disposed ();
				return bfd_disassembler;
			}
		}

		public IArchitecture Architecture {
			get {
				check_disposed ();
				return arch;
			}
		}

		public Module[] Modules {
			get {
				return new Module[] { bfd.Module };
			}
		}

		public TargetMemoryArea[] GetMemoryMaps ()
		{
			return core_bfd.GetMemoryMaps ();
		}

		//
		// ITargetInfo
		//

		public int TargetAddressSize {
			get {
				// FIXME
				return 4;
			}
		}

		public int TargetIntegerSize {
			get {
				// FIXME
				return 4;
			}
		}

		public int TargetLongIntegerSize {
			get {
				// FIXME
				return 8;
			}
		}

		//
		// ITargetMemoryAccess
		//

		public AddressDomain GlobalAddressDomain {
			get { return address_domain; }
		}

		public AddressDomain AddressDomain {
			get { return address_domain; }
		}

		public byte ReadByte (TargetAddress address)
		{
			return core_bfd.GetReader (address).ReadByte ();
		}

		public int ReadInteger (TargetAddress address)
		{
			return core_bfd.GetReader (address).ReadInteger ();
		}

		public long ReadLongInteger (TargetAddress address)
		{
			return core_bfd.GetReader (address).ReadLongInteger ();
		}

		public TargetAddress ReadAddress (TargetAddress address)
		{
			return core_bfd.GetReader (address).ReadAddress ();
		}

		public TargetAddress ReadGlobalAddress (TargetAddress address)
		{
			return core_bfd.GetReader (address).ReadGlobalAddress ();
		}

		public string ReadString (TargetAddress address)
		{
			return core_bfd.GetReader (address).BinaryReader.ReadString ();
		}

		public ITargetMemoryReader ReadMemory (TargetAddress address, int size)
		{
			return new TargetReader (ReadBuffer (address, size), this);
		}

		public ITargetMemoryReader ReadMemory (byte[] buffer)
		{
			return new TargetReader (buffer, this);
		}

		public byte[] ReadBuffer (TargetAddress address, int size)
		{
			return core_bfd.GetReader (address).BinaryReader.ReadBuffer (size);
		}

		public bool CanWrite {
			get {
				return false;
			}
		}

		public TargetAddress MainMethodAddress {
			get {
				throw new NotImplementedException ();
			}
		}

		public TargetAddress GetReturnAddress ()
		{
			throw new NotImplementedException ();
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
			if (bfd_container != null)
				bfd_container.CloseBfd (bfd);
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
