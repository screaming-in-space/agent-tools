namespace ModelBoss.Benchmarks;

/// <summary>
/// Scores model output against expected criteria. Runs multiple checks per response
/// and produces a weighted composite accuracy score.
/// No LLM-as-judge here — this is deterministic, reproducible, fast.
/// </summary>
public static class AccuracyScorer
{
    /// <summary>
    /// Scores a model's raw output against the expected output definition.
    /// Returns an <see cref="AccuracyResult"/> with individual check breakdowns.
    /// </summary>
    public static AccuracyResult Score(string modelId, BenchmarkPrompt prompt, string rawOutput)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        rawOutput ??= "";

        var expected = prompt.Expected;
        var checks = new List<AccuracyCheck>();

        // ── Length check ───────────────────────────────────────────────
        checks.Add(ScoreLength(rawOutput, expected));

        // ── Required substrings ────────────────────────────────────────
        if (expected.RequiredSubstrings.Count > 0)
        {
            checks.Add(ScoreRequiredSubstrings(rawOutput, expected.RequiredSubstrings));
        }

        // ── Forbidden substrings ───────────────────────────────────────
        if (expected.ForbiddenSubstrings.Count > 0)
        {
            checks.Add(ScoreForbiddenSubstrings(rawOutput, expected.ForbiddenSubstrings));
        }

        // ── Required structure ─────────────────────────────────────────
        if (expected.RequiredStructure.Count > 0)
        {
            checks.Add(ScoreRequiredStructure(rawOutput, expected.RequiredStructure));
        }

        // ── Reference similarity ───────────────────────────────────────
        if (!string.IsNullOrEmpty(expected.ReferenceOutput))
        {
            checks.Add(ScoreReferenceSimilarity(rawOutput, expected.ReferenceOutput));
        }

        // ── Composite ──────────────────────────────────────────────────
        var totalWeight = 0.0;
        var weightedSum = 0.0;

        foreach (var check in checks)
        {
            weightedSum += check.Score * check.Weight;
            totalWeight += check.Weight;
        }

        var compositeScore = totalWeight > 0 ? weightedSum / totalWeight : 0;

        return new AccuracyResult
        {
            ModelId = modelId,
            PromptName = prompt.Name,
            Score = compositeScore,
            Passed = compositeScore >= expected.PassThreshold,
            Checks = checks,
        };
    }

    // ── Individual checks ──────────────────────────────────────────────

    private static AccuracyCheck ScoreLength(string output, ExpectedOutput expected)
    {
        var len = output.Length;
        double score;
        string detail;

        if (len < expected.MinLength)
        {
            score = len > 0 ? (double)len / expected.MinLength : 0;
            detail = $"Too short: {len} < {expected.MinLength} minimum";
        }
        else if (len > expected.MaxLength)
        {
            // Penalize proportionally but don't zero out
            var overshoot = (double)(len - expected.MaxLength) / expected.MaxLength;
            score = Math.Max(0.2, 1.0 - overshoot);
            detail = $"Too long: {len} > {expected.MaxLength} maximum (score: {score:F2})";
        }
        else
        {
            score = 1.0;
            detail = $"Length OK: {len} chars";
        }

        return new AccuracyCheck
        {
            Name = "length",
            Score = score,
            Weight = 0.5,
            Detail = detail,
        };
    }

    private static AccuracyCheck ScoreRequiredSubstrings(string output, IReadOnlyList<string> required)
    {
        var found = 0;
        var missing = new List<string>();

        foreach (var substring in required)
        {
            if (output.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                found++;
            }
            else
            {
                missing.Add(substring);
            }
        }

        var score = (double)found / required.Count;
        var detail = missing.Count == 0
            ? $"All {required.Count} required substrings found"
            : $"Missing {missing.Count}/{required.Count}: {string.Join(", ", missing)}";

        return new AccuracyCheck
        {
            Name = "required_substrings",
            Score = score,
            Weight = 2.0,
            Detail = detail,
        };
    }

    private static AccuracyCheck ScoreForbiddenSubstrings(string output, IReadOnlyList<string> forbidden)
    {
        var violations = new List<string>();

        foreach (var substring in forbidden)
        {
            if (output.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(substring);
            }
        }

        var score = violations.Count == 0
            ? 1.0
            : Math.Max(0, 1.0 - ((double)violations.Count / forbidden.Count));

        var detail = violations.Count == 0
            ? "No forbidden substrings found"
            : $"Found {violations.Count} forbidden: {string.Join(", ", violations)}";

        return new AccuracyCheck
        {
            Name = "forbidden_substrings",
            Score = score,
            Weight = 1.5,
            Detail = detail,
        };
    }

    private static AccuracyCheck ScoreRequiredStructure(string output, IReadOnlyList<string> structure)
    {
        var found = 0;
        var missing = new List<string>();

        foreach (var element in structure)
        {
            if (output.Contains(element, StringComparison.Ordinal))
            {
                found++;
            }
            else
            {
                missing.Add(element);
            }
        }

        var score = (double)found / structure.Count;
        var detail = missing.Count == 0
            ? $"All {structure.Count} structural elements present"
            : $"Missing {missing.Count}/{structure.Count}: {string.Join(", ", missing.Select(s => $"'{s}'"))}";

        return new AccuracyCheck
        {
            Name = "required_structure",
            Score = score,
            Weight = 1.5,
            Detail = detail,
        };
    }

    /// <summary>
    /// Bigram similarity between output and reference. Fast, deterministic,
    /// captures structural similarity without exact-match fragility.
    /// </summary>
    private static AccuracyCheck ScoreReferenceSimilarity(string output, string reference)
    {
        var score = BigramSimilarity(output.ToLowerInvariant(), reference.ToLowerInvariant());
        var detail = $"Bigram similarity to reference: {score:F2}";

        return new AccuracyCheck
        {
            Name = "reference_similarity",
            Score = score,
            Weight = 1.0,
            Detail = detail,
        };
    }

    /// <summary>
    /// Dice's coefficient using character bigrams.
    /// Returns 0.0 for no overlap, 1.0 for identical strings.
    /// </summary>
    internal static double BigramSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (a == b)
        {
            return 1.0;
        }

        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);

        if (bigramsA.Count == 0 || bigramsB.Count == 0)
        {
            return 0;
        }

        var intersection = 0;
        var countB = new Dictionary<string, int>(bigramsB.Count);

        foreach (var bigram in bigramsB)
        {
            countB.TryGetValue(bigram, out var existing);
            countB[bigram] = existing + 1;
        }

        foreach (var bigram in bigramsA)
        {
            if (countB.TryGetValue(bigram, out var remaining) && remaining > 0)
            {
                intersection++;
                countB[bigram] = remaining - 1;
            }
        }

        return 2.0 * intersection / (bigramsA.Count + bigramsB.Count);
    }

    private static List<string> GetBigrams(string text)
    {
        var bigrams = new List<string>(Math.Max(0, text.Length - 1));

        for (var i = 0; i < text.Length - 1; i++)
        {
            bigrams.Add(text.Substring(i, 2));
        }

        return bigrams;
    }
}
