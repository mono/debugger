namespace Lex
{
/*
 * Class: Emit
 */
using System;
using System.Text;
using System.IO;
using System.Collections;

public class Emit
{
/*
 * Member Variables
 */
private Spec spec;
private StreamWriter outstream;

/*
 * Constants: Anchor Types
 */
private const int START = 1;
private const int END = 2;
private const int NONE = 4;

/*
 * Constants
 */
private const bool EDBG = true;
private const bool NOT_EDBG = false;

/*
 * Function: Emit
 * Description: Constructor.
 */
public Emit()
  {
  reset();
  }

/*
 * Function: reset
 * Description: Clears member variables.
 */
private void reset()
  {
  spec = null;
  outstream = null;
  }

/*
 * Function: set
 * Description: Initializes member variables.
 */
private void set(Spec s, StreamWriter o)
  {
#if DEBUG
  Utility.assert(null != s);
  Utility.assert(null != o);
#endif
  spec = s;
  outstream = o;
  }

/*
 * Function: print_details
 * Description: Debugging output.
 */
private void print_details()
  {
  int i;
  int j;
  int next;
  int state;
  DTrans dtrans;
  Accept accept;
  bool tr;

  System.Console.WriteLine("---------------------- Transition Table ----------------------");
  for (i = 0; i < spec.row_map.Length; ++i)
    {
    System.Console.Write("State " + i);

    accept = (Accept) spec.accept_list[i];
    if (null == accept)
      {
      System.Console.WriteLine(" [nonaccepting]");
      }
    else
      {
      System.Console.WriteLine(" [accepting, line "
			+ accept.line_number
			+ " <"
			+ accept.action
			+ ">]");
      }
    dtrans = (DTrans) spec.dtrans_list[spec.row_map[i]];

    tr = false;
    state = dtrans.GetDTrans(spec.col_map[0]);
    if (DTrans.F != state)
      {
      tr = true;
      System.Console.Write("\tgoto " + state.ToString() + " on [");
      }
    for (j = 1; j < spec.dtrans_ncols; j++)
      {
      next = dtrans.GetDTrans(spec.col_map[j]);
      if (state == next)
	{
	if (DTrans.F != state)
	  {
	  System.Console.Write((char) j);
	  }
	}
      else
	{
	state = next;
	if (tr)
	  {
	  System.Console.WriteLine("]");
	  tr = false;
	  }
	if (DTrans.F != state)
	  {
	  tr = true;
	  System.Console.Write("\tgoto " + state.ToString() +
			" on [" + Char.ToString((char) j));
	  }
	}
      }
    if (tr)
      {
      System.Console.WriteLine("]");
      }
    }
  System.Console.WriteLine("---------------------- Transition Table ----------------------");
  }

/*
 * Function: Write
 * Description: High-level access function to module.
 */
public void Write(Spec spec, StreamWriter o)
  {
  set(spec, o);

#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != o);
#endif

#if OLD_DEBUG
  print_details();
#endif

  Header();
  Construct();
  Helpers();
  Driver();
  Footer();
  
  reset();
  }

/*
 * Function: construct
 * Description: Emits constructor, member variables,
 * and constants.
 */
private void Construct()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif

  /* Constants */
  outstream.Write(
    "private const int YY_BUFFER_SIZE = 512;\n"
    + "private const int YY_F = -1;\n"
    + "private const int YY_NO_STATE = -1;\n"
    + "private const int YY_NOT_ACCEPT = 0;\n"
    + "private const int YY_START = 1;\n"
    + "private const int YY_END = 2;\n"
    + "private const int YY_NO_ANCHOR = 4;\n"
    );

  /* type declarations */
  outstream.Write("delegate "+spec.type_name+" AcceptMethod();\n" +
		  "AcceptMethod[] accept_dispatch;\n"); 

  /*
   * write beg and end line chars
   */
  outstream.Write("private const int YY_BOL = ");
  outstream.Write(spec.BOL);
  outstream.Write(";\n");
  outstream.Write("private const int YY_EOF = ");
  outstream.Write(spec.EOF);
  outstream.Write(";\n");

  if (spec.integer_type || true == spec.yyeof)
    outstream.Write("public const int YYEOF = -1;\n");

  /* User specified class code */
  if (null != spec.class_code)
    {
    outstream.Write(spec.class_code);
    }

  /* Member Variables */
  outstream.Write(
    "private System.IO.TextReader yy_reader;\n"
    + "private int yy_buffer_index;\n"
    + "private int yy_buffer_read;\n"
    + "private int yy_buffer_start;\n"
    + "private int yy_buffer_end;\n"
    + "private char[] yy_buffer;\n");

  if (spec.count_chars)
    outstream.Write("private int yychar;\n");

  if (spec.count_lines)
    outstream.Write("private int yyline;\n");

  outstream.Write("private bool yy_at_bol;\n");
  outstream.Write("private int yy_lexical_state;\n\n");

  /* Function: first constructor (Reader) */
  string spec_access = "internal ";
  if (spec.lex_public)
    spec_access = "public ";
  outstream.Write(
    spec_access + spec.class_name
    + "(System.IO.TextReader reader) : this()\n"
    + "  {\n"
    + "  if (null == reader)\n"
    + "    {\n"
    + "    throw new System.ApplicationException(\"Error: Bad input stream initializer.\");\n"
    + "    }\n"
    + "  yy_reader = reader;\n"
    + "  }\n\n");


  /* Function: second constructor (InputStream) */
  outstream.Write(
    spec_access + spec.class_name
    + "(System.IO.FileStream instream) : this()\n"
    + "  {\n"
    + "  if (null == instream)\n"
    + "    {\n"
    + "    throw new System.ApplicationException(\"Error: Bad input stream initializer.\");\n"
    + "    }\n"
    + "  yy_reader = new System.IO.StreamReader(instream);\n"
    + "  }\n\n");

  /* Function: third, private constructor - only for internal use */
  outstream.Write(
    "private " + spec.class_name + "()\n"
    + "  {\n"
    + "  yy_buffer = new char[YY_BUFFER_SIZE];\n"
    + "  yy_buffer_read = 0;\n"
    + "  yy_buffer_index = 0;\n"
    + "  yy_buffer_start = 0;\n"
    + "  yy_buffer_end = 0;\n");
  if (spec.count_chars)
    outstream.Write("  yychar = 0;\n");

  if (spec.count_lines)
    outstream.Write("  yyline = 0;\n");

  outstream.Write("  yy_at_bol = true;\n");
  outstream.Write("  yy_lexical_state = YYINITIAL;\n");

  
  string methinit = Action_Methods_Init();
  outstream.Write(methinit);

  /* User specified constructor code. */
  if (null != spec.init_code)
    outstream.Write(spec.init_code);

  outstream.Write("  }\n\n");

  string methstr = Action_Methods_Body();
  outstream.Write(methstr);  
  }

/*
 * Function: states
 * Description: Emits constants that serve as lexical states,
 * including YYINITIAL.
 */
private void States()
  {
  foreach (string state in spec.states.Keys)
    {
#if DEBUG
    Utility.assert(null != state);
#endif
    outstream.Write(
      "private const int " + state + " = " + spec.states[state] + ";\n");
    }

  outstream.Write("private static int[] yy_state_dtrans = new int[] \n"
		  + "  { ");
  for (int index = 0; index < spec.state_dtrans.Length; ++index)
    {
    outstream.Write("  " + spec.state_dtrans[index]);
    if (index < spec.state_dtrans.Length - 1)
      outstream.Write(",\n");
    else
      outstream.Write("\n");
    }
  outstream.Write("  };\n");
  }

/*
 * Function: Helpers
 * Description: Emits helper functions, particularly 
 * error handling and input buffering.
 */
private void Helpers()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif

  /* Function: yy_do_eof */
  if (spec.eof_code != null)
    {
    outstream.Write("private bool yy_eof_done = false;\n"
      + "private void yy_do_eof ()\n"
      + "  {\n"
      + "  if (!yy_eof_done)\n"
      + "    {\n"
      + "    " + spec.eof_code + "\n"
      + "    }\n"
      + "  yy_eof_done = true;\n"
      + "  }\n\n");
    }
  States();

  /* Function: yybegin */
  outstream.Write(
    "private void yybegin (int state)\n"
    + "  {\n"
    + "  yy_lexical_state = state;\n"
    + "  }\n\n");

  /* Function: yy_advance */
  outstream.Write(
    "private char yy_advance ()\n"
    + "  {\n"
    + "  int next_read;\n"
    + "  int i;\n"
    + "  int j;\n"
    + "\n"
    + "  if (yy_buffer_index < yy_buffer_read)\n"
    + "    {\n"
    + "    return yy_buffer[yy_buffer_index++];\n"
    + "    }\n"
    + "\n"
    + "  if (0 != yy_buffer_start)\n"
    + "    {\n"
    + "    i = yy_buffer_start;\n"
    + "    j = 0;\n"
    + "    while (i < yy_buffer_read)\n"
    + "      {\n"
    + "      yy_buffer[j] = yy_buffer[i];\n"
    + "      i++;\n"
    + "      j++;\n"
    + "      }\n"
    + "    yy_buffer_end = yy_buffer_end - yy_buffer_start;\n"
    + "    yy_buffer_start = 0;\n"
    + "    yy_buffer_read = j;\n"
    + "    yy_buffer_index = j;\n"
    + "    next_read = yy_reader.Read(yy_buffer,yy_buffer_read,\n"
    + "                  yy_buffer.Length - yy_buffer_read);\n"
    //    + "    if (-1 == next_read)\n"
    + "    if (next_read <= 0)\n"
    + "      {\n"
    + "      return (char) YY_EOF;\n"
    + "      }\n"
    + "    yy_buffer_read = yy_buffer_read + next_read;\n"
    + "    }\n"
    + "  while (yy_buffer_index >= yy_buffer_read)\n"
    + "    {\n"
    + "    if (yy_buffer_index >= yy_buffer.Length)\n"
    + "      {\n"
    + "      yy_buffer = yy_double(yy_buffer);\n"
    + "      }\n"
    + "    next_read = yy_reader.Read(yy_buffer,yy_buffer_read,\n"
    + "                  yy_buffer.Length - yy_buffer_read);\n"
    //    + "    if (-1 == next_read)\n"
    + "    if (next_read <= 0)\n"
    + "      {\n"
    + "      return (char) YY_EOF;\n"
    + "      }\n"
    + "    yy_buffer_read = yy_buffer_read + next_read;\n"
    + "    }\n"
    + "  return yy_buffer[yy_buffer_index++];\n"
    + "  }\n");

  /* Function: yy_move_end */
  outstream.Write(
      "private void yy_move_end ()\n"
    + "  {\n"
    + "  if (yy_buffer_end > yy_buffer_start && \n"
    + "      '\\n' == yy_buffer[yy_buffer_end-1])\n"
    + "    yy_buffer_end--;\n"
    + "  if (yy_buffer_end > yy_buffer_start &&\n"
    + "      '\\r' == yy_buffer[yy_buffer_end-1])\n"
    + "    yy_buffer_end--;\n"
    + "  }\n"
    );

  /* Function: yy_mark_start */
  outstream.Write("private bool yy_last_was_cr=false;\n"
    + "private void yy_mark_start ()\n"
    + "  {\n");
  if (spec.count_lines)
    {
    outstream.Write(
      "  int i;\n"
      + "  for (i = yy_buffer_start; i < yy_buffer_index; i++)\n"
      + "    {\n"
      + "    if (yy_buffer[i] == '\\n' && !yy_last_was_cr)\n"
      + "      {\n"
      + "      yyline++;\n"
      + "      }\n"
      + "    if (yy_buffer[i] == '\\r')\n"
      + "      {\n"
      + "      yyline++;\n"
      + "      yy_last_was_cr=true;\n"
      + "      }\n"
      + "    else\n"
      + "      {\n"
      + "      yy_last_was_cr=false;\n"
      + "      }\n"
      + "    }\n"
      );
    }
  if (spec.count_chars)
    {
    outstream.Write(
      "  yychar = yychar + yy_buffer_index - yy_buffer_start;\n");
    }
  outstream.Write(
    "  yy_buffer_start = yy_buffer_index;\n"
    + "  }\n");

  /* Function: yy_mark_end */
  outstream.Write(
    "private void yy_mark_end ()\n"
    + "  {\n"
    + "  yy_buffer_end = yy_buffer_index;\n"
    + "  }\n");

  /* Function: yy_to_mark */
  outstream.Write(
    "private void yy_to_mark ()\n"
    + "  {\n"
    + "  yy_buffer_index = yy_buffer_end;\n"
    + "  yy_at_bol = (yy_buffer_end > yy_buffer_start) &&\n"
    + "    (yy_buffer[yy_buffer_end-1] == '\\r' ||\n"
    + "    yy_buffer[yy_buffer_end-1] == '\\n');\n"
    + "  }\n");

  /* Function: yytext */
  outstream.Write(
    "internal string yytext()\n"
    + "  {\n"
    + "  return (new string(yy_buffer,\n"
    + "                yy_buffer_start,\n"
    + "                yy_buffer_end - yy_buffer_start)\n"
    + "         );\n"
    + "  }\n");

  /* Function: yylength */
  outstream.Write(
    "private int yylength ()\n"
    + "  {\n"
    + "  return yy_buffer_end - yy_buffer_start;\n"
    + "  }\n");

  /* Function: yy_double */
  outstream.Write(
    "private char[] yy_double (char[] buf)\n"
    + "  {\n"
    + "  int i;\n"
    + "  char[] newbuf;\n"
    + "  newbuf = new char[2*buf.Length];\n"
    + "  for (i = 0; i < buf.Length; i++)\n"
    + "    {\n"
    + "    newbuf[i] = buf[i];\n"
    + "    }\n"
    + "  return newbuf;\n"
    + "  }\n");

  /* Function: yy_error */
  outstream.Write(
    "private const int YY_E_INTERNAL = 0;\n"
    + "private const int YY_E_MATCH = 1;\n"
    + "private static string[] yy_error_string = new string[]\n"
    + "  {\n"
    + "  \"Error: Internal error.\\n\",\n"
    + "  \"Error: Unmatched input.\\n\"\n"
    + "  };\n");

  outstream.Write(
    "private void yy_error (int code,bool fatal)\n"
    + "  {\n"
    + "  System.Console.Write(yy_error_string[code]);\n"
    + "  if (fatal)\n"
    + "    {\n"
    + "    throw new System.ApplicationException(\"Fatal Error.\\n\");\n"
    + "    }\n"
    + "  }\n");

//  /* Function: yy_next */
//  outstream.Write("\tprivate int yy_next (int current,char lookahead) {\n"
//    + "  return yy_nxt[yy_rmap[current],yy_cmap[lookahead]];\n"
//    + "\t}\n");

//  /* Function: yy_accept */
//  outstream.Write("\tprivate int yy_accept (int current) {\n");
//    + "  return yy_acpt[current];\n"
//    + "\t}\n");
  }

