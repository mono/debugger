using System;
using System.Runtime.Serialization;
using System.Xml;

namespace Mono.Debugger
{
	public class Display : DebuggerMarshalByRefObject
	{
		public DebuggerSession Session {
			get { return session; }
		}

		public int Index {
			get { return index; }
		}

		public string Text {
			get { return text; }
		}

		public bool IsEnabled {
			get { return enabled; }
			set { enabled = value; }
		}

		//
		// Session handling.
		//

		internal void GetSessionData (XmlElement root)
		{
			XmlElement element = root.OwnerDocument.CreateElement ("Display");
			root.AppendChild (element);

			element.SetAttribute ("index", index.ToString ());
			element.SetAttribute ("enabled", enabled ? "true" : "false");
			element.SetAttribute ("text", text);
		}

		//
		// Everything below is private.
		//

		static int next_index = 0;
		private readonly DebuggerSession session;
		private readonly string text;
		private readonly int index;
		bool enabled = true;

		internal Display (DebuggerSession session, int index, bool enabled, string text)
		{
			this.session = session;
			this.index = index;
			this.enabled = enabled;
			this.text = text;
		}

		internal Display (DebuggerSession session, string text)
			: this (session, ++next_index, true, text)
		{ }
	}
}
