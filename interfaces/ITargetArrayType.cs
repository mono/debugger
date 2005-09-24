namespace Mono.Debugger.Languages
{
	public interface ITargetArrayType : ITargetType
	{
		int Rank {
			get;
		}

		// <summary>
		//   The array's element type.
		// </summary>
		TargetType ElementType {
			get;
		}
	}
}
