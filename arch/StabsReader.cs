using System;
using System.Collections;
using Mono.Debugger;
using Mono.Debugger.Languages;
using Mono.Debugger.Languages.Native;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Architecture
{
	internal class StabsReader : SymbolTable, ISimpleSymbolTable
	{
		protected Module module;
		protected ArrayList methods;
		protected Bfd bfd;
		protected SourceFileFactory factory;
		ArrayList files;
		string filename;
		ISymbolRange[] ranges;
		protected TargetAddress entry_point = TargetAddress.Null;

		ObjectCache stabs_reader;
		ObjectCache stabstr_reader;

		public enum StabType : byte {
			N_FUN	= 0x24,
			N_MAIN	= 0x2a,
			N_RSYM	= 0x40,
			N_SLINE	= 0x44,
			N_SO	= 0x64,
			N_PSYM	= 0xa0
		}

		public StabsReader (Bfd bfd, Module module, SourceFileFactory factory)
			: base (bfd.StartAddress, bfd.EndAddress)
		{
			this.bfd = bfd;
			this.module = module;
			this.factory = factory;
			this.filename = bfd.FileName;

			stabs_reader = create_reader ("LC_SYMTAB.stabs");
			stabstr_reader = create_reader ("LC_SYMTAB.stabstr");

			TargetBinaryReader reader = StabTableReader;
			TargetBinaryReader string_reader = StringTableReader;

			files = new ArrayList ();
			methods = new ArrayList ();

			while (reader.Position < reader.Size) {
				Entry entry = new Entry (reader, string_reader);

				if (entry.n_type == (byte) StabType.N_SO) {
					FileEntry fentry = FileEntry.Create (
						this, reader, string_reader, ref entry);
					files.Add (fentry);
				}
			}

			ranges = new ISymbolRange [files.Count];
			for (int i = 0; i < files.Count; i++) {
				ranges [i] = new FileRangeEntry ((FileEntry) files [i]);
			}
		}

		public TargetAddress EntryPoint {
			get { return entry_point; }
		}

		public override bool HasRanges {
			get { return true; }
		}

		public override ISymbolRange[] SymbolRanges {
			get { return ranges; }
		}

		public override bool HasMethods {
			get { return true; }
		}

		protected override ArrayList GetMethods ()
		{
			return methods;
		}

		public override string SimpleLookup (TargetAddress address, bool exact_match)
		{
			if ((address < StartAddress) || (address >= EndAddress))
				return null;

			for (int i = methods.Count - 1; i >= 0; i--) {
				MethodEntry method = (MethodEntry) methods [i];

				if ((address < method.StartAddress) ||
				    (address > method.EndAddress))
					continue;

				long offset = address.Address - method.StartAddress.Address;
				if (offset == 0)
					return String.Format (
						"0x{0:x} <{1}>", address.Address, method.Name);
				else if (exact_match)
					return null;
				else
					return String.Format (
						"0x{0:x} <{1}+0x{2:x}>",
						address.Address, method.Name, offset);
			}

			if (exact_match)
				return null;
			else
				return String.Format ("<{0}:0x{1:x}>", bfd.FileName,
						      address.Address);
		}

		public SourceFile[] Sources {
			get {
				SourceFile[] result = new SourceFile [files.Count];
				files.CopyTo (result, 0);
				return result;
			}
		}

		protected struct Entry {
			public readonly string n_str;
			public readonly byte n_type;
			public readonly byte n_other;
			public readonly short n_ndesc;
			public readonly long n_value;

			public Entry (TargetBinaryReader reader, TargetBinaryReader str_reader)
			{
				int n_strx = reader.ReadInt32 ();
				if (n_strx > 0) {
					str_reader.Position = n_strx;
					n_str = str_reader.ReadString ();
				} else
					n_str = null;

				n_type = reader.ReadByte ();
				n_other = reader.ReadByte ();
				n_ndesc = reader.ReadInt16 ();
				n_value = reader.ReadAddress ();
			}

			public static byte PeekType (TargetBinaryReader reader)
			{
				return reader.PeekByte (reader.Position + 4);
			}

			public override string ToString ()
			{
				return String.Format ("Entry ({0:x}:{1:x}:{2:x}:{3:x}:{4})",
						      n_type, n_other, n_ndesc, n_value, n_str);
			}
		}

		protected class FileRangeEntry : SymbolRangeEntry
		{
			FileEntry file;

			public FileRangeEntry (FileEntry file)
				: base (file.StartAddress, file.EndAddress)
			{
				this.file = file;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return file;
			}
		}

		protected class FileEntry : SourceFile, ISymbolLookup, ISymbolContainer {
			StabsReader stabs;
			public readonly ISourceBuffer SourceBuffer;

			TargetAddress start, end;

			ArrayList lines = new ArrayList ();
			ArrayList methods = new ArrayList ();

			FileEntry (TargetBinaryReader reader, TargetBinaryReader str_reader,
				   StabsReader stabs, ref Entry entry, string name)
				: base (stabs.module, name)
			{
				this.stabs = stabs;

				start = stabs.bfd.GetAddress (entry.n_value);

				while (reader.Position < reader.Size) {
					entry = new Entry (reader, str_reader);

					if (entry.n_type == (byte) StabType.N_SO) {
						end = stabs.bfd.GetAddress (entry.n_value);
						break;
					} else if (entry.n_type == (byte) StabType.N_SLINE)
						lines.Add (new LineNumberEntry (
								   entry.n_ndesc, entry.n_value));
					else if (entry.n_type == (byte) StabType.N_FUN) {
						MethodEntry mentry = new MethodEntry (
							this, reader, str_reader,
							ref entry, ref lines);
						methods.Add (mentry);
					}
				}

				if (lines.Count > 0)
					SourceBuffer = stabs.factory.FindFile (name);
			}

			public static FileEntry Create (StabsReader stabs,
							TargetBinaryReader reader,
							TargetBinaryReader str_reader,
							ref Entry entry)
			{
				string name;
				if (entry.n_str.EndsWith ("/")) {
					string dir = entry.n_str;
					entry = new Entry (reader, str_reader);
					if (entry.n_str.StartsWith ("/"))
						name = entry.n_str;
					else
						name = dir + entry.n_str;
				} else
					name = entry.n_str;

				return new FileEntry (
					reader, str_reader, stabs, ref entry, name);
			}

			public StabsReader StabsReader {
				get { return stabs; }
			}

			bool ISymbolContainer.IsContinuous {
				get { return true; }
			}

			public TargetAddress StartAddress {
				get { return start; }
			}

			public TargetAddress EndAddress {
				get { return end; }
			}

			IMethod ISymbolLookup.Lookup (TargetAddress address)
			{
				foreach (MethodEntry method in methods) {
					if ((address >= method.StartAddress) &&
					    (address < method.EndAddress))
						return method;
				}

				return null;
			}

			protected override ArrayList GetMethods ()
			{
				return null;
			}
		}

		protected class MethodRange : SymbolRangeEntry
		{
			StabsReader stabs;

			public MethodRange (StabsReader stabs,
					    TargetAddress start, TargetAddress end)
				: base (start, end)
			{
				this.stabs = stabs;
			}

			protected override ISymbolLookup GetSymbolLookup ()
			{
				return stabs;
			}
		}

		protected class MethodSourceEntry : MethodSource {
			MethodEntry method;

			public MethodSourceEntry (MethodEntry method)
				: base (method, method.File)
			{
				this.method = method;
			}

			protected override MethodSourceData ReadSource ()
			{
				Bfd bfd = method.File.StabsReader.bfd;
				ArrayList addresses = new ArrayList ();
				foreach (LineNumberEntry line in method.Lines) {
					TargetAddress addr = bfd.GetAddress (line.Offset);
					addresses.Add (new LineEntry (addr, line.Line));
				}

				return new MethodSourceData (
					method.StartLine, method.EndLine, addresses,
					method.File.SourceBuffer);
			}
		}

		protected class MethodEntry : MethodBase {
			public readonly FileEntry File;
			public readonly int StartLine, EndLine;
			public readonly LineNumberEntry[] Lines;
			public readonly MethodRange Range;
			ArrayList parameters;

			static string GetName (string name)
			{
				int pos = name.IndexOf (':');
				if (pos < 0)
					return name;
				else
					return name.Substring (0, pos);
			}

			public MethodEntry (FileEntry file, TargetBinaryReader reader,
					    TargetBinaryReader str_reader,
					    ref Entry entry, ref ArrayList lines)
				: base (GetName (entry.n_str), file.StabsReader.FileName,
					file.StabsReader.module)
			{
				this.File = file;
				this.StartLine = entry.n_ndesc;

				long start_offset = entry.n_value;
				long end_offset = 0;

				Bfd bfd = file.StabsReader.bfd;

				this.Lines = new LineNumberEntry [lines.Count];
				lines.CopyTo (Lines, 0);

				while (reader.Position < reader.Size) {
					entry = new Entry (reader, str_reader);

					if (entry.n_type == (byte) StabType.N_FUN) {
						end_offset = entry.n_value;
						break;
					} else if (entry.n_type == (byte) StabType.N_PSYM) {
						byte next_type = Entry.PeekType (reader);
						MyVariable var;
						if (next_type == (byte) StabType.N_RSYM) {
							Entry next_entry = new Entry (
								reader, str_reader);
							var = new MyVariable (
								ref entry, ref next_entry);
						} else
							var = new MyVariable (ref entry);
						if (parameters == null)
							parameters = new ArrayList ();
						parameters.Add (var);
					}
				}

				TargetAddress start = bfd.GetAddress (start_offset);
				TargetAddress end = bfd.GetAddress (start_offset + end_offset);

				SetAddresses (start, end);

				if (Lines.Length > 0) {
					LineNumberEntry first = Lines [0];
					LineNumberEntry last = Lines [Lines.Length -1];

					TargetAddress mstart = bfd.GetAddress (first.Offset);
					TargetAddress mend = bfd.GetAddress (last.Offset);

					SetMethodBounds (mstart, mend);
				}

				this.Range = new MethodRange (file.StabsReader, start, end);
				SetSource (new MethodSourceEntry (this));

				if (Name == "main")
					file.StabsReader.entry_point = start;

				file.StabsReader.methods.Add (this);

				lines = new ArrayList ();
			}

			public override object MethodHandle {
				get { return null; }
			}

			public override ITargetType DeclaringType {
				get { return null; }
			}

			public override bool HasThis {
				get { return false; }
			}

			public override IVariable This {
				get {
					throw new InvalidOperationException ();
				}
			}

			public override IVariable[] Parameters {
				get {
					if (parameters == null)
						return new IVariable [0];

					IVariable[] result = new IVariable [parameters.Count];
					parameters.CopyTo (result);
					return result;
				}
			}

			public override IVariable[] Locals {
				get { return new IVariable [0]; }
			}

			public override SourceMethod GetTrampoline (ITargetMemoryAccess memory,
								    TargetAddress address)
			{
				return null;
			}

			public override string ToString ()
			{
				return String.Format ("Method ({0}:{1}:{2:x})", Name,
						      StartLine, StartAddress);
			}
		}

		protected class MyVariable : IVariable
		{
			string name;
			NativeType type;
			long offset;
			int register;

			static string GetName (string name)
			{
				int pos = name.IndexOf (':');
				if (pos < 0)
					return name;
				else
					return name.Substring (0, pos);
			}

			public MyVariable (ref Entry entry)
			{
				this.name = GetName (entry.n_str);
				this.type = NativeType.VoidType;

				this.offset = entry.n_value;
				this.register = -1;

				Console.WriteLine (this);
			}

			public MyVariable (ref Entry entry, ref Entry next_entry)
				: this (ref entry)
			{
				this.register = (int) entry.n_value;

				Console.WriteLine (this);
			}

			public string Name {
				get { return name; }
			}

			public ITargetType Type {
				get { return type; }
			}

			public bool IsAlive (TargetAddress address)
			{
				// FIXME
				return false;
			}

			public bool CheckValid (StackFrame frame)
			{
				// FIXME
				return false;
			}

			protected TargetLocation GetAddress (StackFrame frame)
			{
				throw new InvalidOperationException ();
			}

			public ITargetObject GetObject (StackFrame frame)
			{
				TargetLocation location = GetAddress (frame);
				if (location == null)
					return null;

				return type.GetObject (location);
			}

			public override string ToString ()
			{
				return String.Format ("NativeVariable [{0}:{1}:{2}:{3:x}]",
						      Name, Type, register, offset);
			}
		}

		protected class StabsSourceMethod : SourceMethod {
			MethodEntry method;

			public StabsSourceMethod (MethodEntry method)
				: base (method.File, method.Name, method.StartLine,
					method.EndLine, false)
			{
				this.method = method;
			}

			public override bool IsLoaded {
				get { return true; }
			}

			public override IMethod Method {
				get { return method; }
			}

			public override TargetAddress Lookup (int SourceLine)
			{
				foreach (LineNumberEntry line in method.Lines) {
					if (line.Line >= SourceLine)
						return method.StartAddress + line.Offset;
				}

				return TargetAddress.Null;
			}

			internal override IDisposable RegisterLoadHandler (Process process,
									   MethodLoadedHandler handler,
									   object user_data)
			{
				throw new InvalidOperationException ();
			}
		}

		protected struct LineNumberEntry {
			public readonly int Line;
			public readonly long Offset;

			public LineNumberEntry (int line, long offset)
			{
				this.Line = line;
				this.Offset = offset;
			}

			public override string ToString ()
			{
				return String.Format ("Line ({0}:{1:x})", Line, Offset);
			}
		}

		public TargetBinaryReader StabTableReader {
			get {
				return new TargetBinaryReader (
					(TargetBlob) stabs_reader.Data, bfd.TargetInfo);
			}
		}

		public TargetBinaryReader StringTableReader {
			get {
				return new TargetBinaryReader (
					(TargetBlob) stabstr_reader.Data, bfd.TargetInfo);
			}
		}

		public void Read ()
		{ }

		public string FileName {
			get { return filename; }
		}

		object create_reader_func (object user_data)
		{
			byte[] contents = bfd.GetSectionContents ((string) user_data, true);
			if (contents == null)
				throw new StabsException (
					this, "Can't find stabs debugging info");

			return new TargetBlob (contents);
		}

		ObjectCache create_reader (string section_name)
		{
			return new ObjectCache (
				new ObjectCacheFunc (create_reader_func), section_name, 5);
		}

		protected class StabsException : Exception
		{
			public StabsException (StabsReader reader, string message)
				: base (String.Format ("{0}: {1}", reader.FileName, message))
			{ }

			public StabsException (StabsReader reader, string message,
					       Exception inner)
				: base (String.Format ("{0}: {1}", reader.FileName, message),
					inner)
			{ }
		}
	}
}
