using Serilog.Core;
using Serilog.Events;

namespace Skinora.API.Logging;

/// <summary>
/// Centralized secret masking for log events (09 §18.5).
/// Walks every property in the log event (recursively, including structures and
/// sequences) and masks any value whose property name matches a configured
/// secret-bearing field. Applied once via Serilog enricher pipeline so that
/// individual log call sites do not need to repeat masking.
///
/// Masked fields:
///   - privateKey, apiKey, refreshToken, accessToken, password, secret  → fully masked
///   - walletAddress, address                                          → partial mask (first 4 + last 4 chars)
///
/// Matching is case-insensitive on the final segment of the property name.
/// </summary>
public sealed class SecretMaskingEnricher : ILogEventEnricher
{
    private const string FullMask = "***";

    private static readonly HashSet<string> FullyMaskedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "privateKey",
        "apiKey",
        "refreshToken",
        "accessToken",
        "password",
        "secret",
        "jwtSecret",
        "mnemonic",
        "hdWalletMnemonic",
        "authorization",
    };

    private static readonly HashSet<string> PartiallyMaskedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "walletAddress",
        "address",
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Snapshot keys to avoid mutation-during-enumeration.
        var keys = logEvent.Properties.Keys.ToList();
        foreach (var key in keys)
        {
            var original = logEvent.Properties[key];
            var masked = MaskValue(key, original);
            if (!ReferenceEquals(masked, original))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, masked));
            }
        }
    }

    private static LogEventPropertyValue MaskValue(string propertyName, LogEventPropertyValue value)
    {
        if (FullyMaskedFields.Contains(propertyName))
        {
            return new ScalarValue(FullMask);
        }

        if (PartiallyMaskedFields.Contains(propertyName) && value is ScalarValue scalar && scalar.Value is string str)
        {
            return new ScalarValue(PartialMask(str));
        }

        return value switch
        {
            StructureValue structure => MaskStructure(structure),
            DictionaryValue dictionary => MaskDictionary(dictionary),
            SequenceValue sequence => MaskSequence(sequence, propertyName),
            _ => value,
        };
    }

    private static StructureValue MaskStructure(StructureValue structure)
    {
        var changed = false;
        var newProperties = new List<LogEventProperty>(structure.Properties.Count);
        foreach (var prop in structure.Properties)
        {
            var maskedValue = MaskValue(prop.Name, prop.Value);
            if (!ReferenceEquals(maskedValue, prop.Value))
            {
                changed = true;
            }
            newProperties.Add(new LogEventProperty(prop.Name, maskedValue));
        }

        return changed ? new StructureValue(newProperties, structure.TypeTag) : structure;
    }

    private static DictionaryValue MaskDictionary(DictionaryValue dictionary)
    {
        var changed = false;
        var newElements = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(dictionary.Elements.Count);
        foreach (var kvp in dictionary.Elements)
        {
            var keyName = kvp.Key.Value?.ToString() ?? string.Empty;
            var maskedValue = MaskValue(keyName, kvp.Value);
            if (!ReferenceEquals(maskedValue, kvp.Value))
            {
                changed = true;
            }
            newElements.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(kvp.Key, maskedValue));
        }

        return changed ? new DictionaryValue(newElements) : dictionary;
    }

    private static SequenceValue MaskSequence(SequenceValue sequence, string propertyName)
    {
        var changed = false;
        var newElements = new List<LogEventPropertyValue>(sequence.Elements.Count);
        foreach (var element in sequence.Elements)
        {
            var maskedElement = MaskValue(propertyName, element);
            if (!ReferenceEquals(maskedElement, element))
            {
                changed = true;
            }
            newElements.Add(maskedElement);
        }

        return changed ? new SequenceValue(newElements) : sequence;
    }

    private static string PartialMask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length <= 8)
        {
            return FullMask;
        }

        return $"{value[..4]}…{value[^4..]}";
    }
}
