using System.Windows.Automation;
using LocalDictation.Core;

namespace LocalDictation.App.Services;

internal static class ReadAloudSelection
{
    public static string? Capture(nint window, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (window == 0 || WindowsTextDelivery.ForegroundWindow != window) return null;
        var focused = AutomationElement.FocusedElement;
        if (focused is null || focused.Current.IsPassword || !focused.Current.IsEnabled) return null;
        var scope = AutomationWindowScope.Resolve(focused, window, token);
        if (scope.Status != AutomationWindowScope.ScopeStatus.Matched ||
            !focused.TryGetCurrentPattern(TextPattern.Pattern, out var provider)) return null;

        var parts = new List<string>();
        var remaining = ReadAloudPlayback.MaxTextLength;
        foreach (var range in ((TextPattern)provider).GetSelection())
        {
            token.ThrowIfCancellationRequested();
            var text = range.GetText(ReadAloudPlayback.MaxTextLength + 1);
            if (string.IsNullOrEmpty(text)) continue;
            remaining -= text.Length + (parts.Count == 0 ? 0 : 1);
            if (remaining < 0)
                throw new SelectionTooLongException();
            parts.Add(text);
        }

        token.ThrowIfCancellationRequested();
        if (WindowsTextDelivery.ForegroundWindow != window || !AutomationWindowScope.IsCurrent(scope.NativeHost, window)
            || !focused.Equals(AutomationElement.FocusedElement)) return null;
        return string.Join("\n", parts);
    }

    public sealed class SelectionTooLongException() : InvalidOperationException(
        $"Choose up to {ReadAloudPlayback.MaxTextLength} characters to read aloud.");
}
