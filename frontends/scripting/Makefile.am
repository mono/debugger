noinst_DATA = Parser.cs

if HAVE_JAY

Parser.cs: Parser.jay
	$(JAY) -ctv < skeleton Parser.jay > Parser.tmp && mv -f Parser.tmp Parser.cs

endif

MAINTAINERCLEANFILES = Parser.cs y.output

EXTRA_DIST = Parser.cs Parser.jay skeleton @srcdir@/*.cs

