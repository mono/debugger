noinst_DATA = ExpressionParser.cs

JAY = $(top_srcdir)/../mcs/jay/jay

ExpressionParser.cs: ExpressionParser.jay
	$(JAY) -ctv < skeleton ExpressionParser.jay > tmp-p && mv -f tmp-p ExpressionParser.cs

MAINTAINERCLEANFILES = ExpressionParser.cs y.output

EXTRA_DIST = \
	DebuggerTextWriter.cs GnuReadLine.cs Main.cs CL.cs \
	ExpressionParser.cs ExpressionParser.jay Tokenizer.cs skeleton
