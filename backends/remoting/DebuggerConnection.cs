using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public delegate void ConnectionHandler (DebuggerConnection connection);

	public class DebuggerConnection : IDisposable
	{
		string url;
		int child_pid, socket_fd;
		DebuggerStream network_stream;
		DebuggerServerChannel server_channel;
		Hashtable requests;
		Thread poll_thread;
		bool aborted;
		bool shutdown_requested;

		public DebuggerConnection (DebuggerServerChannel server_channel, string url, int fd)
		{
			this.url = url;
			this.server_channel = server_channel;
			this.socket_fd = fd;

			Init ();
		}

		public string URL {
			get { return url; }
		}

		public DebuggerConnection (DebuggerServerChannel server_channel, string url,
					   string[] argv, string[] envp)
		{
			this.url = url;
			this.server_channel = server_channel;

			IntPtr error;
			if (!mono_debugger_remoting_spawn (argv, envp, out child_pid, out socket_fd, out error)) {
				string message = Marshal.PtrToStringAuto (error);
				g_free (error);

				throw new DebuggerRemotingException ("Cannot start server: `{0}'", message);
			}

			Init ();
		}

		public void Shutdown ()
		{
			lock (this) {
				if (shutdown_requested || aborted)
					return;

				shutdown_requested = true;
				if (requests.Count > 0)
					return;

				Abort ();
			}
		}

		public event ConnectionHandler ConnectionClosedEvent;

		void Init ()
		{
			requests = Hashtable.Synchronized (new Hashtable ());
			network_stream = new DebuggerStream (socket_fd);

			poll_thread = new Thread (poll_thread_main);
			poll_thread.IsBackground = true;
			poll_thread.Start ();
		}

		delegate void PollHandler ();

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_remoting_poll (int socket_fd, PollHandler func);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_remoting_kill (int pid, int socket_fd);

		[DllImport("monodebuggerserver")]
		static extern bool mono_debugger_remoting_spawn (string[] argv, string[] envp, out int child_pid,
								 out int child_socket, out IntPtr error);

		[DllImport("libglib-2.0-0.dll")]
		static extern void g_free (IntPtr data);

		void poll_cb ()
		{
			long sequence_id;
			MessageStatus status;

			if (aborted)
				return;

			ITransportHeaders headers = null;
			Stream stream = null;

			lock (this) {
				status = DebuggerMessageIO.ReceiveMessageStatus (network_stream, out sequence_id);
				if ((status == MessageStatus.Message) || (status == MessageStatus.Reply))
					stream = DebuggerMessageIO.ReceiveMessageStream (network_stream, out headers);
			}

			switch (status) {
			case MessageStatus.Message:
				SendMessage (sequence_id, stream, headers);
				break;

			case MessageStatus.Reply:
				MessageData data = (MessageData) requests [sequence_id];
				if (data == null)
					throw new DebuggerRemotingException (
						"Received unknown message {0:x}", sequence_id);

				data.ResponseHeaders = headers;
				data.ResponseStream = stream;
				data.Handle.Set ();
				break;

			case MessageStatus.Async:
			case MessageStatus.OneWay:
				break;

			default:
				throw new DebuggerRemotingException ("Received unknown message {0}", status);
			}
		}

		void SendMessage (long sequence_id, Stream request_stream, ITransportHeaders request_headers)
		{
			ITransportHeaders response_headers;
			Stream response_stream;

			ServerProcessing proc = server_channel.Sink.InternalProcessMessage (
				this, sequence_id, request_stream, request_headers,
				out response_stream, out response_headers);

			lock (this) {
				switch (proc) {
				case ServerProcessing.Complete:
					DebuggerMessageIO.SendMessageStatus (
						network_stream, MessageStatus.Reply, sequence_id);
					DebuggerMessageIO.SendMessageStream (
						network_stream, response_stream,  response_headers);
					break;
				case ServerProcessing.Async:
					DebuggerMessageIO.SendMessageStatus (
						network_stream, MessageStatus.Async, sequence_id);
					break;
				case ServerProcessing.OneWay:
					DebuggerMessageIO.SendMessageStatus (
						network_stream, MessageStatus.Async, sequence_id);
					break;
				}
			}
		}

		public void SendAsyncResponse (long sequence_id, Stream response_stream,
					       ITransportHeaders response_headers)
		{
			lock (this) {
				DebuggerMessageIO.SendMessageStatus (
					network_stream, MessageStatus.Reply, sequence_id);
				DebuggerMessageIO.SendMessageStream (
					network_stream, response_stream,  response_headers);
			}
		}

		public Stream NetworkStream {
			get { return network_stream; }
		}
		
		protected long SequenceCode {
			get { return 0x100000000; }
		}

		static int next_sequence_id = 0;
		protected long GetNextSequenceID ()
		{
			return (++next_sequence_id) | SequenceCode;
		}

		public void Run ()
		{
			poll_thread.Join ();
		}

		void Abort ()
		{
			aborted = true;
			mono_debugger_remoting_kill (child_pid, socket_fd);
			poll_thread.Abort ();
		}

		void poll_thread_main ()
		{
			mono_debugger_remoting_poll (socket_fd, poll_cb);
			if (ConnectionClosedEvent != null)
				ConnectionClosedEvent (this);
		}

		protected sealed class MessageData : IDisposable
		{
			public readonly long SequenceID;
			public readonly ManualResetEvent Handle;

			public ITransportHeaders ResponseHeaders;
			public Stream ResponseStream;

			public MessageData (long id)
			{
				this.SequenceID = id;
				this.Handle = new ManualResetEvent (false);
			}

			public void Dispose ()
			{
				((IDisposable) Handle).Dispose ();
				// Take yourself off the Finalization queue
				GC.SuppressFinalize (this);
			}

			~MessageData ()
			{
				((IDisposable) Handle).Dispose ();
			}
		}

		public void SendMessage (Stream request_stream, ITransportHeaders request_headers,
					 out ITransportHeaders response_headers,
					 out Stream response_stream)
		{
			MessageData data;
			long sequence_id = GetNextSequenceID ();
			lock (this) {
				data = new MessageData (sequence_id);
				requests.Add (sequence_id, data);

				DebuggerMessageIO.SendMessageStatus (
					network_stream, MessageStatus.Message, sequence_id);
				DebuggerMessageIO.SendMessageStream (
					network_stream, request_stream, request_headers);
			}

			data.Handle.WaitOne ();
			response_headers = data.ResponseHeaders;
			response_stream = data.ResponseStream;

			lock (this) {
				requests.Remove (sequence_id);
				if (shutdown_requested && (requests.Count == 0))
					Abort ();
			}
		}

		public void SendAsyncMessage (Stream request_stream, ITransportHeaders request_headers)
		{
			MessageData data;
			long sequence_id = GetNextSequenceID ();
			lock (this) {
				data = new MessageData (sequence_id);
				requests.Add (sequence_id, data);

				DebuggerMessageIO.SendMessageStatus (
					network_stream, MessageStatus.Message, sequence_id);
				DebuggerMessageIO.SendMessageStream (
					network_stream, request_stream, request_headers);
			}
		}

		void FinishRequest (MessageData data)
		{
		}

		protected bool disposed = false;

		protected virtual void DoDispose ()
		{
			poll_thread.Abort ();
			if (child_pid != 0)
				mono_debugger_remoting_kill (child_pid, socket_fd);
		}

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing)
					DoDispose ();

				// Release unmanaged resources
				this.disposed = true;
			}
		}

		public void Dispose ()
		{
			Dispose (true);
			// Take yourself off the Finalization queue
			GC.SuppressFinalize (this);
		}

		~DebuggerConnection ()
		{
			Dispose (false);
		}
	}
}
