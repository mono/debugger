using System;

namespace Mono.Debugger.Languages.CSharp
{
	internal class MonoStringType : MonoType
	{
		static int max_string_length = 100;

		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;

		public MonoStringType (Type type, int size, TargetBinaryReader info)
			: base (TargetObjectKind.Fundamental, type, size, false)
		{
			LengthOffset = info.ReadByte ();
			LengthSize = info.ReadByte ();
			DataOffset = info.ReadByte ();
		}

		public static bool Supports (Type type)
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

		public override MonoObject GetObject (MonoTargetLocation location)
		{
			return new MonoStringObject (this, location);
		}
	}
}
