namespace Lex
{
/*
 * Class: Utility
 */
using System;
using System.Text;

public class Utility 
{
#if DUMMY
/*
 * Constants
 */
public const bool DEBUG = true;
public const bool SLOW_DEBUG = true;
public const bool DUMP_DEBUG = true;
/*public const bool DEBUG = false;
  public const bool SLOW_DEBUG = false;
  public const bool DUMP_DEBUG = false;*/
public const bool DESCENT_DEBUG = false;
public const bool OLD_DEBUG = false;
public const bool OLD_DUMP_DEBUG = false;
public const bool FOODEBUG = false;
public const bool DO_DEBUG = false;
#endif
  
/*
 * Constants: Integer Bounds
 */
public const int INT_MAX = 2147483647;

/* UNDONE: What about other character values??? */
public const int MAX_SEVEN_BIT = 127;
public const int MAX_EIGHT_BIT = 256;

/*
 * Function: enter
 * Description: Debugging routine.
 */
public static void enter(String descent, char lexeme, int token)
  {
  StringBuilder sb = new StringBuilder();
  sb.Append("Entering ");
  sb.Append(descent);
  sb.Append(" [lexeme: '");
  if (lexeme < ' ')
    {
    lexeme += (char) 64;
    sb.Append("^");
    }
  sb.Append(lexeme);
  sb.Append("'] [token: ");
  sb.Append(token);
  sb.Append("]");
  Console.WriteLine(sb.ToString());
  }

/*
 * Function: leave
 * Description: Debugging routine.
 */
public static void leave(String descent, char lexeme, int token)
  {
  StringBuilder sb = new StringBuilder();
  sb.Append("Leaving ");
  sb.Append(descent);
  sb.Append(" [lexeme: '");
  if (lexeme < ' ')
    {
    lexeme += (char) 64;
    sb.Append("^");
    }
  sb.Append(lexeme);
  sb.Append("'] [token: ");
  sb.Append(token);
  sb.Append("]");
  Console.WriteLine(sb.ToString());
  }

/*
 * Function: assert
 * Description: Debugging routine.
 */
public static void assert(bool expr)
  {
  if (false == expr)
    {
    Console.WriteLine("Assertion Failed");
    throw new ApplicationException("Assertion Failed.");
    }
  }

/*
 * Function: doubleSize
 */
public static char[] doubleSize(char[] oldBuffer)
  {
  char[] newBuffer = new char[2 * oldBuffer.Length];
  int elem;

  for (elem = 0; elem < oldBuffer.Length; ++elem)
    {
    newBuffer[elem] = oldBuffer[elem];
    }
  return newBuffer;
  }

/*
 * Function: doubleSize
 */
public static byte[] doubleSize(byte[] oldBuffer)
  {
  byte[] newBuffer = new byte[2 * oldBuffer.Length];
  int elem;

  for (elem = 0; elem < oldBuffer.Length; elem++)
    {
    newBuffer[elem] = oldBuffer[elem];
    }
  return newBuffer;
  }

/*
 * Function: hex2bin
 */
public static char hex2bin(char c)
  {
  if ('0' <= c && '9' >= c)
    {
    return (char) (c - '0');
    }
  else if ('a' <= c && 'f' >= c)
    {
    return (char) (c - 'a' + 10);
    }	    
  else if ('A' <= c && 'F' >= c)
    {
    return (char) (c - 'A' + 10);
    }
  Error.impos("Bad hexidecimal digit" + Char.ToString(c));
  return (char) 0;
  }

/*
 * Function: ishexdigit
 */
public static bool ishexdigit(char c)
  {
  if (('0' <= c && '9' >= c)
      || ('a' <= c && 'f' >= c)
      || ('A' <= c && 'F' >= c))
    {
    return true;
    }
  return false;
  }

/*
 * Function: oct2bin
 */
public static char oct2bin(char c)
  {
  if ('0' <= c && '7' >= c)
    {
    return (char) (c - '0');
    }
  Error.impos("Bad octal digit " + Char.ToString(c));
  return (char) 0;
  }

/*
 * Function: isoctdigit
 */
public static bool isoctdigit(char c)
  {
  if ('0' <= c && '7' >= c)
    {
    return true;
    }
  return false;
  }

/*
 * Function: isspace
 */
public static bool IsSpace(char c)
  {
  if ('\b' == c 
      || '\t' == c
      || '\n' == c
      || '\f' == c
      || '\r' == c
      || ' ' == c)
    {
    return true;
    }
  return false;
  }

/*
 * Function: IsNewline
 */
public static bool IsNewline(char c)
  {
  if ('\n' == c
      || '\r' == c)
    {
    return true;
    }
  return false;
  }

/*
 * Function: isalpha
 */
public static bool isalpha(char c)
  {
  if (('a' <= c && 'z' >= c)
      || ('A' <= c && 'Z' >= c))
    {
    return true;
    }
  return false;
  }

/*
 * Function: toupper
 */
public static char toupper(char c)
  {
  if (('a' <= c && 'z' >= c))
    {
    return (char) (c - 'a' + 'A');
    }
  return c;
  }

/*
 * Function: bytencmp
 * Description: Compares up to n elements of 
 * byte array a[] against byte array b[].
 * The first byte comparison is made between 
 * a[a_first] and b[b_first].  Comparisons continue
 * until the null terminating byte '\0' is reached
 * or until n bytes are compared.
 * Return Value: Returns 0 if arrays are the 
 * same up to and including the null terminating byte 
 * or up to and including the first n bytes,
 * whichever comes first.
 */
public static int bytencmp(byte[] a, int a_first, byte[] b, int b_first, int n)
  {
  int elem;

  for (elem = 0; elem < n; ++elem)
    {
    /*System.out.print((char) a[a_first + elem]);
      System.out.print((char) b[b_first + elem]);*/

    if ('\0' == a[a_first + elem] && '\0' == b[b_first + elem])
      {
      /*Console.WriteLine("return 0");*/
      return 0;
      }
    if (a[a_first + elem] < b[b_first + elem])
      {
      /*Console.WriteLine("return 1");*/
      return 1;
      }
    else if (a[a_first + elem] > b[b_first + elem])
      {
      /*Console.WriteLine("return -1");*/
      return -1;
      }
    }
  /*Console.WriteLine("return 0");*/
  return 0;
  }

/*
 * Function: charncmp
 */
public static int charncmp(char[] a, int a_first, char[] b, int b_first, int n)
  {
  int elem;

  for (elem = 0; elem < n; ++elem)
    {
    if ('\0' == a[a_first + elem] && '\0' == b[b_first + elem])
      {
      return 0;
      }
    if (a[a_first + elem] < b[b_first + elem])
      {
      return 1;
      }
    else if (a[a_first + elem] > b[b_first + elem])
      {
      return -1;
      }
    }
  return 0;
  }

public static int Compare(char[] c, String s)
  {
  char[] x = s.ToCharArray();
  for (int i = 0; i < x.Length; i++)
    {
    if (c[i] < x[i])
      return 1;
    if (c[i] > x[i])
      return -1;
    }
  return 0;
  }

}
}
