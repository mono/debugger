namespace Lex
{
/*
 * Class: Alloc
 */
public class Alloc
{
/*
 * Function: NewCDfa
 */
public static Dfa NewDfa(Spec spec)
  {
  Dfa dfa;

  dfa = new Dfa(spec.dfa_states.Count);
  spec.dfa_states.Add(dfa);

  return dfa;
  }

/*
 * Function: NewNfaPair
 */
public static NfaPair NewNfaPair()
  {
  NfaPair pair = new NfaPair();
  return pair;
  }

/*
 * Function: NewNfa
 */
public static Nfa NewNfa(Spec spec)
  {
  Nfa p;

  /* UNDONE: Buffer this? */
  p = new Nfa();

  /*p.label = spec.nfa_states.size();*/
  spec.nfa_states.Add(p);
  p.SetEdge(Nfa.EPSILON);

  return p;
  }

/**
 * NewNLPair
 * return a new NfaPair that matches a new line
 * (\r\n?|\n)
 */
public static NfaPair NewNLPair(Spec spec)
  {
  NfaPair pair = NewNfaPair();
  pair.end = NewNfa(spec);	// newline accepting state
  pair.start = NewNfa(spec);	// new state with two epsilon edges

  Nfa pstart = pair.start;
  pstart.SetNext(NewNfa(spec));

  Nfa pstartnext = pstart.GetNext();
  pstartnext.SetEdge(Nfa.CCL);
  pstartnext.SetCharSet(new CharSet());
  pstartnext.GetCharSet().add('\n');
  pstartnext.SetNext(pair.end); // accept '\n'

  pstart.SetSib(NewNfa(spec));
  Nfa pstartsib = pstart.GetSib();
  pstartsib.SetEdge('\r');

  pstartsib.SetNext(NewNfa(spec));
  Nfa pstartsibnext = pstartsib.GetNext();
  pstartsibnext.SetNext(null); // do NOT accept just '\r'

  pstartsibnext.SetSib(NewNfa(spec));
  pstartsibnext.GetSib().SetEdge('\n');
  pstartsibnext.GetSib().SetNext(pair.end); // accept '\r\n'

  return pair;
  }
}
}
