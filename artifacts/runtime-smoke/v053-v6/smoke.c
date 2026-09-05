__declspec(noreturn) void entry(void) {
    for (;;) { __asm__ __volatile__("pause"); }
}
