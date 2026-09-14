using Jornada.Processor.Worker;
using Xunit;

namespace Jornada.Tests.Unit;

public sealed class PersonIdentifierResolutionPolicyTests
{
    [Fact]
    public void Cpf_Wins_External_Hierarchy()
    {
        var cpfUuid = Guid.NewGuid();
        var cnsUuid = Guid.NewGuid();

        var decision = PersonIdentifierResolutionPolicy.Decide([
            new ResolvedIdentifierCandidate("CNS", cnsUuid, cnsUuid, true, 70, false),
            new ResolvedIdentifierCandidate("CPF", cpfUuid, cpfUuid, true, 100, false)
        ]);

        Assert.Equal(cpfUuid, decision.PessoaUuid);
        Assert.Equal("CPF", decision.TipoIdentificadorDeterminante);
        Assert.False(decision.InconsistenciaRetroalimentacao);
    }

    [Fact]
    public void Jornada_Feedback_Does_Not_Override_Cpf()
    {
        var cpfUuid = Guid.NewGuid();
        var returnedUuid = Guid.NewGuid();

        var decision = PersonIdentifierResolutionPolicy.Decide([
            new ResolvedIdentifierCandidate("CPF", cpfUuid, cpfUuid, true, 100, false),
            new ResolvedIdentifierCandidate("UUID_JORNADA", returnedUuid, returnedUuid, true, null, true)
        ]);

        Assert.Equal(cpfUuid, decision.PessoaUuid);
        Assert.Equal("CPF", decision.TipoIdentificadorDeterminante);
        Assert.True(decision.InconsistenciaRetroalimentacao);
        Assert.Equal(returnedUuid, decision.UuidJornadaRecebido);
        Assert.Equal("INCONSISTENCIA_RETROALIMENTACAO", decision.Motivo);
    }

    [Fact]
    public void Redirected_Jornada_Feedback_Is_Consistent_With_Cpf()
    {
        var cpfUuid = Guid.NewGuid();
        var oldPublishedUuid = Guid.NewGuid();

        var decision = PersonIdentifierResolutionPolicy.Decide([
            new ResolvedIdentifierCandidate("CPF", cpfUuid, cpfUuid, true, 100, false),
            new ResolvedIdentifierCandidate("UUID_JORNADA", oldPublishedUuid, cpfUuid, true, null, true)
        ]);

        Assert.Equal(cpfUuid, decision.PessoaUuid);
        Assert.False(decision.InconsistenciaRetroalimentacao);
        Assert.Equal(oldPublishedUuid, decision.UuidJornadaRecebido);
    }

    [Fact]
    public void Jornada_Feedback_Alone_Provides_Internal_Continuity()
    {
        var publishedUuid = Guid.NewGuid();
        var canonicalUuid = Guid.NewGuid();

        var decision = PersonIdentifierResolutionPolicy.Decide([
            new ResolvedIdentifierCandidate("UUID_JORNADA", publishedUuid, canonicalUuid, true, null, true)
        ]);

        Assert.Equal(canonicalUuid, decision.PessoaUuid);
        Assert.Equal("CONTINUIDADE_JORNADA", decision.Estado);
        Assert.Equal("UUID_JORNADA", decision.TipoIdentificadorDeterminante);
        Assert.False(decision.InconsistenciaRetroalimentacao);
    }

    [Fact]
    public void No_Deterministic_Identifier_Waits_For_Linkage()
    {
        var decision = PersonIdentifierResolutionPolicy.Decide([]);

        Assert.Null(decision.PessoaUuid);
        Assert.Equal("SEM_IDENTIFICADOR_DETERMINISTICO", decision.Estado);
        Assert.Equal("AGUARDA_LINKAGE", decision.Motivo);
    }
}
