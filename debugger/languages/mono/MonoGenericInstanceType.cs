using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetGenericInstanceType, IMonoStructType
	{
		public readonly MonoClassType Container;
		TargetType[] type_args;
		TargetAddress class_ptr;
		MonoClassInfo class_info;
		string full_name;

		public MonoGenericInstanceType (MonoClassType container, TargetType[] type_args,
						TargetAddress class_ptr)
			: base (container.File.MonoLanguage)
		{
			this.Container = container;
			this.type_args = type_args;
			this.class_ptr = class_ptr;

			StringBuilder sb = new StringBuilder (container.Type.FullName);
			sb.Append ('<');
			for (int i = 0; i < type_args.Length; i++) {
				if (i > 0)
					sb.Append (',');
				sb.Append (type_args [i].Name);
			}
			sb.Append ('>');
			full_name = sb.ToString ();
		}

		TargetStructType IMonoStructType.Type {
			get { return this; }
		}

		public override string Name {
			get { return full_name; }
		}

		public override Module Module {
			get { return Container.Module; }
		}

		public MonoSymbolFile File {
			get { return Container.File; }
		}

		public override TargetClassType ContainerType {
			get { return Container; }
		}

		public override TargetType[] TypeArguments {
			get { return type_args; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override bool IsByRef {
			get { return Container.IsByRef; }
		}

		public override bool HasFixedSize {
			get { return false; }
		}

		public override int Size {
			get { return 2 * Language.TargetInfo.TargetAddressSize; }
		}

		public override bool HasParent {
			get { return true; }
		}

		internal override TargetStructType GetParentType (TargetMemoryAccess target)
		{
			ResolveClass (target, true);

			MonoClassInfo parent = class_info.GetParent (target);
			if (parent == null)
				return null;

			if (!parent.IsGenericClass)
				return parent.ClassType;

			return File.MonoLanguage.ReadGenericClass (target, parent.GenericClass);
		}

		public MonoClassInfo ResolveClass (TargetMemoryAccess target, bool fail)
		{
			if (class_info != null)
				return class_info;

			if (class_ptr.IsNull)
				return null;

			TargetAddress klass = target.ReadAddress (class_ptr);
			if (klass.IsNull)
				return null;

			class_info = File.MonoLanguage.ReadClassInfo (target, klass);
			if (class_info != null) {
				ResolveClass (target, class_info, fail);
				return class_info;
			}

			if (fail)
				throw new TargetException (TargetError.ClassNotInitialized,
							   "Class `{0}' not initialized yet.", Name);

			return null;
		}

		public void ResolveClass (TargetMemoryAccess target, MonoClassInfo info, bool fail)
		{
			this.class_info = info;
		}

		internal override TargetClass GetClass (TargetMemoryAccess target)
		{
			ResolveClass (target, true);
			return class_info;
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			ResolveClass (target, true);
			return new MonoGenericInstanceObject (this, class_info, location);
		}
	}
}
