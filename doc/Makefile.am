EXTRA_DIST = FAQ.txt breakpoints.txt debugger-status.txt \
	     symbol-lookups.txt debugger.source

sourcesdir = @mono_prefix@/lib/monodoc/sources

sources_DATA = debugger.tree debugger.zip debugger.source

debugger.tree debugger.zip: $(srcdir)/api/*/*.xml
	mono --debug @monodoc_prefix@/lib/monodoc/assembler.exe --ecma $(srcdir)/api -o debugger

update:
	monodocer -assembly:@prefix@/lib/mono/mono-debugger/Mono.Debugger.dll -path:./api -delete -includenonpublic

dist-hook:
	find . -name '*.xml' > files
	tar cTf files - | (cd $(distdir); tar xf -)
	rm -f files

CLEANFILES = debugger.tree debugger.zip files

