using System;

namespace Mono.Debugger
{
	public abstract class Block
	{
		public enum Type
		{
			Lexical			= 1,
			CompilerGenerated	= 2,
			IteratorBody		= 3,
			IteratorDispatcher	= 4
		}

		public int Index {
			get;
			private set;
		}

		public Block Parent {
			get;
			protected set;
		}

		public abstract Block[] Children {
			get;
		}

		public Type BlockType {
			get;
			private set;
		}

		public int StartAddress {
			get;
			private set;
		}

		public int EndAddress {
			get;
			private set;
		}

		public bool IsIteratorBody {
			get {
				if (BlockType == Type.IteratorBody)
					return true;
				else if ((BlockType == Type.Lexical) && (Parent != null))
					return Parent.IsIteratorBody;
				else
					return false;
			}
		}

		protected Block (Type type, int index, int start, int end)
		{
			this.BlockType = type;
			this.Index = index;
			this.StartAddress = start;
			this.EndAddress = end;
		}

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3:x}:{4:x})", GetType (),
					      Index, BlockType, StartAddress, EndAddress);
		}
	}
}
