using System;
using System.IO;
using System.Configuration;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using Mono.Debugger.Languages;
using Mono.Debugger.Backends;
using Mono.Debugger.Remoting;

namespace Mono.Debugger
{
	[Serializable]
	public class DebuggerOptions
	{
		/* The executable file we're debugging */
		public string File = "";

		/* argv[1...n] for the inferior process */
		public string[] InferiorArgs = null;

		/* The command line prompt.  should we really even
		 * bother letting the user set this?  why? */
		public string Prompt = "(mdb) ";

		/* JIT optimization flags affecting the inferior
		 * process */
		public string JitOptimizations = "";

		/* The inferior process's working directory */
		public string WorkingDirectory = Environment.CurrentDirectory;

		/* Whether or not we load native symbol tables */
		public bool LoadNativeSymbolTable = true;

		/* true if we're running in a script */
		public bool IsScript = false;

		/* true if we want to start the application immediately */
		public bool StartTarget = false;
	  
		/* the value of the -debug-flags: command line argument */
		public bool HasDebugFlags = false;
		public DebugFlags DebugFlags = DebugFlags.None;
		public string DebugOutput = null;

		/* true if -f/-fullname is specified on the command line */
		public bool InEmacs = false;

		/* non-null if the user specified the -mono-prefix
		 * command line argument */
		public string MonoPrefix = null;

		/* non-null if the user specified the -mono command line argument */
		public string MonoPath = null;

		public string RemoteHost = null;
		public string RemoteMono = null;
	}

	public class DebuggerSession : MarshalByRefObject
	{
		public readonly DebuggerClient Client;

		Hashtable events = Hashtable.Synchronized (new Hashtable ());

		internal DebuggerSession (DebuggerClient client)
		{
			this.Client = client;
		}

		protected DebuggerSession (DebuggerClient client, EventHandle[] event_list)
			: this (client)
		{
			foreach (EventHandle handle in event_list)
				events.Add (handle.Index, handle);				
		}

		public EventHandle[] Events {
			get {
				EventHandle[] handles = new EventHandle [events.Count];
				events.Values.CopyTo (handles, 0);
				return handles;
			}
		}

		public EventHandle GetEvent (int index)
		{
			return (EventHandle) events [index];
		}

		public void DeleteEvent (int index)
		{
			events.Remove (index);
		}

		private static SurrogateSelector CreateSurrogateSelector (StreamingContext context)
		{
			SurrogateSelector ss = new SurrogateSelector ();

			ss.AddSurrogate (typeof (Module), context,
					 new Module.SessionSurrogate ());
			ss.AddSurrogate (typeof (ThreadGroup), context,
					 new ThreadGroup.SessionSurrogate ());
			ss.AddSurrogate (typeof (EventHandle), context,
					 new EventHandle.SessionSurrogate ());
			ss.AddSurrogate (typeof (BreakpointHandle), context,
					 new EventHandle.SessionSurrogate ());
			ss.AddSurrogate (typeof (CatchpointHandle), context,
					 new EventHandle.SessionSurrogate ());

			return ss;
		}

		internal static DebuggerSession Load (DebuggerClient client, Stream stream)
		{
			StreamingContext context = new StreamingContext (
				StreamingContextStates.Persistence, client);

			SurrogateSelector ss = CreateSurrogateSelector (context);
			BinaryFormatter formatter = new BinaryFormatter (ss, context);

			SessionInfo info = (SessionInfo) formatter.Deserialize (stream);
			return info.CreateSession (client);
		}

		public void Save (Stream stream)
		{
			StreamingContext context = new StreamingContext (
				StreamingContextStates.Persistence, this);

			SurrogateSelector ss = CreateSurrogateSelector (context);
			BinaryFormatter formatter = new BinaryFormatter (ss, context);

			SessionInfo info = new SessionInfo (this);
			formatter.Serialize (stream, info);
		}

		public void InsertBreakpoints (Thread thread)
		{
			foreach (EventHandle handle in events.Values)
				handle.Enable (thread);
		}

		public EventHandle InsertBreakpoint (Thread target, int domain,
						     SourceLocation location, Breakpoint bpt)
		{
			EventHandle handle = new BreakpointHandle (target, domain, bpt, location);
			if (handle == null)
				return handle;

			events.Add (handle.Index, handle);
			return handle;
		}

		public EventHandle InsertBreakpoint (Thread target, TargetFunctionType func,
						     Breakpoint bpt)
		{
			EventHandle handle = new BreakpointHandle (target, bpt, func);
			if (handle == null)
				return handle;

			events.Add (handle.Index, handle);
			return handle;
		}

		public EventHandle InsertExceptionCatchPoint (Thread target, ThreadGroup group,
							      TargetType exception)
		{
			EventHandle handle = new CatchpointHandle (target, group, exception);
			if (handle == null)
				return null;

			events.Add (handle.Index, handle);
			return handle;
		}

		[Serializable]
		private class SessionInfo : ISerializable, IDeserializationCallback
		{
			public readonly Module[] Modules;
			public readonly EventHandle[] Events;

			public SessionInfo (DebuggerSession session)
			{
#if FIXME
				this.Modules = session.Process.Modules;
#endif
				this.Events = session.Events;
			}

			public void GetObjectData (SerializationInfo info, StreamingContext context)
			{
				info.AddValue ("modules", Modules);
				info.AddValue ("events", Events);
			}

			void IDeserializationCallback.OnDeserialization (object obj)
			{ }

			public DebuggerSession CreateSession (DebuggerClient client)
			{
				return new DebuggerSession (client, Events);
			}

			private SessionInfo (SerializationInfo info, StreamingContext context)
			{
				Modules = (Module []) info.GetValue (
					"modules", typeof (Module []));
				Events = (EventHandle []) info.GetValue (
					"events", typeof (EventHandle []));
			}
		}
	}
}
