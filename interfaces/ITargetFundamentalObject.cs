namespace Mono.Debugger.Languages
{
	public interface ITargetFundamentalObject : ITargetObject
	{
		// <summary>
		//   If true, you may get a Mono object representing this object with the
		//   @Object property.
		// </summary>
		bool HasObject {
			get;
		}

		// <summary>
		//   Returns a Mono object representing this object.
		// </summary>
		object Object {
			get; set;
		}
	}
}
