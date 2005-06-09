noinst_DATA = $(top_builddir)/frontend/CSharpExpressionParser.cs

JAY = $(top_builddir)/jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

$(top_builddir)/frontend/CSharpExpressionParser.cs: CSharpExpressionParser.jay $(srcdir)/skeleton
	$(JAY) -ctv < $(srcdir)/skeleton $< > tmp-p && mv -f tmp-p $(top_builddir)/frontend/CSharpExpressionParser.cs

MAINTAINERCLEANFILES = CSharpExpressionParser.cs y.output

EXTRA_DIST = \
	CL.cs				\
	Command.cs			\
	Completer.cs			\
	DebuggerTextWriter.cs		\
	Expression.cs			\
	CSharpExpressionParser.jay	\
	CSharpTokenizer.cs		\
	GnuReadLine.cs			\
	IExpressionParser.cs		\
	Interpreter.cs			\
	Main.cs				\
	MyTextReader.cs			\
	ScriptingContext.cs		\
	Style.cs			\
	skeleton
