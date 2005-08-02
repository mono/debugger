using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	internal class DebuggerClientTransportSink : IClientChannelSink, IDisposable
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
			Console.Error.WriteLine ("TRANSPORT GET STREAM: {0} {1}", msg, headers);
			return null;
		}

		private void CreateConnection ()
		{
			ProcessStartInfo info = new ProcessStartInfo ("/home/martin/INSTALL/bin/mono");
			info.Arguments = "--debug " + path;
			Console.Error.WriteLine ("START: |{0}|", path);
			info.UseShellExecute = false;
			info.RedirectStandardInput = true;
			info.RedirectStandardOutput = true;
			// info.RedirectStandardError = true;

			process = Process.Start (info);
			Console.Error.WriteLine ("CONNECT: {0} {1}", process, process.Id);
			process.StandardOutput.ReadLine ();
			Console.Error.WriteLine ("CONNECTED");
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
			requestHeaders [CommonTransportKeys.RequestUri] = object_uri;

			Console.Error.WriteLine ("PROCESS MESSAGE: |{0}|{1}|", object_uri, request_uri);

			// send the message
			DebuggerMessageFormat.SendMessageStream (
				process.StandardInput.BaseStream, requestStream, requestHeaders);
			process.StandardInput.BaseStream.Flush ();

			Console.Error.WriteLine ("MESSAGE SENT: {0} {1}", process.Id, Process.GetCurrentProcess ().Id);

			MessageStatus status = DebuggerMessageFormat.ReceiveMessageStatus (
				process.StandardOutput.BaseStream);

			if (status != MessageStatus.MethodMessage)
				throw new RemotingException ("Unknown response message from server");

			responseStream = DebuggerMessageFormat.ReceiveMessageStream (
				process.StandardOutput.BaseStream, out responseHeaders);
		}

#region IDisposable implementation
		~DebuggerClientTransportSink ()
		{
			Dispose (false);
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			// Check to see if Dispose has already been called.
			// If this is a call to Dispose, dispose all managed resources.
			if (disposing) {
				Console.Error.WriteLine ("DISPOSE CLIENT TRANSPORT SINK!");
			}

			disposed = true;
		}


		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}
#endregion
	}
}	
