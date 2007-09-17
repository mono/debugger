using System;
using System.Text;
using Cecil = Mono.Cecil;

namespace Mono.Debugger.Languages.Mono
{
	internal class MonoGenericInstanceType : TargetClassType
	{
		public readonly MonoClassType UnderlyingType;
		public readonly MonoGenericContext GenericContext;
		string full_name;

		MonoFieldInfo[] inflated_fields;

		public MonoGenericInstanceType (MonoClassType underlying, MonoGenericContext context)
			: base (underlying.File.MonoLanguage, TargetObjectKind.Class)
		{
			this.UnderlyingType = underlying;
			this.GenericContext = context;

			Console.WriteLine ("GENERIC INSTANCE TYPE CTOR: {0} {1}", underlying, context);

			StringBuilder sb = new StringBuilder (underlying.Type.FullName);
			sb.Append ("<");
			for (int i = 0; i < context.MethodInst.Types.Length; i++) {
				if (i > 0)
					sb.Append (",");
				sb.Append (context.MethodInst.Types [i].Name);
			}
			sb.Append (">");
			full_name = sb.ToString ();
		}

		public override string Name {
			get { return full_name; }
		}

		public override bool IsByRef {
			get { return true; }
		}

		public override bool HasFixedSize {
			get { return true; }
		}

		public override int Size {
			get { return UnderlyingType.Size; }
		}

		protected override TargetObject DoGetObject (TargetLocation location)
		{
			return new MonoGenericInstanceObject (this, location);
		}

		public override bool HasParent {
			get { return false; }
		}

		public override Module Module {
			get { return UnderlyingType.Module; }
		}

		public override TargetClassType ParentType {
			get { return null; }
		}

		void get_fields ()
		{
			if (inflated_fields != null)
				return;

			MonoFieldInfo[] fields = (MonoFieldInfo[]) UnderlyingType.Fields;
			inflated_fields = new MonoFieldInfo [fields.Length];
			for (int i = 0; i < inflated_fields.Length; i++) {
				inflated_fields [i] = fields [i].InflateField (GenericContext);
			}
		}

		public override TargetFieldInfo[] Fields {
			get {
				get_fields ();
				return inflated_fields;
			}
		}

		public override TargetFieldInfo[] StaticFields {
			get { return new TargetFieldInfo [0]; }
		}

		internal TargetObject GetField (Thread target, TargetLocation location,
						MonoFieldInfo finfo)
		{
			Console.WriteLine ("GET FIELD: {0} {1}", this, finfo);

			return null;
		}

		public override TargetObject GetStaticField (Thread target,
							     TargetFieldInfo field)
		{
			return null;
		}

		public override void SetStaticField (Thread target, TargetFieldInfo field,
						     TargetObject obj)
		{ }

		public override TargetPropertyInfo[] Properties {
			get { return new TargetPropertyInfo [0]; }
		}

		public override TargetPropertyInfo[] StaticProperties {
			get { return new TargetPropertyInfo [0]; }
		}

		public override TargetEventInfo[] Events {
			get { return new TargetEventInfo [0]; }
		}

		public override TargetEventInfo[] StaticEvents {
			get { return new TargetEventInfo [0]; }
		}

		public override TargetMethodInfo[] Methods {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] StaticMethods {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] Constructors {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMethodInfo[] StaticConstructors {
			get { return new TargetMethodInfo [0]; }
		}

		public override TargetMemberInfo FindMember (string name, bool search_static,
							     bool search_instance)
		{
			return null;
		}
	}
}
