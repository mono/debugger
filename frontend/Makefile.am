noinst_DATA = ExpressionParser.cs

JAY = $(top_builddir)/jay/jay

ExpressionParser.cs: ExpressionParser.jay
	$(JAY) -ctv < skeleton ExpressionParser.jay > tmp-p && mv -f tmp-p ExpressionParser.cs

MAINTAINERCLEANFILES = ExpressionParser.cs y.output

EXTRA_DIST = \
	CL.cs			\
	Command.cs		\
	DebuggerTextWriter.cs	\
	Expression.cs		\
	ExpressionParser.cs	\
	ExpressionParser.jay	\
	GnuReadLine.cs		\
	Interpreter.cs		\
	Main.cs			\
	MyTextReader.cs		\
	ScriptingContext.cs	\
	Session.cs		\
	Style.cs		\
	skeleton		\
	Tokenizer.cs
