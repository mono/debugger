namespace Lex
{
/*
 * Class: Nfa
 */
using System;
using System.Collections;
using BitSet;

public class Nfa : IComparable
{
/*
 * Member Variables
 */
int edge;		/* Label for edge type:
				   character code, 
				   CCL (character class), 
				   [STATE,
				   SCL (state class),]
				   EMPTY, 
				   EPSILON. */

CharSet cset;		/* Set to store character classes. */
Nfa next;		/* Next state (or null if none). */
Nfa next2;		/* Another state with type == EPSILON
			   and null if not used.  
			   The NFA construction should result in two
			   outgoing edges only if both are EPSILON
			   edges. */

Accept accept;		/* Set to null if nonaccepting state. */
int anchor;		/* Says if and where pattern is anchored. */
int label;
BitSet states;

/*
 * Constants
 */
public const int NO_LABEL = -1;

/*
 * Constants: Edge Types
 * Note: Edge transitions on one specific character
 * are labelled with the character Ascii (Unicode)
 * codes.  So none of the constants below should
 * overlap with the natural character codes.
 */
public const int CCL = -1;
public const int EMPTY = -2;
public const int EPSILON = -3;
   
/*
 * Function: Nfa
 */
public Nfa()
  {
  edge = EMPTY;
  cset = null;
  next = null;
  next2 = null;
  accept = null;
  anchor = Spec.NONE;
  label = NO_LABEL;
  states = null;
  }

public void dump()
  {
  Console.WriteLine("[Nfa begin dump]");
  Console.WriteLine("label="+label);
  Console.WriteLine("edge="+edge);
  Console.Write("set=");
  if (cset == null)
    Console.WriteLine("null");
  else
    Console.WriteLine(cset);
  Console.Write("next=");
  if (next == null)
    Console.WriteLine("null");
  else
    Console.WriteLine(next);
  Console.Write("next2=");
  if (next2 == null)
    Console.WriteLine("null");
  else
    Console.WriteLine(next2);
  Console.Write("accept=");
  if (accept == null)
    Console.WriteLine("null");
  else
    accept.dump();
  Console.WriteLine("anchor="+anchor);
  Console.Write("states=");
  if (states == null)
    Console.WriteLine("null");
  else
    {
    for (int i=0; i < states.GetLength(); i++)
      if (states.Get(i))
	Console.Write("1");
      else
	Console.Write("0");
    Console.WriteLine("");
    }
  Console.WriteLine("[Nfa end dump]");
  }

/*
 * Function: mimic
 * Description: Converts this NFA state into a copy of
 * the input one.
 */
public void mimic(Nfa nfa)
  {
  edge = nfa.edge;
	
  if (null != nfa.cset)
    {
    if (null == cset)
      {
      cset = new CharSet();
      }
    cset.mimic(nfa.cset);
    }
  else
    {
    cset = null;
    }

  next = nfa.next;
  next2 = nfa.next2;
  accept = nfa.accept;
  anchor = nfa.anchor;

  if (null != nfa.states)
    {
    states = new BitSet(nfa.states);
    }
  else
    {
    states = null;
    }
  }

public int CompareTo(Object y)
  {
  return label - ((Nfa) y).label;
  }

public int GetLabel()
  {
  return label;
  }

public void SetLabel(int i)
  {
  label = i;
  }

public Nfa GetNext() { return next; }
public void SetNext(Nfa x) { next = x; }

public Nfa GetSib() { return next2; }
public void SetSib(Nfa x) { next2 = x; }

public int GetEdge() { return edge; }
public void SetEdge(int i) { edge = i; }

public CharSet GetCharSet() { return cset; }
public void SetCharSet(CharSet s) { cset = s; }
  
 public int GetAnchor() { return anchor; }
 public void SetAnchor(int i) { anchor = i; }

 public Accept GetAccept() { return accept; }
 public void SetAccept(Accept a) { accept = a; }

 public BitSet GetStates() { return states; }
 public void SetStates(BitSet b) { states = b; }
 
}
}
