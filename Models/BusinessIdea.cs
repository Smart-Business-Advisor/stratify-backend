namespace BusinessIdeaAPI.Models;

public class BusinessIdea
{
    public string Title { get; set; } = string.Empty;  
    public string Description { get; set; } = string.Empty;
    public decimal EstimatedStartingCost { get; set; } 
    public string ExpectedProfitPercentage { get; set; } = string.Empty;  
    public string RiskLevel { get; set; } = string.Empty;  
}