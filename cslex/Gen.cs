namespace Lex
{
/*
 * Class: Gen
 */
using System;
using System.Text;
using System.IO;
using System.Collections;
using BitSet;

public class Gen 
{
/*
 * Member Variables
 */
private StreamReader instream;	/* Lex specification file. */
private StreamWriter outstream;	/* Lexical analyzer source file. */

private Input ibuf;		/* Input buffer class. */

private Hashtable tokens;	/* Hashtable that maps characters to their 
				 corresponding lexical code for
				 the internal lexical analyzer. */
private Spec spec;		/* Spec class holds information
				 about the generated lexer. */
private bool init_flag;		/* Flag set to true only upon 
				 successful initialization. */

private MakeNfa makeNfa;	/* NFA machine generator module. */
private Nfa2Dfa nfa2dfa;	/* NFA to DFA machine (transition table) 
				 conversion module. */
private Minimize minimize;	/* Transition table compressor. */
//private SimplifyNfa simplifyNfa; /* NFA simplifier using char classes */
private Emit emit;		/* Output module that emits source code
				 into the generated lexer file. */

private String usercode;	/* temporary to hold the user supplied code */
/*
 * Constants
 */
private const bool ERROR = false;
private const bool NOT_ERROR = true;
private const int BUFFER_SIZE = 1024;

/*
 * Constants: Token Types
 */
public const int EOS = 1;
public const int ANY = 2;
public const int AT_BOL = 3;
public const int AT_EOL = 4;
public const int CCL_END = 5;
public const int CCL_START = 6;
public const int CLOSE_CURLY = 7;
public const int CLOSE_PAREN = 8;
public const int CLOSURE = 9;
public const int DASH = 10;
public const int END_OF_INPUT = 11;
public const int L = 12;
public const int OPEN_CURLY = 13;
public const int OPEN_PAREN = 14;
public const int OPTIONAL = 15;
public const int OR = 16;
public const int PLUS_CLOSE = 17;

/*
 * Function: LexGen
 */
public Gen(String filename)
  {

  /* Successful initialization flag. */
  init_flag = false;

  /* Open input stream. */
  instream = new StreamReader(
			      new FileStream(filename, FileMode.Open,
					     FileAccess.Read, FileShare.Read,
					     Lex.MAXBUF)
				  );
  if (instream == null)
    {
    Console.WriteLine("Error: Unable to open input file " + filename + ".");
    return;
    }
  int j = filename.LastIndexOf('\\');
  if (j < 0)
    j = 0;
  else
    j++;
  String outfile = filename.Substring(j,filename.Length-j).Replace('.','_')
    + ".cs";
  Console.WriteLine("Creating output file ["+outfile+"]");
  /* Open output stream. */
  outstream = new StreamWriter(
		       new FileStream(outfile,
			      FileMode.Create,
			      FileAccess.Write,
			      FileShare.Write, 8192)
			   );
  if (outstream == null)
    {
    Console.WriteLine("Error: Unable to open output file " + filename + ".java.");
    return;
    }

  /* Create input buffer class. */
  ibuf = new Input(instream);

  /* Initialize character hash table. */
  tokens = new Hashtable();
  tokens['$'] = AT_EOL;
  tokens['('] = OPEN_PAREN;
  tokens[')'] = CLOSE_PAREN;
  tokens['*'] = CLOSURE;
  tokens['+'] = PLUS_CLOSE;
  tokens['-'] = DASH;
  tokens['.'] = ANY;
  tokens['?'] = OPTIONAL;
  tokens['['] = CCL_START;
  tokens[']'] = CCL_END;
  tokens['^'] = AT_BOL;
  tokens['{'] = OPEN_CURLY;
  tokens['|'] = OR;
  tokens['}'] = CLOSE_CURLY;

  /* Initialize spec structure. */
  //  spec = new Spec(this);
  spec = new Spec();

  /* Nfa to dfa converter. */
  nfa2dfa = new Nfa2Dfa();
  minimize = new Minimize();
  makeNfa = new MakeNfa();
  //  simplifyNfa = new SimplifyNfa();
  emit = new Emit();

  /* Successful initialization flag. */
  init_flag = true;
  }

/*
 * Function: generate
 * Description: 
 */
public void generate()
  {
  if (!init_flag)
    {
    Error.parse_error(Error.E_INIT,0);
    }
#if DEBUG
  Utility.assert(this != null);
  Utility.assert(outstream != null);
  Utility.assert(ibuf != null);
  Utility.assert(tokens != null);
  Utility.assert(spec != null);
  Utility.assert(init_flag);
#endif

  if (spec.verbose)
    {
    Console.WriteLine("Processing first section -- user code.");
    }
  userCode();
  if (ibuf.eof_reached)
    {
    Error.parse_error(Error.E_EOF,ibuf.line_number);
    }

  if (spec.verbose)
    {
    Console.WriteLine("Processing second section -- Lex declarations.");
    }
  userDeclare();
  if (ibuf.eof_reached)
    {
    Error.parse_error(Error.E_EOF,ibuf.line_number);
    }

  if (spec.verbose)
    {
    Console.WriteLine("Processing third section -- lexical rules.");
    }
  userRules();

#if DO_DEBUG
  Console.WriteLine("Printing DO_DEBUG header");
  print_header();
#endif

  if (spec.verbose)
    {
    Console.WriteLine("Outputting lexical analyzer code.");
    }
  outstream.Write("namespace "+spec.namespace_name+"\n{\n");
  outstream.Write(usercode);
  outstream.Write("/* test */\n");
  emit.Write(spec,outstream);
  outstream.Write("\n}\n");

#if OLD_DUMP_DEBUG
  details();
#endif

  outstream.Close();
  }

/*
 * Function: userCode
 * Description: Process first section of specification,
 * echoing it into output file.
 */
private void userCode()
  {
  StringBuilder sb = new StringBuilder();
  if (!init_flag)
    {
    Error.parse_error(Error.E_INIT,0);
    }

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  if (ibuf.eof_reached)
    {
    Error.parse_error(Error.E_EOF,0);
    }

  while (true)
    {
    if (ibuf.GetLine())
      {
      /* Eof reached. */
      Error.parse_error(Error.E_EOF,0);
      }

    if (ibuf.line_read >= 2
	&& ibuf.line[0] == '%'
	&& ibuf.line[1] == '%')
      {
      usercode = sb.ToString();
      /* Discard remainder of line. */
      ibuf.line_index = ibuf.line_read;
      return;
      }

    sb.Append(new String(ibuf.line,0,ibuf.line_read));
    }
  }

/*
 * Function: getName
 */
private String getName()
  {
  /* Skip white space. */
  while (ibuf.line_index < ibuf.line_read
	 && Utility.IsSpace(ibuf.line[ibuf.line_index]))
    {
    ibuf.line_index++;
    }

  /* No name? */
  if (ibuf.line_index >= ibuf.line_read)
    {
    Error.parse_error(Error.E_DIRECT,0);
    }

  /* Determine length. */
  int elem = ibuf.line_index;
  while (elem < ibuf.line_read && !Utility.IsNewline(ibuf.line[elem]))
    elem++;

  /* Allocate non-terminated buffer of exact length. */
  StringBuilder sb = new StringBuilder(elem - ibuf.line_index);

  /* Copy. */
  elem = 0;
  while (ibuf.line_index < ibuf.line_read
	 && !Utility.IsNewline(ibuf.line[ibuf.line_index]))
    {
    sb.Append(ibuf.line[ibuf.line_index]);
    ibuf.line_index++;
    }

  return sb.ToString();
  }

private const int CLASS_CODE = 0;
private const int INIT_CODE = 1;
private const int EOF_CODE = 2;
private const int EOF_VALUE_CODE = 3;

/*
 * Function: packCode
 * Description:
 */
//private String packCode(String start_dir, String end_dir,
//			String prev_code, int prev_read, int specified)
private String packCode(String st_dir, String end_dir, String prev, int code)
  {
#if DEBUG
  Utility.assert(INIT_CODE == code
		  || CLASS_CODE == code
		  || EOF_CODE == code
		  || EOF_VALUE_CODE == code
		  );
#endif
  if (Utility.Compare(ibuf.line, st_dir) != 0)
    Error.parse_error(Error.E_INTERNAL,0);

  /*
   * build up the text to be stored in sb
   */
  StringBuilder sb;
  if (prev != null)
    sb = new StringBuilder(prev, prev.Length*2); // use any previous text
  else
    sb = new StringBuilder(Lex.MAXSTR);

  ibuf.line_index = st_dir.Length;
  while (true)
    {
    while (ibuf.line_index >= ibuf.line_read)
      {
      if (ibuf.GetLine())
	Error.parse_error(Error.E_EOF,ibuf.line_number);

      if (Utility.Compare(ibuf.line, end_dir) == 0)
	{
	ibuf.line_index = end_dir.Length - 1;
	return sb.ToString();
	}
      }
    while (ibuf.line_index < ibuf.line_read)
      {
      sb.Append(ibuf.line[ibuf.line_index]);
      ibuf.line_index++;
      }
    }
  }

/*
 * Member Variables: Lex directives.
 */
private String state_dir = "%state";
private String char_dir = "%char";
private String line_dir = "%line";
private String class_dir = "%class";
private String implements_dir = "%implements";
private String function_dir = "%function";
private String type_dir = "%type";
private String integer_dir = "%integer";
private String intwrap_dir = "%intwrap";
private String full_dir = "%full";
private String unicode_dir = "%unicode";
private String ignorecase_dir = "%ignorecase";
private String init_code_dir = "%init{";
private String init_code_end_dir = "%init}";
private String eof_code_dir = "%eof{";
private String eof_code_end_dir = "%eof}";
private String eof_value_code_dir = "%eofval{";
private String eof_value_code_end_dir = "%eofval}";
private String class_code_dir = "%{";
private String class_code_end_dir = "%}";
private String yyeof_dir = "%yyeof";
private String public_dir = "%public";
private String namespace_dir = "%namespace";

/*
 * Function: userDeclare
 * Description:
 */
private void userDeclare()
  {
#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  if (ibuf.eof_reached)
    {
    /* End-of-file. */
    Error.parse_error(Error.E_EOF,ibuf.line_number);
    }

  while (!ibuf.GetLine())
    {
    /* Look for double percent. */
    if (ibuf.line_read >= 2
	&& '%' == ibuf.line[0] 
	&& '%' == ibuf.line[1])
      {
      /* Mess around with line. */
      for (int elem = 0; elem < ibuf.line.Length - 2; ++elem)
	ibuf.line[elem] = ibuf.line[elem + 2];

      ibuf.line_read = ibuf.line_read - 2;

      ibuf.pushback_line = true;
      /* Check for and discard empty line. */
      if (ibuf.line_read == 0
	  || '\r' == ibuf.line[0]
	  || '\n' == ibuf.line[0])
	{
	ibuf.pushback_line = false;
	}
      return;
      }
    if (ibuf.line_read == 0)
      continue;

    if (ibuf.line[0] == '%')
      {
      /* Special lex declarations. */
      if (1 >= ibuf.line_read)
	{
	Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	continue;
	}

      switch (ibuf.line[1])
	{
	case '{':
	  if (Utility.Compare(ibuf.line, class_code_dir) == 0)
	    {
	    spec.class_code = packCode(class_code_dir,
				       class_code_end_dir,
				       spec.class_code,
				       CLASS_CODE);
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'c':
	  if (Utility.Compare(ibuf.line, char_dir) == 0)
	    {
	    /* Set line counting to ON. */
	    ibuf.line_index = char_dir.Length;
	    spec.count_chars = true;
	    break;
	    }	
	  if (Utility.Compare(ibuf.line, class_dir) == 0)
	    {
	    ibuf.line_index = class_dir.Length;
	    spec.class_name = getName();
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'e':
	  if (Utility.Compare(ibuf.line, eof_code_dir) == 0)
	    {
	    spec.eof_code = packCode(eof_code_dir,
				     eof_code_end_dir,
				     spec.eof_code,
				     EOF_CODE);
	    break;
	    }
	  if (Utility.Compare(ibuf.line, eof_value_code_dir) == 0)
	    {
	    spec.eof_value_code = packCode(eof_value_code_dir,
					   eof_value_code_end_dir,
					   spec.eof_value_code,
					   EOF_VALUE_CODE);
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;
	case 'f':
	  if (Utility.Compare(ibuf.line, function_dir) == 0)
	    {
	    /* Set line counting to ON. */
	    ibuf.line_index = function_dir.Length;
	    spec.function_name = getName();
	    break;
	    }
	  if (Utility.Compare(ibuf.line, full_dir) == 0)
	    {
	    ibuf.line_index = full_dir.Length;
	    spec.dtrans_ncols = Utility.MAX_EIGHT_BIT + 1;
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'i':
	  if (Utility.Compare(ibuf.line, integer_dir) == 0)
	    {
	    /* Set line counting to ON. */
	    ibuf.line_index = integer_dir.Length;
	    spec.integer_type = true;
	    break;
	    }
	  if (Utility.Compare(ibuf.line, intwrap_dir) == 0)
	    {
	    /* Set line counting to ON. */
	    ibuf.line_index = integer_dir.Length;
	    spec.intwrap_type = true;
	    break;
	    }
	  if (Utility.Compare(ibuf.line, init_code_dir) == 0)
	    {
	    spec.init_code = packCode(init_code_dir,
				      init_code_end_dir,
				      spec.init_code,
				      INIT_CODE);
	    break;
	    }
	  if (Utility.Compare(ibuf.line, implements_dir) == 0)
	    {
	    ibuf.line_index = implements_dir.Length;
	    spec.implements_name = getName();
	    break;
	    }
	  if (Utility.Compare(ibuf.line, ignorecase_dir) == 0)
	    {
	    /* Set ignorecase to ON. */
	    ibuf.line_index = ignorecase_dir.Length;
	    spec.ignorecase = true;
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'l':
	  if (Utility.Compare(ibuf.line, line_dir) == 0)
	    {
	    /* Set line counting to ON. */
	    ibuf.line_index = line_dir.Length;
	    spec.count_lines = true;
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'p':
	  if (Utility.Compare(ibuf.line, public_dir) == 0)
	    {
	    /* Set public flag. */
	    ibuf.line_index = public_dir.Length;
	    spec.lex_public = true;
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 's':
	  if (Utility.Compare(ibuf.line, state_dir) == 0)
	    {
	    /* Recognize state list. */
	    ibuf.line_index = state_dir.Length;
	    saveStates();
	    break;
	    }
	  /* Undefined directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 't':
	  if (Utility.Compare(ibuf.line, type_dir) == 0)
	    {
	    ibuf.line_index = type_dir.Length;
	    spec.type_name = getName();
	    break;
	    }
	  /* Undefined directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'u':
	  if (Utility.Compare(ibuf.line, unicode_dir) == 0)
	    {
	    ibuf.line_index = unicode_dir.Length;
	    /* UNDONE: What to do here? */
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'y':
	  if (Utility.Compare(ibuf.line, yyeof_dir) == 0)
	    {
	    ibuf.line_index = yyeof_dir.Length;
	    spec.yyeof = true;
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	case 'n':
	  if (Utility.Compare(ibuf.line, namespace_dir) == 0)
	    {
	    ibuf.line_index = namespace_dir.Length;
	    spec.namespace_name = getName();
	    break;
	    }
	  /* Bad directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;

	default:
	  /* Undefined directive. */
	  Error.parse_error(Error.E_DIRECT, ibuf.line_number);
	  break;
	}
      }
    else
      {
      /* Regular expression macro. */
      ibuf.line_index = 0;
      saveMacro();
      }
#if OLD_DEBUG
    Console.WriteLine("Line number " + ibuf.line_number + ":"); 
    Console.Write(new String(ibuf.line, 0,ibuf.line_read));
#endif
    }
  }

/*
 * Function: userRules
 * Description: Processes third section of Lex 
 * specification and creates minimized transition table.
 */
private void userRules()
  {
  if (false == init_flag)
    {
    Error.parse_error(Error.E_INIT,0);
    }

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  /* UNDONE: Need to handle states preceding rules. */

  if (spec.verbose)
    {
    Console.WriteLine("Creating NFA machine representation.");
    }

  MakeNfa.Allocate_BOL_EOF(spec);
  MakeNfa.CreateMachine(this, spec, ibuf);
  SimplifyNfa.simplify(spec);

#if DUMMY
  print_nfa();
#endif

#if DEBUG
  Utility.assert(END_OF_INPUT == spec.current_token);
#endif

  if (spec.verbose)
    {
    Console.WriteLine("Creating DFA transition table.");
    }

  Nfa2Dfa.MakeDFA(spec);

#if FOODEBUG
  Console.WriteLine("Printing FOODEBUG header");
  print_header();
#endif

  if (spec.verbose)
    {
    Console.WriteLine("Minimizing DFA transition table.");
    }
  minimize.min_dfa(spec);
  }

/*
 * Function: printccl
 * Description: Debuggng routine that outputs readable form
 * of character class.
 */
private void printccl(CharSet cset)
  {
  int i;

  Console.Write(" [");
  for (i = 0; i < spec.dtrans_ncols; ++i)
    {
    if (cset.contains(i))
      {
      Console.Write(interp_int(i));
      }
    }
  Console.Write(']');
  }

/*
 * Function: plab
 * Description:
 */
private String plab(Nfa state)
  {
  if (null == state)
    {
    return "--";
    }

  int index = spec.nfa_states.IndexOf(state, 0, spec.nfa_states.Count);

  return index.ToString();
  }

/*
 * Function: interp_int
 * Description:
 */
private String interp_int(int i)
  {
  switch (i)
    {
    case (int) '\b':
      return "\\b";

    case (int) '\t':
      return "\\t";

    case (int) '\n':
      return "\\n";

    case (int) '\f':
      return "\\f";

    case (int) '\r':
      return "\\r";

    case (int) ' ':
      return "\\ ";

    default:
      {
      return Char.ToString((char) i);
      }
    }
  }

/*
 * Function: print_nfa
 * Description:
 */
public void print_nfa()
  {
  int elem;
  Nfa nfa;
  int j;
  int vsize;

  Console.WriteLine("--------------------- NFA -----------------------");

  for (elem = 0; elem < spec.nfa_states.Count; elem++)
    {
    nfa = (Nfa) spec.nfa_states[elem];

    Console.Write("Nfa state " + plab(nfa) + ": ");

    if (null == nfa.GetNext())
      {
      Console.Write("(TERMINAL)");
      }
    else
      {
      Console.Write("--> " + plab(nfa.GetNext()));
      Console.Write("--> " + plab(nfa.GetSib()));

      switch (nfa.GetEdge())
	{
	case Nfa.CCL:
	  printccl(nfa.GetCharSet());
	  break;

	case Nfa.EPSILON:
	  Console.Write(" EPSILON ");
	  break; 

	default:
	  Console.Write(" " + interp_int(nfa.GetEdge()));
	  break;
	}
      }

    if (0 == elem)
      {
      Console.Write(" (START STATE)");
      }

    if (null != nfa.GetAccept())
      {
      Console.Write(" accepting " 
		    + ((0 != (nfa.GetAnchor() & Spec.START)) ? "^" : "")
		    + "<" 
		    + nfa.GetAccept().action
		    + ">"
		    + ((0 != (nfa.GetAnchor() & Spec.END)) ? "$" : ""));
      }
    Console.WriteLine("");
    }

  foreach (string state in spec.states.Keys)
    {
    int index = (int) spec.states[state];
#if DEBUG
    Utility.assert(null != state);
#endif
    Console.WriteLine("State \"" + state
		      + "\" has identifying index " + index + ".");
    Console.Write("\tStart states of matching rules: ");

    vsize = spec.state_rules[index].Count;
    for (j = 0; j < vsize; ++j)
      {
      ArrayList a = spec.state_rules[index];
#if DEBUG
      Utility.assert(null != a);
#endif
      object o = a[j];
#if DEBUG
      Utility.assert(null != o);
#endif
      nfa = (Nfa) o;
      Console.Write(spec.nfa_states.IndexOf(nfa) + " ");
      }

    Console.WriteLine("");
    }

  Console.WriteLine("-------------------- NFA ----------------------");
  }

/*
 * Function: getStates
 * Description: Parses the state area of a rule,
 * from the beginning of a line.
 * < state1, state2 ... > regular_expression { action }
 * Returns null on only EOF.  Returns all_states, 
 * initialied properly to correspond to all states,
 * if no states are found.
 * Special Notes: This function treats commas as optional
 * and permits states to be spread over multiple lines.
 */
private BitSet all_states = null;
public BitSet GetStates()
  {
  int start_state;
  int count_state;
  BitSet states;
  String name;
  int index;

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  states = null;

  /* Skip white space. */
  while (Utility.IsSpace(ibuf.line[ibuf.line_index]))
    {
    ibuf.line_index++;

    while (ibuf.line_index >= ibuf.line_read)
      {
      /* Must just be an empty line. */
      if (ibuf.GetLine())
	{
	/* EOF found. */
	return null;
	}
      }
    }

  /* Look for states. */
  if ('<' == ibuf.line[ibuf.line_index])
    {
    ibuf.line_index++;

    states = new BitSet();	// create new BitSet

    /* Parse states. */
    while (true)
      {
      /* We may have reached the end of the line. */
      while (ibuf.line_index >= ibuf.line_read)
	{
	if (ibuf.GetLine())
	  {
	  /* EOF found. */
	  Error.parse_error(Error.E_EOF,ibuf.line_number);
	  return states;
	  }
	}
      while (true)
	{
	/* Skip white space. */
	while (Utility.IsSpace(ibuf.line[ibuf.line_index]))
	  {
	  ibuf.line_index++;
	  while (ibuf.line_index >= ibuf.line_read)
	    {
	    if (ibuf.GetLine())
	      {
	      /* EOF found. */
	      Error.parse_error(Error.E_EOF,ibuf.line_number);
	      return states;
	      }
	    }
	  }

	if (',' != ibuf.line[ibuf.line_index])
	  {
	  break;
	  }

	ibuf.line_index++;
	}

      if ('>' == ibuf.line[ibuf.line_index])
	{
	ibuf.line_index++;
	if (ibuf.line_index < ibuf.line_read)
	  {
	  advance_stop = true;
	  }
	return states;
	}

      /* Read in state name. */
      start_state = ibuf.line_index;
      while (false == Utility.IsSpace(ibuf.line[ibuf.line_index])
	     && ',' != ibuf.line[ibuf.line_index]
	     && '>' != ibuf.line[ibuf.line_index])
	{
	ibuf.line_index++;

	if (ibuf.line_index >= ibuf.line_read)
	  {
	  /* End of line means end of state name. */
	  break;
	  }
	}
      count_state = ibuf.line_index - start_state;

      /* Save name after checking definition. */
      name = new String(ibuf.line, start_state, count_state);
      Object o = spec.states[name];
      if (o == null)
	{
	/* Uninitialized state. */
	Console.WriteLine("Uninitialized State Name: [" + name +"]");
	Error.parse_error(Error.E_STATE,ibuf.line_number);
	}
      index = (int) o;
      states.Set(index, true);
      }
    }

  if (null == all_states)
    {
    all_states = new BitSet(states.Count, true);
    }

  if (ibuf.line_index < ibuf.line_read)
    {
    advance_stop = true;
    }
  return all_states;
  }

/*
 * Function: expandMacro
 * Description: Returns false on error, true otherwise. 
 */
private bool expandMacro()
  {
  int elem;
  int start_macro;
  int end_macro;
  int start_name;
  int count_name;
  String def;
  int def_elem;
  String name;
  char[] replace;
  int rep_elem;

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  /* Check for macro. */
  if ('{' != ibuf.line[ibuf.line_index])
    {
    Error.parse_error(Error.E_INTERNAL,ibuf.line_number);
    return ERROR;
    }

  start_macro = ibuf.line_index;
  elem = ibuf.line_index + 1;
  if (elem >= ibuf.line_read)
    {
    Error.impos("Unfinished macro name");
    return ERROR;
    }

  /* Get macro name. */
  start_name = elem;
  while ('}' != ibuf.line[elem])
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      Error.impos("Unfinished macro name at line " + ibuf.line_number);
      return ERROR;
      }
    }
  count_name = elem - start_name;
  end_macro = elem;

  /* Check macro name. */
  if (0 == count_name)
    {
    Error.impos("Nonexistent macro name");
    return ERROR;
    }

  /* Debug checks. */
#if DEBUG
  Utility.assert(0 < count_name);
#endif

  /* Retrieve macro definition. */
  name = new String(ibuf.line,start_name,count_name);
  def = (String) spec.macros[name];
  if (null == def)
    {
    /*Error.impos("Undefined macro \"" + name + "\".");*/
    Console.WriteLine("Error: Undefined macro \"" + name + "\".");
    Error.parse_error(Error.E_NOMAC, ibuf.line_number);
    return ERROR;
    }
#if OLD_DUMP_DEBUG
  Console.WriteLine("expanded escape: \"" + def + "\"");
#endif

  /* Replace macro in new buffer,
     beginning by copying first part of line buffer. */
  replace = new char[ibuf.line.Length];
  for (rep_elem = 0; rep_elem < start_macro; ++rep_elem)
    {
    replace[rep_elem] = ibuf.line[rep_elem];
#if DEBUG
    Utility.assert(rep_elem < replace.Length);
#endif
    }

  /* Copy macro definition. */
  if (rep_elem >= replace.Length)
    {
    replace = Utility.doubleSize(replace);
    }
  for (def_elem = 0; def_elem < def.Length; ++def_elem)
    {
    replace[rep_elem] = def[def_elem];

    ++rep_elem;
    if (rep_elem >= replace.Length)
      {
      replace = Utility.doubleSize(replace);
      }
    }

  /* Copy last part of line. */
  if (rep_elem >= replace.Length)
    {
    replace = Utility.doubleSize(replace);
    }
  for (elem = end_macro + 1; elem < ibuf.line_read; ++elem)
    {
    replace[rep_elem] = ibuf.line[elem];

    ++rep_elem;
    if (rep_elem >= replace.Length)
      {
      replace = Utility.doubleSize(replace);
      }
    } 

  /* Replace buffer. */
  ibuf.line = replace;
  ibuf.line_read = rep_elem;

#if OLD_DEBUG
  Console.WriteLine(new String(ibuf.line,0,ibuf.line_read));
#endif
  return NOT_ERROR;
  }

/*
 * Function: saveMacro
 * Description: Saves macro definition of form:
 * macro_name = macro_definition
 */
private void saveMacro()
  {
  int elem;
  int start_name;
  int count_name;
  int start_def;
  int count_def;
  bool saw_escape;
  bool in_quote;
  bool in_ccl;

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  /* Macro declarations are of the following form:
     macro_name macro_definition */

  elem = 0;

  /* Skip white space preceding macro name. */
  while (Utility.IsSpace(ibuf.line[elem]))
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* End of line has been reached,
	 and line was found to be empty. */
      return;
      }
    }

  /* Read macro name. */
  start_name = elem;
  while (false == Utility.IsSpace(ibuf.line[elem])
	 && '=' != ibuf.line[elem])
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* Macro name but no associated definition. */
      Error.parse_error(Error.E_MACDEF,ibuf.line_number);
      }
    }
  count_name = elem - start_name;

  /* Check macro name. */
  if (0 == count_name) 
    {
    /* Nonexistent macro name. */
    Error.parse_error(Error.E_MACDEF,ibuf.line_number);
    }

  /* Skip white space between name and definition. */
  while (Utility.IsSpace(ibuf.line[elem]))
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* Macro name but no associated definition. */
      Error.parse_error(Error.E_MACDEF,ibuf.line_number);
      }
    }

  if ('=' == ibuf.line[elem])
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* Macro name but no associated definition. */
      Error.parse_error(Error.E_MACDEF,ibuf.line_number);
      }
    }

  /* Skip white space between name and definition. */
  while (Utility.IsSpace(ibuf.line[elem]))
    {
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* Macro name but no associated definition. */
      Error.parse_error(Error.E_MACDEF,ibuf.line_number);
      }
    }

  /* Read macro definition. */
  start_def = elem;
  in_quote = false;
  in_ccl = false;
  saw_escape = false;
  while (false == Utility.IsSpace(ibuf.line[elem])
	 || true == in_quote
	 || true == in_ccl
	 || true == saw_escape)
    {
    if ('\"' == ibuf.line[elem] && false == saw_escape)
      {
      in_quote = !in_quote;
      }

    if ('\\' == ibuf.line[elem] && false == saw_escape)
      {
      saw_escape = true;
      }
    else
      {
      saw_escape = false;
      }
    if (false == saw_escape && false == in_quote)
      {
      if ('[' == ibuf.line[elem] && false == in_ccl)
	in_ccl = true;
      if (']' == ibuf.line[elem] && true == in_ccl)
	in_ccl = false;
      }
    ++elem;
    if (elem >= ibuf.line_read)
      {
      /* End of line. */
      break;
      }
    }
  count_def = elem - start_def;

  /* Check macro definition. */
  if (count_def == 0) 
    {
    /* Nonexistent macro name. */
    Error.parse_error(Error.E_MACDEF,ibuf.line_number);
    }

  /* Debug checks. */
