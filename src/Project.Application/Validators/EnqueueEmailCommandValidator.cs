using FluentValidation;
using Project.Application.Commands.SendEmail;

namespace Project.Application.Validators;

public class EnqueueEmailCommandValidator : AbstractValidator<EnqueueEmailCommand>
{
    public EnqueueEmailCommandValidator()
    {
        RuleFor(x => x.To).NotEmpty().EmailAddress();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(10000);
    }
}
