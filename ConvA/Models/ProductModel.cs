using System.Text.Json.Serialization;

namespace ConvA;

public class ProductModel {
    [JsonPropertyName("product")] public ProductInfoModel? Product { get; set; }
}

public class ProductInfoModel {
    [JsonPropertyName("roots")] public List<string>? Roots { get; set; }
    [JsonPropertyName("subproducts")] public List<string>? SubProducts { get; set; }
    [JsonPropertyName("projects")] public List<ProjectModel>? Projects { get; set; }
}

public class ProjectModel {
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("out")] public string? OutputPath { get; set; }
    [JsonPropertyName("targets")] public TargetModel? Targets { get; set; }
    [JsonPropertyName("apis")] public List<int>? Apis { get; set; }
}

public class TargetModel {
    [JsonPropertyName("debug")] public string? Debug { get; set; }
    [JsonPropertyName("release")] public string? Release { get; set; }
    [JsonPropertyName("tests")] public string? Tests { get; set; }
    [JsonPropertyName("device_tests")] public string? DeviceTests { get; set; }
    [JsonPropertyName("default")] public string? Default { get; set; }
}