namespace Mono.Debugger.Languages
{
	public abstract class TargetGenericInstanceType : TargetClassType
	{
		protected TargetGenericInstanceType (Language language)
			: base (language, TargetObjectKind.GenericInstance)
		{ }

		public abstract TargetClassType ContainerType {
			get;
		}

		public abstract TargetType[] TypeArguments {
			get;
		}

		public override bool ContainsGenericParameters {
			get { return true; }
		}
	}
}
