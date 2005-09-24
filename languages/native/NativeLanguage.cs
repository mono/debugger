using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Architecture;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeLanguage : MarshalByRefObject, ILanguage
	{
		BfdContainer bfd_container;
		TargetFundamentalType integer_type;
		TargetFundamentalType long_type;
		NativePointerType pointer_type;
		NativeOpaqueType void_type;
		ITargetInfo info;

		public NativeLanguage (BfdContainer bfd_container, ITargetInfo info)
		{
			this.bfd_container = bfd_container;
			this.info = info;

			integer_type = new TargetFundamentalType (this, "int", FundamentalKind.Int32, 4);
			long_type = new TargetFundamentalType (this, "long", FundamentalKind.Int64, 8);
			pointer_type = new NativePointerType (this, "void *", info.TargetAddressSize);
			void_type = new NativeOpaqueType (this, "void", 0);
		}

		public string Name {
			get { return "native"; }
		}

		public TargetFundamentalType IntegerType {
			get { return integer_type; }
		}

		public TargetFundamentalType LongIntegerType {
			get { return long_type; }
		}

		public TargetFundamentalType StringType {
			get { return null; }
		}

		public ITargetType PointerType {
			get { return pointer_type; }
		}

		public ITargetType VoidType {
			get { return void_type; }
		}

		public ITargetInfo TargetInfo {
			get { return info; }
		}

		ITargetClassType ILanguage.ExceptionType {
			get { return null; }
		}

		ITargetClassType ILanguage.DelegateType {
			get { return null; }
		}

		public string SourceLanguage (StackFrame frame)
		{
			return "";
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

		public TargetFundamentalObject CreateInstance (ITargetAccess target, int value)
		{
			throw new InvalidOperationException ();
		}

		public ITargetPointerObject CreatePointer (StackFrame frame,
							   TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (
				frame.TargetAccess, address);
			return new NativePointerObject (pointer_type, location);
		}

		public ITargetObject CreateObject (ITargetAccess target, TargetAddress address)
		{
			throw new NotSupportedException ();
		}

		public TargetAddress AllocateMemory (ITargetAccess target, int size)
		{
			throw new NotSupportedException ();
		}

		public ITargetObject CreateNullObject (ITargetAccess target, ITargetType type)
		{
			throw new NotSupportedException ();
		}

		private bool disposed = false;

		private void Dispose (bool disposing)
		{
			lock (this) {
				if (disposed)
					return;
			  
				disposed = true;
			}

			if (disposing) {
				if (bfd_container != null) {
					bfd_container.Dispose ();

					bfd_container = null;
				}
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~NativeLanguage ()
		{
			Dispose (false);
		}
	}
}
