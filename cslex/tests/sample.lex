using System;
using System.Text;

class Sample
{
public static void Main(String[] argv)
  {
  String [] args = Environment.GetCommandLineArgs();
  System.IO.FileStream f = new System.IO.FileStream(args[1], System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 8192);
  Yylex yy = new Yylex(f);
  Yytoken t;
  while ((t = yy.yylex()) != null)
    Console.WriteLine(t);
  }
}

class Utility {
  public static void assert
   (
   bool expr
   )
    { 
    if (false == expr)
      throw new ApplicationException("Error: Assertion failed.");
    }
  
  private static String[] errorMsg = new String[]
    {
    "Error: Unmatched end-of-comment punctuation.",
    "Error: Unmatched start-of-comment punctuation.",
    "Error: Unclosed string.",
    "Error: Illegal character."
    };
  
  public const int E_ENDCOMMENT = 0; 
  public const int E_STARTCOMMENT = 1; 
  public const int E_UNCLOSEDSTR = 2; 
  public const int E_UNMATCHED = 3; 

  public static void error
    (
    int code
    )
    {
    Console.WriteLine(errorMsg[code]);
    }
  }

class Yytoken  {
  internal Yytoken
    (
    int index,
    String text,
    int line,
    int charBegin,
    int charEnd
    )
    {
    m_index = index;
    m_text = text;
    m_line = line;
    m_charBegin = charBegin;
    m_charEnd = charEnd;
    }

  public int m_index;
  public String m_text;
  public int m_line;
  public int m_charBegin;
  public int m_charEnd;
  public override String ToString() {
    return "Token #"+ m_index + ": " + m_text  + " (line "+ m_line + ")";
    }
  }

%%

%{
private static int comment_count = 0;
%} 
%line
%char
%state COMMENT

ALPHA=[A-Za-z]
DIGIT=[0-9]
NEWLINE=((\r\n)|\n)
NONNEWLINE_WHITE_SPACE_CHAR=[\ \t\b\012]
WHITE_SPACE_CHAR=[{NEWLINE}\ \t\b\012]
STRING_TEXT=(\\\"|[^{NEWLINE}\"]|\\{WHITE_SPACE_CHAR}+\\)*
COMMENT_TEXT=([^*/\r\n]|[^*\r\n]"/"[^*\r\n]|[^/\r\n]"*"[^/\r\n]|"*"[^/\r\n]|"/"[^*\r\n])*


%% 

<YYINITIAL> "," { return (new Yytoken(0,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> ":" { return (new Yytoken(1,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> ";" { return (new Yytoken(2,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "(" { return (new Yytoken(3,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> ")" { return (new Yytoken(4,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "[" { return (new Yytoken(5,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "]" { return (new Yytoken(6,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "{" { return (new Yytoken(7,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "}" { return (new Yytoken(8,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "." { return (new Yytoken(9,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "+" { return (new Yytoken(10,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "-" { return (new Yytoken(11,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "*" { return (new Yytoken(12,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "/" { return (new Yytoken(13,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "=" { return (new Yytoken(14,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "<>" { return (new Yytoken(15,yytext(),yyline,yychar,yychar+2)); }
<YYINITIAL> "<"  { return (new Yytoken(16,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "<=" { return (new Yytoken(17,yytext(),yyline,yychar,yychar+2)); }
<YYINITIAL> ">"  { return (new Yytoken(18,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> ">=" { return (new Yytoken(19,yytext(),yyline,yychar,yychar+2)); }
<YYINITIAL> "&"  { return (new Yytoken(20,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> "|"  { return (new Yytoken(21,yytext(),yyline,yychar,yychar+1)); }
<YYINITIAL> ":=" { return (new Yytoken(22,yytext(),yyline,yychar,yychar+2)); }

<YYINITIAL> {NONNEWLINE_WHITE_SPACE_CHAR}+ { return null; }

<YYINITIAL,COMMENT> [(\r\n?|\n)] { return null; }

<YYINITIAL> "/*" { yybegin(COMMENT); comment_count = comment_count + 1; return null;
}

<COMMENT> "/*" { comment_count = comment_count + 1; return null;
}
<COMMENT> "*/" { 
	comment_count = comment_count - 1; 
	Utility.assert(comment_count >= 0);
	if (comment_count == 0) {
    		yybegin(YYINITIAL);
		}
        return null;
}

<COMMENT> {COMMENT_TEXT} { return null; }

<YYINITIAL> \"{STRING_TEXT}\" {
	String str =  yytext().Substring(1,yytext().Length - 1);
	
	Utility.assert(str.Length == yytext().Length - 2);
	return (new Yytoken(40,str,yyline,yychar,yychar + str.Length));
}
<YYINITIAL> \"{STRING_TEXT} {
	String str =  yytext().Substring(1,yytext().Length);

	Utility.error(Utility.E_UNCLOSEDSTR);
	Utility.assert(str.Length == yytext().Length - 1);
	return (new Yytoken(41,str,yyline,yychar,yychar + str.Length));
} 
<YYINITIAL> {DIGIT}+ { 
	return (new Yytoken(42,yytext(),yyline,yychar,yychar + yytext().Length));
}	
<YYINITIAL> {ALPHA}({ALPHA}|{DIGIT}|_)* {
	return (new Yytoken(43,yytext(),yyline,yychar,yychar + yytext().Length));
}	
<YYINITIAL,COMMENT> . {
	StringBuilder sb = new StringBuilder("Illegal character: <");
	String s = yytext();
	for (int i = 0; i < s.Length; i++)
	  if (s[i] >= 32)
	    sb.Append(s[i]);
	  else
	    {
	    sb.Append("^");
	    sb.Append(Convert.ToChar(s[i]+'A'-1));
	    }
        sb.Append(">");
	Console.WriteLine(sb.ToString());	
	Utility.error(Utility.E_UNMATCHED);
        return null;
}







