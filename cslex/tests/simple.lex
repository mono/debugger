using System;
using System.Text;

class Simple
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

class Util
{
public static void IllChar(String s)
 {
 StringBuilder sb = new StringBuilder("Illegal character: <");
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
 }
} 

class Yytoken
  {
  public int m_index;
  public String m_text;
  public int m_line;
  public int m_charBegin;
  public int m_charEnd;

internal Yytoken(int index, String text, int line, int charBegin, int charEnd)
  {
  m_index = index;
  m_text = text;
  m_line = line;
  m_charBegin = charBegin;
  m_charEnd = charEnd;
  }

public override String ToString()
  {
  return "Token #"+ m_index + ": " + m_text
    + " (line "+ m_line + ")";
  }
}

%%
%line
%char
%state COMMENT
NONNEWLINE_WHITE_SPACE_CHAR=[\ \t\b\012]
COMMENT_TEXT=([^/*$]|[^*$]"/"[^*$]|[^/$]"*"[^/$]|"*"[^/$]|"/"[^*$])*
%% 

<YYINITIAL> ((\r\n)|\n) { /* this is newline */
Console.WriteLine("Parsed Newline.");
return null;
}

<YYINITIAL> {NONNEWLINE_WHITE_SPACE_CHAR}+ { /* this is whitespace */
Console.WriteLine("Parsed Whitespace = ["+yytext()+"]");
return null;
}

<YYINITIAL> "*" { /* this is the '*' char */
return (new Yytoken(2,yytext(),yyline,yychar,yychar+1));
}

<YYINITIAL> "/" { /* this is the '/' char */
return (new Yytoken(1,yytext(),yyline,yychar,yychar+1));
}

<YYINITIAL> "/*" { /* comment begin (initial) */ yybegin(COMMENT);
Console.WriteLine("Comment_Begin = ["+yytext()+"]");
return null;
}

<COMMENT> {COMMENT_TEXT} {
Console.WriteLine("Comment_Text = ["+yytext()+"]");
 /* comment text here */
return null;
}

<YYINITIAL> "/*" {
  /* comment begin (initial) */
  yybegin(COMMENT);
  return null;
  }

<COMMENT> "/*" {
  /* comment begin (non-initial) */
  return null;
  }

<COMMENT> "*/" {
  /* comment end */
  Console.WriteLine("Comment_End = ["+yytext()+"]");
  yybegin(YYINITIAL);
  return null;
  }

<YYINITIAL> . {
  Util.IllChar(yytext());
  return null;
  }
