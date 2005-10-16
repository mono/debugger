using System;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeLanguage : Language
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

		public override string Name {
			get { return "native"; }
		}

		public override TargetFundamentalType IntegerType {
			get { return integer_type; }
		}

		public override TargetFundamentalType LongIntegerType {
			get { return long_type; }
		}

		public override TargetFundamentalType StringType {
			get { return null; }
		}

		public override TargetType PointerType {
			get { return pointer_type; }
		}

		public override TargetType VoidType {
			get { return void_type; }
		}

		public override ITargetInfo TargetInfo {
			get { return info; }
		}

		public override TargetClassType ExceptionType {
			get { return null; }
		}

		public override TargetClassType DelegateType {
			get { return null; }
		}

		public override TargetClassType ObjectType {
			get { return null; }
		}

		public override string SourceLanguage (StackFrame frame)
		{
			return "";
		}

		public override TargetType LookupType (StackFrame frame, string name)
		{
			return bfd_container.LookupType (frame, name);
		}

		public override bool CanCreateInstance (Type type)
		{
			return false;
		}

		public override TargetFundamentalObject CreateInstance (TargetAccess target,
									object value)
		{
			throw new InvalidOperationException ();
		}

		public override TargetPointerObject CreatePointer (StackFrame frame,
								   TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (address);
			return new NativePointerObject (pointer_type, location);
		}

		public override TargetObject CreateObject (TargetAccess target, TargetAddress address)
		{
			throw new NotSupportedException ();
		}

		public override TargetObject CreateNullObject (TargetAccess target, TargetType type)
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
