noinst_DATA = CSharpExpressionParser.cs CSharpTokenizer_lex.cs

JAY = $(top_builddir)/jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

CSharpExpressionParser.cs: CSharpExpressionParser.jay
	$(JAY) -ctv < skeleton $< > tmp-p && mv -f tmp-p CSharpExpressionParser.cs

CSharpTokenizer_lex.cs: CSharpTokenizer.lex
	mono $(CSLEX) $<

MAINTAINERCLEANFILES = CSharpExpressionParser.cs CSharpTokenizer_lex.cs y.output

EXTRA_DIST = \
	CL.cs				\
	Command.cs			\
	DebuggerTextWriter.cs		\
	Expression.cs			\
	CSharpExpressionParser.cs	\
	CSharpExpressionParser.jay	\
	CSharpTokenizer_lex.cs		\
	CSharpTokenizer.lex		\
	GnuReadLine.cs			\
	IExpressionParser.cs		\
	Interpreter.cs			\
	Main.cs				\
	MyTextReader.cs			\
	ScriptingContext.cs		\
	Session.cs			\
	Style.cs			\
	skeleton
