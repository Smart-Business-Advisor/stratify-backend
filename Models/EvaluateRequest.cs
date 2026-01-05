namespace BusinessIdeaAPI.Models;  

public class EvaluateRequest
{
    public string Industry { get; set; } = "";
    public int FundingRounds { get; set; }
    public decimal FundingAmount { get; set; }
    public decimal Valuation { get; set; }
    public decimal Revenue { get; set; }
    public int Employees { get; set; }
    public decimal MarketShare { get; set; }
    public int YearFounded { get; set; }
    public string Region { get; set; } = "";
}