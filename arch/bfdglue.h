#ifndef __BFD_GLUE_H__
#define __BFD_GLUE_H__

#include <glib.h>
#include <bfd.h>

G_BEGIN_DECLS

extern gboolean
bfd_glue_check_format_object (bfd *abfd);

extern int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table);

extern const char *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int index, guint64 *address);

G_END_DECLS

#endif
