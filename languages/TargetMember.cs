using System;
using System.Diagnostics;

namespace Mono.Debugger.Languages
{
	public enum TargetMemberAccessibility
	{
		Public,
		Protected,
		Internal,
		Private
	}

	[Serializable]
	public abstract class TargetMemberInfo : DebuggerMarshalByRefObject
	{
		public readonly TargetType Type;
		public readonly string Name;
		public readonly int Index;
		public readonly bool IsStatic;

		public readonly TargetMemberAccessibility Accessibility;

		protected TargetMemberInfo (TargetType type, string name, int index, bool is_static,
					    TargetMemberAccessibility accessibility)
		{
			this.Type = type;
			this.Name = name;
			this.Index = index;
			this.IsStatic = is_static;
			this.Accessibility = accessibility;
		}

		protected abstract string MyToString ();

		public override string ToString ()
		{
			return String.Format ("{0} ({1}:{2}:{3}:{4})",
					      GetType (), Type, Index, IsStatic, MyToString ());
		}
	}

	[Serializable]
	public abstract class TargetEnumInfo : TargetMemberInfo
	{
		public readonly bool HasConstValue;

		protected TargetEnumInfo (TargetType type, string name, int index, bool is_static,
					  TargetMemberAccessibility accessibility, int position,
					  int offset, bool has_const_value)
			: base (type, name, index, is_static, accessibility)
		{
			this.HasConstValue = has_const_value;
		}

		public abstract object ConstValue {
			get;
		}

		protected override string MyToString ()
		{
			return String.Format ("{0}:{1}", Type, HasConstValue);
		}
	}

	[Serializable]
	public abstract class TargetFieldInfo : TargetMemberInfo
	{
		public readonly int Offset;
		public readonly int Position;
		public readonly bool HasConstValue;

		protected TargetFieldInfo (TargetType type, string name, int index, bool is_static,
					   TargetMemberAccessibility accessibility, int position,
					   int offset, bool has_const_value)
			: base (type, name, index, is_static, accessibility)
		{
			this.Position = position;
			this.Offset = offset;
			this.HasConstValue = has_const_value;
		}

		public abstract bool IsCompilerGenerated {
			get;
		}

		public virtual DebuggerBrowsableState DebuggerBrowsable {
			get { return DebuggerBrowsableState.Collapsed; }
		}

		public abstract object ConstValue {
			get;
		}

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
					      bool is_static, TargetMemberAccessibility accessibility,
					      TargetFunctionType getter, TargetFunctionType setter)
			: base (type, name, index, is_static, accessibility)
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

		public virtual DebuggerBrowsableState DebuggerBrowsable {
			get { return DebuggerBrowsableState.Collapsed; }
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
					   bool is_static, TargetMemberAccessibility accessibility,
					   TargetFunctionType add, TargetFunctionType remove,
					   TargetFunctionType raise)
			: base (type, name, index, is_static, accessibility)
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
					    bool is_static, TargetMemberAccessibility accessibility,
					    string full_name)
			: base (type, name, index, is_static, accessibility)
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
