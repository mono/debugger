using System;
using Math = System.Math;
using System.Text;
using System.Collections;

using Mono.Debugger.Languages;

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

		public override string ToString ()
		{
			return String.Format ("LineEntry ({0}:{1})", Line, Address);
		}
	}

	public abstract class MethodBase : IMethod, ISymbolLookup, IComparable
	{
		TargetAddress start, end;
		TargetAddress method_start, method_end;
		TargetAddress wrapper_addr = TargetAddress.Null;
		MethodSource source;
		Module module;
		bool is_loaded, has_bounds;
		string image_file;
		string name;

		protected MethodBase (string name, string image_file, Module module,
				      TargetAddress start, TargetAddress end)
			: this (name, image_file, module)
		{
			this.start = start;
			this.end = end;
			this.method_start = start;
			this.method_end = end;
			this.is_loaded = true;
		}

		protected MethodBase (string name, string image_file, Module module)
		{
			this.name = name;
			this.image_file = image_file;
			this.module = module;
		}

		protected MethodBase (IMethod method)
			: this (method.Name, method.ImageFile, method.Module,
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

		protected void SetSource (MethodSource source)
		{
			this.source = source;
		}

		protected void SetWrapperAddress (TargetAddress wrapper_addr)
		{
			this.wrapper_addr = wrapper_addr;
		}

		public virtual SimpleStackFrame UnwindStack (SimpleStackFrame frame,
							     ITargetMemoryAccess target,
							     IArchitecture arch)
		{
			if (!IsLoaded)
				return null;

			SimpleStackFrame new_frame = Module.UnwindStack (frame, target);
			if (new_frame != null)
				return new_frame;

			int prologue_size;
			if (HasMethodBounds)
				prologue_size = (int) (MethodStartAddress - StartAddress);
			else
				prologue_size = (int) (EndAddress - StartAddress);
			int offset = (int) (frame.Address - StartAddress);
			prologue_size = Math.Min (prologue_size, offset);
			prologue_size = Math.Min (prologue_size, arch.MaxPrologueSize);

			byte[] prologue = target.ReadBuffer (StartAddress, prologue_size);

			return arch.UnwindStack (frame, prologue, target);
		}

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

		public Module Module {
			get {
				return module;
			}
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

		public bool IsWrapper {
			get {
				return !wrapper_addr.IsNull;
			}
		}

		public TargetAddress WrapperAddress {
			get {
				if (!IsWrapper)
					throw new InvalidOperationException ();

				return wrapper_addr;
			}
		}

		public bool HasSource {
			get {
				return source != null;
			}
		}

		public MethodSource Source {
			get {
				if (!HasSource)
					throw new InvalidOperationException ();

				return source;
			}
		}

		public static bool IsInSameMethod (IMethod method, TargetAddress address)
                {
			if (address.IsNull || !method.IsLoaded)
				return false;

                        if ((address < method.StartAddress) || (address >= method.EndAddress))
                                return false;

                        return true;
                }

		public abstract ITargetStructType DeclaringType {
			get;
		}

		public abstract bool HasThis {
			get;
		}

		public abstract IVariable This {
			get;
		}

		public abstract IVariable[] Parameters {
			get;
		}

		public abstract IVariable[] Locals {
			get;
		}

		public abstract SourceMethod GetTrampoline (ITargetMemoryAccess memory,
							    TargetAddress address);

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
