using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringType : MonoFundamentalType
	{
		static int max_string_length = 10000;

		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;

		protected readonly TargetAddress CreateString;

		public MonoStringType (Type type, int size, TargetBinaryReader info, MonoSymbolTable table)
			: base (type, size, info, table, false)
		{
			LengthOffset = info.ReadByte ();
			LengthSize = info.ReadByte ();
			DataOffset = info.ReadByte ();
			CreateString = table.Language.MonoDebuggerInfo.CreateString;
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

			TargetAddress retval = frame.CallMethod (CreateString, str);
			TargetLocation location = new AbsoluteTargetLocation (frame, retval);
			return new MonoFundamentalObject (this, location);
		}
	}
}