#if DEBUG
  Utility.assert(0 < count_def);
  Utility.assert(0 < count_name);
  Utility.assert(null != spec.macros);
#endif

  String macro_name = new String(ibuf.line,start_name,count_name);
  String macro_def = new String(ibuf.line,start_def,count_def);
#if OLD_DEBUG
  Console.WriteLine("macro name \"" + macro_name + "\".");
  Console.WriteLine("macro definition \"" + macro_def + "\".");
#endif
  /* Add macro name and definition to table. */
  spec.macros.Add(macro_name, macro_def);
  }

/*
 * Function: saveStates
 * Description: Takes state declaration and makes entries
 * for them in state hashtable in CSpec structure.
 * State declaration should be of the form:
 * %state name0[, name1, name2 ...]
 * (But commas are actually optional as long as there is 
 * white space in between them.)
 */
private void saveStates()
  {
  int start_state;
  int count_state;

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  /* EOF found? */
  if (ibuf.eof_reached)
    return;

  /* Debug checks. */
#if DEBUG
  Utility.assert('%' == ibuf.line[0]);
  Utility.assert('s' == ibuf.line[1]);
  Utility.assert(ibuf.line_index <= ibuf.line_read);
  Utility.assert(0 <= ibuf.line_index);
  Utility.assert(0 <= ibuf.line_read);
#endif

  /* Blank line?  No states? */
  if (ibuf.line_index >= ibuf.line_read)
    return;

  while (ibuf.line_index < ibuf.line_read)
    {
#if OLD_DEBUG
    Console.WriteLine("line read " + ibuf.line_read
		      + "\tline index = " + ibuf.line_index);
#endif

    /* Skip white space. */
    while (Utility.IsSpace(ibuf.line[ibuf.line_index]))
      {
      ibuf.line_index++;
      if (ibuf.line_index >= ibuf.line_read)
	{
	/* No more states to be found. */
	return;
	}
      }

    /* Look for state name. */
    start_state = ibuf.line_index;
    while (!Utility.IsSpace(ibuf.line[ibuf.line_index])
	   && ibuf.line[ibuf.line_index] != ',')
      {
      ibuf.line_index++;
      if (ibuf.line_index >= ibuf.line_read)
	{
	/* End of line and end of state name. */
	break;
	}
      }
    count_state = ibuf.line_index - start_state;

#if OLD_DEBUG
    Console.WriteLine("State name \"" 
		      + new String(ibuf.line,start_state,count_state)
			  + "\".");
    Console.WriteLine("Integer index \"" 
		      + spec.states.Count
		      + "\".");
#endif

    /* Enter new state name, along with unique index. */
    spec.states[new String(ibuf.line,start_state,count_state)] =
      spec.states.Count;

    /* Skip comma. */
    if (',' == ibuf.line[ibuf.line_index])
      {
      ibuf.line_index++;
      if (ibuf.line_index >= ibuf.line_read)
	{
	/* End of line. */
	return;
	}
      }
    }
  }

