using System.Runtime.Serialization;

namespace Mono.Debugger.Languages
{
	public abstract class TargetFunctionType : TargetType
	{
		protected TargetFunctionType (Language language)
			: base (language, TargetObjectKind.Function)
		{ }

		public abstract bool HasReturnValue {
			get;
		}

		public abstract TargetType ReturnType {
			get;
		}

		public abstract TargetType[] ParameterTypes {
			get;
		}

		public abstract SourceMethod Source {
			get;
		}

		// <summary>
		//   The current programming language's native representation of
		//   a method.
		// </summary>
		public abstract object MethodHandle {
			get;
		}

		public Module Module {
			get { return DeclaringType.Module; }
		}

		public abstract TargetClassType DeclaringType {
			get;
		}

		public abstract bool IsLoaded {
			get;
		}

		public abstract TargetAddress GetMethodAddress (Thread target);

		//
		// Session handling.
		//

		protected abstract void GetSessionData (SerializationInfo info);

		protected abstract object SetSessionData (SerializationInfo info);

		protected internal class SessionSurrogate : ISerializationSurrogate
		{
			public virtual void GetObjectData (object obj, SerializationInfo info,
							   StreamingContext context)
			{
				TargetFunctionType type = (TargetFunctionType) obj;
				type.GetSessionData (info);
			}

			public object SetObjectData (object obj, SerializationInfo info,
						     StreamingContext context,
						     ISurrogateSelector selector)
			{
				TargetFunctionType type = (TargetFunctionType) obj;
				return type.SetSessionData (info);
			}
		}
	}
}
