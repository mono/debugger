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
	internal sealed class BfdModule : NativeModule, ISymbolContainer
	{
		Bfd bfd;
		string filename;
		bool is_library;
		TargetAddress start, end;

		public BfdModule (string filename, DebuggerBackend backend, bool is_library)
			: base (filename, backend)
		{
			this.filename = filename;
			this.is_library = is_library;

			backend.ModuleManager.AddModule (this);
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
				if (bfd == value)
					return;

				bfd = value;
				if (bfd != null) {
					if (bfd.IsContinuous) {
						start = bfd.StartAddress;
						end = bfd.EndAddress;
						is_library = true;
						CheckLoaded ();
					}
					OnSymbolsLoadedEvent ();
				} else
					OnSymbolsUnLoadedEvent ();
			}
		}

		internal void BfdDisposed ()
		{
			bfd = null;
		}

		public override void UnLoad ()
		{
			Bfd = null;
			base.UnLoad ();
		}

		public override bool IsLoaded {
			get {
				return base.IsLoaded && (!is_library || !start.IsNull);
			}
		}

		public override bool SymbolsLoaded {
			get {
				return (Bfd != null) && (Bfd.SymbolTable != null);
			}
		}

		protected override void SymbolsChanged (bool loaded)
		{
			if (loaded)
				OnSymbolsLoadedEvent ();
			else
				OnSymbolsUnLoadedEvent ();
		}

		protected override SourceInfo[] GetSources ()
		{
			if (Bfd == null)
				return null;

			return Bfd.GetSources ();
		}

		protected override ISymbolTable GetSymbolTable ()
		{
			if (Bfd == null)
				return null;

			return Bfd.SymbolTable;
		}

		//
		// ISymbolContainer
		//

		public bool IsContinuous {
			get {
				return is_library;
			}
		}

		public TargetAddress StartAddress {
			get {
				if (!IsContinuous || !IsLoaded)
					throw new InvalidOperationException ();

				return start;
			}
		}

		public TargetAddress EndAddress {
			get {
				if (!IsContinuous || !IsLoaded)
					throw new InvalidOperationException ();

				return end;
			}
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
