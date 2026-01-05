
namespace BusinessIdeaAPI.Models;

public class IdeaSuggestionResponse
{
    public List<BusinessIdea> Ideas { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;  
}