/*
 * Function: Header
 * Description: Emits class header.
 */
private void Header()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif

  outstream.Write("\n\n");
  string spec_access = "internal ";
  if (spec.lex_public)
    spec_access = "public ";

  outstream.Write(spec_access + "class " + spec.class_name);
  if (spec.implements_name != null)
    {
    outstream.Write(" : ");
    outstream.Write(spec.implements_name);
    }	  
  outstream.Write("\n{\n");
  }

private void Accept_table()
  {
  int size = spec.accept_list.Count;
  int lastelem = size-1;
  StringBuilder sb = new StringBuilder(Lex.MAXSTR);

  sb.Append("private static int[] yy_acpt = new int[]\n  {\n");
  for (int elem = 0; elem < size; elem++)
    {
    sb.Append("  /* ");
    sb.Append(elem);
    sb.Append(" */ ");
    string s = "  YY_NOT_ACCEPT"; // default to NOT
    Accept accept = (Accept) spec.accept_list[elem];
    if (accept != null)
      {
      bool is_start = ((spec.anchor_array[elem] & Spec.START) != 0);
      bool is_end = ((spec.anchor_array[elem] & Spec.END) != 0);

      if (is_start && is_end)
	s = "  YY_START | YY_END";
      else if (is_start)
	s = "  YY_START";
      else if (is_end)
	s = "  YY_END";
      else
	s = "  YY_NO_ANCHOR";
      }
    sb.Append(s);
    if (elem < lastelem)
      sb.Append(",");
    sb.Append("\n");
    }
  sb.Append("  };\n");
  outstream.Write(sb.ToString());
  }

