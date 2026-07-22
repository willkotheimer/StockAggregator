namespace StockAggregatorApp.Models;

public sealed record MemberChange(string Symbol, decimal? ChangePct);

/// <summary>One ETF's divergence read for a day: the "hidden signal" is the ETF
/// rising while most tracked members are flat/negative.</summary>
public sealed record HiddenSignal(
    string Etf,
    string Description,
    decimal? EtfChangePct,
    int MembersTracked,
    int MembersUp,
    int MembersDownOrFlat,
    bool IsHiddenSignal,
    string? TopMemberSymbol,
    decimal? TopMemberChangePct,
    IReadOnlyList<MemberChange> Members);

public sealed record HiddenSignalResponse(string? AsOfDate, IReadOnlyList<HiddenSignal> Signals);

public sealed record RotationRow(int Rank, string Etf, string Description, decimal? ChangePct);

public sealed record RotationResponse(string? AsOfDate, IReadOnlyList<RotationRow> Rows);
