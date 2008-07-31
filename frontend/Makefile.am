SUBDIRS = jay libedit

if COMPILING_FROM_SVN

noinst_DATA = $(real_top_srcdir)/frontend/CSharpExpressionParser.cs

JAY = jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

$(real_top_srcdir)/frontend/CSharpExpressionParser.cs: CSharpExpressionParser.jay $(srcdir)/skeleton
	$(JAY) -ctv < $(srcdir)/skeleton $(real_top_srcdir)/frontend/CSharpExpressionParser.jay > tmp-p && mv -f tmp-p $(real_top_srcdir)/frontend/CSharpExpressionParser.cs

DISTCLEANFILES = CSharpExpressionParser.cs y.output

endif

EXTRA_DIST = skeleton CSharpExpressionParser.jay $(srcdir)/*.cs
