namespace Vidyano.Script.Runtime;

/// <summary>The result of the most recent <c>CHART</c> verb: the requested chart <see cref="Name"/> and the
/// aggregated chart JSON the server returned (<see cref="Data"/>, the <c>Data</c> attribute of the hidden
/// <c>QueryFilter.Chart</c> result PO). A chart is a read-only observable of a query — never a navigation
/// frame — so it is buffered here (like <see cref="ClientOperation"/>) and read by <c>EXPECT Chart</c> /
/// <c>EXPECT Chart.Data</c>. The buffer is cleared by the interpreter's per-verb reset, so it reflects only
/// the immediately preceding verb.</summary>
public sealed record CapturedChart(string Name, string? Data);
