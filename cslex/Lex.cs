namespace Lex
{
/*
 * Class: Lex
 * Description: Top-level lexical analyzer generator function.
 *
 * History:
 *  This is a conversion of the JLex program which itself was based on the
 *  original C-based Lex tool.
 *
 * Brad Merrill
 * 20-Sep-1999
 */
using System;
public class Lex
{
public const int MAXBUF = 8192;
public const int MAXSTR = 128;

/***************************************************************
  Function: main
  **************************************************************/
public static void Main()
  {
  String [] args = Environment.GetCommandLineArgs();
  Gen lg;

  if (args.Length < 2)
    {
    Console.WriteLine("lex <filename>");
    return;
    }

  lg = new Gen(args[1]);
  lg.generate();
  }
}    
}
