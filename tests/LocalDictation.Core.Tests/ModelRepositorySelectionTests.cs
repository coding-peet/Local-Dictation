using LocalDictation.Core;
using Xunit;
public class ModelRepositorySelectionTests
{
    [Fact] public void InstalledCatalogSelectionAdvancesItsBoundUrl()=>Assert.Equal("https://huggingface.co/owner/next",ModelRepositorySelection.AfterRefresh("https://huggingface.co/owner/old","owner/old","owner/next"));
    [Fact] public void CustomUrlSurvivesCatalogRefresh()=>Assert.Equal("https://huggingface.co/custom/model",ModelRepositorySelection.AfterRefresh("https://huggingface.co/custom/model","owner/old","owner/next"));
    [Fact] public void EmptyInputUsesTheAvailableSelection()=>Assert.Equal("https://huggingface.co/owner/next",ModelRepositorySelection.AfterRefresh("",null,"owner/next"));
    [Fact] public void LastInstalledCatalogSelectionClearsOnlyItsBoundUrl()=>Assert.Equal("",ModelRepositorySelection.AfterRefresh("https://huggingface.co/owner/old/","owner/old",null));
}
