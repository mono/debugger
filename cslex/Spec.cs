namespace Lex
{
/*
 * Class: Spec
 */
using System;
using System.Collections;

public class Spec
{
/*
 * Member Variables
 */

/* Lexical States. */
public Hashtable states; /* Hashtable taking state indices (Integer) 
			    to state name (String). */

/* Regular Expression Macros. */ 
public Hashtable macros;	/* Hashtable taking macro name (String)
				   to corresponding char buffer that
				   holds macro definition. */

/* NFA Machine. */
public Nfa nfa_start;		/* Start state of NFA machine. */
public ArrayList nfa_states;	/* List of states, with index
				 corresponding to label. */

public ArrayList[] state_rules; /* An array of Lists of Integers.
				   The ith Vector represents the lexical state
				   with index i.  The contents of the ith 
				   List are the indices of the NFA start
				   states that can be matched while in
				   the ith lexical state. */


public int[] state_dtrans;

/* DFA Machine. */
public ArrayList dfa_states;	/* List of states, with index
				   corresponding to label. */
public Hashtable dfa_sets;	/* Hashtable taking set of NFA states
				   to corresponding DFA state, 
				   if the latter exists. */

/* Accept States and Corresponding Anchors. */
public ArrayList accept_list;
public int[] anchor_array;

/* Transition Table. */
public ArrayList dtrans_list;
public int dtrans_ncols;
public int[] row_map;
public int[] col_map;

/* Special pseudo-characters for beginning-of-line and end-of-file. */
public const int NUM_PSEUDO=2;
public int BOL; // beginning-of-line
public int EOF; // end-of-file

/* NFA character class minimization map. */
public int[] ccls_map;

/* Regular expression token variables. */
public int current_token;
public char lexeme;
public bool in_quote;
public bool in_ccl;

/* Verbose execution flag. */
public bool verbose;

/* directives flags. */
public bool integer_type;
public bool intwrap_type;
public bool yyeof;
public bool count_chars;
public bool count_lines;
public bool cup_compatible;
public bool lex_public;
public bool ignorecase;

public String init_code;
public String class_code;
public String eof_code;
public String eof_value_code;

/* Class, function, type names. */
public String class_name = "Yylex";
public String implements_name;
public String function_name = "yylex";
public String type_name = "Yytoken";
public String namespace_name = "YyNameSpace";

/*
 * Constants
 */
public const int NONE = 0;
public const int START = 1;
public const int END = 2;


class StrHCode : IHashCodeProvider
{
/*
 * Provides a hashkey for string, for use with Hashtable.
 *
 * The First 100,008 Primes
 * (the 10,000th is 104,729)
 * (the 100,008th is 1,299,827)
 * For more information on primes see http://www.utm.edu/research/primes
 */
const int prime = 1299827;

public int GetHashCode(Object o)
  {
  if (o.GetType() != Type.GetType("System.String"))
    throw new ApplicationException("Argument must be a String, found ["
			     + o.GetType().ToString()+"]");
  String s = (String) o;
  int h = prime;
  for (int i = 0; i < s.Length; i++)
    {
    Char c = s[i];
    h ^= Convert.ToInt32(c);
    }
  return h%prime;
  }
  }

class StrHComp : IComparer
{
/*
 * Provides a compare function for a String, for use with Hashtable.
 */
public int Compare(Object o1, Object o2)
  {
  if (o1.GetType() != Type.GetType("System.String") ||
      o2.GetType() != Type.GetType("System.String"))
    throw new ApplicationException("Argument must be a String");

  String s1 = (String) o1;
  String s2 = (String) o2;
  return (String.Compare(s1, s2));
  }
}


/*
 * Function: Spec
 * Description: Constructor.
 */
public Spec()
  {

  /* Initialize regular expression token variables. */
  current_token = Gen.EOS;
  lexeme = '\0';
  in_quote = false;
  in_ccl = false;

  /* Initialize hashtable for lexer states. */
  states = new Hashtable(new StrHCode(), new StrHComp());
  states["YYINITIAL"] = (int) states.Count;

  /* Initialize hashtable for lexical macros. */
  macros = new Hashtable(new StrHCode(), new StrHComp());

  /* Initialize variables for lexer options. */
  integer_type = false;
  intwrap_type = false;
  count_lines = false;
  count_chars = false;
  cup_compatible = false;
  lex_public = false;
  yyeof = false;
  ignorecase = false;

  /* Initialize variables for Lex runtime options. */
  verbose = true;

  nfa_start = null;
  nfa_states = new ArrayList();

  dfa_states = new ArrayList();

  dfa_sets = new Hashtable();	// uses BitSet

  dtrans_list = new ArrayList();
  dtrans_ncols = Utility.MAX_SEVEN_BIT + 1;
  row_map = null;
  col_map = null;

  accept_list = null;
  anchor_array = null;

  init_code = null;
  class_code = null;
  eof_code = null;
  eof_value_code = null;

  state_dtrans = null;

  state_rules = null;
  }


private int unmarked_dfa;

public void InitUnmarkedDFA()
  {
  unmarked_dfa = 0;
  }

/*
 * Function: GetNextUnmarkedDFA
 * Description: Returns next unmarked DFA state from spec
 */
public Dfa GetNextUnmarkedDFA()
  {
  int size;
  Dfa dfa;

  size = dfa_states.Count;
  while (unmarked_dfa < size)
    {
    dfa = (Dfa) dfa_states[unmarked_dfa];

    if (!dfa.IsMarked())
      {
#if OLD_DUMP_DEBUG
      Console.Write("*");

      Console.WriteLine("---------------");
      Console.Write("working on DFA state " 
		    + unmarked_dfa
		    + " = NFA states: ");
      Nfa2Dfa.Print_Set(dfa.GetNFASet());
      Console.WriteLine("");
#endif
      return dfa;
      }
    unmarked_dfa++;
    }
  return null;
  }
}
}
