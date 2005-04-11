using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

namespace Mono.Debugger.Frontend.CSharp
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

		//
		// Values for the associated token returned
		//
		int putback_char;
		Object val;

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
		

		//
		// Class initializer
		// 
		static Tokenizer ()
		{
			InitTokens ();
			id_builder = new System.Text.StringBuilder ();
			string_builder = new System.Text.StringBuilder ();
		}

		static void InitTokens ()
		{
			keywords = new Hashtable ();
			short_keywords = new Hashtable ();

			keywords.Add ("new", Token.NEW);
			keywords.Add ("this", Token.THIS);
			keywords.Add ("base", Token.BASE);
			keywords.Add ("catch", Token.CATCH);
			keywords.Add ("true", Token.TRUE);
			keywords.Add ("false", Token.FALSE);
			keywords.Add ("null", Token.FALSE);
		}

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
			
			GetChar ();
			error = false;
			for (i = 0; i < top; i++){
				c = GetChar ();
				
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
					int p = PeekChar ();
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

			d = PeekChar ();
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
			GetChar ();
			return v;
		}

		int GetChar ()
		{
			if (putback_char != -1){
				int x = putback_char;
				putback_char = -1;

				return x;
			}
			return reader.Read ();
		}

		int PeekChar ()
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
			return PeekChar () >= 0;
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

