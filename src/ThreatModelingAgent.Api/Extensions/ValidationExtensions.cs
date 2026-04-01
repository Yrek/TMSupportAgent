using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ThreatModelingAgent.Api.Extensions;

public static class ValidationExtensions
{
    public static ModelStateDictionary ToModelStateDictionary(this ValidationResult result)
    {
        var dict = new ModelStateDictionary();
        foreach (var error in result.Errors)
            dict.AddModelError(error.PropertyName, error.ErrorMessage);
        return dict;
    }
}
