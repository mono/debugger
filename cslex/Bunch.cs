namespace Lex
{
/*
 * Class: Bunch
 */
using System;
using System.Collections;
using BitSet;

public class Bunch
{
/*
 * Member Variables
 */
ArrayList nfa_set; /* List of Nfa states in dfa state */
BitSet nfa_bit; /* BitSet representation of Nfa labels */
Accept accept;	 /* Accepting actions, or null if nonaccepting state */
int anchor;	 /* Anchors on regular expression */
int accept_index; /* Nfa index corresponding to accepting actions */

public ArrayList GetNFASet() { return nfa_set; }
public void SetNFASet(ArrayList a) { nfa_set = a; }
public BitSet GetNFABit() { return nfa_bit; }
public void SetNFABit(BitSet b) { nfa_bit = b; }
public Accept GetAccept() { return accept; }
public void SetAccept(Accept a) { accept = a; }
public int GetAnchor() { return anchor; }
public void SetAnchor(int a) { anchor = a; }
public int GetIndex() { return accept_index; }
public void SetIndex(int i) { accept_index = i; }

/*
 * Function: Bunch
 * Description: Constructor.
 */
public Bunch(ArrayList nfa_start_states)
  {
  int size = nfa_start_states.Count;
  nfa_set = new ArrayList(nfa_start_states);
  nfa_bit = new BitSet(size);
  accept = null;
  anchor = Spec.NONE;

  /* Initialize bit set. */
  for (int i = 0; i < size; i++)
    {
    int label = ((Nfa) nfa_set[i]).GetLabel();
    nfa_bit.Set(label, true);
    }

  accept_index = Utility.INT_MAX;
  }

public void dump()
  {
#if DUMMY
  Console.WriteLine("[CBunch Dump Begin]");
  if (nfa_set == null)
    Console.WriteLine("nfa_set=null");
  else
    {
    int n1 = nfa_set.Count;
    for (int i = 0; i < n1; i++)
      {
      Object o2 = nfa_set[i];
      Console.Write("i="+Int32.ToString(i)+" elem=");
      if (o2 == null)
	Console.WriteLine("null");
      else
	{
	CNfa elem = (CNfa) o2;
	elem.dump();
	}
      }
    }
  if (nfa_bit == null)
    Console.WriteLine("nfa_bit=null");
  else
    {
    Console.Write("nfa_bit("+Int32.ToString(nfa_bit.GetLength())+")=");
    for (int i = 0; i < nfa_bit.GetLength(); i++)
      if (nfa_bit.Get(i))
	Console.Write("1");
      else
	Console.Write("0");
    Console.WriteLine("");
    }
  if (accept == null)
    Console.WriteLine("accept=null");
  else
    accept.dump();
  Console.WriteLine("anchor="+Int32.ToString(anchor));
  Console.WriteLine("accept_index="+Int32.ToString(accept_index));
#endif
  }

public bool IsEmpty()
  {
  return (nfa_set == null);
  }

/*
 * Function: e_closure
 * Description: Alters input set.
 */
public void e_closure()
  {
  Nfa state = null;

  /*
   * Debug checks
   */
#if DEBUG
  Utility.assert(null != nfa_set);
  Utility.assert(null != nfa_bit);
#endif

  accept = null;
  anchor = Spec.NONE;
  accept_index = Utility.INT_MAX;

  /*
   * Create initial stack.
   */
  Stack nfa_stack = new Stack();
  int size = nfa_set.Count;

  for (int i = 0; i < size; i++)
    {
    state = (Nfa) nfa_set[i];
#if DEBUG
    Utility.assert(nfa_bit.Get(state.GetLabel()));
#endif
    nfa_stack.Push(state);
    }

  /*
   * Main loop.
   */
  while (nfa_stack.Count > 0)
    {
    Object o = nfa_stack.Pop();
    if (o == null)
      break;
    state = (Nfa) o;

#if OLD_DUMP_DEBUG
    if (null != state.GetAccept())
      {
      Console.WriteLine("Looking at accepting state "
			+ state.GetLabel()
			+ " with <"
			+ state.GetAccept().action
			+ ">");
      }
#endif
    if (null != state.GetAccept() && state.GetLabel() < accept_index)
      {
      accept_index = state.GetLabel();
      accept = state.GetAccept();
      anchor = state.GetAnchor();

#if OLD_DUMP_DEBUG
      Console.WriteLine("Found accepting state "
			+ state.GetLabel()
			+ " with <"
			+ state.GetAccept().action
			+ ">");
#endif
#if DEBUG
      Utility.assert(null != accept);
      Utility.assert(Spec.NONE == anchor
		      || 0 != (anchor & Spec.END)
		      || 0 != (anchor & Spec.START));
#endif
      }

    if (Nfa.EPSILON == state.GetEdge())
      {
      if (state.GetNext() != null)
	{
	if (false == nfa_set.Contains(state.GetNext()))
	  {
#if DEBUG
	  Utility.assert(false == nfa_bit.Get(state.GetNext().GetLabel()));
#endif
	  nfa_bit.Set(state.GetNext().GetLabel(), true);
	  nfa_set.Add(state.GetNext());
	  nfa_stack.Push(state.GetNext());
	  }
	}
      if (null != state.GetSib())
	{
	if (false == nfa_set.Contains(state.GetSib()))
	  {
#if DEBUG
	  Utility.assert(false == nfa_bit.Get(state.GetSib().GetLabel()));
#endif
	  nfa_bit.Set(state.GetSib().GetLabel(), true);
	  nfa_set.Add(state.GetSib());
	  nfa_stack.Push(state.GetSib());
	  }
	}
      }
    }
  if (null != nfa_set)
    sort_states();
  }


private class NfaComp : IComparer
  {
  public int Compare(Object x, Object y)
    {
    Nfa a = (Nfa) x;
    Nfa b = (Nfa) y;
    return a.GetLabel() - b.GetLabel();
    }
  }

/*
 * Function: sort_states
 */
public void sort_states()
  {
  nfa_set.Sort(0, nfa_set.Count, null);
  //nfa_set.Sort(0, nfa_set.Count, new NfaComp());
#if OLD_DEBUG
  Console.Write("NFA vector indices: ");  
	    
  for (int index = 0; index < nfa_set.Count; index++)
    {
    Nfa elem = (Nfa) nfa_set[index];
    Console.Write(elem.GetLabel() + " ");
    }
  Console.Write("\n");
#endif
  return;
  }
 
/*
 * Function: move
 */
public void move(Dfa dfa, int b)
  {
  int size;
  Nfa state;
  ArrayList old_set = dfa.GetNFASet();

  nfa_set = null;
  nfa_bit = null;

  size = old_set.Count;
  for (int index = 0; index < size; index++)
    {
    state = (Nfa) old_set[index];

    if (b == state.GetEdge()
	|| (Nfa.CCL == state.GetEdge() && state.GetCharSet().contains(b)))
      {
      if (nfa_set == null)
	{
	nfa_set = new ArrayList();
	nfa_bit = new BitSet();
	}
      nfa_set.Add(state.GetNext());
#if OLD_DEBUG
      Console.WriteLine("Size of bitset: " + nfa_bit.GetLength());
      Console.WriteLine("Reference index: " + state.GetNext().GetLabel());
#endif
      nfa_bit.Set(state.GetNext().GetLabel(), true);
      }
    }

  if (nfa_set != null)
    {
#if DEBUG
    Utility.assert(null != nfa_bit);
#endif
    sort_states();
    }
  return;
  }
}
}
