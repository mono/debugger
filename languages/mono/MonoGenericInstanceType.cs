using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetGenericInstanceType
	{
		MonoClassType container;
		TargetAddress cached_ptr = TargetAddress.Null;

		MonoClassInfo class_info;

		public MonoGenericInstanceType (MonoClassType container, MonoGenericContext context,
						TargetAddress cached_ptr)
			: base (container.Language)
		{
			this.container = container;
			this.cached_ptr = cached_ptr;
		}

		public MonoGenericInstanceType (MonoClassType container, MonoGenericContext context,
						MonoClassInfo class_info)
			: base (container.Language)
		{
			this.container = container;
			this.class_info = class_info;
		}

		public override TargetClassType ContainerType {
			get { return container; }
		}

		public override string Name {
			get { return container.Name; }
		}

		public override bool IsByRef {
			get { return container.IsByRef; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override bool HasClassType {
			get { return false; }
		}

		public override TargetClassType ClassType {
			get { throw new InvalidOperationException (); }
		}

		public override int Size {
			get { return 0; }
		}

		protected MonoClassInfo ResolveClass (TargetMemoryAccess target)
		{
			if (class_info != null)
				return class_info;

			class_info = DoResolveClass (target);
			if (class_info != null)
				return class_info;

			throw new TargetException (TargetError.ClassNotInitialized,
						   "Class `{0}' not initialized yet.", Name);
		}

		MonoClassInfo DoResolveClass (TargetMemoryAccess target)
		{
			TargetAddress klass = target.ReadAddress (cached_ptr);
			if (klass.IsNull)
				return null;

			return container.File.MonoLanguage.ReadClassInfo (target, klass);
		}

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			ResolveClass (target);
			return new MonoGenericInstanceObject (this, class_info, location);
		}
	}
}
