using System;
using System.Text;
using System.Collections;

namespace Mono.Debugger
{
	public abstract class MethodSource : IMethodSource
	{
		SourceFile file;
		SourceMethod source_method;
		int start_row, end_row;
		bool sources_read;
		string name;

		TargetAddress start, end;
		TargetAddress method_start, method_end;

		protected MethodSource (IMethod method, SourceFile file)
		{
			this.file = file;
			this.start = method.StartAddress;
			this.end = method.EndAddress;
			this.name = method.Name;

			if (method.HasMethodBounds) {
				method_start = method.MethodStartAddress;
				method_end = method.MethodEndAddress;
			} else {
				method_start = start;
				method_end = end;
			}
		}

		protected MethodSource (TargetAddress start, TargetAddress end)
		{
			this.start = this.method_start = start;
			this.end = this.method_end = end;
			this.name = "<unknown>";
		}

		protected virtual void SetEndAddress (TargetAddress end)
		{
			this.end = this.method_end = end;
		}

		ObjectCache source_cache = null;
		object read_source (object user_data)
		{
			return ReadSource ();
		}

		protected MethodSourceData SourceData {
			get {
				if (source_cache == null)
					source_cache = new ObjectCache
						(new ObjectCacheFunc (read_source), null, 5);

				return (MethodSourceData) source_cache.Data;
			}
		}

		protected abstract MethodSourceData ReadSource ();

		//
		// IMethodSource
		//

		public virtual string Name {
			get {
				if (IsDynamic)
					return SourceBuffer.Name;
				else
					return SourceFile.Name;
			}
		}

		public bool IsDynamic {
			get {
				return file == null;
			}
		}

		public ISourceBuffer SourceBuffer {
			get {
				return SourceData.SourceBuffer;
			}
		}

		public SourceFile SourceFile {
			get {
				if (IsDynamic)
					throw new InvalidOperationException ();

				return file;
			}
		}

		public SourceMethod SourceMethod {
			get {
				if (IsDynamic)
					throw new InvalidOperationException ();

				return SourceData.SourceMethod;
			}
		}

		protected LineEntry[] Addresses {
			get {
				return SourceData.Addresses;
			}
		}

		public virtual int StartRow {
			get {
				return SourceData.StartRow;
			}
		}

		public virtual int EndRow {
			get {
				return SourceData.EndRow;
			}
		}

		public TargetAddress Lookup (int line)
		{
			ReadSource ();
			if ((Addresses == null) || (line < StartRow) || (line > EndRow))
				return TargetAddress.Null;

			for (int i = 0; i < Addresses.Length; i++) {
				LineEntry entry = (LineEntry) Addresses [i];

				if (line <= entry.Line)
					return entry.Address;
			}

			return TargetAddress.Null;
		}

		public SourceAddress Lookup (TargetAddress address)
		{
			ReadSource ();
			if (address.IsNull || (address < start) || (address >= end))
				return null;

			if (address < method_start)
				return new SourceAddress (
					this, StartRow, (int) (address - start),
					(int) (method_start - address));
			else if (address >= method_end)
				return new SourceAddress (
					this, EndRow, (int) (address - method_end),
					(int) (end - address));			

			TargetAddress next_address = end;

			for (int i = Addresses.Length-1; i >= 0; i--) {
				LineEntry entry = (LineEntry) Addresses [i];

				int range = (int) (next_address - address);
				next_address = entry.Address;

				if (next_address > address)
					continue;

				int offset = (int) (address - next_address);

				return new SourceAddress (this, entry.Line, offset, range);
			}

			return null;
		}

		public abstract SourceMethod[] MethodLookup (string query);

		public void DumpLineNumbers ()
		{
			Console.WriteLine ("--------");
			Console.WriteLine ("DUMPING LINE NUMBER TABLE");
			Console.WriteLine ("BOUNDS: start = {0} / {1}, end = {2} / {3}", 
					   start, method_start, end, method_end);
			Console.WriteLine ("SOURCE BOUNDS: start = {0}, end = {1}", start_row, end_row);
			Console.WriteLine ("--------");
			for (int i = 0; i < Addresses.Length; i++) {
				LineEntry entry = (LineEntry) Addresses [i];
				Console.WriteLine ("{0,4} {1,4}  {2}", i, entry.Line, entry.Address);
			}
			Console.WriteLine ("--------");
		}

		public class MethodSourceData
		{
			public readonly int StartRow;
			public readonly int EndRow;
			public readonly LineEntry[] Addresses;
			public readonly SourceMethod SourceMethod;
			public readonly ISourceBuffer SourceBuffer;

			public MethodSourceData (int start, int end, ArrayList addresses, ISourceBuffer buffer)
				: this (start, end, addresses, null, buffer)
			{ }

			public MethodSourceData (int start, int end, ArrayList addresses,
						 SourceMethod method, ISourceBuffer buffer)
			{
				this.StartRow = start;
				this.EndRow = end;
				this.SourceMethod = method;
				this.SourceBuffer = buffer;

				Addresses = new LineEntry [addresses.Count];
				addresses.CopyTo (Addresses, 0);
			}
		}
	}
}
