using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class DiagnosedFault
{
    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    [JsonProperty("severity")]
    public string Severity { get; set; } = "medium"; // critical, high, medium, low

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("detectedAt")]
    [JsonProperty("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("anomalyScore")]
    [JsonProperty("anomalyScore")]
    public double AnomalyScore { get; set; }
}
