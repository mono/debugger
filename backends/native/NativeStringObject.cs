using System;
using System.Text;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Languages.Native
{
	internal class NativeStringObject : NativeObject, ITargetFundamentalObject
	{
		public NativeStringObject (NativeType type, MonoTargetLocation location)
			: base (type, location)
		{ }

		protected override int MaximumDynamicSize {
			get {
				return NativeStringType.MaximumStringLength;
			}
		}

		protected override long GetDynamicSize (ITargetMemoryReader reader,
							MonoTargetLocation location,
							out MonoTargetLocation dynamic_location)
		{
			throw new InvalidOperationException ();
		}

		public static int ChunkSize {
			get {
				return 16;
			}
		}

		public bool HasObject {
			get {
				return true;
			}
		}

		public object Object {
			get {
				return GetObject ();
			}
		}

		internal object GetObject ()
		{
			try {
				return ReadString (location.GetLocationAtOffset (0, true));
			} catch {
				is_valid = false;
				throw new LocationInvalidException ();
			}
		}

		static char[] hex_chars = { '0', '1', '2', '3', '4', '5', '6', '7',
					    '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

		protected string ReadString (MonoTargetLocation start)
		{
			StringBuilder sb = new StringBuilder ();
			bool done = false;

			int offset = 0;
			bool quoted_chars = false;

			while (!done && (offset < MaximumDynamicSize)) {
				MonoTargetLocation location = start.GetLocationAtOffset (offset, false);
				byte[] buffer = location.ReadBuffer (ChunkSize);

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
					    Char.IsWhiteSpace (ch))
						char_buffer [pos++] = ch;
					else if (ch == '\\') {
						char_buffer [pos++] = '\\';
						char_buffer [pos++] = '\\';
						quoted_chars = true;
					} else {
						char_buffer [pos++] = '\\';
						char_buffer [pos++] = hex_chars [(ch & 0xf0) >> 8];
						char_buffer [pos++] = hex_chars [ch & 0x0f];
						quoted_chars = true;
					}
				}

				string str = new String (char_buffer, 0, pos);
				sb.Append (str);

				offset += size;
			}

			if (quoted_chars)
				return String.Concat ("\"", sb.ToString (), "\"");
			else
				return String.Concat ("\'", sb.ToString (), "\'");
		}
	}
}