/*
 * Function: expandEscape
 * Description: Takes escape sequence and returns
 * corresponding character code.
 */
private char expandEscape()
  {
  char r;

  /* Debug checks. */
#if DEBUG
  Utility.assert(ibuf.line_index < ibuf.line_read);
  Utility.assert(0 < ibuf.line_read);
  Utility.assert(0 <= ibuf.line_index);
#endif

  if (ibuf.line[ibuf.line_index] != '\\')
    {
    ibuf.line_index++;
    return ibuf.line[ibuf.line_index - 1];
    }
  else
    {
    ibuf.line_index++;
    switch (Utility.toupper(ibuf.line[ibuf.line_index]))
      {
      case 'B':
	ibuf.line_index++;
	return '\b';

      case 'T':
	ibuf.line_index++;
	return '\t';

      case 'N':
	ibuf.line_index++;
	return '\n';

      case 'F':
	ibuf.line_index++;
	return '\f';

      case 'R':
	ibuf.line_index++;
	return '\r';

      case '^':
	ibuf.line_index++;
	r = (char) (Utility.toupper(ibuf.line[ibuf.line_index]) - '@');
	ibuf.line_index++;
	return r;

      case 'X':
	ibuf.line_index++;
	r = (char) 0;
	if (Utility.ishexdigit(ibuf.line[ibuf.line_index]))
	  {
	  r = Utility.hex2bin(ibuf.line[ibuf.line_index]);
	  ibuf.line_index++;
	  }
	if (Utility.ishexdigit(ibuf.line[ibuf.line_index]))
	  {
	  r = (char) (r << 4);
	  r = (char) (r | Utility.hex2bin(ibuf.line[ibuf.line_index]));
	  ibuf.line_index++;
	  }
	if (Utility.ishexdigit(ibuf.line[ibuf.line_index]))
	  {
	  r = (char) (r << 4);
	  r = (char) (r | Utility.hex2bin(ibuf.line[ibuf.line_index]));
	  ibuf.line_index++;
	  }
	return r;

      default:
	if (!Utility.isoctdigit(ibuf.line[ibuf.line_index]))
	  {
	  r = ibuf.line[ibuf.line_index];
	  ibuf.line_index++;
	  }
	else
	  {
	  r = Utility.oct2bin(ibuf.line[ibuf.line_index]);
	  ibuf.line_index++;

	  if (Utility.isoctdigit(ibuf.line[ibuf.line_index]))
	    {
	    r = (char) (r << 3);
	    r = (char) (r | Utility.oct2bin(ibuf.line[ibuf.line_index]));
	    ibuf.line_index++;
	    }

	  if (Utility.isoctdigit(ibuf.line[ibuf.line_index]))
	    {
	    r = (char) (r << 3);
	    r = (char) (r | Utility.oct2bin(ibuf.line[ibuf.line_index]));
	    ibuf.line_index++;
	    }
	  }
	return r;
      }
    }
  }

