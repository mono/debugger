/*
 * Class: Accept
 */
using System;

public class Accept
{
/*
 * Member Variables
 */
public string action
  {
  get { return astr; }
  }
public int line_number
  {
  get { return line; }
  }

string astr;
int line;

/*
 * Function: Accept
 */
public Accept(String a, int n)
  {
  astr = a;
  line = n;
  }

public Accept(Accept a)
  {
  astr = a.action;
  line = a.line_number;
  }

/*
 * Function: mimic
 */
public void mimic(Accept a)
  {
  astr = a.action;
  }

public void dump()
  {
  Console.WriteLine("Line:" + line_number +":"+action);
  }

}
