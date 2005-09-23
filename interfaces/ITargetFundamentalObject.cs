namespace Mono.Debugger.Languages
{
	public interface ITargetFundamentalObject : ITargetObject
	{
		// <summary>
		//   Returns a Mono object representing this object.
		// </summary>
		object GetObject (ITargetAccess target);

		// <summary>
		// </summary>
		void SetObject (ITargetObject obj);
	}
}
