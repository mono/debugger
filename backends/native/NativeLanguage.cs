using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Architecture;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeLanguage : ILanguage
	{
		BfdContainer bfd_container;
		NativeFundamentalType integer_type;
		NativeFundamentalType long_type;
		NativePointerType pointer_type;

		public NativeLanguage (BfdContainer bfd_container, ITargetInfo info)
		{
			this.bfd_container = bfd_container;

			integer_type = new NativeFundamentalType ("int", typeof (int), Marshal.SizeOf (typeof (int)));
			long_type = new NativeFundamentalType ("long", typeof (long), Marshal.SizeOf (typeof (long)));
			pointer_type = new NativePointerType ("void *", info.TargetAddressSize);
		}

		public string Name {
			get { return "native"; }
		}

		public ITargetFundamentalType IntegerType {
			get { return integer_type; }
		}

		public ITargetFundamentalType LongIntegerType {
			get { return long_type; }
		}

		public ITargetFundamentalType StringType {
			get { return null; }
		}

		public ITargetType PointerType {
			get { return pointer_type; }
		}

		public ITargetType ExceptionType {
			get { return null; }
		}

		public ITargetType LookupType (StackFrame frame, string name)
		{
			return bfd_container.LookupType (frame, name);
		}

		public bool CanCreateInstance (Type type)
		{
			return false;
		}

		public ITargetObject CreateInstance (StackFrame frame, object obj)
		{
			throw new InvalidOperationException ();
		}

		public ITargetPointerObject CreatePointer (StackFrame frame,
							   TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (frame, address);
			return new NativePointerObject (pointer_type, location);
		}

		public ITargetObject CreateObject (StackFrame frame, TargetAddress address)
		{
			return null;
		}
	}
}
