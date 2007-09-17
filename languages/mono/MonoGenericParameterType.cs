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

		public int Position {
			get { return gen_param.Position; }
		}

		internal override TargetObject GetObject (TargetLocation location)
		{
			throw new NotImplementedException ();
		}

		internal override TargetType InflateType (Mono.MonoGenericContext context)
		{
			MonoGenericInst inst;
			if (gen_param.Owner is Cecil.MethodDefinition)
				inst = context.MethodInst;
			else
				inst = context.ClassInst;

			return inst.Types [gen_param.Position];
		}
	}
}
