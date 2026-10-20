using StockAggregator.Services;
using Xunit;

namespace StockAggregator.Tests.Projection;

public class EarningsDateFetcherTests
{
    [Fact]
    public void Parse_ReadsReportDatesFromNasdaqsTable()
    {
        // Trimmed from a real api.nasdaq.com/api/company/MSFT/earnings-surprise response.
        const string json = """
        {"data":{"symbol":"msft","earningsSurpriseTable":{"rows":[
          {"fiscalQtrEnd":"Jun 2026","dateReported":"7/29/2026","eps":4.74},
          {"fiscalQtrEnd":"Mar 2026","dateReported":"4/29/2026","eps":4.27},
          {"fiscalQtrEnd":"Dec 2025","dateReported":"not a date","eps":4.14}]}},
         "message":null,"status":{"rCode":200}}
        """;

        var reports = EarningsDateFetcher.Parse("MSFT", json);

        Assert.Equal(2, reports.Count);
        Assert.Equal(new EarningsReport("MSFT", new DateTime(2026, 7, 29), "Jun 2026"), reports[0]);
    }

    [Fact]
    public void Parse_AFundWithNoTableGivesNothing()
    {
        Assert.Empty(EarningsDateFetcher.Parse("XLK", """{"data":null,"message":"No data","status":{"rCode":200}}"""));
    }
}
