using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringType : MonoFundamentalType
	{
		static int max_string_length = 100;

		internal readonly TargetAddress VTableAddress;
		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;

		public MonoStringType (Type type, int size, TargetAddress klass,
				       TargetBinaryReader info, MonoSymbolTable table)
			: base (type, size, klass, info, table, false)
		{
			VTableAddress = new TargetAddress (table.GlobalAddressDomain, info.ReadAddress ());
			LengthOffset = info.ReadByte ();
			LengthSize = info.ReadByte ();
			DataOffset = info.ReadByte ();
		}

		new public static bool Supports (Type type)
		{
			return type == typeof (string);
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public static int MaximumStringLength {
			get {
				return max_string_length;
			}

			set {
				max_string_length = value;
			}
		}

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoStringObject (this, location);
		}

		public override byte[] CreateObject (object obj)
		{
			string str = obj as string;
			if (str == null)
				throw new ArgumentException ();

			char[] carray = ((string) obj).ToCharArray ();
			byte[] retval = new byte [carray.Length * 2];

			for (int i = 0; i < carray.Length; i++) {
				retval [2*i] = (byte) (carray [i] & 0x00ff);
				retval [2*i+1] = (byte) (carray [i] >> 8);
			}

			return retval;
		}

		internal override MonoFundamentalObjectBase CreateInstance (StackFrame frame, object obj)
		{
			string str = obj as string;
			if (str == null)
				throw new ArgumentException ();

			byte[] contents = CreateObject (str);

			int size = Size + contents.Length;
			TargetLocation location = Heap.Allocate (frame, size);

			TargetBinaryWriter writer = new TargetBinaryWriter (size, (ITargetInfo) frame.TargetAccess);
			writer.WriteAddress (VTableAddress);
			writer.WriteAddress (0);
			writer.WriteInt32 (str.Length);
			writer.WriteBuffer (contents);

			frame.TargetAccess.WriteBuffer (location.Address, writer.Contents);

			return new MonoFundamentalObject (this, location);
		}
	}
}
