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

		protected string ReadString (MonoTargetLocation start)
		{
			StringBuilder sb = new StringBuilder ();
			bool done = false;

			int offset = 0;

			while (!done) {
				MonoTargetLocation location = start.GetLocationAtOffset (offset, false);
				byte[] buffer = location.ReadBuffer (ChunkSize);

				int size = buffer.Length;
				char[] char_buffer = new char [size];
				for (int i = 0; i < size; i++) {
					if (buffer [i] == 0) {
						done = true;
						size = i;
						break;
					}

					char_buffer [i] = (char) buffer [i];
				}

				string str = new String (char_buffer, 0, size);
				sb.Append (str);

				offset += size;
			}

			return sb.ToString ();
		}
	}
}

