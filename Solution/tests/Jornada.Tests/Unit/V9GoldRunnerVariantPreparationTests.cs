using System.Text.Json;
using System.Text.Json.Nodes;
using Jornada.Linkage.Parameters.Worker;

namespace Jornada.Tests.Unit;

[TestFixture, Category("Unit")]
public sealed class V9GoldRunnerVariantPreparationTests
{
    private static readonly string Winner = new('a', 64);
    private static readonly string Selection = new('b', 64);

    private static JsonObject ConditionalReceipt() => new()
    {
        ["selectionEnvelopeSha256"] = Selection,
        ["bestConditionalSnapshotSha256"] = Winner,
        ["selectedApprovedSnapshotSha256"] = null,
        ["comparedConfigurations"] = 1,
        ["status"] = "CONDITIONAL_ONLY_IID_NOT_INDEPENDENTLY_VALIDATED",
        ["testUsed"] = false,
        ["refitted"] = false,
        ["certified"] = false,
        ["activated"] = false,
        ["identityClusterIidIndependentlyValidated"] = false,
        ["measured"] = new JsonArray(new JsonObject
        {
            ["SnapshotSha256"] = Winner,
            ["Refitted"] = false,
            ["Metric"] = new JsonObject
            {
                ["SyntheticGoldOracleGrouping"] = false,
                ["BonferroniComparedConfigurations"] = 1,
                ["ConditionalClusterCpFdrUpper"] = 0.009
            }
        })
    };

    private static string Require(JsonObject node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return V9GoldRunnerVariantPreparation.RequireConditionalWinner(doc.RootElement);
    }

    private static void Reject(JsonObject node) =>
        Assert.Throws<InvalidDataException>((Action)(() => Require(node)));

    [Test]
    public void Requires_exact_conditional_validation_receipt_without_activation()
    {
        Assert.That(Require(ConditionalReceipt()), Is.EqualTo(Winner));
    }

    [TestCase("testUsed")]
    [TestCase("refitted")]
    [TestCase("certified")]
    [TestCase("activated")]
    [TestCase("identityClusterIidIndependentlyValidated")]
    public void Rejects_test_refit_certification_or_activation_claims(string field)
    {
        var receipt = ConditionalReceipt();
        receipt[field] = true;
        Reject(receipt);
        receipt = ConditionalReceipt();
        receipt.Remove(field);
        Reject(receipt);
    }

    [Test]
    public void Rejects_fake_approved_or_missing_selection_identity()
    {
        var receipt = ConditionalReceipt();
        receipt["selectedApprovedSnapshotSha256"] = Winner;
        Reject(receipt);
        receipt = ConditionalReceipt();
        receipt["selectionEnvelopeSha256"] = "not-a-sha";
        Reject(receipt);
        receipt = ConditionalReceipt();
        receipt["status"] = "NO_FINALIST_PASSES_DECLARED_CONDITIONAL_GATES";
        Reject(receipt);
        receipt = ConditionalReceipt();
        receipt["bestConditionalSnapshotSha256"] = new string('c', 64);
        Reject(receipt);
    }

    [Test]
    public void Rejects_duplicate_unmeasured_or_oracle_finalists()
    {
        var receipt = ConditionalReceipt();
        var rows = (JsonArray)receipt["measured"]!;
        rows.Add(rows[0]!.DeepClone());
        receipt["comparedConfigurations"] = 2;
        ((JsonObject)rows[0]!["Metric"]!)["BonferroniComparedConfigurations"] = 2;
        ((JsonObject)rows[1]!["Metric"]!)["BonferroniComparedConfigurations"] = 2;
        Reject(receipt);
        receipt = ConditionalReceipt();
        ((JsonObject)receipt["measured"]![0]!["Metric"]!)["SyntheticGoldOracleGrouping"] = true;
        Reject(receipt);
        receipt = ConditionalReceipt();
        ((JsonObject)receipt["measured"]![0]!["Metric"]!)["ConditionalClusterCpFdrUpper"] = null;
        Reject(receipt);
        receipt = ConditionalReceipt();
        ((JsonObject)receipt["measured"]![0]!)["Refitted"] = true;
        Reject(receipt);
        receipt = ConditionalReceipt();
        receipt["comparedConfigurations"] = 2;
        Reject(receipt);
    }
}
