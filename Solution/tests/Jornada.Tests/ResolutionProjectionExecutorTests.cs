using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests;

public sealed class ResolutionProjectionExecutorTests
{
    [Test]
    public void Project_ExecutesHomologatedCoreAndDynamicAlgorithms()
    {
        var result = ResolutionProjectionExecutor.Project(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan,
            new ResolutionSourceValue[]
            {
                new(PersonResolutionAttributeCatalog.FullName, "José da Silva"),
                new(PersonResolutionAttributeCatalog.BirthDate, "1980-07-09"),
                new(PersonResolutionAttributeCatalog.ContactPhone, "+55 (11) 99999-0001"),
                new(PersonResolutionAttributeCatalog.ContactEmail, " Pessoa@EXAMPLE.Test "),
                new(PersonResolutionAttributeCatalog.SocialName, "Maria das Flores")
            });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain(new Jornada.Contracts.BlockingProjectionKey("name_full", "JOSE DA SILVA")));
            Assert.That(result, Does.Contain(new Jornada.Contracts.BlockingProjectionKey("birth_year", "1980")));
            Assert.That(result, Does.Contain(new Jornada.Contracts.BlockingProjectionKey("telefone_contato__canonical", "5511999990001")));
            Assert.That(result, Does.Contain(new Jornada.Contracts.BlockingProjectionKey("email_contato__canonical", "pessoa@example.test")));
            Assert.That(result.Any(static key => key.Feature == "nome_social__normalized"), Is.True);
        });
    }

    [Test]
    public void Project_PreservesSeveralValuesOfMultiValuedAttribute()
    {
        var result = ResolutionProjectionExecutor.Project(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan,
            new ResolutionSourceValue[]
            {
                new(PersonResolutionAttributeCatalog.ContactEmail, "a@example.test"),
                new(PersonResolutionAttributeCatalog.ContactEmail, "b@example.test")
            });

        Assert.That(result.Where(static key => key.Feature == "email_contato__canonical").Select(static key => key.Value),
            Is.EquivalentTo(new[] { "a@example.test", "b@example.test" }));
    }

    [Test]
    public void Project_DoesNotInferConfidentialAddressOrUnknownPersonField()
    {
        var result = ResolutionProjectionExecutor.Project(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan,
            new ResolutionSourceValue[]
            {
                new(PersonResolutionAttributeCatalog.ConfidentialShelterAddress, "Rua Sigilosa, 10"),
                new("nome_parecido_com_nome", "Pessoa Qualquer")
            });

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Project_InvalidLegacyContactValueFailsClosedForThatValue()
    {
        var result = ResolutionProjectionExecutor.Project(
            BlockingCandidateFeatureCatalog.CurrentResolutionProjectionPlan,
            new[] { new ResolutionSourceValue(PersonResolutionAttributeCatalog.ContactEmail, "sem-arroba") });

        Assert.That(result, Is.Empty);
    }
}
