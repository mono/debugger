namespace Lex
{
/*
 * Class: Nfa2Dfa
 */
using System;
using System.Text;
using System.Collections;
using BitSet;

class Nfa2Dfa
{
/*
 * Constants
 */
private const int NOT_IN_DSTATES = -1;

/*
 * Function: make_dfa
 * Description: High-level access function to module.
 */
//public void make_dfa(Gen l, Spec s)
public static void MakeDFA(Spec s)
  {
  make_dtrans(s);
  free_nfa_states(s);
#if OLD_DUMP_DEBUG
  Console.WriteLine(s.dfa_states.Count
		    + " DFA states in original machine.");
#endif
  free_dfa_states(s);
  }     

/*
 * Function: make_dtrans
 * Description: Creates uncompressed CDTrans transition table.
 */
//private void make_dtrans()
private static void make_dtrans(Spec s)
  {
  Dfa dfa;
  int nextstate;

  Console.WriteLine("Working on DFA states.");

  /* Reference passing type and initializations. */
  s.InitUnmarkedDFA();

  /* Allocate mapping array. */
  int nstates = s.state_rules.Length;
  s.state_dtrans = new int[nstates];

  for (int istate = 0; istate < nstates; istate++)
    {
    /* Create start state and initialize fields. */

    Bunch bunch = new Bunch(s.state_rules[istate]);

    bunch.e_closure();
    add_to_dstates(s, bunch);

    s.state_dtrans[istate] = s.dtrans_list.Count;

    /* Main loop of DTrans creation. */
    while (null != (dfa = s.GetNextUnmarkedDFA()))
      {
      Console.Write(".");
#if DEBUG
      Utility.assert(!dfa.IsMarked());
#endif
      /* Get first unmarked node, then mark it. */
      dfa.SetMarked();

      /* Allocate new DTrans, then initialize fields. */
      DTrans dt = new DTrans(s, dfa);

      /* Set dt array for each character transition. */
      for (int i = 0; i < s.dtrans_ncols; i++)
	{
	/* Create new dfa set by attempting character transition. */
	bunch.move(dfa, i);
	if (!bunch.IsEmpty())
	  bunch.e_closure();
#if DEBUG
	Utility.assert((null == bunch.GetNFASet() 
			 && null == bunch.GetNFABit())
			|| (null != bunch.GetNFASet() 
			    && null != bunch.GetNFABit()));
#endif
	/* Create new state or set state to empty. */
	if (bunch.IsEmpty())
	  {
	  nextstate = DTrans.F;
	  }
	else 
	  {
	  nextstate = in_dstates(s, bunch);

	  if (nextstate == NOT_IN_DSTATES)
	    nextstate = add_to_dstates(s, bunch);
	  }
#if DEBUG
	Utility.assert(nextstate < s.dfa_states.Count);
#endif
	dt.SetDTrans(i, nextstate);
	}
#if DEBUG
      Utility.assert(s.dtrans_list.Count == dfa.GetLabel());
#endif
#if DEBUG
      StringBuilder sb1 = new StringBuilder(Lex.MAXSTR);
      sb1.Append("Current count = "+s.dtrans_list.Count+"\n");
      for (int i1 = 0; i1 < dt.GetDTransLength(); i1++)
	sb1.Append(dt.GetDTrans(i1)+",");
      sb1.Append("end\n");
      Console.Write(sb1.ToString());
#endif
      s.dtrans_list.Add(dt);
      }
    }
  Console.WriteLine("");
  }

/*
 * Function: free_dfa_states
 */
//private void free_dfa_states()
private static void free_dfa_states(Spec s)
  {
  s.dfa_states = null;
  s.dfa_sets = null;
  }

/*
 * Function: free_nfa_states
 */
private static void free_nfa_states(Spec s)
  {
  /* UNDONE: Remove references to nfas from within dfas. */
  /* UNDONE: Don't free CAccepts. */
  s.nfa_states = null;
  s.nfa_start = null;
  s.state_rules = null;
  }

/*
 * function: add_to_dstates
 * Description: Takes as input a CBunch with details of
 * a dfa state that needs to be created.
 * 1) Allocates a new dfa state and saves it in the appropriate Spec list
 * 2) Initializes the fields of the dfa state with the information in the CBunch.
 * 3) Returns index of new dfa.
 */
private static int add_to_dstates(Spec s, Bunch bunch)
  {
  Dfa dfa;

#if DEBUG
  Utility.assert(null != bunch.GetNFASet());
  Utility.assert(null != bunch.GetNFABit());
  Utility.assert(null != bunch.GetAccept() || Spec.NONE == bunch.GetAnchor());
#endif

  /* Allocate, passing Spec so dfa label can be set. */
  dfa = Alloc.NewDfa(s);

  /* Initialize fields, including the mark field. */
  dfa.SetNFASet(new ArrayList(bunch.GetNFASet()));
  dfa.SetNFABit(new BitSet(bunch.GetNFABit()));
  dfa.SetAccept(bunch.GetAccept());
  dfa.SetAnchor(bunch.GetAnchor());
  dfa.ClearMarked();

#if OLD_DUMP_DEBUG
  Console.WriteLine("[Created new dfa_state #"+dfa.GetLabel()+"]");
  dfa.dump();
#endif

  /* Register dfa state using BitSet in spec Hashtable. */
  s.dfa_sets[dfa.GetNFABit()] = dfa;

#if OLD_DUMP_DEBUG
  Console.Write("Registering set : ");
  Print_Set(dfa.GetNFASet());
  Console.WriteLine("");
#endif

  return dfa.GetLabel();
  }

/*
 * Function: in_dstates
 */
private static int in_dstates(Spec s, Bunch bunch)
  {
  Dfa dfa;

#if OLD_DEBUG
  Console.Write("Looking for set : ");
  Print_Set(bunch.GetNFASet());
  bunch.dump();
#endif

  Object o = s.dfa_sets[bunch.GetNFABit()];

  if (null != o)
    {
    dfa = (Dfa) o;
#if OLD_DUMP_DEBUG
    Console.WriteLine(" FOUND!");
#endif
    return dfa.GetLabel();
    }

#if OLD_DUMP_DEBUG
  Console.WriteLine(" NOT FOUND!");
#endif
  return NOT_IN_DSTATES;
  }

#if OLD_DUMP_DEBUG
/*
 * function: Print_Set
 */
public static void Print_Set(ArrayList nfa_set)
  {
  int size; 
  int elem;

  size = nfa_set.Count;

  if (size == 0)
    {
    Console.Write("empty ");
    }

  for (elem = 0; elem < size; ++elem)
    {
    Nfa nfa = (Nfa) nfa_set[elem];
    Console.Write(nfa.GetLabel() + " ");
    }
  }
#endif
}
}
