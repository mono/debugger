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

		protected BfdDisassembler bfd_disassembler;
		protected IArchitecture arch;

		protected SymbolTableCollection native_symtabs;
		protected SymbolTableCollection symtab_collection;
		protected ISymbolTable application_symtab;
		protected ISourceFileFactory source_factory;

		public CoreFile (string application, string core_file, ISourceFileFactory factory)
		{
			this.source_factory = factory;

			bfd = new Bfd (this, application, false, factory);
			core_bfd = new Bfd (this, core_file, true, null);

			Console.WriteLine ("CORE DUMP FROM: {0}", core_bfd.CrashProgram);

			bfd_disassembler = bfd.GetDisassembler (this);
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

			bfd_disassembler.SymbolTable = symtab_collection;
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

		public IInferiorStackFrame[] GetBacktrace (int max_frames, bool full_backtrace)
		{
			throw new NotImplementedException ();
		}

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

		//
		// ITargetInfo
		//

		public int TargetAddressSize {
			get {
				throw new NotImplementedException ();
			}
		}

		public int TargetIntegerSize {
			get {
				throw new NotImplementedException ();
			}
		}

		public int TargetLongIntegerSize {
			get {
				throw new NotImplementedException ();
			}
		}

		//
		// ITargetMemoryAccess
		//

		public byte ReadByte (TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		public int ReadInteger (TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		public long ReadLongInteger (TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		public TargetAddress ReadAddress (TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		public string ReadString (TargetAddress address)
		{
			throw new NotImplementedException ();
		}

		public ITargetMemoryReader ReadMemory (TargetAddress address, int size)
		{
			throw new NotImplementedException ();
		}

		public byte[] ReadBuffer (TargetAddress address, int size)
		{
			throw new NotImplementedException ();
		}

		public Stream GetMemoryStream (TargetAddress address)
		{
			throw new NotImplementedException ();
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

		//
		// IInferior - everything below throws a CannotExecuteCoreFileException.
		//

		public void Continue ()
		{
			throw new CannotExecuteCoreFileException ();
		}

		public void Continue (TargetAddress until)
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

		public void Step (IStepFrame frame)
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
