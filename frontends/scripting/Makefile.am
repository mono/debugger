MCS=mcs
JAY=jay

noinst_DATA = Parser.cs

Parser.cs: Parser.jay
	$(JAY) -ctv < skeleton Parser.jay > Parser.tmp && mv -f Parser.tmp Parser.cs

CLEANFILES = Parser.cs y.output

