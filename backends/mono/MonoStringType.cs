using System;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoStringType : MonoFundamentalType
	{
		int object_size;

		protected readonly TargetAddress CreateString;

		public MonoStringType (MonoSymbolFile file, Type type, int object_size,
				       int size, TargetAddress klass)
			: base (file, type, FundamentalKind.String, size, klass)
		{
			this.object_size = object_size;
			this.CreateString = file.MonoLanguage.MonoDebuggerInfo.CreateString;
		}

		protected override MonoTypeInfo CreateTypeInfo ()
		{
			return new MonoStringTypeInfo (this, object_size, size, klass_address);
		}

		public override bool IsByRef {
			get { return true; }
		}

		protected override MonoTypeInfo DoGetTypeInfo (TargetBinaryReader info)
		{
			throw new InvalidOperationException ();
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
                        return new MonoStringObject ((MonoStringTypeInfo)type_info, location);
                }
	}
}