private void CMap_table()
  {
  //  int size = spec.col_map.Length;
  int size = spec.ccls_map.Length;
  int lastelem = size-1;
  outstream.Write("private static int[] yy_cmap = new int[]\n  {\n  ");
  for (int i = 0; i < size; i++)
    {
    outstream.Write(spec.col_map[spec.ccls_map[i]]);
    if (i < lastelem)
      outstream.Write(",");
    if (((i + 1) % 8) == 0)
      outstream.Write("\n  ");
    else
      outstream.Write(" ");
    }
  if (size%8 != 0)
    outstream.Write("\n  ");
  outstream.Write("};\n");
  }

private void RMap_table()
  {
  int size = spec.row_map.Length;
  int lastelem = size-1;
  outstream.Write("private static int[] yy_rmap = new int[]\n  {\n  ");
  for (int i = 0; i < size; ++i)
    {
    outstream.Write(spec.row_map[i]);
    if (i < lastelem)
      outstream.Write(",");
    if (((i + 1) % 8) == 0)
      outstream.Write("\n  ");
    else
      outstream.Write(" ");
    }
  if (size%8 != 0)
    outstream.Write("\n  ");
  outstream.Write("};\n");
  }

private void YYNXT_table()
  {
  int size = spec.dtrans_list.Count;
  int lastelem = size-1;
  int lastcol = spec.dtrans_ncols-1;
  StringBuilder sb = new StringBuilder(Lex.MAXSTR);
  sb.Append("private static int[,] yy_nxt = new int[,]\n  {\n");
  for (int elem = 0; elem < size; elem++)
    {
    DTrans cdt_list = (DTrans) spec.dtrans_list[elem];
#if DEBUG
    Utility.assert( spec.dtrans_ncols <= cdt_list.GetDTransLength() );
#endif
    sb.Append("  { ");
    for (int i = 0; i < spec.dtrans_ncols; i++)
      {
      sb.Append(cdt_list.GetDTrans(i));
      if (i < lastcol)
	{
	sb.Append(",");
	if (((i + 1) % 8) == 0)
	  sb.Append("\n   ");
	else
	  sb.Append(" ");
	}
      }
    sb.Append(" }");
    if (elem < lastelem)
      sb.Append(",");
    sb.Append("\n");
    }
  sb.Append("  };\n");
  outstream.Write(sb.ToString());
  }

