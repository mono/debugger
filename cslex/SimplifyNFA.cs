namespace Lex
{
/*
 * Class SimplifyNFA
 * Extract character classes from the NFA and simplify.
 */
using System;
using System.Collections;
using BitSet;

class SimplifyNfa
{
static private int[] ccls;		// character class mapping.
static private int original_charset_size; // original charset size
static private int mapped_charset_size; // reduced charset size

static internal void simplify(Spec spec)
  {
  computeClasses(spec);	// initialize fields.
  /* 
   * now rewrite the NFA using our character class mapping.
   */
  for (int i = 0; i < spec.nfa_states.Count; i++)
    {
    Nfa nfa = (Nfa) spec.nfa_states[i];
    if (nfa.GetEdge() == Nfa.EMPTY || nfa.GetEdge() == Nfa.EPSILON)
      continue;			// no change.
    if (nfa.GetEdge() == Nfa.CCL)
      {
      CharSet nset = new CharSet();
      nset.map(nfa.GetCharSet(), ccls); // map it.
      nfa.SetCharSet(nset);
      }
    else
      {				// single character
      nfa.SetEdge(ccls[nfa.GetEdge()]); // map it.
      }
    }
  /*
   * now update spec with the mapping.
   */
  spec.ccls_map = ccls;
  spec.dtrans_ncols = mapped_charset_size;
  }

/*
 * Compute minimum set of character classes needed to disambiguate
 * edges.  We optimistically assume that every character belongs to
 * a single character class, and then incrementally split classes
 * as we see edges that require discrimination between characters in
 * the class.
 */
static private void computeClasses(Spec spec)
  {
  original_charset_size = spec.dtrans_ncols;
  ccls = new int[original_charset_size]; // initially all zero.

  int nextcls = 1;
  BitSet clsA = new BitSet();
  BitSet clsB = new BitSet();
  Hashtable h = new Hashtable();
    
  Console.WriteLine("Working on character classes.");
  for (int index = 0; index < spec.nfa_states.Count; index++)
    {
    Nfa nfa = (Nfa) spec.nfa_states[index];
    if (nfa.GetEdge() == Nfa.EMPTY || nfa.GetEdge() == Nfa.EPSILON)
      continue;			// no discriminatory information.
    clsA.ClearAll();
    clsB.ClearAll();
    for (int i=0; i < ccls.Length; i++)
      {
      if (nfa.GetEdge() == i ||	// edge labeled with a character
	  nfa.GetEdge() == Nfa.CCL
	  && nfa.GetCharSet().contains(i)) // set of characters
	  clsA.Set(ccls[i], true);
      else
	  clsB.Set(ccls[i], true);
      }
    /*
     * now figure out which character classes we need to split.
     */
    clsA.And(clsB);  // split the classes which show up on both sides of edge
    if (clsA.GetLength() == 0)
      {
      Console.Write(".");
      continue;
      }
    Console.Write(":");

    /*
     * and split them.
     */
    h.Clear(); // h will map old to new class name
    for (int i=0; i < ccls.Length; i++)
      {
      if (clsA.Get(ccls[i])) // a split class
	{
	if (nfa.GetEdge() == i ||
	    nfa.GetEdge() == Nfa.CCL
	    && nfa.GetCharSet().contains(i))
	  { // on A side
	  int split = ccls[i];
	  if (!h.ContainsKey(split))
	    {
	    h.Add(split, nextcls++); // make new class
#if DEBUG
	    Console.WriteLine("Adding char "+(nextcls-1)+" split="+split+" i="+i);
#endif
	    }
	  ccls[i] = (int) h[split];
	  }
	}
      }
    }
  Console.WriteLine();
  Console.WriteLine("NFA has "+nextcls+" distinct character classes.");
  mapped_charset_size = nextcls;
  }
}
}
