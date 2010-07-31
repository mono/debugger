SUBDIRS = jay

noinst_DATA = CSharpExpressionParser.cs

JAY = jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

CSharpExpressionParser.cs: CSharpExpressionParser.jay $(srcdir)/jay/skeleton.cs
	$(JAY) -ctv < $(srcdir)/jay/skeleton.cs $(srcdir)/CSharpExpressionParser.jay > tmp-p && mv -f tmp-p CSharpExpressionParser.cs

DISTCLEANFILES = CSharpExpressionParser.cs y.output

EXTRA_DIST = CSharpExpressionParser.jay $(srcdir)/*.cs

dist-hook:
	rm -f CSharpExpressionParser.cs
