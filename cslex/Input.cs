namespace Lex
{
/*
 * Class: CInput
 * Description: 
 */
using System;
using System.IO;

public class Input
{
/*
 * Member Variables
 */
private StreamReader instream; /* Lex specification file. */

public bool eof_reached; /* Whether EOF has been encountered. */
public bool pushback_line; 

public char[] line; /* Line buffer. */
public int line_read; /* Number of bytes read into line buffer. */
public int line_index; /* Current index into line buffer. */
public int line_number; /* Current line number. */

/***************************************************************
  Constants
  **************************************************************/
private const int BUFFER_SIZE = 1024;
const bool EOF = true;
const bool NOT_EOF = false;

/*
 * Function: Input
 */
public Input(StreamReader ihandle)
  {
#if DEBUG
  Utility.assert(ihandle != null);
#endif

  /* Initialize input stream. */
  instream = ihandle;

  line = new char[BUFFER_SIZE];
  line_read = 0;
  line_index = 0;

  /* Initialize state variables. */
  eof_reached = false;
  line_number = 0;
  pushback_line = false;
  }

/*
 * Function: GetLine
 * Description: Returns true on EOF, false otherwise.
 * Guarantees not to return a blank line, or a line
 * of zero length.
 */
public bool GetLine()
  {
  int elem;

  /* Has EOF already been reached? */
  if (eof_reached)
    return true;

  /* pushback current line? */
  if (pushback_line)
    {
    pushback_line = false;

    /* check for empty line. */
    for (elem = 0; elem < line_read; ++elem)
      if (!Utility.IsSpace(line[elem]))
	break;

    /* nonempty? */
    if (elem < line_read)
      {
      line_index = 0;
      return false;
      }
    }

  while (true)
    {
    String lineStr = instream.ReadLine();
    if (lineStr == null)
      {
      eof_reached = true;
      line_index = 0;
      return true;
      }
    line = (lineStr + "\n").ToCharArray();
    line_read = line.Length;
    line_number++;

    /* check for empty lines and discard them */
    elem = 0;
    while (Utility.IsSpace(line[elem]))
      {
      elem++;
      if (elem == line_read)
	break;
      }
    if (elem < line_read)
      break;
    }

  line_index = 0;
  return false;
  }
}
}
