namespace Lex
{
/*
 * Class: Dfa
 */
using System;
using System.Collections;
using BitSet;

public class Dfa 
{
/*
 * Member Variables
 */
bool mark;
Accept accept;
int anchor;
ArrayList nfa_set;
BitSet nfa_bit;
int label;

 public ArrayList GetNFASet() { return nfa_set; }
 public void SetNFASet(ArrayList a) { nfa_set = a; }
 public BitSet GetNFABit() { return nfa_bit; }
 public void SetNFABit(BitSet b) { nfa_bit = b; }
 public int GetLabel() { return label; }
 public void SetLabel(int i) { label = i; }
 public Accept GetAccept() { return accept; }
 public void SetAccept(Accept a) { accept = a; }
 public int GetAnchor() { return anchor; }
 public void SetAnchor(int a) { anchor = a; }

/*
 * Function: Dfa
 */
public Dfa(int l)
  {
  mark = false;

  accept = null;
  anchor = Spec.NONE;

  nfa_set = null;
  nfa_bit = null;

  label = l;
  }

public void dump()
  {
#if DUMMY
  Console.WriteLine("[Dfa begin dump]");
  Console.WriteLine("group="+Int32.ToString(group));
  Console.WriteLine("mark="+Boolean.ToString(mark));
  if (accept == null)
    Console.WriteLine("accept=null");
  else
    accept.dump();
  Console.WriteLine("anchor="+Int32.ToString(anchor));
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
	Nfa elem = (CNfa) o2;
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
  Console.WriteLine("[Dfa end dump]");
#endif
  }

public bool IsMarked() { return mark; }
public void SetMarked() { mark = true; }
public void ClearMarked() { mark = false; }

}
}
