/*
 * Class: Error
 */
using System;

public class Error 
{
/*
 * Function: impos
 */
public static void impos(String message)
  {
  Console.WriteLine("Lex Error: " + message);
  }

/*
 * Constants
 * Description: Error codes for parse_error().
 */
public const int E_BADEXPR = 0;
public const int E_PAREN = 1;
public const int E_LENGTH = 2;
public const int E_BRACKET = 3;
public const int E_BOL = 4;
public const int E_CLOSE = 5;
public const int E_NEWLINE = 6;
public const int E_BADMAC = 7;
public const int E_NOMAC = 8;
public const int E_MACDEPTH = 9;
public const int E_INIT = 10;
public const int E_EOF = 11;
public const int E_DIRECT = 12;
public const int E_INTERNAL = 13;
public const int E_STATE = 14;
public const int E_MACDEF = 15;
public const int E_SYNTAX = 16;
public const int E_BRACE = 17;
public const int E_DASH = 18;
public const int E_ZERO = 19;

/********************************************************
  Constants
  Description: String messages for parse_error();
  *******************************************************/
public static String errmsg(int i)
  {
  switch (i)
    {
    case 0: return "Malformed regular expression.";
    case 1: return "Missing close parenthesis.";
    case 2: return "Too many regular expressions or expression too long.";
    case 3: return "Missing [ in character class.";
    case 4: return "^ must be at start of expression or after [.";
    case 5: return "+ ? or * must follow an expression or subexpression.";
    case 6: return "Newline in quoted string.";
    case 7: return "Missing } in macro expansion.";
    case 8: return "Macro does not exist.";
    case 9: return "Macro expansions nested too deeply.";
    case 10: return "Lex has not been successfully initialized.";
    case 11: return "Unexpected end-of-file found.";
    case 12: return "Undefined or badly-formed Lex directive.";
    case 13: return "Internal Lex error.";
    case 14: return "Unitialized state name.";
    case 15: return "Badly formed macro definition.";
    case 16: return "Syntax error.";
    case 17: return "Missing brace at start of lexical action.";
    case 18: return "Special character dash - in character class [...] must\n"
	      + "\tbe preceded by start-of-range character.";
    case 19: return "Zero-length regular expression.";
    };
  return null;
  }
/*
 * Function: parse_error
 */
public static void parse_error(int error_code, int line_number)
  {
  Console.WriteLine("Error: Parse error at line "
		    + line_number + ".");
  Console.WriteLine("Description: " + errmsg(error_code));
  //  throw new ApplicationException("Parse error.");
  System.Environment.Exit(1);
  }
}




