using System;

namespace Mono.Debugger
{
	public abstract class Variable : IVariable
	{
		string name;
		ITargetType type;

		protected Variable (string name, ITargetType type)
		{
			this.name = name;
			this.type = type;
		}

		public string Name {
			get {
				return name;
			}
		}

		public ITargetType Type {
			get {
				return type;
			}
		}

		public abstract ITargetObject GetObject (IStackFrame frame);

		public override string ToString ()
		{
			return String.Format ("{0} ({1},{2})", GetType (), Name, Type);
		}
	}
}
