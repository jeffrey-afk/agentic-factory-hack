using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class WorkOrder
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("workOrderNumber")]
    [JsonProperty("workOrderNumber")]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = "corrective"; // corrective, preventive, emergency

    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string Priority { get; set; } = "medium"; // critical, high, medium, low

    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "new"; // new, assigned, in_progress, on_hold, completed, cancelled

    [JsonPropertyName("createdAt")]
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; } // technician id

    [JsonPropertyName("assignedAt")]
    [JsonProperty("assignedAt")]
    public DateTime? AssignedAt { get; set; }

    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int EstimatedDuration { get; set; } // in minutes

    [JsonPropertyName("actualDuration")]
    [JsonProperty("actualDuration")]
    public int? ActualDuration { get; set; } // in minutes

    [JsonPropertyName("scheduledStartTime")]
    [JsonProperty("scheduledStartTime")]
    public DateTime? ScheduledStartTime { get; set; }

    [JsonPropertyName("scheduledEndTime")]
    [JsonProperty("scheduledEndTime")]
    public DateTime? ScheduledEndTime { get; set; }

    [JsonPropertyName("actualStartTime")]
    [JsonProperty("actualStartTime")]
    public DateTime? ActualStartTime { get; set; }

    [JsonPropertyName("actualEndTime")]
    [JsonProperty("actualEndTime")]
    public DateTime? ActualEndTime { get; set; }

    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = new List<RepairTask>();

    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = new List<WorkOrderPartUsage>();

    [JsonPropertyName("totalPartsCount")]
    [JsonProperty("totalPartsCount")]
    public int TotalPartsCount { get; set; }

    [JsonPropertyName("totalPartsCost")]
    [JsonProperty("totalPartsCost")]
    public decimal TotalPartsCost { get; set; }

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string Notes { get; set; } = string.Empty;

    [JsonPropertyName("completionNotes")]
    [JsonProperty("completionNotes")]
    public string CompletionNotes { get; set; } = string.Empty;

    [JsonPropertyName("relatedFaultType")]
    [JsonProperty("relatedFaultType")]
    public string RelatedFaultType { get; set; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
