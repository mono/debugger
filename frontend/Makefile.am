SUBDIRS = jay libedit

noinst_DATA = $(top_builddir)/frontend/CSharpExpressionParser.cs

JAY = jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

$(top_builddir)/frontend/CSharpExpressionParser.cs: CSharpExpressionParser.jay $(srcdir)/skeleton
	$(JAY) -ctv < $(srcdir)/skeleton $(real_top_srcdir)/frontend/CSharpExpressionParser.jay > tmp-p && mv -f tmp-p $(top_builddir)/frontend/CSharpExpressionParser.cs

DISTCLEANFILES = CSharpExpressionParser.cs y.output

EXTRA_DIST = skeleton CSharpExpressionParser.jay *.cs
