namespace BusinessIdeaAPI.Models;

public class IdeaSuggestionRequest
{
    public decimal Budget { get; set; }
    public string Location { get; set; } = string.Empty;  
    public string Field { get; set; } = string.Empty;  
}
