namespace BusinessIdeaAPI.Models;

public class EvaluateResponse
{
    public int Prediction { get; set; }  
    public bool IsProfitable { get; set; }  
    public string Message { get; set; } = ""; 
}
