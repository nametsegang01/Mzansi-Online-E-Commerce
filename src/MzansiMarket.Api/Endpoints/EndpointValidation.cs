using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MzansiMarket.Api.Endpoints;

internal static class EndpointValidation
{
    public static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);

        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => new { Member = member, Message = result.ErrorMessage ?? "The value is invalid." }))
            .GroupBy(item => item.Member, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Message).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string[]> FromIdentity(IdentityResult result) =>
        result.Errors
            .GroupBy(error => IdentityField(error.Code), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static string IdentityField(string code) =>
        code.Contains("Password", StringComparison.OrdinalIgnoreCase) ? "Password" : "Email";
}
