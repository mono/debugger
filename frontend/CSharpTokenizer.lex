using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using Mono.Debugger;

public class Yytoken  {
	public readonly int Index;
	public readonly string Text;
	public readonly object Value;
	public readonly int Line;
	public readonly int CharBegin;
	public readonly int CharEnd;

	internal Yytoken (int index)
	{
		Index = index;
	}

	internal Yytoken (int index, string text, int line, int charBegin, int charEnd)
	{
		Index = index;
		Text = text;
		Line = line;
		CharBegin = charBegin;
		CharEnd = charEnd;
	}

	internal Yytoken (int index, string text, object value, int line, int charBegin, int charEnd)
	{
		Index = index;
		Text = text;
		Value = value;
		Line = line;
		CharBegin = charBegin;
		CharEnd = charEnd;
	}

	public override String ToString()
	{
		return "Token #"+ Index + ": " + Text  + " (line "+ Line + ")";
	}
}

%%

%public
%namespace Mono.Debugger.Frontend.CSharp
%class Tokenizer
%implements yyParser.yyInput

%eofval{
	return new Yytoken (Token.EOF);
%eofval}

%{
	string name;

	public string Name {
	  set { name = value; }
	  get { return name; }
	}

	Yytoken current_token;

	public bool advance () {
		current_token = yylex();
		return current_token.Index != Token.EOF;
	}

	public int token () {
		return current_token.Index;
	}

	public Object value () {
		return current_token.Value;
	}

	public void restart () {
		yyline = 0;
		yychar = 1;
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

#if false
				if (current_token == Token.ERROR)
					det = "detail: " + error_details;
				else
#endif
					det = "";
				
				// return "Line:     "+line+" Col: "+col + "\n" +
				//       "VirtLine: "+ref_line +
				//       " Token: "+current_token + " " + det;
				string current_token_name = TokenValueName [current_token.Index] as string;
				if (current_token_name == null)
					current_token_name = current_token.ToString ();

				return String.Format ("{0}, Token: {1} {2}", name,
						      current_token_name, det);
			}
		}


%}

%line
%char

DIGIT=[0-9]
ALPHA=[A-Za-z]
HEXDIGIT=[A-Fa-f0-9]
IDENTIFIER=[A-Za-z_][A-Za-z0-9_]*
NEWLINE=((\r\n)|\n)
NONNEWLINE_WHITE_SPACE_CHAR=[\ \t\b\012]
WHITE_SPACE_CHAR=[{NEWLINE}\ \t\b\012]
STRING_TEXT=(\\\"|[^{NEWLINE}\"]|\\{WHITE_SPACE_CHAR}+\\)*

%%

<YYINITIAL> {DIGIT}+ { return new Yytoken (Token.INTEGER, yytext(), (int) UInt32.Parse (yytext()), yyline, yychar, yychar+1); }
<YYINITIAL> {DIGIT}*\.{DIGIT}+ { return new Yytoken (Token.FLOAT, yytext(), Single.Parse (yytext()), yyline, yychar, yychar+1); }
<YYINITIAL> 0x{HEXDIGIT}+ { return new Yytoken (Token.NUMBER, yytext(), (long) UInt64.Parse (yytext(), NumberStyles.HexNumber), yyline,yychar, yychar+yytext().Length);}

<YYINITIAL> "."  { return new Yytoken (Token.DOT, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> ".." { return new Yytoken (Token.DOTDOT, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "#"  { return new Yytoken (Token.HASH, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "@"  { return new Yytoken (Token.AT, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "%"  { return new Yytoken (Token.PERCENT, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "$"  { return new Yytoken (Token.DOLLAR, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "&"  { return new Yytoken (Token.AMPERSAND, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "?"  { return new Yytoken (Token.QUESTION, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> ":"  { return new Yytoken (Token.COLON, yytext(), yyline, yychar, yychar+1); }

<YYINITIAL> "!" { return new Yytoken (Token.NOT, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "=" { return new Yytoken (Token.EQUAL, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "*" { return new Yytoken (Token.STAR, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "+" { return new Yytoken (Token.PLUS, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "-" { return new Yytoken (Token.MINUS, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "/" { return new Yytoken (Token.DIV, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "(" { return new Yytoken (Token.OPAREN, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> ")" { return new Yytoken (Token.CPAREN, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "[" { return new Yytoken (Token.OBRACKET, yytext(), yyline, yychar, yychar+1); }
<YYINITIAL> "]" { return new Yytoken (Token.CBRACKET, yytext(), yyline, yychar, yychar+1); }

<YYINITIAL> "->" { return new Yytoken (Token.ARROW, yytext(), yyline, yychar, yychar + 2); }

<YYINITIAL> "==" { return new Yytoken (Token.EQUALEQUAL, yytext(), yyline, yychar, yychar + 2); }
<YYINITIAL> "!=" { return new Yytoken (Token.NOTEQUAL, yytext(), yyline, yychar, yychar + 2); }
<YYINITIAL> "<"  { return new Yytoken (Token.LT, yytext(), yyline, yychar, yychar + 1); }
<YYINITIAL> ">"  { return new Yytoken (Token.GT, yytext(), yyline, yychar, yychar + 1); }
<YYINITIAL> "<=" { return new Yytoken (Token.LE, yytext(), yyline, yychar, yychar + 2); }
<YYINITIAL> ">=" { return new Yytoken (Token.GE, yytext(), yyline, yychar, yychar + 2); }
<YYINITIAL> "||" { return new Yytoken (Token.OR, yytext(), yyline, yychar, yychar + 2); }
<YYINITIAL> "&&" { return new Yytoken (Token.AND, yytext(), yyline, yychar, yychar + 2); }

<YYINITIAL> "new"   { return new Yytoken (Token.NEW, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "this"  { return new Yytoken (Token.THIS, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "base"  { return new Yytoken (Token.BASE, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "catch" { return new Yytoken (Token.CATCH, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "true"  { return new Yytoken (Token.TRUE, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "false" { return new Yytoken (Token.FALSE, yytext(), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> "null"  { return new Yytoken (Token.NULL, yytext(), yyline, yychar, yychar+yytext().Length); }

<YYINITIAL> \"{STRING_TEXT}\" { return new Yytoken (Token.STRING, yytext(), yytext().Substring(1, yytext().Length - 1), yyline, yychar, yychar+yytext().Length); }
<YYINITIAL> {IDENTIFIER}      { return new Yytoken (Token.IDENTIFIER, yytext(), yytext(), yyline, yychar, yychar+yytext().Length);}

<YYINITIAL> {WHITE_SPACE_CHAR}+ { return null; }

<YYINITIAL> . { Console.WriteLine ("illegal character: <{0}>", yytext()); return null; }
