namespace Lex
{
/*
 * Class: CharSet
 */
using System;
using System.Collections;
using BitSet;

public class CharSet
{
/*
 * Member Variables
 */
private BitSet set;
private bool compflag;

/*
 * Function: CharSet
 */
public CharSet()
  {
  set = new BitSet();
  compflag = false;
  }

/*
 * Function: complement
 */
public void complement()
  {
  compflag = true;
  }

/*
 * Function: add
 */
public void add(int i)
  {
  if (i == 0)
    Console.WriteLine("i = 0");
  set.Set(i,true);
  }

/*
 * Function: addncase
 * Description: add, ignoring case.
 */
public void addncase(char c)
  {
  /* Do this in a Unicode-friendly way. */
  /* (note that duplicate adds have no effect) */
  add(c);
  add(Char.ToLower(c));
  add(Char.ToUpper(c));
  }

/*
 * Function: contains
 */
public bool contains(int i)
  {
  bool result;
	
  result = set.Get(i);
  if (compflag)
    return (false == result);
  return result;
  }

/*
 * Function: mimic
 */
public void mimic(CharSet s)
  {
  compflag = s.compflag;
  set = new BitSet(s.set);
  }

public IEnumerator GetEnumerator() { return set.GetEnumerator(); }

/*
 * Map set using character classes
 */
public void map(CharSet old, int[] mapping)
  {
  compflag = old.compflag;
  set = new BitSet();

  foreach (int index in old)
    {
    if (index < mapping.Length) // skip unmapped chars
      {
      int pos = mapping[index];
      set.Set(pos, true);
      }
    }
  }

}
}
