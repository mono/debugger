using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal abstract class CoreFile : IInferior
	{
		protected Bfd bfd;
		protected Bfd core_bfd;
		protected BfdContainer bfd_container;

		protected BfdDisassembler bfd_disassembler;
		protected IArchitecture arch;

		protected SymbolTableCollection native_symtabs;
		protected SymbolTableCollection symtab_collection;
		protected ISymbolTable application_symtab;

		public CoreFile (string application, string core_file, BfdContainer bfd_container)
		{
			bfd = bfd_container.AddFile (this, application, false);
			core_bfd = new Bfd (this, core_file, true, false, null);

			Console.WriteLine ("CORE DUMP FROM: {0}", core_bfd.CrashProgram);

			// bfd_disassembler = bfd.GetDisassembler (this);
			arch = new ArchitectureI386 (this);

			native_symtabs = new SymbolTableCollection ();

			try {
				ISymbolTable bfd_symtab = bfd.SymbolTable;
				if (bfd_symtab != null)
					native_symtabs.AddSymbolTable (bfd_symtab);
			} catch (Exception e) {
				Console.WriteLine ("Can't get native symbol table: {0}", e);
			}

			update_symtabs ();
		}

		void update_symtabs ()
		{
			symtab_collection = new SymbolTableCollection ();
			symtab_collection.AddSymbolTable (native_symtabs);
			symtab_collection.AddSymbolTable (application_symtab);

			if (bfd_disassembler != null)
				bfd_disassembler.SymbolTable = symtab_collection;
		}

		protected class CoreFileStackFrame : IInferiorStackFrame
		{
			IInferior inferior;
			TargetAddress address;
			TargetAddress params_address;
			TargetAddress locals_address;

			public CoreFileStackFrame (IInferior inferior, long address,
						   long params_address, long locals_address)
			{
				this.inferior = inferior;
				this.address = new TargetAddress (inferior, address);
				this.params_address = new TargetAddress (inferior, params_address);
				this.locals_address = new TargetAddress (inferior, locals_address);
			}

			public IInferior Inferior {
				get {
					return inferior;
				}
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

		//
		// IInferior
		//

		public abstract TargetAddress CurrentFrame {
			get;
		}

		public TargetAddress SimpleLookup (string name)
		{
			return bfd [name];
		}

		public abstract long GetRegister (int register);

		public abstract long[] GetRegisters (int[] registers);

		public abstract IInferiorStackFrame[] GetBacktrace (int max_frames, TargetAddress stop);

		public IDisassembler Disassembler {
			get {
				check_disposed ();
				return bfd_disassembler;
			}
		}

		public ISymbolTable SymbolTable {
			get {
				check_disposed ();
				return native_symtabs;
			}
		}

		public ISymbolTable ApplicationSymbolTable {
			get {
				check_disposed ();
				return application_symtab;
			}

			set {
				check_disposed ();
				application_symtab = value;
				update_symtabs ();
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

		//
		// ITargetNotification
		//

		public TargetState State {
			get {
				return TargetState.CORE_FILE;
			}
		}

		public event TargetOutputHandler TargetOutput;
		public event TargetOutputHandler TargetError;
		public event StateChangedHandler StateChanged;
		public event TargetExitedHandler TargetExited;
		public event ChildEventHandler ChildEvent;

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

		public string ReadString (TargetAddress address)
		{
			return core_bfd.GetReader (address).BinaryReader.ReadString ();
		}

		public ITargetMemoryReader ReadMemory (TargetAddress address, int size)
		{
			return new TargetReader (ReadBuffer (address, size), this);
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

		public void WriteBuffer (TargetAddress address, byte[] buffer, int size)
		{
			throw new InvalidOperationException ();
		}

		public void WriteByte (TargetAddress address, byte value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteInteger (TargetAddress address, int value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteLongInteger (TargetAddress address, long value)
		{
			throw new InvalidOperationException ();
		}

		public void WriteAddress (TargetAddress address, TargetAddress value)
		{
			throw new InvalidOperationException ();
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
		// IInferior - everything below throws a CannotExecuteCoreFileException.
		//

		public SingleSteppingEngine SingleSteppingEngine {
			get {
				throw new CannotExecuteCoreFileException ();
			}

			set {
				throw new CannotExecuteCoreFileException ();
			}
		}

		public bool CurrentInstructionIsBreakpoint {
			get {
				throw new CannotExecuteCoreFileException ();
			}
		}

		public void Continue ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void Shutdown ()
		{
			// Do nothing.
		}

		public void Kill ()
		{
			// Do nothing.
		}

		public void Step ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void Stop ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		public long CallMethod (TargetAddress method, long method_argument)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public long CallStringMethod (TargetAddress method, long method_argument,
					      string string_argument)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public TargetAddress CallInvokeMethod (TargetAddress invoke_method,
						       TargetAddress method_argument,
						       TargetAddress object_argument,
						       TargetAddress[] param_objects,
						       out TargetAddress exc_object)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public int InsertBreakpoint (TargetAddress address)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void RemoveBreakpoint (int breakpoint)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void EnableBreakpoint (int breakpoint)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void DisableBreakpoint (int breakpoint)
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void EnableAllBreakpoints ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void DisableAllBreakpoints ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		//
		// IDisposable
		//

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("Inferior");
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			if (!this.disposed) {
				// If this is a call to Dispose,
				// dispose all managed resources.
				if (disposing) {
					bfd_container.CloseBfd (bfd);
					if (core_bfd != null)
						core_bfd.Dispose ();
				}
				
				this.disposed = true;

				lock (this) {
					// Release unmanaged resources
				}
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
