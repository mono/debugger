using System;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericParameterType : TargetType
	{
		int pos;
		bool is_mvar;
		string name;

		public MonoGenericParameterType (MonoLanguageBackend mono, int pos, bool is_mvar)
			: base (mono, TargetObjectKind.Alias)
		{
			this.pos = pos;
			this.is_mvar = is_mvar;
			this.name = String.Format ("{0}{1}", is_mvar ? "??" : "?", pos);
		}

		public MonoGenericParameterType (MonoLanguageBackend mono,
						 Cecil.GenericParameter gen_param)
			: base (mono, TargetObjectKind.Alias)
		{
			this.pos = gen_param.Position;
			this.is_mvar = gen_param.Owner is Cecil.MethodDefinition;
			this.name = gen_param.Name;
		}

		public override string Name {
			get { return name; }
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

		public int Position {
			get { return pos; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			Console.WriteLine ("GENERIC PARAM GET OBJECT: {0}", this);
			return null;
		}
	}
}
