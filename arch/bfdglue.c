#include <bfdglue.h>

gboolean
bfd_glue_check_format_object (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_object);
}

int
bfd_glue_get_symbols (bfd *abfd, asymbol ***symbol_table)
{
	int storage_needed = bfd_get_symtab_upper_bound (abfd);

	if (storage_needed <= 0)
		return storage_needed;

	*symbol_table = g_malloc0 (storage_needed);

	return bfd_canonicalize_symtab (abfd, *symbol_table);
}

const char *
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int index, guint64 *address)
{
	asymbol *symbol = symbol_table [index];

	if (symbol->flags == (BSF_OBJECT | BSF_GLOBAL))
		*address = symbol->section->vma + symbol->value;
	else if (symbol->flags == BSF_FUNCTION)
		*address = symbol->value;
	else
		return NULL;

	return symbol->name;
}
