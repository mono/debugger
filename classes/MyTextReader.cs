using System;
using System.IO;
using System.Runtime.InteropServices;
using GLib;

namespace Mono.Debugger
{
	public abstract class MyTextReader : TextReader
	{
		public abstract void Discard ();
	}
}