/*
 * Function: Table
 * Description: Emits transition table.
 */
private void Table()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif

  Accept_table();
  CMap_table();
  RMap_table();
  YYNXT_table();
  }

string EOF_Test()
  {
  StringBuilder sb = new StringBuilder(Lex.MAXSTR);
  if (spec.eof_code != null)
    sb.Append("        yy_do_eof();\n");

  if (spec.integer_type)
    sb.Append("        return YYEOF;\n");
  else if (null != spec.eof_value_code) 
    sb.Append(spec.eof_value_code);
  else
    sb.Append("        return null;\n");
  return sb.ToString();
  }

/*
 * Function: Driver
 * Description: 
 */
private void Driver()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif
  Table();

  string begin_str = "";
  string state_str = "";
#if NOT_EDBG
    begin_str = "  System.Console.WriteLine(\"Begin\");\n";
    state_str =
      "  System.Console.WriteLine(\"\\n\\nCurrent state: \" + yy_state);\n"
    + "  System.Console.Write(\"Lookahead input: "
    + "   (\" + yy_lookahead + \")\");\n"
    + "  if (yy_lookahead < 32)\n"
    + "    System.Console.WriteLine("
    + "   \"'^\" + System.Convert.ToChar(yy_lookahead+'A'-1).ToString() + \"'\");\n"
    + "  else if (yy_lookahead > 127)\n"
    + "    System.Console.WriteLine("
    + "   \"'^\" + yy_lookahead + \"'\");\n"
    + "  else\n"
    + "    System.Console.WriteLine("
    + "   \"'\" + yy_lookahead.ToString() + \"'\");\n"
    + "  System.Console.WriteLine(\"State = \"+ yy_state);\n"
    + "  System.Console.WriteLine(\"Accepting status = \"+ yy_this_accept);\n"
      + "  System.Console.WriteLine(\"Last accepting state = \"+ yy_last_accept_state);\n"
      + "  System.Console.WriteLine(\"Next state = \"+ yy_next_state);\n"
