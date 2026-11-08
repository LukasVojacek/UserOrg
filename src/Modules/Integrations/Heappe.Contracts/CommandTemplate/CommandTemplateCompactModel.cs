namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateCompactModel
(
    long Id,
    string Name,
    string Description,
    string ExtendedAllocationCommand,
    bool IsGeneric,
    bool IsEnabled,
    long? CreatedFromGenericTemplateId,
    IEnumerable<CommandTemplateParameterCompactModel> TemplateParameters
)
{

    public static CommandTemplateCompactModel FromCommandTemplateModel(CommandTemplateModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new CommandTemplateCompactModel(
            model.Id,
            model.Name,
            model.Description ?? string.Empty,
            model.ExtendedAllocationCommand ?? string.Empty,
            model.IsGeneric,
            model.IsEnabled,
            model.CreatedFromGenericTemplateId,
            model.TemplateParameters.Select(p => CommandTemplateParameterCompactModel.FromCommandTemplateParameterModel(p))
        );
    }

}