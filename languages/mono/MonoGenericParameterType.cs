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
			get {
				Console.WriteLine ("GP ISBYREF: {0}", Environment.StackTrace);
				return false;
			}
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

		internal TargetType GetType (StackFrame frame)
		{
			MonoMethodFrameInfo info = (MonoMethodFrameInfo) frame.MethodFrameInfo;
			if (info == null)
				return null;

			Console.WriteLine ("GET OBJECT: {0} {1}", info, gen_param.Owner.GetType ());

			MonoGenericInst inst;
			if (gen_param.Owner is Cecil.MethodDefinition)
				inst = info.MethodInst;
			else
				inst = info.ClassInst;

			return inst.Types [gen_param.Position];
		}
	}
}
