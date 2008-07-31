SUBDIRS = jay libedit

noinst_DATA = CSharpExpressionParser.cs

JAY = jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

CSharpExpressionParser.cs: CSharpExpressionParser.jay $(srcdir)/skeleton
	$(JAY) -ctv < $(srcdir)/skeleton $(srcdir)/CSharpExpressionParser.jay > tmp-p && mv -f tmp-p CSharpExpressionParser.cs

DISTCLEANFILES = CSharpExpressionParser.cs y.output

EXTRA_DIST = skeleton CSharpExpressionParser.jay $(srcdir)/*.cs

dist-hook:
	echo "DIST HOOK: $distdir"
	pwd
	rm -f CSharpExpressionParser.cs
