using System;
using Mono.Debugger.Backends;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringType : MonoFundamentalType
	{
		static int max_string_length = 10000;

		public readonly int ObjectSize;
		protected readonly TargetAddress CreateString;

		private MonoStringType (MonoSymbolFile file, Cecil.TypeDefinition typedef,
					int object_size, int size)
			: base (file, typedef, "string", FundamentalKind.String, size)
		{
			this.ObjectSize = object_size;
			this.CreateString = file.MonoLanguage.MonoDebuggerInfo.CreateString;
		}

		public static MonoStringType Create (MonoSymbolFile corlib, TargetMemoryAccess memory)
		{
			int object_size = 2 * memory.TargetMemoryInfo.TargetAddressSize;

			MonoStringType type = new MonoStringType (
				corlib, corlib.ModuleDefinition.Types ["System.String"],
				object_size, object_size + 4);

			TargetAddress klass = OldMonoRuntime.GetStringClass (corlib.MonoLanguage, memory);
			type.create_type (memory, klass);

			return type;
		}

		public static int MaximumStringLength {
			get {
				return max_string_length;
			}

			set {
				max_string_length = value;
			}
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

                internal override TargetFundamentalObject CreateInstance (Thread target, object obj)
                {
                        string str = obj as string;
                        if (str == null)
                                throw new ArgumentException ();

                        TargetAddress retval = target.CallMethod (
				CreateString, TargetAddress.Null, 0, 0, str);
                        TargetLocation location = new AbsoluteTargetLocation (retval);
                        return new MonoStringObject (this, location);
                }

		protected override TargetObject DoGetObject (TargetMemoryAccess target, TargetLocation location)
		{
			return new MonoStringObject (this, location);
		}
	}
}
