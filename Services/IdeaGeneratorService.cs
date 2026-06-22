using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private readonly bool UseStaticData = false;

    private const string JsonSchema = """
        {
          "ideas": [
            {
              "title": "string",
              "description": "string (2-3 sentences)",
              "estimatedStartingCost": 0,
              "expectedProfitPercentage": "20%",
              "riskLevel": "Low | Medium | High"
            }
          ],
          "recommendation": "string explaining which idea is best and why"
        }
        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IdeaGeneratorService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://mariam-abdelsalam-stratify-space-9c30d4a.hf.space");
    }

    // =======================
    // Feature 1: Business Idea Suggestion
    // =======================
    public async Task<IdeaSuggestionResponse> GenerateIdeasAsync(IdeaSuggestionRequest request)
    {
        if (UseStaticData)
            return GetStaticBusinessIdeas(request);

        try
        {
           
            var prompt = $"""
                You are a business advisor. A user wants 3 small business ideas.
                Budget: ${request.Budget}
                Location: {request.Location}
                Field: {request.Field}

                IMPORTANT: Reply with ONLY valid raw JSON — no markdown, no code fences, no explanation.
                Use exactly this structure (3 ideas, estimatedStartingCost as a plain number):
                {JsonSchema}
                """;

            var payload = new
            {
                budget = request.Budget,
                location = request.Location,
                field = request.Field,
                prompt   
            };

            var response = await _httpClient.PostAsJsonAsync("/stratify/llm_predict", payload);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"LLM Error: {response.StatusCode}");

            var rawResponse = await response.Content.ReadAsStringAsync();

            
            var structured = TryParseStructuredResponse(rawResponse);
            if (structured != null)
                return structured;

            
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
            Console.WriteLine($"[GenerateIdeas] Error: {ex.Message}");
            return GetStaticBusinessIdeas(request);
        }
    }

    // =======================
    // Feature 2: Evaluate Business Idea
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
            var response = await _httpClient.PostAsJsonAsync("/stratify/predict_sucess", payload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return new EvaluateResponse
                {
                    Prediction = -1,
                    IsProfitable = false,
                    Message = "AI Model Error: " + error,
                    SuccessProbability = 0
                };
            }

            var rawJson = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(rawJson);
            var root = jsonDoc.RootElement;

            int prediction = -1;
            double successProbability = 0.0;

            if (root.TryGetProperty("prediction", out var predElement))
            {
                if (predElement.ValueKind == JsonValueKind.Array && predElement.GetArrayLength() > 0)
                    prediction = predElement[0].GetInt32();
                else if (predElement.ValueKind == JsonValueKind.Number)
                    prediction = predElement.GetInt32();
            }

            if (root.TryGetProperty("prediction_probability", out var probElement))
            {
                if (probElement.ValueKind == JsonValueKind.Array && probElement.GetArrayLength() > 0)
                {
                    var firstArray = probElement[0];
                    if (firstArray.ValueKind == JsonValueKind.Array && firstArray.GetArrayLength() > 1)
                        successProbability = firstArray[prediction].GetDouble() * 100;
                }
            }

            return new EvaluateResponse
            {
                Prediction = prediction,
                IsProfitable = prediction == 0,
                SuccessProbability = Math.Round(successProbability, 2),
                Message = prediction == 0
                    ? $"Profitable! 🚀 Success Probability: {Math.Round(successProbability, 2)}%"
                    : $"Not profitable ⚠️ Success Probability: {Math.Round(successProbability, 2)}%"
            };
        }
        catch (Exception ex)
        {
            return new EvaluateResponse
            {
                Prediction = -1,
                IsProfitable = false,
                SuccessProbability = 0,
                Message = "Error: " + ex.Message
            };
        }
    }

    // =======================
    // STATIC FALLBACK DATA
    // =======================
    private IdeaSuggestionResponse GetStaticBusinessIdeas(IdeaSuggestionRequest request)
    {
        return new IdeaSuggestionResponse
        {
            Ideas = new List<BusinessIdea>
            {
                new BusinessIdea
                {
                    Title = "First Aid Kit Assembly & Sales",
                    Description = "Assemble and sell customized first aid kits to homes, schools, and offices in Cairo.",
                    EstimatedStartingCost = 800,
                    ExpectedProfitPercentage = "30%",
                    RiskLevel = "Low"
                },
                new BusinessIdea
                {
                    Title = "Medical Equipment Rental",
                    Description = "Rent medical devices to clinics and patients with recurring revenue model.",
                    EstimatedStartingCost = 1500,
                    ExpectedProfitPercentage = "25%",
                    RiskLevel = "Medium"
                },
                new BusinessIdea
                {
                    Title = "Online Pharmacy Delivery",
                    Description = "Fast medicine delivery service focusing on elderly and chronic patients.",
                    EstimatedStartingCost = 1200,
                    ExpectedProfitPercentage = "35%",
                    RiskLevel = "Low"
                }
            },
            Recommendation = $"I recommend **First Aid Kit Assembly & Sales** — best for your budget of ${request.Budget}."
        };
    }

    // =======================
    // PARSE HELPERS
    // =======================
    private IdeaSuggestionResponse? TryParseStructuredResponse(string rawText)
    {
        try
        {
            
            var cleaned = Regex.Replace(rawText, @"```json|```", "").Trim();
           
            var innerMatch = Regex.Match(cleaned,
                @"""ideas""\s*:\s*\[\s*""(.*?)""\s*\]",
                RegexOptions.Singleline);

            if (innerMatch.Success)
                cleaned = Regex.Unescape(innerMatch.Groups[1].Value).Trim();

            var jsonMatch = Regex.Match(cleaned, @"\{.*\}", RegexOptions.Singleline);
            if (!jsonMatch.Success) return null;

            var dto = JsonSerializer.Deserialize<LlmIdeaDto>(jsonMatch.Value, JsonOpts);
            if (dto?.Ideas == null || dto.Ideas.Count == 0) return null;

            return new IdeaSuggestionResponse
            {
                Ideas = dto.Ideas
                    .Where(i => !string.IsNullOrWhiteSpace(i.Title))
                    .Take(3)
                    .Select(i => new BusinessIdea
                    {
                        Title = i.Title!.Trim(),
                        Description = i.Description?.Trim() ?? "AI-generated business idea.",
                        EstimatedStartingCost = i.EstimatedStartingCost,
                        ExpectedProfitPercentage = i.ExpectedProfitPercentage?.Trim() ?? "",
                        RiskLevel = i.RiskLevel?.Trim() ?? ""
                    })
                    .ToList(),
                Recommendation = dto.Recommendation ?? "No recommendation provided."
            };
        }
        catch
        {
            return null; 
        }
    }

    /// <summary>
    /// FALLBACK: Pull the plain text out of the raw LLM response envelope.
    /// </summary>
    private string ExtractCleanTextFromLlmResponse(string rawText)
    {
        try
        {
            var match = Regex.Match(rawText,
                @"""ideas""\s*:\s*\[\s*""(.*?)""\s*\]",
                RegexOptions.Singleline);

            if (match.Success)
                return Regex.Unescape(match.Groups[1].Value);

            return Regex.Unescape(rawText);
        }
        catch
        {
            return rawText;
        }
    }

    /// <summary>
    /// FALLBACK: Parse markdown/prose into BusinessIdea objects when JSON parsing fails.
    /// Uses Matches (not Split) to avoid capturing-group delimiter issues.
    /// </summary>
    private List<BusinessIdea> ParseBusinessIdeas(string text)
    {
        var ideas = new List<BusinessIdea>();

       
        var headerRegex = new Regex(
            @"(?:\*\*\s*)?(?:Idea\s+\d+|#\s*\d+|\d+\.)\s*[:\-–]?\s*\*?\*?\s*([^\n\*\r]{3,80})",
            RegexOptions.IgnoreCase);

        var headers = headerRegex.Matches(text);

        for (int i = 0; i < headers.Count && ideas.Count < 3; i++)
        {
            var titleCandidate = headers[i].Groups[1].Value
                .Replace("**", "").Replace(":", "").Trim();

            
            var lower = titleCandidate.ToLower();
            if (titleCandidate.Length > 80
                || lower.Contains("i've") || lower.Contains("here are")
                || lower.Contains("based on") || lower.Contains("following"))
                continue;

            
            int blockStart = headers[i].Index + headers[i].Length;
            int blockEnd = (i + 1 < headers.Count) ? headers[i + 1].Index : text.Length;
            string block = text.Substring(blockStart, blockEnd - blockStart);

            var idea = new BusinessIdea { Title = titleCandidate };
            string description = "";

            foreach (var rawLine in block.Split('\n'))
            {
                var line = rawLine.Trim().TrimStart('*', '-', '•', ' ');
                if (string.IsNullOrWhiteSpace(line)) continue;

                var lineLower = line.ToLower();

                if ((lineLower.Contains("start") || lineLower.Contains("cost")) && line.Contains("$"))
                    idea.EstimatedStartingCost = ExtractDecimalNumber(line);
                else if (lineLower.Contains("profit") && line.Contains("%"))
                    idea.ExpectedProfitPercentage = ExtractValueAfterMarker(line);
                else if (lineLower.Contains("risk"))
                    idea.RiskLevel = ExtractValueAfterMarker(line);
                else if (description.Length < 500 && line.Length > 20)
                    description += line + " ";
            }

            idea.Description = string.IsNullOrWhiteSpace(description)
                ? "AI-generated business idea."
                : description.Trim();

            ideas.Add(idea);
        }

     
        if (ideas.Count == 0)
            ideas.Add(new BusinessIdea
            {
                Title = "AI Generated Idea",
                Description = text.Length > 1000 ? text[..1000] + "..." : text
            });

        return ideas;
    }

    private string ParseRecommendationText(string text)
    {
        var markers = new[] { "Recommendation", "I recommend", "recommend", "best idea" };
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index != -1)
                return text[index..].Trim();
        }
        return "No recommendation provided.";
    }

    private decimal ExtractDecimalNumber(string line)
    {
        var match = Regex.Match(line, @"\d{1,10}(?:,\d{3})*(?:\.\d+)?");
        var cleaned = match.Value.Replace(",", "");
        return decimal.TryParse(cleaned, out var num) ? num : 0;
    }

    private string ExtractValueAfterMarker(string line)
    {
        var parts = line.Split(new[] { ':', '-' }, 2);
        return parts.Length > 1 ? parts[1].Trim() : line.Trim();
    }


    private class LlmIdeaDto
    {
        [JsonPropertyName("ideas")]
        public List<LlmIdeaItem>? Ideas { get; set; }

        [JsonPropertyName("recommendation")]
        public string? Recommendation { get; set; }
    }

    private class LlmIdeaItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("estimatedStartingCost")]
        public decimal EstimatedStartingCost { get; set; }

        [JsonPropertyName("expectedProfitPercentage")]
        public string? ExpectedProfitPercentage { get; set; }

        [JsonPropertyName("riskLevel")]
        public string? RiskLevel { get; set; }
    }

    private record PredictionResult(int prediction);
}
