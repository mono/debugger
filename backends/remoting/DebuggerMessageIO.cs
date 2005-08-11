using System;
using System.Runtime.Serialization.Formatters;
using System.Runtime.Serialization;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Text;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	enum MessageStatus { Unknown = 0, Message = 1, Reply = 2, Async = 3, OneWay = 4 }

	internal class DebuggerMessageIO
	{
		static byte[] msg_headers = {
			(byte)'M', (byte)'D', (byte)'B', 148, 1, 0
		};

		public static int DefaultStreamBufferSize = 1000;

		// Identifies an incoming message
		public static MessageStatus ReceiveMessageStatus (Stream network_stream, out long sequence_id)
		{
			byte[] buffer = new byte [18];
			StreamRead (network_stream, buffer, 18);

			for (int i = 0; i < 6; i++) {
				if (buffer [i] != msg_headers [i])
					throw new RemotingException ("Received unknown message.");
			}

			MessageStatus status = (MessageStatus) BitConverter.ToInt32 (buffer, 6);
			sequence_id = BitConverter.ToInt64 (buffer, 10);

			return status;
		}

		static void StreamRead (Stream networkStream, byte[] buffer, int count)
		{
			int nr = 0;
			do {
				int pr = networkStream.Read (buffer, nr, count - nr);
				if (pr == 0)
					throw new RemotingException ("Connection closed");
				nr += pr;
			} while (nr < count);
		}

		public static void SendMessageStatus (Stream network_stream, MessageStatus status, long sequence_id)
		{
			byte[] buffer = new byte [18];
			msg_headers.CopyTo (buffer, 0);

			byte[] status_data = BitConverter.GetBytes ((int) status);
			status_data.CopyTo (buffer, 6);

			byte[] seq_data = BitConverter.GetBytes (sequence_id);
			seq_data.CopyTo (buffer, 10);

			network_stream.Write (buffer, 0, 18);
		}

		public static void SendMessageStream (Stream network_stream, Stream message_stream,
						      ITransportHeaders message_headers)
		{
			byte[] buffer = new byte [DefaultStreamBufferSize];

			// Writes header tag (0x0000 if request stream, 0x0002 if response stream)
			if (message_headers [CommonTransportKeys.RequestUri] != null)
				buffer [0] = (byte) 0;
			else
				buffer[0] = (byte) 2;
			buffer [1] = (byte) 0 ;

			// Writes ID
			buffer [2] = (byte) 0;

			// Writes assemblyID????
			buffer [3] = (byte) 0;

			// Writes the length of the stream being sent (not including the headers)
			int num = (int) message_stream.Length;
			buffer [4] = (byte) num;
			buffer [5] = (byte) (num >> 8);
			buffer [6] = (byte) (num >> 16);
			buffer [7] = (byte) (num >> 24);
			network_stream.Write (buffer, 0, 8);

			// Writes the message headers
			SendHeaders (network_stream, message_headers, buffer);

			// Writes the stream
			if (message_stream is MemoryStream) {
				// The copy of the stream can be optimized. The internal
				// buffer of MemoryStream can be used.
				MemoryStream mem_stream = (MemoryStream) message_stream;
				network_stream.Write (mem_stream.GetBuffer(), 0, (int) mem_stream.Length);
			} else {
				int nread = message_stream.Read (buffer, 0, buffer.Length);
				while (nread > 0) {
					network_stream.Write (buffer, 0, nread);
					nread = message_stream.Read (buffer, 0, buffer.Length);
				}
			}
		}
		
		static byte[] msgUriTransportKey = new byte[] { 4, 0, 1, 1 };
		static byte[] msgContentTypeTransportKey = new byte[] { 6, 0, 1, 1 };
		static byte[] msgDefaultTransportKey = new byte[] { 1, 0, 1 };
		static byte[] msgHeaderTerminator = new byte[] { 0, 0 };

		private static void SendHeaders(Stream networkStream, ITransportHeaders requestHeaders, byte[] buffer)
		{
			foreach (DictionaryEntry hdr in requestHeaders) {
				switch (hdr.Key.ToString()) {
				case CommonTransportKeys.RequestUri: 
					networkStream.Write (msgUriTransportKey, 0, 4);
					break;
				case "Content-Type": 
					networkStream.Write (msgContentTypeTransportKey, 0, 4);
					break;
				default: 
					networkStream.Write (msgDefaultTransportKey, 0, 3);
					SendString (networkStream, hdr.Key.ToString(), buffer);
					networkStream.WriteByte (1);
					break;
				}
				SendString (networkStream, hdr.Value.ToString(), buffer);
			}
			networkStream.Write (msgHeaderTerminator, 0, 2);	// End of headers
		}
		
		public static ITransportHeaders ReceiveHeaders (Stream networkStream, byte[] buffer)
		{
			StreamRead (networkStream, buffer, 2);
			
			byte headerType = buffer [0];
			TransportHeaders headers = new TransportHeaders ();

			while (headerType != 0)
			{
				string key;
				StreamRead (networkStream, buffer, 1);	// byte 1
				switch (headerType)
				{
					case 4: key = CommonTransportKeys.RequestUri; break;
					case 6: key = "Content-Type"; break;
					case 1: key = ReceiveString (networkStream, buffer); break;
					default: throw new NotSupportedException ("Unknown header code: " + headerType);
				}
				StreamRead (networkStream, buffer, 1);	// byte 1
				headers[key] = ReceiveString (networkStream, buffer);

				StreamRead (networkStream, buffer, 2);
				headerType = buffer [0];
			}

			return headers;
		}
		
		public static MemoryStream ReceiveMessageStream (Stream network_stream, out ITransportHeaders headers)
		{
			byte[] buffer = new byte [DefaultStreamBufferSize];

			headers = null;

			// Reads header tag:  0 -> Stream with headers or 2 -> Response Stream
			// +
			// Gets the length of the data stream
			StreamRead (network_stream, buffer, 8);

			int byteCount = (buffer [4] | (buffer [5] << 8) |
				(buffer [6] << 16) | (buffer [7] << 24));

			// Reads the headers
			headers = ReceiveHeaders (network_stream, buffer);

			byte[] resultBuffer = new byte [byteCount];
			StreamRead (network_stream, resultBuffer, byteCount);

			return new MemoryStream (resultBuffer, 0, resultBuffer.Length, false, true);
		}

		private static void SendString (Stream networkStream, string str, byte[] buffer)
		{
			// Allocates a buffer. Use the internal buffer if it is 
			// big enough. If not, create a new one.

			int maxBytes = Encoding.UTF8.GetMaxByteCount(str.Length)+4;	//+4 bytes for storing the string length
			if (maxBytes > buffer.Length)
				buffer = new byte[maxBytes];

			int num = Encoding.UTF8.GetBytes (str, 0, str.Length, buffer, 4);

			// store number of bytes (not number of chars!)

			buffer [0] = (byte) num;
			buffer [1] = (byte) (num >> 8);
			buffer [2] = (byte) (num >> 16);
			buffer [3] = (byte) (num >> 24);

			// Write the string bytes
			networkStream.Write (buffer, 0, num + 4);
		}

		private static string ReceiveString (Stream networkStream, byte[] buffer)
		{
			StreamRead (networkStream, buffer, 4);

			// Reads the number of bytes (not chars!)

			int byteCount = (buffer [0] | (buffer [1] << 8) |
				(buffer [2] << 16) | (buffer [3] << 24));

			if (byteCount == 0) return string.Empty;

			// Allocates a buffer of the correct size. Use the
			// internal buffer if it is big enough

			if (byteCount > buffer.Length)
				buffer = new byte[byteCount];

			// Reads the string

			StreamRead (networkStream, buffer, byteCount);
			char[] chars = Encoding.UTF8.GetChars (buffer, 0, byteCount);
	
			return new string (chars);
		}
	}
}

