using System;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStringType : NativeType, ITargetFundamentalType
	{
		static int max_string_length = 100;

		public NativeStringType (int size)
			: base ("char *", TargetObjectKind.Fundamental, size)
		{
			this.type_handle = typeof (string);
		}

		public override bool IsByRef {
			get {
				return true;
			}
		}

		public Type Type {
			get {
				return typeof (string);
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

		public override NativeObject GetObject (MonoTargetLocation location)
		{
			return new NativeStringObject (this, location);
		}
	}
}
