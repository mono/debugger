namespace YyNameSpace
{
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
/* test */


internal class Yylex
{
private const int YY_BUFFER_SIZE = 512;
private const int YY_F = -1;
private const int YY_NO_STATE = -1;
private const int YY_NOT_ACCEPT = 0;
private const int YY_START = 1;
private const int YY_END = 2;
private const int YY_NO_ANCHOR = 4;
delegate Yytoken AcceptMethod();
AcceptMethod[] accept_dispatch;
private const int YY_BOL = 128;
private const int YY_EOF = 129;
private System.IO.TextReader yy_reader;
private int yy_buffer_index;
private int yy_buffer_read;
private int yy_buffer_start;
private int yy_buffer_end;
private char[] yy_buffer;
private int yychar;
private int yyline;
private bool yy_at_bol;
private int yy_lexical_state;

internal Yylex(System.IO.TextReader reader) : this()
  {
  if (null == reader)
    {
    throw new System.ApplicationException("Error: Bad input stream initializer.");
    }
  yy_reader = reader;
  }

internal Yylex(System.IO.FileStream instream) : this()
  {
  if (null == instream)
    {
    throw new System.ApplicationException("Error: Bad input stream initializer.");
    }
  yy_reader = new System.IO.StreamReader(instream);
  }

private Yylex()
  {
  yy_buffer = new char[YY_BUFFER_SIZE];
  yy_buffer_read = 0;
  yy_buffer_index = 0;
  yy_buffer_start = 0;
  yy_buffer_end = 0;
  yychar = 0;
  yyline = 0;
  yy_at_bol = true;
  yy_lexical_state = YYINITIAL;
accept_dispatch = new AcceptMethod[] 
 {
  null,
  null,
  new AcceptMethod(this.Accept_2),
  new AcceptMethod(this.Accept_3),
  new AcceptMethod(this.Accept_4),
  new AcceptMethod(this.Accept_5),
  new AcceptMethod(this.Accept_6),
  new AcceptMethod(this.Accept_7),
  new AcceptMethod(this.Accept_8),
  new AcceptMethod(this.Accept_9),
  new AcceptMethod(this.Accept_10),
  null,
  new AcceptMethod(this.Accept_12),
  new AcceptMethod(this.Accept_13),
  null,
  new AcceptMethod(this.Accept_15),
  null,
  new AcceptMethod(this.Accept_17),
  null,
  null,
  };
  }

Yytoken Accept_2()
    { // begin accept action #2
{ /* this is newline */
Console.WriteLine("Parsed Newline.");
return null;
}
    } // end accept action #2

Yytoken Accept_3()
    { // begin accept action #3
{ /* this is whitespace */
Console.WriteLine("Parsed Whitespace = ["+yytext()+"]");
return null;
}
    } // end accept action #3

Yytoken Accept_4()
    { // begin accept action #4
{ /* this is the '*' char */
return (new Yytoken(2,yytext(),yyline,yychar,yychar+1));
}
    } // end accept action #4

Yytoken Accept_5()
    { // begin accept action #5
{ /* this is the '/' char */
return (new Yytoken(1,yytext(),yyline,yychar,yychar+1));
}
    } // end accept action #5

Yytoken Accept_6()
    { // begin accept action #6
{
  Util.IllChar(yytext());
  return null;
  }
    } // end accept action #6

Yytoken Accept_7()
    { // begin accept action #7
{ /* comment begin (initial) */ yybegin(COMMENT);
Console.WriteLine("Comment_Begin = ["+yytext()+"]");
return null;
}
    } // end accept action #7

Yytoken Accept_8()
    { // begin accept action #8
{
Console.WriteLine("Comment_Text = ["+yytext()+"]");
 /* comment text here */
return null;
}
    } // end accept action #8

Yytoken Accept_9()
    { // begin accept action #9
{
  /* comment end */
  Console.WriteLine("Comment_End = ["+yytext()+"]");
  yybegin(YYINITIAL);
  return null;
  }
    } // end accept action #9

Yytoken Accept_10()
    { // begin accept action #10
{
  /* comment begin (non-initial) */
  return null;
  }
    } // end accept action #10

Yytoken Accept_12()
    { // begin accept action #12
{ /* this is newline */
Console.WriteLine("Parsed Newline.");
return null;
}
    } // end accept action #12

Yytoken Accept_13()
    { // begin accept action #13
{
Console.WriteLine("Comment_Text = ["+yytext()+"]");
 /* comment text here */
return null;
}
    } // end accept action #13

Yytoken Accept_15()
    { // begin accept action #15
{
Console.WriteLine("Comment_Text = ["+yytext()+"]");
 /* comment text here */
return null;
}
    } // end accept action #15

Yytoken Accept_17()
    { // begin accept action #17
{
Console.WriteLine("Comment_Text = ["+yytext()+"]");
 /* comment text here */
return null;
}
    } // end accept action #17

private const int COMMENT = 1;
private const int YYINITIAL = 0;
private static int[] yy_state_dtrans = new int[] 
  {   0,
  8
  };
private void yybegin (int state)
  {
  yy_lexical_state = state;
  }

private char yy_advance ()
  {
  int next_read;
  int i;
  int j;

  if (yy_buffer_index < yy_buffer_read)
    {
    return yy_buffer[yy_buffer_index++];
    }

  if (0 != yy_buffer_start)
    {
    i = yy_buffer_start;
    j = 0;
    while (i < yy_buffer_read)
      {
      yy_buffer[j] = yy_buffer[i];
      i++;
      j++;
      }
    yy_buffer_end = yy_buffer_end - yy_buffer_start;
    yy_buffer_start = 0;
    yy_buffer_read = j;
    yy_buffer_index = j;
    next_read = yy_reader.Read(yy_buffer,yy_buffer_read,
                  yy_buffer.Length - yy_buffer_read);
    if (next_read <= 0)
      {
      return (char) YY_EOF;
      }
    yy_buffer_read = yy_buffer_read + next_read;
    }
  while (yy_buffer_index >= yy_buffer_read)
    {
    if (yy_buffer_index >= yy_buffer.Length)
      {
      yy_buffer = yy_double(yy_buffer);
      }
    next_read = yy_reader.Read(yy_buffer,yy_buffer_read,
                  yy_buffer.Length - yy_buffer_read);
    if (next_read <= 0)
      {
      return (char) YY_EOF;
      }
    yy_buffer_read = yy_buffer_read + next_read;
    }
  return yy_buffer[yy_buffer_index++];
  }
private void yy_move_end ()
  {
  if (yy_buffer_end > yy_buffer_start && 
      '\n' == yy_buffer[yy_buffer_end-1])
    yy_buffer_end--;
  if (yy_buffer_end > yy_buffer_start &&
      '\r' == yy_buffer[yy_buffer_end-1])
    yy_buffer_end--;
  }
private bool yy_last_was_cr=false;
private void yy_mark_start ()
  {
  int i;
  for (i = yy_buffer_start; i < yy_buffer_index; i++)
    {
    if (yy_buffer[i] == '\n' && !yy_last_was_cr)
      {
      yyline++;
      }
    if (yy_buffer[i] == '\r')
      {
      yyline++;
      yy_last_was_cr=true;
      }
    else
      {
      yy_last_was_cr=false;
      }
    }
  yychar = yychar + yy_buffer_index - yy_buffer_start;
  yy_buffer_start = yy_buffer_index;
  }
private void yy_mark_end ()
  {
  yy_buffer_end = yy_buffer_index;
  }
private void yy_to_mark ()
  {
  yy_buffer_index = yy_buffer_end;
  yy_at_bol = (yy_buffer_end > yy_buffer_start) &&
    (yy_buffer[yy_buffer_end-1] == '\r' ||
    yy_buffer[yy_buffer_end-1] == '\n');
  }
internal string yytext()
  {
  return (new string(yy_buffer,
                yy_buffer_start,
                yy_buffer_end - yy_buffer_start)
         );
  }
private int yylength ()
  {
  return yy_buffer_end - yy_buffer_start;
  }
private char[] yy_double (char[] buf)
  {
  int i;
  char[] newbuf;
  newbuf = new char[2*buf.Length];
  for (i = 0; i < buf.Length; i++)
    {
    newbuf[i] = buf[i];
    }
  return newbuf;
  }
private const int YY_E_INTERNAL = 0;
private const int YY_E_MATCH = 1;
private static string[] yy_error_string = new string[]
  {
  "Error: Internal error.\n",
  "Error: Unmatched input.\n"
  };
private void yy_error (int code,bool fatal)
  {
  System.Console.Write(yy_error_string[code]);
  if (fatal)
    {
    throw new System.ApplicationException("Fatal Error.\n");
    }
  }
private static int[] yy_acpt = new int[]
  {
  /* 0 */   YY_NOT_ACCEPT,
  /* 1 */   YY_NO_ANCHOR,
  /* 2 */   YY_NO_ANCHOR,
  /* 3 */   YY_NO_ANCHOR,
  /* 4 */   YY_NO_ANCHOR,
  /* 5 */   YY_NO_ANCHOR,
  /* 6 */   YY_NO_ANCHOR,
  /* 7 */   YY_NO_ANCHOR,
  /* 8 */   YY_NO_ANCHOR,
  /* 9 */   YY_NO_ANCHOR,
  /* 10 */   YY_NO_ANCHOR,
  /* 11 */   YY_NOT_ACCEPT,
  /* 12 */   YY_NO_ANCHOR,
  /* 13 */   YY_NO_ANCHOR,
  /* 14 */   YY_NOT_ACCEPT,
  /* 15 */   YY_NO_ANCHOR,
  /* 16 */   YY_NOT_ACCEPT,
  /* 17 */   YY_NO_ANCHOR,
  /* 18 */   YY_NOT_ACCEPT,
  /* 19 */   YY_NOT_ACCEPT
  };
private static int[] yy_cmap = new int[]
  {
  6, 6, 6, 6, 6, 6, 6, 6,
  3, 3, 2, 6, 6, 1, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  3, 6, 6, 6, 7, 6, 6, 6,
  6, 6, 4, 6, 6, 6, 6, 5,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  6, 6, 6, 6, 6, 6, 6, 6,
  0, 0 
  };
private static int[] yy_rmap = new int[]
  {
  0, 1, 2, 2, 1, 3, 1, 1,
  4, 1, 1, 5, 1, 6, 7, 8,
  9, 10, 11, 12 
  };
private static int[,] yy_nxt = new int[,]
  {
  { 1, 11, 2, 3, 4, 5, 6, 6 },
  { -1, -1, -1, -1, -1, -1, -1, -1 },
  { -1, -1, 3, 3, -1, -1, -1, -1 },
  { -1, -1, -1, -1, 7, -1, -1, -1 },
  { 1, 13, 13, 13, 14, 16, 13, -1 },
  { -1, -1, 12, -1, -1, -1, -1, -1 },
  { -1, 13, 13, 13, 18, 19, 13, -1 },
  { -1, 13, 13, 13, 15, 9, 13, -1 },
  { -1, 13, 13, 13, 15, 19, 13, -1 },
  { -1, 13, 13, 13, 10, 17, 13, -1 },
  { -1, 13, 13, 13, 18, 17, 13, -1 },
  { -1, 13, 13, 13, 15, -1, 13, -1 },
  { -1, 13, 13, 13, -1, 17, 13, -1 }
  };
public Yytoken yylex()
  {
  char yy_lookahead;
  int yy_anchor = YY_NO_ANCHOR;
  int yy_state = yy_state_dtrans[yy_lexical_state];
  int yy_next_state = YY_NO_STATE;
  int yy_last_accept_state = YY_NO_STATE;
  bool yy_initial = true;
  int yy_this_accept;

  yy_mark_start();
  yy_this_accept = yy_acpt[yy_state];
  if (YY_NOT_ACCEPT != yy_this_accept)
    {
    yy_last_accept_state = yy_state;
    yy_mark_end();
    }
  while (true)
    {
    if (yy_initial && yy_at_bol)
      yy_lookahead = (char) YY_BOL;
    else
      {
      yy_lookahead = yy_advance();
      }
    yy_next_state = yy_nxt[yy_rmap[yy_state],yy_cmap[yy_lookahead]];
    if (YY_EOF == yy_lookahead && yy_initial)
      {
        return null;
      }
    if (YY_F != yy_next_state)
      {
      yy_state = yy_next_state;
      yy_initial = false;
      yy_this_accept = yy_acpt[yy_state];
      if (YY_NOT_ACCEPT != yy_this_accept)
        {
        yy_last_accept_state = yy_state;
        yy_mark_end();
        }
      }
    else
      {
      if (YY_NO_STATE == yy_last_accept_state)
        {
        throw new System.ApplicationException("Lexical Error: Unmatched Input.");
        }
      else
        {
        yy_anchor = yy_acpt[yy_last_accept_state];
        if (0 != (YY_END & yy_anchor))
          {
          yy_move_end();
          }
        yy_to_mark();
        if (yy_last_accept_state < 0)
          {
          if (yy_last_accept_state < 20)
            yy_error(YY_E_INTERNAL, false);
          }
        else
          {
          AcceptMethod m = accept_dispatch[yy_last_accept_state];
          if (m != null)
            {
            Yytoken tmp = m();
            if (tmp != null)
              return tmp;
            }
          }
        yy_initial = true;
        yy_state = yy_state_dtrans[yy_lexical_state];
        yy_next_state = YY_NO_STATE;
        yy_last_accept_state = YY_NO_STATE;
        yy_mark_start();
        yy_this_accept = yy_acpt[yy_state];
        if (YY_NOT_ACCEPT != yy_this_accept)
          {
          yy_last_accept_state = yy_state;
          yy_mark_end();
          }
        }
      }
    }
  }
}

}
