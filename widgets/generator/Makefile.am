APIS = \
	gtk-api.xml

API_INCLUDES = \
	$(GTKSHARP_PREFIX)/share/gapi/gtk-api.xml \
	$(GTKSHARP_PREFIX)/share/gapi/atk-api.xml

RUNTIME=mono

all: generated-stamp

generated-stamp: $(APIS)
	$(RUNTIME) @GTKSHARP_PREFIX@/gapi_codegen.exe --generate $(APIS) --include $(API_INCLUDES) && touch generated-stamp

clean:
	rm -f generated-stamp

EXTRA_DISt = gtk-api.xml

