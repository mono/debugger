using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using Mono.Debugger;
using Mono.Debugger.Backends;

namespace Mono.Debugger.Architecture
{
	internal sealed class BfdModule : NativeModule
	{
		Bfd bfd;
		string filename;

		public BfdModule (string filename, DebuggerBackend backend)
			: base (filename, backend)
		{
			this.filename = filename;
		}

		public override ILanguageBackend Language {
			get {
				return null;
			}
		}

		public override string FullName {
			get {
				return filename;
			}
		}

		public Bfd Bfd {
			get {
				return bfd;
			}

			set {
				bfd = value;
				if (bfd != null)
					OnSymbolsLoadedEvent ();
				else
					OnSymbolsUnLoadedEvent ();
			}
		}

		public override bool SymbolsLoaded {
			get {
				return Bfd != null;
			}
		}

		protected override void SymbolsChanged (bool loaded)
		{
		}

		protected override SourceInfo[] GetSources ()
		{
			return Bfd.GetSources ();
		}

		//
		// ISerializable
		//

		public override void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			base.GetObjectData (info, context);
		}

		private BfdModule (SerializationInfo info, StreamingContext context)
			: base (info, context)
		{ }
	}
}
