EXTRA_DIST = FAQ.txt breakpoints.txt debugger-status.txt sse-notes.txt \
	     symbol-lookups.txt debugger.sources

sourcesdir = $(libdir)/monodoc/sources

sources_DATA = debugger.tree debugger.zip debugger.source

debugger.tree debugger.zip: api/*/*.xml
	mono --debug $(libdir)/monodoc/assembler.exe --ecma $(srcdir)/api/ -o debugger

update:
	mono --debug $(libdir)/monodoc/updater.exe @prefix@/lib/Mono.Debugger.dll -o ./api -f -p
	mono --debug $(libdir)/monodoc/updater.exe @prefix@/lib/Mono.Debugger.Backend.dll -o ./api -f -p
	mono --debug $(libdir)/monodoc/updater.exe @prefix@/bin/Interpreter.exe -o ./api -f -p

