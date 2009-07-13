using System;
using Mono.Debugger;
using Mono.Debugger.Architectures;

namespace Mono.Debugger.Backend
{
	internal class PEReader : NativeExecutableReader
	{
		public override Module Module
		{
			get { throw new NotImplementedException(); }
		}

		public override TargetAddress LookupSymbol(string name)
		{
			throw new NotImplementedException();
		}

		public override TargetAddress LookupLocalSymbol(string name)
		{
			throw new NotImplementedException();
		}

		public override TargetAddress GetSectionAddress(string name)
		{
			throw new NotImplementedException();
		}

		public override TargetAddress EntryPoint
		{
			get {
				// FIXME
				return TargetAddress.Null;

			}
		}

		public override TargetReader GetReader(TargetAddress address)
		{
			throw new NotImplementedException();
		}

		public void ReadTypes()
		{
			throw new NotImplementedException();
		}
	}
}
