__declspec(dllimport) void __stdcall Sleep(unsigned long milliseconds);
__declspec(noreturn) void mainCRTStartup(void) { for (;;) Sleep(1000); }
