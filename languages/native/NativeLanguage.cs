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
		NativeStringType string_type;
		TargetInfo info;

		public NativeLanguage (BfdContainer bfd_container, TargetInfo info)
		{
			this.bfd_container = bfd_container;
			this.info = info;

			integer_type = new NativeFundamentalType (this, "int", FundamentalKind.Int32, 4);
			long_type = new NativeFundamentalType (this, "long", FundamentalKind.Int64, 8);
			pointer_type = new NativePointerType (this, "void *", info.TargetAddressSize);
			void_type = new NativeOpaqueType (this, "void", 0);
			string_type = new NativeStringType (this, info.TargetAddressSize);

		}

		public override string Name {
			get { return "native"; }
		}

		public override bool IsManaged {
			get { return false; }
		}

		internal override ProcessServant Process {
			get { return bfd_container.Process; }
		}

		public override TargetFundamentalType IntegerType {
			get { return integer_type; }
		}

		public override TargetFundamentalType LongIntegerType {
			get { return long_type; }
		}

		public override TargetFundamentalType StringType {
			get { return string_type; }
		}

		public override TargetType PointerType {
			get { return pointer_type; }
		}

		public override TargetType VoidType {
			get { return void_type; }
		}

		public override TargetInfo TargetInfo {
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

		public override TargetClassType ArrayType {
			get { return null; }
		}

		public override string SourceLanguage (StackFrame frame)
		{
			return "";
		}

		public override TargetType LookupType (string name)
		{
			return bfd_container.LookupType (name);
		}

		public override bool CanCreateInstance (Type type)
		{
			return false;
		}

		public override TargetFundamentalObject CreateInstance (Thread target, object value)
		{
			throw new InvalidOperationException ();
		}

		public override TargetPointerObject CreatePointer (StackFrame frame,
								   TargetAddress address)
		{
			TargetLocation location = new AbsoluteTargetLocation (address);
			return new NativePointerObject (pointer_type, location);
		}

		public override TargetObject CreateObject (Thread target, TargetAddress address)
		{
			throw new NotSupportedException ();
		}

		public override TargetObject CreateNullObject (Thread target, TargetType type)
		{
			throw new NotSupportedException ();
		}

		public override TargetPointerType CreatePointerType (TargetType type)
		{
			return new NativePointerType (this, type.Name + "*", type, type.Size);
		}

		internal TargetAddress LookupSymbol (string name)
		{
			return bfd_container.LookupSymbol (name);
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
