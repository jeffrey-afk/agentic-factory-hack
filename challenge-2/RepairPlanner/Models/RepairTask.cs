using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class RepairTask
{
    [JsonPropertyName("sequence")]
    [JsonProperty("sequence")]
    public int Sequence { get; set; }

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("estimatedDurationMinutes")]
    [JsonProperty("estimatedDurationMinutes")]
    public int EstimatedDurationMinutes { get; set; }

    [JsonPropertyName("requiredSkills")]
    [JsonProperty("requiredSkills")]
    public List<string> RequiredSkills { get; set; } = new List<string>();

    [JsonPropertyName("safetyNotes")]
    [JsonProperty("safetyNotes")]
    public string SafetyNotes { get; set; } = string.Empty;

    [JsonPropertyName("prerequisites")]
    [JsonProperty("prerequisites")]
    public List<int> Prerequisites { get; set; } = new List<int>(); // sequence numbers of tasks that must be completed first

    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "pending"; // pending, in_progress, completed, skipped

    [JsonPropertyName("actualDurationMinutes")]
    [JsonProperty("actualDurationMinutes")]
    public int? ActualDurationMinutes { get; set; }

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string Notes { get; set; } = string.Empty;
}
