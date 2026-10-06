using System.Reflection;
using System.Text.Json;
using MonitorCloud.Application.Common;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.ArchitectureTests;

/// <summary>06 section 6: every error code is listed in <c>ErrorCodes</c> and translated in both portal dictionaries.</summary>
public sealed class ErrorCodeTests
{
    private static IEnumerable<string> DeclaredCodes() =>
        Assemblies.Domain.GetTypes().Concat(Assemblies.Application.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(Error))
            .Select(f => ((Error)f.GetValue(null)!).Code)
            .Distinct(StringComparer.Ordinal);

    private static Dictionary<string, string> Dictionary(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepositoryPaths.Root, "portal", "src", "app", "core", "i18n", $"{language}.json")))!;

    [Fact]
    public void Every_error_defined_in_code_is_listed_in_ErrorCodes()
    {
        var missing = DeclaredCodes().Where(c => !ErrorCodes.All.Contains(c)).ToList();

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Codes_are_upper_snake_case() =>
        ErrorCodes.All.ShouldAllBe(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-Z]+(_[A-Z]+)*$"));

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void Every_code_is_translated_in_the_portal(string language)
    {
        var dictionary = Dictionary(language);

        var missing = ErrorCodes.All.Where(c => !dictionary.ContainsKey($"error.{c}")).ToList();

        missing.ShouldBeEmpty();
    }

    [Fact]
    public void Both_dictionaries_have_the_same_keys()
    {
        var en = Dictionary("en").Keys.ToHashSet(StringComparer.Ordinal);
        var ar = Dictionary("ar").Keys.ToHashSet(StringComparer.Ordinal);

        en.Except(ar).ShouldBeEmpty("missing in ar.json");
        ar.Except(en).ShouldBeEmpty("missing in en.json");
    }
}
