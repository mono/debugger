namespace Lex
{
/*
 * Class: DTrans
 */
public class DTrans
{
/*
 * Member Variables
 */
int[] dtrans;
Accept accept;
int anchor;
int label;

 public int GetLabel() { return label; } 
 public void SetLabel(int i) { label = i; } 
 public int GetAnchor() { return anchor; }
 public void SetAnchor(int i) { anchor = i; }
 public Accept GetAccept() { return accept; }
 public void SetAccept(Accept a) { accept = a; }
 public void SetDTrans(int dest, int index) { dtrans[dest] = index; }
 public int GetDTrans(int i) { return dtrans[i]; }
 public int GetDTransLength() { return dtrans.Length; } 

/*
 * Constants
 */
public const int F = -1;

/*
 * Function: DTrans
 */
public DTrans(Spec s, Dfa dfa)
  {
  dtrans = new int[s.dtrans_ncols];
  label = s.dtrans_list.Count;
  accept = dfa.GetAccept();
  anchor = dfa.GetAnchor();
  }

}
}