;
#endif

  string hdr_str = "";
  if (spec.integer_type)
    hdr_str = "public int " + spec.function_name + "()\n";
  else if (spec.intwrap_type)
    hdr_str = "public Int32 " + spec.function_name + "()\n";
  else
    hdr_str = "public " + spec.type_name
		+ " " + spec.function_name + "()\n";

  outstream.Write(hdr_str
    + "  {\n"
    + "  char yy_lookahead;\n"
    + "  int yy_anchor = YY_NO_ANCHOR;\n"
    + "  int yy_state = yy_state_dtrans[yy_lexical_state];\n"
    + "  int yy_next_state = YY_NO_STATE;\n"
    + "  int yy_last_accept_state = YY_NO_STATE;\n"
    + "  bool yy_initial = true;\n"
    + "  int yy_this_accept;\n"
    + "\n"
    + "  yy_mark_start();\n"
    + "  yy_this_accept = yy_acpt[yy_state];\n"
    + "  if (YY_NOT_ACCEPT != yy_this_accept)\n"
    + "    {\n"
    + "    yy_last_accept_state = yy_state;\n"
    + "    yy_mark_end();\n"
    + "    }\n"
    + begin_str
    + "  while (true)\n"
    + "    {\n"
    + "    if (yy_initial && yy_at_bol)\n"
    + "      yy_lookahead = (char) YY_BOL;\n"
    + "    else\n"
    + "      {\n"
    + "      yy_lookahead = yy_advance();\n"
		  //    + "    yy_next_state = YY_F;\n"
		  //    + "    if (YY_EOF != yy_lookahead)\n"
    + "      }\n"

    + "    yy_next_state = yy_nxt[yy_rmap[yy_state],yy_cmap[yy_lookahead]];\n"
    + state_str

    + "    if (YY_EOF == yy_lookahead && yy_initial)\n"
    + "      {\n"
    + EOF_Test()
    + "      }\n"

    + "    if (YY_F != yy_next_state)\n"
    + "      {\n"
    + "      yy_state = yy_next_state;\n"
    + "      yy_initial = false;\n"
    + "      yy_this_accept = yy_acpt[yy_state];\n"
    + "      if (YY_NOT_ACCEPT != yy_this_accept)\n"
    + "        {\n"
    + "        yy_last_accept_state = yy_state;\n"
    + "        yy_mark_end();\n"
    + "        }\n"
    + "      }\n"
    + "    else\n"
    + "      {\n"
    + "      if (YY_NO_STATE == yy_last_accept_state)\n"
    + "        {\n"
    + "        throw new System.ApplicationException(\"Lexical Error: Unmatched Input.\");\n"
    + "        }\n"
    + "      else\n"
    + "        {\n"
    + "        yy_anchor = yy_acpt[yy_last_accept_state];\n"
    + "        if (0 != (YY_END & yy_anchor))\n"
    + "          {\n"
    + "          yy_move_end();\n"
    + "          }\n"
    + "        yy_to_mark();\n"
    + "        if (yy_last_accept_state < 0)\n"
    + "          {\n"
    + "          if (yy_last_accept_state < " +
		  spec.accept_list.Count
		  + ")\n"
    + "            yy_error(YY_E_INTERNAL, false);\n"
    + "          }\n"
    + "        else\n"
    + "          {\n"
    + "          AcceptMethod m = accept_dispatch[yy_last_accept_state];\n"
    + "          if (m != null)\n"
    + "            {\n"
    + "            "+spec.type_name+" tmp = m();\n"
    + "            if (tmp != null)\n"
    + "              return tmp;\n"
    + "            }\n"
    + "          }\n"
    + "        yy_initial = true;\n"
    + "        yy_state = yy_state_dtrans[yy_lexical_state];\n"
    + "        yy_next_state = YY_NO_STATE;\n"
    + "        yy_last_accept_state = YY_NO_STATE;\n"
    + "        yy_mark_start();\n"
    + "        yy_this_accept = yy_acpt[yy_state];\n"
    + "        if (YY_NOT_ACCEPT != yy_this_accept)\n"
    + "          {\n"
    + "          yy_last_accept_state = yy_state;\n"
    + "          yy_mark_end();\n"
    + "          }\n"
    + "        }\n"
    + "      }\n"
    + "    }\n"
    + "  }\n");
  }

