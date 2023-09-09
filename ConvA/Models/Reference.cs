namespace ConvA;

public class Reference {
    public string? Name { get; set; }
    public string? HintPath { get; set; }
    public string? Condition { get; set; }
}

public class ExpandedReference : Reference {
    public List<string> ExpandedNames { get; set; } = new();
    public List<string> ExpandedHintPath { get; set; } = new();
}