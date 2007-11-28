using System;
using System.Text;

using Mono.Debugger.Backend;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStringObject : TargetFundamentalObject
	{
		public NativeStringObject (NativeStringType type, TargetLocation location)
			: base (type, location)
		{ }

		protected int MaximumDynamicSize {
			get {
				return NativeStringType.MaximumStringLength;
			}
		}

		public static int ChunkSize {
			get {
				return 16;
			}
		}

		internal override object DoGetObject (TargetMemoryAccess target)
		{
			try {
				return ReadString (target, Location);
			} catch (TargetException ex) {
				throw new LocationInvalidException (ex);
			}
		}

		static char[] hex_chars = { '0', '1', '2', '3', '4', '5', '6', '7',
					    '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

		protected string ReadString (TargetMemoryAccess target, TargetLocation start)
		{
			if (start.HasAddress && start.GetAddress (target).IsNull)
				return "null";

			StringBuilder sb = new StringBuilder ();
			bool done = false;

			int offset = 0;

			while (!done && (offset < MaximumDynamicSize)) {
				TargetLocation location = start.GetLocationAtOffset (offset);
				byte[] buffer = location.ReadBuffer (target, ChunkSize);

				int pos = 0;
				int size = buffer.Length;
				char[] char_buffer = new char [size * 3];
				for (int i = 0; i < size; i++) {
					if (buffer [i] == 0) {
						done = true;
						break;
					}

					char ch = (char) buffer [i];
					if (Char.IsLetterOrDigit (ch) || Char.IsPunctuation (ch) ||
					    Char.IsWhiteSpace (ch) || (ch == '<') || (ch == '>'))
						char_buffer [pos++] = ch;
					else if (ch == '\\') {
						char_buffer [pos++] = '\\';
						char_buffer [pos++] = '\\';
					} else if ((ch == '\'') || (ch == '`')) {
						char_buffer [pos++] = ch;
					} else {
						char_buffer [pos++] = '\\';
						char_buffer [pos++] = hex_chars [(ch & 0xf0) >> 4];
						char_buffer [pos++] = hex_chars [ch & 0x0f];
					}
				}

				string str = new String (char_buffer, 0, pos);
				sb.Append (str);

				offset += size;
			}

			return sb.ToString ();
		}
	}
}

