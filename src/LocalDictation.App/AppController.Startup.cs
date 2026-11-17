using LocalDictation.App.Services;
using LocalDictation.Core;

namespace LocalDictation.App;

public sealed partial class AppController
{
    WindowsStartupRegistration? startupRegistration;
    WindowsStartupRegistration StartupRegistration => startupRegistration ??= CreateStartupRegistration();

    public WindowsStartupState StartupState => StartupRegistration.Read();

    public WindowsStartupChangeResult SetStartWithWindows(bool enabled)
    {
        var result = StartupRegistration.SetEnabled(enabled);
        Changed?.Invoke();
        return result;
    }

    static WindowsStartupRegistration CreateStartupRegistration()
    {
        // The actual process path is intentional: never substitute an assembly DLL
        // or a guessed EXE when running under dotnet, diagnostics or a test host.
        var executable = Environment.ProcessPath;
        return new(new WindowsStartupRegistryStore(executable), executable);
    }
}
