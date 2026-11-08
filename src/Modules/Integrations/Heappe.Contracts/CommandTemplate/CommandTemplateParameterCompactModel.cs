namespace Heappe.Contracts.CommandTemplate;

public sealed record CommandTemplateParameterCompactModel
(
       string Identifier,
       string Description
)
{

    public static CommandTemplateParameterCompactModel FromCommandTemplateParameterModel(CommandTemplateParameterModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new CommandTemplateParameterCompactModel
        (
            model.Identifier,
            model.Description ?? string.Empty
        );
    }
}