/*
 * Function: packAccept
 * Description: Packages and returns CAccept 
 * for action next in input stream.
 */
public Accept packAccept()
  {
  Accept accept;
  int brackets;
  bool inquotes;
  bool instarcomment;
  bool inslashcomment;
  bool escaped;
  bool slashed;

  StringBuilder action = new StringBuilder(BUFFER_SIZE);

#if DEBUG
  Utility.assert(null != this);
  Utility.assert(null != outstream);
  Utility.assert(null != ibuf);
  Utility.assert(null != tokens);
  Utility.assert(null != spec);
#endif

  /* Get a new line, if needed. */
  while (ibuf.line_index >= ibuf.line_read)
    {
    if (ibuf.GetLine())
      {
      Error.parse_error(Error.E_EOF,ibuf.line_number);
      return null;
      }
    }

  /* Look for beginning of action */
  while (Utility.IsSpace(ibuf.line[ibuf.line_index]))
    {
    ibuf.line_index++;

    /* Get a new line, if needed. */
    while (ibuf.line_index >= ibuf.line_read)
      {
      if (ibuf.GetLine())
	{
	Error.parse_error(Error.E_EOF,ibuf.line_number);
	return null;
	}
      }
    }

  /* Look for brackets. */
  if ('{' != ibuf.line[ibuf.line_index])
    {
    Error.parse_error(Error.E_BRACE,ibuf.line_number); 
    }

  /* Copy new line into action buffer. */
  brackets = 0;
  inquotes = inslashcomment = instarcomment = false;
  escaped  = slashed = false;
  while (true)
    {
    action.Append(ibuf.line[ibuf.line_index]);

    /* Look for quotes. */
    if (inquotes && escaped)
      escaped=false;		// only protects one char, but this is enough.
    else if (inquotes && '\\' == ibuf.line[ibuf.line_index])
      escaped=true;
    else if ('\"' == ibuf.line[ibuf.line_index])
      inquotes=!inquotes; // unescaped quote.
    /* Look for comments. */
    if (instarcomment)
      {	// inside "/*" comment; look for "*/"
      if (slashed && '/' == ibuf.line[ibuf.line_index])
	instarcomment = slashed = false;
      else			// note that inside a star comment,
			      // slashed means starred
	slashed = ('*' == ibuf.line[ibuf.line_index]);
      }
    else if (!inslashcomment)
      {			// not in comment, look for /* or //
      inslashcomment = 
	(slashed && '/' == ibuf.line[ibuf.line_index]);
      instarcomment =
	(slashed && '*' == ibuf.line[ibuf.line_index]);
      slashed = ('/' == ibuf.line[ibuf.line_index]);
      }

    /* Look for brackets. */
    if (!inquotes && !instarcomment && !inslashcomment)
      {
      if ('{' == ibuf.line[ibuf.line_index])
	{
	++brackets;
	}
      else if ('}' == ibuf.line[ibuf.line_index])
	{
	--brackets;
	if (0 == brackets)
	  {
	  ibuf.line_index++;
	  break;
	  }
	}
      }

    ibuf.line_index++;
    /* Get a new line, if needed. */
    while (ibuf.line_index >= ibuf.line_read)
      {
      inslashcomment = slashed = false;
      if (inquotes)
	{ // non-fatal
	Error.parse_error(Error.E_NEWLINE,ibuf.line_number);
	inquotes=false;
	}
      if (ibuf.GetLine())
	{
	Error.parse_error(Error.E_SYNTAX,ibuf.line_number);
	return null;
	}
      }
    }

  accept = new Accept(action.ToString(), ibuf.line_number);

#if DEBUG
  Utility.assert(null != accept);
#endif

#if DESCENT_DEBUG
  Console.Write("\nAccepting action:");
  Console.WriteLine(accept.action);
#endif

  return accept;
  }