#region "taken from MonoDevelop's Lexer.cs"
		int ReadDigit(char ch)
		{
			++col;

			StringBuilder sb = new StringBuilder(ch.ToString());
			StringBuilder prefix = new StringBuilder();
			StringBuilder suffix = new StringBuilder();
			
			bool ishex      = false;
			bool isunsigned = false;
			bool islong     = false;
			bool isfloat    = false;
			bool isdouble   = false;
			bool isdecimal  = false;
			
			if (ch == '0' && Char.ToUpper((char)PeekChar()) == 'X') {
				const string hex = "0123456789ABCDEF";
				GetChar(); // skip 'x'
				++col;
				while (hex.IndexOf(Char.ToUpper((char)PeekChar())) != -1) {
					sb.Append(Char.ToUpper((char)GetChar()));
					++col;
				}
				ishex = true;
				prefix.Append("0x");
			} else {
				while (Char.IsDigit((char)PeekChar())) {
					sb.Append((char)GetChar());
					++col;
				}
			}
			
			if (PeekChar() == '.') { // read floating point number
				isdouble = true; // double is default
				if (ishex) {
					error_details = "No hexadecimal floating point values allowed";
					return Token.ERROR;
				}
				sb.Append((char)GetChar());
				++col;
				
				while (Char.IsDigit((char)PeekChar())) { // read decimal digits beyond the dot
					sb.Append((char)GetChar());
					++col;
				}
			}
			
			if (Char.ToUpper((char)PeekChar()) == 'E') { // read exponent
				isdouble = true;
				sb.Append((char)GetChar());
				++col;
				if (PeekChar() == '-' || PeekChar() == '+') {
					sb.Append((char)GetChar());
					++col;
				}
				while (Char.IsDigit((char)PeekChar())) { // read exponent value
					sb.Append((char)GetChar());
					++col;
				}
				isunsigned = true;
			}
			
			if (Char.ToUpper((char)PeekChar()) == 'F') { // float value
				suffix.Append(PeekChar());
				GetChar();
				++col;
				isfloat = true;
			} else if (Char.ToUpper((char)PeekChar()) == 'D') { // double type suffix (obsolete, double is default)
				suffix.Append(PeekChar());
				GetChar();
				++col;
				isdouble = true;
			} else if (Char.ToUpper((char)PeekChar()) == 'M') { // decimal value
				suffix.Append(PeekChar());
				GetChar();
				++col;
				isdecimal = true;
			} else if (!isdouble) {
				if (Char.ToUpper((char)PeekChar()) == 'U') {
					suffix.Append(PeekChar());
					GetChar();
					++col;
					isunsigned = true;
				}
				
				if (Char.ToUpper((char)PeekChar()) == 'L') {
					suffix.Append(PeekChar());
					GetChar();
					++col;
					islong = true;
					if (!isunsigned && Char.ToUpper((char)PeekChar()) == 'U') {
						suffix.Append(PeekChar());
						GetChar();
						++col;
						isunsigned = true;
					}
				}
			}
			
			string digit = sb.ToString();
			//string stringValue = String.Concat(prefix.ToString(), digit, suffix.ToString());
			if (isfloat) {
				try {
					NumberFormatInfo numberFormatInfo = new NumberFormatInfo();
					numberFormatInfo.CurrencyDecimalSeparator = ".";
					val = Single.Parse(digit, numberFormatInfo);
					return Token.FLOAT;
				} catch (Exception) {
					error_details = String.Format("Can't parse float {0}", digit);

					val = 0f;
					return Token.FLOAT;
				}
			}
			if (isdecimal) {
				try {
					NumberFormatInfo numberFormatInfo = new NumberFormatInfo();
					numberFormatInfo.CurrencyDecimalSeparator = ".";
					val = Decimal.Parse(digit, numberFormatInfo);
					return Token.DECIMAL;
				} catch (Exception) {
					error_details = String.Format("Can't parse decimal {0}", digit);
					val = 0m;
					return Token.DECIMAL;
				}
			}
			if (isdouble) {
				try {
					NumberFormatInfo numberFormatInfo = new NumberFormatInfo();
					numberFormatInfo.CurrencyDecimalSeparator = ".";
					val = Double.Parse(digit, numberFormatInfo);
					return Token.DOUBLE;
				} catch (Exception) {
					error_details = String.Format("Can't parse double {0}", digit);
					val = 0d;
					return Token.DOUBLE;
				}
			}
			
			double d = 0;
			// FIXME: http://bugzilla.ximian.com/show_bug.cgi?id=72221
			if (!Double.TryParse(digit, ishex ? NumberStyles.HexNumber : NumberStyles.Integer, null, out d)) {
				error_details = String.Format("Can't parse integral constant {0}", digit);
				val = 0;
				return Token.INT;
			}
			if (d < long.MinValue || d > long.MaxValue) {
				islong = true;
				isunsigned = true;	
			}
			else if (d < uint.MinValue || d > uint.MaxValue) {
				islong = true;	
			}
			else if (d < int.MinValue || d > int.MaxValue) {
				isunsigned = true;	
			}
			if (islong) {
				if (isunsigned) {
					try {
						val = UInt64.Parse(digit, ishex ? NumberStyles.HexNumber : NumberStyles.Number);
						return Token.ULONG;
					} catch (Exception) {
						error_details = String.Format("Can't parse unsigned long {0}", digit);
						val = 0UL;
						return Token.ULONG;
					}
				} else {
					try {
						val = Int64.Parse(digit, ishex ? NumberStyles.HexNumber : NumberStyles.Number);
						return Token.LONG;
					} catch (Exception) {
						error_details = String.Format("Can't parse long {0}", digit);
						val = 0L;
						return Token.LONG;
					}
				}
			} else {
				if (isunsigned) {
					try {
						val = UInt32.Parse(digit, ishex ? NumberStyles.HexNumber : NumberStyles.Number);
						return Token.UINT;
					} catch (Exception) {
						error_details = String.Format("Can't parse unsigned int {0}", digit);
						val = 0U;
						return Token.UINT;
					}
				} else {
					try {
					 	val = Int32.Parse(digit, ishex ? NumberStyles.HexNumber : NumberStyles.Number);
						return Token.INT;
					} catch (Exception) {
						error_details = String.Format("Can't parse int {0}", digit);
						val = 0;
						return Token.INT;
					}
				}
			}
		}
