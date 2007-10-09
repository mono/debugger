using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetGenericInstanceType
	{
		MonoClassType container;

		public MonoGenericInstanceType (MonoClassType container, MonoGenericContext context,
						TargetAddress cached_ptr)
			: base (container.Language)
		{
			this.container = container;
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

		protected override TargetObject DoGetObject (TargetMemoryAccess target,
							     TargetLocation location)
		{
			return new MonoGenericInstanceObject (this, location);
		}
	}
}
