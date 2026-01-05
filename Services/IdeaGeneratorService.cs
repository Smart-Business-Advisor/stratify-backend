using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BusinessIdeaAPI.Models;

namespace BusinessIdeaAPI.Services;

public interface IIdeaGeneratorService
{
    Task<IdeaSuggestionResponse> GenerateIdeasAsync(IdeaSuggestionRequest request);
    Task<EvaluateResponse> EvaluateIdeaAsync(EvaluateRequest request);
}

public class IdeaGeneratorService : IIdeaGeneratorService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;

    public IdeaGeneratorService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _config = config;

        // Shared base URL for both models
        _httpClient.BaseAddress = new Uri("https://mlmodels-production.up.railway.app");
    }

    // =======================
    // Feature 1: Business Idea Suggestion
    // =======================
    public async Task<IdeaSuggestionResponse> GenerateIdeasAsync(IdeaSuggestionRequest request)
    {
        var payload = new { budget = request.Budget, location = request.Location, field = request.Field };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/llm_predict", payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"LLM Model Error {(int)response.StatusCode}: {error}");
            }

            var rawResponse = await response.Content.ReadAsStringAsync();
            var cleanText = ExtractCleanTextFromLlmResponse(rawResponse);

            var ideas = ParseBusinessIdeas(cleanText);
            var recommendation = ParseRecommendationText(cleanText);

            return new IdeaSuggestionResponse
            {
                Ideas = ideas,
                Recommendation = recommendation
            };
        }
        catch (Exception ex)
        {
            // Return fallback so frontend doesn't crash
            return new IdeaSuggestionResponse
            {
                Ideas = new List<BusinessIdea>
                {
                    new BusinessIdea
                    {
                        Title = "Error",
                        Description = $"Failed to generate ideas: {ex.Message}"
                    }
                },
                Recommendation = "Please try again later."
            };
        }
    }

    // =======================
    // Feature 2: Evaluate Business Idea (Profitable or Not)
    // =======================
    public async Task<EvaluateResponse> EvaluateIdeaAsync(EvaluateRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["Industry"] = request.Industry,
            ["Funding Rounds"] = request.FundingRounds,
            ["Funding Amount (M USD)"] = request.FundingAmount,
            ["Valuation (M USD)"] = request.Valuation,
            ["Revenue (M USD)"] = request.Revenue,
            ["Employees"] = request.Employees,
            ["Market Share (%)"] = request.MarketShare,
            ["Year Founded"] = request.YearFounded,
            ["Region"] = request.Region
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/predict_sucess", payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new EvaluateResponse
                {
                    Prediction = -1,
                    IsProfitable = false,
                    Message = "AI Model Error: " + error
                };
            }

            var rawJson = await response.Content.ReadAsStringAsync();

            var jsonDoc = JsonDocument.Parse(rawJson);
            var root = jsonDoc.RootElement;

            int prediction = -1;

            if (root.TryGetProperty("prediction", out var predElement))
            {
                if (predElement.ValueKind == JsonValueKind.Array && predElement.GetArrayLength() > 0)
                {
                    prediction = predElement[0].GetInt32();
                }
                else if (predElement.ValueKind == JsonValueKind.Number)
                {
                    prediction = predElement.GetInt32();
                }
            }

            if (prediction != 0 && prediction != 1)
            {
                return new EvaluateResponse
                {
                    Prediction = -1,
                    IsProfitable = false,
                    Message = "Unexpected response: " + rawJson
                };
            }

            return new EvaluateResponse
            {
                Prediction = prediction,
                IsProfitable = prediction == 0,
                Message = prediction == 0
                    ? "Profitable! 🚀 High success probability"
                    : "Not profitable ⚠️ Low success probability"
            };
        }
        catch (Exception ex)
        {
            return new EvaluateResponse
            {
                Prediction = -1,
                IsProfitable = false,
                Message = "Error: " + ex.Message
            };
        }
    }
    // =======================
    // Helper Methods
    // =======================
    private string ExtractCleanTextFromLlmResponse(string rawText)
    {
        try
        {
            // Handle {"ideas":"long text here"}
            var match = Regex.Match(rawText, @"\\?""ideas\\?""\s*:\s*\\?""([^""]+)\\?""", RegexOptions.Singleline);
            if (match.Success) return Regex.Unescape(match.Groups[1].Value);

            // Handle {"ideas":["text"]}
            match = Regex.Match(rawText, @"\\?""ideas\\?""\s*:\s*\[\s*\\?""([^""]+)\\?""\s*\]", RegexOptions.Singleline);
            if (match.Success) return Regex.Unescape(match.Groups[1].Value);

            // Fallback: unescape everything
            return Regex.Unescape(rawText);
        }
        catch
        {
            return rawText;
        }
    }

    // Parse the 3 business ideas from clean text
    private List<BusinessIdea> ParseBusinessIdeas(string text)
    {
        var ideas = new List<BusinessIdea>();
        var lines = text.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToArray();

        BusinessIdea current = null;

        foreach (var line in lines)
        {
            // New idea detected
            if (Regex.IsMatch(line, @"^Idea\s+[1-3]", RegexOptions.IgnoreCase) ||
                line.StartsWith("**Idea") ||
                line.Contains("Idea 1:") || line.Contains("Idea 2:") || line.Contains("Idea 3:"))
            {
                if (current != null) ideas.Add(current);

                current = new BusinessIdea
                {
                    Title = Regex.Replace(line, @"[^\w\s&'-]", "", RegexOptions.IgnoreCase).Trim()
                };
                continue;
            }

            if (current == null) continue;

            // Description (first long line)
            if (string.IsNullOrEmpty(current.Description) && line.Length > 40 &&
                !line.ToLower().Contains("cost") && !line.ToLower().Contains("profit") && !line.ToLower().Contains("risk"))
            {
                current.Description = line;
                continue;
            }

            // Cost
            if (line.ToLower().Contains("cost") || line.Contains("$"))
            {
                current.EstimatedStartingCost = ExtractDecimalNumber(line);
            }

            // Profit %
            if (line.ToLower().Contains("profit") && line.Contains("%"))
            {
                current.ExpectedProfitPercentage = ExtractValueAfterMarker(line);
            }

            // Risk Level
            if (line.ToLower().Contains("risk"))
            {
                current.RiskLevel = ExtractValueAfterMarker(line);
            }
        }

        if (current != null) ideas.Add(current);

        // Fallback if parsing failed
        if (ideas.Count == 0)
        {
            ideas.Add(new BusinessIdea
            {
                Title = "Raw AI Response",
                Description = text.Length > 800 ? text.Substring(0, 800) + "..." : text
            });
        }

        return ideas;
    }

    // Parse recommendation
    private string ParseRecommendationText(string text)
    {
        var markers = new[] { "recommend", "best idea", "I recommend", "Recommendation", "overall" };
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index != -1)
            {
                var rec = text.Substring(index);
                var endIndex = rec.IndexOf("\n\n");
                return endIndex > 0 ? rec.Substring(0, endIndex).Trim() : rec.Trim();
            }
        }
        return "No recommendation provided.";
    }

    // Extract number (handles $1,500 or 1500.00)
    private decimal ExtractDecimalNumber(string line)
    {
        var match = Regex.Match(line, @"\d{1,10}(?:,\d{3})*(?:\.\d+)?");
        if (match.Success)
        {
            var cleaned = match.Value.Replace(",", "");
            return decimal.TryParse(cleaned, out var num) ? num : 0;
        }
        return 0;
    }

    // Extract text after : or -
    private string ExtractValueAfterMarker(string line)
    {
        var parts = line.Split(new[] { ':', '-' }, 2);
        return parts.Length > 1 ? parts[1].Trim(' ', '*', '"', '-') : line.Trim();
    }

    // Helper record for classification model
    private record PredictionResult(int prediction);
}