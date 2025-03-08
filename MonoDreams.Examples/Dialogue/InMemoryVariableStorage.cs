using Yarn;

namespace MonoDreams.Examples.Dialogue;

public class InMemoryVariableStorage : IVariableStorage
{
    private Dictionary<string, object> _variables = new();

    public bool TryGetValue<T>(string variableName, out T result)
    {
        if (_variables.TryGetValue(variableName, out var entry) && entry is T value)
        {
            result = value;
            return true;
        }
        result = default;
        return false;
    }

    public VariableKind GetVariableKind(string name)
    {
        return VariableKind.Smart;
    }

    public Yarn.Program Program { get; set; }
    public ISmartVariableEvaluator SmartVariableEvaluator { get; set; }

    public void SetValue(string variableName, string stringValue)
    {
        _variables[variableName] = stringValue;
    }

    public void SetValue(string variableName, float floatValue)
    {
        _variables[variableName] = floatValue;
    }

    public void SetValue(string variableName, bool boolValue)
    {
        _variables[variableName] = boolValue;
    }

    public void Clear()
    {
        _variables.Clear();
    }
}