/*
 * Function: advance
 * Description: Returns code for next token.
 */
private bool advance_stop = false;

public int Advance()
  {
  bool saw_escape = false;

  if (ibuf.eof_reached)
    {
    /* EOF has already been reached, so return appropriate code. */
    spec.current_token = END_OF_INPUT;
    spec.lexeme = '\0';
    return spec.current_token;
    }

  /* End of previous regular expression? Refill line buffer? */
  if (EOS == spec.current_token || ibuf.line_index >= ibuf.line_read)
    {
    if (spec.in_quote)
      {
      Error.parse_error(Error.E_SYNTAX,ibuf.line_number);
      }

    while (true)
      {
      if (!advance_stop || ibuf.line_index >= ibuf.line_read)
	{
	if (ibuf.GetLine())
	  {
	  /* EOF has already been reached, so return appropriate code. */

	  spec.current_token = END_OF_INPUT;
	  spec.lexeme = '\0';
	  return spec.current_token;
	  }
	ibuf.line_index = 0;
	}
      else
	{
	advance_stop = false;
	}

      while (ibuf.line_index < ibuf.line_read
	     && true == Utility.IsSpace(ibuf.line[ibuf.line_index]))
	{
	ibuf.line_index++;
	}

      if (ibuf.line_index < ibuf.line_read)
	{
	break;
	}
      }
    }

#if DEBUG
  Utility.assert(ibuf.line_index <= ibuf.line_read);
#endif

  while (true)
    {
    if (!spec.in_quote && '{' == ibuf.line[ibuf.line_index])
      {
      if (!expandMacro())
	{
	break;
	}

      if (ibuf.line_index >= ibuf.line_read)
	{
	spec.current_token = EOS;
	spec.lexeme = '\0';
	return spec.current_token;
	}
      }
    else if ('\"' == ibuf.line[ibuf.line_index])
      {
      spec.in_quote = !spec.in_quote;
      ibuf.line_index++;

      if (ibuf.line_index >= ibuf.line_read)
	{
	spec.current_token = EOS;
	spec.lexeme = '\0';
	return spec.current_token;
	}
      }
    else
      {
      break;
      }
    }

  if (ibuf.line_index > ibuf.line_read)
    {
    Console.WriteLine("ibuf.line_index = " + ibuf.line_index);
    Console.WriteLine("ibuf.line_read = " + ibuf.line_read);
    Utility.assert(ibuf.line_index <= ibuf.line_read);
    }

  /* Look for backslash, and corresponding 
     escape sequence. */
  if ('\\' == ibuf.line[ibuf.line_index])
    {
    saw_escape = true;
    }
  else
    {
    saw_escape = false;
    }

  if (!spec.in_quote)
    {
    if (!spec.in_ccl && Utility.IsSpace(ibuf.line[ibuf.line_index]))
      {
      /* White space means the end of 
	 the current regular expression. */

      spec.current_token = EOS;
      spec.lexeme = '\0';
      return spec.current_token;
      }

    /* Process escape sequence, if needed. */
    if (saw_escape)
      {
      spec.lexeme = expandEscape();
      }
    else
      {
      spec.lexeme = ibuf.line[ibuf.line_index];
      ibuf.line_index++;
      }
    }
  else
    {
    if (saw_escape 
	&& (ibuf.line_index + 1) < ibuf.line_read
	&& '\"' == ibuf.line[ibuf.line_index + 1])
      {
      spec.lexeme = '\"';
      ibuf.line_index = ibuf.line_index + 2;
      }
    else
      {
      spec.lexeme = ibuf.line[ibuf.line_index];
      ibuf.line_index++;
      }
    }

  if (spec.in_quote || saw_escape)
    {
    spec.current_token = L;
    }
  else
    {
    Object code = tokens[spec.lexeme];
    if (code == null)
      {
      spec.current_token = L;
      }
    else
      {
      spec.current_token = (int) code;
      }
    }

  if (CCL_START == spec.current_token)
    spec.in_ccl = true;
  if (CCL_END   == spec.current_token)
    spec.in_ccl = false;

#if FOODEBUG
  DumpLexeme(spec.lexeme, spec.current_token, ibuf.line_index);
#endif

  return spec.current_token;
  }

