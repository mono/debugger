namespace Lex
{
/*
 * Class: Minimize
 */
using System;
using System.Collections;

public class Minimize
{
/*
 * Member Variables
 */
Spec spec;
ArrayList group;
int[] ingroup;

/*
 * Function: Minimize
 * Description: Constructor.
 */
public Minimize()
  {
  reset();
  }
  
/*
 * Function: reset
 * Description: Resets member variables.
 */
private void reset()
  {
  spec = null;
  group = null;
  ingroup = null;
  }

/*
 * Function: set
 * Description: Sets member variables.
 */
private void set(Spec s)
  {
#if DEBUG
  Utility.assert(null != s);
#endif

  spec = s;
  group = null;
  ingroup = null;
  }

/*
 * Function: min_dfa
 * Description: High-level access function to module.
 */
public void min_dfa(Spec s)
  {
  set(s);

  /* Remove redundant states. */
  minimize();

  /* Column and row compression. 
     Save accept states in auxilary vector. */
  reduce();

  reset();
  }

/*
 * Function: col_copy
 * Description: Copies source column into destination column.
 */
private void col_copy(int dest, int src)
  {
  int n;
  int i;
  DTrans d;

  n = spec.dtrans_list.Count;
  for (i = 0; i < n; ++i)
    {
    d = (DTrans) spec.dtrans_list[i];
    d.SetDTrans(dest, d.GetDTrans(src));
    }
  }	
	
/*
 * Function: row_copy
 * Description: Copies source row into destination row.
 */
private void row_copy(int dest, int src)
  {
  spec.dtrans_list[dest] = spec.dtrans_list[src]; 
  }	
	
/*
 * Function: col_equiv
 */
private bool col_equiv(int col1, int col2)
  {
  int n;
  int i;
  DTrans d;

  n = spec.dtrans_list.Count;
  for (i = 0; i < n; ++i)
    {
    d = (DTrans) spec.dtrans_list[i];
    if (d.GetDTrans(col1) != d.GetDTrans(col2))
      return false;
    }
  return true;
  }

/*
 * Function: row_equiv
 */
private bool row_equiv(int row1, int row2)
  {
  int i;
  DTrans d1;
  DTrans d2;

  d1 = (DTrans) spec.dtrans_list[row1];
  d2 = (DTrans) spec.dtrans_list[row2];
	
  for (i = 0; i < spec.dtrans_ncols; ++i)
    {
    if (d1.GetDTrans(i) != d2.GetDTrans(i)) 
      return false;
    }
  return true;
  }

/*
 * Function: reduce
 */
private void reduce()
  {
  int i;
  int j;
  int k;
  int nrows;
  int reduced_ncols;
  int reduced_nrows;
  BitArray set;
  DTrans d;
  int dtrans_size;

  /* Save accept nodes and anchor entries. */
  dtrans_size = spec.dtrans_list.Count;
  set = new BitArray(dtrans_size);
  spec.anchor_array = new int[dtrans_size];
  spec.accept_list = new ArrayList();
  for (i = 0; i < dtrans_size; ++i)
    {
    d = (DTrans) spec.dtrans_list[i];
    spec.accept_list.Add(d.GetAccept());
    spec.anchor_array[i] = d.GetAnchor();
    d.SetAccept(null);
    }

  /* Allocate column map. */
  spec.col_map = new int[spec.dtrans_ncols];
  for (i = 0; i < spec.dtrans_ncols; ++i)
    {
    spec.col_map[i] = -1;
    }

  /* Process columns for reduction. */
  for (reduced_ncols = 0; ; ++reduced_ncols)
    {
#if DEBUG
    for (i = 0; i < reduced_ncols; ++i)
      {
      Utility.assert(-1 != spec.col_map[i]);
      }
#endif

    for (i = reduced_ncols; i < spec.dtrans_ncols; ++i)
      {
      if (-1 == spec.col_map[i])
	break;
      }

    if (i >= spec.dtrans_ncols)
      break;

    if (i >= set.Length)
      set.Length = i+1;
#if DEBUG
    Utility.assert(false == set.Get(i));
    Utility.assert(-1 == spec.col_map[i]);
#endif
    set.Set(i, true);

    spec.col_map[i] = reduced_ncols;

    /* UNDONE: Optimize by doing all comparisons in one batch. */
    for (j = i + 1; j < spec.dtrans_ncols; ++j)
      {
      if (-1 == spec.col_map[j] && true == col_equiv(i,j))
	{
	spec.col_map[j] = reduced_ncols;
	}
      }
    }

  /* Reduce columns. */
  k = 0;
  for (i = 0; i < spec.dtrans_ncols; ++i)
    {
    if (i >= set.Length)
      set.Length = i+1;
    if (set.Get(i))
      {
      ++k;
      set.Set(i,false);
      j = spec.col_map[i];
#if DEBUG
      Utility.assert(j <= i);
#endif
      if (j == i)
	continue;
      col_copy(j,i);
      }
    }
  spec.dtrans_ncols = reduced_ncols;

#if DEBUG
  Utility.assert(k == reduced_ncols);
#endif

  /* Allocate row map. */
  nrows = spec.dtrans_list.Count;
  spec.row_map = new int[nrows];
  for (i = 0; i < nrows; ++i)
    spec.row_map[i] = -1;


  /* Process rows to reduce. */
  for (reduced_nrows = 0; ; ++reduced_nrows)
    {

#if DEBUG
    for (i = 0; i < reduced_nrows; ++i)
      {
      Utility.assert(-1 != spec.row_map[i]);
      }
#endif

    for (i = reduced_nrows; i < nrows; ++i)
      {
      if (-1 == spec.row_map[i])
	break;
      }

    if (i >= nrows)
      break;

#if DEBUG
    Utility.assert(false == set.Get(i));
    Utility.assert(-1 == spec.row_map[i]);
#endif

    set.Set(i,true);

    spec.row_map[i] = reduced_nrows;

    /* UNDONE: Optimize by doing all comparisons in one batch. */
    for (j = i + 1; j < nrows; ++j)
      {
      if (-1 == spec.row_map[j] && true == row_equiv(i,j))
	{
	spec.row_map[j] = reduced_nrows;
	}
      }
    }

  /* Reduce rows. */
  k = 0;
  for (i = 0; i < nrows; ++i)
    {
    if (set.Get(i))
      {
      k++;
      set.Set(i,false);
      j = spec.row_map[i];
#if DEBUG
      Utility.assert(j <= i);
#endif
      if (j == i)
	continue;
      row_copy(j,i);
      }
    }
#if DEBUG
  Console.Write("k = " + k + "\nreduced_nrows = " + reduced_nrows + "\n");
  Utility.assert(k == reduced_nrows);
#endif
  spec.dtrans_list.RemoveRange(reduced_nrows,dtrans_size-reduced_nrows);
  }

/*
 * Function: fix_dtrans
 * Description: Updates CDTrans table after minimization 
 * using groups, removing redundant transition table states.
 */
private void fix_dtrans()
  {
  ArrayList new_list;
  int i;
  int size;
  ArrayList dtrans_group;
  DTrans first;
  int c;

  new_list = new ArrayList();

  size = spec.state_dtrans.Length;
  for (i = 0; i < size; ++i)
    {
    if (DTrans.F != spec.state_dtrans[i])
      {
      spec.state_dtrans[i] = ingroup[spec.state_dtrans[i]];
      }
    }

  size = group.Count;
  for (i = 0; i < size; ++i)
    {
    dtrans_group = (ArrayList) group[i];
    first = (DTrans) dtrans_group[0];
    new_list.Add(first);

    for (c = 0; c < spec.dtrans_ncols; c++)
      {
      if (DTrans.F != first.GetDTrans(c))
	{
	first.SetDTrans(c, ingroup[first.GetDTrans(c)]);
	}
      }
    }

  group = null;
  spec.dtrans_list = new_list;
  }

/*
 * Function: minimize
 * Description: Removes redundant transition table states.
 */
private void minimize()
  {
  ArrayList dtrans_group;
  ArrayList new_group;
  int i;
  int j;
  int old_group_count;
  int group_count;
  DTrans next;
  DTrans first;
  int goto_first;
  int goto_next;
  int c;
  int group_size;
  bool added;

  init_groups();

  group_count = group.Count;
  old_group_count = group_count - 1;

  while (old_group_count != group_count)
    {
    old_group_count = group_count;

#if DEBUG
    Utility.assert(group.Count == group_count);
#endif

    for (i = 0; i < group_count; ++i)
      {
      dtrans_group = (ArrayList) group[i];

      group_size = dtrans_group.Count;
      if (group_size <= 1)
	continue;

      new_group = new ArrayList();
      added = false;
		
      first = (DTrans) dtrans_group[0];
      for (j = 1; j < group_size; ++j)
	{
	next = (DTrans) dtrans_group[j];

	for (c = 0; c < spec.dtrans_ncols; ++c)
	  {
	  goto_first = first.GetDTrans(c);
	  goto_next = next.GetDTrans(c);

	  if (goto_first != goto_next
	      && (goto_first == DTrans.F
		  || goto_next == DTrans.F
		  || ingroup[goto_next] != ingroup[goto_first]))
	    {
#if DEBUG
	    Utility.assert(dtrans_group[j] == next);
#endif
	    dtrans_group.RemoveAt(j);
	    j--;
	    group_size--;
	    new_group.Add(next);
	    if (!added)
	      {
	      added = true;
	      group_count++;
	      group.Add(new_group);
	      }
	    ingroup[next.GetLabel()] = group.Count - 1;

#if DEBUG
	    Utility.assert(group.Contains(new_group) == true);
	    Utility.assert(group.Contains(dtrans_group) == true);
	    Utility.assert(dtrans_group.Contains(first) == true);
	    Utility.assert(dtrans_group.Contains(next) == false);
	    Utility.assert(new_group.Contains(first) == false);
	    Utility.assert(new_group.Contains(next) == true);
	    Utility.assert(dtrans_group.Count == group_size);
	    Utility.assert(i == ingroup[first.GetLabel()]);
	    Utility.assert((group.Count - 1)  == ingroup[next.GetLabel()]);
#endif
	    break;
	    }
	  }
	}
      }
    }
  Console.WriteLine(group.Count + " states after removal of redundant states.");
  //  if (spec.verbose) && Utility.OLD_DUMP_DEBUG)
#if OLD_DUMP_DEBUG
  Console.WriteLine("\nStates grouped as follows after minimization");
  pgroups();
#endif
  fix_dtrans();
  }

/*
 * Function: init_groups
 */
private void init_groups()
  {
  bool group_found;

  int group_count = 0;
  group = new ArrayList();
	
  int size = spec.dtrans_list.Count;
  ingroup = new int[size];
	
  for (int i = 0; i < size; ++i)
    {
    group_found = false;
    DTrans dtrans = (DTrans) spec.dtrans_list[i];

#if DEBUG
    Utility.assert(i == dtrans.GetLabel());
    Utility.assert(false == group_found);
    Utility.assert(group_count == group.Count);
#endif
	    
    for (int j = 0; j < group_count; j++)
      {
      ArrayList dtrans_group = (ArrayList) group[j];
#if DEBUG
      Utility.assert(false == group_found);
      Utility.assert(0 < dtrans_group.Count);
#endif
      DTrans first = (DTrans) dtrans_group[0];
		
#if DEBUG
      int s = dtrans_group.Count;
      Utility.assert(0 < s);

      for (int k = 1; k < s; k++)
	{
	DTrans check = (DTrans) dtrans_group[k];
	Utility.assert(check.GetAccept() == first.GetAccept());
	}
#endif

      if (first.GetAccept() == dtrans.GetAccept())
	{
	dtrans_group.Add(dtrans);
	ingroup[i] = j;
	group_found = true;
		    
#if DEBUG
	Utility.assert(j == ingroup[dtrans.GetLabel()]);
#endif
	break;
	}
      }
	    
    if (!group_found)
      {
      ArrayList dtrans_group = new ArrayList();
      dtrans_group.Add(dtrans);
      ingroup[i] = group.Count;
      group.Add(dtrans_group);
      group_count++;
      }
    }

#if OLD_DUMP_DEBUG
  Console.WriteLine("Initial grouping:");
  pgroups();
  Console.WriteLine("");
#endif
  }

/*
 * Function: pset
 */
private void pset(ArrayList dtrans_group)
  {
  int size = dtrans_group.Count;
  for (int i = 0; i < size; ++i)
    {
    DTrans dtrans = (DTrans) dtrans_group[i];
    Console.Write(dtrans.GetLabel() + " ");
    }
  }
  
/*
 * Function: pgroups
 */
private void pgroups()
  {
  int dtrans_size;
  int group_size = group.Count;

  for (int i = 0; i < group_size; ++i)
    {
    Console.Write("\tGroup " + i + " {");
    pset((ArrayList) group[i]);
    Console.WriteLine("}\n");
    }
	
  Console.WriteLine("");
  dtrans_size = spec.dtrans_list.Count;
  for (int i = 0; i < dtrans_size; ++i)
    {
    Console.WriteLine("\tstate " + i
		      + " is in group " + ingroup[i]);
    }
  }
}
}
