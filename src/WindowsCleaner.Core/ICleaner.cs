namespace WindowsCleaner.Core;

public sealed record CleanItem(string Id, string Category, string Description, long SizeBytes, bool SelectedByDefault = true);

public sealed record CleanResult(long BytesFreed, int Deleted, int Skipped, IReadOnlyList<string> Errors)
{
    public static CleanResult Empty { get; } = new(0, 0, 0, Array.Empty<string>());

    public CleanResult Plus(CleanResult o) =>
        new(BytesFreed + o.BytesFreed, Deleted + o.Deleted, Skipped + o.Skipped, Errors.Concat(o.Errors).ToList());
}

public interface ICleaner
{
    string Name { get; }
    Task<IReadOnlyList<CleanItem>> ScanAsync(CancellationToken ct = default);
    Task<CleanResult> CleanAsync(IEnumerable<CleanItem> selected, IProgress<string>? progress = null, CancellationToken ct = default);
}
