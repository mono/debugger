using System;

namespace Mono.Debugger.Languages
{
	[Serializable]
	public abstract class TargetMemberInfo
	{
		public readonly TargetType Type;
		public readonly string Name;
		public readonly int Index;
		public readonly bool IsStatic;

		protected TargetMemberInfo (TargetType type, string name, int index, bool is_static)
		{
			this.Type = type;
			this.Name = name;
			this.Index = index;
			this.IsStatic = is_static;
		}

		protected abstract string MyToString ();

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4})",
					      GetType (), Type, Index, IsStatic, MyToString ());
		}
	}

	[Serializable]
	public abstract class TargetFieldInfo : TargetMemberInfo
	{
		public readonly int Offset;
		public readonly int Position;
		public readonly bool HasConstValue;

		protected TargetFieldInfo (TargetType type, string name, int index, bool is_static,
					   int position, int offset, bool has_const_value)
			: base (type, name, index, is_static)
		{
			this.Position = position;
			this.Offset = offset;
			this.HasConstValue = has_const_value;
		}

		public abstract TargetObject GetConstValue (TargetAccess target);

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}:{2}", Type, Offset, HasConstValue);
		}
	}

	[Serializable]
	public abstract class TargetPropertyInfo : TargetMemberInfo
	{
		public readonly TargetFunctionType Getter;
		public readonly TargetFunctionType Setter;

		protected TargetPropertyInfo (TargetType type, string name, int index,
					      bool is_static, TargetFunctionType getter,
					      TargetFunctionType setter)
			: base (type, name, index, is_static)
		{
			this.Getter = getter;
			this.Setter = setter;
		}

		public bool CanRead {
			get { return Getter != null; }
		}

		public bool CanWrite {
			get { return Setter != null; }
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", Getter, Setter);
		}
	}

	[Serializable]
	public abstract class TargetEventInfo : TargetMemberInfo
	{
		public readonly TargetFunctionType Add;
		public readonly TargetFunctionType Remove;
		public readonly TargetFunctionType Raise;

		protected TargetEventInfo (TargetType type, string name, int index,
					   bool is_static, TargetFunctionType add,
					   TargetFunctionType remove, TargetFunctionType raise)
			: base (type, name, index, is_static)
		{
			this.Add = add;
			this.Remove = remove;
			this.Raise = raise;
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}:{2}", Add, Remove, Raise);
		}
	}

	[Serializable]
	public abstract class TargetMethodInfo : TargetMemberInfo
	{
		public readonly string FullName;
		public new readonly TargetFunctionType Type;

		protected TargetMethodInfo (TargetFunctionType type, string name, int index,
					    bool is_static, string full_name)
			: base (type, name, index, is_static)
		{
			this.Type = type;
			this.FullName = full_name;
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}", FullName);
		}
	}
}
