using System;
using System.Text;
using System.Collections;
using System.Reflection;

namespace Mono.Debugger.Frontends.CommandLine
{
	/// <summary>
	///   Reads input from either the keyboard or a script.
	/// </summary>
	public interface InputProvider
	{
		// <summary>
		//   Read a new line of input.
		// </summary>
		string ReadInput ();

		// <summary>
		//   The parser discovered that it needs one more
		//   line of imput; display a special prompt to the user.
		// </summary>
		string ReadMoreInput ();

		// <summary>
		//   An error occur.
		// </summary>
		void Error (int pos, string message);
	}

	public class ParserError : Exception
	{
		public ParserError (string message)
			: base (message)
		{ }

		public ParserError (string message, params string[] args)
			: this (String.Format (message, args))
		{ }
	}

	public class SyntaxError : ParserError
	{
		public SyntaxError (string message)
			: base ("syntax error: " + message)
		{ }

		public SyntaxError (Parser parser)
			: this (String.Format ("{0} expected", parser.CurrentStateName))
		{ }
	}

	public class Parser
	{
		Engine engine;
		InputProvider input;
		State state;
		ArrayList arguments = null;
		CommandGroup current_group;
		Command current_command;
		ExpressionParser parser;
		ScriptingContext context;
		Tokenizer lexer;
		int pos = -1;

		public Parser (Engine engine, ScriptingContext context, InputProvider input)
		{
			this.engine = engine;
			this.context = context;
			this.input = input;

			lexer = new Tokenizer (this, input);
			parser = new ExpressionParser (this, lexer);

			Reset ();
		}

		void Reset ()
		{
			state = State.Command;
			current_group = Engine.Root;
			current_command = null;
			arguments = null;
			lexer.restart ();
		}

		public State CurrentState {
			get { return state; }
		}

		public string CurrentStateName {
			get { return GetStateName (state); }
		}

		public string GetStateName (State state)
		{
			switch (state) {
			case State.Command:
				return "command";

			case State.CommandGroup:
				return current_group.ToString ();

			case State.ParameterOrArgument:
				return "parameter, argument or end of line";

			case State.Parameter:
				return "parameter name";

			case State.Argument:
				return "argument";

			case State.EndOfLine:
				return "end of line";

			case State.Integer:
				return "integer";

			case State.Finished:
				return "<finished parsing command>";

			default:
				throw new InternalError (
					"Parser in unknown state: {0}", state);
			}
		}

		protected int NextToken ()
		{
			int token;
			do {
				token = lexer.token ();
				if (token == Token.EOF)
					throw new SyntaxError ("unexpected end of file");

				lexer.advance ();
			} while (token == Token.EOL);

			return token;
		}

		protected int PeekToken ()
		{
			return lexer.token ();
		}

		protected void Advance ()
		{
			if (!lexer.advance ())
				throw new SyntaxError ("unexpected end of file");
		}

		protected void ParseCommand ()
		{
			int token = PeekToken ();
			if ((token == Token.EOL) || (token == Token.EOF)) {
				state = State.Finished;
				return;
			}

			if (token != Token.IDENTIFIER)
				throw new SyntaxError (this);

			string identifier = (string) lexer.value ();
			Advance ();

			object result = current_group.Lookup (identifier);
			if (result == null)
				throw new ParserError ("No such command: `{0}'", identifier);

			if (result is CommandGroup) {
				current_group = (CommandGroup) result;
				state = State.CommandGroup;
				return;
			}

			current_command = (Command) Activator.CreateInstance ((Type) result);
			state = State.ParameterOrArgument;
		}

		protected void ParseCommandGroup ()
		{
			int token = NextToken ();
			if (token != Token.IDENTIFIER)
				throw new SyntaxError (this);

			string identifier = (string) lexer.value ();
			Advance ();

			object result = current_group.Lookup (identifier);
			if (result == null)
				throw new ParserError ("No such command: `{0}'",
						       current_group.MakeName (identifier));

			if (result is CommandGroup) {
				current_group = (CommandGroup) result;
				state = State.CommandGroup;
				return;
			}

			current_command = (Command) Activator.CreateInstance ((Type) result);
			state = State.ParameterOrArgument;
		}