#if FOODEBUG 
void DumpLexeme(char lexeme, int token, int index)
  {
  StringBuilder sb = new StringBuilder();
  sb.Append("Lexeme: '");
  if (lexeme < ' ')
    {
    lexeme += (char) 64;
    sb.Append("^");
    }
  sb.Append(lexeme);
  sb.Append("'\tToken: ");
  sb.Append(token);
  sb.Append("\tIndex: ");
  sb.Append(index);
  Console.WriteLine(sb.ToString());
  }
#endif

/*
 * Function: details
 * Description: High level debugging routine.
 */
private void details()
  {
  Console.WriteLine("\n\t** Macros **");
  foreach (string name in spec.macros.Keys)
    {
#if DEBUG
    Utility.assert(null != name);
#endif
    string def = (String) spec.macros[name];
#if DEBUG
    Utility.assert(null != def);
#endif
    Console.WriteLine("Macro name \"" + name + "\" has definition \"" 
		      + def + "\".");
    }

  Console.WriteLine("\n\t** States **");
  foreach (string state in spec.states.Keys)
    {
    int index = (int) spec.states[state];

#if DEBUG
    Utility.assert(null != state);
#endif

    Console.WriteLine("State \"" + state + "\" has identifying index " 
		      + index + ".");
    }

  Console.WriteLine("\n\t** Character Counting **");
  if (!spec.count_chars)
    Console.WriteLine("Character counting is off.");
  else
    {
#if DEBUG
    Utility.assert(spec.count_lines);
#endif
    Console.WriteLine("Character counting is on.");
    }

  Console.WriteLine("\n\t** Line Counting **");
  if (!spec.count_lines)
    Console.WriteLine("Line counting is off.");
  else
    {
#if DEBUG
    Utility.assert(spec.count_lines);
#endif
    Console.WriteLine("Line counting is on.");
    }

  Console.WriteLine("\n\t** Operating System Specificity **");

#if FOODEBUG
  if (spec.nfa_states != null && spec.nfa_start != null)
    {
    Console.WriteLine("\n\t** NFA machine **");
    print_nfa();
    }
#endif

  if (spec.dtrans_list != null)
    {
    Console.WriteLine("\n\t** DFA transition table **");
    }
  }

