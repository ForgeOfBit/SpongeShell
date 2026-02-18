#include "PtyHost.h"
#include <stdexcept>

namespace SpongeShell::Core {

    PtyHost::PtyHost() = default;

    PtyHost::~PtyHost() {
        Stop();
    }

    bool PtyHost::Start(const std::wstring& command, short cols, short rows) {
        // --- 1. Create pipe pairs ---
        HANDLE hPipeIn_Read, hPipeIn_Write;   // PTY reads from here  (our writes go in)
        HANDLE hPipeOut_Read, hPipeOut_Write;  // PTY writes here      (we read from here)

        if (!CreatePipe(&hPipeIn_Read, &hPipeIn_Write, nullptr, 0)) return false;
        if (!CreatePipe(&hPipeOut_Read, &hPipeOut_Write, nullptr, 0)) return false;

        m_hInput = hPipeIn_Write;   // we write keystrokes here
        m_hOutput = hPipeOut_Read;   // we read shell output here

        // --- 2. Create the pseudo console ---
        COORD size{ cols, rows };
        HRESULT hr = CreatePseudoConsole(size, hPipeIn_Read, hPipeOut_Write, 0, &m_hPC);
        CloseHandle(hPipeIn_Read);
        CloseHandle(hPipeOut_Write);
        if (FAILED(hr)) return false;

        // --- 3. Build a STARTUPINFOEX with the ConPTY attribute ---
        SIZE_T attrSize = 0;
        InitializeProcThreadAttributeList(nullptr, 1, 0, &attrSize);
        auto attrList = reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(malloc(attrSize));
        InitializeProcThreadAttributeList(attrList, 1, 0, &attrSize);
        UpdateProcThreadAttribute(attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            m_hPC, sizeof(HPCON), nullptr, nullptr);

        STARTUPINFOEXW si{};
        si.StartupInfo.cb = sizeof(STARTUPINFOEXW);
        si.lpAttributeList = attrList;

        // --- 4. Spawn the shell ---
        PROCESS_INFORMATION pi{};
        std::wstring cmd = command; // CreateProcessW needs a mutable buffer
        bool ok = CreateProcessW(
            nullptr, cmd.data(), nullptr, nullptr,
            FALSE,
            EXTENDED_STARTUPINFO_PRESENT,
            nullptr, nullptr,
            &si.StartupInfo, &pi
        );

        DeleteProcThreadAttributeList(attrList);
        free(attrList);

        if (!ok) return false;

        m_hProcess = pi.hProcess;
        CloseHandle(pi.hThread);

        // --- 5. Start the output read loop ---
        m_running = true;
        m_readThread = std::thread(&PtyHost::ReadLoop, this);
        return true;
    }

    void PtyHost::Resize(short cols, short rows) {
        if (m_hPC) {
            COORD size{ cols, rows };
            ResizePseudoConsole(m_hPC, size);
        }
    }

    void PtyHost::Write(const std::string& data) {
        if (m_hInput == INVALID_HANDLE_VALUE) return;
        DWORD written = 0;
        WriteFile(m_hInput, data.data(), static_cast<DWORD>(data.size()), &written, nullptr);
    }

    void PtyHost::Stop() {
        m_running = false;
        if (m_hPC) { ClosePseudoConsole(m_hPC); m_hPC = nullptr; }
        if (m_hInput != INVALID_HANDLE_VALUE) { CloseHandle(m_hInput);  m_hInput = INVALID_HANDLE_VALUE; }
        if (m_hOutput != INVALID_HANDLE_VALUE) { CloseHandle(m_hOutput); m_hOutput = INVALID_HANDLE_VALUE; }
        if (m_hProcess != INVALID_HANDLE_VALUE) {
            TerminateProcess(m_hProcess, 0);
            CloseHandle(m_hProcess);
            m_hProcess = INVALID_HANDLE_VALUE;
        }
        if (m_readThread.joinable()) m_readThread.join();
    }

    void PtyHost::ReadLoop() {
        char buf[4096];
        DWORD bytesRead = 0;
        while (m_running) {
            if (!ReadFile(m_hOutput, buf, sizeof(buf), &bytesRead, nullptr) || bytesRead == 0)
                break;
            if (OnOutput)
                OnOutput(std::string(buf, bytesRead));
        }
    }

} // namespace SpongeShell::Core