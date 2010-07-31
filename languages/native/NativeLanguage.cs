using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeLanguage : Language
	{
		Process process;
		OperatingSystemBackend os;
		TargetFundamentalType unsigned_type;
		TargetFundamentalType integer_type;
		TargetFundamentalType long_type;
		TargetFundamentalType ulong_type;
		NativePointerType pointer_type;
		NativeOpaqueType void_type;
		NativeStringType string_type;
		TargetInfo info;

		Hashtable type_hash;

		public NativeLanguage (Process process, OperatingSystemBackend os, TargetInfo info)
		{
			this.process = process;
			this.os = os;
			this.info = info;

			this.type_hash = Hashtable.Synchronized (new Hashtable ());

			integer_type = new NativeFundamentalType (this, "int", FundamentalKind.Int32, 4);
			unsigned_type = new NativeFundamentalType (this, "unsigned int", FundamentalKind.UInt32, 4);
			long_type = new NativeFundamentalType (this, "long", FundamentalKind.Int64, 8);
			ulong_type = new NativeFundamentalType (this, "unsigned long", FundamentalKind.UInt64, 8);
			pointer_type = new NativePointerType (this, "void *", info.TargetAddressSize);
			void_type = new NativeOpaqueType (this, "void", 0);
			string_type = new NativeStringType (this, info.TargetAddressSize);
		}

		internal OperatingSystemBackend OperatingSystem {
			get { return os; }
		}

		public override string Name {
			get { return "native"; }
		}

		public override bool IsManaged {
			get { return false; }
		}

		internal override Process Process {
			get { return process; }
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
			os.ReadNativeTypes ();

			ITypeEntry entry = (ITypeEntry) type_hash [name];
			if (entry == null)
				return null;

			return entry.ResolveType ();
		}

		public void AddType (ITypeEntry entry)
		{
			if (!type_hash.Contains (entry.Name))
				type_hash.Add (entry.Name, entry);

			if (entry.IsComplete)
				type_hash [entry.Name] = entry;
		}

		TargetFundamentalType GetFundamentalType (Type type)
		{
			if (type == typeof (int))
				return integer_type;
			else if (type == typeof (uint))
				return unsigned_type;
			else if (type == typeof (long))
				return long_type;
			else if (type == typeof (ulong))
				return ulong_type;
			else
				return null;
		}

		public override bool CanCreateInstance (Type type)
		{
			return GetFundamentalType (type) != null;
		}

		public override TargetFundamentalObject CreateInstance (Thread thread, object obj)
		{
			TargetFundamentalType type = GetFundamentalType (obj.GetType ());
			if (type == null)
				return null;

			return type.CreateInstance (thread, obj);
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
			TargetLocation location = new AbsoluteTargetLocation (TargetAddress.Null);
			NativePointerType pointer = new NativePointerType (this, type);
			return new NativePointerObject (pointer, location);
		}

		public override TargetObjectObject CreateBoxedObject (Thread thread, TargetObject value)
		{
			throw new NotSupportedException ();
		}

		public override TargetPointerType CreatePointerType (TargetType type)
		{
			return new NativePointerType (this, type);
		}

		public override bool IsExceptionType (TargetClassType type)
		{
			return false;
		}

		private bool disposed = false;

		private void check_disposed ()
		{
			if (disposed)
				throw new ObjectDisposedException ("NativeLanguage");
		}

		private void Dispose (bool disposing)
		{
			lock (this) {
				if (disposed)
					return;
			  
				disposed = true;
			}

			if (disposing) {
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
