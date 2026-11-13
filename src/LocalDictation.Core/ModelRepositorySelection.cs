namespace LocalDictation.Core;

public static class ModelRepositorySelection
{
    public static string AfterRefresh(string current,string? previousId,string? nextId)
    {
        var value=current.Trim().TrimEnd('/');
        var followsCatalog=value.Length==0||previousId!=null&&value=="https://huggingface.co/"+previousId;
        return followsCatalog?nextId==null?"":"https://huggingface.co/"+nextId:current;
    }
}
