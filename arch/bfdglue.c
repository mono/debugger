#include <bfdglue.h>
#include <signal.h>
#include <string.h>
#include <link.h>
#include <elf.h>
#ifdef __linux__
#include <sys/user.h>
#include <sys/procfs.h>
#endif
#ifdef __FreeBSD__
#include <sys/param.h>
#include <sys/procfs.h>
#endif

gboolean
bfd_glue_check_format_object (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_object);
}

gboolean
bfd_glue_check_format_core (bfd *abfd)
{
	return bfd_check_format (abfd, bfd_core);
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
bfd_glue_get_symbol (bfd *abfd, asymbol **symbol_table, int idx, int only_functions, guint64 *address)
{
	asymbol *symbol = symbol_table [idx];

	if (!only_functions && (symbol->flags == (BSF_OBJECT | BSF_GLOBAL)))
		*address = symbol->section->vma + symbol->value;
	else if (symbol->flags == BSF_FUNCTION)
		*address = symbol->value;
	else if (symbol->flags == (BSF_FUNCTION | BSF_GLOBAL)) {
		*address = symbol->section->vma + symbol->value;
		return symbol->name;
	} else if (!strcmp (symbol->name, "__pthread_threads_debug") ||
		   !strcmp (symbol->name, "__pthread_handles") ||
		   !strcmp (symbol->name, "__pthread_handles_num") ||
		   !strcmp (symbol->name, "__pthread_last_event")) {
		*address = symbol->section->vma + symbol->value;
		return symbol->name;
	} else
		return NULL;

	return symbol->name;
}

struct disassemble_info *
bfd_glue_init_disassembler (bfd *abfd)
{
	struct disassemble_info *info = g_new0 (struct disassemble_info, 1);

	INIT_DISASSEMBLE_INFO (*info, stderr, fprintf);
	info->flavour = bfd_get_flavour (abfd);
	info->arch = bfd_get_arch (abfd);
	info->mach = bfd_get_mach (abfd);
	info->octets_per_byte = bfd_octets_per_byte (abfd);

	if (bfd_big_endian (abfd))
		info->display_endian = info->endian = BFD_ENDIAN_BIG;
	else if (bfd_little_endian (abfd))
		info->display_endian = info->endian = BFD_ENDIAN_LITTLE;
	else
		g_assert_not_reached ();

	return info;
}

static int
read_memory_func (bfd_vma memaddr, bfd_byte *myaddr, unsigned int length, struct disassemble_info *info)
{
	BfdGlueDisassemblerInfo *data = info->application_data;

	return (* data->read_memory_cb) (memaddr, myaddr, length);
}

static int
fprintf_func (gpointer stream, const char *message, ...)
{
	BfdGlueDisassemblerInfo *data = stream;
	va_list args;
	gchar *output;
	int retval;

	va_start (args, message);
	output = g_strdup_vprintf (message, args);
	va_end (args);

	data->output_cb (output);
	retval = strlen (output);
	g_free (output);

	return retval;
}

static void
print_address_func (bfd_vma address, struct disassemble_info *info)
{
	BfdGlueDisassemblerInfo *data = info->application_data;

	(* data->print_address_cb) (address);
}

void
bfd_glue_setup_disassembler (struct disassemble_info *info, BfdGlueReadMemoryHandler read_memory_cb,
			     BfdGlueOutputHandler output_cb, BfdGluePrintAddressHandler print_address_cb)
{
	BfdGlueDisassemblerInfo *data = g_new0 (BfdGlueDisassemblerInfo, 1);

	data->read_memory_cb = read_memory_cb;
	data->output_cb = output_cb;
	data->print_address_cb = print_address_cb;

	info->application_data = data;
	info->read_memory_func = read_memory_func;
	info->fprintf_func = fprintf_func;
	info->print_address_func = print_address_func;
	info->stream = data;
}

void
bfd_glue_free_disassembler (struct disassemble_info *info)
{
	g_free (info->application_data);
	g_free (info);
}

int
bfd_glue_disassemble_insn (disassembler_ftype dis, struct disassemble_info *info, guint64 address)
{
	return dis (address, info);
}

gboolean
bfd_glue_get_section_contents (bfd *abfd, asection *section, int raw_section, guint64 offset,
			       gpointer *data, guint32 *size)
{
	gboolean retval;

	if (raw_section)
		*size = section->_raw_size;
	else
		*size = section->_cooked_size;
	*data = g_malloc0 (*size);

	retval = bfd_get_section_contents (abfd, section, *data, offset, *size);
	if (!retval)
		g_free (*data);
	return retval;
}

static void
fill_section (BfdGlueSection *section, asection *p, int idx)
{
	BfdGlueSectionFlags flags = 0;

	if (p->flags & SEC_LOAD)
		flags |= SECTION_FLAGS_LOAD;
	if (p->flags & SEC_ALLOC)
		flags |= SECTION_FLAGS_ALLOC;
	if (p->flags & SEC_READONLY)
		flags |= SECTION_FLAGS_READONLY;

	section->index = idx;
	section->vma = p->vma;
	section->size = p->_raw_size;
	section->flags = flags;
	section->section = GPOINTER_TO_UINT (p);
}

gboolean
bfd_glue_get_sections (bfd *abfd, BfdGlueSection **sections, guint32 *count_ret)
{
	int count = 0;
	asection *p;

	for (p = abfd->sections; p != NULL; p = p->next)
		count++;

	*count_ret = count;
	*sections = g_new0 (BfdGlueSection, count);

	for (p = abfd->sections, count = 0; p != NULL; p = p->next, count++)
		fill_section (&((*sections) [count]), p, count);

	return TRUE;
}

gboolean
bfd_glue_get_section_by_name (bfd *abfd, const char *name, BfdGlueSection **section)
{
	asection *p = bfd_get_section_by_name (abfd, name);

	if (!p)
		return FALSE;

	*section = g_new0 (BfdGlueSection, 1);

	fill_section (*section, p, 0);

	return TRUE;
}

guint64
bfd_glue_elfi386_locate_base (bfd *abfd, const guint8 *data, int size)
{
	const guint8 *ptr;

	for (ptr = data; ptr < data + size; ptr += sizeof (Elf32_Dyn)) {
		Elf32_Dyn *dyn = (Elf32_Dyn *) ptr;

		if (dyn->d_tag == DT_NULL)
			break;
		else if (dyn->d_tag == DT_DEBUG)
			return (guint32) dyn->d_un.d_ptr;
	}

	return 0;
}

gboolean
bfd_glue_core_file_elfi386_get_registers (const guint8 *data, int size, guint32 *regs)
{
#ifdef __linux__
	if (size != 68) {
		g_warning (G_STRLOC ": Core file has unknown .reg section size %d", size);
		return FALSE;
	}

	memcpy (regs, data, size);
	return TRUE;
#endif
#ifdef __FreeBSD__
	gregset_t *regset = (gregset_t *) data;

	if (size != sizeof (gregset_t)) {
		g_warning (G_STRLOC ": Core file has unknown .reg section size %d (expected %d)",
			   size, sizeof (gregset_t));
		return FALSE;
	}

	regs [0] = regset->r_ebx;
	regs [1] = regset->r_ecx;
	regs [2] = regset->r_edx;
	regs [3] = regset->r_esi;
	regs [4] = regset->r_edi;
	regs [5] = regset->r_ebp;
	regs [6] = regset->r_eax;
	regs [7] = regset->r_ds;
	regs [8] = regset->r_es;
	regs [9] = regset->r_fs;
	regs [10] = regset->r_gs;
	regs [12] = regset->r_eip;
	regs [13] = regset->r_cs;
	regs [14] = regset->r_eflags;
	regs [15] = regset->r_esp;
	regs [16] = regset->r_ss;

	return TRUE;
#endif

	return FALSE;
}