#endregion

		bool is_hex (int e)
		{
			return (e >= '0' && e <= '9') || (e >= 'A' && e <= 'F') || (e >= 'a' && e <= 'f');
		}

		private int consume_identifier (int c, bool quoted) 
		{
			bool old_tokens_seen = tokens_seen;
			tokens_seen = true;

			id_builder.Length = 0;

			id_builder.Append ((char) c);
					
			while ((c = PeekChar ()) != -1) {
				if (Char.IsLetterOrDigit ((char)c) || c == '_') {
					id_builder.Append ((char)GetChar ());
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
								
			while ((c = GetChar ()) != -1){
				if (c == '"'){
					if (quoted && PeekChar () == '"'){
						string_builder.Append ((char) c);
						GetChar ();
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
								
			while ((c = GetChar ()) != -1){
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

		public int xtoken ()
		{
			int c;

			val = null;
			// optimization: eliminate col and implement #directive semantic correctly.
			for (;(c = GetChar ()) != -1; col++) {

				if (c == 0)
					continue;

				if (Char.IsLetter ((char)c) || c == '_')
					return consume_identifier (c, false);

				if (c == '\'')
					return consume_quoted_identifier ();

				if (c == '"')
					return consume_string (false);

				if (Char.IsDigit ((char)c)) {
					tokens_seen = true;
					return ReadDigit ((char)c);
				}

				if (c == '#')
					return Token.HASH;
				else if (c == '@')
					return Token.AT;
				else if (c == '$')
					return Token.DOLLAR;
				else if (c == '.') {
					if (Char.IsDigit ((char)PeekChar())) {
						putback(c);
						col -=2;
						return ReadDigit ('0');
					}

					if ((c = PeekChar ()) == '.') {
						GetChar ();
						return Token.DOTDOT;
					}

					return Token.DOT;
				}
				else if (c == '!') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.NOTEQUAL;
					}

					return Token.NOT;
				}
				else if (c == '=') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.EQUAL;
					}

					return Token.ASSIGN;
				}
				else if (c == '*') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.STARASSIGN;
					}

					return Token.STAR;
				}
				else if (c == '+') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.PLUSASSIGN;
					}

					return Token.PLUS;
				}
				else if (c == '-') { // FIXME: negative numbers...
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.MINUSASSIGN;
					}
					if ((c = PeekChar ()) == '>') {
						GetChar ();
						return Token.ARROW;
					}

					return Token.MINUS;
				}
				else if (c == '/') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.DIVASSIGN;
					}

					return Token.DIV;
				}
				else if (c == '%') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.PERCENTASSIGN;
					}

					return Token.PERCENT;
				}
				else if (c == '|') {
					if ((c = PeekChar ()) == '|') {
						GetChar ();
						return Token.OROR;
					}

					return Token.OR;
				}
				else if (c == '&') {
					if ((c = PeekChar ()) == '&') {
						GetChar ();
						return Token.ANDAND;
					}

					return Token.AMPERSAND;
				}
				else if (c == '(')
					return Token.OPAREN;
				else if (c == ')')
					return Token.CPAREN;
				else if (c == '[')
					return Token.OBRACKET;
				else if (c == ']')
					return Token.CBRACKET;
				else if (c == ',')
					return Token.COMMA;
				else if (c == '<') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.LE;
					}

					if ((c = PeekChar ()) == '<') {
						GetChar ();

						if (PeekChar () == '=') {
							GetChar();
							return Token.LEFTSHIFTASSIGN;
						}
						else
							return Token.LEFTSHIFT;
					}

					return Token.LT;
				}
				else if (c == '>') {
					if ((c = PeekChar ()) == '=') {
						GetChar ();
						return Token.GE;
					}

					if ((c = PeekChar ()) == '>') {
						GetChar ();

						if (PeekChar () == '=') {
							GetChar();
							return Token.RIGHTSHIFTASSIGN;
						}
						else
							return Token.RIGHTSHIFT;
					}

					return Token.GT;
				}
				else if (c == ':')
					return Token.COLON;
				else if (c == '?')
					return Token.QUESTION;

				if (c == ' ' || c == '\t' || c == '\f' || c == '\v' || c == '\r' || c == '\n'){
					if (current_token == Token.HASH) {
						error_details = "No whitespace allowed after `#'";
						return Token.ERROR;
					}
					
					if (current_token == Token.AT) {
						error_details = "No whitespace allowed after `@'";
						return Token.ERROR;
					}

					if (c == '\t')
						col = (((col + 8) / 8) * 8) - 1;
					continue;
				}


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
		public string Location {
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
