#include <server.h>
#include <signal.h>
#include <unistd.h>
#include <errno.h>

gboolean
mono_debugger_util_read (int fd, gpointer data, int size)
{
	guint8 *ptr = data;

	while (size) {
		int ret = read (fd, ptr, size);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			g_warning (G_STRLOC ": Can't read from server: %s (%d)",
				   g_strerror (errno), errno);
			return FALSE;
		}

		size -= ret;
		ptr += ret;
	}

	return TRUE;
}

gboolean
mono_debugger_util_write (int fd, gconstpointer data, int size)
{
	guint8 *ptr = data;

	while (size) {
		int ret = write (fd, ptr, size);
		if (ret < 0) {
			if (errno == EINTR)
				continue;
			g_warning (G_STRLOC ": Can't write to server: %s (%d)",
				   g_strerror (errno), errno);
			return FALSE;
		}

		size -= ret;
		ptr += ret;
	}

	return TRUE;
}
