noinst_DATA = Parser.cs

JAY = $(top_srcdir)/../mcs/jay/jay

Parser.cs: Parser.jay
	$(JAY) -ctv < skeleton Parser.jay > Parser.tmp && mv -f Parser.tmp Parser.cs

MAINTAINERCLEANFILES = Parser.cs y.output

EXTRA_DIST = Parser.cs Parser.jay skeleton @srcdir@/*.cs

