namespace StockAggregatorApp.Services.Digest;

/// <summary>
/// Plain names for the digest email. The dashboard's symbolNames.ts uses Yahoo's
/// shortName ("Taiwan Semiconductor Manufactur", "Walt Disney Company (The)"), which
/// reads badly in a sentence, so the email keeps its own short, everyday names.
/// A symbol missing here falls back to its ticker, so a new symbol never breaks a send.
/// </summary>
public static class DigestNames
{
    private static readonly Dictionary<string, string> Companies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NVDA"] = "Nvidia", ["AVGO"] = "Broadcom", ["TSM"] = "Taiwan Semiconductor", ["AMD"] = "AMD",
        ["MU"] = "Micron", ["AMAT"] = "Applied Materials", ["LRCX"] = "Lam Research",
        ["MSFT"] = "Microsoft", ["AAPL"] = "Apple", ["CRM"] = "Salesforce", ["ORCL"] = "Oracle",
        ["ADBE"] = "Adobe", ["NOW"] = "ServiceNow", ["PANW"] = "Palo Alto Networks",
        ["META"] = "Meta", ["GOOGL"] = "Alphabet", ["NFLX"] = "Netflix", ["DIS"] = "Disney",
        ["TMUS"] = "T-Mobile", ["VZ"] = "Verizon", ["CMCSA"] = "Comcast",
        ["AMZN"] = "Amazon", ["TSLA"] = "Tesla", ["HD"] = "Home Depot", ["MCD"] = "McDonald's",
        ["NKE"] = "Nike", ["LOW"] = "Lowe's", ["TJX"] = "TJX",
        ["BRK-B"] = "Berkshire Hathaway", ["JPM"] = "JPMorgan Chase", ["V"] = "Visa", ["MA"] = "Mastercard",
        ["MS"] = "Morgan Stanley", ["GS"] = "Goldman Sachs", ["BAC"] = "Bank of America",
        ["LLY"] = "Eli Lilly", ["UNH"] = "UnitedHealth", ["JNJ"] = "Johnson & Johnson", ["ABBV"] = "AbbVie",
        ["MRK"] = "Merck", ["AMGN"] = "Amgen", ["ISRG"] = "Intuitive Surgical",
        ["GE"] = "GE Aerospace", ["CAT"] = "Caterpillar", ["HON"] = "Honeywell", ["UNP"] = "Union Pacific",
        ["LMT"] = "Lockheed Martin", ["BA"] = "Boeing", ["DE"] = "Deere",
        ["XOM"] = "Exxon Mobil", ["CVX"] = "Chevron", ["COP"] = "ConocoPhillips", ["SLB"] = "SLB",
        ["EOG"] = "EOG Resources", ["MPC"] = "Marathon Petroleum", ["PSX"] = "Phillips 66",
        ["FTRE"] = "Fortrea", ["VRT"] = "Vertiv", ["SSTK"] = "Shutterstock", ["NSP"] = "Insperity",
        ["OUT"] = "Outfront Media", ["ENS"] = "EnerSys", ["GTLB"] = "GitLab",
        ["SSNLF"] = "Samsung", ["SKHY"] = "SK Hynix", ["ASML"] = "ASML", ["TCEHY"] = "Tencent",
        ["NSRGY"] = "Nestle", ["7203.T"] = "Toyota",
        ["MELI"] = "MercadoLibre", ["YPF"] = "YPF", ["BMA"] = "Banco Macro", ["GGAL"] = "Grupo Galicia",
        ["PAM"] = "Pampa Energia", ["CEPU"] = "Central Puerto", ["LOMA"] = "Loma Negra",
    };

    // What each fund holds, as a plural noun phrase: "Chip stocks are the strongest group".
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SMH"] = "chip stocks",
        ["XLK"] = "tech stocks",
        ["XLC"] = "communication stocks",
        ["XLY"] = "consumer spending stocks",
        ["XLF"] = "bank and finance stocks",
        ["XLV"] = "health care stocks",
        ["XLI"] = "industrial stocks",
        ["XLE"] = "energy stocks",
        ["IWM"] = "small company stocks",
        ["VEU"] = "stocks outside the US",
        ["ARGT"] = "Argentina stocks",
    };

    // Funds that aren't a group: where rotation money can go. Named whole, since
    // "the fund for gold" reads worse than "the gold fund".
    private static readonly Dictionary<string, string> OtherFunds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["XLP"] = "the consumer staples stock fund",
        ["XLU"] = "the utility stock fund",
        ["TLT"] = "the long-term Treasury bond fund",
        ["GLD"] = "the gold fund",
    };

    public static bool IsFund(string symbol) => Groups.ContainsKey(symbol) || OtherFunds.ContainsKey(symbol);

    /// <summary>"chip stocks" — the group a fund stands for.</summary>
    public static string GroupOf(string etf) =>
        Groups.TryGetValue(etf, out var group) ? group : $"{etf} stocks";

    /// <summary>
    /// How a symbol is named in a sentence: "Micron (MU)" for a company,
    /// "the chip stock fund (SMH)" for a fund.
    /// </summary>
    public static string Of(string symbol)
    {
        if (OtherFunds.TryGetValue(symbol, out var fund))
        {
            return $"{fund} ({symbol})";
        }

        if (Groups.TryGetValue(symbol, out var group))
        {
            // "chip stocks" -> "the chip stock fund"; "stocks outside the US" -> "the fund for stocks outside the US".
            var single = group.EndsWith(" stocks", StringComparison.Ordinal)
                ? $"the {group[..^" stocks".Length]} stock fund"
                : $"the fund for {group}";
            return $"{single} ({symbol})";
        }

        // "TJX", not "TJX (TJX)", when the company goes by its ticker.
        return Companies.TryGetValue(symbol, out var name) && !string.Equals(name, symbol, StringComparison.Ordinal)
            ? $"{name} ({symbol})"
            : symbol;
    }
}