/*
 * Function: print_header
 */
private void print_header()
  {
  int j;
  int chars_printed=0;
  DTrans dtrans;
  int last_transition;
  String str;
  Accept accept;

  Console.WriteLine("/*---------------------- DFA -----------------------");

  if (spec.states == null)
    throw new ApplicationException("States is null");

  foreach (string state in spec.states.Keys)
    {
    int index = (int) spec.states[state];
#if DEBUG
    Utility.assert(null != state);
#endif

    Console.WriteLine("State \"" + state + "\" has identifying index " 
		      + index + ".");
    if (DTrans.F != spec.state_dtrans[index])
      {
      Console.WriteLine("\tStart index in transition table: "
			+ spec.state_dtrans[index]);
      }
    else
      {
      Console.WriteLine("\tNo associated transition states.");
      }
    }

  for (int i = 0; i < spec.dtrans_list.Count; ++i)
    {
    dtrans = (DTrans) spec.dtrans_list[i];

    if (null == spec.accept_list && null == spec.anchor_array)
      {
      if (null == dtrans.GetAccept())
	{
	Console.Write(" * State " + i + " [nonaccepting]");
	}
      else
	{
	Console.Write(" * State " + i
		      + " [accepting, line "
		      + dtrans.GetAccept().line_number
		      + " <"
		      + dtrans.GetAccept().action
		      + ">]");
	if (Spec.NONE != dtrans.GetAnchor())
	  {
	  Console.Write(" Anchor: "
			+ ((0 != (dtrans.GetAnchor() & Spec.START)) 
			   ? "start " : "")
			+ ((0 != (dtrans.GetAnchor() & Spec.END)) 
			   ? "end " : ""));
	  }
	}
      }
    else
      {
      accept = (Accept) spec.accept_list[i];

      if (null == accept)
	{
	Console.Write(" * State " + i + " [nonaccepting]");
	}
      else
	{
	Console.Write(" * State " + i
		      + " [accepting, line "
		      + accept.line_number
		      + " <"
		      + accept.action
		      + ">]");
	if (Spec.NONE != spec.anchor_array[i])
	  {
	  Console.Write(" Anchor: "
			+ ((0 != (spec.anchor_array[i] & Spec.START)) 
			   ? "start " : "")
			+ ((0 != (spec.anchor_array[i] & Spec.END)) 
			   ? "end " : ""));
	  }
	}
      }

    last_transition = -1;
    for (j = 0; j < spec.dtrans_ncols; ++j)
      {
      if (DTrans.F != dtrans.GetDTrans(j))
	{
	if (last_transition != dtrans.GetDTrans(j))
	  {
	  Console.Write("\n *    goto " + dtrans.GetDTrans(j) + " on ");
	  chars_printed = 0;
	  }
	str = interp_int((int) j);
	Console.Write(str);

	chars_printed = chars_printed + str.Length; 
	if (56 < chars_printed)
	  {
	  Console.Write("\n *             ");
	  chars_printed = 0;
	  }
	last_transition = dtrans.GetDTrans(j);
	}
      }
    Console.WriteLine("");
    }
  Console.WriteLine(" */\n");
  }
}
}
