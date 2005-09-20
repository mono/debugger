using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringType : MonoFundamentalType
	{
		static int max_string_length = 10000;

		public readonly int ObjectSize;
		protected readonly TargetAddress CreateString;

		public MonoStringType (MonoSymbolFile file, Cecil.ITypeDefinition type, int object_size,
				       int size, TargetAddress klass)
			: base (file, type, FundamentalKind.String, size, klass)
		{
			this.ObjectSize = object_size;
			this.CreateString = file.MonoLanguage.MonoDebuggerInfo.CreateString;
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

                internal override MonoFundamentalObjectBase CreateInstance (StackFrame frame, object obj)
                {
                        string str = obj as string;
                        if (str == null)
                                throw new ArgumentException ();

                        TargetAddress retval = frame.Process.CallMethod (CreateString, 0, str);
                        TargetLocation location = new AbsoluteTargetLocation (frame, retval);
                        return new MonoStringObject (this, location);
                }

		public override MonoObject GetObject (TargetLocation location)
		{
			return new MonoStringObject (this, location);
		}
	}
}
