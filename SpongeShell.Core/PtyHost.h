#pragma once
#include <windows.h>
#include <string>
#include <functional>
#include <thread>

namespace SpongeShell::Core {

    class PtyHost {
    public:
        PtyHost();
        ~PtyHost();

        // Spawn a shell process inside the PTY
        bool Start(const std::wstring& command, short cols, short rows);

        // Resize the PTY viewport
        void Resize(short cols, short rows);

        // Write input (keystrokes) to the PTY
        void Write(const std::string& data);

        // Called on background thread whenever output arrives
        std::function<void(const std::string&)> OnOutput;

        void Stop();

    private:
        void ReadLoop();

        HPCON        m_hPC = nullptr;
        HANDLE       m_hInput = INVALID_HANDLE_VALUE; // write end  -> PTY stdin
        HANDLE       m_hOutput = INVALID_HANDLE_VALUE; // read end   <- PTY stdout
        HANDLE       m_hProcess = INVALID_HANDLE_VALUE;
        std::thread  m_readThread;
        bool         m_running = false;
    };

} // namespace SpongeShell::Core