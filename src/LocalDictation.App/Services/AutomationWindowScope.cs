using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LocalDictation.App.Services;

internal static class AutomationWindowScope
{
    const int MaximumElements = 32;
    const uint RootAncestor = 2;

    internal enum ScopeStatus { Matched, Mismatched, Unresolved }
    internal readonly record struct Scope(ScopeStatus Status, nint NativeHost, nint Root);

    internal static Scope Resolve(AutomationElement element, nint expectedWindow, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (expectedWindow == 0 || !IsWindow(expectedWindow))
            return new(ScopeStatus.Unresolved, 0, 0);

        try
        {
            AutomationElement? current = element;
            var walker = TreeWalker.RawViewWalker;
            for (var depth = 0; depth < MaximumElements && current != null; depth++)
            {
                token.ThrowIfCancellationRequested();
                var nativeHost = (nint)current.Current.NativeWindowHandle;
                token.ThrowIfCancellationRequested();
                if (nativeHost != 0)
                {
                    // The nearest native host defines the element's window scope.
                    // Never climb past a foreign host to seek a matching ancestor.
                    var root = GetAncestor(nativeHost, RootAncestor);
                    token.ThrowIfCancellationRequested();
                    if (!IsWindow(nativeHost) || root == 0 || !IsWindow(expectedWindow))
                        return new(ScopeStatus.Unresolved, nativeHost, root);
                    return new(root == expectedWindow ? ScopeStatus.Matched : ScopeStatus.Mismatched,
                        nativeHost, root);
                }
                if (depth + 1 < MaximumElements)
                {
                    current = walker.GetParent(current);
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            token.ThrowIfCancellationRequested();
            return new(ScopeStatus.Unresolved, 0, 0);
        }
        return new(ScopeStatus.Unresolved, 0, 0);
    }

    internal static bool IsCurrent(nint host, nint window) =>
        host != 0 && window != 0 && IsWindow(host) && IsWindow(window) &&
        GetAncestor(host, RootAncestor) == window;

    [DllImport("user32.dll")] static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] static extern nint GetAncestor(nint window, uint flags);
}
