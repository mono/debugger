namespace Mono.Debugger.Frontend
{
	// <summary>
	// The interface the Interpreter uses to parse expressions the user
	// enters on the command line.
	// </summary>
	// <remarks>
	// Classes that implement this interface must also provide a
	// constructor of the form:
	//
	//    public ExpressionCtor (ScriptingContext context, string name);
	//
	// as those arguments are passed along by Interpreter when creating
	// the instance.

	// </remarks>
	public interface IExpressionParser
	{
		// used for debugging purposes
		bool Verbose { get; set; }

		Expression Parse (string text);
	}
}
