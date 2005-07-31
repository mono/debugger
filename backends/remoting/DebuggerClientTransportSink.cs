using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	internal class DebuggerClientTransportSink : IClientChannelSink
	{
		string url;
		string host;
		string path;
		string object_uri;
		Process process;

		public DebuggerClientTransportSink (string url)
		{
			this.url = url;
			path = DebuggerChannel.ParseDebuggerURL (url, out host, out object_uri);
			CreateConnection ();
		}

		public IDictionary Properties {
			get { return null; }
		}

		public IClientChannelSink NextChannelSink {
			get { return null; }
		}

		public void AsyncProcessRequest (IClientChannelSinkStack sinkStack, IMessage msg,
						 ITransportHeaders headers, Stream stream)
		{
			throw new NotImplementedException ();			
		}

		public void AsyncProcessResponse (IClientResponseChannelSinkStack sinkStack,
						  object state, ITransportHeaders headers,
						  Stream stream)
		{
			throw new NotImplementedException ();
		}

		public Stream GetRequestStream (IMessage msg, ITransportHeaders headers)
		{
			Console.WriteLine ("TRANSPORT GET STREAM: {0} {1}", msg, headers);
			return null;
		}

		private void CreateConnection ()
		{
			ProcessStartInfo info = new ProcessStartInfo ("/home/martin/INSTALL/bin/mono");
			info.Arguments = "--debug " + path;
			info.UseShellExecute = false;
			info.RedirectStandardInput = true;
			info.RedirectStandardOutput = true;
			// info.RedirectStandardError = true;

			process = Process.Start (info);
			Console.WriteLine ("CONNECT: {0}", process);
		}
		
		public void ProcessMessage (IMessage msg,
					    ITransportHeaders requestHeaders,
					    Stream requestStream,
					    out ITransportHeaders responseHeaders,
					    out Stream responseStream)
		{
			if (requestHeaders == null)
				requestHeaders = new TransportHeaders();
			string request_uri = ((IMethodMessage) msg).Uri;
			requestHeaders [CommonTransportKeys.RequestUri] = request_uri;

			Console.WriteLine ("PROCESS MESSAGE: {0} {1} {2} {3} {4}", msg, requestHeaders,
					   requestStream, url, request_uri);

			// send the message
			DebuggerMessageFormat.SendMessageStream (
				process.StandardInput.BaseStream, (MemoryStream) requestStream,
				DebuggerMessageFormat.MessageType.Request, request_uri);

			// read the response fro the network an copy it to a memory stream
			DebuggerMessageFormat.MessageType msg_type;
			string uri;
			MemoryStream mem_stream = DebuggerMessageFormat.ReceiveMessageStream (
				process.StandardOutput.BaseStream, out msg_type, out uri);

			Console.WriteLine ("DONE PROCESSING MESSAGE");

			switch (msg_type) {
			case DebuggerMessageFormat.MessageType.Response:
				//fixme: read response message
				responseHeaders = null;
				responseStream = mem_stream;
				break;
			default:
				throw new Exception ("unknown response mesage header");
			}
		}
	}
}	
