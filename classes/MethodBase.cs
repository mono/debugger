using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public struct LineEntry : IComparable {
		public readonly TargetAddress Address;
		public readonly int Line;

		public LineEntry (TargetAddress address, int line)
		{
			this.Address = address;;
			this.Line = line;
		}

		public int CompareTo (object obj)
		{
			LineEntry entry = (LineEntry) obj;

			if (entry.Address < Address)
				return 1;
			else if (entry.Address > Address)
				return -1;
			else
				return 0;
		}
	}

	public abstract class MethodBase : IMethod, IMethodSource, ISymbolLookup, IComparable
	{
		ArrayList addresses;
		ObjectCache source;
		int start_row, end_row;
		TargetAddress start, end;
		TargetAddress method_start, method_end;
		bool is_loaded, has_bounds;
		bool has_source;
		string image_file;
		string name;

		protected MethodBase (string name, string image_file,
				      TargetAddress start, TargetAddress end)
			: this (name, image_file)
		{
			this.start = start;
			this.end = end;
			this.is_loaded = true;
		}

		protected MethodBase (string name, string image_file)
			: this (name, image_file, true)
		{ }

		protected MethodBase (string name, string image_file, bool has_source)
		{
			this.name = name;
			this.image_file = image_file;
			this.has_source = has_source;
		}

		protected MethodBase (IMethod method)
			: this (method.Name, method.ImageFile,
				method.StartAddress, method.EndAddress)
		{ }

		protected void SetAddresses (TargetAddress start, TargetAddress end)
		{
			this.start = start;
			this.end = end;
			this.is_loaded = true;
			this.has_bounds = false;
		}

		protected void SetMethodBounds (TargetAddress method_start, TargetAddress method_end)
		{
			this.method_start = method_start;
			this.method_end = method_end;
			this.has_bounds = true;
		}

		object read_source (object user_data)
		{
			return ReadSource (out start_row, out end_row, out addresses);
		}

		protected ISourceBuffer ReadSource ()
		{
			if (source == null)
				source = new ObjectCache (new ObjectCacheFunc (read_source), null,
							  new TimeSpan (0,1,0));

			return (ISourceBuffer) source.Data;
		}

		protected abstract ISourceBuffer ReadSource (out int start_row, out int end_row,
							     out ArrayList addresses);


		//
		// IMethod
		//

		public string Name {
			get {
				return name;
			}
		}

		public string ImageFile {
			get {
				return image_file;
			}
		}

		public abstract ILanguageBackend Language {
			get;
		}

		public abstract object MethodHandle {
			get;
		}

		public bool IsLoaded {
			get {
				return is_loaded;
			}
		}

		public bool HasMethodBounds {
			get {
				return has_bounds;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return start;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!is_loaded)
					throw new InvalidOperationException ();

				return end;
			}
		}

		public TargetAddress MethodStartAddress {
			get {
				if (!has_bounds)
					throw new InvalidOperationException ();

				return method_start;
			}
		}

		public TargetAddress MethodEndAddress {
			get {
				if (!has_bounds)
					throw new InvalidOperationException ();

				return method_end;
			}
		}

		public bool HasSource {
			get {
				return has_source;
			}
		}

		public IMethodSource Source {
			get {
				if (!HasSource)
					throw new InvalidOperationException ();

				return this;
			}
		}

		//
		// IMethodSource
		//

		public ISourceBuffer SourceBuffer {
			get {
				return ReadSource ();
			}
		}

		public int StartRow {
			get {
				return start_row + 1;
			}
		}

		public int EndRow {
			get {
				return end_row;
			}
		}

		public static bool IsInSameMethod (IMethod method, TargetAddress address)
		{
			if ((address < method.StartAddress) || (address >= method.EndAddress))
				return false;

			return true;
		}

		public ISourceLocation Lookup (TargetAddress address)
		{
			if (!IsInSameMethod (this, address))
				return null;

			ISourceBuffer source = ReadSource ();
			if (source == null)
				return null;

			TargetAddress next_address = end;

			for (int i = addresses.Count-1; i >= 0; i--) {
				LineEntry entry = (LineEntry) addresses [i];

				int range = (int) (next_address - address);
				next_address = entry.Address;

				if (next_address > address)
					continue;

				int offset = (int) (address - next_address);

				return new SourceLocation (source, entry.Line, offset, range);
			}

			return null;
		}

		//
		// ISourceLookup
		//

		IMethod ISymbolLookup.Lookup (TargetAddress address)
		{
			if (!is_loaded)
				return null;

			if ((address < start) || (address >= end))
				return null;

			return this;
		}

		public int CompareTo (object obj)
		{
			IMethod method = (IMethod) obj;

			TargetAddress address;
			try {
				address = method.StartAddress;
			} catch {
				return is_loaded ? -1 : 0;
			}

			if (!is_loaded)
				return 1;

			if (address < start)
				return 1;
			else if (address > start)
				return -1;
			else
				return 0;
		}

		public override string ToString ()
		{
			return String.Format ("{0}({1},{2:x},{3:x})", GetType (), name, start, end);
		}
	}
}
