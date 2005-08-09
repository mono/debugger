using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Channels;

namespace Mono.Debugger.Remoting
{
	public class DebuggerClientConnection : IDisposable
	{
		delegate void PollHandler ();

		[DllImport("monodebuggerremoting")]
		static extern bool mono_debugger_remoting_poll (int fd, PollHandler func);

		[DllImport("monodebuggerremoting")]
		static extern bool mono_debugger_remoting_kill (int pid, int fd);

		[DllImport("monodebuggerremoting")]
		static extern bool mono_debugger_remoting_spawn (string[] argv, string[] envp, out int child_pid,
								 out int child_socket, out IntPtr error);

		[DllImport("libglib-2.0.so.0")]
		static extern void g_free (IntPtr data);

		int child_pid;
		int socket_fd;
		DebuggerStream network_stream;
		Hashtable requests;
		Thread poll_thread;

		public DebuggerClientConnection (string[] argv, string[] envp)
		{
			IntPtr error;
			if (!mono_debugger_remoting_spawn (argv, envp, out child_pid, out socket_fd, out error)) {
				string message = Marshal.PtrToStringAuto (error);
				g_free (error);

				throw new DebuggerRemotingException ("Cannot start server: `{0}'", message);
			}

			requests = Hashtable.Synchronized (new Hashtable ());

			network_stream = new DebuggerStream (socket_fd);
			poll_thread = new Thread (ThePoll);
			poll_thread.IsBackground = true;
			poll_thread.Start ();
		}

		void HandleMessage ()
		{
			if (disposed)
				return;

			MessageStatus status = DebuggerMessageFormat.ReceiveMessageStatus (network_stream);

			long sequence_id;
			ITransportHeaders response_headers;
			Stream response_stream = DebuggerMessageFormat.ReceiveMessageStream (
				network_stream, out sequence_id, out response_headers);

			lock (this) {
				MessageData data = (MessageData) requests [sequence_id];
				if (data == null)
					throw new DebuggerRemotingException (
						"Received unknown message {0}", sequence_id);
				data.ResponseHeaders = response_headers;
				data.ResponseStream = response_stream;
				data.Handle.Set ();
			}
		}

		void ThePoll ()
		{
			mono_debugger_remoting_poll (socket_fd, HandleMessage);
		}

		public Stream Stream {
			get { return network_stream; }
		}

		static long next_sequence_id = 0;

		private sealed class MessageData : IDisposable
		{
			public readonly ManualResetEvent Handle;

			public ITransportHeaders ResponseHeaders;
			public Stream ResponseStream;

			public MessageData (long id)
			{
				// this.SequenceID = id;
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

		public void SendMessage (Stream request_stream, ITransportHeaders headers,
					 out ITransportHeaders response_headers,
					 out Stream response_stream)
		{
			MessageData data;
			long sequence_id;
			lock (this) {
				sequence_id = ++next_sequence_id;
				data = new MessageData (sequence_id);
				requests.Add (sequence_id, data);
				DebuggerMessageFormat.SendMessageStream (
					network_stream, request_stream, sequence_id, headers);
			}

			data.Handle.WaitOne ();
			response_headers = data.ResponseHeaders;
			response_stream = data.ResponseStream;
			requests.Remove (sequence_id);
		}

		private bool disposed = false;

		protected virtual void Dispose (bool disposing)
		{
			if (!this.disposed) {
				if (disposing) {
					Console.WriteLine ("DISPOSE CLIENT CONNECTION!");
					poll_thread.Abort ();
					mono_debugger_remoting_kill (child_pid, socket_fd);
				}

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

		~DebuggerClientConnection ()
		{
			Dispose (false);
		}
	}
}