		protected void ParseParameterOrArgument ()
		{
			int token = PeekToken ();
			if (token == Token.MINUS) {
				Advance ();
				state = State.Parameter;
				return;
			}

			if ((token == Token.EOF) || (token == Token.EOL))
				state = State.EndOfLine;
			else
				state = State.Argument;
		}

		protected void ParseParameter ()
		{
			int token = NextToken ();
			if (token != Token.IDENTIFIER)
				throw new SyntaxError (this);

			string id = (string) lexer.value ();

			ArgumentAttribute attr;
			PropertyInfo pi = engine.GetParameter (current_command, id, out attr);
			if ((pi == null) || (attr == null))
				throw new ParserError ("No such parameter: {0}", id);

			object result = null;
			switch (attr.Type) {
			case ArgumentType.Integer:
				state = State.Integer;
				result = ParseInteger ();
				break;

			case ArgumentType.Flag:
				result = true;
				break;

			case ArgumentType.Process: {
				ProcessCommand pcommand = (ProcessCommand) current_command;
				if (pcommand.ProcessExpression != null)
					throw new ParserError (
						"The `{0}' argument can only be given once",
						attr.Name);

				FrameCommand fcommand = current_command as FrameCommand;
				if ((fcommand != null) && (fcommand.FrameExpression != null))
					throw new ParserError (
						"When specifying both a process and a stack " +
						"frame, the process must come first");

				state = State.Integer;
				result = new ProcessExpression (ParseInteger ());
				break;
			}

			case ArgumentType.Frame: {
				FrameCommand fcommand = (FrameCommand) current_command;
				if (fcommand.FrameExpression != null)
					throw new ParserError (
						"The `{0}' argument can only be given once",
						attr.Name);

				state = State.Integer;
				result = new FrameExpression (
					fcommand.ProcessExpression, ParseInteger ());
				break;
			}

			default:
				throw new InternalError ();
			}

			if (result == null)
				throw new SyntaxError (this);

			pi.SetValue (current_command, result, null);
			state = State.ParameterOrArgument;
		}

		protected void ParseArgument ()
		{
			lexer.ParsingExpression = true;

			object expression = parser.Parse ();

			lexer.ParsingExpression = false;

			if (arguments == null)
				arguments = new ArrayList ();

			arguments.Add (expression);

			state = State.ParameterOrArgument;
		}

		protected void ParseEndOfLine ()
		{
			int token = PeekToken ();
			if ((token != Token.EOF) && (token != Token.EOL))
				throw new SyntaxError (this);

			state = State.Finished;

			if (current_command == null)
				return;

			object[] args = null;
			if (arguments != null) {
				args = new object [arguments.Count];
				arguments.CopyTo (args);
			}

			if (!current_command.Resolve (context, args))
				throw new ParserError ("Cannot resolve command");

			current_command.Execute (context);
		}

		protected int ParseInteger ()
		{
			int token = NextToken ();
			if (token != Token.INTEGER)
				throw new SyntaxError (this);

			return (int) lexer.value ();
		}

		/// <summary>
		///   What the parser expects to find next.
		/// </summary>
		public enum State
		{
			Command,
			CommandGroup,
			ParameterOrArgument,
			Parameter,
			Argument,
			EndOfLine,
			Integer,
			Finished
		}

		protected void ParseState ()
		{
			switch (state) {
			case State.Command:
				ParseCommand ();
				break;

			case State.CommandGroup:
				ParseCommandGroup ();
				break;

			case State.ParameterOrArgument:
				ParseParameterOrArgument ();
				break;

			case State.Parameter:
				ParseParameter ();
				break;

			case State.Argument:
				ParseArgument ();
				break;

			case State.EndOfLine:
				ParseEndOfLine ();
				break;

			default:
				throw new InternalError (
					"Parser in unknown state: {0}", state);
			}
		}

		protected void Parse ()
		{
			while (state != State.Finished)
				ParseState ();
		}

		public void Run ()
		{
			int token;
			while ((token = PeekToken ()) != Token.EOF) {
				try {
					state = State.Command;
					Parse ();
					Reset ();
				} catch (ParserError error) {
					input.Error (lexer.Location, error.Message);
					Reset ();
				} catch (Exception ex) {
					string message = String.Format (
						"Parser caught exception while in state " +
						"{0}: {1}", state, ex);

					input.Error (lexer.Location, message);
					Reset ();
				}
			}
		}
	}
}
