#define _CRT_SECURE_NO_WARNINGS
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>

static const wchar_t *arg_value(int argc, wchar_t **argv, const wchar_t *name) {
    for (int i = 1; i + 1 < argc; ++i) {
        if (wcscmp(argv[i], name) == 0) return argv[i + 1];
    }
    return NULL;
}

static int read_file_small(const wchar_t *path, char *buffer, size_t capacity) {
    FILE *f = _wfopen(path, L"rb");
    if (!f) return 0;
    size_t n = fread(buffer, 1, capacity - 1, f);
    fclose(f);
    buffer[n] = '\0';
    while (n > 0 && (buffer[n - 1] == '\r' || buffer[n - 1] == '\n' || buffer[n - 1] == ' ' || buffer[n - 1] == '\t')) {
        buffer[--n] = '\0';
    }
    return 1;
}

static char *read_stdin_all(size_t *length) {
    size_t cap = 4096;
    size_t len = 0;
    char *buf = (char *)malloc(cap + 1);
    if (!buf) return NULL;
    for (;;) {
        if (len == cap) {
            cap *= 2;
            char *next = (char *)realloc(buf, cap + 1);
            if (!next) { free(buf); return NULL; }
            buf = next;
        }
        size_t n = fread(buf + len, 1, cap - len, stdin);
        len += n;
        if (n == 0) break;
    }
    buf[len] = '\0';
    *length = len;
    return buf;
}

static void write_repeat(FILE *stream, char ch, size_t count) {
    char block[4096];
    memset(block, ch, sizeof(block));
    while (count > 0) {
        size_t n = count < sizeof(block) ? count : sizeof(block);
        fwrite(block, 1, n, stream);
        fflush(stream);
        count -= n;
    }
}

int wmain(int argc, wchar_t **argv) {
    const wchar_t *model_path = arg_value(argc, argv, L"--model");
    const wchar_t *token_text = arg_value(argc, argv, L"--max-output-tokens");
    if (!model_path || !token_text || _wtoi(token_text) < 1) return 11;

    char mode[64];
    if (!read_file_small(model_path, mode, sizeof(mode))) return 11;

    size_t request_len = 0;
    char *request = read_stdin_all(&request_len);
    if (!request) return 13;

    if (strcmp(mode, "NORMAL") == 0) {
        fwrite("fixture:", 1, 8, stdout);
        fwrite(request, 1, request_len, stdout);
        fflush(stdout);
        free(request);
        return 0;
    }
    if (strcmp(mode, "STDOUT_OVER") == 0) {
        write_repeat(stdout, 'O', 200000);
        free(request);
        return 0;
    }
    if (strcmp(mode, "STDERR_OVER") == 0) {
        write_repeat(stderr, 'E', 200000);
        fwrite("never-admit", 1, 11, stdout);
        fflush(stdout);
        free(request);
        return 0;
    }
    if (strcmp(mode, "SLEEP") == 0) {
        Sleep(10000);
        fwrite("late", 1, 4, stdout);
        fflush(stdout);
        free(request);
        return 0;
    }
    if (strcmp(mode, "NONZERO") == 0) {
        fwrite("fixture nonzero", 1, 15, stderr);
        fflush(stderr);
        free(request);
        return 7;
    }
    if (strcmp(mode, "INVALID_UTF8") == 0) {
        const unsigned char invalid[2] = { 0xC3, 0x28 };
        fwrite(invalid, 1, sizeof(invalid), stdout);
        fflush(stdout);
        free(request);
        return 0;
    }

    fwrite("unknown fixture model mode", 1, 26, stderr);
    fflush(stderr);
    free(request);
    return 12;
}
