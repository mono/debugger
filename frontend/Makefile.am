SUBDIRS = jay libedit

if MAINTAINER_MODE
noinst_DATA = $(real_top_srcdir)/frontend/CSharpExpressionParser.cs

JAY = jay/jay
CSLEX = $(top_builddir)/cslex/cslex.exe

$(real_top_srcdir)/frontend/CSharpExpressionParser.cs: $(real_top_srcdir)/frontend/CSharpExpressionParser.jay $(srcdir)/skeleton
	echo $(real_top_srcdir)
	$(JAY) -ctv < $(srcdir)/skeleton $(real_top_srcdir)/frontend/CSharpExpressionParser.jay > tmp-p && mv -f tmp-p $(real_top_srcdir)/frontend/CSharpExpressionParser.cs

DISTCLEANFILES = CSharpExpressionParser.cs y.output
endif

EXTRA_DIST = skeleton CSharpExpressionParser.jay CSharpExpressionParser.cs *.cs
