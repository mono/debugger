APIS = \
	$(srcdir)/gtk-api.xml \
	$(srcdir)/mono.debugger.gui-api.xml

API_INCLUDES = \
	$(GTKSHARP_PREFIX)/share/gapi/gdk-api.xml \
	$(GTKSHARP_PREFIX)/share/gapi/gtk-api.xml \
	$(GTKSHARP_PREFIX)/share/gapi/atk-api.xml

RUNTIME=mono

if USE_GAPI_CODEGEN

all: generated-stamp

generated-stamp: $(APIS)
	$(RUNTIME) @GTKSHARP_PREFIX@/bin/gapi_codegen.exe --generate $(APIS) --include $(API_INCLUDES) && touch generated-stamp

clean:
	rm -f generated-stamp

endif

EXTRA_DIST = $(APIS)

