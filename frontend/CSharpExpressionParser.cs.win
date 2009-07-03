// created by jay 0.7 (c) 1998 Axel.Schreiner@informatik.uni-osnabrueck.de

#line 2 "./CSharpExpressionParser.jay"
using System.Text;
using System.IO;
using System.Collections;
using System;

namespace Mono.Debugger.Frontend.CSharp
{
	internal class ExpressionParser
	{
		MyTextReader reader;
		Tokenizer lexer;

		protected bool yacc_verbose_flag = false;

		public bool Verbose {
			set {
				yacc_verbose_flag = value;
			}

			get {
				return yacc_verbose_flag;
			}
		}

#line default

  /** simplified error message.
      @see <a href="#yyerror(java.lang.String, java.lang.String[])">yyerror</a>
    */
  public void yyerror (string message) {
    throw new yyParser.yyException(message);
  }

  /** (syntax) error message.
      Can be overwritten to control message format.
      @param message text to be displayed.
      @param expected vector of acceptable tokens, if available.
    */
  public void yyerror (string message, string[] expected) {
    throw new yyParser.yyException(message, expected);
  }

  /** debugging support, requires the package jay.yydebug.
      Set to null to suppress debugging messages.
    */
  internal yydebug.yyDebug debug;

  protected static  int yyFinal = 22;
  public static  string [] yyRule = {
    "$accept : parse_expression",
    "parse_expression : primary_expression",
    "primary_expression : expression",
    "primary_expression : expression ASSIGN expression",
    "primary_expression : expression PLUS expression",
    "primary_expression : expression MINUS expression",
    "primary_expression : expression STAR expression",
    "primary_expression : expression DIV expression",
    "constant : TRUE",
    "constant : FALSE",
    "constant : LONG",
    "constant : ULONG",
    "constant : INT",
    "constant : UINT",
    "constant : FLOAT",
    "constant : DOUBLE",
    "constant : DECIMAL",
    "constant : STRING",
    "constant : NULL",
    "expression : constant",
    "expression : THIS",
    "expression : CATCH",
    "expression : BASE DOTDOT IDENTIFIER",
    "expression : BASE DOT IDENTIFIER",
    "expression : variable_or_type_name",
    "expression : PERCENT IDENTIFIER",
    "expression : STAR expression",
    "expression : AMPERSAND expression",
    "expression : expression OBRACKET expression_list CBRACKET",
    "expression : expression OPAREN expression_list_0 CPAREN",
    "expression : NEW variable_or_type_name OPAREN expression_list_0 CPAREN",
    "expression : OPAREN variable_or_type_name CPAREN expression",
    "expression : expression QUESTION expression COLON expression",
    "expression : PARENT opt_parent_level OPAREN expression CPAREN",
    "expression : OPAREN expression CPAREN",
    "opt_parent_level :",
    "opt_parent_level : PLUS INT",
    "expression_list_0 :",
    "expression_list_0 : expression_list",
    "expression_list : expression",
    "expression_list : expression_list COMMA expression",
    "variable_or_type_name : variable_or_type_name_0",
    "variable_or_type_name : variable_or_type_name STAR",
    "member_name : IDENTIFIER",
    "$$1 :",
    "member_name : IDENTIFIER BACKTICK $$1 INT",
    "variable_or_type_name_0 : member_name",
    "variable_or_type_name_0 : expression DOT member_name",
    "variable_or_type_name_0 : expression DOTDOT member_name",
    "variable_or_type_name_0 : expression ARROW member_name",
  };
  protected static  string [] yyNames = {    
    "end-of-file",null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,null,null,null,null,null,null,null,
    null,null,null,null,null,null,null,"QUIT","EOF","NONE","ERROR",
    "IDENTIFIER","INT","UINT","FLOAT","DOUBLE","DECIMAL","ULONG","LONG",
    "STRING","HASH","AT","DOT","DOTDOT","NOT","COMMA","ASSIGN","EQUAL",
    "NOTEQUAL","STAR","PLUS","MINUS","DIV","PERCENT","STARASSIGN",
    "PLUSASSIGN","MINUSASSIGN","DIVASSIGN","PERCENTASSIGN","OPAREN",
    "CPAREN","OBRACKET","CBRACKET","RIGHTSHIFT","RIGHTSHIFTASSIGN",
    "LEFTSHIFT","LEFTSHIFTASSIGN","LT","GT","LE","GE","AND","OR","OROR",
    "ANDAND","COLON","QUESTION","AMPERSAND","ARROW","BACKTICK","LENGTH",
    "LOWER","UPPER","PARENT","NEW","THIS","BASE","CATCH","TRUE","FALSE",
    "NULL",
  };