/*
 * Function: Actions
 * Description:     
 */
private string Actions()
  {
  int size = spec.accept_list.Count;
  int bogus_index = -2;
  Accept accept;
  StringBuilder sb = new StringBuilder(Lex.MAXSTR);

#if DEBUG
  Utility.assert(spec.accept_list.Count == spec.anchor_array.Length);
#endif
  string prefix = "";

  for (int elem = 0; elem < size; elem++)
    {
    accept = (Accept) spec.accept_list[elem];
    if (accept != null) 
      {
      sb.Append("        " + prefix + "if (yy_last_accept_state == ");
      sb.Append(elem);
      sb.Append(")\n");
      sb.Append("          { // begin accept action #");
      sb.Append(elem);
      sb.Append("\n");
      sb.Append(accept.action);
      sb.Append("\n");
      sb.Append("          } // end accept action #");
      sb.Append(elem);
      sb.Append("\n");
      sb.Append("          else if (yy_last_accept_state == ");
      sb.Append(bogus_index);
      sb.Append(")\n");
      sb.Append("            { /* no work */ }\n");
      prefix = "else ";
      bogus_index--;
      }
    }
  return sb.ToString();
  }

/*
 * Function: Action_Methods_Init
 */
private string Action_Methods_Init()
  {
  int size = spec.accept_list.Count;
  Accept accept;
  StringBuilder tbl = new StringBuilder();

#if DEBUG
  Utility.assert(spec.accept_list.Count == spec.anchor_array.Length);
#endif
  tbl.Append("accept_dispatch = new AcceptMethod[] \n {\n");
  for (int elem = 0; elem < size; elem++)
    {
    accept = (Accept) spec.accept_list[elem];
    if (accept != null && accept.action != null) 
      {
      tbl.Append("  new AcceptMethod(this.Accept_");
      tbl.Append(elem);
      tbl.Append("),\n");
      }
    else
      tbl.Append("  null,\n");
    }
  tbl.Append("  };\n");
  return tbl.ToString();
  }

/*
 * Function: Action_Methods_Body
 */
private string Action_Methods_Body()
  {
  int size = spec.accept_list.Count;
  Accept accept;
  StringBuilder sb = new StringBuilder(Lex.MAXSTR);

#if DEBUG
  Utility.assert(spec.accept_list.Count == spec.anchor_array.Length);
#endif
  for (int elem = 0; elem < size; elem++)
    {
    accept = (Accept) spec.accept_list[elem];
    if (accept != null && accept.action != null) 
      {
      sb.Append(spec.type_name+" Accept_");
      sb.Append(elem);
      sb.Append("()\n");
      sb.Append("    { // begin accept action #");
      sb.Append(elem);
      sb.Append("\n");
      sb.Append(accept.action);
      sb.Append("\n");
      sb.Append("    } // end accept action #");
      sb.Append(elem);
      sb.Append("\n\n");
      }
    }
  return sb.ToString();
  }


/*
 * Function: Footer
 * Description:     
 */
private void Footer()
  {
#if DEBUG
  Utility.assert(null != spec);
  Utility.assert(null != outstream);
#endif
  outstream.Write("}\n");
  }
}
}
