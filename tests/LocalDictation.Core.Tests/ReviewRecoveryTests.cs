using System.Reflection;
using System.Text;
using LocalDictation.Core;
using Xunit;

public class ReviewRecoveryTests
{
    [Theory]
    [InlineData("renamed")]
    [InlineData("null")]
    [InlineData("malformed-json")]
    [InlineData("invalid-cipher")]
    public void InvalidItemDoesNotPreventPendingRecoveryOrAlterSavedFiles(string damage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ld-review-recovery-" + Guid.NewGuid());
        try
        {
            var store = new ReviewStore(directory);
            var good = new ReviewDocument();
            good.SetPrimary("Recover this valid dictation.");
            store.Save(good);
            var goodPath = Path.Combine(directory, good.Id + ".review");
            var goodBytes = File.ReadAllBytes(goodPath);
            var badPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".review");
            if (damage == "renamed")
            {
                var renamed = new ReviewDocument();
                renamed.SetPrimary("Preserve this mismatched recording for recovery.");
                store.Save(renamed);
                File.Move(Path.Combine(directory, renamed.Id + ".review"), badPath);
            }
            else
            {
                var badBytes = damage == "invalid-cipher" ? new byte[] { 1, 2, 3 } :
                    Protect(damage == "null" ? "null" : "{");
                File.WriteAllBytes(badPath, badBytes);
            }
            var originalBadBytes = File.ReadAllBytes(badPath);

            var pending = new PendingReviews(store);

            var recovered = Assert.Single(pending.Documents);
            Assert.Equal(good.Id, recovered.Id);
            Assert.Equal("Recover this valid dictation.", recovered.OutputText);
            Assert.Equal(Path.GetFileName(badPath), Assert.Single(store.Errors));
            Assert.Empty(pending.UnsavedIds);
            Assert.Equal(goodBytes, File.ReadAllBytes(goodPath));
            Assert.Equal(originalBadBytes, File.ReadAllBytes(badPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    static byte[] Protect(string plain)
    {
        // Build semantically invalid records with the real current-user DPAPI format.
        // The public store intentionally cannot save a null document or malformed JSON.
        var protection = typeof(ReviewStore).Assembly.GetType("LocalDictation.Core.UserProtection", true)!;
        var transform = protection.GetMethod("Transform", BindingFlags.Public | BindingFlags.Static)!;
        return (byte[])transform.Invoke(null, [Encoding.UTF8.GetBytes(plain), false])!;
    }
}