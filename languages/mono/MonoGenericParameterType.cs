using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericParameterType : TargetType
	{
		Cecil.GenericParameter gen_param;

		public MonoGenericParameterType (MonoLanguageBackend mono,
						 Cecil.GenericParameter gen_param)
			: base (mono, TargetObjectKind.Alias)
		{
			this.gen_param = gen_param;
		}

		public override string Name {
			get { return gen_param.Name; }
		}

		public override bool IsByRef {
			get { return false; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { throw new InvalidOperationException (); }
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			throw new NotImplementedException ();
		}

		internal TargetObject GetObject (StackFrame frame, TargetLocation location)
		{
			Console.WriteLine ("GET OBJECT: {0} {1} {2}", this, frame, location);
			return null;
		}
	}
}
