using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Configuration;
using System.Globalization;
using System.Reflection;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.InteropServices;

using Mono.Debugger.Backends;
using Mono.Debugger.Architecture;

namespace Mono.Debugger.Backends
{
	internal delegate bool DaemonEventHandler (NativeProcess process, Inferior inferior,
						   TargetEventArgs args);

	internal abstract class NativeProcess : Process
	{
		protected NativeProcess (ProcessStart start)
			: base (start)
		{ }

		public DaemonEventHandler DaemonEventHandler;

		public void SetDaemonFlag ()
		{
			is_daemon = true;
		}

		public abstract void Start (TargetAddress start, bool is_main);
	}
}