  /** index-checked interface to yyNames[].
      @param token single character or %token value.
      @return token name or [illegal] or [unknown].
    */
  public static string yyname (int token) {
    if ((token < 0) || (token > yyNames.Length)) return "[illegal]";
    string name;
    if ((name = yyNames[token]) != null) return name;
    return "[unknown]";
  }

  /** computes list of expected tokens on error by tracing the tables.
      @param state for which to compute the list.
      @return list of token names.
    */
  protected string[] yyExpecting (int state) {
    int token, n, len = 0;
    bool[] ok = new bool[yyNames.Length];

    if ((n = yySindex[state]) != 0)
      for (token = n < 0 ? -n : 0;
           (token < yyNames.Length) && (n+token < yyTable.Length); ++ token)
        if (yyCheck[n+token] == token && !ok[token] && yyNames[token] != null) {
          ++ len;
          ok[token] = true;
        }
    if ((n = yyRindex[state]) != 0)
      for (token = n < 0 ? -n : 0;
           (token < yyNames.Length) && (n+token < yyTable.Length); ++ token)
        if (yyCheck[n+token] == token && !ok[token] && yyNames[token] != null) {
          ++ len;
          ok[token] = true;
        }

    string [] result = new string[len];
    for (n = token = 0; n < len;  ++ token)
      if (ok[token]) result[n++] = yyNames[token];
    return result;
  }

  /** the generated parser, with debugging messages.
      Maintains a state and a value stack, currently with fixed maximum size.
      @param yyLex scanner.
      @param yydebug debug message writer implementing yyDebug, or null.
      @return result of the last reduction, if any.
      @throws yyException on irrecoverable parse error.
    */
  internal Object yyparse (yyParser.yyInput yyLex, Object yyd)
				 {
    this.debug = (yydebug.yyDebug)yyd;
    return yyparse(yyLex);
  }

  /** initial size and increment of the state/value stack [default 256].
      This is not final so that it can be overwritten outside of invocations
      of yyparse().
    */
  protected int yyMax;

  /** executed at the beginning of a reduce action.
      Used as $$ = yyDefault($1), prior to the user-specified action, if any.
      Can be overwritten to provide deep copy, etc.
      @param first value for $1, or null.
      @return first.
    */
  protected Object yyDefault (Object first) {
    return first;
  }

