namespace Lex
{
/*
 * Class: MakeNfa
 */
using System;
using System.Collections;
using BitSet;

public class MakeNfa
{
/*
 * Member Variables
 */
private static Spec spec;
private static Gen gen;
private static Input input;

/*
 * Expands character class to include special BOL and
 * EOF characters.  Puts numeric index of these characters in
 * input Spec.
 */
public static void Allocate_BOL_EOF(Spec s)
  {
#if DEBUG
  Utility.assert(Spec.NUM_PSEUDO==2);
#endif
  s.BOL = s.dtrans_ncols++;
  s.EOF = s.dtrans_ncols++;
  }

/*
 * Function: CreateMachine
 * Description: High level access function to module.
 * Deposits result in input Spec.
 */
public static void CreateMachine(Gen cmg, Spec cms, Input cmi)
  {
  int i;
  Nfa elem;
  int size;

  spec = cms;
  gen = cmg;
  input = cmi;

  size = spec.states.Count;
  spec.state_rules = new ArrayList[size];
  for (i = 0; i < size; ++i)
    {
    spec.state_rules[i] = new ArrayList();
    }

  /*
   * Initialize current token variable and create nfa.
   */

  spec.nfa_start = machine();
	  
  /* Set labels in created nfa machine. */
  size = spec.nfa_states.Count;
  for (i = 0; i < size; ++i)
    {
    elem = (Nfa) spec.nfa_states[i];
    elem.SetLabel(i);
    }

  /* Debugging output. */
#if DO_DEBUG
  gen.print_nfa();
#endif

  if (spec.verbose)
    {
    Console.WriteLine("NFA comprised of "
		      + (spec.nfa_states.Count + 1) 
		      + " states.");
    }
  }
     
/*
 * Function: discardCNfa
 */
private static void discardNfa(Nfa nfa)
  {
  spec.nfa_states.Remove(nfa);
  }

/*
 * Function: ProcessStates
 */
private static void ProcessStates(BitSet bset, Nfa current)
  {
  foreach (int rule in bset)
    {
    ArrayList p = spec.state_rules[rule];
#if DEBUG
    Utility.assert(p != null);
#endif
    p.Add(current);
    }
  }

/*
 * Function: machine
 * Description: Recursive descent regular expression parser.
 */
private static Nfa machine()
  {
  Nfa start;
  Nfa p;
  BitSet states;

#if DESCENT_DEBUG
  Utility.enter("machine",spec.lexeme,spec.current_token);
#endif

  start = Alloc.NewNfa(spec);
  p = start;

  states = gen.GetStates();

  /* Begin: Added for states. */
  spec.current_token = Gen.EOS;
  gen.Advance();
  /* End: Added for states. */

  if (Gen.END_OF_INPUT != spec.current_token)
    {
    p.SetNext(rule());
    ProcessStates(states,p.GetNext());
    }

  while (Gen.END_OF_INPUT != spec.current_token)
    {
    /* Make state changes HERE. */
    states = gen.GetStates();
	
    /* Begin: Added for states. */
    gen.Advance();
    if (Gen.END_OF_INPUT == spec.current_token)
      break;
    /* End: Added for states. */
	    
    p.SetSib(Alloc.NewNfa(spec));
    p = p.GetSib();
    p.SetNext(rule());

    ProcessStates(states,p.GetNext());
    }

  /*
   * add pseudo-rules for BOL and EOF
   */
  p.SetSib(Alloc.NewNfa(spec));
  p = p.GetSib();
  p.SetNext(Alloc.NewNfa(spec));
  Nfa pnext = p.GetNext();
  pnext.SetEdge(Nfa.CCL);
  pnext.SetNext(Alloc.NewNfa(spec));
  pnext.SetCharSet(new CharSet());
  pnext.GetCharSet().add(spec.BOL);
  pnext.GetCharSet().add(spec.EOF);

  // do-nothing accept rule
  pnext.GetNext().SetAccept(new Accept(null, input.line_number+1));

  /* add the pseudo rules */
  for (int i=0; i < spec.states.Count; i++)
    {
    ArrayList srule = spec.state_rules[i];
    srule.Add(pnext);
    }

#if DESCENT_DEBUG
  Utility.leave("machine",spec.lexeme,spec.current_token);
#endif

  return start;
  }
  
/*
 * Function: rule
 * Description: Recursive descent regular expression parser.
 */
private static Nfa rule()
  {
  NfaPair pair; 
  Nfa start = null;
  Nfa end = null;
  int anchor = Spec.NONE;

#if DESCENT_DEBUG
  Utility.enter("rule", spec.lexeme, spec.current_token);
#endif

  pair = Alloc.NewNfaPair();

  if (Gen.AT_BOL == spec.current_token)
    {
    anchor = anchor | Spec.START;
    gen.Advance();
    expr(pair);

    start = Alloc.NewNfa(spec);
    start.SetEdge(spec.BOL);
    start.SetNext(pair.start);
    end = pair.end;
    }
  else
    {
    expr(pair);
    start = pair.start;
    end = pair.end;
    }

  if (Gen.AT_EOL == spec.current_token)
    {
    gen.Advance();

    NfaPair nlpair = Alloc.NewNLPair(spec);
    end.SetNext(Alloc.NewNfa(spec));
    Nfa enext = end.GetNext();
    enext.SetNext(nlpair.start);
    enext.SetSib(Alloc.NewNfa(spec));
    enext.GetSib().SetEdge(spec.EOF);
    enext.GetSib().SetNext(nlpair.end);
    end = nlpair.end;

    anchor = anchor | Spec.END;
    }

  /* check for null rules */
  if (end == null)
    Error.parse_error(Error.E_ZERO, input.line_number);

  /* Handle end of regular expression */
  end.SetAccept(gen.packAccept());
  end.SetAnchor(anchor);

#if DESCENT_DEBUG
  Utility.leave("rule",spec.lexeme,spec.current_token);
#endif
  return start;
  }
	    
/*
 * Function: expr
 * Description: Recursive descent regular expression parser.
 */
private static void expr(NfaPair pair)
  {
  NfaPair e2_pair;
  Nfa p;

#if DESCENT_DEBUG
  Utility.enter("expr",spec.lexeme,spec.current_token);
#endif

#if DEBUG
  Utility.assert(null != pair);
#endif

  e2_pair = Alloc.NewNfaPair();

  cat_expr(pair);
	
  while (Gen.OR == spec.current_token)
    {
    gen.Advance();
    cat_expr(e2_pair);

    p = Alloc.NewNfa(spec);
    p.SetSib(e2_pair.start);
    p.SetNext(pair.start);
    pair.start = p;

    p = Alloc.NewNfa(spec);
    pair.end.SetNext(p);
    e2_pair.end.SetNext(p);
    pair.end = p;
    }

#if DESCENT_DEBUG
  Utility.leave("expr",spec.lexeme,spec.current_token);
#endif
  }
	    
/*
 * Function: cat_expr
 * Description: Recursive descent regular expression parser.
 */
private static void cat_expr(NfaPair pair)
  {
  NfaPair e2_pair;

#if DESCENT_DEBUG
  Utility.enter("cat_expr",spec.lexeme,spec.current_token);
#endif

#if DEBUG
  Utility.assert(null != pair);
#endif
	
  e2_pair = Alloc.NewNfaPair();
	
  if (first_in_cat(spec.current_token))
    {
    factor(pair);
    }

  while (first_in_cat(spec.current_token))
    {
    factor(e2_pair);

    /* Destroy */
    pair.end.mimic(e2_pair.start);
    discardNfa(e2_pair.start);

    pair.end = e2_pair.end;
    }

#if DESCENT_DEBUG
  Utility.leave("cat_expr",spec.lexeme,spec.current_token);
#endif
  }
  
/*
 * Function: first_in_cat
 * Description: Recursive descent regular expression parser.
 */
private static bool first_in_cat(int token)
  {
  if (token == Gen.CLOSE_PAREN || token == Gen.AT_EOL
      || token == Gen.OR || token == Gen.EOS)
    return false;
  if (token == Gen.CLOSURE || token == Gen.PLUS_CLOSE
      || token == Gen.OPTIONAL)
    {
    Error.parse_error(Error.E_CLOSE,input.line_number);
    return false;
    }
  if (token == Gen.CCL_END)
    {
    Error.parse_error(Error.E_BRACKET,input.line_number);
    return false;
    }
  if (token == Gen.AT_BOL)
    {
    Error.parse_error(Error.E_BOL,input.line_number);
    return false;
    }
  return true;
  }

/*
 * Function: factor
 * Description: Recursive descent regular expression parser.
 */
private static void factor(NfaPair pair)
  {
  Nfa start = null;
  Nfa end = null;

#if DESCENT_DEBUG
  Utility.enter("factor",spec.lexeme,spec.current_token);
#endif

  term(pair);

  if (Gen.CLOSURE == spec.current_token
      || Gen.PLUS_CLOSE == spec.current_token
      || Gen.OPTIONAL == spec.current_token)
    {
    start = Alloc.NewNfa(spec);
    end = Alloc.NewNfa(spec);
	    
    start.SetNext(pair.start);
    pair.end.SetNext(end);

    if (Gen.CLOSURE == spec.current_token
	|| Gen.OPTIONAL == spec.current_token)
      {
      start.SetSib(end);
      }

    if (Gen.CLOSURE == spec.current_token
	|| Gen.PLUS_CLOSE == spec.current_token)
      {
      pair.end.SetSib(pair.start);
      }

    pair.start = start;
    pair.end = end;
    gen.Advance();
    }

#if DESCENT_DEBUG
  Utility.leave("factor",spec.lexeme,spec.current_token);
#endif
  }
      
/*
 * Function: term
 * Description: Recursive descent regular expression parser.
 */
private static void term(NfaPair pair)
  {
  Nfa start;
  bool isAlphaL;

#if DESCENT_DEBUG
  Utility.enter("term",spec.lexeme,spec.current_token);
#endif

  if (Gen.OPEN_PAREN == spec.current_token)
    {
    gen.Advance();
    expr(pair);

    if (Gen.CLOSE_PAREN == spec.current_token)
      {
      gen.Advance();
      }
    else
      {
      Error.parse_error(Error.E_SYNTAX,input.line_number);
      }
    }
  else
    {
    start = Alloc.NewNfa(spec);
    pair.start = start;

    start.SetNext(Alloc.NewNfa(spec));
    pair.end = start.GetNext();

    if (Gen.L == spec.current_token && Char.IsLetter(spec.lexeme)) 
      {
      isAlphaL = true;
      } 
    else 
      {
      isAlphaL = false;
      }
    if (false == (Gen.ANY == spec.current_token
		  || Gen.CCL_START == spec.current_token
		  || (spec.ignorecase && isAlphaL)))
      {
      start.SetEdge(spec.lexeme);
      gen.Advance();
      }
    else
      {
      start.SetEdge(Nfa.CCL);
      start.SetCharSet(new CharSet());
      CharSet cset = start.GetCharSet();

      /* Match case-insensitive letters using character class. */
      if (spec.ignorecase && isAlphaL) 
	{
	cset.addncase(spec.lexeme);
	}
      /* Match dot (.) using character class. */
      else if (Gen.ANY == spec.current_token)
	{
	cset.add('\n');
	cset.add('\r');
	/* exclude BOL and EOF from character classes */
	cset.add(spec.BOL);
	cset.add(spec.EOF);
	cset.complement();
	}
      else
	{
	gen.Advance();
	if (Gen.AT_BOL == spec.current_token)
	  {
	  gen.Advance();
	  /* exclude BOL and EOF from character classes */
	  cset.add(spec.BOL);
	  cset.add(spec.EOF);
	  cset.complement();
	  }
	if (!(Gen.CCL_END == spec.current_token))
	  {
	  dodash(cset);
	  }
	}
      gen.Advance();
      }
    }

#if DESCENT_DEBUG
  Utility.leave("term",spec.lexeme,spec.current_token);
#endif
  }

/*
 * Function: dodash
 * Description: Recursive descent regular expression parser.
 */
private static void dodash(CharSet set)
  {
  int first = -1;

#if DESCENT_DEBUG
  Utility.enter("dodash",spec.lexeme,spec.current_token);
#endif

  while (Gen.EOS != spec.current_token 
	 && Gen.CCL_END != spec.current_token)
    {
    // DASH loses its special meaning if it is first in class.
    if (Gen.DASH == spec.current_token && -1 != first)
      {
      gen.Advance();
      // DASH loses its special meaning if it is last in class.
      if (spec.current_token == Gen.CCL_END)
	{
	// 'first' already in set.
	set.add('-');
	break;
	}
      for ( ; first <= spec.lexeme; ++first)
	{
	if (spec.ignorecase) 
	  set.addncase((char)first);
	else
	  set.add(first);
	}
      }
    else
      {
      first = spec.lexeme;
      if (spec.ignorecase)
	set.addncase(spec.lexeme);
      else
	set.add(spec.lexeme);
      }

    gen.Advance();
    }

#if DESCENT_DEBUG
  Utility.leave("dodash",spec.lexeme,spec.current_token);
#endif
  }
}
}
