using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringTypeInfo : MonoFundamentalTypeInfo
	{
		static int max_string_length = 10000;

		internal readonly int LengthOffset;
		internal readonly int LengthSize;
		internal readonly int DataOffset;

		public MonoStringTypeInfo (MonoStringType type, int object_size, int size, TargetAddress klass)
			: base (type, size, klass)
		{
			this.LengthOffset = object_size;
			this.LengthSize = 4;
			this.DataOffset = object_size + 4;
		}

		public override bool HasFixedSize {
			get { return false; }
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
	}
}
