// SpongeShellBackend.cpp

#include <Windows.h>

extern "C"
{
	__declspec(dllexport) void StartShell()
	{
		// WinExec artık kullanımdan kalktığı için CreateProcess kullanıyoruz.
		STARTUPINFOA si;
		PROCESS_INFORMATION pi;
		ZeroMemory(&si, sizeof(si));
		si.cb = sizeof(si);
		ZeroMemory(&pi, sizeof(pi));

		// CreateProcess, değiştirilebilir bir komut satırı tamponu ister
		char cmd[] = "cmd.exe";

		if (CreateProcessA(NULL, cmd, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi))
		{
			// Handle'ları kapatıyoruz; işlem arka planda çalışmaya devam eder.
			CloseHandle(pi.hProcess);
			CloseHandle(pi.hThread);
		}
		// Başarısız olursa burada hata işleme ekleyebilirsiniz.
	}
}