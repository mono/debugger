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
	internal sealed class BfdModule : IModule, ISerializable
	{
		public bool IsLoaded;
		public bool SymbolsLoaded;
		public bool LoadSymbols;
		public bool StepInto;

		string name;
		string filename;
		bool load_symbols;
		bool step_into;

		public BfdModule (string filename)
		{
			this.name = this.filename = filename;
		}

		public ILanguageBackend Language {
			get {
				return null;
			}
		}

		public string Name {
			get {
				return name;
			}
		}

		public string FullName {
			get {
				return filename;
			}
		}

		bool IModule.IsLoaded {
			get {
				return IsLoaded;
			}
		}

		bool IModule.SymbolsLoaded {
			get {
				return SymbolsLoaded;
			}
		}

		bool IModule.LoadSymbols {
			get {
				return LoadSymbols;
			}

			set {
				LoadSymbols = value;
			}
		}

		bool IModule.StepInto {
			get {
				return StepInto;
			}

			set {
				StepInto = value;
			}
		}

		//
		// ISerializable
		//

		public void GetObjectData (SerializationInfo info, StreamingContext context)
		{
			info.AddValue ("name", name);
			info.AddValue ("filename", filename);
			info.AddValue ("load_symbols", load_symbols);
			info.AddValue ("step_into", step_into);
		}

		private BfdModule (SerializationInfo info, StreamingContext context)
		{
			name = info.GetString ("name");
			filename = info.GetString ("filename");
			load_symbols = info.GetBoolean ("load_symbols");
			step_into = info.GetBoolean ("step_into");
		}
	}
}