  /** the generated parser.
      Maintains a state and a value stack, currently with fixed maximum size.
      @param yyLex scanner.
      @return result of the last reduction, if any.
      @throws yyException on irrecoverable parse error.
    */
  internal Object yyparse (yyParser.yyInput yyLex)
				{
    if (yyMax <= 0) yyMax = 256;			// initial size
    int yyState = 0;                                   // state stack ptr
    int [] yyStates = new int[yyMax];	                // state stack 
    Object yyVal = null;                               // value stack ptr
    Object [] yyVals = new Object[yyMax];	        // value stack
    int yyToken = -1;					// current input
    int yyErrorFlag = 0;				// #tks to shift

    int yyTop = 0;
    goto skip;
    yyLoop:
    yyTop++;
    skip:
    for (;; ++ yyTop) {
      if (yyTop >= yyStates.Length) {			// dynamically increase
        int[] i = new int[yyStates.Length+yyMax];
        yyStates.CopyTo (i, 0);
        yyStates = i;
        Object[] o = new Object[yyVals.Length+yyMax];
        yyVals.CopyTo (o, 0);
        yyVals = o;
      }
      yyStates[yyTop] = yyState;
      yyVals[yyTop] = yyVal;
      if (debug != null) debug.push(yyState, yyVal);

      yyDiscarded: for (;;) {	// discarding a token does not change stack
        int yyN;
        if ((yyN = yyDefRed[yyState]) == 0) {	// else [default] reduce (yyN)
          if (yyToken < 0) {
            yyToken = yyLex.advance() ? yyLex.token() : 0;
            if (debug != null)
              debug.lex(yyState, yyToken, yyname(yyToken), yyLex.value());
          }
          if ((yyN = yySindex[yyState]) != 0 && ((yyN += yyToken) >= 0)
              && (yyN < yyTable.Length) && (yyCheck[yyN] == yyToken)) {
            if (debug != null)
              debug.shift(yyState, yyTable[yyN], yyErrorFlag-1);
            yyState = yyTable[yyN];		// shift to yyN
            yyVal = yyLex.value();
            yyToken = -1;
            if (yyErrorFlag > 0) -- yyErrorFlag;
            goto yyLoop;
          }
          if ((yyN = yyRindex[yyState]) != 0 && (yyN += yyToken) >= 0
              && yyN < yyTable.Length && yyCheck[yyN] == yyToken)
            yyN = yyTable[yyN];			// reduce (yyN)
          else
            switch (yyErrorFlag) {
  
            case 0:
              yyerror(String.Format ("syntax error, got token `{0}'", yyname (yyToken)), yyExpecting(yyState));
              if (debug != null) debug.error("syntax error");
              goto case 1;
            case 1: case 2:
              yyErrorFlag = 3;
              do {
                if ((yyN = yySindex[yyStates[yyTop]]) != 0
                    && (yyN += Token.yyErrorCode) >= 0 && yyN < yyTable.Length
                    && yyCheck[yyN] == Token.yyErrorCode) {
                  if (debug != null)
                    debug.shift(yyStates[yyTop], yyTable[yyN], 3);
                  yyState = yyTable[yyN];
                  yyVal = yyLex.value();
                  goto yyLoop;
                }
                if (debug != null) debug.pop(yyStates[yyTop]);
              } while (-- yyTop >= 0);
              if (debug != null) debug.reject();
              throw new yyParser.yyException("irrecoverable syntax error");
  
            case 3:
              if (yyToken == 0) {
                if (debug != null) debug.reject();
                throw new yyParser.yyException("irrecoverable syntax error at end-of-file");
              }
              if (debug != null)
                debug.discard(yyState, yyToken, yyname(yyToken),
  							yyLex.value());
              yyToken = -1;
              goto yyDiscarded;		// leave stack alone
            }
        }
        int yyV = yyTop + 1-yyLen[yyN];
        if (debug != null)
          debug.reduce(yyState, yyStates[yyV-1], yyN, yyRule[yyN], yyLen[yyN]);
        yyVal = yyDefault(yyV > yyTop ? null : yyVals[yyV]);
        switch (yyN) {
case 1:
#line 104 "./CSharpExpressionParser.jay"
  {
		return yyVals[0+yyTop];
	  }
  break;
case 3:
#line 112 "./CSharpExpressionParser.jay"
  {
		yyVal = new AssignmentExpression ((Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 4:
#line 116 "./CSharpExpressionParser.jay"
  {
		yyVal = new BinaryOperator (BinaryOperator.Kind.Plus, (Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 5:
#line 120 "./CSharpExpressionParser.jay"
  {
		yyVal = new BinaryOperator (BinaryOperator.Kind.Minus, (Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 6:
#line 124 "./CSharpExpressionParser.jay"
  {
		yyVal = new BinaryOperator (BinaryOperator.Kind.Mult, (Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 7:
#line 128 "./CSharpExpressionParser.jay"
  {
		yyVal = new BinaryOperator (BinaryOperator.Kind.Div, (Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 8:
#line 135 "./CSharpExpressionParser.jay"
  {
		yyVal = new BoolExpression (true);
	  }
  break;
case 9:
#line 139 "./CSharpExpressionParser.jay"
  {
		yyVal = new BoolExpression (false);
	  }
  break;
case 10:
#line 143 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((long) yyVals[0+yyTop]);
	  }
  break;
case 11:
#line 147 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((ulong) yyVals[0+yyTop]);
	  }
  break;
case 12:
#line 151 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((int) yyVals[0+yyTop]);
	  }
  break;
case 13:
#line 155 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((uint) yyVals[0+yyTop]);
	  }
  break;
case 14:
#line 159 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((float) yyVals[0+yyTop]);
	  }
  break;
case 15:
#line 163 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((double) yyVals[0+yyTop]);
	  }
  break;
case 16:
#line 167 "./CSharpExpressionParser.jay"
  {
		yyVal = new NumberExpression ((decimal) yyVals[0+yyTop]);
	}
  break;
case 17:
#line 171 "./CSharpExpressionParser.jay"
  {
		yyVal = new StringExpression ((string) yyVals[0+yyTop]);
	  }
  break;
case 18:
#line 175 "./CSharpExpressionParser.jay"
  {
		yyVal = new NullExpression ();
	  }
  break;
case 20:
#line 183 "./CSharpExpressionParser.jay"
  {
		yyVal = new ThisExpression ();
	  }
  break;
case 21:
#line 187 "./CSharpExpressionParser.jay"
  {
		yyVal = new CatchExpression ();
	  }
  break;
case 22:
#line 191 "./CSharpExpressionParser.jay"
  {
		yyVal = new MemberAccessExpression (new BaseExpression (), "." + ((string) yyVals[0+yyTop]));
	  }
  break;
case 23:
#line 195 "./CSharpExpressionParser.jay"
  {
		yyVal = new MemberAccessExpression (new BaseExpression (), (string) yyVals[0+yyTop]);
	  }
  break;
case 25:
#line 200 "./CSharpExpressionParser.jay"
  {
		yyVal = new RegisterExpression ((string) yyVals[0+yyTop]);
	  }
  break;
case 26:
#line 204 "./CSharpExpressionParser.jay"
  {
		yyVal = new PointerDereferenceExpression ((Expression) yyVals[0+yyTop], false);
	  }
  break;
case 27:
#line 208 "./CSharpExpressionParser.jay"
  {
		yyVal = new AddressOfExpression ((Expression) yyVals[0+yyTop]);
	  }
  break;
case 28:
#line 212 "./CSharpExpressionParser.jay"
  {
		Expression[] exps = new Expression [((ArrayList) yyVals[-1+yyTop]).Count];
		((ArrayList) yyVals[-1+yyTop]).CopyTo (exps, 0);

		yyVal = new ArrayAccessExpression ((Expression) yyVals[-3+yyTop], exps);
	  }
  break;
case 29:
#line 219 "./CSharpExpressionParser.jay"
  {
		yyVal = new InvocationExpression ((Expression) yyVals[-3+yyTop], ((Expression []) yyVals[-1+yyTop]));
	  }
  break;
case 30:
#line 223 "./CSharpExpressionParser.jay"
  {
		yyVal = new NewExpression ((Expression) yyVals[-3+yyTop], ((Expression []) yyVals[-1+yyTop]));
	  }
  break;
case 31:
#line 227 "./CSharpExpressionParser.jay"
  {
		yyVal = new CastExpression ((Expression) yyVals[-2+yyTop], (Expression) yyVals[0+yyTop]);
	  }
  break;
case 32:
#line 231 "./CSharpExpressionParser.jay"
  {
		yyVal = new ConditionalExpression ((Expression)yyVals[-4+yyTop], (Expression)yyVals[-2+yyTop], (Expression)yyVals[0+yyTop]);
	  }
  break;
case 33:
#line 235 "./CSharpExpressionParser.jay"
  {
		yyVal = new ParentExpression ((Expression) yyVals[-1+yyTop], (int) yyVals[-3+yyTop]);
	  }
  break;
case 34:
#line 239 "./CSharpExpressionParser.jay"
  {
		yyVal = yyVals[-1+yyTop];
	  }
  break;
case 35:
#line 246 "./CSharpExpressionParser.jay"
  {
		yyVal = 0;
	  }
  break;
case 36:
#line 250 "./CSharpExpressionParser.jay"
  {
		if ((int) yyVals[0+yyTop] < 1)
			throw new yyParser.yyException ("expected positive integer");
		yyVal = (int) yyVals[0+yyTop];
	  }
  break;
case 37:
#line 259 "./CSharpExpressionParser.jay"
  {
		yyVal = new Expression [0];
	  }
  break;
case 38:
#line 263 "./CSharpExpressionParser.jay"
  {
		Expression[] exps = new Expression [((ArrayList) yyVals[0+yyTop]).Count];
		((ArrayList) yyVals[0+yyTop]).CopyTo (exps, 0);

		yyVal = exps;
	  }
  break;
case 39:
#line 273 "./CSharpExpressionParser.jay"
  {
		ArrayList args = new ArrayList ();
		args.Add (yyVals[0+yyTop]);

		yyVal = args;
	  }
  break;
case 40:
#line 280 "./CSharpExpressionParser.jay"
  {
		ArrayList args = (ArrayList) yyVals[-2+yyTop];
		args.Add (yyVals[0+yyTop]);

		yyVal = args;
	  }
  break;
case 42:
#line 291 "./CSharpExpressionParser.jay"
  {
		yyVal = new PointerTypeExpression ((Expression) yyVals[-1+yyTop]);
	  }
  break;
case 43:
#line 298 "./CSharpExpressionParser.jay"
  {
		yyVal = (string) yyVals[0+yyTop];
	  }
  break;
case 44:
#line 302 "./CSharpExpressionParser.jay"
  {
		lexer.ReadGenericArity = true;
	  }
  break;
case 45:
#line 306 "./CSharpExpressionParser.jay"
  {
		lexer.ReadGenericArity = false;
		yyVal = String.Format ("{0}`{1}", (string) yyVals[-3+yyTop], (int) yyVals[0+yyTop]);
	  }
  break;
case 46:
#line 314 "./CSharpExpressionParser.jay"
  {
		yyVal = new SimpleNameExpression ((string) yyVals[0+yyTop]);
	  }
  break;
case 47:
#line 318 "./CSharpExpressionParser.jay"
  { 
		yyVal = new MemberAccessExpression ((Expression) yyVals[-2+yyTop], (string) yyVals[0+yyTop]);
	  }
  break;
case 48:
#line 322 "./CSharpExpressionParser.jay"
  { 
		yyVal = new MemberAccessExpression ((Expression) yyVals[-2+yyTop], "." + (string) yyVals[0+yyTop]);
	  }
  break;
case 49:
#line 326 "./CSharpExpressionParser.jay"
  {
		Expression expr = new PointerDereferenceExpression ((Expression) yyVals[-2+yyTop], true);
		yyVal = new MemberAccessExpression (expr, (string) yyVals[0+yyTop]);
	  }
  break;
#line default
        }
        yyTop -= yyLen[yyN];
        yyState = yyStates[yyTop];
        int yyM = yyLhs[yyN];
        if (yyState == 0 && yyM == 0) {
          if (debug != null) debug.shift(0, yyFinal);
          yyState = yyFinal;
          if (yyToken < 0) {
            yyToken = yyLex.advance() ? yyLex.token() : 0;
            if (debug != null)
               debug.lex(yyState, yyToken,yyname(yyToken), yyLex.value());
          }
          if (yyToken == 0) {
            if (debug != null) debug.accept(yyVal);
            return yyVal;
          }
          goto yyLoop;
        }
        if (((yyN = yyGindex[yyM]) != 0) && ((yyN += yyState) >= 0)
            && (yyN < yyTable.Length) && (yyCheck[yyN] == yyState))
          yyState = yyTable[yyN];
        else
          yyState = yyDgoto[yyM];
        if (debug != null) debug.shift(yyStates[yyTop], yyState);
	 goto yyLoop;
      }
    }
  }

   static  short [] yyLhs  = {              -1,
    0,    1,    1,    1,    1,    1,    1,    3,    3,    3,
    3,    3,    3,    3,    3,    3,    3,    3,    2,    2,
    2,    2,    2,    2,    2,    2,    2,    2,    2,    2,
    2,    2,    2,    2,    7,    7,    6,    6,    5,    5,
    4,    4,    9,   10,    9,    8,    8,    8,    8,
  };
   static  short [] yyLen = {           2,
    1,    1,    3,    3,    3,    3,    3,    1,    1,    1,
    1,    1,    1,    1,    1,    1,    1,    1,    1,    1,
    1,    3,    3,    1,    2,    2,    2,    4,    4,    5,
    4,    5,    5,    3,    0,    2,    0,    1,    1,    3,
    1,    2,    1,    0,    4,    1,    3,    3,    3,
  };
   static  short [] yyDefRed = {            0,
    0,   12,   13,   14,   15,   16,   11,   10,   17,    0,
    0,    0,    0,    0,    0,   20,    0,   21,    8,    9,
   18,    0,    1,    0,   19,    0,   41,   46,   44,    0,
   25,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,   42,    0,   34,    0,   36,    0,    0,   23,   22,
   47,   48,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,   49,   45,    0,    0,    0,    0,   29,   28,
    0,   33,   30,    0,    0,
  };
  protected static  short [] yyDgoto  = {            22,
   23,   68,   25,   26,   69,   70,   36,   27,   28,   53,
  };
  protected static  short [] yySindex = {         -200,
 -305,    0,    0,    0,    0,    0,    0,    0,    0, -200,
 -249, -200, -200, -248, -200,    0, -263,    0,    0,    0,
    0,    0,    0, -252,    0, -244,    0,    0,    0, -266,
    0, -198, -279, -266, -224, -245, -266, -253, -216, -206,
 -189, -189, -200, -200, -200, -200, -200, -200, -200, -200,
 -189,    0, -205,    0, -200,    0, -200, -200,    0,    0,
    0,    0, -266, -266, -266, -266, -266, -266, -202, -214,
 -261, -168,    0,    0, -266, -161, -212, -200,    0,    0,
 -200,    0,    0, -266, -266,
  };
  protected static  short [] yyRindex = {            0,
    1,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0, -209,    0,    0,    0,    0,    0,    0,
    0,    0,    0,   82,    0,   22,    0,    0,    0,   41,
    0,    0, -148,   59,    0,    0,    0, -221,    0,    0,
    0,    0,    0,    0,    0,    0,    0, -196,    0,    0,
    0,    0,    0,    0,    0,    0,    0, -196,    0,    0,
    0,    0,   90,   96,   97,   98,   99, -273, -190,    0,
    0,    0,    0,    0,   77,    0,    0,    0,    0,    0,
    0,    0,    0, -204,   95,
  };
  protected static  short [] yyGindex = {            0,
    0,    3,    0,   -7,   52,   44,    0,    0,   -8,    0,
  };
  protected static  short [] yyTable = {            52,
   43,   39,   24,   29,   33,   41,   42,   38,   39,   40,
   55,   31,   30,   78,   32,   34,   39,   37,   39,   41,
   42,   24,   48,   43,   49,   52,   44,   45,   46,   47,
   80,   35,   61,   62,   52,   58,   48,   56,   49,   50,
   26,   51,   73,   57,   59,   63,   64,   65,   66,   67,
   24,   24,   72,   50,   60,   51,   74,   75,   27,   76,
    1,    2,    3,    4,    5,    6,    7,    8,    9,   24,
   40,    1,   78,   41,   42,   79,   31,   83,   10,   35,
   84,    2,   11,   85,   24,   40,   24,   40,   12,    3,
   48,   54,   49,   37,   32,    6,    4,    5,    7,   38,
   71,   77,    0,   41,   42,    0,   13,   50,    0,   51,
   41,   42,   14,   15,   16,   17,   18,   19,   20,   21,
   48,    0,   49,   24,   24,    0,    0,   48,   82,   49,
    0,    0,    0,    0,    0,    0,   81,   50,    0,   51,
   24,    0,   24,    0,   50,    0,   51,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,   24,    0,   24,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,    0,
    0,    0,   43,   43,    0,   43,   43,    0,    0,   43,
   43,   43,   43,    0,    0,    0,    0,    0,    0,   43,
   43,   43,   43,   24,   24,    0,   24,   24,    0,    0,
    0,   24,   24,   24,    0,   43,   43,    0,   43,    0,
   24,   24,   24,   24,    0,   26,   26,    0,    0,   26,
   26,   26,   26,    0,    0,    0,   24,   24,    0,   24,
   26,    0,   26,   27,   27,    0,    0,   27,   27,   27,
   27,    0,    0,    0,    0,   26,    0,    0,   27,    0,
   27,   31,   31,    0,    0,   31,   31,   31,   31,    0,
    0,    0,    0,   27,    0,    0,   31,    0,   31,   32,
   32,    0,    0,   32,   32,   32,   32,    0,    0,    0,
    0,   31,    0,    0,   32,    0,   32,    0,    0,    0,
    0,    0,    0,    0,    0,    0,    0,    0,    0,   32,
  };
  protected static  short [] yyCheck = {           279,
    0,  275,    0,  309,   12,  272,  273,   15,  272,  273,
  290,  261,   10,  275,   12,   13,  290,   15,  292,  272,
  273,    0,  289,  276,  291,  279,  279,  280,  281,  282,
  292,  280,   41,   42,  279,  289,  289,  262,  291,  306,
    0,  308,   51,  289,  261,   43,   44,   45,   46,   47,
  272,  273,   50,  306,  261,  308,  262,   55,    0,   57,
  261,  262,  263,  264,  265,  266,  267,  268,  269,  291,
  275,  261,  275,  272,  273,  290,    0,  290,  279,  289,
   78,    0,  283,   81,  306,  290,  308,  292,  289,    0,
  289,  290,  291,  290,    0,    0,    0,    0,    0,  290,
   49,   58,   -1,  272,  273,   -1,  307,  306,   -1,  308,
  272,  273,  313,  314,  315,  316,  317,  318,  319,  320,
  289,   -1,  291,  272,  273,   -1,   -1,  289,  290,  291,
   -1,   -1,   -1,   -1,   -1,   -1,  305,  306,   -1,  308,
  289,   -1,  291,   -1,  306,   -1,  308,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,  306,   -1,  308,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,
   -1,   -1,  272,  273,   -1,  275,  276,   -1,   -1,  279,
  280,  281,  282,   -1,   -1,   -1,   -1,   -1,   -1,  289,
  290,  291,  292,  272,  273,   -1,  275,  276,   -1,   -1,
   -1,  280,  281,  282,   -1,  305,  306,   -1,  308,   -1,
  289,  290,  291,  292,   -1,  275,  276,   -1,   -1,  279,
  280,  281,  282,   -1,   -1,   -1,  305,  306,   -1,  308,
  290,   -1,  292,  275,  276,   -1,   -1,  279,  280,  281,
  282,   -1,   -1,   -1,   -1,  305,   -1,   -1,  290,   -1,
  292,  275,  276,   -1,   -1,  279,  280,  281,  282,   -1,
   -1,   -1,   -1,  305,   -1,   -1,  290,   -1,  292,  275,
  276,   -1,   -1,  279,  280,  281,  282,   -1,   -1,   -1,
   -1,  305,   -1,   -1,  290,   -1,  292,   -1,   -1,   -1,
   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,   -1,  305,
  };

#line 333 "./CSharpExpressionParser.jay"

public ExpressionParser (string name)
{
	this.reader = new MyTextReader ();

	lexer = new Tokenizer (reader, name);
}

public Expression Parse (string text)
{
	try {
		reader.Text = text;
		lexer.Restart ();
		if (yacc_verbose_flag)
			return (Expression) yyparse (lexer, new yydebug.yyDebugSimple ());
		else
			return (Expression) yyparse (lexer);
	} catch (yyParser.yyException ex) {
		throw new ExpressionParsingException (text, lexer.Position, ex.Message);
	} catch (Exception ex) {
		string message = String.Format ("caught unexpected exception: {0}", ex.Message);
		throw new ExpressionParsingException (text, lexer.Position, message);
	}
}

/* end end end */
}
#line default
namespace yydebug {
        using System;
	 internal interface yyDebug {
		 void push (int state, Object value);
		 void lex (int state, int token, string name, Object value);
		 void shift (int from, int to, int errorFlag);
		 void pop (int state);
		 void discard (int state, int token, string name, Object value);
		 void reduce (int from, int to, int rule, string text, int len);
		 void shift (int from, int to);
		 void accept (Object value);
		 void error (string message);
		 void reject ();
	 }
	 
	 class yyDebugSimple : yyDebug {
		 void println (string s){
			 Console.Error.WriteLine (s);
		 }
		 
		 public void push (int state, Object value) {
			 println ("push\tstate "+state+"\tvalue "+value);
		 }
		 
		 public void lex (int state, int token, string name, Object value) {
			 println("lex\tstate "+state+"\treading "+name+"\tvalue "+value);
		 }
		 
		 public void shift (int from, int to, int errorFlag) {
			 switch (errorFlag) {
			 default:				// normally
				 println("shift\tfrom state "+from+" to "+to);
				 break;
			 case 0: case 1: case 2:		// in error recovery
				 println("shift\tfrom state "+from+" to "+to
					     +"\t"+errorFlag+" left to recover");
				 break;
			 case 3:				// normally
				 println("shift\tfrom state "+from+" to "+to+"\ton error");
				 break;
			 }
		 }
		 
		 public void pop (int state) {
			 println("pop\tstate "+state+"\ton error");
		 }
		 
		 public void discard (int state, int token, string name, Object value) {
			 println("discard\tstate "+state+"\ttoken "+name+"\tvalue "+value);
		 }
		 
		 public void reduce (int from, int to, int rule, string text, int len) {
			 println("reduce\tstate "+from+"\tuncover "+to
				     +"\trule ("+rule+") "+text);
		 }
		 
		 public void shift (int from, int to) {
			 println("goto\tfrom state "+from+" to "+to);
		 }
		 
		 public void accept (Object value) {
			 println("accept\tvalue "+value);
		 }
		 
		 public void error (string message) {
			 println("error\t"+message);
		 }
		 
		 public void reject () {
			 println("reject");
		 }
		 
	 }
}
// %token constants
 class Token {
  public const int QUIT = 257;
  public const int EOF = 258;
  public const int NONE = 259;
  public const int ERROR = 260;
  public const int IDENTIFIER = 261;
  public const int INT = 262;
  public const int UINT = 263;
  public const int FLOAT = 264;
  public const int DOUBLE = 265;
  public const int DECIMAL = 266;
  public const int ULONG = 267;
  public const int LONG = 268;
  public const int STRING = 269;
  public const int HASH = 270;
  public const int AT = 271;
  public const int DOT = 272;
  public const int DOTDOT = 273;
  public const int NOT = 274;
  public const int COMMA = 275;
  public const int ASSIGN = 276;
  public const int EQUAL = 277;
  public const int NOTEQUAL = 278;
  public const int STAR = 279;
  public const int PLUS = 280;
  public const int MINUS = 281;
  public const int DIV = 282;
  public const int PERCENT = 283;
  public const int STARASSIGN = 284;
  public const int PLUSASSIGN = 285;
  public const int MINUSASSIGN = 286;
  public const int DIVASSIGN = 287;
  public const int PERCENTASSIGN = 288;
  public const int OPAREN = 289;
  public const int CPAREN = 290;
  public const int OBRACKET = 291;
  public const int CBRACKET = 292;
  public const int RIGHTSHIFT = 293;
  public const int RIGHTSHIFTASSIGN = 294;
  public const int LEFTSHIFT = 295;
  public const int LEFTSHIFTASSIGN = 296;
  public const int LT = 297;
  public const int GT = 298;
  public const int LE = 299;
  public const int GE = 300;
  public const int AND = 301;
  public const int OR = 302;
  public const int OROR = 303;
  public const int ANDAND = 304;
  public const int COLON = 305;
  public const int QUESTION = 306;
  public const int AMPERSAND = 307;
  public const int ARROW = 308;
  public const int BACKTICK = 309;
  public const int LENGTH = 310;
  public const int LOWER = 311;
  public const int UPPER = 312;
  public const int PARENT = 313;
  public const int NEW = 314;
  public const int THIS = 315;
  public const int BASE = 316;
  public const int CATCH = 317;
  public const int TRUE = 318;
  public const int FALSE = 319;
  public const int NULL = 320;
  public const int yyErrorCode = 256;
 }
 namespace yyParser {
  using System;
  /** thrown for irrecoverable syntax errors and stack overflow.
    */
  internal class yyException : System.Exception {
    public readonly string Message;
    public readonly object[] Expected;

    public yyException (string message) : base (message) {
      this.Message = message;
    }

    public yyException (string message, object[] expected) : base (message) {
      this.Message = message;
      this.Expected = expected;
    }
  }

  /** must be implemented by a scanner object to supply input to the parser.
    */
  internal interface yyInput {
    /** move on to next token.
        @return false if positioned beyond tokens.
        @throws IOException on input error.
      */
    bool advance (); // throws java.io.IOException;
    /** classifies current token.
        Should not be called if advance() returned false.
        @return current %token or single character.
      */
    int token ();
    /** associated with current token.
        Should not be called if advance() returned false.
        @return value for token().
      */
    Object value ();
  }
 }
} // close outermost namespace, that MUST HAVE BEEN opened in the prolog
