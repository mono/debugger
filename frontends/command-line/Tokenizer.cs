using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontends.Scripting
{
	public class SyntaxError : Exception
	{
		public SyntaxError (string message)
			: base ("syntax error: " + message)
		{ }
	}

	public class Tokenizer : yyParser.yyInput
	{
		//
		// Class variables
		// 
		static Hashtable keywords;
		static Hashtable short_keywords;
		static System.Text.StringBuilder id_builder;
		static System.Text.StringBuilder string_builder;
		static System.Text.StringBuilder number_builder;

		//
		// Values for the associated token returned
		//
		int putback_char;
		Object val;

		//
		// Class initializer
		// 
		static Tokenizer ()
		{
			InitTokens ();
			id_builder = new System.Text.StringBuilder ();
			string_builder = new System.Text.StringBuilder ();
			number_builder = new System.Text.StringBuilder ();
		}

		static void InitTokens ()
		{
			keywords = new Hashtable ();
			short_keywords = new Hashtable ();

			keywords.Add ("new", Token.NEW);
		}

		ScriptingContext context;
		TextReader reader;
		string ref_name;
		int current_token;
		int col = 1;

		//
		// Whether tokens have been seen on this line
		//
		bool tokens_seen = false;

		//
		// Details about the error encoutered by the tokenizer
		//
		string error_details;
		
		public string error {
			get {
				return error_details;
			}
		}

		public Tokenizer (ScriptingContext context, TextReader reader, string name)
		{
			this.context = context;
			this.reader = reader;
			this.ref_name = name;
		}

		public void restart ()
		{
			tokens_seen = false;
			col = 1;
		}

		//
		// Accepts exactly count (4 or 8) hex, no more no less
		//
		int getHex (int count, out bool error)
		{
			int i;
			int total = 0;
			int c;
			int top = count != -1 ? count : 4;
			
			getChar ();
			error = false;
			for (i = 0; i < top; i++){
				c = getChar ();
				
				if (c >= '0' && c <= '9')
					c = (int) c - (int) '0';
				else if (c >= 'A' && c <= 'F')
					c = (int) c - (int) 'A' + 10;
				else if (c >= 'a' && c <= 'f')
					c = (int) c - (int) 'a' + 10;
				else {
					error = true;
					return 0;
				}
				
				total = (total * 16) + c;
				if (count == -1){
					int p = peekChar ();
					if (p == -1)
						break;
					if (!is_hex ((char)p))
						break;
				}
			}
			return total;
		}

		int escape (int c)
		{
			bool error;
			int d;
			int v;

			d = peekChar ();
			if (c != '\\')
				return c;
			
			switch (d){
			case 'a':
				v = '\a'; break;
			case 'b':
				v = '\b'; break;
			case 'n':
				v = '\n'; break;
			case 't':
				v = '\t'; break;
			case 'v':
				v = '\v'; break;
			case 'r':
				v = '\r'; break;
			case '\\':
				v = '\\'; break;
			case 'f':
				v = '\f'; break;
			case '0':
				v = 0; break;
			case '"':
				v = '"'; break;
			case '\'':
				v = '\''; break;
			case 'x':
				v = getHex (-1, out error);
				if (error)
					goto default;
				return v;
			case 'u':
				v = getHex (4, out error);
				if (error)
					goto default;
				return v;
			case 'U':
				v = getHex (8, out error);
				if (error)
					goto default;
				return v;
			default:
				context.Error ("Unrecognized escape sequence in " + (char)d);
				return d;
			}
			getChar ();
			return v;
		}

		int getChar ()
		{
			if (putback_char != -1){
				int x = putback_char;
				putback_char = -1;

				return x;
			}
			return reader.Read ();
		}

		int peekChar ()
		{
			if (putback_char != -1)
				return putback_char;
			return reader.Peek ();
		}

		void putback (int c)
		{
			if (putback_char != -1)
				throw new Exception ("This should not happen putback on putback");
			putback_char = c;
		}

		public bool advance ()
		{
			return peekChar () >= 0;
		}

		bool is_identifier_start_character (char c)
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || Char.IsLetter (c);
		}

		bool is_identifier_part_character (char c)
		{
			return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || (c >= '0' && c <= '9') || Char.IsLetter (c);
		}

		int GetKeyword (string name, bool tokens_seen)
		{
			object o = keywords [name];

			if (o != null)
				return (int) o;

			if (tokens_seen)
				return -1;

			o = short_keywords [name];
			if (o != null)
				return (int) o;

			return -1;
		}

		//
		// Invoked if we know we have .digits or digits
		//
		int is_number (int c)
		{
			number_builder.Length = 0;

			if (c >= '0' && c <= '9'){
				if (c == '0' && peekChar () == 'x' || peekChar () == 'X'){
					getChar ();
					hex_digits (-1);

					string s = number_builder.ToString ();

					val = (long) System.UInt64.Parse (s, NumberStyles.HexNumber);
					return Token.NUMBER;
				}
				decimal_digits (c);

				val = (int) System.UInt32.Parse (number_builder.ToString ());
				return Token.INTEGER;
			}

			throw new Exception ("Is Number should never reach this point");
		}

		bool decimal_digits (int c)
		{
			int d;
			bool seen_digits = false;
			
			if (c != -1)
				number_builder.Append ((char) c);
			
			while ((d = peekChar ()) != -1){
				if (d >= '0' && d <= '9'){
					number_builder.Append ((char) d);
					getChar ();
					seen_digits = true;
				} else
					break;
			}
			
			return seen_digits;
		}

		bool is_hex (int e)
		{
			return (e >= '0' && e <= '9') || (e >= 'A' && e <= 'F') || (e >= 'a' && e <= 'f');
		}
		
		void hex_digits (int c)
		{
			int d;

			if (c != -1)
				number_builder.Append ((char) c);
			while ((d = peekChar ()) != -1){
				if (is_hex (d)){
					number_builder.Append ((char) d);
					getChar ();
				} else
					break;
			}
		}

		private int consume_identifier (int c, bool quoted) 
		{
			bool old_tokens_seen = tokens_seen;
			tokens_seen = true;

			id_builder.Length = 0;

			id_builder.Append ((char) c);
					
			while ((c = peekChar ()) != -1) {
				if (is_identifier_part_character ((char) c)){
					id_builder.Append ((char)getChar ());
					col++;
				} else 
					break;
			}

			string ids = id_builder.ToString ();
			int keyword = GetKeyword (ids, old_tokens_seen);

			if (keyword == -1 || quoted){
				val = ids;
				if (ids.Length > 512){
					context.Error ("Identifier too long (limit is 512 chars)");
				}
				return Token.IDENTIFIER;
			}

			return keyword;
		}

		private int consume_string (bool quoted) 
		{
			int c;
			string_builder.Length = 0;
								
			while ((c = getChar ()) != -1){
				if (c == '"'){
					if (quoted && peekChar () == '"'){
						string_builder.Append ((char) c);
						getChar ();
						continue;
					} else {
						val = string_builder.ToString ();
						return Token.STRING;
					}
				}

				if (c == '\n'){
					if (!quoted)
						context.Error ("Newline in constant");
					col = 0;
				} else
					col++;

				if (!quoted){
					c = escape (c);
					if (c == -1)
						return Token.ERROR;
				}
				string_builder.Append ((char) c);
			}

			context.Error ("Unterminated string literal");
			return Token.EOF;
		}

		private int consume_quoted_identifier ()
		{
			int c;

			id_builder.Length = 0;
								
			while ((c = getChar ()) != -1){
				if (c == '\''){
					val = id_builder.ToString ();
					return Token.IDENTIFIER;
				}

				if (c == '\n')
					col = 0;
				else
					col++;

				id_builder.Append ((char) c);
			}

			context.Error ("Unterminated quoted identifier");
			return Token.EOF;
		}

		private string consume_help ()
		{
			int c;
			StringBuilder sb = new StringBuilder ();
								
			while ((c = getChar ()) != -1){
				if (c == '\n') {
					col = 0;
					return sb.ToString ();
				}

				col++;
				sb.Append ((char) c);
			}

			return sb.ToString ();
		}

		public int xtoken ()
		{
			int c;

			val = null;
			// optimization: eliminate col and implement #directive semantic correctly.
			for (;(c = getChar ()) != -1; col++) {
				if (is_identifier_start_character ((char)c))
					return consume_identifier (c, false);

				if (c == 0)
					continue;
				else if (c == '#')
					return Token.HASH;
				else if (c == '@')
					return Token.AT;
				else if (c == '%')
					return Token.PERCENT;
				else if (c == '$')
					return Token.DOLLAR;
				else if (c == '.')
					return Token.DOT;
				else if (c == '!')
					return Token.BANG;
				else if (c == '=')
					return Token.ASSIGN;
				else if (c == '*')
					return Token.STAR;
				else if (c == '+')
					return Token.PLUS;
				else if (c == '-') // FIXME: negative numbers...
					return Token.MINUS;
				else if (c == '/')
					return Token.DIV;
				else if (c == '(')
					return Token.OPEN_PARENS;
				else if (c == ')')
					return Token.CLOSE_PARENS;
				else if (c == '[')
					return Token.OPEN_BRACKET;
				else if (c == ']')
					return Token.CLOSE_BRACKET;
				else if (c == ',')
					return Token.COMMA;
				else if (c == '<')
					return Token.OP_LT;
				else if (c == '>')
					return Token.OP_GT;
				else if (c == ':')
					return Token.COLON;
				else if (c == '&')
					return Token.AMPERSAND;

				if (c >= '0' && c <= '9') {
					tokens_seen = true;
					return is_number (c);
				}

				if (c == '"')
					return consume_string (false);

				if (c == ' ' || c == '\t' || c == '\f' || c == '\v' || c == '\r' || c == '\n'){
					if (current_token == Token.HASH) {
						error_details = "No whitespace allowed after `#'";
						return Token.ERROR;
					} else if (current_token == Token.AT) {
						error_details = "No whitespace allowed after `@'";
						return Token.ERROR;
					}

					if (c == '\t')
						col = (((col + 8) / 8) * 8) - 1;
					continue;
				}

				if (c == '\'')
					return consume_quoted_identifier ();

				error_details = "Unknown character `" + (char) c + "'";
				return Token.ERROR;
			}

			return Token.EOF;
		}

		public int token ()
		{
			current_token = xtoken ();
			return current_token;
		}

		public Object value ()
		{
			return val;
		}

		static Hashtable tokenValues;
		
		private static Hashtable TokenValueName
		{
			get {
				if (tokenValues == null)
					tokenValues = GetTokenValueNameHash ();

				return tokenValues;
			}
		}

		private static Hashtable GetTokenValueNameHash ()
		{
			Type t = typeof (Token);
			FieldInfo [] fields = t.GetFields ();
			Hashtable hash = new Hashtable ();
			foreach (FieldInfo field in fields) {
				if (field.IsLiteral && field.IsStatic && field.FieldType == typeof (int))
					hash.Add (field.GetValue (null), field.Name);
			}
			return hash;
		}

		//
		// Returns a verbose representation of the current location
		//
		public string location {
			get {
				string det;

				if (current_token == Token.ERROR)
					det = "detail: " + error_details;
				else
					det = "";
				
				// return "Line:     "+line+" Col: "+col + "\n" +
				//       "VirtLine: "+ref_line +
				//       " Token: "+current_token + " " + det;
				string current_token_name = TokenValueName [current_token] as string;
				if (current_token_name == null)
					current_token_name = current_token.ToString ();

				return String.Format ("{0}, Token: {1} {2}", ref_name,
						      current_token_name, det);
			}
		}
	}
}
